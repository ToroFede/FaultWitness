using System.Text.Json;
using FaultWitness.Core;
using FaultWitness.Platform;
using FaultWitness.Platform.Windows;
using FaultWitness.Rules;
using FaultWitness.Storage;

namespace FaultWitness.App;

public sealed record UserSettings(string Language = "system", AppTheme Theme = AppTheme.System,
    AnalysisPeriod Period = AnalysisPeriod.Week, int RetentionDays = 30);

public interface IAppServices
{
    CaptureWorkflow? CreateCaptureWorkflow() => null;
    Task<ScanResult> AnalyzeAsync(DateTimeOffset from, DateTimeOffset endUtc, IProgress<string> progress, CancellationToken token);
    Task<ImportResult> ImportAsync(string path, CancellationToken token);
    Task<ScanResult> AnalyzeImportedAsync(EventBatch batch, CancellationToken token);
    Task<IReadOnlyList<DiagnosticReadinessItem>> ReadinessAsync(CancellationToken token);
    Task<IReadOnlyDictionary<string, string>> InventoryAsync(CancellationToken token);
    async Task<SystemInventorySnapshot> SystemInventoryAsync(CancellationToken token)
    {
        var values = await InventoryAsync(token).ConfigureAwait(false);
        var fields = values.Select(pair => new InventoryField(pair.Key, pair.Value)).ToArray();
        return new SystemInventorySnapshot([new InventoryGroup(WindowsSystemInventory.InventoryGroupOperatingSystem,
            [new InventoryDevice("legacy", fields)])]);
    }
    Task SaveAsync(ScanResult result, int retentionDays, ScanHistoryMetadata metadata, CancellationToken token);
    Task<IReadOnlyList<StoredScan>> LoadHistoryAsync(CancellationToken token);
    Task ClearAsync(CancellationToken token);
    UserSettings LoadSettings();
    void SaveSettings(UserSettings settings);
}

public sealed class DesktopServices : IAppServices
{
    public CaptureWorkflow CreateCaptureWorkflow() => new(new ElevatedCaptureClient(), new CaptureJournalStore(Path.Combine(root, "faultwitness.db")));
    private readonly WindowsDiagnosticsProvider provider = new();
    private readonly string root;
    public DesktopServices(string? dataDirectory = null) => root = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaultWitness");
    public async Task<ScanResult> AnalyzeAsync(DateTimeOffset from, DateTimeOffset endUtc, IProgress<string> progress, CancellationToken token)
    {
        progress.Report("ReadingSources");
        var batch = await provider.ReadAsync(from, endUtc, token).ConfigureAwait(false);
        progress.Report("EvaluatingEvidence");
        var scan = await Task.Run(() => new IncidentAnalyzer(RuleCatalog.CreateDefault()).Analyze(batch, from, endUtc, token), token).ConfigureAwait(false);
        progress.Report("ReadingChangeHistory");
        IReadOnlyList<RetainedOccurrence> retained = [];
        var historyAvailable = true;
        var settings = LoadSettings();
        if (settings.RetentionDays > 0 && File.Exists(Path.Combine(root, "faultwitness.db")))
        {
            try
            {
                var store = new FaultWitnessStore(Path.Combine(root, "faultwitness.db"));
                await store.InitializeAsync(token).ConfigureAwait(false);
                retained = await store.LoadRetainedOccurrencesAsync(DateTimeOffset.UtcNow.AddDays(-settings.RetentionDays),
                    RuleCatalog.DatabaseVersion, token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { historyAvailable = false; }
        }
        return await ChangeHistoryEnricher.EnrichAsync(scan, retained, new WindowsChangeHistoryProvider(), token, historyAvailable).ConfigureAwait(false);
    }
    public Task<ImportResult> ImportAsync(string path, CancellationToken token) => WindowsImportService.ImportAsync([path], token);
    public Task<ScanResult> AnalyzeImportedAsync(EventBatch batch, CancellationToken token) => Task.Run(() =>
    {
        var from = batch.Events.Count > 0 ? batch.Events.Min(item => item.TimestampUtc) : DateTimeOffset.UtcNow;
        var to = batch.Events.Count > 0 ? batch.Events.Max(item => item.TimestampUtc) : from;
        return new IncidentAnalyzer(RuleCatalog.CreateDefault()).Analyze(batch, from, to, token);
    }, token);
    public async Task<IReadOnlyList<DiagnosticReadinessItem>> ReadinessAsync(CancellationToken token)
        => await new WindowsDiagnosticReadiness().GetAsync(token).ConfigureAwait(false);
    public Task<SystemInventorySnapshot> SystemInventoryAsync(CancellationToken token) => new WindowsSystemInventory().GetAsync(token);
    public async Task<IReadOnlyDictionary<string, string>> InventoryAsync(CancellationToken token)
    {
        return (await SystemInventoryAsync(token).ConfigureAwait(false)).ToSummary();
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
