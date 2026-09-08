using System.Diagnostics.Eventing.Reader;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FaultWitness.Core;

namespace FaultWitness.Platform.Windows;

public sealed record ImportLimits(long FileBytes = 64 * 1024 * 1024, long WerBytes = 1024 * 1024,
    int WerLines = 2000, int LineCharacters = 8192, int Entries = 8,
    long EntryBytes = 8 * 1024 * 1024, long ExpandedBytes = 16 * 1024 * 1024, int Events = 20000);

/// <summary>All paths inside imported data remain references, never filesystem instructions.</summary>
public static class WindowsImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 32 };

    public static async Task<ImportResult> ImportAsync(IEnumerable<string> paths, CancellationToken cancellationToken,
        ImportLimits? limits = null)
    {
        limits ??= new ImportLimits();
        var events = new List<NormalizedEvent>();
        var coverage = new List<SourceCoverage>();
        var errors = new List<string>();
        var files = paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(21).ToArray();
        if (files.Length > 20) throw new InvalidDataException("Too many input files.");
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > limits.FileBytes) throw new InvalidDataException("Unavailable or oversized input.");
                var imported = info.Extension.ToLowerInvariant() switch
                {
                    ".evtx" => await ReadEvtxAsync(info.FullName, limits, cancellationToken).ConfigureAwait(false),
                    ".wer" => await Task.Run(() => ReadWer(info.FullName, limits, cancellationToken), cancellationToken).ConfigureAwait(false),
                    ".zip" => await ReadBundleAsync(info.FullName, limits, cancellationToken).ConfigureAwait(false),
                    _ => throw new InvalidDataException("Unsupported input format.")
                };
                if (events.Count + imported.Count > limits.Events) throw new InvalidDataException("Record limit exceeded.");
                events.AddRange(imported);
                // A file is a sample, not proof of complete coverage of the originating machine.
                coverage.Add(new(SourceType.Imported, CoverageState.Partial,
                    imported.Count == 0 ? null : imported.Min(static item => item.TimestampUtc),
                    imported.Count == 0 ? null : imported.Max(static item => item.TimestampUtc), "coverage.import_sample"));
            }
            catch (UnauthorizedAccessException)
            {
                errors.Add("Access to the selected diagnostic file was denied.");
                coverage.Add(new(SourceType.Imported, CoverageState.AccessDenied, null, null, "coverage.import_access_denied"));
            }
            catch (Exception exception) when (exception is EventLogException or InvalidDataException or JsonException or IOException or DecoderFallbackException or ArgumentException or NotSupportedException or System.Xml.XmlException or InvalidOperationException or OverflowException)
            {
                errors.Add("The selected diagnostic file could not be read safely.");
                coverage.Add(new(SourceType.Imported, CoverageState.Unavailable, null, null, "coverage.import_unavailable"));
            }
        }
        return new ImportResult(new EventBatch(events, coverage), errors);
    }

    private static List<NormalizedEvent> ReadWer(string path, ImportLimits limits, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        return [WerParser.Parse(stream, File.GetLastWriteTimeUtc(path), $"import:{Path.GetFileName(path)}", limits, cancellationToken)];
    }

    private static Task<List<NormalizedEvent>> ReadEvtxAsync(string path, ImportLimits limits, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using (var input = File.OpenRead(path))
        {
            Span<byte> signature = stackalloc byte[8];
            if (input.Read(signature) != 8 || !signature.SequenceEqual("ElfFile\0"u8)) throw new InvalidDataException("Invalid EVTX header.");
        }
        var events = new List<NormalizedEvent>();
        using var reader = new EventLogReader(new EventLogQuery(path, PathType.FilePath));
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var record = reader.ReadEvent();
            if (record is null) break;
            if (events.Count >= limits.Events) throw new InvalidDataException("EVTX record limit exceeded.");
            events.Add(WindowsDiagnosticsProvider.NormalizeImportedEventRecord(record, path));
        }
        return events;
    }, cancellationToken);

    private static async Task<List<NormalizedEvent>> ReadBundleAsync(string path, ImportLimits limits, CancellationToken cancellationToken)
    {
        await using var source = File.OpenRead(path);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, false);
        if (archive.Entries.Count > limits.Entries) throw new InvalidDataException("Too many entries.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        ZipArchiveEntry? eventsEntry = null;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Exact allowlist also rejects UNC, drive-relative paths, nested ZIPs and both slash styles.
            if (entry.FullName is not "events.json" and not "support-summary.md" || !names.Add(entry.FullName) ||
                entry.Length > limits.EntryBytes) throw new InvalidDataException("Unsafe archive entry.");
            expanded = checked(expanded + entry.Length);
            if (expanded > limits.ExpandedBytes) throw new InvalidDataException("Expansion limit exceeded.");
            if (entry.FullName == "events.json") eventsEntry = entry;
        }
        if (eventsEntry is null) throw new InvalidDataException("Missing events.");
        await using var stream = eventsEntry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limits.EntryBytes) throw new InvalidDataException("Actual expansion limit exceeded.");
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        var events = await JsonSerializer.DeserializeAsync<List<NormalizedEvent>>(buffer, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Invalid event array.");
        if (events.Count > limits.Events) throw new InvalidDataException("Record limit exceeded.");
        var ids = new HashSet<Guid>();
        foreach (var item in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) || item.Fields is null ||
                item.Fields.Count > 256 || string.IsNullOrWhiteSpace(item.Platform) || string.IsNullOrWhiteSpace(item.Provider) ||
                string.IsNullOrWhiteSpace(item.SourceReference) || item.TimestampUtc == default ||
                item.TimestampUtc.Offset != TimeSpan.Zero || !Enum.IsDefined(item.SourceType) ||
                !Enum.IsDefined(item.Severity) || item.EventId < 0 ||
                item.Fields.Any(pair => pair.Key.Length > 256 || pair.Value is null || pair.Value.Length > limits.LineCharacters))
                throw new InvalidDataException("Invalid normalized event.");
        }
        return events.Select(static item => item with { SourceReference = $"bundle:{item.SourceReference}" }).ToList();
    }
}
