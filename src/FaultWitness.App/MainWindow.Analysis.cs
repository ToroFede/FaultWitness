using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FaultWitness.Core;

namespace FaultWitness.App;

public sealed partial class MainWindow
{
    private static readonly int[] WindowOptions = [2, 5, 10, 30, 60];
    private string AnalysisRange()
    {
        var now = DateTimeOffset.Now;
        var (from, to) = ViewModel.LastRequestedFromUtc is { } requestedFrom && ViewModel.LastRequestedToUtc is { } requestedTo
            ? (requestedFrom, requestedTo)
            : ViewModel.IsAround
                ? (ViewModel.AroundTime.AddMinutes(-ViewModel.WindowMinutes), ViewModel.AroundTime.AddMinutes(ViewModel.WindowMinutes))
                : ViewModel.Period switch
                {
                    AnalysisPeriod.Day => (now.AddDays(-1), now),
                    AnalysisPeriod.Month => (now.AddDays(-30), now),
                    AnalysisPeriod.Custom => (ViewModel.CustomFrom, ViewModel.CustomTo),
                    _ => (now.AddDays(-7), now)
                };
        var culture = ViewModel.Text.Culture;
        return from.ToLocalTime().ToString("g", culture) + " — " + to.ToLocalTime().ToString("g", culture);
    }

    private StackPanel QuietResultContent(string key, bool backgroundOnly = false)
    {
        var title = ViewModel.IsImported ? T(key + "Imported") : ViewModel.Text.Format(key, AnalysisRange());
        var content = Stack(Label(title, TextRole.RowTitle));
        if (backgroundOnly)
            content.Children.Add(Muted(ViewModel.Text.Format("BackgroundEntriesCount", T("PriorityBackground"), ViewModel.BackgroundCount.ToString(ViewModel.Text.Culture))));
        content.Children.Add(Muted(T("QuietResultCaution"), TextRole.Body));

        var limitedCoverage = ViewModel.Result.Coverage.Where(source => source.State != CoverageState.Complete)
            .Select(source => T(PresentationPolicy.SourceKey(source)) + ": " + T("Coverage" + source.State)).ToArray();
        if (limitedCoverage.Length > 0)
            content.Children.Add(Muted(T("CoverageLimitSummary") + " " + string.Join(" · ", limitedCoverage), TextRole.Body));
        return content;
    }

    private Border QuietResult(string key, bool backgroundOnly = false)
    {
        var panel = Surface(QuietResultContent(key, backgroundOnly));
        panel.Name = "QuietResult";
        return panel;
    }

    private StackPanel IncidentEmptyContent()
    {
        if (!ViewModel.HasAnalysis) return Stack(Label(T("NoAnalysis"), TextRole.RowTitle), Muted(T("NoHistory"), TextRole.Body));
        if (ViewModel.AllRows.Count == 0) return QuietResultContent("NoSupportedIncidents");
        if (ViewModel.FilteredRows.Count > 0) return new StackPanel();

        var reset = Button("ResetFilters", () => { ViewModel.SetFilter(new()); RenderPage(); }, "ResetFilters");
        return Stack(Label(T("NoFilterMatches"), TextRole.SectionTitle), Muted(T("ResetFilterHelp")), reset);
    }

    private ScrollViewer BuildOverview()
    {
        var primaryKey = ViewModel.Period switch
        {
            AnalysisPeriod.Day => "AnalyzeLastDay", AnalysisPeriod.Month => "AnalyzeLastMonth",
            AnalysisPeriod.Custom => "AnalyzeSelectedPeriod", _ => "AnalyzeLastWeek"
        };
        var body = Stack(Heading("Home", "HomePurpose"),
            PrimaryButton(primaryKey, () => ViewModel.OpenAnalyze(AnalysisMode.Recent), "PrimaryAnalyze"),
            Muted(T("ProductScope"), TextRole.Body), Muted(T("LocalAnalysisOnly")),
            Actions(SubtleButton("AnalyzeCrashFreeze", () => ViewModel.OpenAnalyze(AnalysisMode.Around), "StartAroundFlow"),
                SubtleButton("AnalyzeFiles", () => ViewModel.OpenAnalyze(AnalysisMode.Files), "StartFilesFlow")));
        if (!ViewModel.HasAnalysis)
        {
            body.Children.Add(Empty("NoAnalysis"));
            body.Children.Add(Muted(T("PrivacyStatement")));
            return Scroll(body);
        }
        body.Children.Add(Label(T(ViewModel.IsImported ? "ImportedAnalysis" : "LastAnalysis"), TextRole.SectionTitle));
        var shownFrom = ViewModel.LastRequestedFromUtc ?? ViewModel.Result.StartedUtc; var shownTo = ViewModel.LastRequestedToUtc ?? ViewModel.Result.FinishedUtc;
        body.Children.Add(Muted(ViewModel.Origin + " · " + shownFrom.ToLocalTime().ToString("g", ViewModel.Text.Culture) + " — " + shownTo.ToLocalTime().ToString("g", ViewModel.Text.Culture)));
        if (!ViewModel.IsImported && ViewModel.LastDurationSeconds > 0)
            body.Children.Add(Muted(ViewModel.Text.Format("AnalysisDuration", ViewModel.LastDurationSeconds)));
        Panel counts = layoutClass == "small" ? new StackPanel { Spacing = D("primitive.space.1") } : new WrapPanel { Orientation = Orientation.Horizontal };
        counts.Name = "HomeSummary";
        counts.Classes.Add(layoutClass == "small" ? "summary-stacked" : "summary-strip");
        var levels = new[] { AttentionLevel.Attention, AttentionLevel.Knowing, AttentionLevel.Background };
        var values = new[] { ViewModel.AttentionCount, ViewModel.KnowingCount, ViewModel.BackgroundCount };
        for (var index = 0; index < levels.Length; index++)
        {
            var level = levels[index];
            var metric = new Button { Name = "Count" + level, Content = Label(values[index].ToString(ViewModel.Text.Culture) + "  " + T("Priority" + level), bold: true),
                HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left,
                MinHeight = D("component.action.standard.minHeight"), Padding = new Thickness(D("primitive.space.2")) };
            Avalonia.Automation.AutomationProperties.SetName(metric, T("Priority" + level) + ": " + values[index]);
            metric.Click += (_, _) => ViewModel.ShowPriority(level);
            metric.Classes.Add("summary-metric"); counts.Children.Add(metric);
        }
        body.Children.Add(counts);
        body.Children.Add(Label(T("RecentSignificant"), TextRole.SectionTitle));
        if (ViewModel.RecentSignificant.Count == 0)
        {
            var backgroundOnly = ViewModel.AllRows.Count > 0 && ViewModel.BackgroundCount > 0;
            body.Children.Add(QuietResult(backgroundOnly ? "NoPriorityIncidents" : "NoSupportedIncidents", backgroundOnly));
        }
        else
        {
            var list = IncidentList(ViewModel.RecentSignificant, true); list.Height = 290; body.Children.Add(list);
        }
        body.Children.Add(Actions(SubtleButton("ViewAll", () => ViewModel.ShowPriority(null), "ViewAllIncidents"), SubtleButton("OpenHistory", async () => { await ViewModel.RefreshHistoryAsync(); ViewModel.Navigate(AppPage.History); }, "OpenHistory"), SubtleButton("Export", () => ViewModel.Navigate(AppPage.Export), "OverviewExport")));
        body.Children.Add(Expand("SourceCoverage", Coverage(ViewModel.Result.Coverage)));
        return Scroll(body);
    }
    private static DateTimeOffset Combine(DateTimeOffset? date, TimeSpan? time)
    {
        var local = DateTime.SpecifyKind((date ?? DateTimeOffset.Now).Date + (time ?? TimeSpan.Zero), DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local)) throw new ArgumentException("Invalid local time.");
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
    private ScrollViewer BuildAnalysis(AnalysisMode mode)
    {
        var around = mode == AnalysisMode.Around;
        var body = Stack(Heading("Analyze", "AnalysisHelp"));
        body.Children.Add(Muted(T(mode == AnalysisMode.Files ? "ImportScope" : "ProductScope"), TextRole.Body));
        var modes = new ComboBox { Name = "AnalysisMode", ItemsSource = new[] { T("AnalyzeRecent"), T("AnalyzeCrashFreeze"), T("AnalyzeFiles") }, SelectedIndex = (int)mode, Width = 320 };
        modes.SelectionChanged += (_, _) => { if (modes.SelectedIndex >= 0 && (AnalysisMode)modes.SelectedIndex != ViewModel.AnalysisMode) ViewModel.OpenAnalyze((AnalysisMode)modes.SelectedIndex); };
        body.Children.Add(Field("AnalysisWorkflow", modes));
        body.Children.Add(Label(T(mode == AnalysisMode.Files ? "ImportHelp" : around ? "AroundAnalysisGuide" : "RecentAnalysisGuide")));
        if (mode == AnalysisMode.Files) { body.Children.Add(BuildImportContent()); return Scroll(body); }
        var period = new ComboBox { Name = "PeriodSelector", ItemsSource = new[] { T("PeriodDay"), T("PeriodWeek"), T("PeriodMonth"), T("PeriodCustom") }, SelectedIndex = (int)ViewModel.Period, Width = 280 };
        var fromDate = new DatePicker { Name = "FromDate", SelectedDate = ViewModel.CustomFrom };
        var toDate = new DatePicker { Name = "ToDate", SelectedDate = ViewModel.CustomTo };
        var custom = Actions(Field("FromDate", fromDate), Field("ToDate", toDate));
        custom.IsVisible = !around && ViewModel.Period == AnalysisPeriod.Custom;
        period.SelectionChanged += (_, _) => { ViewModel.Period = (AnalysisPeriod)Math.Max(0, period.SelectedIndex); custom.IsVisible = ViewModel.Period == AnalysisPeriod.Custom; };
        var center = new DatePicker { Name = "AroundDate", SelectedDate = ViewModel.AroundTime };
        var time = new TimePicker { Name = "AroundTime", SelectedTime = ViewModel.AroundTime.TimeOfDay, ClockIdentifier = "24HourClock" };
        var window = new ComboBox { Name = "AroundWindow", ItemsSource = WindowOptions.Select(minutes => ViewModel.Text.Format("AroundWindowValue", minutes)).ToArray(), SelectedIndex = 1, Width = 160 };
        if (around) body.Children.Add(Actions(Field("Date", center), Field("Time", time), Field("Window", window)));
        else { body.Children.Add(Field("AnalysisPeriod", period)); body.Children.Add(custom); }
        var sources = Stack(Label(T("SourcesHelp")), Label(T("SourceSystem")), Label(T("SourceApplication")), Label(T("SourceWer")), Label(T("SourceReliability")), Label(T("SourceArtifacts")));
        body.Children.Add(Expand("AdvancedSources", sources));
        body.Children.Add(PrimaryAsyncButton(around ? "AnalyzeCrashFreeze" : "RunSelectedAnalysis", async () =>
        {
            ViewModel.CustomFrom = Combine(fromDate.SelectedDate, TimeSpan.Zero);
            ViewModel.CustomTo = Combine(toDate.SelectedDate, new TimeSpan(23, 59, 59));
            if (around) { ViewModel.AroundTime = Combine(center.SelectedDate, time.SelectedTime); ViewModel.WindowMinutes = WindowOptions[Math.Max(window.SelectedIndex, 0)]; }
            await ViewModel.AnalyzeAsync(around).ConfigureAwait(true);
        }, "RunAnalysis"));
        body.Children.Add(Muted(T("PrivacyStatement")));
        return Scroll(body);
    }
    private Grid BuildIncidents()
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), Name = "IncidentsPage" };
        grid.Children.Add(Heading("Incidents", ViewModel.IsImported ? "ImportedData" : "IncidentHelp"));
        var priorities = new ComboBox { Name = "PriorityFilter", ItemsSource = new[] { T("PriorityAttention"), T("PriorityKnowing"), T("PriorityBackground"), T("All") },
            SelectedIndex = ViewModel.Filter.Priority is null ? 3 : (int)ViewModel.Filter.Priority.Value, Width = 190 };
        var categories = new ComboBox { Name = "CategoryFilter", ItemsSource = new[] { T("AllCategories") }.Concat(Enum.GetValues<IncidentCategory>().Select(value => T("Category" + value))).ToArray(), SelectedIndex = ViewModel.Filter.Category is null ? 0 : (int)ViewModel.Filter.Category.Value + 1, Width = 195 };
        var strength = new ComboBox { Name = "StrengthFilter", ItemsSource = new[] { T("AllEvidence") }.Concat(Enum.GetValues<EvidenceStrength>().Select(value => T("Strength" + value))).ToArray(), SelectedIndex = ViewModel.Filter.Strength is null ? 0 : (int)ViewModel.Filter.Strength.Value + 1, Width = 160 };
        var search = new TextBox { Name = "IncidentSearch", PlaceholderText = T("SearchHint"), Text = ViewModel.Filter.Search, MinWidth = 250 };
        var from = new DatePicker { Name = "FilterFrom", SelectedDate = ViewModel.Filter.From?.ToLocalTime() }; var to = new DatePicker { Name = "FilterTo", SelectedDate = ViewModel.Filter.To?.ToLocalTime() };
        void Filter()
        {
            ViewModel.SetFilter(ViewModel.Filter with
            {
                Priority = priorities.SelectedIndex == 3 ? null : (AttentionLevel)priorities.SelectedIndex,
                Category = categories.SelectedIndex <= 0 ? null : (IncidentCategory)(categories.SelectedIndex - 1),
                Strength = strength.SelectedIndex <= 0 ? null : (EvidenceStrength)(strength.SelectedIndex - 1), Search = search.Text ?? string.Empty,
                From = from.SelectedDate is null ? null : Combine(from.SelectedDate, TimeSpan.Zero).ToUniversalTime(),
                To = to.SelectedDate is null ? null : Combine(to.SelectedDate, new TimeSpan(23, 59, 59)).ToUniversalTime()
            });
        }
        priorities.SelectionChanged += (_, _) => Filter(); categories.SelectionChanged += (_, _) => Filter(); strength.SelectionChanged += (_, _) => Filter(); search.TextChanged += (_, _) => Filter();
        from.SelectedDateChanged += (_, _) => Filter(); to.SelectedDateChanged += (_, _) => Filter();
        var filters = Stack(Actions(Field("Priority", priorities), Field("Category", categories), Field("Evidence", strength)), search,
            Expand("DateRange", Actions(Field("FromDate", from), Field("ToDate", to))));
        Grid.SetRow(filters, 1); grid.Children.Add(filters);
        filterCount = Muted(ViewModel.Text.Format("ItemsShown", ViewModel.FilteredRows.Count, ViewModel.AllRows.Count)); filterCount.Margin = new Thickness(0, D("primitive.space.2"), 0, D("primitive.space.2"));
        Grid.SetRow(filterCount, 2); grid.Children.Add(filterCount);
        incidentList = IncidentList(ViewModel.FilteredRows); Grid.SetRow(incidentList, 3); grid.Children.Add(incidentList);
        filterEmpty = Surface(IncidentEmptyContent());
        filterEmpty.Name = "FilterEmpty"; filterEmpty.IsVisible = ViewModel.FilteredRows.Count == 0;
        filterEmpty.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRow(filterEmpty, 3); grid.Children.Add(filterEmpty);
        return grid;
    }
}
