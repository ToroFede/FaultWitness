using System.Xml.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Presentation;
using FaultWitness.Core;
using FaultWitness.Localization;
using FaultWitness.Rules;

namespace FaultWitness.UI.Tests;

public sealed class IncidentExplainabilityTests
{
    internal static readonly string[] Locales = ["en", "it", "es", "fr", "de", "pt", "ru", "pl"];
    private static readonly string[] Keys = ["IncidentProcessValue", "IncidentServiceValue", "IncidentComponentValue",
        "IncidentIdentityUnknown", "IncidentRecordedAt", "BackgroundProcessGuidance", "MaximumAnalysisRange",
        "AnalysisCompleteLimited", "QuietAnalysisComplete", "BackgroundOnlyExplanation", "QuietResultCaution"];

    [Fact]
    public void EveryCategoryKeepsItsLocalizedDomainLabelAndAllEightResourcesMatch()
    {
        var resources = Path.Combine(AppContext.BaseDirectory, "resources");
        var english = Read(Path.Combine(resources, "Strings.resx"));
        foreach (var locale in Locales)
        {
            var text = Text(locale);
            var localized = Read(Path.Combine(resources, locale == "en" ? "Strings.resx" : "Strings." + locale + ".resx"));
            Assert.True(english.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(localized.Keys));
            foreach (var key in Keys)
            {
                Assert.Equal(localized[key], text.Get(key));
                Assert.False(string.IsNullOrWhiteSpace(localized[key]));
                Assert.Equal(english[key].Contains("{0}", StringComparison.Ordinal), localized[key].Contains("{0}", StringComparison.Ordinal));
                if (locale != "en") Assert.NotEqual(english[key], localized[key]);
            }
            foreach (var category in Enum.GetValues<IncidentCategory>())
            {
                var incident = Pass2FFixtures.Result().Incidents[0] with { Category = category };
                var identity = IncidentIdentity.From(incident, text);
                Assert.Equal(localized["Category" + category], identity.Type);
                Assert.NotEqual("Category" + category, identity.Type);
            }
            Assert.Contains("90", text.Get("MaximumAnalysisRange"), StringComparison.Ordinal);
            Assert.Contains("90", text.Get("InvalidTimeRange"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void IdentityUsesAnchorFactsWithoutAttributingNearbyProcessesOrFaultingModules()
    {
        var text = Text("en");
        var result = Pass2FFixtures.Result();
        var crash = result.Incidents.Single(item => item.Category == IncidentCategory.ApplicationCrash && item.AnchorEvent.Process is not null);
        Assert.Equal("Application / process in record: sample.exe", IncidentIdentity.From(crash, text).Subject);
        Assert.DoesNotContain("sample.dll", IncidentIdentity.From(crash, text).Subject, StringComparison.Ordinal);
        var unknown = result.Incidents.Single(item => item.Category == IncidentCategory.ApplicationCrash && item.AnchorEvent.Process is null);
        var nearby = unknown with { SourceEvents = [unknown.AnchorEvent, crash.AnchorEvent] };
        Assert.Equal(text.Get("IncidentIdentityUnknown"), IncidentIdentity.From(nearby, text).Subject);
        var service = crash with { Category = IncidentCategory.Service, AnchorEvent = crash.AnchorEvent with
            { Process = null, Fields = new Dictionary<string, string> { ["ServiceName"] = "SampleService" } } };
        Assert.Equal("Service in record: SampleService", IncidentIdentity.From(service, text).Subject);
        var device = crash with { Category = IncidentCategory.Storage, AnchorEvent = crash.AnchorEvent with { Process = null, Device = @"\Device\RaidPort0" } };
        Assert.Equal(@"Component in record: \Device\RaidPort0", IncidentIdentity.From(device, text).Subject);
    }

    [Fact]
    public void ProvenanceAndWhyShownPreserveExactRecordsAndCausalLimits()
    {
        var text = Text("en");
        var result = Pass2FFixtures.Result();
        foreach (var incident in result.Incidents)
        {
            var row = new IncidentRow(incident, text, 1);
            var detail = new IncidentDetailPresentation();
            detail.Refresh(row, result, text, "Synthetic illustrative data");
            Assert.Same(incident, detail.Incident);
            Assert.Contains(incident.AnchorEvent.Provider, detail.Source, StringComparison.Ordinal);
            Assert.Contains(incident.AnchorEvent.SourceReference, detail.TechnicalRows.Single(item => item.Source == incident.AnchorEvent).Technical, StringComparison.Ordinal);
            Assert.Contains(incident.AnchorEvent.TimestampUtc.ToLocalTime().ToString("G", text.Culture), detail.RecordedAt, StringComparison.Ordinal);
            Assert.Equal(row.Assessment, detail.ObservedSummary);
            Assert.Equal(incident.Findings.Where(item => item.Disposition != FindingDisposition.Suppressed).Select(item => text.Get(item.NotEstablishedKey)).Distinct(), detail.Limitations);
            Assert.DoesNotContain("caused your", detail.Subject + detail.Source + detail.ObservedSummary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(detail.Subject, row.AccessibleName, StringComparison.Ordinal);
        }
        var wer = result.Incidents.Single(item => item.Category == IncidentCategory.Graphics).AnchorEvent with { SourceType = SourceType.Wer, Channel = null };
        Assert.Empty(IncidentIdentity.From(result.Incidents[0] with { AnchorEvent = wer }, text).EventIdentity);
        Assert.Contains("1000", IncidentIdentity.From(result.Incidents.Single(item => item.Category == IncidentCategory.ApplicationCrash && item.AnchorEvent.Process is not null), text).EventIdentity, StringComparison.Ordinal);
        Assert.Contains("does not establish", text.Get("BackgroundProcessGuidance"), StringComparison.Ordinal);
    }

    [AvaloniaTheory]
    [InlineData(CoverageState.Complete)][InlineData(CoverageState.Partial)][InlineData(CoverageState.AccessDenied)]
    [InlineData(CoverageState.Unavailable)][InlineData(CoverageState.NotSupported)]
    public async Task CompletedQuietResultUsesSavedQueryNotChangedInputsAndExposesCoverage(CoverageState state)
    {
        var services = new TestServices { Result = Pass2FFixtures.Result(state) with { Incidents = [] } };
        using var vm = new MainViewModel(services) { Period = AnalysisPeriod.Custom,
            CustomFrom = Pass2FFixtures.End.AddDays(-90), CustomTo = Pass2FFixtures.End };
        await vm.AnalyzeAsync();
        var quiet = QuietResultProjection.Create(vm, "NoSupportedIncidents", false);
        Assert.Contains(state == CoverageState.Complete ? vm.Text.Get("QuietAnalysisComplete") : vm.Text.Get("AnalysisCompleteLimited"), quiet.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", quiet.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("{1}", quiet.Title, StringComparison.Ordinal);
        Assert.Contains(services.From.ToLocalTime().ToString("g", vm.Text.Culture), quiet.Title, StringComparison.Ordinal);
        Assert.Contains(services.To.ToLocalTime().ToString("g", vm.Text.Culture), quiet.Title, StringComparison.Ordinal);
        vm.CustomFrom = DateTimeOffset.Now.AddDays(-1); vm.CustomTo = DateTimeOffset.Now; vm.Period = AnalysisPeriod.Day;
        Assert.Equal(quiet, QuietResultProjection.Create(vm, "NoSupportedIncidents", false));
        Assert.Equal(state != CoverageState.Complete, quiet.Coverage.Length > 0);
        if (state != CoverageState.Complete) Assert.Contains(vm.Text.Get("Coverage" + state), quiet.Coverage, StringComparison.Ordinal);
        Assert.Contains("outside the supported records", quiet.Caution, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ActualCanonicalMaximumAcceptsExactly90DaysAndRejectsAnyExcess()
    {
        var services = new TestServices();
        using var vm = new MainViewModel(services) { Period = AnalysisPeriod.Custom,
            CustomFrom = Pass2FFixtures.End.AddDays(-90), CustomTo = Pass2FFixtures.End };
        await vm.AnalyzeAsync();
        Assert.Equal(1, services.AnalysisCalls);
        Assert.Equal(TimeSpan.FromDays(90), services.To - services.From);
        vm.CustomFrom = vm.CustomFrom.AddTicks(-1);
        await vm.AnalyzeAsync();
        Assert.Equal(1, services.AnalysisCalls);
        Assert.Equal("InvalidTimeRange", vm.StatusKey);
        Assert.Contains("90", vm.Text.Get(vm.StatusKey), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void UnknownCoverageAndBackgroundContextNeverBecomeConfidentAbsenceOrFailure()
    {
        using var vm = new MainViewModel(new TestServices());
        var empty = Pass2FFixtures.Result() with { Incidents = [], Coverage = [] };
        vm.SetResult(empty);
        var quiet = QuietResultProjection.Create(vm, "NoSupportedIncidents", false);
        Assert.Equal(vm.Text.Get("CoverageNotChecked"), quiet.Coverage);
        Assert.Contains(vm.Text.Get("HistoryPeriodUnavailable"), quiet.Title, StringComparison.Ordinal);
        vm.SetResult(Pass2FFixtures.Result() with { Incidents = [Pass2FFixtures.Background()] });
        Assert.Equal(AttentionLevel.Background, Assert.Single(vm.AllRows).Priority);
        var background = QuietResultProjection.Create(vm, "NoPriorityIncidents", true);
        Assert.Contains(vm.Text.Get("BackgroundOnlyExplanation"), background.Caution, StringComparison.Ordinal);
        Assert.Contains("Background context: 1", background.BackgroundCount, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void IdentityReadOrderAndQuietContentDoNotAcquireLiveRegionOwnership()
    {
        using var vm = new MainViewModel(new TestServices());
        var window = new MainWindow(vm);
        window.Show();
        try
        {
            vm.SetResult(Pass2FFixtures.Result()); vm.Select(vm.AllRows[0]); CaptureAxamlTests.Settle(window);
            var subject = window.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Name == "IncidentSubject");
            var siblings = ((StackPanel)subject.Parent!).Children.Select(item => item.Name).ToArray();
            Assert.True(Array.IndexOf(siblings, "IncidentSubject") < Array.IndexOf(siblings, "IncidentRecordedAt"));
            Assert.True(Array.IndexOf(siblings, "IncidentRecordedAt") < Array.IndexOf(siblings, "IncidentSource"));
            vm.SetResult(Pass2FFixtures.Result() with { Incidents = [] }); vm.Navigate(AppPage.Home); CaptureAxamlTests.Settle(window);
            var quiet = window.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "QuietResult");
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(quiet));
            Assert.All(quiet.GetVisualDescendants().OfType<TextBlock>(), item => Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(item)));
        }
        finally { window.Close(); }
    }

    private static Dictionary<string, string> Read(string file) => XDocument.Load(file).Root!.Elements("data")
        .ToDictionary(item => (string)item.Attribute("name")!, item => item.Element("value")!.Value, StringComparer.Ordinal);
    private static LocalizationService Text(string locale) { var text = new LocalizationService(); text.SetCulture(locale); return text; }
}

internal static class Pass2FFixtures
{
    internal static readonly DateTimeOffset End = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    internal static ScanResult Result(CoverageState state = CoverageState.Complete)
    {
        var time = End.AddDays(-10);
        NormalizedEvent Record(int offset, string channel, string provider, int id, string? process, Dictionary<string, string>? fields = null) =>
            new(Guid.NewGuid(), SourceType.EventLog, "Windows", time.AddHours(offset), channel, provider, id, 0,
                IncidentSeverity.Medium, process, null, process is null ? null : "sample.dll", null, fields ?? [],
                "synthetic illustrative record:" + offset, "<Event>Illustrative sample only</Event>");
        var records = new[] {
            Record(0, "Application", "Application Error", 1000, "sample.exe"),
            Record(1, "Application", "Application Error", 1000, null),
            Record(2, "System", "Microsoft-Windows-Kernel-Power", 41, null, new() { ["BugcheckCode"] = "0" }),
            Record(3, "Application", "Windows Error Reporting", 1001, null, new() { ["EventName"] = "LiveKernelEvent", ["P1"] = "141" }),
            Record(4, "System", "Service Control Manager", 7031, null, new() { ["ServiceName"] = "SampleService" }) };
        var coverage = new[] { new SourceCoverage(SourceType.EventLog, state, End.AddDays(-90), End, "synthetic", "System"),
            new SourceCoverage(SourceType.EventLog, CoverageState.Complete, End.AddDays(-90), End, "synthetic", "Application") };
        return new IncidentAnalyzer(RuleCatalog.CreateDefault()).Analyze(new EventBatch(records, coverage), End.AddSeconds(-1), End);
    }
    internal static Incident Background() => Result().Incidents.Single(item => item.Category == IncidentCategory.Service);
}
