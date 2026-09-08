using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

public sealed class ApplicationRuleTests
{

    [Fact]
    public void application_crash_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("application_crash", "application.crash");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void application_crash_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("application_hang", "application.crash");
    }

    [Fact]
    public void application_crash_with_wer_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("application_crash_with_wer_and_reliability", "application.crash_with_wer");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void application_crash_with_wer_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("application_crash", "application.crash_with_wer");
    }

    [Fact]
    public void application_third_party_faulting_module_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("third_party_fault_module", "application.third_party_faulting_module");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void application_third_party_faulting_module_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("windows_fault_module", "application.third_party_faulting_module");
    }

    [Fact]
    public void application_repeated_signature_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("same_signature_different_pid", "application.repeated_signature");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void application_repeated_signature_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("different_signature_same_application", "application.repeated_signature");
    }

    [Fact]
    public void application_hang_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("application_hang", "application.hang");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void application_hang_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("application_crash", "application.hang");
    }
}
