using System.Text;
using FaultWitness.Core;
using FaultWitness.Localization;

namespace FaultWitness.Export;

public static class SystemSummaryExporter
{
    private static readonly Dictionary<string, string> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OperatingSystem"] = "InventoryGroupOperatingSystem", ["ProcessorMemory"] = "InventoryGroupProcessorMemory",
        ["Graphics"] = "InventoryGroupGraphics", ["Firmware"] = "InventoryGroupFirmware",
        ["Storage"] = "InventoryGroupStorage", ["Drivers"] = "InventoryGroupDrivers"
    };
    private static readonly Dictionary<string, string> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OperatingSystem"] = "InventoryOperatingSystem", ["DisplayVersion"] = "InventoryDisplayVersion", ["BuildNumber"] = "InventoryBuildNumber", ["OsVersion"] = "InventoryOsVersion", ["Architecture"] = "InventoryArchitecture", ["ProcessArchitecture"] = "InventoryProcessArchitecture", ["ProcessorCount"] = "InventoryProcessorCount", ["Processor"] = "InventoryProcessor", ["ProcessorCores"] = "InventoryProcessorCores", ["ProcessorLogical"] = "InventoryProcessorLogical", ["MemoryBytes"] = "InventoryMemoryBytes", ["MemoryGiB"] = "InventoryMemoryGiB", ["ComputerManufacturer"] = "InventoryComputerManufacturer", ["ComputerModel"] = "InventoryComputerModel", ["GraphicsName"] = "InventoryGraphicsName", ["GraphicsVendor"] = "InventoryGraphicsVendor", ["GraphicsDriverVersion"] = "InventoryGraphicsDriverVersion", ["GraphicsDriverDate"] = "InventoryGraphicsDriverDate", ["BoardManufacturer"] = "InventoryBoardManufacturer", ["BoardModel"] = "InventoryBoardModel", ["BiosVendor"] = "InventoryBiosVendor", ["BiosVersion"] = "InventoryBiosVersion", ["BiosDate"] = "InventoryBiosDate", ["StorageModel"] = "InventoryStorageModel", ["StorageCapacity"] = "InventoryStorageCapacity", ["StorageCapacityGiB"] = "InventoryStorageCapacityGiB", ["StorageMedia"] = "InventoryStorageMedia", ["StorageBus"] = "InventoryStorageBus", ["StorageFirmware"] = "InventoryStorageFirmware", ["DriverName"] = "InventoryDriverName", ["DriverProvider"] = "InventoryDriverProvider", ["DriverVersion"] = "InventoryDriverVersion", ["DriverDate"] = "InventoryDriverDate", ["DriverCategory"] = "InventoryDriverCategory"
    };
    private static readonly Dictionary<string, string> Sources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["system-event-log"] = "SourceSystem", ["application-event-log"] = "SourceApplication", ["wer"] = "SourceTypeWer", ["reliability"] = "SourceTypeReliability", ["crash-artifacts"] = "SourceTypeCrashArtifact", ["system-dumps"] = "DumpConfiguration", ["pagefile-support"] = "PageFile", ["local-dumps"] = "ApplicationDumps", ["dump-storage"] = "FreeSpace"
    };
    private static readonly HashSet<string> ObservationKeys = new(StringComparer.OrdinalIgnoreCase) { "ReadinessDumpType", "ReadinessDumpTarget", "ReadinessDedicatedDump", "ReadinessAutomaticPageFile", "ReadinessActivePageFiles", "ReadinessAllocatedPageFile", "ReadinessLocalDumpScope", "ReadinessDumpCount", "ReadinessStorageObserved", "ReadinessSystemDrive", "ReadinessDumpDestination" };
    private static readonly HashSet<string> SafeLocalizationValues = new(StringComparer.OrdinalIgnoreCase) { "ReadinessDefaultScope", "ReadinessObserved", "ReadinessNotObserved", "ReadinessTargetUnknown", "ReadinessCustomTarget" };
    private static readonly HashSet<string> DetailKeys = new(StringComparer.OrdinalIgnoreCase) { "ReadinessSourceReady", "ReadinessSourceLimited", "ReadinessSourceUnavailable", "ReadinessSourceAccessDenied", "ReadinessSourceNotSupported", "ReadinessElevationHelp", "ReadinessConfigurationUnknown", "ReadinessSystemDumpsDisabled", "ReadinessSystemDumpsConfigured", "ReadinessPageFileUnknown", "ReadinessLocalDumpsAbsent", "ReadinessLocalDumpsConfigured", "ReadinessLocalDumpsLimited", "ReadinessStorageObserved" };

    public static string ToMarkdown(SystemInventorySnapshot inventory, IReadOnlyList<DiagnosticReadinessItem> readiness, LocalizationService? translation = null)
    {
        var language = translation ?? new LocalizationService();
        var output = new StringBuilder("# " + language.Get("Readiness") + "\n\n## " + language.Get("SourceTypeInventory") + "\n");
        foreach (var group in inventory.Groups ?? [])
        {
            if (!Groups.TryGetValue(group.Key, out _) && !(group.Key.StartsWith("InventoryGroup", StringComparison.OrdinalIgnoreCase) && Groups.ContainsKey(group.Key[13..]))) continue;
            foreach (var device in group.Devices ?? [])
                foreach (var field in device.Fields ?? [])
                    if (field.Availability == InventoryAvailability.Available && !string.IsNullOrWhiteSpace(field.Value) && Fields.TryGetValue(field.Key.StartsWith("Inventory", StringComparison.OrdinalIgnoreCase) ? field.Key[9..] : field.Key, out var fieldLabel))
                        output.Append("- ").Append(language.Get(fieldLabel)).Append(": ").Append(SanitizeInventoryValue(field.Key, field.Value)).AppendLine();
        }
        output.AppendLine("\n## " + language.Get("Readiness"));
        foreach (var item in readiness ?? [])
        {
            if (!Sources.TryGetValue(item.Id, out var sourceLabel)) continue;
            var detail = DetailKeys.Contains(item.DetailKey) ? language.Get(item.DetailKey) : language.Get("ReadinessConfigurationUnknown");
            output.Append("- ").Append(language.Get(sourceLabel)).Append(": ").Append(language.Get("ReadinessStatus" + item.Status)).Append(". ").Append(detail).AppendLine();
            foreach (var observation in item.Observations ?? [])
            {
                if (!ObservationKeys.Contains(observation.NameKey) || observation.ValueIsLocalizationKey && !SafeLocalizationValues.Contains(observation.Value)) continue;
                var value = observation.Value;
                if (observation.ValueIsLocalizationKey && SafeLocalizationValues.Contains(value)) value = language.Get(value);
                else if (value.Contains('%') || value.Contains('\\') || value.Contains(':')) value = IsApprovedTarget(value) ? value : language.Get("ReadinessCustomTarget");
                if (observation.NameKey.Equals("ReadinessLocalDumpScope", StringComparison.OrdinalIgnoreCase) && value != language.Get("ReadinessDefaultScope") && !value.StartsWith('#')) continue;
                output.Append("  - ").Append(language.Get(observation.NameKey)).Append(": ").Append(ReportExporter.Redact(value, new ExportPrivacyOptions())).AppendLine();
            }
        }
        return output.ToString();
    }

    private static bool IsApprovedTarget(string value) => value.Equals("%SystemRoot%\\MEMORY.DMP", StringComparison.OrdinalIgnoreCase) || value.Equals("%SystemRoot%\\Minidump", StringComparison.OrdinalIgnoreCase) || value.Equals("%LOCALAPPDATA%\\CrashDumps", StringComparison.OrdinalIgnoreCase);
    private static string SanitizeInventoryValue(string key, string value)
    {
        if ((key.Contains("Version", StringComparison.OrdinalIgnoreCase) || key.Contains("Build", StringComparison.OrdinalIgnoreCase)) && value.All(c => char.IsLetterOrDigit(c) || ".-_+".Contains(c))) return value;
        return ReportExporter.Redact(value, new ExportPrivacyOptions());
    }
}
