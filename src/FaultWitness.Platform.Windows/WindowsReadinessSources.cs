using System.Diagnostics.Eventing.Reader;
using System.Management;
using FaultWitness.Core;

namespace FaultWitness.Platform.Windows;

public interface IWindowsReadinessSourceProbe
{
    Task<DiagnosticCapabilityStatus> ProbeAsync(string id, CancellationToken token);
}

public enum ReadinessDirectoryState { Accessible, Missing, Denied, Failed, Reparse }
public sealed record ReadinessFileSystemEntry(string Path, bool IsDirectory, bool IsReparsePoint);
public sealed record ReadinessDirectoryResult(ReadinessDirectoryState State, IReadOnlyList<ReadinessFileSystemEntry> Entries, bool Truncated = false);
public interface IWindowsReadinessFileSystem
{
    ReadinessDirectoryResult Read(string path, CancellationToken token);
}

internal sealed class WindowsReadinessFileSystem : IWindowsReadinessFileSystem
{
    public ReadinessDirectoryResult Read(string path, CancellationToken token)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReparsePoint) != 0) return new(ReadinessDirectoryState.Reparse, []);
            var entries = new List<ReadinessFileSystemEntry>();
            foreach (var item in Directory.EnumerateFileSystemEntries(path))
            {
                token.ThrowIfCancellationRequested();
                var itemAttrs = File.GetAttributes(item);
                entries.Add(new ReadinessFileSystemEntry(item, (itemAttrs & FileAttributes.Directory) != 0, (itemAttrs & FileAttributes.ReparsePoint) != 0));
                if (entries.Count > 256) return new(ReadinessDirectoryState.Accessible, entries, true);
            }
            return new(ReadinessDirectoryState.Accessible, entries);
        }
        catch (DirectoryNotFoundException) { return new(ReadinessDirectoryState.Missing, []); }
        catch (FileNotFoundException) { return new(ReadinessDirectoryState.Missing, []); }
        catch (UnauthorizedAccessException) { return new(ReadinessDirectoryState.Denied, []); }
        catch (IOException) { return new(ReadinessDirectoryState.Failed, []); }
    }
}

public sealed class WindowsReadinessSources
{
    private static readonly (string Id, string NameKey)[] Sources =
    [
        ("system-event-log", "SourceSystem"),
        ("application-event-log", "SourceApplication"),
        ("wer", "SourceTypeWer"),
        ("reliability", "SourceTypeReliability"),
        ("crash-artifacts", "SourceTypeCrashArtifact")
    ];

    private readonly IWindowsReadinessSourceProbe _probe;

    public WindowsReadinessSources(IWindowsReadinessSourceProbe? probe = null) =>
        _probe = probe ?? new WindowsReadinessSourceProbe();

    public async Task<IReadOnlyList<DiagnosticReadinessItem>> GetAsync(CancellationToken cancellationToken)
    {
        var tasks = Sources.Select(source => ProbeOneAsync(source, cancellationToken)).ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<DiagnosticReadinessItem> ProbeOneAsync((string Id, string NameKey) source, CancellationToken cancellationToken)
    {
        DiagnosticCapabilityStatus status;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var operation = _probe.ProbeAsync(source.Id, timeout.Token);
            status = await operation.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) { status = DiagnosticCapabilityStatus.Limited; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { status = DiagnosticCapabilityStatus.Limited; }
        catch (PlatformNotSupportedException) { status = DiagnosticCapabilityStatus.NotSupported; }
        catch { status = DiagnosticCapabilityStatus.Unavailable; }

        var detail = status switch
        {
            DiagnosticCapabilityStatus.Ready => "ReadinessSourceReady",
            DiagnosticCapabilityStatus.Limited => "ReadinessSourceLimited",
            DiagnosticCapabilityStatus.AccessDenied => "ReadinessSourceAccessDenied",
            DiagnosticCapabilityStatus.NotSupported => "ReadinessSourceNotSupported",
            _ => "ReadinessSourceUnavailable"
        };
        var elevation = status == DiagnosticCapabilityStatus.AccessDenied;
        return new DiagnosticReadinessItem(source.Id, source.NameKey, status, detail, null, elevation,
            elevation ? "ReadinessElevationHelp" : null);
    }
}

public sealed class WindowsReadinessSourceProbe : IWindowsReadinessSourceProbe
{
    public Task<DiagnosticCapabilityStatus> ProbeAsync(string id, CancellationToken token) => id switch
    {
        "system-event-log" => ProbeEventLogAsync("System", token),
        "application-event-log" => ProbeEventLogAsync("Application", token),
        "wer" => ProbeWerAsync(token),
        "reliability" => ProbeReliabilityAsync(token),
        "crash-artifacts" => ProbeCrashArtifactsAsync(token),
        _ => Task.FromResult(DiagnosticCapabilityStatus.NotSupported)
    };

    private static async Task<DiagnosticCapabilityStatus> ProbeEventLogAsync(string channel, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) return DiagnosticCapabilityStatus.NotSupported;
        return await Task.Run(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                using var reader = new EventLogReader(new EventLogQuery(channel, PathType.LogName));
                using var record = reader.ReadEvent(TimeSpan.FromSeconds(3));
                return DiagnosticCapabilityStatus.Ready;
            }
            catch (Exception ex) when (IsAccessDenied(ex)) { return DiagnosticCapabilityStatus.AccessDenied; }
            catch (PlatformNotSupportedException) { return DiagnosticCapabilityStatus.NotSupported; }
            catch (EventLogException) { return DiagnosticCapabilityStatus.Unavailable; }
            catch (UnauthorizedAccessException) { return DiagnosticCapabilityStatus.AccessDenied; }
        }, token).WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
    }

    private static async Task<DiagnosticCapabilityStatus> ProbeWerAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) return DiagnosticCapabilityStatus.NotSupported;
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportArchive"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportQueue")
        };
        return await Task.Run(() => ProbeDirectories(roots, static path =>
        {
            if (!string.Equals(Path.GetFileName(path), "Report.wer", StringComparison.OrdinalIgnoreCase)) return;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
        }, new WindowsReadinessFileSystem(), false, token), token).WaitAsync(TimeSpan.FromSeconds(8), token).ConfigureAwait(false);
    }

    private static async Task<DiagnosticCapabilityStatus> ProbeCrashArtifactsAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) return DiagnosticCapabilityStatus.NotSupported;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var roots = new[]
        {
            Path.Combine(windows, "Minidump"), Path.Combine(windows, "LiveKernelReports"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps")
        };
        return await Task.Run(() => ProbeDirectories(roots, path =>
        {
            var name = Path.GetFileName(path);
            if (!name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase) && !string.Equals(name, "MEMORY.DMP", StringComparison.OrdinalIgnoreCase)) return;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
        }, new WindowsReadinessFileSystem(), true, token), token).WaitAsync(TimeSpan.FromSeconds(8), token).ConfigureAwait(false);
    }

    private static async Task<DiagnosticCapabilityStatus> ProbeReliabilityAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) return DiagnosticCapabilityStatus.NotSupported;
        return await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TimeGenerated FROM Win32_ReliabilityRecords");
                searcher.Options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(3) };
                using var results = searcher.Get();
                using var enumerator = results.GetEnumerator();
                token.ThrowIfCancellationRequested();
                if (enumerator.MoveNext() && enumerator.Current is IDisposable item) item.Dispose();
                return DiagnosticCapabilityStatus.Ready;
            }
            catch (Exception ex) when (IsAccessDenied(ex)) { return DiagnosticCapabilityStatus.AccessDenied; }
            catch (PlatformNotSupportedException) { return DiagnosticCapabilityStatus.NotSupported; }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.NotFound || ex.ErrorCode == ManagementStatus.InvalidClass) { return DiagnosticCapabilityStatus.Unavailable; }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied) { return DiagnosticCapabilityStatus.AccessDenied; }
            catch (ManagementException) { return DiagnosticCapabilityStatus.Unavailable; }
        }, token).WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
    }

    public static DiagnosticCapabilityStatus ProbeDirectories(IEnumerable<string> roots, Action<string> inspectFile, IWindowsReadinessFileSystem fileSystem, bool artifactOnly, CancellationToken token)
    {
        var rootList = roots.ToArray();
        var existing = 0; var missing = 0; var accessible = 0; var denied = 0; var failed = 0; var truncated = false; var skippedReparse = false; var entries = 0;
        foreach (var root in rootList)
        {
            token.ThrowIfCancellationRequested();
            var rootResult = fileSystem.Read(root, token);
            if (rootResult.State == ReadinessDirectoryState.Missing) { missing++; continue; }
            if (rootResult.State == ReadinessDirectoryState.Denied) { denied++; continue; }
            if (rootResult.State == ReadinessDirectoryState.Reparse) { skippedReparse = true; continue; }
            if (rootResult.State == ReadinessDirectoryState.Failed) { failed++; continue; }
            existing++; accessible++;
            var stack = new Stack<(string Path, int Depth)>(); stack.Push((root, 0));
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var (directory, depth) = stack.Pop();
                var result = directory == root ? rootResult : fileSystem.Read(directory, token);
                if (result.State == ReadinessDirectoryState.Denied) { denied++; continue; }
                if (result.State == ReadinessDirectoryState.Failed) { failed++; continue; }
                if (result.State == ReadinessDirectoryState.Missing) { failed++; continue; }
                accessible++;
                if (result.Truncated) truncated = true;
                foreach (var entry in result.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (++entries > 256) { truncated = true; break; }
                    if (entry.IsReparsePoint) { skippedReparse = true; continue; }
                    if (entry.IsDirectory)
                    {
                        if (depth >= 8) { truncated = true; continue; }
                        stack.Push((entry.Path, depth + 1));
                        continue;
                    }
                    var name = Path.GetFileName(entry.Path);
                    if (!artifactOnly || name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase))
                    {
                        try { inspectFile(entry.Path); } catch (UnauthorizedAccessException) { denied++; } catch (IOException) { failed++; }
                    }
                }
                if (truncated) break;
            }
            if (truncated) break;
        }
        if (existing == 0) return skippedReparse ? DiagnosticCapabilityStatus.Limited : denied > 0 ? (missing > 0 ? DiagnosticCapabilityStatus.Limited : DiagnosticCapabilityStatus.AccessDenied) : DiagnosticCapabilityStatus.Unavailable;
        if (accessible == 0 && denied > 0) return DiagnosticCapabilityStatus.AccessDenied;
        if (denied > 0 || failed > 0 || truncated || skippedReparse || missing > 0) return DiagnosticCapabilityStatus.Limited;
        return DiagnosticCapabilityStatus.Ready;
    }

    private static bool IsAccessDenied(Exception ex) => ex is UnauthorizedAccessException || (ex.HResult & 0xFFFF) == 5;
}
