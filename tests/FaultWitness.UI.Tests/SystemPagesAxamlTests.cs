using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;
using FaultWitness.Localization;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Headless")]
public sealed class SystemPagesAxamlTests
{
    private static readonly string[] Languages = ["en", "it", "es", "fr", "de", "pt", "ru", "pl"];

    [AvaloniaFact]
    public async Task System_InventoryAndCaptureEntryRemainAvailable_AndFailureIsReadable()
    {
        var services = new TestServices
        {
            StructuredInventory = new SystemInventorySnapshot([
                new InventoryGroup("InventoryGroupOperatingSystem", [new("synthetic-private-device-id", [new("InventoryOperatingSystem", "Synthetic Windows")])]),
                new InventoryGroup("InventoryGroupGraphics", [new("synthetic-gpu-id", [new("InventoryGraphicsName", null, InventoryAvailability.AccessDenied)])])])
        };
        using var viewModel = new MainViewModel(services);
        var window = Open(viewModel);
        try
        {
            viewModel.Navigate(AppPage.System);
            await viewModel.RefreshInventoryAsync();
            window.UpdateLayout();
            var view = Assert.IsType<SystemView>(Find<ContentControl>(window, "PageHost").Content);
            var text = VisibleText(window);
            Assert.Contains("Synthetic Windows", text, StringComparison.Ordinal);
            Assert.Contains(viewModel.Text.Get("InventoryAvailabilityAccessDenied"), text, StringComparison.Ordinal);
            Assert.Contains(viewModel.Text.Get("CaptureTitle"), text, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-private-device-id", text, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-gpu-id", text, StringComparison.Ordinal);
            Assert.NotNull(Find<Button>(window, "SystemInformationTab"));
            Assert.NotNull(Find<Button>(window, "SystemReadinessTab"));

            services.FailInventory = true;
            await viewModel.RefreshInventoryAsync();
            window.UpdateLayout();
            Assert.Same(view, Find<ContentControl>(window, "PageHost").Content);
            Assert.Contains("Synthetic Windows", VisibleText(window), StringComparison.Ordinal);
            Assert.Contains(viewModel.Text.Get("SourceUnavailable"), VisibleText(window), StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SystemSubpageAndReadinessStatesSurviveRuntimeLocaleAndThemeChanges()
    {
        var services = new TestServices
        {
            StructuredReadiness = Enum.GetValues<DiagnosticCapabilityStatus>()
                .Select((status, index) => new DiagnosticReadinessItem("synthetic-" + index, "Readiness", status, "ReadinessHelp"))
                .ToArray()
        };
        using var viewModel = new MainViewModel(services);
        var window = Open(viewModel);
        try
        {
            viewModel.Navigate(AppPage.Readiness);
            await viewModel.RefreshReadinessAsync();
            var readiness = Assert.IsType<DiagnosticReadinessView>(Find<ContentControl>(window, "PageHost").Content);
            foreach (var language in Languages)
            {
                viewModel.ChangeSettings(viewModel.Settings with { Language = language, Theme = language == "de" ? AppTheme.Dark : AppTheme.Light });
                window.UpdateLayout();
                Assert.Same(readiness, Find<ContentControl>(window, "PageHost").Content);
                Assert.Contains("selected", Find<Button>(window, "SystemReadinessTab").Classes);
                Assert.Equal(viewModel.Text.Get("Readiness"), AutomationProperties.GetName(Find<Button>(window, "SystemReadinessTab")));
                var visible = VisibleText(window);
                Assert.All(Enum.GetValues<DiagnosticCapabilityStatus>(), status =>
                    Assert.Contains(viewModel.Text.Get("ReadinessStatus" + status), visible, StringComparison.Ordinal));
                Assert.Contains(viewModel.Text.Get("ReadinessNoHealthScore"), visible, StringComparison.Ordinal);
                Assert.DoesNotContain(viewModel.Text.Get("HealthScore"), visible, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.Name?.StartsWith("CaptureConfigure", StringComparison.Ordinal) == true);
            }

            viewModel.Navigate(AppPage.System);
            window.UpdateLayout();
            Assert.Equal(AppPage.System, viewModel.Page);
            Assert.Contains("selected", Find<Button>(window, "SystemInformationTab").Classes);
            viewModel.Navigate(AppPage.Readiness);
            Assert.Contains("selected", Find<Button>(window, "SystemReadinessTab").Classes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Settings_SelectionsPersistAndRefreshInPlaceWithoutDroppingFocus()
    {
        var services = new TestServices { Settings = new UserSettings(Language: "en") };
        using var viewModel = new MainViewModel(services);
        viewModel.SetResult(SyntheticResults.Create(3));
        viewModel.SetFilter(new IncidentFilter(Search: "SearchTarget"));
        var window = Open(viewModel);
        try
        {
            viewModel.Navigate(AppPage.Settings);
            window.UpdateLayout();
            var settings = Assert.IsType<SettingsView>(Find<ContentControl>(window, "PageHost").Content);
            var language = Find<ComboBox>(window, "LanguageSelector");
            Assert.True(language.Focus());
            language.SelectedIndex = 5;
            window.UpdateLayout();
            Assert.Equal("de", viewModel.Settings.Language);
            Assert.Equal(viewModel.Settings, services.Settings);
            Assert.Same(settings, Find<ContentControl>(window, "PageHost").Content);
            Assert.True(language.IsKeyboardFocusWithin);

            Find<ComboBox>(window, "ThemeSelector").SelectedIndex = (int)AppTheme.Dark;
            Find<ComboBox>(window, "DefaultPeriod").SelectedIndex = (int)AnalysisPeriod.Month;
            Find<ComboBox>(window, "RetentionSelector").SelectedIndex = 2;
            window.UpdateLayout();
            Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);
            Assert.Equal(AnalysisPeriod.Month, viewModel.Settings.Period);
            Assert.Equal(30, viewModel.Settings.RetentionDays);
            Assert.Equal(viewModel.Settings, services.Settings);
            Assert.Same(settings, Find<ContentControl>(window, "PageHost").Content);

            var themeSelector = Find<ComboBox>(window, "ThemeSelector");
            foreach (var themeIndex in new[] { (int)AppTheme.System, (int)AppTheme.Light, (int)AppTheme.Dark })
            {
                themeSelector.SelectedIndex = themeIndex;
                window.UpdateLayout();
                Assert.Equal(themeIndex switch { 0 => ThemeVariant.Default, 1 => ThemeVariant.Light, _ => ThemeVariant.Dark }, window.RequestedThemeVariant);
                Assert.Same(settings, Find<ContentControl>(window, "PageHost").Content);
            }

            foreach (var locale in Languages)
            {
                viewModel.ChangeSettings(viewModel.Settings with { Language = locale });
                window.UpdateLayout();
                Assert.Same(settings, Find<ContentControl>(window, "PageHost").Content);
                Assert.Equal(locale, viewModel.Settings.Language);
                Assert.Equal((int)AppTheme.Dark, Find<ComboBox>(window, "ThemeSelector").SelectedIndex);
                Assert.Equal((int)AnalysisPeriod.Month, Find<ComboBox>(window, "DefaultPeriod").SelectedIndex);
                Assert.Equal(2, Find<ComboBox>(window, "RetentionSelector").SelectedIndex);
            }
            Assert.Equal("SearchTarget", viewModel.Filter.Search);
            Assert.Equal(3, viewModel.AllRows.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ClearHistoryConfirmation_IsLocalizedAndUsesNamedSemanticButtons()
    {
        var text = new LocalizationService();
        text.SetCulture("de");
#pragma warning disable CA2000 // The opened MainWindow owns and disposes the supplied view model.
        var owner = Open(new MainViewModel(new TestServices { Settings = new UserSettings(Language: "de", Theme: AppTheme.Dark) }));
#pragma warning restore CA2000
        var dialog = new ClearDataConfirmationWindow();
        dialog.Prepare(text, owner);
        var result = dialog.ShowDialog<bool>(owner);
        try
        {
            dialog.UpdateLayout();
            Assert.Equal(ThemeVariant.Dark, dialog.ActualThemeVariant);
            Assert.Contains(text.Get("ClearDataWarning"), string.Join(" ", dialog.GetVisualDescendants()
                .OfType<TextBlock>().Where(control => control.IsVisible).Select(control => control.Text)), StringComparison.Ordinal);
            var confirm = dialog.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "ConfirmClear");
            var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "CancelClear");
            Assert.Equal(text.Get("ConfirmClear"), AutomationProperties.GetName(confirm));
            Assert.Equal(text.Get("Cancel"), AutomationProperties.GetName(cancel));
            Assert.True(cancel.IsCancel);
            Assert.Contains("danger-action", confirm.Classes);
            confirm.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(await result);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public void ExportSupport_UsesExistingRedactedFormatterAndPreservesFormatAndScope()
    {
        using var viewModel = new MainViewModel(new TestServices());
        viewModel.SetResult(SyntheticResults.Create(4));
        viewModel.Select(viewModel.AllRows[0]);
        var window = Open(viewModel);
        try
        {
            viewModel.Navigate(AppPage.Export);
            window.UpdateLayout();
            var view = Assert.IsType<ExportSupportView>(Find<ContentControl>(window, "PageHost").Content);
            var preview = Find<TextBox>(window, "ExportPreview");
            Assert.DoesNotContain("synthetic-private", preview.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("<Event>", preview.Text, StringComparison.Ordinal);
            Assert.Contains(viewModel.Text.Get("RedactionNotice"), VisibleText(window), StringComparison.Ordinal);
            Assert.Equal(viewModel.Text.Get("Format"), AutomationProperties.GetName(Find<ComboBox>(window, "ExportFormat")));
            Assert.Equal(viewModel.Text.Get("Scope"), AutomationProperties.GetName(Find<ComboBox>(window, "ExportScope")));

            Find<ComboBox>(window, "ExportFormat").SelectedIndex = (int)ExportFormat.Json;
            using var full = JsonDocument.Parse(window.ExportPreview);
            Assert.Equal(4, full.RootElement.GetProperty("Incidents").GetArrayLength());
            Find<ComboBox>(window, "ExportScope").SelectedIndex = 1;
            using var selected = JsonDocument.Parse(window.ExportPreview);
            Assert.Equal(1, selected.RootElement.GetProperty("Incidents").GetArrayLength());
            Assert.DoesNotContain("synthetic-private", window.ExportPreview, StringComparison.Ordinal);
            Assert.Equal((int)ExportFormat.Json, Find<ComboBox>(window, "ExportFormat").SelectedIndex);
            Assert.Equal(1, Find<ComboBox>(window, "ExportScope").SelectedIndex);
            Assert.Same(view, Find<ContentControl>(window, "PageHost").Content);
            Assert.NotNull(Find<Button>(window, "CopySupport"));
            Assert.NotNull(Find<Button>(window, "SaveExport"));

            viewModel.ChangeSettings(viewModel.Settings with { Language = "ru", Theme = AppTheme.Dark });
            window.UpdateLayout();
            Assert.Same(view, Find<ContentControl>(window, "PageHost").Content);
            Assert.Equal((int)ExportFormat.Json, Find<ComboBox>(window, "ExportFormat").SelectedIndex);
            Assert.Equal(1, Find<ComboBox>(window, "ExportScope").SelectedIndex);
            Assert.Contains(viewModel.Text.Get("RedactionNotice"), VisibleText(window), StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")]
    [InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public async Task AllMigratedPagesRemainLocalizedAndReadableAtCompactAndDesktopWidths(string language)
    {
        var services = new TestServices
        {
            StructuredReadiness = Enum.GetValues<DiagnosticCapabilityStatus>()
                .Select((status, index) => new DiagnosticReadinessItem("src-" + index, "Readiness", status, "ReadinessHelp"))
                .ToArray(),
            StructuredInventory = new SystemInventorySnapshot([
                new InventoryGroup("InventoryGroupOperatingSystem", [new("os", [new("InventoryOperatingSystem", "Synthetic Windows")])])])
        };
        using var viewModel = new MainViewModel(services);
        viewModel.SetResult(SyntheticResults.Create(2));
        var window = Open(viewModel);
        try
        {
            foreach (var width in new[] { 560, 1280 })
            foreach (var page in new[] { AppPage.System, AppPage.Readiness, AppPage.Settings, AppPage.Export })
            {
                viewModel.ChangeSettings(viewModel.Settings with { Language = language, Theme = width == 560 ? AppTheme.Dark : AppTheme.Light });
                viewModel.Navigate(page);
                if (page == AppPage.System) await viewModel.RefreshInventoryAsync();
                if (page == AppPage.Readiness) await viewModel.RefreshReadinessAsync();
                window.Width = width;
                window.Height = 900;
                window.UpdateLayout();
                var visible = VisibleText(window);
                Assert.NotEmpty(visible);
                Assert.Contains(viewModel.Text.Get(page == AppPage.Export ? "Export" : page == AppPage.Settings ? "Settings" : "System"), visible, StringComparison.Ordinal);
                Assert.All(window.GetVisualDescendants().OfType<ScrollViewer>()
                    .Where(viewer => !viewer.GetVisualAncestors().OfType<TextBox>().Any()), viewer =>
                    Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 2 || viewer.Viewport.Width == 0,
                        $"{language}/{page}/{width}: {viewer.Extent.Width} > {viewer.Viewport.Width}"));
            }
        }
        finally { window.Close(); }
    }

    private static MainWindow Open(MainViewModel viewModel, double width = 1280)
    {
        var window = new MainWindow(viewModel) { Width = width, Height = 900 };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static T Find<T>(MainWindow window, string name) where T : Control
    {
        var match = window.GetVisualDescendants().OfType<T>().SingleOrDefault(control => control.Name == name);
        return match ?? throw new InvalidOperationException($"{typeof(T).Name} named '{name}' was not found in the current visual tree.");
    }

    private static string VisibleText(MainWindow window) => string.Join(" ",
        window.GetVisualDescendants().OfType<TextBlock>().Where(control => control.IsVisible).Select(control => control.Text));
}
