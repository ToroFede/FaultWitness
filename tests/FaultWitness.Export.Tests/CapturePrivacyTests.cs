using System.IO.Compression;
using FaultWitness.Core;
using FaultWitness.Export;

namespace FaultWitness.Export.Tests;

public sealed class CapturePrivacyTests
{
    private static readonly string[] BundleEntries = ["events.json", "support-summary.md"];
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupportBundleNeverPackagesReferencedDumpsOrCrashMemory(bool raw)
    {
        var directory = Directory.CreateTempSubdirectory("FaultWitness-capture-privacy-");
        try
        {
            var captures = Directory.CreateDirectory(Path.Combine(directory.FullName, "FaultWitness", "Captures"));
            var dump = Path.Combine(captures.FullName, "synthetic.exe.dmp");
            await File.WriteAllTextAsync(dump, "SYNTHETIC-MEMORY-SENTINEL", CancellationToken.None);
            var stamp = DateTimeOffset.UtcNow;
            var record = new NormalizedEvent(Guid.NewGuid(), SourceType.CrashArtifact, "Windows", stamp, null, "CrashArtifact", 0, null, IncidentSeverity.Low, null, null, null, null,
                new Dictionary<string,string> { ["DumpKind"] = "Unknown", ["DumpContents"] = "SYNTHETIC-MEMORY-SENTINEL" }, dump, "SYNTHETIC-MEMORY-SENTINEL");
            var incident = new Incident(Guid.NewGuid(), stamp, stamp, IncidentCategory.ApplicationCrash, IncidentSeverity.Low, record, [], [], [record], [], "synthetic");
            var scan = new ScanResult([incident], [], stamp, stamp);
            var privacy = new ExportPrivacyOptions(IncludeRawXml: raw);
            Assert.DoesNotContain("SYNTHETIC-MEMORY-SENTINEL", ReportExporter.ToJson(scan, privacy), StringComparison.Ordinal);
            var bundle = Path.Combine(directory.FullName, "support.zip");
            await ReportExporter.CreateSupportBundleAsync(bundle, scan, privacy, CancellationToken.None);
            using var zip = ZipFile.OpenRead(bundle);
            Assert.Equal(BundleEntries, zip.Entries.Select(e => e.FullName).Order().ToArray());
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                Assert.DoesNotContain("SYNTHETIC-MEMORY-SENTINEL", await reader.ReadToEndAsync(CancellationToken.None), StringComparison.Ordinal);
            }
            Assert.True(File.Exists(dump));
        }
        finally { directory.Delete(true); }
    }
}
