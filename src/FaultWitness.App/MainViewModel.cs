using System.Diagnostics;
using FaultWitness.Core;
using FaultWitness.Localization;
using FaultWitness.Storage;

namespace FaultWitness.App;

public enum ViewChange { Page, State, Results, Filter, Language, Theme, HistorySelection }

public sealed class MainViewModel : IDisposable
{
    private readonly IAppServices services;
    private CancellationTokenSource? operation;
    private AppPage operationPage;
    private AppPage statusPage;
    private bool inventoryAttempted;
    private Task? inventoryRefreshTask;
    private IReadOnlyList<IncidentRow> rows = [];
    public MainViewModel(IAppServices services, LocalizationService? text = null)
    {
        this.services = services;
        Text = text ?? new LocalizationService();
        Settings = services.LoadSettings();
        Text.SetCulture(Settings.Language);
        Period = Settings.Period;
    }
    public event Action<ViewChange>? Changed;
    public LocalizationService Text { get; }
    public UserSettings Settings { get; private set; }
    public AppPage Page { get; private set; } = AppPage.Home;
    public AnalysisMode AnalysisMode { get; private set; } = AnalysisMode.Recent;
    public AnalysisPeriod Period { get; set; } = AnalysisPeriod.Week;
    public DateTimeOffset CustomFrom { get; set; } = DateTimeOffset.Now.AddDays(-7);
    public DateTimeOffset CustomTo { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset AroundTime { get; set; } = DateTimeOffset.Now.AddMinutes(-10);
    public int WindowMinutes { get; set; } = 5;
    public bool IsBusy { get; private set; }
    public bool HasAnalysis { get; private set; }
    public bool IsImported { get; private set; }
    public bool IsAround { get; private set; }
    public string StatusKey { get; private set; } = "Ready";
    public string TechnicalError { get; private set; } = string.Empty;
    public double LastDurationSeconds { get; private set; }
    public DateTimeOffset? LastRequestedFromUtc { get; private set; }
    public DateTimeOffset? LastRequestedToUtc { get; private set; }
    public ScanResult Result { get; private set; } = ScanResult.Empty(DateTimeOffset.UtcNow);
    public IncidentRow? Selected { get; private set; }
    public IncidentFilter Filter { get; private set; } = new();
    public IReadOnlyList<IncidentRow> AllRows => rows;
    public IReadOnlyList<IncidentRow> FilteredRows { get; private set; } = [];
    public IReadOnlyList<IncidentRow> RecentSignificant => rows.Where(row => row.Priority != AttentionLevel.Background)
        .OrderBy(row => row.Priority).ThenByDescending(row => row.Incident.StartTimeUtc)
        .DistinctBy(row => row.Incident.Signature).Take(6).ToArray();
    public int AttentionCount => rows.Count(row => row.Priority == AttentionLevel.Attention);
    public int KnowingCount => rows.Count(row => row.Priority == AttentionLevel.Knowing);
    public int BackgroundCount => rows.Count(row => row.Priority == AttentionLevel.Background);
    public List<ImportRow> Imports { get; } = [];
    public IReadOnlyList<DiagnosticReadinessItem> Readiness { get; private set; } = [];
    public IReadOnlyDictionary<string, string> Inventory { get; private set; } = new Dictionary<string, string>();
    public SystemInventorySnapshot SystemInventory { get; private set; } = new([]);
    public IReadOnlyList<HistoryRow> History { get; private set; } = [];
    public HistoryRow? SelectedHistory { get; private set; }
    public string StatusText => StatusKey == "AnalysisComplete" ? Text.Format(StatusKey, AttentionCount, KnowingCount) : Text.Get(StatusKey);
    public bool HasVisibleStatus => StatusKey != "Ready" && (IsBusy || statusPage == Page);
    public string Origin => Text.Get(IsImported ? "ImportedData" : "LocalSystem");

    public void Navigate(AppPage page)
    {
        if (Page != page && !IsBusy) { StatusKey = "Ready"; TechnicalError = string.Empty; }
        Page = page;
        Changed?.Invoke(ViewChange.Page);
        if (page == AppPage.System && !inventoryAttempted && !IsBusy) _ = RefreshInventoryAsync();
    }
    public void OpenAnalyze(AnalysisMode mode) { AnalysisMode = mode; Navigate(AppPage.Analyze); }
    public void SelectHistory(HistoryRow row)
    {
        if (ReferenceEquals(SelectedHistory, row)) return;
        SelectedHistory = row;
        Changed?.Invoke(ViewChange.HistorySelection);
    }
    public void Select(IncidentRow row) { Selected = row; Navigate(AppPage.Detail); }
    public void SetFilter(IncidentFilter filter)
    {
        Filter = filter;
        FilteredRows = rows.Where(filter.Matches).ToArray();
        Changed?.Invoke(ViewChange.Filter);
    }
    public void ViewOccurrences()
    {
        if (Selected is null) return;
        var pattern = Result.Patterns.FirstOrDefault(item => item.IncidentIds.Contains(Selected.Incident.Id));
        SetFilter(new IncidentFilter(Occurrences: pattern?.IncidentIds.ToHashSet() ?? [Selected.Incident.Id]));
        Navigate(AppPage.Incidents);
    }
    public void ShowPriority(AttentionLevel? priority) { SetFilter(new(Priority: priority)); Navigate(AppPage.Incidents); }
    public void SetResult(ScanResult result, bool imported = false, bool around = false)
    {
        Result = result;
        IsImported = imported;
        IsAround = around;
        HasAnalysis = true;
        Selected = null;
        RebuildRows();
        SetFilter(new());
        Changed?.Invoke(ViewChange.Results);
    }
    private void RebuildRows()
    {
        var selectedId = Selected?.Incident.Id;
        var counts = Result.Patterns.SelectMany(pattern => pattern.IncidentIds.Select(id => (id, pattern.IncidentIds.Count)))
            .GroupBy(item => item.id).ToDictionary(group => group.Key, group => group.Max(item => item.Count));
        var reportCounts = Result.Incidents.Select(item => (Key: PresentationPolicy.ReportReferenceKey(item), item.Id))
            .Where(item => item.Key is not null).GroupBy(item => item.Key!).ToDictionary(group => group.Key, group => group.Select(item => item.Id).Distinct().Count());
        rows = Result.Incidents.OrderByDescending(item => item.StartTimeUtc).Select(item => new IncidentRow(item, Text,
            counts.GetValueOrDefault(item.Id, 1), reportCounts.GetValueOrDefault(PresentationPolicy.ReportReferenceKey(item) ?? "", 1))).ToArray();
        Selected = rows.FirstOrDefault(item => item.Incident.Id == selectedId);
        FilteredRows = rows.Where(Filter.Matches).ToArray();
    }
    public async Task AnalyzeAsync(bool around = false)
    {
        if (IsBusy) return;
        var now = DateTimeOffset.UtcNow;
        var to = around ? AroundTime.ToUniversalTime().AddMinutes(WindowMinutes) : Period == AnalysisPeriod.Custom ? CustomTo.ToUniversalTime() : now;
        var from = around ? AroundTime.ToUniversalTime().AddMinutes(-WindowMinutes) : Period switch
        {
            AnalysisPeriod.Day => now.AddDays(-1), AnalysisPeriod.Month => now.AddDays(-30),
            AnalysisPeriod.Custom => CustomFrom.ToUniversalTime(), _ => now.AddDays(-7)
        };
        if (to < from || from > now || to - from > TimeSpan.FromDays(90) || WindowMinutes is < 1 or > 60)
        { Notify("InvalidTimeRange"); return; }
        using var cancellation = BeginOperation("ReadingSources");
        var watch = Stopwatch.StartNew();
        try
        {
            var progress = new Progress<string>(key => { if (IsBusy && operation == cancellation) Notify(key); });
            var result = await services.AnalyzeAsync(from, to, progress, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            LastRequestedFromUtc = from; LastRequestedToUtc = to;
            SetResult(result, around: around);
            Notify("SavingHistory");
            try { await services.SaveAsync(result, Settings.RetentionDays, HistoryMetadata(around ? "around" : "recent", from, to, watch.Elapsed), cancellation.Token).ConfigureAwait(true); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { TechnicalError = exception.GetType().Name; Notify("HistoryError"); }
            if (StatusKey != "HistoryError") Notify("AnalysisComplete");
            statusPage = AppPage.Home;
            Navigate(AppPage.Home);
        }
        catch (OperationCanceledException) { Notify("AnalysisCancelled"); }
        catch (Exception exception) { Fail("AnalysisError", exception); }
        finally { LastDurationSeconds = watch.Elapsed.TotalSeconds; EndOperation(); if (Page == AppPage.Home) Changed?.Invoke(ViewChange.Page); }
    }
    public void Cancel() { operation?.Cancel(); if (IsBusy) Notify("Cancelling"); }
    public void AddImports(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        foreach (var path in paths.Take(21))
        {
            if (Imports.Any(row => string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            if (Imports.Count >= 20) { Notify("ImportLimit"); break; }
            var supported = new[] { ".evtx", ".wer", ".zip" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
            Imports.Add(new(path, supported ? "ImportReady" : "ImportUnsupported"));
        }
        Changed?.Invoke(ViewChange.Page);
    }
    public async Task AnalyzeImportsAsync()
    {
        if (IsBusy || Imports.Count == 0) return;
        using var cancellation = BeginOperation("ReadingSources");
        try
        {
            var events = new List<NormalizedEvent>();
            var coverage = new List<SourceCoverage>();
            for (var index = 0; index < Imports.Count; index++)
            {
                var row = Imports[index];
                if (row.StatusKey == "ImportUnsupported") continue;
                var imported = await services.ImportAsync(row.Path, cancellation.Token).ConfigureAwait(true);
                if (imported.Errors.Count != 0 || events.Count + imported.Batch.Events.Count > 20_000)
                {
                    Imports[index] = row with { StatusKey = "ImportRejected" };
                    continue;
                }
                Imports[index] = row with { StatusKey = "ImportAccepted" };
                events.AddRange(imported.Batch.Events);
                coverage.AddRange(imported.Batch.Coverage);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (!Imports.Any(row => row.StatusKey == "ImportAccepted")) { Notify("ImportRejected"); return; }
            var result = await services.AnalyzeImportedAsync(new EventBatch(events, coverage), cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            SetResult(result, imported: true);
            LastRequestedFromUtc = result.StartedUtc; LastRequestedToUtc = result.FinishedUtc;
            try { await services.SaveAsync(result, Settings.RetentionDays, HistoryMetadata("imported", result.StartedUtc, result.FinishedUtc, TimeSpan.Zero), cancellation.Token).ConfigureAwait(true); }
            catch (Exception exception) when (exception is not OperationCanceledException) { TechnicalError = exception.GetType().Name; Notify("HistoryError"); }
            if (StatusKey != "HistoryError") Notify("ImportedAnalysis");
            statusPage = AppPage.Home;
            Navigate(AppPage.Home);
        }
        catch (OperationCanceledException) { Notify("AnalysisCancelled"); }
        catch (Exception exception) { Fail("ImportRejected", exception); }
        finally { EndOperation(); Changed?.Invoke(ViewChange.Page); }
    }
    public async Task RefreshReadinessAsync()
    {
        if (IsBusy) return;
        using var cancellation = BeginOperation("CheckingSources");
        try { Readiness = await services.ReadinessAsync(cancellation.Token).ConfigureAwait(true); Notify("ReadinessComplete"); }
        catch (OperationCanceledException) { Notify("AnalysisCancelled"); }
        catch (Exception exception) { Fail("SourceUnavailable", exception); }
        finally { EndOperation(); Changed?.Invoke(ViewChange.Page); }
    }
    public Task RefreshInventoryAsync()
    {
        if (IsBusy) return inventoryRefreshTask ?? Task.CompletedTask;
        inventoryAttempted = true;
        return inventoryRefreshTask = RefreshInventoryCoreAsync();
    }
    private async Task RefreshInventoryCoreAsync()
    {
        using var cancellation = BeginOperation("ReadingSources");
        try
        {
            SystemInventory = await services.SystemInventoryAsync(cancellation.Token).ConfigureAwait(true);
            Inventory = SystemInventory.ToSummary();
            Notify("Ready");
        }
        catch (OperationCanceledException) { Notify("AnalysisCancelled"); }
        catch (Exception exception) { Fail("SourceUnavailable", exception); }
        finally { EndOperation(); Changed?.Invoke(ViewChange.Page); }
    }
    public async Task RefreshHistoryAsync()
    {
        if (IsBusy) return;
        using var cancellation = BeginOperation("LoadingHistory");
        try
        {
            var selectedId = SelectedHistory?.Scan.Id;
            History = (await services.LoadHistoryAsync(cancellation.Token).ConfigureAwait(true)).Select(item => new HistoryRow(item, Text)).ToArray();
            SelectedHistory = History.FirstOrDefault(row => row.Scan.Id == selectedId) ?? (History.Count > 0 ? History[0] : null);
            Notify("Ready");
        }
        catch (Exception exception) { Fail("HistoryError", exception); }
        finally { EndOperation(); Changed?.Invoke(ViewChange.Page); }
    }
    public void ChangeSettings(UserSettings settings)
    {
        var languageChanged = Settings.Language != settings.Language;
        Settings = settings;
        Period = settings.Period;
        if (languageChanged) { Text.SetCulture(settings.Language); RebuildRows(); }
        try { services.SaveSettings(settings); }
        catch (Exception exception) { Fail("SettingsError", exception); }
        Changed?.Invoke(languageChanged ? ViewChange.Language : ViewChange.Theme);
    }
    public async Task ClearDataAsync()
    {
        if (IsBusy) return;
        using var cancellation = BeginOperation("ClearingData");
        try
        {
            await services.ClearAsync(cancellation.Token).ConfigureAwait(true);
            SetResult(ScanResult.Empty(DateTimeOffset.UtcNow));
            HasAnalysis = false;
            LastRequestedFromUtc = null; LastRequestedToUtc = null;
            Imports.Clear();
            History = []; SelectedHistory = null;
            Notify("DataCleared");
        }
        catch (Exception exception) { Fail("HistoryError", exception); }
        finally { EndOperation(); Changed?.Invoke(ViewChange.Page); }
    }
    public void Notify(string key) { StatusKey = key; statusPage = operation is null ? Page : operationPage; Changed?.Invoke(ViewChange.State); }
    public void Fail(string key, Exception exception) { TechnicalError = exception.GetType().Name; Notify(key); }
    private CancellationTokenSource BeginOperation(string status)
    {
        operation = new CancellationTokenSource();
        operationPage = Page;
        IsBusy = true;
        TechnicalError = string.Empty;
        Notify(status);
        return operation;
    }
    private void EndOperation() { operation = null; IsBusy = false; Changed?.Invoke(ViewChange.State); }
    private ScanHistoryMetadata HistoryMetadata(string type, DateTimeOffset from, DateTimeOffset to, TimeSpan duration) => new(type, from, to,
        (long)duration.TotalMilliseconds, AttentionCount, KnowingCount, BackgroundCount,
        string.Join("; ", Result.Coverage.Select(item => $"{PresentationPolicy.SourceKey(item)}={item.State}")));
    public void Dispose() { operation?.Cancel(); operation?.Dispose(); }
}
