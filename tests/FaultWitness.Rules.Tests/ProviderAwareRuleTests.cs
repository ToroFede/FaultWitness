using FaultWitness.Core;
using FaultWitness.Rules;

namespace FaultWitness.Rules.Tests;

public sealed class ProviderAwareRuleTests
{
    [Fact]
    public void KernelPower41CreatesNoHardwareFailureConclusion()
    {
        var event41 = Event("Microsoft-Windows-Kernel-Power", 41);
        var rule = RuleCatalog.CreateDefault().Single(rule => rule.RuleId == "power.unclean_shutdown");
        var finding = rule.Evaluate(event41, [event41]);
        Assert.Contains("psu_failure", finding.FalsePositiveContract);
        Assert.Equal(EvidenceStrength.Limited, finding.Strength);
    }

    [Fact]
    public void Storage153RequiresAMatchingProvider()
    {
        var disk = Event("Disk", 153);
        var unrelated = Event("nvlddmkm", 153);
        var rule = RuleCatalog.CreateDefault().Single(rule => rule.RuleId == "storage.io_retry");
        Assert.True(rule.AppliesTo(disk));
        Assert.False(rule.AppliesTo(unrelated));
    }

    [Fact]
    public void RuleCatalogHasVersionedDiagnosticFamilies() => Assert.True(RuleCatalog.Definitions.Count >= 30);

    private static NormalizedEvent Event(string provider, int id) => new(Guid.NewGuid(), SourceType.EventLog, "Windows", DateTimeOffset.UtcNow, "System", provider, id, null, IncidentSeverity.Medium, null, null, null, null, new Dictionary<string, string>(), "test");
}
