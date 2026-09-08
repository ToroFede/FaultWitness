using System.Text.Json;
using System.Text.Json.Serialization;
using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

internal sealed record DiagnosticFixture(string Name, string Description,
    List<NormalizedEvent> Events, List<SourceCoverage> Coverage)
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };
    public static DiagnosticFixture Load(string name) =>
        JsonSerializer.Deserialize<DiagnosticFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".json")), Options)
        ?? throw new InvalidDataException(name);
    public ScanResult Analyze() => new IncidentAnalyzer(RuleCatalog.CreateDefault()).Analyze(
        new EventBatch(Events, Coverage), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
}

internal static class RuleAssertions
{
    public static Finding Present(string fixture, string id)
    {
        var findings = DiagnosticFixture.Load(fixture).Analyze().Incidents.SelectMany(static item => item.Findings);
        var matched = findings.Where(item => item.RuleId == id).ToArray();
        Assert.NotEmpty(matched);
        var metadata = RuleCatalog.Definitions.Single(item => item.Id == id);
        Assert.All(matched, finding =>
        {
            Assert.Equal(metadata.BaseStrength, finding.Strength);
            Assert.Empty(finding.HypothesisKeys);
            Assert.Equal(metadata.NotEstablishedKey, finding.NotEstablishedKey);
            Assert.NotEmpty(finding.FalsePositiveContract);
        });
        return matched[0];
    }

    public static void Absent(string fixture, string id) =>
        Assert.DoesNotContain(DiagnosticFixture.Load(fixture).Analyze().Incidents.SelectMany(static item => item.Findings),
            finding => finding.RuleId == id);
}
