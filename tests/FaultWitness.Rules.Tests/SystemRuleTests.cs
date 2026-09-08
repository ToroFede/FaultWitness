using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

public sealed class SystemRuleTests
{

    [Fact]
    public void system_bugcheck_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("other_bugcheck", "system.bugcheck");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void system_bugcheck_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("bugcheck_missing_code", "system.bugcheck");
    }

    [Fact]
    public void system_bugcheck_with_dump_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("bugcheck_with_dump", "system.bugcheck_with_dump");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void system_bugcheck_with_dump_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("whea_bugcheck_124", "system.bugcheck_with_dump");
    }
}
