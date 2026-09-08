using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

// These boundaries test optional context selection, not whether the single anchor occurred.
public sealed class ContextBoundaryTests
{
    [Theory]
    [InlineData("power.planned_restart", "update_planned_restart")]
    public void RequestedRestart_UpdateContextStopsAtFiveMinutes(string ruleId, string scenario) =>
        Check(ruleId, scenario, 300, "update");

    [Theory]
    [InlineData("graphics.engine_timeout", "nvidia_tdr_141")]
    [InlineData("graphics.adapter_timeout", "tdr_117")]
    [InlineData("graphics.repeated_timeout_pattern", "graphics_repeated_days")]
    public void Graphics_OptionalCrashContextStopsAtTwoMinutes(string ruleId, string scenario) =>
        Check(ruleId, scenario, 120, "crash");

    [Theory]
    [InlineData("hardware.whea.corrected", "whea_corrected")]
    [InlineData("hardware.whea.uncorrected", "whea_processor_cache")]
    [InlineData("hardware.whea.processor_mce", "whea_processor_cache")]
    [InlineData("hardware.whea.pcie", "whea_pcie")]
    [InlineData("hardware.whea.memory", "whea_memory")]
    public void Whea_ExactSignatureContextStopsAtFiveMinutes(string ruleId, string scenario) =>
        Check(ruleId, scenario, 300, "clone");

    [Theory]
    [InlineData("hardware.whea_bugcheck_124", "whea_bugcheck_124")]
    [InlineData("system.bugcheck", "other_bugcheck")]
    public void Bugcheck_PrecedingWheaContextStopsAtFiveMinutes(string ruleId, string scenario) =>
        Check(ruleId, scenario, 300, "whea");

    [Theory]
    [InlineData("application.crash", "application_crash")]
    [InlineData("application.third_party_faulting_module", "third_party_fault_module")]
    [InlineData("application.repeated_signature", "same_signature_different_pid")]
    [InlineData("application.hang", "application_hang")]
    [InlineData("audio.audiodg_crash", "audiodg_windows_module")]
    [InlineData("audio.audiodg_third_party_module", "audiodg_third_party_apo")]
    [InlineData("audio.repeated_apo_failure", "audiodg_repeated_apo")]
    public void ApplicationAndAudio_PrecedingExhaustionStopsAtTwoMinutes(string ruleId, string scenario) =>
        Check(ruleId, scenario, 120, "exhaustion");

    [Theory]
    [InlineData("storage.request_timeout", "storage_129")]
    [InlineData("storage.io_retry", "storage_153_single")]
    [InlineData("storage.io_warning", "storage_51_single")]
    public void Storage_SameDeviceContextStopsAtFiveMinutes(string ruleId, string scenario) =>
        Check(ruleId, scenario, 300, "clone");

    [Theory]
    [InlineData("resources.memory_exhaustion", "resource_exhaustion")]
    public void Exhaustion_FollowingFailureContextStopsAtTwoMinutes(string ruleId, string scenario) =>
        Check(ruleId, scenario, 120, "hang");

    private static void Check(string ruleId, string scenario, int limit, string supportKind)
    {
        var rule = RuleCatalog.CreateDefault().Single(item => item.RuleId == ruleId);
        var anchor = DiagnosticFixture.Load(scenario).Events.First(rule.AppliesTo);
        var support = supportKind switch
        {
            "update" => DiagnosticFixture.Load("update_planned_restart").Events.Single(DiagnosticFacts.IsUpdate),
            "crash" => DiagnosticFixture.Load("application_crash").Events[0],
            "whea" => DiagnosticFixture.Load("whea_processor_cache").Events[0],
            "exhaustion" => DiagnosticFixture.Load("resource_exhaustion").Events[0],
            "hang" => DiagnosticFixture.Load("application_hang").Events[0],
            _ => anchor with { Id = Guid.NewGuid() }
        };
        var direction = supportKind is "update" or "whea" or "exhaustion" ? -1 : 1;
        foreach (var seconds in new[] { limit - 1, limit, limit + 1 })
        {
            var shifted = support with { TimestampUtc = anchor.TimestampUtc.AddSeconds(direction * seconds) };
            Assert.True(rule.IsRelated(anchor, shifted) == (seconds <= limit),
                $"{ruleId}: optional context at {direction * seconds}s must follow the closed {limit}s boundary.");
        }
    }
}
