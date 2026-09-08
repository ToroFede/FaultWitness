using System.Text.Json;
using FaultWitness.Core;
using FaultWitness.Platform.Windows;
using FaultWitness.Rules;
using FaultWitness.Storage;

namespace FaultWitness.App;

public sealed record UserSettings(string Language = "system", AppTheme Theme = AppTheme.System,
    AnalysisPeriod Period = AnalysisPeriod.Week, int RetentionDays = 30);

public interface IAppServices
{
    Task<ScanResult> AnalyzeAsync(DateTimeOffset from, DateTimeOffset endUtc, IProgress<string> progress, CancellationToken token);
    Task<ImportResult> ImportAsync(string path, CancellationToken token);
    Task<ScanResult> AnalyzeImportedAsync(EventBatch batch, CancellationToken token);
    Task<IReadOnlyList<DiagnosticReadinessItem>> ReadinessAsync(CancellationToken token);
    Task<IReadOnlyDictionary<string, string>> InventoryAsync(CancellationToken token);
    Task SaveAsync(ScanResult result, int retentionDays, ScanHistoryMetadata metadata, CancellationToken token);
    Task<IReadOnlyList<StoredScan>> LoadHistoryAsync(CancellationToken token);
    Task ClearAsync(CancellationToken token);
    UserSettings LoadSettings();
    void SaveSettings(UserSettings settings);
}

public sealed class DesktopServices : IAppServices
{
    private readonly WindowsDiagnosticsProvider provider = new();
    private readonly string root;
    public DesktopServices(string? dataDirectory = null) => root = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaultWitness");
    public async Task<ScanResult> AnalyzeAsync(DateTimeOffset from, DateTimeOffset endUtc, IProgress<string> progress, CancellationToken token)
    {
        progress.Report("ReadingSources");
        var batch = await provider.ReadAsync(from, endUtc, token).ConfigureAwait(false);
        progress.Report("EvaluatingEvidence");
        return await Task.Run(() => new IncidentAnalyzer(RuleCatalog.CreateDefault()).Analyze(batch, from, endUtc, token), token).ConfigureAwait(false);
    }
    public Task<ImportResult> ImportAsync(string path, CancellationToken token) => WindowsImportService.ImportAsync([path], token);
    public Task<ScanResult> AnalyzeImportedAsync(EventBatch batch, CancellationToken token) => Task.Run(() =>
    {
        var from = batch.Events.Count > 0 ? batch.Events.Min(item => item.TimestampUtc) : DateTimeOffset.UtcNow;
        var to = batch.Events.Count > 0 ? batch.Events.Max(item => item.TimestampUtc) : from;
        return new IncidentAnalyzer(RuleCatalog.CreateDefault()).Analyze(batch, from, to, token);
    }, token);
    public async Task<IReadOnlyList<DiagnosticReadinessItem>> ReadinessAsync(CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var batch = await provider.ReadAsync(now.AddMinutes(-1), now, token).ConfigureAwait(false);
        return batch.Coverage.Select(item => new DiagnosticReadinessItem(PresentationPolicy.SourceKey(item), item.State, item.DetailKey)).ToArray();
    }
    public async Task<IReadOnlyDictionary<string, string>> InventoryAsync(CancellationToken token)
    {
        var inventory = await provider.GetInventoryAsync(token).ConfigureAwait(false);
        var windows = await WindowsProductInformation.ReadAsync(token).ConfigureAwait(false);
        return windows.ApplyTo(inventory);
    }
    public async Task SaveAsync(ScanResult result, int retentionDays, ScanHistoryMetadata metadata, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var store = new FaultWitnessStore(Path.Combine(root, "faultwitness.db"));
        await store.InitializeAsync(token).ConfigureAwait(false);
        if (retentionDays > 0) await store.SaveScanAsync(result, RuleCatalog.DatabaseVersion, metadata, token).ConfigureAwait(false);
        if (retentionDays > 0) await store.PruneAsync(DateTimeOffset.UtcNow.AddDays(-retentionDays), token).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<StoredScan>> LoadHistoryAsync(CancellationToken token)
    {
        Directory.CreateDirectory(root); var store = new FaultWitnessStore(Path.Combine(root, "faultwitness.db"));
        await store.InitializeAsync(token).ConfigureAwait(false); return await store.LoadScansAsync(100, token).ConfigureAwait(false);
    }
    public async Task ClearAsync(CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var store = new FaultWitnessStore(Path.Combine(root, "faultwitness.db"));
        await store.InitializeAsync(token).ConfigureAwait(false);
        await store.ClearAsync(token).ConfigureAwait(false);
    }
    public UserSettings LoadSettings()
    {
        try
        {
            var path = Path.Combine(root, "settings.json");
            if (!File.Exists(path)) return new();
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new();
            return settings with { RetentionDays = Math.Clamp(settings.RetentionDays, 0, 365),
                Theme = Enum.IsDefined(settings.Theme) ? settings.Theme : AppTheme.System,
                Period = Enum.IsDefined(settings.Period) ? settings.Period : AnalysisPeriod.Week };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void SaveSettings(UserSettings settings)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
        File.Move(temporary, path, true);
    }
}
