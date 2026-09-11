using FaultWitness.App;
using FaultWitness.Core;
using FaultWitness.Localization;
using FaultWitness.Storage;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "ViewModel")]
public sealed class PresentationRegressionTests
{
    [Fact]
    public void HistoryRow_AccessibleFallbackIsUserFacing()
    {
        var scan = new StoredScan("history", DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, "0.9.0", null, []);
        var row = new HistoryRow(scan, new LocalizationService());
        Assert.Contains(row.Title, row.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(HistoryRow), row.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("FaultWitness.App", row.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void IncidentAccessibleName_ContainsHumanSummaryInsteadOfRuntimeTypeName()
    {
        var row = new IncidentRow(SyntheticResults.Create(1).Incidents[0], new LocalizationService(), 1);
        Assert.Contains(row.Title, row.ToString(), StringComparison.Ordinal); Assert.Contains(row.Timestamp, row.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("FaultWitness.App.IncidentRow", row.ToString(), StringComparison.Ordinal);
    }
    private static Incident Item(string rule, IncidentSeverity severity = IncidentSeverity.Medium, EvidenceStrength strength = EvidenceStrength.Moderate,
        FindingDisposition disposition = FindingDisposition.Significant, IncidentCategory category = IncidentCategory.Graphics)
    {
        var item = SyntheticResults.Create(1).Incidents[0];
        return item with { Category = category, Severity = severity, Signature = Guid.NewGuid().ToString(),
            Findings = [item.Findings[0] with { RuleId = rule, Severity = severity, Strength = strength, Disposition = disposition }] };
    }
    [Theory]
    [InlineData("graphics.engine_timeout")][InlineData("graphics.adapter_timeout")]
    [InlineData("application.crash")][InlineData("application.crash_with_wer")]
    [InlineData("audio.audiodg_crash")][InlineData("storage.request_timeout")]
    public void IsolatedModerateSignal_DoesNotAutomaticallyBecomeWorthKnowing(string rule)
        => Assert.Equal(AttentionLevel.Background, PresentationPolicy.Classify(Item(rule)));

    [Theory]
    [InlineData("graphics.tdr_with_driver_event")][InlineData("graphics.repeated_timeout_pattern")]
    [InlineData("application.repeated_signature")][InlineData("storage.repeated_timeout_pattern")]
    [InlineData("service.repeated_failure")][InlineData("resources.exhaustion_with_hang_or_crash")]
    public void SupportedActionablePattern_IsWorthKnowing(string rule)
        => Assert.Equal(AttentionLevel.Knowing, PresentationPolicy.Classify(Item(rule)));

    [Fact]
    public void NearbyAppCrash_IsLimitedTemporalContext_NotIndependentGraphicsConfirmation()
    {
        var graphics = Item("graphics.engine_timeout");
        graphics = graphics with { Findings = [.. graphics.Findings, graphics.Findings[0] with { RuleId = "graphics.tdr_with_app_crash", Strength = EvidenceStrength.Limited }] };
        Assert.Equal(AttentionLevel.Background, PresentationPolicy.Classify(graphics));
    }
    [Fact]
    public void LowSeverityOrLimitedPattern_RemainsBackgroundEvenWhenRepeated()
    {
        Assert.Equal(AttentionLevel.Background, PresentationPolicy.Classify(Item("pnp.recurrent_device_failure", IncidentSeverity.Low, EvidenceStrength.Limited), 100));
        Assert.Equal(AttentionLevel.Background, PresentationPolicy.Classify(Item("graphics.repeated_timeout_pattern", strength: EvidenceStrength.Limited), 100));
    }
    [Fact]
    public void UncleanShutdown_IsWorthKnowingWithoutClaimingItsCause()
        => Assert.Equal(AttentionLevel.Knowing, PresentationPolicy.Classify(Item("power.unclean_shutdown", strength: EvidenceStrength.Limited, category: IncidentCategory.Power)));

    [Fact]
    public void CorroboratingProvenance_DoesNotPromoteOneApplicationCrash()
    {
        var item = Item("application.crash");
        item = item with { Evidence = [.. item.Evidence, .. item.Evidence, .. item.Evidence] };
        Assert.Equal(AttentionLevel.Background, PresentationPolicy.Classify(item));
    }
    [Fact]
    public void LargeReviewShape_PrioritizesMeaningfulPatternsWithoutDeletingAnyOccurrence()
    {
        var items = new List<Incident>();
        for (var index = 0; index < 1145; index++)
        {
            var gfx = Item(index < 178 ? "graphics.adapter_timeout" : "graphics.engine_timeout");
            if (index < 190) gfx = gfx with { Findings = [.. gfx.Findings, gfx.Findings[0] with { RuleId = "graphics.tdr_with_app_crash", Strength = EvidenceStrength.Limited }] };
            items.Add(gfx);
        }
        for (var index = 0; index < 26; index++) items.Add(Item(index < 15 ? "application.repeated_signature" : "application.crash", category: IncidentCategory.ApplicationCrash));
        var audio = Enumerable.Range(0, 14).Select(_ => Item("audio.audiodg_crash", category: IncidentCategory.Audio)).ToArray(); items.AddRange(audio);
        items.AddRange(Enumerable.Range(0, 3).Select(_ => Item("application.hang", IncidentSeverity.Low, EvidenceStrength.Limited)));
        items.AddRange(Enumerable.Range(0, 26).Select(_ => Item("pnp.umdf_transient_load_warning", disposition: FindingDisposition.Context)));
        items.AddRange(Enumerable.Range(0, 12).Select(_ => Item("power.planned_restart", disposition: FindingDisposition.Expected)));
        items.Add(Item("power.unclean_shutdown", strength: EvidenceStrength.Limited)); items.Add(Item("service.unexpected_termination", disposition: FindingDisposition.Supporting));
        var result = SyntheticResults.Create(0) with { Incidents = items, Patterns = [new("verified-audio", IncidentCategory.Audio, audio.Select(item => item.Id).ToArray())] };
        using var vm = new MainViewModel(new TestServices()); vm.SetResult(result);
        Assert.Equal(1228, vm.AllRows.Count); Assert.Equal(0, vm.AttentionCount); Assert.Equal(30, vm.KnowingCount); Assert.Equal(1198, vm.BackgroundCount);
        Assert.All(vm.RecentSignificant, row => Assert.NotEqual(AttentionLevel.Background, row.Priority));
        Assert.Same(result, vm.Result);
    }
    [Theory]
    [InlineData(100)][InlineData(1000)][InlineData(5000)]
    [Trait("Suite", "LargeData")]
    public void ThousandsOfSimilarButUnconfirmedGraphicsRecords_StaySearchableInBackground(int count)
    {
        var items = Enumerable.Range(0, count).Select(_ => Item("graphics.engine_timeout")).ToArray();
        using var vm = new MainViewModel(new TestServices()); vm.SetResult(SyntheticResults.Create(0) with { Incidents = items });
        Assert.Equal(count, vm.BackgroundCount); Assert.Empty(vm.RecentSignificant); vm.ShowPriority(AttentionLevel.Background);
        Assert.Equal(count, vm.FilteredRows.Count);
    }
    [Fact]
    public void SeparateGraphicsOccurrencesInOneMinute_ShowSecondsAndRemainSeparate()
    {
        var first = Item("graphics.engine_timeout"); var second = first with { Id = Guid.NewGuid(), StartTimeUtc = first.StartTimeUtc.AddSeconds(7) };
        using var vm = new MainViewModel(new TestServices()); vm.SetResult(SyntheticResults.Create(0) with { Incidents = [first, second] });
        Assert.Equal(2, vm.AllRows.Count); Assert.NotEqual(vm.AllRows[0].Timestamp, vm.AllRows[1].Timestamp);
        Assert.All(vm.AllRows, row => Assert.Equal(1, row.RecurrenceCount));
    }
    [Fact]
    public void UnknownEvidence_IdentifiesDifferentSourcesAndCoverageStates()
    {
        var text = new LocalizationService();
        SourceCoverage[] coverage = [new(SourceType.Wer, CoverageState.Partial, null, null, ""), new(SourceType.CrashArtifact, CoverageState.AccessDenied, null, null, "")];
        var wer = EvidencePresentation.Describe(new(EvidenceKind.Unknown, "Wer.", "evidence.source.unknown", "synthetic"), coverage, text);
        var dump = EvidencePresentation.Describe(new(EvidenceKind.Unknown, "CrashArtifact.", "evidence.source.unknown", "synthetic"), coverage, text);
        Assert.Contains(text.Get("SourceWer"), wer, StringComparison.Ordinal); Assert.Contains(text.Get("CoveragePartial"), wer, StringComparison.Ordinal);
        Assert.Contains(text.Get("SourceArtifacts"), dump, StringComparison.Ordinal); Assert.Contains(text.Get("CoverageAccessDenied"), dump, StringComparison.Ordinal);
        Assert.NotEqual(wer, dump);
    }
    [Fact]
    public void SupportSummary_DoesNotCollapseUnknownSourcesIntoOneGenericMessage()
    {
        var result = SyntheticResults.Create(1); var incident = result.Incidents[0] with { Evidence = [
            new(EvidenceKind.Unknown, "Wer.", "evidence.source.unknown", "synthetic-wer"),
            new(EvidenceKind.Unknown, "CrashArtifact.", "evidence.source.unknown", "synthetic-dump")] };
        var summary = FaultWitness.Export.ReportExporter.ToSupportMarkdown(result with { Incidents = [incident] }, new LocalizationService(), false, "test", "test");
        var text = new LocalizationService();
        Assert.Contains(text.Get("EvidenceUnknown") + ": " + text.Get("SourceTypeWer"), summary, StringComparison.Ordinal);
        Assert.Contains(text.Get("EvidenceUnknown") + ": " + text.Get("SourceTypeCrashArtifact"), summary, StringComparison.Ordinal);
    }
    [Fact]
    public void UnknownOutsideCoveredInterval_IsNotRelabelledComplete()
    {
        var description = EvidencePresentation.Describe(new(EvidenceKind.Unknown, "Wer.", "evidence.source.unknown", "synthetic"),
            [new(SourceType.Wer, CoverageState.Complete, null, null, "")], new LocalizationService());
        Assert.Contains("not established", description, StringComparison.Ordinal); Assert.DoesNotContain("Complete", description, StringComparison.Ordinal);
    }
    [Fact]
    public void ObservedEvidence_UsesSupportedEventMeaningInsteadOfGenericRecordText()
    {
        var record = SyntheticResults.Create(1).Incidents[0].AnchorEvent with { Provider = "Application Error", Channel = "Application", EventId = 1000 };
        var description = EvidencePresentation.Describe(new(EvidenceKind.Positive, "EventLog.Application", "evidence.recorded", "synthetic", record), [], new LocalizationService());
        Assert.Contains("application crash", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Example.exe", description, StringComparison.Ordinal); Assert.DoesNotContain("A source record", description, StringComparison.Ordinal);
    }
    [Fact]
    public void WindowsProductDisplay_UsesAuthoritativeCaptionWithoutBuildNumberGuessing()
    {
        var info = new WindowsProductInformation("Microsoft Windows 11 Pro", "24H2", "26100", "10.0.26100");
        var values = info.ApplyTo(new Dictionary<string,string> { ["OperatingSystem"] = "Microsoft Windows 10.0.26100" });
        Assert.Equal("Microsoft Windows 11 Pro", values["OperatingSystem"]); Assert.Equal("24H2", values["DisplayVersion"]);
        Assert.Equal("26100", values["BuildNumber"]); Assert.Equal("10.0.26100", values["NtVersion"]);
        var unknown = new WindowsProductInformation(null, null, "26100", "10.0.26100").ApplyTo(new Dictionary<string,string>());
        Assert.False(unknown.ContainsKey("OperatingSystem"));
    }
    [Fact]
    public void SharedReportBeyondCoreOccurrenceWindow_IsLabelledButNeverMergedOrCountedAsRecurrence()
    {
        var first = Item("graphics.engine_timeout");
        first = first with { AnchorEvent = first.AnchorEvent with { Fields = new Dictionary<string,string> { ["ReportId"] = "synthetic-shared-report" } } };
        var second = first with { Id = Guid.NewGuid(), StartTimeUtc = first.StartTimeUtc.AddMinutes(10), Signature = "different-record" };
        using var vm = new MainViewModel(new TestServices()); vm.SetResult(SyntheticResults.Create(0) with { Incidents = [first, second] });
        Assert.Equal(2, vm.AllRows.Count);
        Assert.All(vm.AllRows, row => { Assert.Equal(2, row.SharedReportCount); Assert.Equal(1, row.RecurrenceCount); Assert.NotEmpty(row.SharedReport); });
        Assert.Equal(2, vm.BackgroundCount);
    }
    [Theory]
    [InlineData("")][InlineData("00000000-0000-0000-0000-000000000000")][InlineData("<redacted>")]
    public void UnknownReportIdentifier_DoesNotCreateAFalseRelationship(string reportId)
    {
        var item = Item("graphics.engine_timeout");
        item = item with { AnchorEvent = item.AnchorEvent with { Fields = new Dictionary<string,string> { ["ReportId"] = reportId } } };
        Assert.Null(PresentationPolicy.ReportReferenceKey(item));
    }
}
