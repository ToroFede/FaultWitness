using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "SystemPagesVisualValidation")]
public sealed class SystemPagesVisualValidationTests
{
    private static readonly string[] ReadinessSourceNames = ["SourceSystem", "SourceWer", "SourceArtifacts", "SourceReliability", "SourceApplication"];

    private sealed record Scenario(string Name, AppPage Page, string Language, AppTheme Theme, int Width,
        bool InventoryFailure = false, InventoryAvailability? InventoryState = null,
        DiagnosticCapabilityStatus[]? ReadinessStates = null, bool HasResult = true,
        ExportFormat? Format = null, bool SelectIncident = false, int ResultCount = 24);

    [AvaloniaFact]
    public async Task SystemReadinessSettingsAndExportSupport_RenderBoundedSyntheticMatrix()
    {
        var scenarios = new List<Scenario>
        {
            new("system-default-en-light-desktop", AppPage.System, "en", AppTheme.Light, 1280),
            new("system-inventory-en-dark-desktop", AppPage.System, "en", AppTheme.Dark, 1280),
            new("system-limited-de-dark-compact", AppPage.System, "de", AppTheme.Dark, 600, InventoryState: InventoryAvailability.AccessDenied),
            new("system-error-ru-light-large", AppPage.System, "ru", AppTheme.Light, 1920, InventoryFailure: true),
            new("system-unknown-it-light-large", AppPage.System, "it", AppTheme.Light, 1008, InventoryState: InventoryAvailability.NotSupported),

            new("readiness-complete-en-light-desktop", AppPage.Readiness, "en", AppTheme.Light, 1280, ReadinessStates: [DiagnosticCapabilityStatus.Ready]),
            new("readiness-partial-en-dark-desktop", AppPage.Readiness, "en", AppTheme.Dark, 1280, ReadinessStates: [DiagnosticCapabilityStatus.Limited]),
            new("readiness-access-denied-de-dark-compact", AppPage.Readiness, "de", AppTheme.Dark, 600, ReadinessStates: [DiagnosticCapabilityStatus.AccessDenied]),
            new("readiness-not-supported-ru-light-compact", AppPage.Readiness, "ru", AppTheme.Light, 560, ReadinessStates: [DiagnosticCapabilityStatus.NotSupported]),
            new("readiness-mixed-it-light-large", AppPage.Readiness, "it", AppTheme.Light, 1920, ReadinessStates: [DiagnosticCapabilityStatus.Ready, DiagnosticCapabilityStatus.Limited, DiagnosticCapabilityStatus.Unavailable, DiagnosticCapabilityStatus.AccessDenied, DiagnosticCapabilityStatus.NotSupported]),

            new("settings-en-light-desktop", AppPage.Settings, "en", AppTheme.Light, 1280),
            new("settings-en-dark-desktop", AppPage.Settings, "en", AppTheme.Dark, 1280),
            new("settings-long-de-dark-compact", AppPage.Settings, "de", AppTheme.Dark, 600),
            new("settings-long-ru-light-boundary", AppPage.Settings, "ru", AppTheme.Light, 641),
            new("settings-long-pl-dark-large", AppPage.Settings, "pl", AppTheme.Dark, 1920),

            new("export-normal-en-light-desktop", AppPage.Export, "en", AppTheme.Light, 1280),
            new("export-options-en-dark-desktop", AppPage.Export, "en", AppTheme.Dark, 1280, Format: ExportFormat.Json, SelectIncident: true),
            new("export-selected-de-dark-compact", AppPage.Export, "de", AppTheme.Dark, 600, Format: ExportFormat.Json, SelectIncident: true),
            new("export-bundle-it-light-large", AppPage.Export, "it", AppTheme.Light, 1920, Format: ExportFormat.Bundle),
            new("export-no-result-pl-light-boundary", AppPage.Export, "pl", AppTheme.Light, 641, HasResult: false),

            new("boundary-560-system-de-dark", AppPage.System, "de", AppTheme.Dark, 560),
            new("boundary-600-readiness-ru-dark", AppPage.Readiness, "ru", AppTheme.Dark, 600, ReadinessStates: [DiagnosticCapabilityStatus.Ready, DiagnosticCapabilityStatus.AccessDenied]),
            new("boundary-640-settings-it-light", AppPage.Settings, "it", AppTheme.Light, 640),
            new("boundary-641-export-de-dark", AppPage.Export, "de", AppTheme.Dark, 641),
            new("boundary-1008-system-it-light", AppPage.System, "it", AppTheme.Light, 1008),
            new("boundary-1280-readiness-pl-light", AppPage.Readiness, "pl", AppTheme.Light, 1280, ReadinessStates: [DiagnosticCapabilityStatus.Limited, DiagnosticCapabilityStatus.NotSupported]),
            new("boundary-1920-export-ru-dark", AppPage.Export, "ru", AppTheme.Dark, 1920),

            new("locale-en-system-compact", AppPage.System, "en", AppTheme.Light, 560),
            new("locale-it-readiness-compact", AppPage.Readiness, "it", AppTheme.Dark, 600, ReadinessStates: [DiagnosticCapabilityStatus.Limited]),
            new("locale-es-settings-compact", AppPage.Settings, "es", AppTheme.Light, 640),
            new("locale-fr-export-compact", AppPage.Export, "fr", AppTheme.Dark, 641),
            new("locale-de-system-compact", AppPage.System, "de", AppTheme.Dark, 600),
            new("locale-pt-readiness-compact", AppPage.Readiness, "pt", AppTheme.Light, 560, ReadinessStates: [DiagnosticCapabilityStatus.AccessDenied]),
            new("locale-ru-settings-compact", AppPage.Settings, "ru", AppTheme.Dark, 640),
            new("locale-pl-export-compact", AppPage.Export, "pl", AppTheme.Light, 641)
        };

        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_SYSTEM_PAGES_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);
        foreach (var scenario in scenarios)
            await Render(scenario, output);
    }

    private static async Task Render(Scenario scenario, string? output)
    {
        var services = new TestServices { Settings = new UserSettings(Language: scenario.Language), FailInventory = scenario.InventoryFailure };
        var groups = new List<InventoryGroup>
        {
            new("InventoryGroupOperatingSystem", [new("os-id", [new("InventoryOperatingSystem", "Synthetic Windows 11"), new("InventoryOsVersion", "Synthetic 1.0")])]),
            new("InventoryGroupProcessorMemory", [new("cpu-id", [new("InventoryProcessorLogical", "8"), new("InventoryArchitecture", "x64")])]),
            new("InventoryGroupGraphics", [new("gpu-id", [new("InventoryGraphicsName", scenario.InventoryState is null ? "Synthetic GPU" : null, scenario.InventoryState ?? InventoryAvailability.Available)])]),
            new("InventoryGroupFirmware", [new("firmware-id", [new("InventoryBiosVendor", "Synthetic Firmware")])]),
            new("InventoryGroupStorage", [new("disk-id", [new("InventoryStorageModel", "Synthetic NVMe")])]),
            new("InventoryGroupDrivers", [new("driver-id", [new("InventoryDriverName", "Synthetic Driver")])])
        };
        services.StructuredInventory = new SystemInventorySnapshot(groups);
        var statuses = scenario.ReadinessStates ?? [DiagnosticCapabilityStatus.Ready, DiagnosticCapabilityStatus.Limited];
        services.StructuredReadiness = statuses.Select((status, index) => new DiagnosticReadinessItem(
            "source-id-" + index, ReadinessSourceNames[index % ReadinessSourceNames.Length], status,
            "ReadinessHelp", [new DiagnosticObservation("SourceSystem", "synthetic retained interval")], true, "ReadinessHelp")).ToArray();

        using var viewModel = new MainViewModel(services);
        if (scenario.HasResult) viewModel.SetResult(SyntheticResults.Create(scenario.ResultCount));
        if (scenario.SelectIncident) viewModel.Select(viewModel.AllRows[0]);
        var window = new MainWindow(viewModel);
        VisualRenderGeometry.ShowAtRequestedGeometry(window, scenario.Width, 900);
        try
        {
            viewModel.ChangeSettings(viewModel.Settings with { Language = scenario.Language, Theme = scenario.Theme });
            viewModel.Navigate(scenario.Page);
            if (scenario.Page == AppPage.System) await viewModel.RefreshInventoryAsync();
            if (scenario.Page == AppPage.Readiness) await viewModel.RefreshReadinessAsync();
            window.UpdateLayout();

            if (scenario.Page == AppPage.Export && scenario.Format is { } format)
            {
                window.GetVisualDescendants().OfType<ComboBox>().Single(item => item.Name == "ExportFormat").SelectedIndex = (int)format;
                window.UpdateLayout();
            }

            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), item => item.IsVisible && !string.IsNullOrWhiteSpace(item.Text));
            Assert.All(window.GetVisualDescendants().OfType<ScrollViewer>()
                .Where(viewer => !viewer.GetVisualAncestors().OfType<TextBox>().Any()), viewer =>
                Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 2 || viewer.Viewport.Width == 0,
                    $"{scenario.Name}: page overflow {viewer.Extent.Width} > {viewer.Viewport.Width}"));

            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{scenario.Name} produced no render.");
            VisualRenderGeometry.AssertFrameMatches(window, frame, scenario.Width, 900, scenario.Name);
            if (!string.IsNullOrWhiteSpace(output)) frame.Save(Path.Combine(output, scenario.Name + ".png"), new PngBitmapEncoderOptions());
        }
        finally { window.Close(); }
    }
}
