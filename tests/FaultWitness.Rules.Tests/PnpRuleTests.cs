using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

public sealed class PnpRuleTests
{

    [Fact]
    public void pnp_umdf_transient_load_warning_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("pnp_219_benign", "pnp.umdf_transient_load_warning");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void pnp_umdf_transient_load_warning_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("clean_system", "pnp.umdf_transient_load_warning");
    }

    [Fact]
    public void pnp_recurrent_device_failure_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("pnp_219_recurrent", "pnp.recurrent_device_failure");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void pnp_recurrent_device_failure_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("pnp_219_benign", "pnp.recurrent_device_failure");
    }
}
