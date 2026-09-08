using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

public sealed class TemporalBoundaryTests
{
    [Theory]
    [InlineData(119, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void Tdr_AppCrash_ClosedTwoMinuteWindow(int seconds, bool expected) =>
        Check("tdr_with_app_crash", "graphics.tdr_with_app_crash", 0, 1, seconds, expected);

    [Theory]
    [InlineData(119, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void Tdr_DriverEvent_ClosedTwoMinuteWindow(int seconds, bool expected) =>
        Check("nvidia_tdr_141", "graphics.tdr_with_driver_event", 1, 0, -seconds, expected);

    [Theory]
    [InlineData(119, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    [InlineData(-1, false)]
    public void Exhaustion_OnlyFollowingHangWithinWindow(int seconds, bool expected) =>
        Check("resource_exhaustion_then_hang", "resources.exhaustion_with_hang_or_crash", 0, 1, seconds, expected);

    [Theory]
    [InlineData(119, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void KernelPower_BugcheckReport_ClosedTwoMinuteWindow(int seconds, bool expected) =>
        Check("kernel41_with_bugcheck", "power.bugcheck_reboot", 0, 1, seconds, expected);

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void PlannedRestart_UpdateMustPrecedeWithinFiveMinutes(int seconds, bool expected) =>
        Check("update_planned_restart", "power.update_restart", 0, 1, -seconds, expected);

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void CorrectedWhea_RepetitionWindow(int seconds, bool expected) =>
        Check("whea_recurrent_corrected", "hardware.whea.recurrent_corrected", 0, 1, seconds, expected);

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void Storage_RepetitionWindow(int seconds, bool expected) =>
        Check("storage_153_repeated_same_device", "storage.repeated_timeout_pattern", 0, 1, seconds, expected);

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void Pnp_RepetitionWindow(int seconds, bool expected) =>
        Check("pnp_219_recurrent", "pnp.recurrent_device_failure", 0, 1, seconds, expected);

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void Bugcheck_DumpMustMatchInsideWindow(int seconds, bool expected) =>
        Check("bugcheck_with_dump", "system.bugcheck_with_dump", 0, 1, seconds, expected);

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void Service_FutureCrashUsesAnchorNotChainedWindow(int seconds, bool expected)
    {
        var fixture = DiagnosticFixture.Load("service_repeated_before_incident");
        fixture.Events[1] = fixture.Events[1] with { TimestampUtc = fixture.Events[0].TimestampUtc.AddSeconds(1) };
        Check(fixture, "service.repeated_failure", 0, 2, seconds, expected);
    }

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void Reboot_WheaLookbackIsClosedAndNotCausal(int seconds, bool expected)
    {
        var fixture = DiagnosticFixture.Load("kernel41_with_whea_before");
        fixture.Events[0] = fixture.Events[0] with { TimestampUtc = fixture.Events[1].TimestampUtc.AddSeconds(-seconds) };
        var reboot = fixture.Analyze().Incidents.Single(item => item.Category == IncidentCategory.Power);
        Assert.Equal(expected, reboot.SourceEvents.Any(DiagnosticFacts.IsWhea));
        Assert.DoesNotContain(reboot.Relations, relation => relation.Kind.ToString() == "Causes");
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void SameReport_DeduplicationHasThirtySecondCap(int seconds, bool expected) =>
        Check("application_crash_with_wer", "application.crash_with_wer", 0, 1, seconds, expected);

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void Pnp_SuccessOnlySuppressesWithinClosedFiveMinutes(int seconds, bool suppressed)
    {
        var fixture = DiagnosticFixture.Load("pnp_219_benign");
        fixture.Events[1] = fixture.Events[1] with { TimestampUtc = fixture.Events[0].TimestampUtc.AddSeconds(seconds) };
        var finding = fixture.Analyze().Incidents.SelectMany(item => item.Findings).Single(item => item.RuleId == "pnp.umdf_transient_load_warning");
        Assert.Equal(suppressed ? FindingDisposition.Suppressed : FindingDisposition.Context, finding.Disposition);
    }

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void Service_PostRebootContextHasClosedFiveMinuteWindow(int seconds, bool context)
    {
        var fixture = DiagnosticFixture.Load("service_failure_after_reboot");
        fixture.Events[1] = fixture.Events[1] with { TimestampUtc = fixture.Events[0].TimestampUtc.AddSeconds(seconds) };
        var finding = fixture.Analyze().Incidents.SelectMany(item => item.Findings).Single(item => item.RuleId == "service.unexpected_termination");
        Assert.Equal(context ? FindingDisposition.Context : FindingDisposition.Supporting, finding.Disposition);
    }

    [Theory]
    [InlineData(299, false)]
    [InlineData(300, true)]
    [InlineData(301, true)]
    public void NoBugcheckClaim_RequiresEntireClosedCoverageWindow(int coveredSeconds, bool permitted)
    {
        var fixture = DiagnosticFixture.Load("kernel41_only");
        var stamp = fixture.Events[0].TimestampUtc;
        fixture.Coverage[0] = fixture.Coverage[0] with { ExaminedFromUtc = stamp.AddSeconds(-coveredSeconds), ExaminedToUtc = stamp.AddSeconds(coveredSeconds) };
        Assert.Equal(permitted, fixture.Analyze().Incidents.SelectMany(item => item.Findings).Any(item => item.RuleId == "power.abrupt_restart_no_bugcheck"));
    }

    [Theory]
    [InlineData(299, false)]
    [InlineData(300, false)]
    [InlineData(301, true)]
    public void NoBugcheckClaim_ChecksAllReportsInItsFullCoverageWindow(int seconds, bool permitted)
    {
        var fixture = DiagnosticFixture.Load("kernel41_only");
        var report = DiagnosticFixture.Load("other_bugcheck").Events[0];
        fixture.Events.Add(report with { TimestampUtc = fixture.Events[0].TimestampUtc.AddSeconds(seconds) });
        Assert.Equal(permitted, fixture.Analyze().Incidents.SelectMany(item => item.Findings).Any(item => item.RuleId == "power.abrupt_restart_no_bugcheck"));
    }

    private static void Check(string fixture, string rule, int anchor, int supporting, int seconds, bool expected) =>
        Check(DiagnosticFixture.Load(fixture), rule, anchor, supporting, seconds, expected);

    private static void Check(DiagnosticFixture fixture, string rule, int anchor, int supporting, int seconds, bool expected)
    {
        var source = fixture.Events[anchor];
        fixture.Events[supporting] = fixture.Events[supporting] with { TimestampUtc = source.TimestampUtc.AddSeconds(seconds) };
        var findings = fixture.Analyze().Incidents.Where(item => item.AnchorEvent.Id == source.Id).SelectMany(static item => item.Findings);
        Assert.Equal(expected, findings.Any(item => item.RuleId == rule));
    }
}
