using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

public sealed class PowerRuleTests
{

    [Fact]
    public void power_unclean_shutdown_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("kernel41_only", "power.unclean_shutdown");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void power_unclean_shutdown_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("normal_shutdown", "power.unclean_shutdown");
    }

    [Fact]
    public void power_bugcheck_reboot_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("kernel41_with_bugcheck", "power.bugcheck_reboot");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void power_bugcheck_reboot_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("kernel41_only", "power.bugcheck_reboot");
    }

    [Fact]
    public void power_planned_restart_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("planned_restart", "power.planned_restart");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void power_planned_restart_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("normal_shutdown", "power.planned_restart");
    }

    [Fact]
    public void power_update_restart_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("update_planned_restart", "power.update_restart");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void power_update_restart_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("planned_restart", "power.update_restart");
    }

    [Fact]
    public void power_abrupt_restart_no_bugcheck_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("kernel41_only", "power.abrupt_restart_no_bugcheck");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void power_abrupt_restart_no_bugcheck_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("kernel41_partial_coverage", "power.abrupt_restart_no_bugcheck");
    }
}
