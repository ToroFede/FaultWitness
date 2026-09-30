using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;
using FaultWitness.Localization;
using FaultWitness.Export;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "IncidentDetailAxaml")]
public sealed class IncidentDetailAxamlTests
{
    private static readonly string[] SectionNames = ["IdentityRegion", "NextStepRegion", "AssessmentLimitsRegion", "MaterialCoverageRegion", "KeyEvidenceRegion", "FullTimeline", "WhatChanged", "Patterns", "Hypotheses", "OtherSteps", "FullCoverage", "TechnicalData"];
    private static readonly string[] ClosedNames = ["FullTimeline", "WhatChanged", "Patterns", "Hypotheses", "OtherSteps", "FullCoverage", "TechnicalData"];
    [Theory]
    [InlineData("restart")][InlineData("graphics")][InlineData("application")][InlineData("groups")]
    [InlineData("partial")][InlineData("denied")][InlineData("not-examined")][InlineData("changes")]
    [InlineData("recurring")][InlineData("imported")]
    public void Projection_PreservesDiagnosticDataAndPreviousActionOrder(string scenario)
    {
        var result = Scenario(scenario);
        var incident = result.Incidents[0];
        var text = new LocalizationService();
        var row = new IncidentRow(incident, text, result.Patterns.Count > 0 ? 2 : 1);
        var model = new IncidentDetailPresentation();
        model.Refresh(row, result, text, text.Get(scenario == "imported" ? "ImportedData" : "LocalSystem"));
        Assert.Same(incident, model.Incident);
        var findings = incident.Findings.Where(item => item.Disposition != FindingDisposition.Suppressed).ToArray();
        Assert.Equal(row.Assessment, model.ObservedSummary);
        Assert.Equal(findings.Select(item => text.Get(item.InterpretationKey)).Distinct(), model.Interpretations);
        Assert.Equal(findings.Select(item => text.Get(item.NotEstablishedKey)).Distinct(), model.Limitations);
        Assert.Equal(findings.SelectMany(item => item.RecommendedActionKeys).Distinct(), model.ActionKeys);
        Assert.Equal(findings.SelectMany(item => item.HypothesisKeys).Distinct().Select(text.Get), model.Hypotheses);
        Assert.Equal(incident.Evidence.Count, model.EvidenceGroups.Sum(item => item.Items.Count));
        foreach (var group in model.EvidenceGroups)
            Assert.Equal(incident.Evidence.Where(item => item.Kind == group.Kind), group.Items.Select(item => item.Source));
        Assert.Equal(incident.SourceEvents.OrderBy(item => item.TimestampUtc), model.TimelineRows.Select(item => item.Source));
        Assert.Equal(result.Coverage, model.CoverageRows.Select(item => item.Source));
        Assert.Equal(incident.RelatedChanges.OrderBy(item => item.Relevance).ThenBy(item => item.OffsetFromFirstObservation.Duration()),
            model.RelevantChanges.Concat(model.LowChanges).Select(item => item.Source));
        Assert.Equal(result.Patterns.Any(item => item.IncidentIds.Contains(incident.Id)), model.HasPattern);
        if (model.HasPattern) Assert.Contains("2", model.Recurrence, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_DoesNotDropDuplicateOrLateLimitingEvidenceOrRescoreRecommendations()
    {
        var basis = Scenario("groups");
        var incident = basis.Incidents[0];
        var first = incident.Findings[0];
        var second = first with { RuleId = "second", InterpretationKey = "NoCauseEstablished", NotEstablishedKey = "rule.system.bugcheck.not_established", RecommendedActionKeys = ["action.ApplicationCrash.investigate", "action.Power.investigate"] };
        incident = incident with { Findings = [first, second], Evidence = [.. incident.Evidence, .. incident.Evidence, new(EvidenceKind.Unknown, "Wer", "evidence.source.unknown", "last-material-limitation")] };
        var text = new LocalizationService();
        var model = new IncidentDetailPresentation();
        model.Refresh(new IncidentRow(incident, text, 1), basis with { Incidents = [incident] }, text, text.Get("LocalSystem"));
        Assert.Equal(incident.Evidence.Count, model.EvidenceGroups.Sum(group => group.Items.Count));
        Assert.Equal(text.Get(first.RecommendedActionKeys[0]), model.BestNextStep);
        Assert.Equal(2, model.Limitations.Count);
        Assert.Equal(2, model.Interpretations.Count);
        Assert.Contains(model.EvidenceGroups.SelectMany(group => group.Items), item => item.Source.Provenance == "last-material-limitation");
    }

    [AvaloniaFact]
    public void FirstHierarchy_AnswersFourQuestionsWithoutOpeningTechnicalSections()
    {
        var window = Open(Scenario("restart"));
        try
        {
            var view = Detail(window);
            var sections = view.FindControl<StackPanel>("DetailSections")!;
            Assert.Equal(SectionNames,
                sections.Children.Skip(1).Select(item => item.Name));
            var text = VisibleText(view);
            var model = (IncidentDetailPresentation)view.DataContext!;
            Assert.Contains(model.ObservedSummary, text, StringComparison.Ordinal);
            Assert.Contains(model.BestNextStep, text, StringComparison.Ordinal);
            Assert.All(model.Interpretations, value => Assert.Contains(value, text, StringComparison.Ordinal));
            Assert.All(model.Limitations, value => Assert.Contains(value, text, StringComparison.Ordinal));
            Assert.All(ClosedNames,
                name => Assert.False(view.FindControl<Expander>(name)!.IsExpanded));
            Assert.False(view.FindControl<Expander>("Patterns")!.IsVisible);
            Assert.False(view.FindControl<Expander>("Hypotheses")!.IsVisible);
            Assert.False(view.FindControl<Expander>("OtherSteps")!.IsVisible);
            Assert.DoesNotContain("<Event>", text, StringComparison.Ordinal);
            var assessment = view.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Text == model.Interpretations[0]);
            var limits = view.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Text == model.Limitations[0]);
            Assert.True(assessment.TranslatePoint(default, view)!.Value.Y < limits.TranslatePoint(default, view)!.Value.Y);
            Assert.True(limits.TranslatePoint(default, view)!.Value.Y < view.FindControl<Control>("KeyEvidenceRegion")!.TranslatePoint(default, view)!.Value.Y);
            Assert.True(limits.TranslatePoint(default, view)!.Value.Y < 900, "Causal limits should occur in the first viewport/short scroll.");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")]
    [InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public void RuntimeLanguageThemeAndNavigation_PreserveSelectionViewFocusAndDisclosure(string language)
    {
        var window = Open(Scenario("changes"));
        try
        {
            var vm = window.ViewModel;
            var view = Detail(window);
            var selected = vm.Selected!.Incident;
            var timeline = view.FindControl<Expander>("FullTimeline")!;
            timeline.IsExpanded = true;
            var copy = view.FindControl<Button>("DetailCopy")!;
            copy.Focus();
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.System })
            {
                vm.ChangeSettings(vm.Settings with { Language = language, Theme = theme });
                window.UpdateLayout();
                Assert.Same(view, Detail(window));
                Assert.Same(selected, vm.Selected!.Incident);
                Assert.True(timeline.IsExpanded);
                Assert.True(copy.IsFocused);
                Assert.Equal(vm.Text.Get("CopyForSupport"), AutomationProperties.GetName(copy));
                Assert.Contains(vm.Text.Get("DetailAssessment"), VisibleText(view), StringComparison.Ordinal);
                Assert.Contains(vm.Text.Get(selected.Findings[0].NotEstablishedKey), VisibleText(view), StringComparison.Ordinal);
                foreach (var width in new[] { 560, 600, 640, 641, 1008, 1280, 1920 })
                {
                    window.Width = width; window.UpdateLayout();
                    Assert.Same(view, Detail(window));
                    Assert.True(timeline.IsExpanded);
                    AssertNoOverflow(view);
                }
            }
            vm.Navigate(AppPage.Incidents); vm.Navigate(AppPage.Detail); window.UpdateLayout();
            Assert.Same(view, Detail(window));
            Assert.True(timeline.IsExpanded);
            Assert.Equal(selected.Id, vm.Selected!.Incident.Id);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void KeyboardAndNames_KeepCommandsAndNestedRawDataAccessible()
    {
        var window = Open(Scenario("recurring"));
        try
        {
            var view = Detail(window);
            var timeline = view.FindControl<Expander>("FullTimeline")!;
            var toggle = timeline.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First();
            Assert.True(toggle.Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); window.UpdateLayout();
            Assert.True(timeline.IsExpanded);
            Assert.NotEmpty(AutomationProperties.GetName(timeline)!);
            var record = timeline.GetVisualDescendants().OfType<Expander>().Single();
            Assert.False(record.IsExpanded);
            record.IsExpanded = true; window.UpdateLayout();
            var xml = record.GetVisualDescendants().OfType<Expander>().Single();
            Assert.False(xml.IsExpanded);
            xml.IsExpanded = true; window.UpdateLayout();
            Assert.Contains(record.GetVisualDescendants().OfType<TextBox>(), item => item.Text?.Contains("<Event>", StringComparison.Ordinal) == true);
            var copy = view.FindControl<Button>("DetailCopy")!;
            Assert.DoesNotContain("primary-action", copy.Classes);
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(AppPage.Export, window.ViewModel.Page);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ChangeHeaders_DistinguishNotExaminedEmptyLimitedAndUnavailable()
    {
        var basis = Scenario("changes");
        var incident = basis.Incidents[0];
        var text = new LocalizationService();
        var summaries = new List<string>();
        foreach (var state in Enum.GetValues<CoverageState>())
        {
            var item = incident with { RelatedChanges = [], ChangeContext = incident.ChangeContext! with { Coverage = [new(SourceType.ChangeHistory, state, null, null, "synthetic", "ChangeSourceSetupApi")] } };
            var model = new IncidentDetailPresentation();
            model.Refresh(new IncidentRow(item, text, 1), basis, text, text.Get("LocalSystem"));
            summaries.Add(model.ChangeSummary);
            Assert.Contains(text.Get("Coverage" + state), string.Join(" ", model.ChangeCoverage), StringComparison.Ordinal);
        }
        Assert.NotEqual(summaries[0], summaries[1]);
        Assert.NotEqual(summaries[1], summaries[2]);
        var unavailable = new IncidentDetailPresentation();
        unavailable.Refresh(new IncidentRow(incident with { ChangeContext = null }, text, 1), basis, text, text.Get("LocalSystem"));
        Assert.Equal(text.Get("ChangesNotExamined"), unavailable.ChangeSummary);
    }

    [Fact]
    public void Projection_UnexaminedCoverageRemainsExplicitAndRelevantChangesSurviveUnavailableSummary()
    {
        var basis = Scenario("changes");
        var text = new LocalizationService();
        var model = new IncidentDetailPresentation();
        var incident = basis.Incidents[0];
        model.Refresh(new IncidentRow(incident, text, 1), basis with { Coverage = [] }, text, text.Get("LocalSystem"));
        Assert.True(model.HasNoCoverage);
        Assert.Contains(text.Get("CoverageNotChecked"), model.CoverageLimitations);
        incident = incident with { ChangeContext = incident.ChangeContext! with { Coverage = [new(SourceType.ChangeHistory, CoverageState.Unavailable, null, null, "synthetic", "ChangeSourceSetupApi")] } };
        model.Refresh(new IncidentRow(incident, text, 1), basis, text, text.Get("LocalSystem"));
        Assert.Contains(text.Format("DetailRelevantChanges", 1), model.ChangeSummary, StringComparison.Ordinal);
        Assert.Contains(text.Get("ChangesUnavailable"), model.ChangeSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignData_IsSyntheticAndDoesNotNeedDesktopServices()
    {
        var model = IncidentDetailDesignData.Sample;
        Assert.Single(model.TimelineRows);
        Assert.StartsWith("synthetic:", model.Incident.AnchorEvent.SourceReference, StringComparison.Ordinal);
        Assert.Equal(IncidentDetailDesignData.Result.Incidents[0].Id, model.Incident.Id);
    }

    [AvaloniaFact]
    public void BoundedPass2aVisualMatrix_RendersFortySyntheticCases()
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2A_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);
        var scenarios = new[] { "restart", "graphics", "application", "groups", "partial", "denied", "not-examined", "changes", "recurring", "imported" };
        var matrix = new[] { (1280, 900, "en", AppTheme.Light), (1280, 900, "en", AppTheme.Dark), (600, 1000, "de", AppTheme.Dark), (1920, 1080, "it", AppTheme.Light) };
        var index = 0;
        foreach (var scenario in scenarios)
        foreach (var (width, height, language, theme) in matrix)
        {
            var window = Open(Scenario(scenario), scenario == "imported");
            try
            {
                window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Language = language, Theme = theme });
                window.Width = width; window.Height = height; window.UpdateLayout();
                var view = Detail(window);
                AssertNoOverflow(view);
                if (scenario == "recurring") Assert.True(((IncidentDetailPresentation)view.DataContext!).HasPattern);
                var side = window.FindControl<Border>("NavigationSurface")!;
                var main = window.FindControl<Grid>("MainRegion")!;
                Assert.True(side.TranslatePoint(default, window)!.Value.X >= 0 && side.Bounds.Width >= 160 && main.Bounds.X >= side.Bounds.Right,
                    $"Shell overlaps: side={side.Bounds}, main={main.Bounds}, shell={window.FindControl<Grid>("Shell")!.Bounds}");
                Assert.Contains(((IncidentDetailPresentation)view.DataContext!).BestNextStep, VisibleText(view), StringComparison.Ordinal);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("AXAML view did not render.");
                Assert.True(frame.PixelSize.Width > 0);
                if (!string.IsNullOrWhiteSpace(output)) frame.Save(Path.Combine(output, $"{index:D2}-{scenario}-{language}-{theme}-{width}x{height}.png"), new PngBitmapEncoderOptions());
                index++;
            }
            finally { window.Close(); }
        }
        Assert.Equal(40, index);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "MainWindow owns and disposes its view model when Closed; every caller closes the window in finally.")]
    private static MainWindow Open(ScanResult result, bool imported = false)
    {
        var window = new MainWindow(new MainViewModel(new TestServices()));
        window.Width = 1280; window.Height = 900; window.Show();
        window.ViewModel.SetResult(result, imported);
        window.ViewModel.Select(window.ViewModel.AllRows.Single(row => row.Incident.Id == result.Incidents[0].Id));
        window.UpdateLayout();
        return window;
    }
    private static IncidentDetailView Detail(MainWindow window) => window.GetVisualDescendants().OfType<IncidentDetailView>().Single();
    private static string VisibleText(Control root) => string.Join(" ", root.GetVisualDescendants().OfType<TextBlock>().Where(item => item.IsVisible).Select(item => item.Text));
    private static void AssertNoOverflow(Control root) => Assert.All(root.GetVisualDescendants().OfType<ScrollViewer>(), viewer =>
        Assert.True(viewer.Viewport.Width == 0 || viewer.Extent.Width <= viewer.Viewport.Width + 2, "Horizontal overflow in Incident Detail"));

    internal static ScanResult Scenario(string name)
    {
        var result = SyntheticResults.Create(name == "recurring" ? 3 : 1);
        var incident = result.Incidents[0];
        if (name == "restart") return IncidentDetailDesignData.Result;
        if (name == "application")
        {
            var finding = incident.Findings[0] with { RuleId = "application.crash", ObservedKey = "rule.application.crash.observed", InterpretationKey = "rule.application.crash.interpretation", NotEstablishedKey = "rule.application.crash.not_established", RecommendedActionKeys = ["action.ApplicationCrash.investigate"] };
            incident = incident with { Category = IncidentCategory.ApplicationCrash, Findings = [finding] };
        }
        if (name == "changes")
        {
            var change = new SystemChange("synthetic-change", "Windows", incident.StartTimeUtc.AddMinutes(-4), ChangeCategory.DriverInstalled, "ChangeSourceSetupApi", "Synthetic display driver", ChangeSubsystem.Display, "1.0", "2.0", "Synthetic vendor", "synthetic-reference");
            incident = incident with
            {
                RelatedChanges = [new(change, ContextualRelevance.High, ChangeTiming.Before, TimeSpan.FromMinutes(-4), "ChangeReasonSameSubsystem"), new(change with { Id = "after", TimestampUtc = incident.StartTimeUtc.AddMinutes(2) }, ContextualRelevance.Low, ChangeTiming.After, TimeSpan.FromMinutes(2), "ChangeReasonAfter")],
                ChangeContext = new(incident.StartTimeUtc, FirstObservationBasis.CurrentScan, false, [new(SourceType.ChangeHistory, CoverageState.Partial, incident.StartTimeUtc.AddDays(-1), incident.StartTimeUtc, "ChangeCoveragePartial", "ChangeSourceSetupApi")])
            };
        }
        if (name == "groups") incident = incident with { Evidence = [.. incident.Evidence, .. Enumerable.Range(0, 5).Select(index => new Evidence(EvidenceKind.Unknown, "Wer", "evidence.source.unknown", "synthetic-extra-" + index))] };
        result = result with { Incidents = [incident, .. result.Incidents.Skip(1)] };
        if (name == "partial") result = result with { Coverage = [new(SourceType.EventLog, CoverageState.Partial, result.StartedUtc, result.FinishedUtc, "synthetic", "System")] };
        if (name == "denied") result = result with { Coverage = [new(SourceType.CrashArtifact, CoverageState.AccessDenied, null, null, "synthetic")] };
        if (name == "imported") result = result with { Coverage = [new(SourceType.Imported, CoverageState.Partial, result.StartedUtc, result.FinishedUtc, "synthetic")] };
        return result;
    }
}
