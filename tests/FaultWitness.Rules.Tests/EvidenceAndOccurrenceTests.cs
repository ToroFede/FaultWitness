using FaultWitness.Core;

namespace FaultWitness.Rules.Tests;

public sealed class EvidenceAndOccurrenceTests
{
    [Fact]
    public void AppError_Wer_Reliability_AreOneObservationWithoutStrengthInflation()
    {
        var single = Assert.Single(DiagnosticFixture.Load("application_crash").Analyze().Incidents);
        var mirrored = Assert.Single(DiagnosticFixture.Load("application_crash_with_wer_and_reliability").Analyze().Incidents);
        Assert.Equal(single.EvidenceStrength, mirrored.EvidenceStrength);
        var positive = mirrored.Evidence.Where(item => item.Kind == EvidenceKind.Positive).ToArray();
        Assert.Equal(3, positive.Length);
        Assert.Single(positive.Select(item => item.ObservationId).Distinct());
        Assert.Equal(3, positive.Select(item => item.Provenance).Distinct().Count());
        Assert.Contains(mirrored.Relations, item => item.Kind == RelationKind.DerivedFrom);
        Assert.Contains(mirrored.Relations, item => item.Kind == RelationKind.SameReport);
        Assert.Empty(mirrored.Findings.SelectMany(item => item.HypothesisKeys));
    }

    [Fact]
    public void TwoActualCrashes_ThirtySecondsApart_RemainTwoOccurrences()
    {
        var result = DiagnosticFixture.Load("same_signature_different_pid").Analyze();
        Assert.Equal(2, result.Incidents.Count);
        Assert.Single(result.Patterns);
        Assert.Single(result.Incidents.Select(item => item.Signature).Distinct());
    }

    [Fact]
    public void DifferentSignatures_SameApplication_DoNotFormExactPattern()
    {
        var result = DiagnosticFixture.Load("different_signature_same_application").Analyze();
        Assert.Equal(2, result.Incidents.Count);
        Assert.Empty(result.Patterns);
        Assert.All(result.Incidents, item => Assert.Single(item.SourceEvents));
    }

    [Theory]
    [InlineData("application_repeated_days")]
    [InlineData("graphics_repeated_days")]
    [InlineData("audiodg_repeated_apo")]
    public void SameSignature_AcrossDays_IsPatternNotGiantIncident(string name)
    {
        var result = DiagnosticFixture.Load(name).Analyze();
        Assert.Equal(2, result.Incidents.Count);
        Assert.All(result.Incidents, item => Assert.Equal(item.StartTimeUtc, item.EndTimeUtc));
        Assert.Single(result.Patterns);
    }

    [Fact]
    public void HangAndCrash_SameProcessAndReportId_StayDistinct()
    {
        var fixture = DiagnosticFixture.Load("application_crash");
        var hang = DiagnosticFixture.Load("application_hang").Events[0];
        fixture.Events.Add(hang with { Fields = fixture.Events[0].Fields });
        var result = fixture.Analyze();
        Assert.Equal(2, result.Incidents.Count);
        Assert.Contains(result.Incidents, item => item.Category == IncidentCategory.ApplicationHang);
    }

    [Theory]
    [InlineData("application_crash")]
    [InlineData("nvidia_tdr_141")]
    [InlineData("whea_processor_cache")]
    [InlineData("audiodg_third_party_apo")]
    [InlineData("storage_129")]
    [InlineData("service_single")]
    public void StableSignatures_IgnorePidAndReportIdentity(string name)
    {
        var source = DiagnosticFixture.Load(name).Events.Last();
        var fields = new Dictionary<string, string>(source.Fields) { ["ReportId"] = "other-report" };
        var changed = source with { Id = Guid.NewGuid(), ProcessId = 7777, Fields = fields, TimestampUtc = source.TimestampUtc.AddDays(1) };
        Assert.Equal(SignatureMatch.Exact, DiagnosticFacts.CompareSignatures(source, changed));
        Assert.False(DiagnosticFacts.SameOccurrence(source, changed));
    }

    [Fact]
    public void SignatureComparison_DistinguishesRelatedSameCategoryAndUnrelated()
    {
        var source = DiagnosticFixture.Load("application_crash").Events[0];
        Assert.Equal(SignatureMatch.Related, DiagnosticFacts.CompareSignatures(source, source with { Module = "Other.dll" }));
        Assert.Equal(SignatureMatch.SameCategory, DiagnosticFacts.CompareSignatures(source, source with { Process = "Other.exe" }));
        Assert.Equal(SignatureMatch.Unrelated, DiagnosticFacts.CompareSignatures(source, DiagnosticFixture.Load("storage_129").Events[0]));
    }

    [Theory]
    [InlineData(CoverageState.Complete, EvidenceKind.Negative)]
    [InlineData(CoverageState.Partial, EvidenceKind.Unknown)]
    [InlineData(CoverageState.Unavailable, EvidenceKind.Unknown)]
    [InlineData(CoverageState.AccessDenied, EvidenceKind.Unknown)]
    [InlineData(CoverageState.NotSupported, EvidenceKind.Unknown)]
    public void MissingWhea_DependsOnExactSystemCoverage(CoverageState state, EvidenceKind expected)
    {
        var fixture = DiagnosticFixture.Load("kernel41_only");
        fixture.Coverage[0] = fixture.Coverage[0] with { State = state };
        var incident = Assert.Single(fixture.Analyze().Incidents);
        Assert.Contains(incident.Evidence, item => item.Kind == expected && item.LocalizationKey.StartsWith("evidence.whea.", StringComparison.Ordinal));
        if (state != CoverageState.Complete)
            Assert.DoesNotContain(incident.Evidence, item => item.Kind == EvidenceKind.Negative && item.LocalizationKey.Contains("whea", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("wrong-channel")]
    [InlineData("short-window")]
    [InlineData("missing-times")]
    [InlineData("contradictory-source")]
    public void NominalCompleteCoverage_WithoutFullExactWindow_IsUnknown(string variant)
    {
        var fixture = DiagnosticFixture.Load("kernel41_only");
        var coverage = fixture.Coverage[0];
        fixture.Coverage[0] = variant switch
        {
            "wrong-channel" => coverage with { Channel = "Application" },
            "short-window" => coverage with { ExaminedFromUtc = fixture.Events[0].TimestampUtc.AddSeconds(-299) },
            "missing-times" => coverage with { ExaminedFromUtc = null, ExaminedToUtc = null },
            _ => coverage
        };
        if (variant == "contradictory-source") fixture.Coverage.Add(coverage with { State = CoverageState.AccessDenied });
        Assert.Contains(Assert.Single(fixture.Analyze().Incidents).Evidence, item => item.Kind == EvidenceKind.Unknown && item.LocalizationKey == "evidence.whea.unknown");
    }

    [Theory]
    [InlineData(CoverageState.Unavailable)]
    [InlineData(CoverageState.AccessDenied)]
    public void ReliabilityFailure_IsUnknownAndDoesNotAbortAnalysis(CoverageState state)
    {
        var fixture = DiagnosticFixture.Load("application_crash");
        fixture.Coverage[2] = fixture.Coverage[2] with { State = state };
        var incident = Assert.Single(fixture.Analyze().Incidents);
        Assert.Contains(incident.Evidence, item => item.Kind == EvidenceKind.Unknown && item.Family.StartsWith("Reliability", StringComparison.Ordinal));
        Assert.Equal(EvidenceStrength.Moderate, incident.EvidenceStrength);
    }

    [Theory]
    [InlineData("pnp_219_benign")]
    [InlineData("pnp_219_single")]
    [InlineData("storage_153_single")]
    [InlineData("planned_restart")]
    [InlineData("driver_event_without_tdr")]
    [InlineData("normal_shutdown")]
    public void LowValueContext_DoesNotBecomeHeadline(string name) =>
        Assert.DoesNotContain(DiagnosticFixture.Load(name).Analyze().Incidents, item => item.IsHeadline);

    [Fact]
    public void PostRebootServiceFailure_IsContextNotCause()
    {
        var result = DiagnosticFixture.Load("service_failure_after_reboot").Analyze();
        var service = result.Incidents.Single(item => item.Category == IncidentCategory.Service);
        Assert.False(service.IsHeadline);
        Assert.All(service.Findings, finding => Assert.Equal(FindingDisposition.Context, finding.Disposition));
    }

    [Fact]
    public void Analyze_CancellationIsCooperative()
    {
        var fixture = DiagnosticFixture.Load("application_crash");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new IncidentAnalyzer(RuleCatalog.CreateDefault()).Analyze(
            new EventBatch(fixture.Events, fixture.Coverage), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, cancellation.Token));
    }
}
