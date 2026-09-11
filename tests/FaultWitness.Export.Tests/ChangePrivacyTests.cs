using FaultWitness.Core;
using FaultWitness.Export;
using System.IO.Compression;

namespace FaultWitness.Export.Tests;

public sealed class ChangePrivacyTests
{
    [Fact]
    public void CompactProjectionRemovesExplicitSerialAndPrivatePathsWithoutLosingVersions()
    {
        var change = new SystemChange("synthetic", "Windows", DateTimeOffset.UnixEpoch, ChangeCategory.DriverInstalled,
            "ChangeSourceSetupApi", "Device SERIAL-SYNTHETIC-SECRET", ChangeSubsystem.Display, "1.2.3.4", "2.3.4.5",
            Environment.MachineName, @"D:\Users\PrivatePerson\PrivateFolder\install.log") { ComponentIdentity = "SERIAL-SYNTHETIC-SECRET" };
        var safe = SystemChangePrivacy.Sanitize(change);
        var json = System.Text.Json.JsonSerializer.Serialize(safe);
        Assert.DoesNotContain("SYNTHETIC-SECRET", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivatePerson", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivateFolder", json, StringComparison.Ordinal);
        Assert.Equal("<computer>", safe.Vendor);
        Assert.Equal("1.2.3.4", safe.PreviousValue);
        Assert.Equal("2.3.4.5", safe.NewValue);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AllExportsKeepSafeChangeProjectionAndDisclaimer(bool redact)
    {
        var id = "PCI\\VEN_1234&DEV_SECRET";
        var evt = new NormalizedEvent(Guid.NewGuid(), SourceType.EventLog, "Windows", new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), "System", "p", 1, null, IncidentSeverity.Low, null, null, null, null, new Dictionary<string, string>(), "ref");
        var change = new SystemChange("c", "Windows", evt.TimestampUtc, ChangeCategory.DriverInstalled, "Update", id, ChangeSubsystem.Display, "old " + id, "new " + id, "Vendor " + id, "Source " + id) { ComponentIdentity = id };
        var incident = new Incident(Guid.NewGuid(), evt.TimestampUtc, evt.TimestampUtc, IncidentCategory.Graphics, IncidentSeverity.Low, evt, [], [], [evt],
            [new RelatedSystemChange(change, ContextualRelevance.High, ChangeTiming.After, TimeSpan.FromMinutes(1), "reason")], "sig")
        { ChangeContext = new ChangeHistoryContext(evt.TimestampUtc, FirstObservationBasis.CurrentScan, false, [new SourceCoverage(SourceType.ChangeHistory, CoverageState.Partial, evt.TimestampUtc.AddDays(-1), evt.TimestampUtc, "detail", "Windows Update")]) };
        var result = new ScanResult([incident], [], evt.TimestampUtc, evt.TimestampUtc);
        var options = new ExportPrivacyOptions(redact);
        foreach (var text in new[] { ReportExporter.ToJson(result, options), ReportExporter.ToMarkdown(result, options), ReportExporter.ToHtml(result, options), ReportExporter.ToSupportMarkdown(result, new FaultWitness.Localization.LocalizationService(), false, "v", "r") })
        {
            Assert.DoesNotContain("DEV_SECRET", text, StringComparison.Ordinal);
            Assert.Contains("Changes close in time provide context. They do not prove what caused the incident.", text, StringComparison.Ordinal);
            Assert.Contains("old", text, StringComparison.Ordinal); Assert.Contains("new", text, StringComparison.Ordinal);
        }
        var directory = Directory.CreateTempSubdirectory("FaultWitness-change-export-");
        try
        {
            var path = Path.Combine(directory.FullName, "support.zip");
            await ReportExporter.CreateSupportBundleAsync(path, result, options, CancellationToken.None);
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var contents = await reader.ReadToEndAsync(CancellationToken.None);
                Assert.DoesNotContain("DEV_SECRET", contents, StringComparison.Ordinal);
                if (entry.Name == "support-summary.md") Assert.Contains(ChangePresentation.Disclaimer, contents, StringComparison.Ordinal);
            }
        }
        finally { directory.Delete(true); }
    }
}
