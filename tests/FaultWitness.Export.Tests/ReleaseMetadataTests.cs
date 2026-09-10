using System.Runtime.InteropServices;
using System.Text.Json;
using FaultWitness.Core;
using FaultWitness.Export;
using FaultWitness.Localization;

namespace FaultWitness.Export.Tests;

public sealed class ReleaseMetadataTests
{
    [Fact]
    public void ExportsIdentifyBuildAndSafeSupportHostWithoutMachineIdentifiers()
    {
        var now = DateTimeOffset.UtcNow;
        var scan = new ScanResult([], [], now, now);
        var markdown = ReportExporter.ToMarkdown(scan, new ExportPrivacyOptions());
        Assert.Contains(ReleaseIdentity.Display, markdown, StringComparison.Ordinal);
        Assert.Contains(RuntimeInformation.OSDescription, markdown, StringComparison.Ordinal);
        Assert.Contains(RuntimeInformation.ProcessArchitecture.ToString(), markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.MachineName, markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), markdown, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(ReportExporter.ToJson(scan, new ExportPrivacyOptions()));
        Assert.Equal(ReleaseIdentity.Product, json.RootElement.GetProperty("ProductVersion").GetString());
        Assert.Equal(ReleaseIdentity.Build, json.RootElement.GetProperty("BuildVersion").GetString());
        var support = ReportExporter.ToSupportMarkdown(scan, new LocalizationService(), false, ReleaseIdentity.Display, "unchanged-rules");
        Assert.Contains(ReleaseIdentity.Build, support, StringComparison.Ordinal);
        Assert.Contains("unchanged-rules", support, StringComparison.Ordinal);
    }
}
