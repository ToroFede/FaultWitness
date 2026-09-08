using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

public sealed class ResourcesRuleTests
{

    [Fact]
    public void resources_memory_exhaustion_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("resource_exhaustion", "resources.memory_exhaustion");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void resources_memory_exhaustion_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("application_crash", "resources.memory_exhaustion");
    }

    [Fact]
    public void resources_exhaustion_with_hang_or_crash_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("resource_exhaustion_then_hang", "resources.exhaustion_with_hang_or_crash");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void resources_exhaustion_with_hang_or_crash_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("resource_exhaustion", "resources.exhaustion_with_hang_or_crash");
    }
}
