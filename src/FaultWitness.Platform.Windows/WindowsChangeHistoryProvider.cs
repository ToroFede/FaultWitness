using System.Diagnostics.Eventing.Reader;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using FaultWitness.Core;
using FaultWitness.Platform;

namespace FaultWitness.Platform.Windows;

/// <summary>Reads the small, source-defined subset of Windows change history.</summary>
public sealed class WindowsChangeHistoryProvider : IChangeHistoryProvider
{
    private const int MaxLogBytes = 8 * 1024 * 1024;
    private const int MaxChanges = 256;
    private readonly string setupApiPath;

    public WindowsChangeHistoryProvider(string? setupApiPath = null) =>
        this.setupApiPath = setupApiPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF", "setupapi.dev.log");

    public Task<ChangeHistoryBatch> GetChangesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Task.Run(() => GetChangesCoreAsync(fromUtc, toUtc, cancellationToken), cancellationToken);

    private async Task<ChangeHistoryBatch> GetChangesCoreAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (fromUtc > toUtc) throw new ArgumentException("The start of the interval must not be after its end.");
        toUtc = toUtc > DateTimeOffset.UtcNow ? DateTimeOffset.UtcNow : toUtc;
        if (fromUtc > toUtc) return Empty(CoverageState.Unavailable, "ChangeCoverageUnavailable", fromUtc, toUtc);
        if (!OperatingSystem.IsWindows()) return Empty(CoverageState.NotSupported, "ChangeCoverageNotSupported", fromUtc, toUtc);
        var changes = new List<SystemChange>();
        var setupState = CoverageState.Partial;
        var wuState = CoverageState.Partial;
        try
        {
            try
            {
                var bytes = await ReadTailAsync(setupApiPath, MaxLogBytes, cancellationToken).ConfigureAwait(false);
                // SetupAPI is ANSI. Register the system code page rather than treating non-ASCII vendor text as UTF-8.
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
                changes.AddRange(WindowsChangeHistoryParser.ParseSetupApi(encoding.GetString(bytes), fromUtc, toUtc, MaxChanges, cancellationToken: cancellationToken));
            }
            catch (FileNotFoundException) { setupState = CoverageState.Unavailable; }
        }
        catch (UnauthorizedAccessException) { setupState = CoverageState.AccessDenied; }
        catch (IOException) { setupState = CoverageState.Partial; }

        try
        {
            changes.AddRange(ReadWindowsUpdateEvents(fromUtc, toUtc, MaxChanges, cancellationToken));
        }
        catch (UnauthorizedAccessException) { wuState = CoverageState.AccessDenied; }
        catch (EventLogException exception) { wuState = (exception.HResult & 0xffff) == 5 ? CoverageState.AccessDenied : CoverageState.Unavailable; }
        catch (InvalidOperationException) { wuState = CoverageState.Partial; }

        return new ChangeHistoryBatch(changes.OrderBy(c => c.TimestampUtc)
            .Select(change => SystemChangePrivacy.Sanitize(change) with { ComponentIdentity = change.ComponentIdentity }).ToArray(),
            [new(SourceType.ChangeHistory, setupState, fromUtc, toUtc, Detail(setupState), "ChangeSourceSetupApi"), new(SourceType.ChangeHistory, wuState, fromUtc, toUtc, Detail(wuState), "ChangeSourceWindowsUpdate")]);
    }

    private static string Detail(CoverageState state) => state == CoverageState.Complete ? "ChangeCoverageComplete" : state == CoverageState.AccessDenied ? "ChangeCoverageAccessDenied" : state == CoverageState.Unavailable ? "ChangeCoverageUnavailable" : state == CoverageState.NotSupported ? "ChangeCoverageNotSupported" : "ChangeCoveragePartial";

    private static async Task<byte[]> ReadTailAsync(string path, int maxBytes, CancellationToken token)
    {
        var info = new FileInfo(path);
        var count = (int)Math.Min(info.Length, maxBytes);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, true);
        stream.Seek(-count, SeekOrigin.End);
        var buffer = new byte[count];
        var read = 0;
        while (read < buffer.Length)
        {
            token.ThrowIfCancellationRequested();
            var n = await stream.ReadAsync(buffer.AsMemory(read), token).ConfigureAwait(false);
            if (n == 0) break;
            read += n;
        }
        return buffer[..read];
    }

    private static IEnumerable<SystemChange> ReadWindowsUpdateEvents(DateTimeOffset fromUtc, DateTimeOffset toUtc, int limit, CancellationToken token)
    {
        if (limit <= 0) yield break;
        var query = new EventLogQuery("System", PathType.LogName,
            $"*[System[(Provider[@Name='Microsoft-Windows-WindowsUpdateClient']) and (EventID=19) and TimeCreated[@SystemTime >= '{fromUtc.UtcDateTime:O}' and @SystemTime <= '{toUtc.UtcDateTime:O}']]]") { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        for (var i = 0; i < limit; i++)
        {
            token.ThrowIfCancellationRequested();
            using var record = reader.ReadEvent();
            if (record is null) yield break;
            var xml = record.ToXml();
            var time = WindowsChangeHistoryParser.ParseWindowsUpdateTimestamp(xml);
            if (time is null || time.Value < fromUtc || time.Value > toUtc) continue;
            var fields = WindowsChangeHistoryParser.ParseWindowsUpdate19(xml);
            if (fields is null) continue;
            yield return WindowsChangeHistoryParser.CreateWindowsUpdate(fields.Value, time.Value, record.RecordId);
        }
    }

    private static ChangeHistoryBatch Empty(CoverageState state, string detail, DateTimeOffset from, DateTimeOffset to) =>
        new([], [new(SourceType.ChangeHistory, state, from, to, detail, "ChangeSourceSetupApi"), new(SourceType.ChangeHistory, state, from, to, detail, "ChangeSourceWindowsUpdate")]);
}

public static class WindowsChangeHistoryParser
{
    private const int MaxSectionChars = 256 * 1024;
    public static IReadOnlyList<SystemChange> ParseSetupApi(string text, DateTimeOffset fromUtc, DateTimeOffset toUtc, int limit = 256, TimeZoneInfo? timeZone = null, CancellationToken cancellationToken = default)
    {
        var result = new List<SystemChange>();
        var starts = System.Text.RegularExpressions.Regex.Split(text, "(?m)(?=^>>>\\s+\\[)");
        foreach (var raw in starts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Count >= limit) break;
            var section = raw.TrimStart();
            if (section.StartsWith(">>>", StringComparison.Ordinal)) section = section[3..].TrimStart();
            if (section.Length > MaxSectionChars) continue;
            if (!section.StartsWith("[Device Install", StringComparison.OrdinalIgnoreCase) || !section.Contains("Section start", StringComparison.OrdinalIgnoreCase)) continue;
            var headerEnd = section.IndexOf('\n');
            if (headerEnd < 0) continue;
            var header = section[..headerEnd];
            var delimiter = header.IndexOf(" - ", StringComparison.Ordinal);
            if (delimiter < 0 || !header.TrimEnd().EndsWith(']')) continue;
            var device = header[(delimiter + 3)..].TrimEnd().TrimEnd(']').Trim();
            if (string.IsNullOrWhiteSpace(device)) continue;
            var start = FindTimestamp(section, "Section start");
            if (start is null || !TryLocalToUtc(start.Value, timeZone ?? TimeZoneInfo.Local, out var timestamp)) continue;
            var selected = section.IndexOf("dvi: Selected Driver:", StringComparison.OrdinalIgnoreCase);
            if (selected < 0 || !Regex.IsMatch(section[selected..], @"(?m)^\s*dvi:\s*\{Core Device Install\}")) continue;
            var end = section.IndexOf("<<<", selected, StringComparison.Ordinal);
            if (end < 0) continue;
            var selectedEnd = Regex.Match(section[(selected + 20)..], @"(?m)^\s*dvi:\s*(?:\{|Default installer|Class installer)");
            var selectedBlock = selectedEnd.Success ? section[selected..(selected + 20 + selectedEnd.Index)] : section[selected..end];
            var inf = ValueAfter(selectedBlock, "InfFile", '-');
            if (string.IsNullOrWhiteSpace(inf)) continue;
            var finalEnd = section.IndexOf("<<<  Section end", end, StringComparison.OrdinalIgnoreCase);
            if (finalEnd < 0 || !section[finalEnd..].Contains("Exit status: SUCCESS", StringComparison.OrdinalIgnoreCase) && !section[finalEnd..].Contains("Exit Status(0x00000000", StringComparison.OrdinalIgnoreCase)) continue;
            var completed = FindTimestamp(section[finalEnd..], "Section end");
            if (completed is null || !TryLocalToUtc(completed.Value, timeZone ?? TimeZoneInfo.Local, out var completedUtc) || completedUtc < timestamp) continue;
            timestamp = completedUtc;
            if (timestamp < fromUtc || timestamp > toUtc) continue;
            var installedVersion = Regex.Match(section[selected..end], @"(?m)^\s*ndv:\s*Driver Version\s*:\s*([0-9]+(?:\.[0-9]+){1,3})\s*$");
            var recordedVersion = installedVersion.Success ? installedVersion.Groups[1].Value : ValueAfter(selectedBlock, "DriverVersion", '-');
            var version = Version.TryParse(recordedVersion, out _) ? recordedVersion : null;
            var provider = ValueAfter(selectedBlock, "Provider", '-');
            var recordedClass = ValueAfter(selectedBlock, "Class GUID", '-');
            var classId = Guid.TryParse(recordedClass, out var classGuid) ? classGuid.ToString("B") : null;
            var subsystem = ParseSubsystem(classId);
            var subject = Path.GetFileName(inf.Trim());
            var sourceRef = "setupapi:" + Hash(timestamp.UtcTicks + device + inf);
            var id = Hash(timestamp.UtcTicks + device + inf);
            result.Add(new SystemChange(id, "Windows", timestamp, ChangeCategory.DriverInstalled, "ChangeSourceSetupApi", subject, subsystem, null, version, provider, sourceRef,
                ChangeQuality.LocalTimeConverted) { ComponentIdentity = device, ClassId = classId });
        }
        return result;
    }

    public static (string Title, string Guid, string Revision)? ParseWindowsUpdate19(string xml)
    {
        try
        {
            if (xml.Length > 256 * 1024 || xml.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)) return null;
            var root = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            if (!string.Equals((string?)root.Descendants().FirstOrDefault(x => x.Name.LocalName == "Provider")?.Attribute("Name"), "Microsoft-Windows-WindowsUpdateClient", StringComparison.OrdinalIgnoreCase)) return null;
            if ((string?)root.Descendants().FirstOrDefault(x => x.Name.LocalName == "EventID") != "19") return null;
            if (!string.Equals((string?)root.Descendants().FirstOrDefault(x => x.Name.LocalName == "Channel"), "System", StringComparison.OrdinalIgnoreCase)) return null;
            string? Get(string key) { var values = root.Descendants().Where(x => x.Name.LocalName == "Data" && (string?)x.Attribute("Name") == key).ToArray(); return values.Length == 1 ? values[0].Value : null; }
            var title = Get("updateTitle"); var guid = Get("updateGuid"); var rev = Get("updateRevisionNumber");
            return string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(guid) ? null : (title, guid, rev ?? "");
        }
        catch (System.Xml.XmlException) { return null; }
    }

    public static DateTimeOffset? ParseWindowsUpdateTimestamp(string xml)
    {
        try
        {
            if (xml.Length > 256 * 1024 || xml.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)) return null;
            var root = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            var value = (string?)root.Descendants().FirstOrDefault(x => x.Name.LocalName == "TimeCreated")?.Attribute("SystemTime");
            return DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var timestamp) ? timestamp : null;
        }
        catch (System.Xml.XmlException) { return null; }
    }

    public static SystemChange CreateWindowsUpdate((string Title, string Guid, string Revision) fields, DateTimeOffset timestamp, long? recordId)
    {
        var definition = System.Text.RegularExpressions.Regex.IsMatch(fields.Title, "\\bKB2267602\\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return new SystemChange(Hash($"wu:{recordId}:{fields.Guid}:{fields.Revision}:{timestamp.UtcTicks}"), "Windows", timestamp.ToUniversalTime(), ChangeCategory.WindowsUpdate, "ChangeSourceWindowsUpdate", fields.Title.Trim(), ChangeSubsystem.System, null, null, "Microsoft", "windowsupdate:" + (recordId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown")) { UpdateIdentity = fields.Guid + (string.IsNullOrWhiteSpace(fields.Revision) ? "" : ":" + fields.Revision), IsDefinitionUpdate = definition };
    }

    private static DateTime? FindTimestamp(string text, string marker) { var i = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase); if (i < 0) return null; var line = text[i..].Split('\n')[0]; var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d{4}/\d{2}/\d{2}\s+\d{2}:\d{2}:\d{2}(?:\.\d+)?)"); return match.Success && DateTime.TryParseExact(match.Value, ["yyyy/MM/dd HH:mm:ss.fff", "yyyy/MM/dd HH:mm:ss"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null; }
    private static bool TryLocalToUtc(DateTime local, TimeZoneInfo zone, out DateTimeOffset utc) { utc = default; if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) return false; utc = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime(); return true; }
    private static string? ValueAfter(string section, string label, char delimiter)
    {
        var matches = Regex.Matches(section, @"(?m)^\s*(?:dvi|ndv|inf|dvs):\s*" + Regex.Escape(label) +
            @"\s*" + Regex.Escape(delimiter.ToString()) + @"\s*([^\r\n]+)", RegexOptions.IgnoreCase);
        return matches.Count == 1 ? matches[0].Groups[1].Value.Trim() : null;
    }
    private static ChangeSubsystem ParseSubsystem(string? id) => id?.ToUpperInvariant() switch { "{4D36E968-E325-11CE-BFC1-08002BE10318}" => ChangeSubsystem.Display, "{4D36E96C-E325-11CE-BFC1-08002BE10318}" => ChangeSubsystem.Audio, "{4D36E972-E325-11CE-BFC1-08002BE10318}" => ChangeSubsystem.Network, "{4D36E97B-E325-11CE-BFC1-08002BE10318}" => ChangeSubsystem.Storage, "{4D36E96A-E325-11CE-BFC1-08002BE10318}" => ChangeSubsystem.Storage, "{4D36E97D-E325-11CE-BFC1-08002BE10318}" => ChangeSubsystem.System, "{4D36E97E-E325-11CE-BFC1-08002BE10318}" => ChangeSubsystem.Printer, _ => ChangeSubsystem.Unknown };
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];
}
