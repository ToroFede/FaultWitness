using System.ComponentModel;
using System.Text.Json;
using FaultWitness.Core;
using FaultWitness.Export;
using FaultWitness.Localization;
using FaultWitness.Storage;

namespace FaultWitness.App.Presentation;

public abstract class PagePresentation : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

public sealed class HomePresentation(MainViewModel viewModel) : PagePresentation
{
    public LocalizedLabels Text { get; private set; } = new(viewModel.Text);
    public string PrimaryAction { get; private set; } = string.Empty;
    public string RangeAndOrigin { get; private set; } = string.Empty;
    public string QuietTitle { get; private set; } = string.Empty;
    public string QuietBackgroundCount { get; private set; } = string.Empty;
    public string QuietCaution { get; private set; } = string.Empty;
    public string QuietCoverage { get; private set; } = string.Empty;
    public string AnalysisTitle { get; private set; } = string.Empty;
    public string Duration { get; private set; } = string.Empty;
    public bool HasAnalysis => viewModel.HasAnalysis;
    public bool IsNoAnalysis => !HasAnalysis;
    public bool HasDuration { get; private set; }
    public bool HasSignificantIncidents => viewModel.RecentSignificant.Count > 0;
    public bool ShowQuietResult => HasAnalysis && !HasSignificantIncidents;
    public bool IsBackgroundOnly => viewModel.AllRows.Count > 0 && viewModel.BackgroundCount > 0 && !HasSignificantIncidents;
    public bool HasLimitedCoverage => QuietCoverage.Length > 0;
    public IReadOnlyList<IncidentRow> RecentIncidents => viewModel.RecentSignificant;
    public IReadOnlyList<SummaryMetricPresentation> SummaryMetrics { get; private set; } = [];
    public IReadOnlyList<CoverageDetailPresentation> CoverageRows { get; private set; } = [];

    public void Refresh()
    {
        Text = new(viewModel.Text);
        PrimaryAction = viewModel.Period switch
        {
            AnalysisPeriod.Day => viewModel.Text.Get("AnalyzeLastDay"),
            AnalysisPeriod.Month => viewModel.Text.Get("AnalyzeLastMonth"),
            AnalysisPeriod.Custom => viewModel.Text.Get("AnalyzeSelectedPeriod"),
            _ => viewModel.Text.Get("AnalyzeLastWeek")
        };
        AnalysisTitle = viewModel.Text.Get(viewModel.IsImported ? "ImportedAnalysis" : "LastAnalysis");
        var shownFrom = viewModel.LastRequestedFromUtc ?? viewModel.Result.StartedUtc;
        var shownTo = viewModel.LastRequestedToUtc ?? viewModel.Result.FinishedUtc;
        RangeAndOrigin = viewModel.Origin + " · " + shownFrom.ToLocalTime().ToString("g", viewModel.Text.Culture) + " — " + shownTo.ToLocalTime().ToString("g", viewModel.Text.Culture);
        HasDuration = !viewModel.IsImported && viewModel.LastDurationSeconds > 0;
        Duration = viewModel.Text.Format("AnalysisDuration", viewModel.LastDurationSeconds);
        SummaryMetrics = new[]
        {
            new SummaryMetricPresentation(AttentionLevel.Attention, viewModel.AttentionCount, viewModel.Text.Get("PriorityAttention"), viewModel.AttentionCount.ToString(viewModel.Text.Culture)),
            new SummaryMetricPresentation(AttentionLevel.Knowing, viewModel.KnowingCount, viewModel.Text.Get("PriorityKnowing"), viewModel.KnowingCount.ToString(viewModel.Text.Culture)),
            new SummaryMetricPresentation(AttentionLevel.Background, viewModel.BackgroundCount, viewModel.Text.Get("PriorityBackground"), viewModel.BackgroundCount.ToString(viewModel.Text.Culture))
        };
        var backgroundOnly = IsBackgroundOnly;
        var key = backgroundOnly ? "NoPriorityIncidents" : "NoSupportedIncidents";
        QuietTitle = viewModel.IsImported
            ? viewModel.Text.Get(key + "Imported")
            : viewModel.Text.Format(key, AnalysisRange());
        QuietBackgroundCount = backgroundOnly
            ? viewModel.Text.Format("BackgroundEntriesCount", viewModel.Text.Get("PriorityBackground"), viewModel.BackgroundCount.ToString(viewModel.Text.Culture))
            : string.Empty;
        QuietCaution = viewModel.Text.Get("QuietResultCaution");
        var limited = viewModel.Result.Coverage.Where(source => source.State != CoverageState.Complete)
            .Select(source => viewModel.Text.Get(PresentationPolicy.SourceKey(source)) + ": " + viewModel.Text.Get("Coverage" + source.State)).ToArray();
        QuietCoverage = limited.Length == 0 ? string.Empty : viewModel.Text.Get("CoverageLimitSummary") + " " + string.Join(" · ", limited);
        CoverageRows = viewModel.Result.Coverage.Select(source => CoverageDetailPresentation.From(source, viewModel.Text)).ToArray();
        Changed();
    }

    private string AnalysisRange()
    {
        var now = DateTimeOffset.Now;
        var (from, to) = viewModel.LastRequestedFromUtc is { } requestedFrom && viewModel.LastRequestedToUtc is { } requestedTo
            ? (requestedFrom, requestedTo)
            : viewModel.IsAround
                ? (viewModel.AroundTime.AddMinutes(-viewModel.WindowMinutes), viewModel.AroundTime.AddMinutes(viewModel.WindowMinutes))
                : viewModel.Period switch
                {
                    AnalysisPeriod.Day => (now.AddDays(-1), now),
                    AnalysisPeriod.Month => (now.AddDays(-30), now),
                    AnalysisPeriod.Custom => (viewModel.CustomFrom, viewModel.CustomTo),
                    _ => (now.AddDays(-7), now)
                };
        return from.ToLocalTime().ToString("g", viewModel.Text.Culture) + " — " + to.ToLocalTime().ToString("g", viewModel.Text.Culture);
    }
}

public sealed record SummaryMetricPresentation(AttentionLevel Priority, int Count, string Label, string FormattedCount)
{
    public string CountLabel => FormattedCount + "  " + Label;
    public string AccessibleName => Label + ": " + FormattedCount;
}

public sealed record CoverageDetailPresentation(SourceCoverage Source, string Name, string State, string Help, string Interval)
{
    public static CoverageDetailPresentation From(SourceCoverage source, LocalizationService text) => new(source,
        text.Get(PresentationPolicy.SourceKey(source)), text.Get("Coverage" + source.State), text.Get("CoverageHelp" + source.State),
        source.ExaminedFromUtc is null ? text.Get("IntervalUnknown") : text.Format("IntervalValue",
            source.ExaminedFromUtc.Value.ToLocalTime().ToString("g", text.Culture),
            source.ExaminedToUtc?.ToLocalTime().ToString("g", text.Culture) ?? text.Get("NotAvailable")));
}

public sealed class AnalyzePresentation(MainViewModel viewModel) : PagePresentation
{
    private static readonly int[] WindowOptions = [2, 5, 10, 30, 60];
    private DateTimeOffset? customFromDate = viewModel.CustomFrom;
    private DateTimeOffset? customToDate = viewModel.CustomTo;
    private DateTimeOffset? aroundDate = viewModel.AroundTime;
    private TimeSpan? aroundClock = viewModel.AroundTime.TimeOfDay;
    private int windowIndex = Math.Max(0, Array.IndexOf(WindowOptions, viewModel.WindowMinutes));
    private AnalysisPeriod selectedPeriod = viewModel.Period;

    public LocalizedLabels Text { get; private set; } = new(viewModel.Text);
    public IReadOnlyList<string> Modes { get; private set; } = [];
    public IReadOnlyList<string> Periods { get; private set; } = [];
    public IReadOnlyList<string> Windows { get; private set; } = [];
    public IReadOnlyList<ImportItemPresentation> Imports { get; private set; } = [];
    public string ModeGuidance { get; private set; } = string.Empty;
    public string Scope { get; private set; } = string.Empty;
    public string Help { get; private set; } = string.Empty;
    public string Privacy { get; private set; } = string.Empty;
    public int ModeIndex => (int)viewModel.AnalysisMode;
    public int PeriodIndex => (int)selectedPeriod;
    public AnalysisPeriod SelectedPeriod => selectedPeriod;
    public int WindowMinutes => WindowOptions[windowIndex];
    public int WindowIndex { get => windowIndex; set { if (value >= 0 && value < WindowOptions.Length) { windowIndex = value; viewModel.WindowMinutes = WindowOptions[value]; Changed(); } } }
    public DateTimeOffset? CustomFromDate { get => customFromDate; set { customFromDate = value; if (value is { } date) viewModel.CustomFrom = date; Changed(); } }
    public DateTimeOffset? CustomToDate { get => customToDate; set { customToDate = value; if (value is { } date) viewModel.CustomTo = date; Changed(); } }
    public DateTimeOffset? AroundDate { get => aroundDate; set { aroundDate = value; Changed(); } }
    public TimeSpan? AroundClock { get => aroundClock; set { aroundClock = value; Changed(); } }
    public bool IsRecent => viewModel.AnalysisMode == AnalysisMode.Recent;
    public bool IsAround => viewModel.AnalysisMode == AnalysisMode.Around;
    public bool IsFiles => viewModel.AnalysisMode == AnalysisMode.Files;
    public bool IsCustomPeriod => selectedPeriod == AnalysisPeriod.Custom && IsRecent;
    public bool HasImports => Imports.Count > 0;
    public bool IsNoImports => !HasImports;
    public bool IsBusy => viewModel.IsBusy;

    public void Refresh()
    {
        Text = new(viewModel.Text);
        Modes = [viewModel.Text.Get("AnalyzeRecent"), viewModel.Text.Get("AnalyzeCrashFreeze"), viewModel.Text.Get("AnalyzeFiles")];
        Periods = [viewModel.Text.Get("PeriodDay"), viewModel.Text.Get("PeriodWeek"), viewModel.Text.Get("PeriodMonth"), viewModel.Text.Get("PeriodCustom")];
        Windows = WindowOptions.Select(minutes => viewModel.Text.Format("AroundWindowValue", minutes)).ToArray();
        Scope = viewModel.Text.Get(IsFiles ? "ImportScope" : "ProductScope");
        Help = viewModel.Text.Get(IsFiles ? "ImportHelp" : IsAround ? "AroundAnalysisGuide" : "RecentAnalysisGuide");
        ModeGuidance = viewModel.Text.Get("AnalysisHelp");
        Privacy = viewModel.Text.Get("PrivacyStatement");
        Imports = viewModel.Imports.Select(row => new ImportItemPresentation(Path.GetFileName(row.Path), viewModel.Text.Get(row.StatusKey))).ToArray();
        Changed();
    }

    public void SelectMode(int index)
    {
        if (index is >= 0 and <= 2 && index != ModeIndex) viewModel.OpenAnalyze((AnalysisMode)index);
    }

    public void SelectPeriod(int index)
    {
        if (index is >= 0 and <= 3 && index != PeriodIndex)
        {
            selectedPeriod = (AnalysisPeriod)index;
            viewModel.Period = selectedPeriod;
            Changed();
        }
    }

    public void SyncPeriodFromViewModel()
    {
        selectedPeriod = viewModel.Period;
        Changed();
    }

    public void SyncSelection()
    {
        windowIndex = Math.Max(0, Array.IndexOf(WindowOptions, viewModel.WindowMinutes));
        Changed();
    }
}

public sealed record ImportItemPresentation(string Name, string Status);
public sealed record AnalyzeRunRequest(bool Around, DateTimeOffset? From, DateTimeOffset? To, DateTimeOffset? AroundDate, TimeSpan? AroundTime, int WindowMinutes);

public sealed class IncidentListPresentation(MainViewModel viewModel) : PagePresentation
{
    public LocalizedLabels Text { get; private set; } = new(viewModel.Text);
    public IReadOnlyList<IncidentRow> Rows { get; private set; } = [];
    public IReadOnlyList<string> Priorities { get; private set; } = [];
    public IReadOnlyList<string> Categories { get; private set; } = [];
    public IReadOnlyList<string> Strengths { get; private set; } = [];
    public string CountText { get; private set; } = string.Empty;
    public string HeadingHelp { get; private set; } = string.Empty;
    public string EmptyHeading { get; private set; } = string.Empty;
    public string EmptyHelp { get; private set; } = string.Empty;
    public string EmptyCoverage { get; private set; } = string.Empty;
    public string EmptyCaution { get; private set; } = string.Empty;
    public string EmptyBackgroundCount { get; private set; } = string.Empty;
    public int PriorityIndex => viewModel.Filter.Priority is null ? 3 : (int)viewModel.Filter.Priority.Value;
    public int CategoryIndex => viewModel.Filter.Category is null ? 0 : (int)viewModel.Filter.Category.Value + 1;
    public int StrengthIndex => viewModel.Filter.Strength is null ? 0 : (int)viewModel.Filter.Strength.Value + 1;
    public string SearchText => viewModel.Filter.Search;
    public DateTimeOffset? FromDate => viewModel.Filter.From?.ToLocalTime();
    public DateTimeOffset? ToDate => viewModel.Filter.To?.ToLocalTime();
    public bool HasAnalysis => viewModel.HasAnalysis;
    public bool HasRows => Rows.Count > 0;
    public bool IsEmpty => !HasRows;
    public bool ShowQuietEmpty => viewModel.HasAnalysis && viewModel.AllRows.Count == 0;
    public bool ShowResetFilters { get; private set; }

    public void Refresh()
    {
        Text = new(viewModel.Text);
        Rows = viewModel.FilteredRows;
        Priorities = [viewModel.Text.Get("PriorityAttention"), viewModel.Text.Get("PriorityKnowing"), viewModel.Text.Get("PriorityBackground"), viewModel.Text.Get("All")];
        Categories = [viewModel.Text.Get("AllCategories"), .. Enum.GetValues<IncidentCategory>().Select(value => viewModel.Text.Get("Category" + value))];
        Strengths = [viewModel.Text.Get("AllEvidence"), .. Enum.GetValues<EvidenceStrength>().Select(value => viewModel.Text.Get("Strength" + value))];
        CountText = viewModel.Text.Format("ItemsShown", viewModel.FilteredRows.Count, viewModel.AllRows.Count);
        HeadingHelp = viewModel.Text.Get(viewModel.IsImported ? "ImportedData" : "IncidentHelp");
        ShowResetFilters = viewModel.HasAnalysis && viewModel.AllRows.Count > 0 && viewModel.FilteredRows.Count == 0;
        if (!viewModel.HasAnalysis)
        {
            EmptyHeading = viewModel.Text.Get("NoAnalysis");
            EmptyHelp = viewModel.Text.Get("NoHistory");
            EmptyCoverage = EmptyCaution = EmptyBackgroundCount = string.Empty;
        }
        else if (viewModel.AllRows.Count == 0)
        {
            var quiet = QuietResultProjection.Create(viewModel, "NoSupportedIncidents", false);
            EmptyHeading = quiet.Title; EmptyHelp = quiet.Caution; EmptyCoverage = quiet.Coverage; EmptyBackgroundCount = string.Empty;
        }
        else if (ShowResetFilters)
        {
            EmptyHeading = viewModel.Text.Get("NoFilterMatches");
            EmptyHelp = viewModel.Text.Get("ResetFilterHelp");
            EmptyCoverage = EmptyCaution = EmptyBackgroundCount = string.Empty;
        }
        else
        {
            EmptyHeading = viewModel.Text.Get("NoSignificant");
            EmptyHelp = viewModel.Text.Get("CheckCoverage");
            EmptyCoverage = EmptyCaution = EmptyBackgroundCount = string.Empty;
        }
        Changed();
    }
}

public sealed record QuietResultProjection(string Title, string BackgroundCount, string Caution, string Coverage)
{
    public static QuietResultProjection Create(MainViewModel viewModel, string key, bool backgroundOnly)
    {
        var now = DateTimeOffset.Now;
        var (from, to) = viewModel.LastRequestedFromUtc is { } requestedFrom && viewModel.LastRequestedToUtc is { } requestedTo
            ? (requestedFrom, requestedTo)
            : viewModel.IsAround
                ? (viewModel.AroundTime.AddMinutes(-viewModel.WindowMinutes), viewModel.AroundTime.AddMinutes(viewModel.WindowMinutes))
                : viewModel.Period switch
                {
                    AnalysisPeriod.Day => (now.AddDays(-1), now), AnalysisPeriod.Month => (now.AddDays(-30), now),
                    AnalysisPeriod.Custom => (viewModel.CustomFrom, viewModel.CustomTo), _ => (now.AddDays(-7), now)
                };
        var range = from.ToLocalTime().ToString("g", viewModel.Text.Culture) + " — " + to.ToLocalTime().ToString("g", viewModel.Text.Culture);
        var title = viewModel.IsImported ? viewModel.Text.Get(key + "Imported") : viewModel.Text.Format(key, range);
        var background = backgroundOnly
            ? viewModel.Text.Format("BackgroundEntriesCount", viewModel.Text.Get("PriorityBackground"), viewModel.BackgroundCount.ToString(viewModel.Text.Culture))
            : string.Empty;
        var limited = viewModel.Result.Coverage.Where(source => source.State != CoverageState.Complete)
            .Select(source => viewModel.Text.Get(PresentationPolicy.SourceKey(source)) + ": " + viewModel.Text.Get("Coverage" + source.State)).ToArray();
        var coverage = limited.Length == 0 ? string.Empty : viewModel.Text.Get("CoverageLimitSummary") + " " + string.Join(" · ", limited);
        return new(title, background, viewModel.Text.Get("QuietResultCaution"), coverage);
    }
}

public static class DateTimeInput
{
    public static DateTimeOffset Combine(DateTimeOffset? date, TimeSpan? time)
    {
        var local = DateTime.SpecifyKind((date ?? DateTimeOffset.Now).Date + (time ?? TimeSpan.Zero), DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local)) throw new ArgumentException("Invalid local time.");
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}

public sealed class HistoryPresentation(MainViewModel viewModel) : PagePresentation
{
    private List<HistoryItemPresentation> items = [];
    private Dictionary<string, HistoryItemPresentation> byId = new(StringComparer.Ordinal);
    public LocalizedLabels Text { get; private set; } = new(viewModel.Text);
    public IReadOnlyList<HistoryItemPresentation> Items => items;
    public HistoryItemPresentation? Selected { get; private set; }
    public bool HasItems => items.Count > 0;
    public bool IsEmpty => !HasItems;
    public bool HasSelection => Selected is not null;
    public bool HasDuration => Selected?.HasDuration ?? false;
    public bool HasCoverageSummary => Selected?.HasCoverageSummary ?? false;
    public string EmptyTitle { get; private set; } = string.Empty;
    public string EmptyHelp { get; private set; } = string.Empty;
    public string Duration => Selected?.Duration ?? string.Empty;
    public string CoverageSummary => Selected?.CoverageSummary ?? string.Empty;
    public string SavedSummaryLabel { get; private set; } = string.Empty;
    public string CopyLabel { get; private set; } = string.Empty;
    public string SaveLabel { get; private set; } = string.Empty;
    public string MoreIncidents { get; private set; } = string.Empty;
    public bool HasMoreIncidents => Selected?.HasMoreIncidents ?? false;
    public IReadOnlyList<HistoryIncidentPresentation> Incidents => Selected?.Incidents ?? [];

    public void Refresh(MainViewModel source, bool refreshItems)
    {
        Text = new(source.Text);
        EmptyTitle = source.Text.Get("HistoryEmptyTitle");
        EmptyHelp = source.Text.Get("NoHistory");
        SavedSummaryLabel = source.Text.Get("SavedSummary");
        CopyLabel = source.Text.Get("CopySavedSummary");
        SaveLabel = source.Text.Get("SaveExport");
        if (refreshItems)
        {
            var old = byId;
            items = source.History.Select(row =>
            {
                var item = old.GetValueOrDefault(row.Scan.Id) ?? new HistoryItemPresentation();
                item.Refresh(row, source.Text);
                return item;
            }).ToList();
            byId = items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        }
        Selected = source.SelectedHistory is { } selected ? byId.GetValueOrDefault(selected.Scan.Id) : null;
        Selected?.RefreshDetail(source.Text);
        MoreIncidents = Selected?.MoreIncidents ?? string.Empty;
        Changed();
    }
}

public sealed class HistoryItemPresentation : PagePresentation
{
    private HistoryRow? row;
    public string Id => row?.Scan.Id ?? string.Empty;
    public HistoryRow SourceRow => row ?? throw new InvalidOperationException("History row is not initialized.");
    public string Title { get; private set; } = string.Empty;
    public string Timestamp { get; private set; } = string.Empty;
    public string Period { get; private set; } = string.Empty;
    public string Counts { get; private set; } = string.Empty;
    public string AccessibleSummary => Title + " · " + Timestamp + " · " + Counts;
    public bool HasDuration { get; private set; }
    public bool HasCoverageSummary { get; private set; }
    public string CoverageSummary { get; private set; } = string.Empty;
    public string Duration { get; private set; } = string.Empty;
    public string MoreIncidents { get; private set; } = string.Empty;
    public bool HasMoreIncidents => row?.Scan.Incidents.Count > 20;
    public IReadOnlyList<HistoryIncidentPresentation> Incidents { get; private set; } = [];
    public override string ToString() => AccessibleSummary;

    public void Refresh(HistoryRow updated, LocalizationService text)
    {
        row = updated;
        Title = updated.Title; Timestamp = updated.Timestamp; Period = updated.Period; Counts = updated.Counts;
        HasDuration = updated.Scan.Metadata?.DurationMilliseconds is long;
        Duration = HasDuration ? text.Format("HistoryDuration", updated.Scan.Metadata!.DurationMilliseconds!.Value / 1000d) : string.Empty;
        HasCoverageSummary = !string.IsNullOrWhiteSpace(updated.Scan.Metadata?.CoverageSummary);
        CoverageSummary = updated.Scan.Metadata?.CoverageSummary ?? string.Empty;
        Incidents = updated.Scan.Incidents.Take(20).Select(incident => HistoryIncidentPresentation.From(incident, text)).ToArray();
        MoreIncidents = updated.Scan.Incidents.Count > 20 ? text.Format("MoreHistoryIncidents", updated.Scan.Incidents.Count - 20) : string.Empty;
        Changed();
    }

    public void RefreshDetail(LocalizationService text) => Refresh(row ?? throw new InvalidOperationException("History row is not initialized."), text);
}

public sealed record HistoryIncidentPresentation(string Category, string TimestampSeverity, string ChangeSummary)
{
    public static HistoryIncidentPresentation From(StoredIncident incident, LocalizationService text)
    {
        var summary = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(incident.SummaryJson);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("ChangeContext", out var context) && context.ValueKind != JsonValueKind.Null)
            {
                var savedContext = context.Deserialize<ChangeHistoryContext>();
                var changes = root.TryGetProperty("RelatedChanges", out var related) && related.ValueKind == JsonValueKind.Array
                    ? related.Deserialize<RelatedSystemChange[]>() ?? [] : [];
                if (savedContext is not null) summary = ChangePresentation.ToMarkdown(savedContext, changes, text, new ExportPrivacyOptions());
            }
        }
        catch (JsonException) { }
        return new(text.Get("Category" + incident.Category), incident.OccurredUtc.ToLocalTime().ToString("G", text.Culture) + " · " + incident.Severity, summary);
    }
}
