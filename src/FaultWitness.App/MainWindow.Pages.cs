using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using FaultWitness.Core;
using FaultWitness.Rules;

using Avalonia.Automation;
using FaultWitness.Platform.Windows;
using FaultWitness.Storage;
using System.Text.Json;
using FaultWitness.Export;

namespace FaultWitness.App;

public sealed partial class MainWindow
{
    private async Task CopyHistoryAsync()
    {
        if (Clipboard is null || ViewModel.SelectedHistory is not { } row) { ViewModel.Notify("ClipboardUnavailable"); return; }
        await Clipboard.SetTextAsync(HistorySummaryText(row)).ConfigureAwait(true); ViewModel.Notify("SummaryCopied");
    }
    private async Task SaveHistoryAsync()
    {
        if (ViewModel.SelectedHistory is not { } row) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = T("SaveExport"), SuggestedFileName = "FaultWitness-history.md", DefaultExtension = "md", FileTypeChoices = [new FilePickerFileType(T("ExportSummary")) { Patterns = ["*.md"] }] }).ConfigureAwait(true);
        if (file is null) return; await using var stream = await file.OpenWriteAsync().ConfigureAwait(true); stream.SetLength(0); await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)); await writer.WriteAsync(HistorySummaryText(row)).ConfigureAwait(true); ViewModel.Notify("ExportSaved");
    }
    private string HistorySummaryText(HistoryRow row)
    {
        var scan = row.Scan;
        return $"FaultWitness\n{row.Title}\n{row.Timestamp}\n{row.Period}\n{row.Counts}\nRules {scan.RulesVersion}\n" + string.Join("\n", scan.Incidents.Select(item => $"{item.OccurredUtc:O} | {item.Category} | {item.Severity} | {SavedChangeSummary(item)}"));
    }
    private string SavedChangeSummary(StoredIncident incident)
    {
        try
        {
            using var document = JsonDocument.Parse(incident.SummaryJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ChangeContext", out var context) || context.ValueKind == JsonValueKind.Null)
                return string.Empty;
            var savedContext = context.Deserialize<ChangeHistoryContext>();
            var changes = root.TryGetProperty("RelatedChanges", out var related) && related.ValueKind == JsonValueKind.Array
                ? related.Deserialize<RelatedSystemChange[]>() ?? [] : [];
            return ChangePresentation.ToMarkdown(savedContext, changes, ViewModel.Text, new ExportPrivacyOptions());
        }
        catch (JsonException) { return string.Empty; }
    }
    private static Control Place(Control control, int row) { Grid.SetRow(control, row); return control; }
    private ScrollViewer BuildReadiness()
    {
        var body = Stack(Heading("System", "SystemHelp"), SystemNavigation(), Label(T("Readiness"), TextRole.SectionTitle), Label(T("ReadinessHelp")), PrimaryAsyncButton("CheckReadiness", ViewModel.RefreshReadinessAsync, "RefreshReadiness"));
        if (ViewModel.Readiness.Count == 0) body.Children.Add(Empty("ReadinessNotChecked", "ReadinessEmptyHelp"));
        foreach (var item in ViewModel.Readiness)
        {
            var status = "ReadinessStatus" + item.Status;
            var details = new List<Control> { Label(T(item.DetailKey)) };
            if (item.Observations is { Count: > 0 })
            {
                var observations = item.Observations.Select(observation => (Control)Label(T(observation.NameKey) + ": " + (observation.ValueIsLocalizationKey ? T(observation.Value) : observation.Value))).ToArray();
                var disclosure = Expand("TechnicalDetails", Stack(observations));
                var observationNames = string.Join(", ", item.Observations.Select(observation => T(observation.NameKey)).Distinct(StringComparer.Ordinal));
                AutomationProperties.SetName(disclosure, T(item.NameKey) + " — " + T("TechnicalDetails") + (observationNames.Length == 0 ? string.Empty : " — " + observationNames));
                details.Add(disclosure);
            }
            if (!string.IsNullOrWhiteSpace(item.NextActionKey)) details.Add(Muted(T(item.NextActionKey)));
            var card = Section(item.NameKey, Stack(Label(T(status), bold: true), Stack(details.ToArray())));
            AutomationProperties.SetName(card, T(item.NameKey) + " — " + T(status));
            body.Children.Add(card);
        }
        body.Children.Add(Muted(T("ReadinessNoHealthScore")));
        return Scroll(body);
    }
    private ScrollViewer BuildSystem()
    {
        var body = Stack(Heading("System", "SystemHelp"), SystemNavigation(), Label(T("SystemInformation"), TextRole.SectionTitle));
        if (ViewModel.SystemInventory.Groups.Count == 0) body.Children.Add(Label(T(ViewModel.IsBusy ? "ReadingSources" : "NotAvailable")));
        var inventory = new Grid
        {
            Name = "SystemInventory",
            ColumnDefinitions = new ColumnDefinitions(layoutClass == "large" ? "*,*" : "*"),
            ColumnSpacing = D("primitive.space.6"), RowSpacing = D("primitive.space.4")
        };
        var index = 0;
        var order = new[] { WindowsSystemInventory.InventoryGroupOperatingSystem, WindowsSystemInventory.InventoryGroupProcessorMemory,
            WindowsSystemInventory.InventoryGroupGraphics, WindowsSystemInventory.InventoryGroupFirmware, WindowsSystemInventory.InventoryGroupStorage, WindowsSystemInventory.InventoryGroupDrivers };
        foreach (var group in ViewModel.SystemInventory.Groups.OrderBy(g => Array.IndexOf(order, g.Key) < 0 ? int.MaxValue : Array.IndexOf(order, g.Key)))
        {
            var row = index / inventory.ColumnDefinitions.Count;
            if (row >= inventory.RowDefinitions.Count) inventory.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var section = InventoryGroupSection(group);
            Grid.SetRow(section, row); Grid.SetColumn(section, index % inventory.ColumnDefinitions.Count);
            inventory.Children.Add(section); index++;
        }
        body.Children.Add(inventory);
        body.Children.Add(AsyncButton("ReadSystem", ViewModel.RefreshInventoryAsync, "ReadSystem"));
        body.Children.Add(BuildCaptureSection());
        return Scroll(body);
    }
    private StackPanel InventoryGroupSection(InventoryGroup group)
    {
        var content = new List<Control>();
        if (group.Devices.Count == 0) content.Add(Muted(T("InventoryGroupEmpty")));
        var isCollapsed = group.Key is WindowsSystemInventory.InventoryGroupStorage or WindowsSystemInventory.InventoryGroupDrivers;
        var devices = isCollapsed ? group.Devices.Take(1).ToArray() : group.Devices.ToArray();
        foreach (var device in devices)
        {
            var fields = device.Fields.Select(field => (Control)InventoryFieldLabel(field)).ToArray();
            content.Add(Stack(fields));
        }
        if (isCollapsed && group.Devices.Count > 1)
        {
            var more = Expand("InventoryMoreDevices", Stack(group.Devices.Skip(1).Select(d => (Control)Stack(d.Fields.Select(InventoryFieldLabel).ToArray())).ToArray()));
            AutomationProperties.SetName(more, T("InventoryMoreDevices"));
            content.Add(more);
        }
        var section = Section(group.Key, Stack(content.ToArray()));
        AutomationProperties.SetName(section, T(group.Key));
        return section;
    }
    private Control InventoryFieldLabel(InventoryField field)
    {
        var value = field.Availability == InventoryAvailability.Available && !string.IsNullOrWhiteSpace(field.Value)
            ? field.Value : T("InventoryAvailability" + field.Availability);
        return Label(T(field.Key) + ": " + value);
    }
    private WrapPanel SystemNavigation() => Actions(
        NavigationButton("SystemInformation", () => ViewModel.Navigate(AppPage.System), "SystemInformationTab", ViewModel.Page == AppPage.System),
        NavigationButton("Readiness", () => ViewModel.Navigate(AppPage.Readiness), "SystemReadinessTab", ViewModel.Page == AppPage.Readiness));
    private async Task BrowseImportsAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("Browse"), AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(T("DiagnosticFiles")) { Patterns = ["*.evtx", "*.wer", "*.zip"] }]
        }).ConfigureAwait(true);
        ViewModel.AddImports(files.Select(file => file.TryGetLocalPath()).OfType<string>());
    }
    private ScrollViewer BuildSettings()
    {
        var languages = new[] { "system", "en", "it", "es", "fr", "de", "pt", "ru", "pl" };
        var language = new ComboBox { Name = "LanguageSelector", Width = 270, ItemsSource = new[] { T("SystemDefault"), "English", "Italiano", "Español", "Français", "Deutsch", "Português", "Русский", "Polski" }, SelectedIndex = Math.Max(0, Array.IndexOf(languages, ViewModel.Settings.Language)) };
        var theme = new ComboBox { Name = "ThemeSelector", Width = 200, ItemsSource = new[] { T("ThemeSystem"), T("ThemeLight"), T("ThemeDark") }, SelectedIndex = (int)ViewModel.Settings.Theme };
        var period = new ComboBox { Name = "DefaultPeriod", Width = 270, ItemsSource = new[] { T("PeriodDay"), T("PeriodWeek"), T("PeriodMonth") }, SelectedIndex = Math.Min(2, (int)ViewModel.Settings.Period) };
        var retentionValues = new[] { 0, 7, 30, 90, 365 };
        var retention = new ComboBox { Name = "RetentionSelector", Width = 200, ItemsSource = retentionValues.Select(days => days == 0 ? T("DoNotRetain") : ViewModel.Text.Format("Days", days)).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(retentionValues, ViewModel.Settings.RetentionDays)) };
        language.SelectionChanged += (_, _) => { if (language.SelectedIndex >= 0) ViewModel.ChangeSettings(ViewModel.Settings with { Language = languages[language.SelectedIndex] }); };
        theme.SelectionChanged += (_, _) => { if (theme.SelectedIndex >= 0) ViewModel.ChangeSettings(ViewModel.Settings with { Theme = (AppTheme)theme.SelectedIndex }); };
        period.SelectionChanged += (_, _) => { if (period.SelectedIndex >= 0) ViewModel.ChangeSettings(ViewModel.Settings with { Period = (AnalysisPeriod)period.SelectedIndex }); };
        retention.SelectionChanged += (_, _) => { if (retention.SelectedIndex >= 0) ViewModel.ChangeSettings(ViewModel.Settings with { RetentionDays = retentionValues[retention.SelectedIndex] }); };
        var body = Stack(Heading("Settings", "SettingsPurpose"), Section("General", Actions(Field("Language", language), Field("Theme", theme))),
            Section("Analysis", Field("DefaultAnalysisPeriod", period)), Section("Privacy", Stack(Field("HistoryRetention", retention), Muted(T("RetentionHelp")), DangerButton("ClearData", () => _ = ConfirmClearAsync(), "ClearData"))),
            Section("About", Stack(Label(ReleaseIdentity.Display), Label(T("RuleDatabase") + " " + RuleCatalog.DatabaseVersion), Label(T("License") + ": MIT"), Label(T("PrivacyStatement")))));
        return Scroll(body);
    }
    private async Task ConfirmClearAsync()
    {
        var dialog = new Window { Title = T("ClearData"), Width = 480, Height = 250, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var clear = DangerButton("ConfirmClear", () => dialog.Close(true));
        dialog.Content = new Border { Padding = new Thickness(D("primitive.space.6")), Child = Stack(Label(T("ClearDataWarning")), Actions(clear, Button("Cancel", () => dialog.Close(false)))) };
        if (await dialog.ShowDialog<bool>(this).ConfigureAwait(true)) await ViewModel.ClearDataAsync().ConfigureAwait(true);
    }
}
