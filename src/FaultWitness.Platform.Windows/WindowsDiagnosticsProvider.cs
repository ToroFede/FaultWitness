using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Management;
using System.Xml.Linq;
using FaultWitness.Core;
using FaultWitness.Platform;

namespace FaultWitness.Platform.Windows;

/// <summary>Windows-only collector. Access failures are reported as coverage, never as absence of evidence.</summary>
public sealed class WindowsDiagnosticsProvider : IPlatformDiagnosticsProvider, ISystemInformationProvider, IDiagnosticCapabilityProvider
{
    private static readonly string[] Channels = ["System", "Application"];

    public async Task<EventBatch> ReadAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (toUtc > now) toUtc = now; // Future time cannot be examined, even if the query returns successfully.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromUtc, toUtc);
        var tasks = Channels.Select(channel => ReadEventLogAsync(channel, fromUtc, toUtc, cancellationToken)).Append(ReadWerAsync(fromUtc, toUtc, cancellationToken)).Append(ReadReliabilityAsync(fromUtc, toUtc, cancellationToken)).Append(DiscoverCrashArtifactsAsync(fromUtc, toUtc, cancellationToken));
        var batches = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new EventBatch(batches.SelectMany(static batch => batch.Events), batches.SelectMany(static batch => batch.Coverage));
    }

    public Task<IReadOnlyDictionary<string, string>> GetInventoryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyDictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OperatingSystem"] = RuntimeInformation.OSDescription,
            ["Architecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["ProcessArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["ProcessorCount"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            ["MachineName"] = Environment.MachineName
        };
        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<DiagnosticReadinessItem>> GetReadinessAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var batch = await ReadAsync(now.AddMinutes(-1), now, cancellationToken).ConfigureAwait(false);
        return batch.Coverage.Select(static coverage => new DiagnosticReadinessItem(coverage.SourceType.ToString(), coverage.State, coverage.DetailKey)).ToList();
    }

    private static Task<EventBatch> ReadEventLogAsync(string channel, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var records = new List<NormalizedEvent>();
        try
        {
            var start = fromUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            var end = toUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            var query = new EventLogQuery(channel, PathType.LogName, $"*[System[TimeCreated[@SystemTime >= '{start}' and @SystemTime <= '{end}']]]") { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (EventRecord? record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (record)
                {
                    records.Add(NormalizeEventRecord(record, channel));
                }
            }
            using var oldestReader = new EventLogReader(new EventLogQuery(channel, PathType.LogName));
            using var oldest = oldestReader.ReadEvent();
            var retained = oldest?.TimeCreated is { } first && first.ToUniversalTime() <= fromUtc.UtcDateTime;
            var state = retained ? CoverageState.Complete : CoverageState.Partial;
            return new EventBatch(records, [new SourceCoverage(SourceType.EventLog, state, fromUtc, toUtc,
                retained ? "coverage.event_log_complete" : "coverage.event_log_partial", channel)]);
        }
        catch (UnauthorizedAccessException)
        {
            return new EventBatch(records, [new SourceCoverage(SourceType.EventLog, CoverageState.AccessDenied, fromUtc, toUtc, "coverage.event_log_access_denied", channel)]);
        }
        catch (Exception exception) when (exception is EventLogException or InvalidDataException or System.Xml.XmlException or InvalidOperationException)
        {
            return new EventBatch(records, [new SourceCoverage(SourceType.EventLog, CoverageState.Partial, fromUtc, toUtc, "coverage.event_log_partial", channel)]);
        }
    }, cancellationToken);

    internal static NormalizedEvent NormalizeImportedEventRecord(EventRecord record, string sourcePath) => NormalizeEventRecord(record, record.LogName ?? "Imported", $"import:{Path.GetFileName(sourcePath)}:{record.RecordId}", SourceType.Imported);

    private static NormalizedEvent NormalizeEventRecord(EventRecord record, string channel) => NormalizeEventRecord(record, channel, $"{channel}:{record.RecordId?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}", SourceType.EventLog);

    private static NormalizedEvent NormalizeEventRecord(EventRecord record, string channel, string sourceReference, SourceType sourceType)
    {
        var raw = record.ToXml();
        return NormalizeXml(raw, sourceType, sourceReference);
    }

    public static NormalizedEvent NormalizeXml(string raw, SourceType sourceType, string sourceReference)
    {
        if (raw.Length > 1024 * 1024) throw new InvalidDataException("Event XML limit exceeded.");
        using var reader = System.Xml.XmlReader.Create(new StringReader(raw),
            new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var xml = XDocument.Load(reader);
        var system = xml.Root?.Elements().SingleOrDefault(static element => element.Name.LocalName == "System")
            ?? throw new InvalidDataException("Missing event system fields.");
        string Value(string name) => system.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value ?? string.Empty;
        var provider = system.Elements().FirstOrDefault(static element => element.Name.LocalName == "Provider")?.Attribute("Name")?.Value
            ?? throw new InvalidDataException("Missing provider.");
        var channel = Value("Channel");
        var time = system.Elements().FirstOrDefault(static element => element.Name.LocalName == "TimeCreated")?.Attribute("SystemTime")?.Value;
        if (!DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp) ||
            !int.TryParse(Value("EventID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            throw new InvalidDataException("Missing event identity or timestamp.");
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(channel) || id < 0)
            throw new InvalidDataException("Invalid event identity.");
        var fields = ParseEventFields(xml);
        fields["OriginalRecordId"] = Value("EventRecordID");
        fields["OriginalChannel"] = channel;
        string? process = FirstField(fields, "AppName", "ProcessName");
        string? module = FirstField(fields, "ModuleName", "FaultingModuleName", "FaultModuleName");
        void Alias(string target, params string[] names)
        {
            if (!fields.ContainsKey(target) && FirstField(fields, names) is { } value) fields[target] = value;
        }
        Alias("ReportId", "IntegratorReportId", "ReportIdentifier");
        if (provider == "Windows Error Reporting" && channel == "Application" && id == 1001 &&
            FirstField(fields, "EventName") is "APPCRASH" or "BEX" or "BEX64")
        {
            process ??= FirstField(fields, "P1");
            module ??= FirstField(fields, "P4");
            Alias("AppVersion", "P2");
            Alias("ModuleVersion", "P5");
            Alias("ExceptionCode", "P7");
        }
        if (provider == "Microsoft-Windows-WER-SystemErrorReporting" && channel == "System" && id == 1001)
        {
            var first = FirstField(fields, "param1")?.Split(' ', '(')[0];
            if (first?.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true) fields["BugcheckCode"] = first;
            Alias("DumpPath", "param2");
        }
        var version = int.TryParse(Value("Version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedVersion) ? parsedVersion : (int?)null;
        byte.TryParse(Value("Level"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level);
        return new NormalizedEvent(Guid.NewGuid(), sourceType, "Windows", timestamp.ToUniversalTime(), channel, provider,
            id, version, ToSeverity(level), process, null, module, FirstField(fields, "DeviceName", "DeviceInstanceId", "Device"),
            fields, sourceReference, raw);
    }

    private static Dictionary<string, string> ParseEventFields(XDocument document)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var data in document.Descendants().Where(element => element.Name.LocalName == "Data"))
        {
            var name = data.Attribute("Name")?.Value;
            if (!string.IsNullOrWhiteSpace(name) && !fields.TryAdd(name, data.Value))
                throw new InvalidDataException("Ambiguous duplicate event field.");
        }
        return fields;
    }

    private static string? FirstField(Dictionary<string, string> fields, params string[] names)
    {
        foreach (var name in names) if (fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        return null;
    }

    private static IncidentSeverity ToSeverity(byte? level) => level switch { 1 or 2 => IncidentSeverity.High, 3 => IncidentSeverity.Medium, 4 => IncidentSeverity.Low, _ => IncidentSeverity.Informational };

    private static Task<EventBatch> ReadWerAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportArchive"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportQueue")
        };
        var events = new List<NormalizedEvent>();
        var anyRoot = false;
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            anyRoot = true;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "Report.wer", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var stamp = File.GetLastWriteTimeUtc(file);
                    try
                    {
                        using var input = File.OpenRead(file);
                        var parsed = WerParser.Parse(input, stamp, file, new ImportLimits(), cancellationToken) with { SourceType = SourceType.Wer };
                        if (parsed.TimestampUtc >= fromUtc && parsed.TimestampUtc <= toUtc) events.Add(parsed);
                    }
                    catch (Exception exception) when (exception is InvalidDataException or IOException or System.Text.DecoderFallbackException) { }
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { }
        }
        // Retained WER directories cannot prove that all reports from the requested period still exist.
        var state = !anyRoot ? CoverageState.Unavailable : CoverageState.Partial;
        return new EventBatch(events, [new SourceCoverage(SourceType.Wer, state, fromUtc, toUtc, state == CoverageState.Partial ? "coverage.wer_partial" : state == CoverageState.Complete ? "coverage.wer_complete" : "coverage.wer_unavailable")]);
    }, cancellationToken);

    private static Task<EventBatch> ReadReliabilityAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var events = new List<NormalizedEvent>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TimeGenerated, ProductName, SourceName, EventIdentifier, RecordNumber, Logfile FROM Win32_ReliabilityRecords");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using var ownedItem = item;
                cancellationToken.ThrowIfCancellationRequested();
                var timestampValue = item["TimeGenerated"]?.ToString();
                if (string.IsNullOrWhiteSpace(timestampValue)) continue;
                var timestamp = ManagementDateTimeConverter.ToDateTime(timestampValue).ToUniversalTime();
                if (timestamp < fromUtc.UtcDateTime || timestamp > toUtc.UtcDateTime) continue;
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ProductName"] = item["ProductName"]?.ToString() ?? string.Empty,
                    ["OriginalChannel"] = item["Logfile"]?.ToString() ?? string.Empty,
                    ["OriginalRecordId"] = item["RecordNumber"]?.ToString() ?? string.Empty
                };
                var provider = item["SourceName"]?.ToString() ?? "Reliability";
                var eventId = int.TryParse(item["EventIdentifier"]?.ToString(), out var value) ? value : 0;
                var process = fields["ProductName"].EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? fields["ProductName"] : null;
                events.Add(new NormalizedEvent(Guid.NewGuid(), SourceType.Reliability, "Windows", timestamp, "Reliability", provider, eventId, null, IncidentSeverity.Low, process, null, null, null, fields, $"reliability:{item["RecordNumber"]}"));
            }
            return new EventBatch(events, [new SourceCoverage(SourceType.Reliability, CoverageState.Complete, fromUtc, toUtc, "coverage.reliability_complete")]);
        }
        catch (UnauthorizedAccessException) { return new EventBatch(events, [new SourceCoverage(SourceType.Reliability, CoverageState.AccessDenied, fromUtc, toUtc, "coverage.reliability_access_denied")]); }
        catch (ManagementException) { return new EventBatch(events, [new SourceCoverage(SourceType.Reliability, CoverageState.Unavailable, fromUtc, toUtc, "coverage.reliability_unavailable")]); }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ArgumentOutOfRangeException or FormatException)
        { return new EventBatch(events, [new SourceCoverage(SourceType.Reliability, CoverageState.Partial, fromUtc, toUtc, "coverage.reliability_partial")]); }
    }, cancellationToken);

    private static Task<EventBatch> DiscoverCrashArtifactsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var locations = new[] { Path.Combine(windows, "Minidump"), Path.Combine(windows, "LiveKernelReports"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps") };
        var events = new List<NormalizedEvent>();
        foreach (var location in locations.Where(Directory.Exists))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(location, "*.dmp", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var stamp = File.GetLastWriteTimeUtc(file);
                    if (stamp < fromUtc.UtcDateTime || stamp > toUtc.UtcDateTime) continue;
                    var kind = string.Equals(location, Path.Combine(windows, "Minidump"), StringComparison.OrdinalIgnoreCase) ? "Kernel" : "Unknown";
                    events.Add(new NormalizedEvent(Guid.NewGuid(), SourceType.CrashArtifact, "Windows", stamp, null, "CrashArtifact", 0, null, IncidentSeverity.Medium, null, null, null, null,
                        new Dictionary<string, string> { ["FileName"] = Path.GetFileName(file), ["DumpKind"] = kind, ["ClassificationBasis"] = "DiscoveryLocation" }, file));
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { }
        }
        return new EventBatch(events, [new SourceCoverage(SourceType.CrashArtifact, CoverageState.Partial, fromUtc, toUtc, "coverage.crash_artifacts_partial")]);
    }, cancellationToken);
}
