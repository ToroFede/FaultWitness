using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

public sealed class GraphicsRuleTests
{

    [Fact]
    public void graphics_engine_timeout_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("nvidia_tdr_141", "graphics.engine_timeout");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void graphics_engine_timeout_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("tdr_117", "graphics.engine_timeout");
    }

    [Fact]
    public void graphics_adapter_timeout_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("tdr_117", "graphics.adapter_timeout");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void graphics_adapter_timeout_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("nvidia_tdr_141", "graphics.adapter_timeout");
    }

    [Fact]
    public void graphics_tdr_with_driver_event_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("amd_tdr_141", "graphics.tdr_with_driver_event");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void graphics_tdr_with_driver_event_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("tdr_without_driver", "graphics.tdr_with_driver_event");
    }

    [Fact]
    public void graphics_tdr_with_app_crash_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("tdr_with_app_crash", "graphics.tdr_with_app_crash");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void graphics_tdr_with_app_crash_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("app_crash_outside_tdr_window", "graphics.tdr_with_app_crash");
    }

    [Fact]
    public void graphics_repeated_timeout_pattern_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("graphics_repeated_days", "graphics.repeated_timeout_pattern");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void graphics_repeated_timeout_pattern_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("nvidia_tdr_141", "graphics.repeated_timeout_pattern");
    }
}
