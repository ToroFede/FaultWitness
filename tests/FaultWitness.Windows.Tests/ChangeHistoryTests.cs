using FaultWitness.Core;
using FaultWitness.Platform.Windows;

namespace FaultWitness.Windows.Tests;

public sealed class ChangeHistoryTests
{
    private static readonly DateTimeOffset From = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddDays(1);

    [Fact]
    public void SetupApi_RequiresSuccessfulInstallAndExplicitInf()
    {
        var text = ">>>  [Device Install - PCI\\VEN_1234\\1]\n>>>  Section start 2026/09/09 12:00:00.000\n     dvi: Selected Driver:\n     dvi: InfFile - oem42.inf\n     dvi: Provider - Acme\n     dvi: Class GUID - {4D36E968-E325-11CE-BFC1-08002BE10318}\n     dvi: {Core Device Install}\n<<<  Section end 2026/09/09 12:00:01.000\n<<<  [Exit status: SUCCESS]\n";
        var changes = WindowsChangeHistoryParser.ParseSetupApi(text, From, To);
        var change = Assert.Single(changes);
        Assert.Equal(ChangeCategory.DriverInstalled, change.Category);
        Assert.Equal("oem42.inf", change.Subject);
        Assert.Null(change.NewValue);
        Assert.Equal(ChangeSubsystem.Display, change.Subsystem);
        Assert.Null(change.PreviousValue);
        Assert.Equal("PCI\\VEN_1234\\1", change.ComponentIdentity);
    }

    [Fact]
    public void SetupApi_SkipsFailureAndCandidateEnumeration()
    {
        var text = ">>>  [Device Install - ROOT\\X]\n>>>  Section start 2026/09/09 12:00:00.000\n     utl: Driver INF - candidate.inf\n<<<  Section end 2026/09/09 12:00:01.000\n<<<  [Exit status: FAILURE(0x00000103)]\n";
        Assert.Empty(WindowsChangeHistoryParser.ParseSetupApi(text, From, To));
    }

    [Fact]
    public void SetupApi_MalformedAndOversizedSectionsAreIgnored()
    {
        var malformed = ">>>  [Device Install - ROOT\\X]\n>>>  Section start not-a-time\n     Driver INF - bad.inf\n<<<  [Exit status: SUCCESS]\n";
        Assert.Empty(WindowsChangeHistoryParser.ParseSetupApi(malformed, From, To));
        Assert.Empty(WindowsChangeHistoryParser.ParseSetupApi(new string('x', 300_000), From, To));
    }

    [Fact]
    public void WindowsUpdate19_UsesStructuredFieldsRegardlessOfRenderedLanguage()
    {
        var xml = "<Event><System><Provider Name='Microsoft-Windows-WindowsUpdateClient'/><EventID>19</EventID><Channel>System</Channel><TimeCreated SystemTime='2026-09-09T12:00:00.0000000Z'/></System><EventData><Data Name='updateTitle'>Aggiornamento definizioni</Data><Data Name='updateGuid'>{ABC}</Data><Data Name='updateRevisionNumber'>7</Data></EventData></Event>";
        var fields = Assert.IsType<(string Title, string Guid, string Revision)>(WindowsChangeHistoryParser.ParseWindowsUpdate19(xml));
        var change = WindowsChangeHistoryParser.CreateWindowsUpdate(fields, From, 42);
        Assert.Equal("Aggiornamento definizioni", change.Subject);
        Assert.Equal("{ABC}:7", change.UpdateIdentity);
        Assert.Null(change.NewValue);
        Assert.Equal("windowsupdate:42", change.SourceReference);
    }

    [Fact]
    public void WindowsUpdate19_RejectsWrongProviderIdAndMalformedXml()
    {
        Assert.Null(WindowsChangeHistoryParser.ParseWindowsUpdate19("<Event><System><Provider Name='Other'/><EventID>19</EventID><Channel>System</Channel></System></Event>"));
        Assert.Null(WindowsChangeHistoryParser.ParseWindowsUpdate19("<broken"));
    }

    [Fact]
    public void SetupApi_RespectsReturnedLimit()
    {
        var item = ">>>  [Device Install - ROOT\\X]\n>>>  Section start 2026/09/09 12:00:00.000\n     dvi: Selected Driver:\n     dvi: InfFile - x.inf\n     dvi: {Core Device Install}\n<<<  Section end 2026/09/09 12:00:01.000\n<<<  [Exit status: SUCCESS]\n";
        Assert.Single(WindowsChangeHistoryParser.ParseSetupApi(item + item.Replace("ROOT\\X", "ROOT\\Y"), From, To, 1));
    }

    [Fact]
    public void SetupApi_UsesCompleteLocalTimestampAndSelectedValuesOnly()
    {
        var text = ">>>  [Device Install - ROOT\\SELECTED]\n>>>  Section start 2026/09/09 12:34:56.789\n     utl: Driver Version - candidate\n     dvi: Selected Driver:\n     dvi: InfFile - selected.inf\n     dvi: Provider - SelectedVendor\n     dvi: Class GUID - {4D36E96C-E325-11CE-BFC1-08002BE10318}\n     dvi: {Core Device Install}\n<<<  Section end 2026/09/09 12:34:57.790\n<<<  [Exit status: SUCCESS]\n";
        var change = Assert.Single(WindowsChangeHistoryParser.ParseSetupApi(text, From, To, timeZone: TimeZoneInfo.Utc));
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 12, 34, 57, 790, TimeSpan.Zero), change.TimestampUtc);
        Assert.Equal("selected.inf", change.Subject);
        Assert.Equal("SelectedVendor", change.Vendor);
        Assert.Equal(ChangeSubsystem.Audio, change.Subsystem);
        Assert.Null(change.NewValue);
    }

    [Fact]
    public void SetupApi_SkipsAmbiguousAndInvalidDaylightSavingTimes()
    {
        var zone = TestDstZone();
        var ambiguous = ValidSection("ROOT\\AMB", "2026/11/01 01:30:00.000");
        var invalid = ValidSection("ROOT\\INVALID", "2026/03/08 02:30:00.000");
        Assert.Empty(WindowsChangeHistoryParser.ParseSetupApi(ambiguous + invalid, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, timeZone: zone));
    }

    [Fact]
    public void SetupApi_DoesNotLetFollowingSectionRescueTruncatedSection()
    {
        var truncated = ">>>  [Device Install - ROOT\\TRUNCATED]\n>>>  Section start 2026/09/09 01:00:00.000\n     dvi: Selected Driver:\n     dvi: InfFile - bad.inf\n     dvi: {Core Device Install}\n";
        var valid = ValidSection("ROOT\\VALID", "2026/09/09 02:00:00.000");
        var changes = WindowsChangeHistoryParser.ParseSetupApi(truncated + valid, From, To);
        Assert.Equal("good.inf", Assert.Single(changes).Subject);
    }

    [Fact]
    public void SetupApi_SupportsCancellationPerSection()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => WindowsChangeHistoryParser.ParseSetupApi(ValidSection("ROOT\\X", "2026/09/09 02:00:00.000"), From, To, cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData("{4D36E968-E325-11CE-BFC1-08002BE10318}", ChangeSubsystem.Display)]
    [InlineData("{4D36E96C-E325-11CE-BFC1-08002BE10318}", ChangeSubsystem.Audio)]
    [InlineData("{4D36E972-E325-11CE-BFC1-08002BE10318}", ChangeSubsystem.Network)]
    [InlineData("{4D36E97B-E325-11CE-BFC1-08002BE10318}", ChangeSubsystem.Storage)]
    [InlineData("{4D36E96A-E325-11CE-BFC1-08002BE10318}", ChangeSubsystem.Storage)]
    [InlineData("{4D36E97D-E325-11CE-BFC1-08002BE10318}", ChangeSubsystem.System)]
    [InlineData("{4D36E97E-E325-11CE-BFC1-08002BE10318}", ChangeSubsystem.Printer)]
    public void SetupApi_MapsKnownClassGuids(string classGuid, ChangeSubsystem expected)
    {
        var text = ValidSection("ROOT\\CLASS", "2026/09/09 03:00:00.000").Replace("{4D36E968-E325-11CE-BFC1-08002BE10318}", classGuid, StringComparison.Ordinal);
        Assert.Equal(expected, Assert.Single(WindowsChangeHistoryParser.ParseSetupApi(text, From, To)).Subsystem);
    }

    [Fact]
    public void WindowsUpdate19_RejectsDuplicateDtdOrOversizedRequiredFields()
    {
        const string prefix = "<Event><System><Provider Name='Microsoft-Windows-WindowsUpdateClient'/><EventID>19</EventID><Channel>System</Channel><TimeCreated SystemTime='2026-09-09T12:00:00Z'/></System><EventData>";
        Assert.Null(WindowsChangeHistoryParser.ParseWindowsUpdate19(prefix + "<Data Name='updateTitle'>A</Data><Data Name='updateTitle'>B</Data><Data Name='updateGuid'>g</Data></EventData></Event>"));
        Assert.Null(WindowsChangeHistoryParser.ParseWindowsUpdate19("<!DOCTYPE Event [<!ENTITY x 'x'>]>" + prefix + "<Data Name='updateTitle'>A</Data><Data Name='updateGuid'>g</Data></EventData></Event>"));
        Assert.Null(WindowsChangeHistoryParser.ParseWindowsUpdate19(prefix + "<Data Name='updateTitle'>" + new string('x', 300_000) + "</Data><Data Name='updateGuid'>g</Data></EventData></Event>"));
    }

    [Fact]
    public void WindowsUpdate19_DefinitionMatchRequiresExactKbToken()
    {
        static SystemChange Make(string title) => WindowsChangeHistoryParser.CreateWindowsUpdate((title, "g", "1"), From, 1);
        Assert.True(Make("Security Intelligence Update for KB2267602").IsDefinitionUpdate);
        Assert.False(Make("Microsoft Defender Antivirus").IsDefinitionUpdate);
        Assert.False(Make("KB22676020").IsDefinitionUpdate);
    }

    [Fact]
    public async Task Provider_CancellationBeforeReadIsObserved()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WindowsChangeHistoryProvider("Z:\\missing\\setupapi.dev.log").GetChangesAsync(From, To, cancellation.Token));
    }

    private static string ValidSection(string device, string timestamp) => $">>>  [Device Install - {device}]\n>>>  Section start {timestamp}\n     dvi: Selected Driver:\n     dvi: InfFile - {(device.EndsWith("VALID", StringComparison.Ordinal) ? "good.inf" : "selected.inf")}\n     dvi: Provider - Vendor\n     dvi: Class GUID - {{4D36E968-E325-11CE-BFC1-08002BE10318}}\n     dvi: {{Core Device Install}}\n<<<  Section end {timestamp}\n<<<  [Exit status: SUCCESS]\n";

    private static TimeZoneInfo TestDstZone() => TimeZoneInfo.CreateCustomTimeZone("Test DST", TimeSpan.Zero, "Test DST", "Test DST", "Test DST", [TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1), TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday), TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday))]);
}
