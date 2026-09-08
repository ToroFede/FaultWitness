using System.Text.Json;
using FaultWitness.Core;
using FaultWitness.Localization;

namespace FaultWitness.Rules.Tests;

public sealed class AssessmentContractTests
{
    public sealed record CoverageCase(string RuleId, string PositiveFixture, string NegativeFixture);
    [Fact]
    public void CoverageManifest_ContainsExactlyEveryCatalogRule()
    {
        Assert.Equal(37, RuleCatalog.Definitions.Count);
        Assert.Equal(RuleCatalog.Definitions.Select(item => item.Id).Order(), Cases().Select(item => (string)item[0]).Order());
        Assert.Equal(37, RuleCatalog.Definitions.Select(item => item.Id).Distinct().Count());
        Assert.All(RuleCatalog.Definitions, item => Assert.Equal(RuleCatalog.DatabaseVersion, item.Version));
    }
    public static IEnumerable<object[]> Cases() =>
        (JsonSerializer.Deserialize<CoverageCase[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "rule-expectations.json")))
        ?? throw new InvalidDataException()).Select(static row => new object[] { row.RuleId, row.PositiveFixture });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "FalsePositive")]
    public void RuleIdentity_RejectsMismatchedProviderChannelAndPlatform(string id, string positiveFixture)
    {
        foreach (var field in new[] { "provider", "channel", "platform" })
        {
            var fixture = DiagnosticFixture.Load(positiveFixture);
            for (var index = 0; index < fixture.Events.Count; index++)
            {
                var item = fixture.Events[index];
                fixture.Events[index] = field switch
                {
                    "provider" => item with { Provider = "Unrelated-Provider" },
                    "channel" => item with { Channel = "Unrelated-Channel" },
                    _ => item with { Platform = "Unrelated-Platform" }
                };
            }
            Assert.DoesNotContain(fixture.Analyze().Incidents.SelectMany(static item => item.Findings), finding => finding.RuleId == id);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "FalsePositive")]
    public void Assessment_ExposesObservationAndNotEstablishedWithoutForbiddenHypothesis(string id, string positiveFixture)
    {
        var metadata = RuleCatalog.Definitions.Single(item => item.Id == id);
        Assert.NotEmpty(metadata.FalsePositiveContract);
        Assert.Equal("Windows", metadata.Platform);
        Assert.NotEmpty(metadata.Anchor);
        Assert.NotEmpty(metadata.RequiredEvidence);
        Assert.True(metadata.WindowSeconds > 0);
        var finding = RuleAssertions.Present(positiveFixture, id);
        Assert.Empty(finding.HypothesisKeys);
        Assert.Equal("rule." + id + ".observed", finding.ObservedKey);
        Assert.Equal("rule." + id + ".not_established", finding.NotEstablishedKey);
        var text = new LocalizationService();
        Assert.NotEqual(finding.ObservedKey, text.Get(finding.ObservedKey));
        Assert.NotEqual(finding.NotEstablishedKey, text.Get(finding.NotEstablishedKey));
        Assert.NotEqual(metadata.RecommendedActionKey, text.Get(metadata.RecommendedActionKey));
    }

    [Fact]
    public void CorrectedWheaAlone_DoesNotEscalateToHighSeverityProcessorFailure()
    {
        var incidents = DiagnosticFixture.Load("whea_corrected").Analyze().Incidents;
        Assert.All(incidents, incident =>
        {
            Assert.Equal(IncidentSeverity.Low, incident.Severity);
            Assert.Equal(EvidenceStrength.Limited, incident.EvidenceStrength);
        });
    }

    [Fact]
    public void LiveKernelNumericCode_IsNotAnEventIdentifier()
    {
        var fixture = DiagnosticFixture.Load("driver_event_without_tdr");
        var item = fixture.Events[0];
        fixture.Events[0] = item with { Provider = "Microsoft-Windows-DxgKrnl", EventId = 141 };
        Assert.Empty(fixture.Analyze().Incidents);
    }

    [Fact]
    public void WER_FreeTextLiveKernelMention_DoesNotCreateTdr()
    {
        var fixture = DiagnosticFixture.Load("application_crash_with_wer");
        fixture.Events[1] = fixture.Events[1] with { RawData = "LiveKernelEvent 141", Fields = new Dictionary<string, string> { ["EventName"] = "APPCRASH", ["P1"] = "141" } };
        Assert.DoesNotContain(fixture.Analyze().Incidents, incident => incident.Category == IncidentCategory.Graphics);
    }
}
