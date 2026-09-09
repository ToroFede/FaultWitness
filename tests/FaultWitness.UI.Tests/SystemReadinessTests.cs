using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Headless")]
public sealed class SystemReadinessTests
{
    private static readonly string[] InventoryGroupKeys = ["InventoryGroupDrivers", "InventoryGroupOperatingSystem", "InventoryGroupStorage", "InventoryGroupGraphics", "InventoryGroupFirmware", "InventoryGroupProcessorMemory"];
    [AvaloniaFact]
    public async Task Readiness_RendersStatusDetailsAndCollapsedTechnicalObservations()
    {
        var services = new TestServices();
        using var vm = new MainViewModel(services);
        var window = new MainWindow(vm);
        window.Show();
        try
        {
            services.StructuredReadiness = [new DiagnosticReadinessItem("capability", "Readiness", DiagnosticCapabilityStatus.Disabled,
                "ReadinessSourceUnavailable", [new DiagnosticObservation("SourceSystem", "secret", false)], false, "ReadinessHelp")];
            window.ViewModel.Navigate(AppPage.Readiness);
            await window.ViewModel.RefreshReadinessAsync();
            window.UpdateLayout();
            Assert.Contains("Disabled / not configured", VisibleText(window));
            var technical = window.GetVisualDescendants().OfType<Expander>().Single(e => AutomationProperties.GetName(e) is { } name && name.StartsWith("Diagnostic readiness", StringComparison.OrdinalIgnoreCase));
            Assert.False(technical.IsExpanded);
            Assert.NotEmpty(technical.GetVisualDescendants().OfType<TextBlock>());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("it")]
    public async Task Readiness_RendersEveryCapabilityStatusThroughLocalization(string language)
    {
        var services = new TestServices { Settings = new UserSettings(Language: language) };
        services.StructuredReadiness = Enum.GetValues<DiagnosticCapabilityStatus>().Select((status, i) =>
            new DiagnosticReadinessItem("cap" + i, "Readiness", status, "ReadinessHelp")).ToArray();
        using var vm = new MainViewModel(services); var window = new MainWindow(vm); window.Show();
        try { vm.Navigate(AppPage.Readiness); await vm.RefreshReadinessAsync(); window.UpdateLayout();
            var text = VisibleText(window);
            Assert.All(Enum.GetValues<DiagnosticCapabilityStatus>(), status => Assert.Contains(vm.Text.Get("ReadinessStatus" + status), text));
        } finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Readiness_AccessibleNamesIncludeSourceAndStatus_AndObservationExpanderIsSafe()
    {
        var services = new TestServices { StructuredReadiness = [new("cap", "Readiness", DiagnosticCapabilityStatus.AccessDenied, "ReadinessHelp", [new("SourceSystem", "private detail")])] };
        using var vm = new MainViewModel(services); var window = new MainWindow(vm); window.Show();
        try { vm.Navigate(AppPage.Readiness); await vm.RefreshReadinessAsync(); window.UpdateLayout();
            var card = window.GetVisualDescendants().OfType<Control>().First(c => AutomationProperties.GetName(c)?.Contains(vm.Text.Get("ReadinessStatusAccessDenied"), StringComparison.Ordinal) == true);
            Assert.Contains(vm.Text.Get("Readiness"), AutomationProperties.GetName(card));
            var expander = window.GetVisualDescendants().OfType<Expander>().Single(e => AutomationProperties.GetName(e)?.Contains(vm.Text.Get("TechnicalDetails"), StringComparison.Ordinal) == true); Assert.False(expander.IsExpanded); expander.IsExpanded = true; window.UpdateLayout();
            Assert.Contains("private detail", VisibleText(window)); Assert.Contains(vm.Text.Get("SourceSystem"), AutomationProperties.GetName(expander));
        } finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task System_RendersGroupsInContractOrderAndAvailabilityText()
    {
        var groups = InventoryGroupKeys
            .Select((key, i) => new InventoryGroup(key, [new("one", [new("InventoryOperatingSystem", "safe", i == 1 ? InventoryAvailability.Unavailable : InventoryAvailability.Available), new("InventoryGraphicsName", null, InventoryAvailability.AccessDenied), new("InventoryProcessor", null, InventoryAvailability.NotSupported)])])).ToArray();
        var services = new TestServices { StructuredInventory = new SystemInventorySnapshot(groups) };
        using var vm = new MainViewModel(services); var window = new MainWindow(vm); window.Show();
        try { vm.Navigate(AppPage.System); await vm.RefreshInventoryAsync(); window.UpdateLayout(); var sections = window.GetVisualDescendants().OfType<Control>().Select(c => AutomationProperties.GetName(c)).OfType<string>().Where(n => n == vm.Text.Get("InventoryGroupOperatingSystem") || n == vm.Text.Get("InventoryGroupGraphics") || n == vm.Text.Get("InventoryGroupFirmware") || n == vm.Text.Get("InventoryGroupStorage") || n == vm.Text.Get("InventoryGroupDrivers") || n == vm.Text.Get("InventoryGroupProcessorMemory")).ToArray(); Assert.Equal(6, sections.Length); Assert.Contains(vm.Text.Get("InventoryAvailabilityUnavailable"), VisibleText(window)); Assert.Contains(vm.Text.Get("InventoryAvailabilityAccessDenied"), VisibleText(window)); Assert.Contains(vm.Text.Get("InventoryAvailabilityNotSupported"), VisibleText(window)); } finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task System_CollapsesAdditionalStorageAndDriverDevices()
    {
        var field = new InventoryField("InventoryStorageModel", "safe"); var groups = new[] { new InventoryGroup("InventoryGroupStorage", [new("a", [field]), new("b", [field with { Value = "second" }])]), new InventoryGroup("InventoryGroupDrivers", [new("a", [new("InventoryDriverName", "driver")]), new("b", [new("InventoryDriverName", "second driver")])]) };
        var services = new TestServices { StructuredInventory = new SystemInventorySnapshot(groups) }; using var vm = new MainViewModel(services); var window = new MainWindow(vm); window.Show();
        try { vm.Navigate(AppPage.System); await vm.RefreshInventoryAsync(); window.UpdateLayout(); var expanders = window.GetVisualDescendants().OfType<Expander>().Where(e => AutomationProperties.GetName(e) == vm.Text.Get("InventoryMoreDevices")).ToArray(); Assert.Equal(2, expanders.Length); Assert.All(expanders, e => Assert.False(e.IsExpanded)); Assert.Contains("safe", VisibleText(window)); expanders[0].IsExpanded = true; expanders[1].IsExpanded = true; window.UpdateLayout(); Assert.Contains("second", VisibleText(window)); Assert.Contains("second driver", VisibleText(window)); } finally { window.Close(); }
    }

    private static string VisibleText(Control root) => string.Join(" ", root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));
}
