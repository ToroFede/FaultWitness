#pragma warning disable xUnit1051
using FaultWitness.Core;
using FaultWitness.Storage;
using Microsoft.Data.Sqlite;

namespace FaultWitness.UI.Tests;

public sealed class CaptureJournalTests
{
    [Fact]
    public async Task RoundTripAndResultUpdatePreserveActionIdentity()
    {
        using var temp = new TemporaryDatabase();
        var store = new CaptureJournalStore(temp.Path);
        var entry = Entry();
        await store.SaveAsync(entry, CancellationToken.None);
        await store.SaveAsync(entry with { Result = CaptureResultCode.Success, ObservedState = entry.RequestedState, RollbackAvailable = true }, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Single(loaded);
        Assert.Equal(CaptureResultCode.Success, loaded[0].Result);
        Assert.True(loaded[0].RollbackAvailable);
    }

    [Fact]
    public async Task SupportsFailedCancelledAndRollbackCompleteTransitions()
    {
        using var temp = new TemporaryDatabase(); var store = new CaptureJournalStore(temp.Path);
        var failed = Entry(); await store.SaveAsync(failed, CancellationToken.None);
        await store.SaveAsync(failed with { Result = CaptureResultCode.ApplyFailed, NativeError = 5 }, CancellationToken.None);
        var cancelled = Entry(); await store.SaveAsync(cancelled, CancellationToken.None);
        await store.SaveAsync(cancelled with { Result = CaptureResultCode.CancelledByUser }, CancellationToken.None);
        var rollback = Entry(); await store.SaveAsync(rollback, CancellationToken.None);
        await store.SaveAsync(rollback with { Result = CaptureResultCode.Success, RollbackAvailable = true }, CancellationToken.None);
        await store.SaveAsync(rollback with { Result = CaptureResultCode.Success, RollbackAvailable = false, RollbackStatus = "Complete" }, CancellationToken.None);
        var rows = await store.LoadAsync(CancellationToken.None);
        Assert.Contains(rows, x => x.Result == CaptureResultCode.ApplyFailed && x.NativeError == 5);
        Assert.Contains(rows, x => x.Result == CaptureResultCode.CancelledByUser);
        Assert.Contains(rows, x => x.RollbackStatus == "Complete" && !x.RollbackAvailable);
    }

    [Fact]
    public async Task EmptyDatabaseInitializesAndConflictingUpdateIsRejected()
    {
        using var temp = new TemporaryDatabase(); var first = new CaptureJournalStore(temp.Path); var entry = Entry();
        Assert.Empty(await first.LoadAsync(CancellationToken.None)); await first.SaveAsync(entry, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CaptureJournalStore(temp.Path).SaveAsync(entry with { TargetExecutable = "Other.exe" }, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsPrivateFolderAndMalformedRowsAreSkipped()
    {
        using var temp = new TemporaryDatabase();
        var store = new CaptureJournalStore(temp.Path);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(Entry() with { RequestedState = new(true, 1, 3, @"C:\Users\secret", "") }, CancellationToken.None));
        await store.SaveAsync(Entry(), CancellationToken.None);
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp.Path }.ToString()); await db.OpenAsync();
        await using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE crash_capture_actions SET target_executable='not valid'"; await cmd.ExecuteNonQueryAsync();
        Assert.Empty(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FaultWitnessStoreMigratesSchemaTwoAndClearPreservesJournal()
    {
        using var temp = new TemporaryDatabase();
        await using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp.Path }.ToString()))
        { await db.OpenAsync(); await using var cmd = db.CreateCommand(); cmd.CommandText = "CREATE TABLE scans(id TEXT PRIMARY KEY,started_utc TEXT NOT NULL,finished_utc TEXT NOT NULL,rules_version TEXT NOT NULL); CREATE TABLE incidents(id TEXT PRIMARY KEY,scan_id TEXT NOT NULL,occurred_utc TEXT NOT NULL,category TEXT NOT NULL,severity TEXT NOT NULL,signature TEXT NOT NULL,summary_json TEXT NOT NULL); CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL); CREATE TABLE action_journal(id TEXT PRIMARY KEY,action_key TEXT NOT NULL,before_state TEXT NOT NULL,after_state TEXT NOT NULL,recorded_utc TEXT NOT NULL); INSERT INTO scans VALUES('scan-1','2026-01-01T00:00:00Z','2026-01-01T00:01:00Z','rules'); INSERT INTO action_journal VALUES('legacy-1','Example.exe','{}','{}','2026-01-01T00:00:00Z'); PRAGMA user_version=2;"; await cmd.ExecuteNonQueryAsync(); }
        var store = new FaultWitnessStore(temp.Path); await store.InitializeAsync(CancellationToken.None);
        await using (var beforeClear = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp.Path }.ToString())) { await beforeClear.OpenAsync(); await using var history = beforeClear.CreateCommand(); history.CommandText = "SELECT COUNT(*) FROM scans"; Assert.Equal(1L, await history.ExecuteScalarAsync()); await using var legacy = beforeClear.CreateCommand(); legacy.CommandText = "SELECT COUNT(*) FROM action_journal"; Assert.Equal(1L, await legacy.ExecuteScalarAsync()); }
        await new CaptureJournalStore(temp.Path).SaveAsync(Entry(), CancellationToken.None); await store.ClearAsync(CancellationToken.None);
        Assert.Single(await new CaptureJournalStore(temp.Path).LoadAsync(CancellationToken.None));
        await using var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp.Path }.ToString()); await check.OpenAsync(); await using var version = check.CreateCommand(); version.CommandText = "PRAGMA user_version"; Assert.Equal(3L, await version.ExecuteScalarAsync());
        await using var preserved = check.CreateCommand(); preserved.CommandText = "SELECT COUNT(*) FROM scans UNION ALL SELECT COUNT(*) FROM action_journal"; await using var rows = await preserved.ExecuteReaderAsync(); Assert.True(await rows.ReadAsync()); Assert.Equal(0L, rows.GetInt64(0)); Assert.True(await rows.ReadAsync()); Assert.Equal(1L, rows.GetInt64(0));
    }

    [Fact]
    public async Task RetentionPrunesOldTerminalButKeepsPendingAndRollback()
    {
        using var temp = new TemporaryDatabase(); var store = new CaptureJournalStore(temp.Path); var old = Entry(); await store.SaveAsync(old, CancellationToken.None);
        await store.SaveAsync(old with { Result = CaptureResultCode.Success }, CancellationToken.None);
        var pending = Entry(); await store.SaveAsync(pending, CancellationToken.None); var rollback = Entry(); await store.SaveAsync(rollback, CancellationToken.None);
        await store.SaveAsync(rollback with { Result = CaptureResultCode.Success, RollbackAvailable = true }, CancellationToken.None);
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp.Path }.ToString()); await db.OpenAsync(); await using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE crash_capture_actions SET updated_utc='2020-01-01T00:00:00Z' WHERE action_id=$id"; cmd.Parameters.AddWithValue("$id", old.ActionId.ToString("D")); await cmd.ExecuteNonQueryAsync();
        for (var i = 0; i < 501; i++) { var terminal = Entry(); await store.SaveAsync(terminal with { Result = CaptureResultCode.Success }, CancellationToken.None); }
        var rows = await store.LoadAsync(CancellationToken.None); Assert.DoesNotContain(rows, x => x.ActionId == old.ActionId); Assert.Contains(rows, x => x.ActionId == pending.ActionId); Assert.Contains(rows, x => x.ActionId == rollback.ActionId);
    }

    [Fact]
    public async Task MoreThanOneThousandRetainedRecoveryEntriesRemainLoadable()
    {
        using var temp = new TemporaryDatabase(); var store = new CaptureJournalStore(temp.Path);
        var oldestPending = Entry(); await store.SaveAsync(oldestPending, CancellationToken.None);
        var oldestRestore = Entry() with { Result = CaptureResultCode.Success, RollbackAvailable = true };
        await store.SaveAsync(oldestRestore, CancellationToken.None);
        for (var i = 0; i < 1001; i++) await store.SaveAsync(Entry(), CancellationToken.None);
        var reopened = await new CaptureJournalStore(temp.Path).LoadAsync(CancellationToken.None);
        Assert.Equal(1003, reopened.Count);
        Assert.Contains(reopened, x => x.ActionId == oldestPending.ActionId && x.Result == CaptureResultCode.Pending);
        Assert.Contains(reopened, x => x.ActionId == oldestRestore.ActionId && x.RollbackAvailable);
    }

    [Fact]
    public async Task PartiallyCorruptRowDoesNotBreakLoading()
    {
        using var temp = new TemporaryDatabase(); var store = new CaptureJournalStore(temp.Path); await store.SaveAsync(Entry(), CancellationToken.None);
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp.Path }.ToString()); await db.OpenAsync(); await using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE crash_capture_actions SET target_executable='bad target'"; await cmd.ExecuteNonQueryAsync(); Assert.Empty(await store.LoadAsync(CancellationToken.None));
    }

    private static CaptureJournalEntry Entry() => new(Guid.NewGuid(), CaptureOperation.ConfigureApplicationCrashDump, "Example.exe", DateTimeOffset.UtcNow, true, new(false), new(true, 1, 3, CrashCapturePolicy.Folder));
    private sealed class TemporaryDatabase : IDisposable { public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fw-journal-" + Guid.NewGuid().ToString("N") + ".db"); public void Dispose() { try { File.Delete(Path); } catch { } } }
}
