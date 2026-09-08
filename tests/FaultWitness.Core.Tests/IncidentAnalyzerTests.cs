using FaultWitness.Core;

namespace FaultWitness.Core.Tests;

public sealed class IncidentAnalyzerTests
{
    [Fact]
    public void AnalyzerKeepsEventsFromDifferentProcessesSeparate()
    {
        var at = DateTimeOffset.UtcNow;
        var first = Event("one.exe", at);
        var second = Event("two.exe", at.AddSeconds(10));
        var result = new IncidentAnalyzer([new TestRule()]).Analyze(new EventBatch([first, second], []), at, at);
        Assert.Equal(2, result.Incidents.Count);
    }

    [Fact]
    public void CoverageIsPreservedWhenNoEventIsFound()
    {
        var coverage = new SourceCoverage(SourceType.EventLog, CoverageState.AccessDenied, null, null, "coverage.event_log_access_denied");
        var result = new IncidentAnalyzer([]).Analyze(new EventBatch([], [coverage]), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Assert.Equal(CoverageState.AccessDenied, Assert.Single(result.Coverage).State);
    }

    [Fact]
    public void RuleSpecificWindowDoesNotMergeEventsOutsideBoundary()
    {
        var at = DateTimeOffset.UtcNow;
        var first = Event("same.exe", at);
        var second = Event("same.exe", at.AddMinutes(1).AddSeconds(1));
        var result = new IncidentAnalyzer([new TestRule(TimeSpan.FromMinutes(1))]).Analyze(new EventBatch([first, second], []), at, at);
        Assert.Equal(2, result.Incidents.Count);
    }

    private static NormalizedEvent Event(string process, DateTimeOffset timestamp) => new(Guid.NewGuid(), SourceType.EventLog, "Windows", timestamp, "Application", "Test", 1, null, IncidentSeverity.Medium, process, null, null, null, new Dictionary<string, string>(), "test");

    private sealed class TestRule(TimeSpan? correlationWindow = null) : IDiagnosticRule
    {
        public string RuleId => "application.crash";
        public string Version => "test";
        public TimeSpan CorrelationWindow => correlationWindow ?? TimeSpan.FromMinutes(5);
        public bool AppliesTo(NormalizedEvent diagnosticEvent) => true;
        public Finding Evaluate(NormalizedEvent anchor, IReadOnlyList<NormalizedEvent> nearbyEvents) => new(RuleId, Version, FindingDisposition.Significant, EvidenceStrength.Limited, "observed", "interpretation", "unknown", [], [], []);
        public string BuildSignature(NormalizedEvent diagnosticEvent) => diagnosticEvent.Process ?? string.Empty;
    }
}
