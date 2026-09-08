using FaultWitness.Core;
using FaultWitness.Export;

namespace FaultWitness.Export.Tests;

public sealed class ReportExporterTests
{
    [Fact]
    public void JsonRedactsProfilePathsAndIpAddressesActuallyPresentInInput()
    {
        var scan = Scan(@"C:\Users\private\file.dmp 192.168.1.5");
        var unredacted = ReportExporter.ToJson(scan, new ExportPrivacyOptions(RedactPersonalData: false));
        Assert.Contains("private", unredacted, StringComparison.Ordinal);
        Assert.Contains("192.168.1.5", unredacted, StringComparison.Ordinal);
        var report = ReportExporter.ToJson(scan, new ExportPrivacyOptions());
        Assert.DoesNotContain("private", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.1.5", report, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonExportIsMachineReadable() => Assert.Contains("Incidents", ReportExporter.ToJson(Scan("reference"), new ExportPrivacyOptions()));

    private static ScanResult Scan(string reference)
    {
        var eventRecord = new NormalizedEvent(Guid.NewGuid(), SourceType.EventLog, "Windows", DateTimeOffset.UtcNow, "System", "Test", 1, null, IncidentSeverity.Low, null, null, null, null, new Dictionary<string, string>(), reference);
        var incident = new Incident(Guid.NewGuid(), eventRecord.TimestampUtc, eventRecord.TimestampUtc, IncidentCategory.Power, IncidentSeverity.Low, eventRecord, [], [], [eventRecord], [], "test");
        return new ScanResult([incident], [], eventRecord.TimestampUtc, eventRecord.TimestampUtc);
    }
}
