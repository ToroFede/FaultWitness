using FaultWitness.Core;
using FaultWitness.Platform.Windows;

namespace FaultWitness.Windows.Tests;

public sealed class NormalizationTests
{
    private static string Xml(string provider = "Application Error", string channel = "Application", string data = "", string time = "2026-01-10T12:00:00Z", int id = 1000) =>
        $"<Event><System><Provider Name='{provider}'/><EventID>{id}</EventID><Version>0</Version><Level>2</Level><TimeCreated SystemTime='{time}'/><EventRecordID>17</EventRecordID><Channel>{channel}</Channel></System><EventData>{data}</EventData></Event>";

    [Fact]
    public void ApplicationError_NamedFieldsAndOriginalRecordArePreserved()
    {
        var item = WindowsDiagnosticsProvider.NormalizeXml(Xml(data: "<Data Name='AppName'>Example.exe</Data><Data Name='ModuleName'>Example.dll</Data>"), SourceType.EventLog, "synthetic:17");
        Assert.Equal("Example.exe", item.Process);
        Assert.Equal("Example.dll", item.Module);
        Assert.Equal("17", item.Field("OriginalRecordId"));
        Assert.Equal("Application", item.Field("OriginalChannel"));
        Assert.Equal("synthetic:17", item.SourceReference);
    }

    [Theory]
    [InlineData("APPCRASH", "Example.exe")]
    [InlineData("LiveKernelEvent", null)]
    public void WerPFields_OnlyCrashSchemaBecomesProcess(string eventName, string? expected)
    {
        var item = WindowsDiagnosticsProvider.NormalizeXml(Xml("Windows Error Reporting", data: $"<Data Name='EventName'>{eventName}</Data><Data Name='P1'>Example.exe</Data><Data Name='P4'>Example.dll</Data>", id: 1001), SourceType.Imported, "synthetic:wer");
        Assert.Equal(expected, item.Process);
    }

    [Fact]
    public void SystemBugcheck_ParametersNormalizeWithoutLocalizedMessage()
    {
        var item = WindowsDiagnosticsProvider.NormalizeXml(Xml("Microsoft-Windows-WER-SystemErrorReporting", "System",
            "<Data Name='param1'>0x00000124 (0x0, 0x0)</Data><Data Name='param2'>C:\\Windows\\Minidump\\synthetic.dmp</Data>", id: 1001), SourceType.EventLog, "synthetic:bugcheck");
        Assert.Equal("0x00000124", item.Field("BugcheckCode"));
        Assert.EndsWith("synthetic.dmp", item.Field("DumpPath"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bad-time")]
    [InlineData("")]
    public void MissingOrInvalidTimestamp_IsNotReplacedByCurrentTime(string time) =>
        Assert.Throws<InvalidDataException>(() => WindowsDiagnosticsProvider.NormalizeXml(Xml(time: time), SourceType.Imported, "synthetic"));

    [Fact]
    public void DuplicateEventField_IsRejectedAsAmbiguous() =>
        Assert.Throws<InvalidDataException>(() => WindowsDiagnosticsProvider.NormalizeXml(Xml(data: "<Data Name='AppName'>A</Data><Data Name='appname'>B</Data>"), SourceType.Imported, "synthetic"));

    [Fact]
    public void ExternalXmlEntities_AreProhibited() =>
        Assert.Throws<System.Xml.XmlException>(() => WindowsDiagnosticsProvider.NormalizeXml("<!DOCTYPE Event [<!ENTITY external SYSTEM 'file:///not-opened'>]>" + Xml(data: "&external;"), SourceType.Imported, "synthetic"));

    [Fact]
    public void ExcessiveXml_IsRejectedBeforeParsing() =>
        Assert.Throws<InvalidDataException>(() => WindowsDiagnosticsProvider.NormalizeXml(new string('x', 1024 * 1024 + 1), SourceType.Imported, "synthetic"));
}
