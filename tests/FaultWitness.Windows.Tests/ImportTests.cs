using System.IO.Compression;
using FaultWitness.Core;
using FaultWitness.Platform.Windows;

namespace FaultWitness.Windows.Tests;

public sealed class ImportTests
{
    [Theory]
    [InlineData("Example.exe")]
    [InlineData("FaultWitness.UI.Tests.exe")]
    [InlineData("FaultWitness.App.exe")]
    public async Task WerImportProducesNormalizedProvenance(string process)
    {
        var path = Path.Combine(Path.GetTempPath(), $"faultwitness-{Guid.NewGuid():N}.wer");
        try
        {
            await File.WriteAllTextAsync(path, $"EventType=APPCRASH\nAppName={process}\nFaultModuleName=Example.dll\n");
            var result = await WindowsImportService.ImportAsync([path], CancellationToken.None);
            var imported = Assert.Single(result.Batch.Events);
            Assert.Equal(SourceType.Imported, imported.SourceType);
            Assert.Equal(process, imported.Process);
            Assert.StartsWith("import:", imported.SourceReference, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task BundleWithTraversalEntryIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"faultwitness-{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                await using var writer = new StreamWriter(archive.CreateEntry("../unsafe.json").Open());
                await writer.WriteAsync("{}");
            }
            var result = await WindowsImportService.ImportAsync([path], CancellationToken.None);
            Assert.Empty(result.Batch.Events);
            Assert.NotEmpty(result.Errors);
        }
        finally { File.Delete(path); }
    }
}
