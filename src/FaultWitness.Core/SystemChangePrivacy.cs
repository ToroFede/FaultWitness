using System.Text.RegularExpressions;

namespace FaultWitness.Core;

/// <summary>Device identifiers are never part of portable change summaries, even with optional redaction off.</summary>
public static class SystemChangePrivacy
{
    public static SystemChange Sanitize(SystemChange change)
    {
        string? Clean(string? value)
        {
            if (value is null) return null;
            if (!string.IsNullOrEmpty(change.ComponentIdentity))
                value = value.Replace(change.ComponentIdentity, "<device-id>", StringComparison.OrdinalIgnoreCase);
            value = Regex.Replace(value, @"(?i)\b(?:PCI|USB|USBSTOR|HDAUDIO|DISPLAY|SWD|ROOT|ACPI|HID|SCSI|STORAGE|BTHENUM|BTHLEDEVICE|BTH)\\[^\s,;]+", "<device-id>");
            value = Regex.Replace(value, @"(?i)(?:\b[A-Z]:\\|\\\\)[^\r\n""<>|]+", "<private-path>");
            value = value.Replace(Environment.MachineName, "<computer>", StringComparison.OrdinalIgnoreCase);
            value = Regex.Replace(value, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", match =>
                // Dotted driver versions are useful recorded values, not addresses.
                change.PreviousValue == value || change.NewValue == value ? match.Value : "<address>");
            return value.Length <= 512 ? value : value[..512];
        }
        return change with
        {
            Id = Clean(change.Id)!, Platform = Clean(change.Platform)!, Source = Clean(change.Source)!,
            Subject = Clean(change.Subject)!, PreviousValue = Clean(change.PreviousValue), NewValue = Clean(change.NewValue),
            Vendor = Clean(change.Vendor), SourceReference = Clean(change.SourceReference)!,
            ClassId = Clean(change.ClassId), UpdateIdentity = Clean(change.UpdateIdentity), ComponentIdentity = null
        };
    }
}
