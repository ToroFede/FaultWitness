using FaultWitness.App;
using FaultWitness.Storage;
using Microsoft.Data.Sqlite;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Persistence")]
public sealed class PersistenceTests
{
    [Fact]
    public void Settings_RoundTripUsesOnlySuppliedApplicationDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("FaultWitness-test-");
        try
        {
            var service = new DesktopServices(directory.FullName);
            var settings = new UserSettings("pl", AppTheme.Dark, AnalysisPeriod.Month, 7);
            service.SaveSettings(settings);
            Assert.Equal(settings, new DesktopServices(directory.FullName).LoadSettings());
            Assert.Single(directory.GetFiles());
        }
        finally { directory.Delete(true); }
    }
    [Fact]
    public async Task CorruptSettings_FallBackWithoutPreventingStartup()
    {
        var directory = Directory.CreateTempSubdirectory("FaultWitness-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "settings.json"), "{invalid", TestContext.Current.CancellationToken);
            Assert.Equal(new UserSettings(), new DesktopServices(directory.FullName).LoadSettings());
        }
        finally { directory.Delete(true); }
    }
    [Fact]
    public async Task Retention_RemovesExpiredScansAndTheirIncidentsOnly()
    {
        var directory = Directory.CreateTempSubdirectory("FaultWitness-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "history.db"); var store = new FaultWitnessStore(path);
            await store.InitializeAsync(CancellationToken.None);
            var older = SyntheticResults.Create(3);
            var newer = SyntheticResults.Create(2) with { FinishedUtc = older.FinishedUtc.AddDays(10) };
            await store.SaveScanAsync(older, "test", CancellationToken.None);
            await store.SaveScanAsync(newer, "test", CancellationToken.None);
            await store.PruneAsync(older.FinishedUtc.AddDays(1), CancellationToken.None);
            Assert.Equal(1, await Count(path, "scans")); Assert.Equal(2, await Count(path, "incidents"));
            await store.ClearAsync(CancellationToken.None);
            Assert.Equal(0, await Count(path, "scans")); Assert.Equal(0, await Count(path, "incidents"));
        }
        finally { SqliteConnection.ClearAllPools(); directory.Delete(true); }
    }
    [Fact]
    public async Task HistoryMetadata_RoundTripsWithoutRawEventXml()
    {
        var directory = Directory.CreateTempSubdirectory("FaultWitness-history-");
        try
        {
            var path = Path.Combine(directory.FullName, "history.db"); var store = new FaultWitnessStore(path);
            await store.InitializeAsync(CancellationToken.None); var result = SyntheticResults.Create(3);
            var metadata = new ScanHistoryMetadata("recent", result.StartedUtc, result.FinishedUtc, 1250, 1, 1, 1, "SourceSystem=Complete");
            await store.SaveScanAsync(result, "test-rules", metadata, CancellationToken.None);
            var loaded = Assert.Single(await store.LoadScansAsync(20, CancellationToken.None));
            Assert.Equal(metadata, loaded.Metadata); Assert.Equal(3, loaded.Incidents.Count); Assert.Equal("test-rules", loaded.RulesVersion);
            Assert.DoesNotContain("synthetic raw", string.Join(" ", loaded.Incidents.Select(item => item.SummaryJson)), StringComparison.Ordinal);
        }
        finally { SqliteConnection.ClearAllPools(); directory.Delete(true); }
    }
    [Fact]
    public async Task ExistingVersionOneDatabase_MigratesAndKeepsItsSummary()
    {
        var directory = Directory.CreateTempSubdirectory("FaultWitness-migration-");
        try
        {
            var path = Path.Combine(directory.FullName, "history.db");
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken); await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE scans (id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, finished_utc TEXT NOT NULL, rules_version TEXT NOT NULL); CREATE TABLE incidents (id TEXT PRIMARY KEY, scan_id TEXT NOT NULL, occurred_utc TEXT NOT NULL, category TEXT NOT NULL, severity TEXT NOT NULL, signature TEXT NOT NULL, summary_json TEXT NOT NULL); INSERT INTO scans VALUES ('old','2026-01-01T00:00:00+00:00','2026-01-01T00:00:01+00:00','old-rules'); INSERT INTO incidents VALUES ('event','old','2026-01-01T00:00:00+00:00','Application','Low','sig','{}');";
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
            var store = new FaultWitnessStore(path); await store.InitializeAsync(CancellationToken.None);
            var loaded = Assert.Single(await store.LoadScansAsync(10, CancellationToken.None));
            Assert.Null(loaded.Metadata); Assert.Single(loaded.Incidents); Assert.Equal("old-rules", loaded.RulesVersion);
        }
        finally { SqliteConnection.ClearAllPools(); directory.Delete(true); }
    }
    private static async Task<long> Count(string path, string table)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        await connection.OpenAsync(); await using var command = connection.CreateCommand();
        command.CommandText = table == "scans" ? "SELECT COUNT(*) FROM scans" : "SELECT COUNT(*) FROM incidents";
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
