using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

public sealed class AudioRuleTests
{

    [Fact]
    public void audio_audiodg_crash_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("audiodg_windows_module", "audio.audiodg_crash");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void audio_audiodg_crash_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("application_crash", "audio.audiodg_crash");
    }

    [Fact]
    public void audio_audiodg_third_party_module_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("audiodg_third_party_apo", "audio.audiodg_third_party_module");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void audio_audiodg_third_party_module_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("audiodg_windows_module", "audio.audiodg_third_party_module");
    }

    [Fact]
    public void audio_repeated_apo_failure_RequiredEvidence_IsRecognized()
    {
        RuleAssertions.Present("audiodg_repeated_apo", "audio.repeated_apo_failure");
    }

    [Fact]
    [Trait("Category", "FalsePositive")]
    public void audio_repeated_apo_failure_InsufficientOrDifferentEvidence_IsRejected()
    {
        RuleAssertions.Absent("audiodg_third_party_apo", "audio.repeated_apo_failure");
    }
}
