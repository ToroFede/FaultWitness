using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

public sealed class ServiceRuleTests
{

    [Fact]
    public void service_unexpected_termination_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("service_failure_after_reboot", "service.unexpected_termination");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void service_unexpected_termination_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("service_normal", "service.unexpected_termination");
    }

    [Fact]
    public void service_repeated_failure_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("service_repeated_before_incident", "service.repeated_failure");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void service_repeated_failure_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("service_failure_after_reboot", "service.repeated_failure");
    }
}
