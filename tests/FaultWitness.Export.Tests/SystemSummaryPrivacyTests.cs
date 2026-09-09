using FaultWitness.Core;
using FaultWitness.Export;

namespace FaultWitness.Export.Tests;

public sealed class SystemSummaryPrivacyTests
{
    [Fact]
    public void ExportKeepsAllowlistedInventoryAndRedactsPrivateValues()
    {
        var inventory = new SystemInventorySnapshot([
            new InventoryGroup("Graphics", [new InventoryDevice("gpu-secret", [new("GraphicsName", "NVIDIA RTX"), new("GraphicsDriverVersion", "551.23"), new("SerialNumber", "SECRET")])]),
            new InventoryGroup("Storage", [new InventoryDevice("disk-secret", [new("StorageModel", "Fast Disk"), new("StorageCapacity", "1000 GB")])]),
            new InventoryGroup("Unknown", [new InventoryDevice("user", [new("Anything", "C:\\Users\\alice\\private")])])
        ]);
        var text = SystemSummaryExporter.ToMarkdown(inventory, []);
        Assert.Contains("NVIDIA RTX", text);
        Assert.Contains("Fast Disk", text);
        Assert.Contains("551.23", text);
        Assert.DoesNotContain("SECRET", text);
        Assert.DoesNotContain("private", text);
        Assert.DoesNotContain("gpu-secret", text);
    }

    [Fact]
    public void ExportUsesStableSourceNamesAndFiltersUnknownObservations()
    {
        var readiness = new[] { new DiagnosticReadinessItem("wer", "attacker", DiagnosticCapabilityStatus.Ready, "ReadinessSourceReady", [
            new DiagnosticObservation("ReadinessDumpTarget", "%SystemRoot%\\Minidump"),
            new DiagnosticObservation("UnknownKey", "C:\\Users\\alice\\secret"),
            new DiagnosticObservation("ReadinessDumpTarget", "C:\\private\\dumps")]) };
        var text = SystemSummaryExporter.ToMarkdown(new SystemInventorySnapshot([]), readiness);
        Assert.Contains("Windows Error Reporting", text);
        Assert.Contains("%SystemRoot%\\Minidump", text);
        Assert.Contains("Custom destination (path withheld)", text);
        Assert.DoesNotContain("attacker", text);
        Assert.DoesNotContain("alice", text);
        Assert.DoesNotContain("UnknownKey", text);
    }
}
