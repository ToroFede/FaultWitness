using FaultWitness.Core;
using FaultWitness.Export;
using FaultWitness.Localization;
using FaultWitness.Platform.Windows;
using FaultWitness.Rules;

namespace FaultWitness.App.Presentation;

public sealed class SystemPresentation : PagePresentation
{
    private static readonly string[] InventoryOrder =
    [
        WindowsSystemInventory.InventoryGroupOperatingSystem,
        WindowsSystemInventory.InventoryGroupProcessorMemory,
        WindowsSystemInventory.InventoryGroupGraphics,
        WindowsSystemInventory.InventoryGroupFirmware,
        WindowsSystemInventory.InventoryGroupStorage,
        WindowsSystemInventory.InventoryGroupDrivers
    ];

    public LocalizedLabels Text { get; private set; } = new(new LocalizationService());
    public InventoryPresentation Inventory { get; private set; } = new();
    public string InventoryStatus { get; private set; } = string.Empty;
    public bool HasInventoryStatus => Inventory.Groups.Count == 0;
    public string RefreshLabel { get; private set; } = string.Empty;
    public bool IsBusy { get; private set; }

    public void Refresh(MainViewModel source, string layoutClass)
    {
        Text = new(source.Text);
        Inventory = InventoryPresentation.From(source.SystemInventory, source.Text, layoutClass);
        IsBusy = source.IsBusy;
        InventoryStatus = source.Text.Get(source.IsBusy ? "ReadingSources" : "NotAvailable");
        RefreshLabel = source.Text.Get("ReadSystem");
        Changed();
    }

    public static int ColumnsFor(string layoutClass) => layoutClass == "large" ? 2 : 1;

    public sealed class InventoryPresentation
    {
        public IReadOnlyList<InventoryGroupPresentation> Groups { get; private set; } = [];
        public int Columns { get; private set; } = 1;

        public static InventoryPresentation From(SystemInventorySnapshot snapshot, LocalizationService text, string layoutClass)
        {
            var ranks = InventoryOrder.Select((key, index) => (key, index)).ToDictionary(item => item.key, item => item.index, StringComparer.Ordinal);
            return new InventoryPresentation
            {
                Columns = ColumnsFor(layoutClass),
                Groups = snapshot.Groups
                    .OrderBy(group => ranks.TryGetValue(group.Key, out var index) ? index : int.MaxValue)
                    .Select(group => InventoryGroupPresentation.From(group, text))
                    .ToArray()
            };
        }
    }
}

public sealed record InventoryGroupPresentation(
    string Key,
    string Name,
    string AccessibleName,
    string EmptyText,
    string MoreLabel,
    IReadOnlyList<InventoryDevicePresentation> Devices,
    IReadOnlyList<InventoryDevicePresentation> AdditionalDevices)
{
    public bool HasDevices => Devices.Count > 0;
    public bool HasAdditionalDevices => AdditionalDevices.Count > 0;

    public static InventoryGroupPresentation From(InventoryGroup group, LocalizationService text)
    {
        var collapseAdditional = group.Key is WindowsSystemInventory.InventoryGroupStorage or WindowsSystemInventory.InventoryGroupDrivers;
        var visibleDevices = collapseAdditional ? group.Devices.Take(1) : group.Devices;
        var additional = collapseAdditional ? group.Devices.Skip(1) : [];
        var name = text.Get(group.Key);
        return new(group.Key, name, name, text.Get("InventoryGroupEmpty"), text.Get("InventoryMoreDevices"),
            visibleDevices.Select(device => InventoryDevicePresentation.From(device, text)).ToArray(),
            additional.Select(device => InventoryDevicePresentation.From(device, text)).ToArray());
    }
}

public sealed record InventoryDevicePresentation(IReadOnlyList<InventoryFieldPresentation> Fields)
{
    public static InventoryDevicePresentation From(InventoryDevice device, LocalizationService text) =>
        new(device.Fields.Select(field => InventoryFieldPresentation.From(field, text)).ToArray());
}

public sealed record InventoryFieldPresentation(string Display)
{
    public static InventoryFieldPresentation From(InventoryField field, LocalizationService text)
    {
        var value = field.Availability == InventoryAvailability.Available && !string.IsNullOrWhiteSpace(field.Value)
            ? field.Value : text.Get("InventoryAvailability" + field.Availability);
        return new(text.Get(field.Key) + ": " + value);
    }
}

public sealed class ReadinessPresentation : PagePresentation
{
    public LocalizedLabels Text { get; private set; } = new(new LocalizationService());
    public IReadOnlyList<ReadinessSourcePresentation> Sources { get; private set; } = [];
    public bool HasSources => Sources.Count > 0;
    public string EmptyTitle { get; private set; } = string.Empty;
    public string EmptyHelp { get; private set; } = string.Empty;
    public string RefreshLabel { get; private set; } = string.Empty;
    public string Purpose { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;

    public void Refresh(MainViewModel source)
    {
        Text = new(source.Text);
        Sources = source.Readiness.Select(item => ReadinessSourcePresentation.From(item, source.Text)).ToArray();
        EmptyTitle = source.Text.Get("ReadinessNotChecked");
        EmptyHelp = source.Text.Get("ReadinessEmptyHelp");
        RefreshLabel = source.Text.Get("CheckReadiness");
        Purpose = source.Text.Get("ReadinessHelp");
        Summary = source.Text.Get("ReadinessNoHealthScore");
        Changed();
    }
}

public sealed record ReadinessSourcePresentation(
    string Name,
    string Status,
    string AccessibleName,
    string Detail,
    string NextAction,
    bool HasNextAction,
    string TechnicalDetailsLabel,
    string TechnicalAccessibleName,
    IReadOnlyList<ReadinessObservationPresentation> Observations)
{
    public bool HasObservations => Observations.Count > 0;

    public static ReadinessSourcePresentation From(DiagnosticReadinessItem item, LocalizationService text)
    {
        var name = text.Get(item.NameKey);
        var status = text.Get("ReadinessStatus" + item.Status);
        var observationRows = item.Observations?.Select(value =>
            new ReadinessObservationPresentation(text.Get(value.NameKey) + ": " +
                (value.ValueIsLocalizationKey ? text.Get(value.Value) : value.Value))).ToArray() ?? [];
        var observationNames = item.Observations?.Select(value => text.Get(value.NameKey)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var technical = text.Get("TechnicalDetails");
        var technicalName = name + " — " + technical + (observationNames.Length == 0 ? string.Empty : " — " + string.Join(", ", observationNames));
        var hasNextAction = !string.IsNullOrWhiteSpace(item.NextActionKey);
        return new(name, status, name + " — " + status, text.Get(item.DetailKey),
            hasNextAction ? text.Get(item.NextActionKey!) : string.Empty, hasNextAction, technical, technicalName, observationRows);
    }
}

public sealed record ReadinessObservationPresentation(string Display);

public sealed class SettingsPresentation : PagePresentation
{
    private static readonly string[] LanguageCodes = ["system", "en", "it", "es", "fr", "de", "pt", "ru", "pl"];
    private static readonly int[] RetentionValues = [0, 7, 30, 90, 365];

    public LocalizedLabels Text { get; private set; } = new(new LocalizationService());
    public IReadOnlyList<string> Languages { get; private set; } = [];
    public IReadOnlyList<string> Themes { get; private set; } = [];
    public IReadOnlyList<string> Periods { get; private set; } = [];
    public IReadOnlyList<string> RetentionOptions { get; private set; } = [];
    public int LanguageIndex { get; private set; }
    public int ThemeIndex { get; private set; }
    public int PeriodIndex { get; private set; }
    public int RetentionIndex { get; private set; }
    public string Release { get; private set; } = string.Empty;
    public string RuleVersion { get; private set; } = string.Empty;
    public string License { get; private set; } = string.Empty;
    public string Privacy { get; private set; } = string.Empty;
    public string RetentionHelp { get; private set; } = string.Empty;

    public void Refresh(MainViewModel source)
    {
        var text = source.Text;
        Text = new(text);
        Languages = [text.Get("SystemDefault"), "English", "Italiano", "Español", "Français", "Deutsch", "Português", "Русский", "Polski"];
        Themes = [text.Get("ThemeSystem"), text.Get("ThemeLight"), text.Get("ThemeDark")];
        Periods = [text.Get("PeriodDay"), text.Get("PeriodWeek"), text.Get("PeriodMonth")];
        RetentionOptions = RetentionValues.Select(days => days == 0 ? text.Get("DoNotRetain") : text.Format("Days", days)).ToArray();
        LanguageIndex = Math.Max(0, Array.IndexOf(LanguageCodes, source.Settings.Language));
        ThemeIndex = (int)source.Settings.Theme;
        PeriodIndex = Math.Min(2, (int)source.Settings.Period);
        RetentionIndex = Math.Max(0, Array.IndexOf(RetentionValues, source.Settings.RetentionDays));
        Release = ReleaseIdentity.Display;
        RuleVersion = text.Get("RuleDatabase") + " " + RuleCatalog.DatabaseVersion;
        License = text.Get("License") + ": MIT";
        Privacy = text.Get("PrivacyStatement");
        RetentionHelp = text.Get("RetentionHelp");
        Changed();
    }

    public static string LanguageCodeAt(int index) => LanguageCodes[index];
    public static AppTheme ThemeAt(int index) => (AppTheme)index;
    public static AnalysisPeriod PeriodAt(int index) => (AnalysisPeriod)index;
    public static int RetentionDaysAt(int index) => RetentionValues[index];
}

public sealed class ExportSupportPresentation : PagePresentation
{
    public LocalizedLabels Text { get; private set; } = new(new LocalizationService());
    public IReadOnlyList<string> Formats { get; private set; } = [];
    public IReadOnlyList<string> Scopes { get; private set; } = [];
    public int FormatIndex { get; private set; }
    public int ScopeIndex { get; private set; }
    public string Preview { get; private set; } = string.Empty;
    public string PreviewNote { get; private set; } = string.Empty;
    public bool HasAnalysis { get; private set; }
    public bool IsBusy { get; private set; }
    public bool HasPreviewNote => PreviewNote.Length > 0;
    public bool CanExport => HasAnalysis && !IsBusy;

    public void Refresh(MainViewModel source, ExportFormat format, bool selectedOnly, string preview)
    {
        Text = new(source.Text);
        HasAnalysis = source.HasAnalysis;
        IsBusy = source.IsBusy;
        Formats = [source.Text.Get("ExportSummary"), "HTML", "JSON", source.Text.Get("ExportBundle")];
        Scopes = source.Selected is null
            ? [source.Text.Get("EntireAnalysis")]
            : [source.Text.Get("EntireAnalysis"), source.Text.Get("SelectedIncident")];
        FormatIndex = (int)format;
        ScopeIndex = selectedOnly && source.Selected is not null ? 1 : 0;
        Preview = preview.Length <= MainWindow.PreviewCharacterLimit ? preview : preview[..MainWindow.PreviewCharacterLimit];
        PreviewNote = preview.Length > MainWindow.PreviewCharacterLimit ? source.Text.Get("PreviewExcerpt") : string.Empty;
        Changed();
    }
}

public sealed class ClearDataConfirmationPresentation(LocalizationService text)
{
    public LocalizedLabels Text { get; } = new(text);
}
