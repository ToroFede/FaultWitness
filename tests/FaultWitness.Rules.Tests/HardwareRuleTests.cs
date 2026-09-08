using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

public sealed class HardwareRuleTests
{

    [Fact]
    public void hardware_whea_corrected_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("whea_corrected", "hardware.whea.corrected");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void hardware_whea_corrected_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("whea_processor_cache", "hardware.whea.corrected");
    }

    [Fact]
    public void hardware_whea_recurrent_corrected_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("whea_recurrent_corrected", "hardware.whea.recurrent_corrected");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void hardware_whea_recurrent_corrected_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("whea_corrected", "hardware.whea.recurrent_corrected");
    }

    [Fact]
    public void hardware_whea_uncorrected_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("whea_processor_cache", "hardware.whea.uncorrected");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void hardware_whea_uncorrected_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("whea_corrected", "hardware.whea.uncorrected");
    }

    [Fact]
    public void hardware_whea_processor_mce_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("whea_processor_cache", "hardware.whea.processor_mce");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void hardware_whea_processor_mce_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("whea_untyped_18", "hardware.whea.processor_mce");
    }

    [Fact]
    public void hardware_whea_pcie_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("whea_pcie", "hardware.whea.pcie");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void hardware_whea_pcie_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("whea_processor_cache", "hardware.whea.pcie");
    }

    [Fact]
    public void hardware_whea_memory_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("whea_memory", "hardware.whea.memory");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void hardware_whea_memory_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("whea_processor_cache", "hardware.whea.memory");
    }

    [Fact]
    public void hardware_whea_bugcheck_124_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("whea_bugcheck_124", "hardware.whea_bugcheck_124");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void hardware_whea_bugcheck_124_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("other_bugcheck", "hardware.whea_bugcheck_124");
    }
}
