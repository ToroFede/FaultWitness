using System.Globalization;
using FaultWitness.Core;

namespace FaultWitness.Rules;

/// <summary>Windows evidence identities. Structured report codes are not Event IDs.</summary>
public static class DiagnosticFacts
{
    private static readonly string[] StorageProviders = ["storahci", "stornvme", "iaStorA", "iaStorAC", "Microsoft-Windows-StorPort"];
    public static bool IsEvent(NormalizedEvent item, string channel, string provider, params int[] ids) =>
        Equal(item.Platform, "Windows") && Equal(item.Channel, channel) && Equal(item.Provider, provider) &&
        item.EventVersion is null or >= 0 && ids.Contains(item.EventId);

    public static bool IsReboot(NormalizedEvent item) => IsEvent(item, "System", "Microsoft-Windows-Kernel-Power", 41);
    public static bool IsPlanned(NormalizedEvent item) => IsEvent(item, "System", "USER32", 1074);
    public static bool IsUpdate(NormalizedEvent item) =>
        IsEvent(item, "System", "Microsoft-Windows-WindowsUpdateClient", 19, 21);
    public static bool IsWhea(NormalizedEvent item) =>
        IsEvent(item, "System", "Microsoft-Windows-WHEA-Logger", 1, 17, 18, 19, 20, 46, 47);
    public static bool IsCorrected(NormalizedEvent item) => IsWhea(item) &&
        (Equal(item.Field("ErrorSeverity"), "Corrected") ||
        (item.Field("ErrorSeverity").Length == 0 && item.EventId is 17 or 19 or 47));
    public static bool IsUncorrected(NormalizedEvent item) => IsWhea(item) &&
        (Equal(item.Field("ErrorSeverity"), "Fatal") || Equal(item.Field("ErrorSeverity"), "Recoverable") ||
        (item.Field("ErrorSeverity").Length == 0 && item.EventId is 18 or 20 or 46));
    public static bool IsProcessor(NormalizedEvent item) => IsWhea(item) &&
        (Equal(item.Field("ErrorSource"), "MachineCheck") || item.Field("MCABank").Length > 0 ||
         item.Field("MciStat").Length > 0);
    public static bool IsPcie(NormalizedEvent item) => IsWhea(item) &&
        (Equal(item.Field("Component"), "PCIe") || (item.Field("PortType").Length > 0 && item.Field("Bus").Length > 0));
    public static bool IsMemory(NormalizedEvent item) => IsWhea(item) &&
        (Equal(item.Field("Component"), "Memory") ||
         (item.Field("PhysicalAddress").Length > 0 && item.Field("PhysicalAddressMask").Length > 0));

    public static bool IsWer(NormalizedEvent item) => Equal(item.Platform, "Windows") &&
        Equal(item.Provider, "Windows Error Reporting") && item.EventId == 1001 &&
        (Equal(item.Channel, "Application") || (item.Channel is null &&
        (item.SourceType == SourceType.Wer || Equal(item.Field("Format"), "WER"))));

    public static string ReportType(NormalizedEvent item) => First(item, "EventName", "EventType");
    public static bool IsTdr(NormalizedEvent item) => IsWer(item) &&
        Equal(ReportType(item), "LiveKernelEvent") && LiveKernelCode(item) is "141" or "117";
    public static string LiveKernelCode(NormalizedEvent item) =>
        First(item, "LiveKernelCode", "P1", "Sig[0].Value").ToUpperInvariant().Replace("0X", "", StringComparison.Ordinal).TrimStart('0');

    public static bool IsVendor(NormalizedEvent item) => Equal(item.Platform, "Windows") &&
        Equal(item.Channel, "System") && new[] { "nvlddmkm", "amdkmdag", "amdwddmg", "igfx" }.Contains(item.Provider, StringComparer.OrdinalIgnoreCase);

    public static bool IsCrash(NormalizedEvent item) =>
        IsEvent(item, "Application", "Application Error", 1000) ||
        (IsWer(item) && new[] { "APPCRASH", "BEX", "BEX64", "CLR20r3", "MoAppCrash" }.Contains(ReportType(item), StringComparer.OrdinalIgnoreCase)) ||
        (item.SourceType == SourceType.Reliability && Equal(item.Platform, "Windows") &&
         Equal(item.Channel, "Reliability") && Equal(item.Provider, "Application Error") &&
         item.EventId == 1000 && Equal(item.Field("OriginalChannel"), "Application") &&
         (item.Field("OriginalRecordId").Length > 0 || (!string.IsNullOrWhiteSpace(item.Process) &&
         (ReportId(item).Length > 0 || (!string.IsNullOrWhiteSpace(item.Module) && item.Field("ExceptionCode").Length > 0)))));
    public static bool IsHang(NormalizedEvent item) =>
        IsEvent(item, "Application", "Application Hang", 1002) ||
        (IsWer(item) && new[] { "AppHangB1", "AppHangXProcB1", "MoAppHang" }.Contains(ReportType(item), StringComparer.OrdinalIgnoreCase));
    public static bool IsAudio(NormalizedEvent item) => IsCrash(item) && Equal(FileName(item.Process), "audiodg.exe");
    public static bool IsThirdParty(NormalizedEvent item) =>
        !string.IsNullOrWhiteSpace(item.Module) && Equal(item.Field("ModuleTrust"), "VerifiedThirdParty");
    public static bool IsApo(NormalizedEvent item) => IsAudio(item) && IsThirdParty(item) && Equal(item.Field("ModuleRole"), "APO");

    public static bool IsStorage(NormalizedEvent item) =>
        IsEvent(item, "System", "Disk", 153, 51) ||
        StorageProviders
            .Any(provider => IsEvent(item, "System", provider, 129));
    public static bool IsExhaustion(NormalizedEvent item) => IsEvent(item, "System", "Microsoft-Windows-Resource-Exhaustion-Detector", 2004);
    public static bool IsPnp(NormalizedEvent item) => IsEvent(item, "System", "Microsoft-Windows-Kernel-PnP", 219);
    public static bool IsDeviceStarted(NormalizedEvent item) => IsEvent(item, "Microsoft-Windows-Kernel-PnP/Configuration", "Microsoft-Windows-Kernel-PnP", 410);
    public static bool IsService(NormalizedEvent item) => IsEvent(item, "System", "Service Control Manager", 7031, 7034);
    public static bool IsBugcheck(NormalizedEvent item) =>
        (IsEvent(item, "System", "Microsoft-Windows-WER-SystemErrorReporting", 1001) ||
         IsEvent(item, "System", "BugCheck", 1001)) && BugcheckCode(item) > 0;
    public static uint BugcheckCode(NormalizedEvent item)
    {
        var value = First(item, "BugcheckCode", "BugCheckCode").Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : 0;
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec) ? dec : 0;
    }

    public static string ReportId(NormalizedEvent item) => First(item, "ReportId", "ReportIdentifier", "IntegratorReportId");
    public static string ServiceName(NormalizedEvent item) => First(item, "ServiceName", "param1");
    public static string Device(NormalizedEvent item) => item.Device ?? First(item, "DeviceInstanceId", "DeviceName");
    public static string Vendor(NormalizedEvent item) => First(item, "Vendor", "DriverProvider");
    public static string First(NormalizedEvent item, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = item.Field(key);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return string.Empty;
    }
    public static bool Equal(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    public static string FileName(string? value) => (value ?? string.Empty).Replace('\\', '/').Split('/')[^1];

    public static string Kind(NormalizedEvent item)
    {
        if (IsTdr(item)) return "graphics";
        if (IsHang(item)) return "hang";
        if (IsAudio(item)) return "audio";
        if (IsCrash(item)) return "application";
        if (IsWhea(item)) return "whea";
        if (IsStorage(item)) return "storage";
        if (IsService(item)) return "service";
        if (IsPnp(item)) return "pnp";
        if (IsBugcheck(item)) return "bugcheck";
        if (IsReboot(item)) return "reboot";
        if (IsPlanned(item)) return "planned";
        if (IsExhaustion(item)) return "resources";
        return "unknown";
    }

    public static string Signature(NormalizedEvent item)
    {
        var kind = Kind(item);
        var detail = kind switch
        {
            "application" or "audio" or "hang" => string.Join("|", FileName(item.Process), item.Field("AppVersion"),
                FileName(item.Module), item.Field("ModuleVersion"), item.Field("ExceptionCode"), First(item, "WERBucket", "Bucket")),
            "graphics" => $"{LiveKernelCode(item)}|{Vendor(item)}",
            "whea" => string.Join("|", IsCorrected(item) ? "Corrected" : IsUncorrected(item) ? "Uncorrected" : "Unknown",
                item.Field("ErrorSource"), item.Field("ErrorType"), item.Field("Component"),
                First(item, "MCABank", "Bank"), First(item, "MciStat", "MCiStatus"), item.Field("ApicId"),
                item.Field("Segment"), item.Field("Bus"), item.Field("Device"), item.Field("Function"), item.Field("PhysicalAddress")),
            "storage" => $"{Device(item)}|{item.Provider}|{item.EventId}",
            "service" => $"{ServiceName(item)}|{item.EventId}",
            "pnp" => Device(item),
            "bugcheck" => BugcheckCode(item).ToString(CultureInfo.InvariantCulture),
            _ => item.Id.ToString()
        };
        // Missing identity must never make unrelated records share an empty signature.
        if (string.IsNullOrWhiteSpace(detail.Replace("|", "", StringComparison.Ordinal)) ||
            (kind is "application" or "audio" or "hang" && string.IsNullOrWhiteSpace(item.Process)) ||
            (kind is "application" or "audio" or "hang" && string.IsNullOrWhiteSpace(item.Module) && First(item, "WERBucket", "Bucket").Length == 0) ||
            (kind == "graphics" && Vendor(item).Length == 0) ||
            (kind == "whea" && First(item, "MCABank", "MciStat", "ApicId", "Bus", "PhysicalAddress", "Device").Length == 0) ||
            (kind is "storage" or "pnp" && Device(item).Length == 0) ||
            (kind == "service" && ServiceName(item).Length == 0))
            detail = item.Id.ToString();
        return $"{kind}|{detail}".ToUpperInvariant();
    }

    public static SignatureMatch CompareSignatures(NormalizedEvent left, NormalizedEvent right)
    {
        if (Kind(left) != Kind(right)) return SignatureMatch.Unrelated;
        if (Signature(left) == Signature(right)) return SignatureMatch.Exact;
        if ((!string.IsNullOrWhiteSpace(left.Process) && Equal(FileName(left.Process), FileName(right.Process))) ||
            (Device(left).Length > 0 && Equal(Device(left), Device(right))) ||
            (Kind(left) == "graphics" && Vendor(left).Length > 0 && Equal(Vendor(left), Vendor(right))) ||
            (Kind(left) == "service" && ServiceName(left).Length > 0 && Equal(ServiceName(left), ServiceName(right))) ||
            (Kind(left) == "whea" && left.Field("ApicId").Length > 0 && Equal(left.Field("ApicId"), right.Field("ApicId"))))
            return SignatureMatch.Related;
        return SignatureMatch.SameCategory;
    }

    public static bool SameOccurrence(NormalizedEvent left, NormalizedEvent right)
    {
        if (left.Id == right.Id) return true;
        if ((left.TimestampUtc - right.TimestampUtc).Duration() > TimeSpan.FromSeconds(30)) return false;
        if (left.Field("OriginalRecordId").Length > 0 && Equal(left.Field("OriginalRecordId"), right.Field("OriginalRecordId")) &&
            left.Field("OriginalChannel").Length > 0 && Equal(left.Field("OriginalChannel"), right.Field("OriginalChannel")) &&
            Equal(left.Provider, right.Provider) && left.EventId == right.EventId &&
            (left.SourceType == SourceType.Reliability || right.SourceType == SourceType.Reliability)) return true;
        if (Kind(left) != Kind(right)) return false;
        var leftReport = ReportId(left);
        var rightReport = ReportId(right);
        if (leftReport.Length > 0 && rightReport.Length > 0) return Equal(leftReport, rightReport);
        // Two observations from the same source can be two real crashes, even one second apart.
        if (Representation(left) == Representation(right)) return false;
        if (left.ProcessId is not null && right.ProcessId is not null && left.ProcessId != right.ProcessId) return false;
        return (left.TimestampUtc - right.TimestampUtc).Duration() <= TimeSpan.FromSeconds(2) &&
            Signature(left) == Signature(right) && !string.IsNullOrWhiteSpace(left.Process) &&
            !string.IsNullOrWhiteSpace(left.Module) && left.Field("ExceptionCode").Length > 0;
    }

    private static string Representation(NormalizedEvent item) =>
        item.SourceType == SourceType.Reliability ? "Reliability" : IsWer(item) ? "WER" : $"{item.Provider}/{item.Channel}";

    public static int OccurrenceCount(IEnumerable<NormalizedEvent> records)
    {
        var representatives = new List<NormalizedEvent>();
        foreach (var item in records)
            if (!representatives.Any(other => SameOccurrence(item, other))) representatives.Add(item);
        return representatives.Count;
    }
}
