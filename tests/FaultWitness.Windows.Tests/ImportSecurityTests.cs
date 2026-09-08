using System.Diagnostics.Eventing.Reader;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FaultWitness.Core;
using FaultWitness.Export;
using FaultWitness.Platform.Windows;
using FaultWitness.Rules;

namespace FaultWitness.Windows.Tests;

public sealed class ImportSecurityTests
{
    [Theory]
    [InlineData("EventType=APPCRASH\nmalformed-line")]
    [InlineData("EventType=APPCRASH\nAppName=one.exe\nAppName=two.exe")]
    [InlineData("EventType=APPCRASH\nAppName=one.exe\nappname=two.exe")]
    [InlineData("AppName=Example.exe")]
    [InlineData("EventType=APPCRASH")]
    [InlineData("EventType=APPCRASH\nAppName=Example.exe\nEventTime=invalid")]
    public async Task Wer_MalformedOrMissingIdentity_IsRejected(string content)
    {
        using var file = new TemporaryFile(".wer");
        await File.WriteAllTextAsync(file.Path, content);
        AssertRejected(await WindowsImportService.ImportAsync([file.Path], CancellationToken.None));
    }

    [Theory]
    [InlineData("line")]
    [InlineData("lines")]
    [InlineData("bytes")]
    public async Task Wer_ResourceLimitViolation_IsNotSilentlyTruncated(string kind)
    {
        using var file = new TemporaryFile(".wer");
        await File.WriteAllTextAsync(file.Path, "EventType=APPCRASH\nAppName=Example.exe\nExtra=abcdefghijk");
        var limits = kind switch
        {
            "line" => new ImportLimits(LineCharacters: 10),
            "lines" => new ImportLimits(WerLines: 2),
            _ => new ImportLimits(WerBytes: 12)
        };
        AssertRejected(await WindowsImportService.ImportAsync([file.Path], CancellationToken.None, limits));
    }

    [Fact]
    public async Task Wer_Utf16Bom_IsAcceptedAndEventTimeIsUsed()
    {
        using var file = new TemporaryFile(".wer");
        await File.WriteAllTextAsync(file.Path, "EventType=APPCRASH\nAppName=Example.exe\nEventTime=133000000000000000", Encoding.Unicode);
        var result = await WindowsImportService.ImportAsync([file.Path], CancellationToken.None);
        Assert.Empty(result.Errors);
        Assert.Equal(DateTimeOffset.FromFileTime(133000000000000000), Assert.Single(result.Batch.Events).TimestampUtc);
    }

    [Fact]
    public async Task Wer_InvalidUtf8_IsRejected()
    {
        using var file = new TemporaryFile(".wer");
        await File.WriteAllBytesAsync(file.Path, [0xff, 0xff, 0x00]);
        AssertRejected(await WindowsImportService.ImportAsync([file.Path], CancellationToken.None));
    }

    [Theory]
    [InlineData("../events.json")]
    [InlineData("..\\events.json")]
    [InlineData("/events.json")]
    [InlineData("C:\\events.json")]
    [InlineData("C:events.json")]
    [InlineData("\\\\host\\share\\events.json")]
    [InlineData("nested.zip")]
    [InlineData("subdir/events.json")]
    public async Task Zip_UnsafeOrUnexpectedNames_AreRejectedWithoutExtraction(string name)
    {
        using var file = new TemporaryFile(".zip");
        CreateZip(file.Path, [(name, "[]")]);
        AssertRejected(await WindowsImportService.ImportAsync([file.Path], CancellationToken.None));
    }

    [Theory]
    [InlineData("events.json")]
    [InlineData("EVENTS.JSON")]
    public async Task Zip_DuplicateOrCaseConflictingEvents_AreRejected(string duplicate)
    {
        using var file = new TemporaryFile(".zip");
        CreateZip(file.Path, [("events.json", "[]"), (duplicate, "[]")]);
        AssertRejected(await WindowsImportService.ImportAsync([file.Path], CancellationToken.None));
    }

    [Theory]
    [InlineData("entries")]
    [InlineData("individual")]
    [InlineData("total")]
    [InlineData("file")]
    public async Task Zip_DeclaredResourceLimits_AreEnforced(string kind)
    {
        using var file = new TemporaryFile(".zip");
        CreateZip(file.Path, [("events.json", "[]"), ("support-summary.md", new string('x', 200))]);
        var limits = kind switch
        {
            "entries" => new ImportLimits(Entries: 1),
            "individual" => new ImportLimits(EntryBytes: 100),
            "total" => new ImportLimits(EntryBytes: 201, ExpandedBytes: 201),
            _ => new ImportLimits(FileBytes: 16)
        };
        AssertRejected(await WindowsImportService.ImportAsync([file.Path], CancellationToken.None, limits));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("{\"Events\":[]}")]
    public async Task Zip_MalformedOrInvalidNormalizedJson_IsRejected(string json)
    {
        using var file = new TemporaryFile(".zip");
        CreateZip(file.Path, [("events.json", json)]);
        AssertRejected(await WindowsImportService.ImportAsync([file.Path], CancellationToken.None));
    }

    [Fact]
    public async Task Zip_ConflictingRecordIds_AreRejected()
    {
        using var file = new TemporaryFile(".zip");
        var item = Sample();
        CreateZip(file.Path, [("events.json", JsonSerializer.Serialize(new[] { item, item with { Process = "other.exe" } }))]);
        AssertRejected(await WindowsImportService.ImportAsync([file.Path], CancellationToken.None));
    }

    [Fact]
    public async Task Bundle_ValidRoundTrip_PreservesDiagnosisAndRedactsSensitiveFields()
    {
        using var file = new TemporaryFile(".zip");
        var item = Sample();
        var batch = new EventBatch([item], []);
        var before = new IncidentAnalyzer(RuleCatalog.CreateDefault()).Analyze(batch, item.TimestampUtc, item.TimestampUtc);
        await ReportExporter.CreateSupportBundleAsync(file.Path, before, new ExportPrivacyOptions(), CancellationToken.None);
        var imported = await WindowsImportService.ImportAsync([file.Path], CancellationToken.None);
        Assert.Empty(imported.Errors);
        var restored = Assert.Single(imported.Batch.Events);
        Assert.Null(restored.RawData);
        Assert.Equal("1.2.3.4", restored.Field("AppVersion"));
        Assert.Equal("<redacted>", restored.Field("UserName"));
        var after = new IncidentAnalyzer(RuleCatalog.CreateDefault()).Analyze(imported.Batch, item.TimestampUtc, item.TimestampUtc);
        Assert.Equal(Assert.Single(before.Incidents).EvidenceStrength, Assert.Single(after.Incidents).EvidenceStrength);
        Assert.Equal(before.Incidents[0].Signature, after.Incidents[0].Signature);
        Assert.All(imported.Batch.Coverage, coverage => Assert.Equal(CoverageState.Partial, coverage.State));
    }

    [Fact]
    public async Task Import_CancelledToken_DoesNotReturnPartialSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => WindowsImportService.ImportAsync(["absent.wer"], cancellation.Token));
    }

    [Fact]
    public async Task Evtx_InvalidHeader_IsRejected()
    {
        using var file = new TemporaryFile(".evtx");
        await File.WriteAllTextAsync(file.Path, "This is not an EVTX file.");
        AssertRejected(await WindowsImportService.ImportAsync([file.Path], CancellationToken.None));
    }

    [Fact]
    public async Task Evtx_NonexistentFile_IsUnavailable()
    {
        var result = await WindowsImportService.ImportAsync([System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".evtx")], CancellationToken.None);
        AssertRejected(result);
        Assert.Equal(CoverageState.Unavailable, Assert.Single(result.Batch.Coverage).State);
    }

    [Fact]
    public async Task Evtx_EmptyExportWithImpossibleRecordId_IsReadable()
    {
        using var file = new TemporaryFile(".evtx");
        // EventRecordID=0 cannot select a private record. The temporary EVTX is never committed.
        EventLogSession.GlobalSession.ExportLog("Application", PathType.LogName, "*[System[EventRecordID=0]]", file.Path);
        var result = await WindowsImportService.ImportAsync([file.Path], CancellationToken.None);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Batch.Events);
    }

    [Fact]
    public async Task Evtx_ReadAccessDenied_IsReportedAndDoesNotEscapeImporter()
    {
        using var file = new TemporaryFile(".evtx");
        await File.WriteAllBytesAsync(file.Path, "ElfFile\0"u8.ToArray());
        var info = new FileInfo(file.Path);
        var original = System.IO.FileSystemAclExtensions.GetAccessControl(info);
        var restricted = System.IO.FileSystemAclExtensions.GetAccessControl(info);
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        restricted.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(identity.User!,
            System.Security.AccessControl.FileSystemRights.ReadData, System.Security.AccessControl.AccessControlType.Deny));
        try
        {
            System.IO.FileSystemAclExtensions.SetAccessControl(info, restricted);
            var result = await WindowsImportService.ImportAsync([file.Path], CancellationToken.None);
            AssertRejected(result);
            Assert.Equal(CoverageState.AccessDenied, Assert.Single(result.Batch.Coverage).State);
        }
        finally { System.IO.FileSystemAclExtensions.SetAccessControl(info, original); }
    }

    [Theory]
    [InlineData("invalid-enum")]
    [InlineData("null-fields")]
    [InlineData("non-utc")]
    [InlineData("record-limit")]
    public async Task Zip_InvalidSemanticRecords_AreRejected(string kind)
    {
        using var file = new TemporaryFile(".zip");
        var item = Sample();
        item = kind switch
        {
            "invalid-enum" => item with { SourceType = (SourceType)999 },
            "null-fields" => item with { Fields = null! },
            "non-utc" => item with { TimestampUtc = item.TimestampUtc.ToOffset(TimeSpan.FromHours(1)) },
            _ => item
        };
        CreateZip(file.Path, [("events.json", JsonSerializer.Serialize(new[] { item }))]);
        AssertRejected(await WindowsImportService.ImportAsync([file.Path], CancellationToken.None,
            kind == "record-limit" ? new ImportLimits(Events: 0) : null));
    }

    [Fact]
    public async Task Zip_WithoutEventsJson_IsRejected()
    {
        using var file = new TemporaryFile(".zip");
        CreateZip(file.Path, [("support-summary.md", "Synthetic report")]);
        AssertRejected(await WindowsImportService.ImportAsync([file.Path], CancellationToken.None));
    }

    private static NormalizedEvent Sample() => new(Guid.NewGuid(), SourceType.EventLog, "Windows",
        new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero), "Application", "Application Error",
        1000, 0, IncidentSeverity.Medium, "Example.exe", 10, "Example.dll", null,
        new Dictionary<string, string> { ["AppVersion"] = "1.2.3.4", ["ExceptionCode"] = "c0000005", ["UserName"] = "synthetic-user" },
        "synthetic:application-1", "<Event>synthetic raw data</Event>");

    private static void AssertRejected(ImportResult result)
    {
        Assert.Empty(result.Batch.Events);
        Assert.NotEmpty(result.Errors);
        Assert.DoesNotContain(result.Batch.Coverage, coverage => coverage.State == CoverageState.Complete);
    }

    private static void CreateZip(string path, (string Name, string Content)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
    }

    private sealed class TemporaryFile(string extension) : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"faultwitness-test-{Guid.NewGuid():N}{extension}");
        public void Dispose() => File.Delete(Path);
    }
}
