using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

public sealed class StorageRuleTests
{

    [Fact]
    public void storage_request_timeout_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("storage_129", "storage.request_timeout");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void storage_request_timeout_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("storage_153_single", "storage.request_timeout");
    }

    [Fact]
    public void storage_io_retry_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("storage_153_single", "storage.io_retry");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void storage_io_retry_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("driver_event_without_tdr", "storage.io_retry");
    }

    [Fact]
    public void storage_io_warning_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("storage_51_single", "storage.io_warning");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void storage_io_warning_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("storage_153_single", "storage.io_warning");
    }

    [Fact]
    public void storage_repeated_timeout_pattern_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("storage_153_repeated_same_device", "storage.repeated_timeout_pattern");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void storage_repeated_timeout_pattern_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("storage_153_different_device", "storage.repeated_timeout_pattern");
    }
}
