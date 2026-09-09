using FaultWitness.Core;
using FaultWitness.Storage;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "WhatChanged")]
public sealed class WhatChangedStorageTests
{
    [Fact]
    public async Task CompactHistoryKeepsBestTwelveAndDisclosesCountWhileRetainingEarlierObservation()
    {
        var directory = Directory.CreateTempSubdirectory("FaultWitness-compact-");
        try
        {
            var token = TestContext.Current.CancellationToken;
            var store = new FaultWitnessStore(Path.Combine(directory.FullName, "history.db"));
            await store.InitializeAsync(token);
            var scan = SyntheticResults.Create(1);
            var incident = scan.Incidents[0];
            var earlier = incident.StartTimeUtc.AddDays(-20);
            var changes = Enumerable.Range(0, 15).Select(index => new RelatedSystemChange(
                new SystemChange(index.ToString(System.Globalization.CultureInfo.InvariantCulture), "Windows", earlier.AddHours(-1), ChangeCategory.DriverInstalled,
                    "ChangeSourceSetupApi", "Synthetic driver", ChangeSubsystem.Display, null, "1.2.3.4", "Vendor", "synthetic"),
                index == 14 ? ContextualRelevance.High : ContextualRelevance.Low, ChangeTiming.Before, TimeSpan.FromHours(-1), "ChangeReasonSameSubsystem")).ToArray();
            incident = incident with { RelatedChanges = changes, ChangeContext = new(earlier, FirstObservationBasis.RetainedHistory, true, []) };
            await store.SaveScanAsync(scan with { Incidents = [incident] }, "rules", new("recent", scan.StartedUtc, scan.FinishedUtc, null, null, null, null, null), token);
            var saved = Assert.Single(await store.LoadScansAsync(1, token));
            using var json = JsonDocument.Parse(Assert.Single(saved.Incidents).SummaryJson);
            Assert.Equal(15, json.RootElement.GetProperty("ChangeContext").GetProperty("TotalChangeCount").GetInt32());
            var rows = json.RootElement.GetProperty("RelatedChanges");
            Assert.Equal(12, rows.GetArrayLength());
            Assert.Equal("14", rows[0].GetProperty("Change").GetProperty("Id").GetString());
            var retained = await store.LoadRetainedOccurrencesAsync(scan.FinishedUtc.AddDays(-1), "rules", token);
            Assert.Contains(retained, item => item.TimestampUtc == earlier);
            Assert.Contains(retained, item => item.TimestampUtc == incident.StartTimeUtc);
        }
        finally { SqliteConnection.ClearAllPools(); directory.Delete(true); }
    }

    [Fact]
    public async Task RetainedQueryFiltersOriginRulesCutoffAndDeduplicatesExactOccurrences()
    {
        var directory = Directory.CreateTempSubdirectory("FaultWitness-retained-");
        try
        {
            var path = Path.Combine(directory.FullName, "history.db"); var store = new FaultWitnessStore(path); await store.InitializeAsync(TestContext.Current.CancellationToken);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); await connection.OpenAsync(TestContext.Current.CancellationToken);
            var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero); var occurrence = now.AddDays(-2);
            await Insert(connection, "recent", "rules", now.AddDays(-1), "sig", occurrence, TestContext.Current.CancellationToken);
            await Insert(connection, "recent", "other", now, "wrong-rule", now, TestContext.Current.CancellationToken);
            await Insert(connection, "imported", "rules", now, "imported", now, TestContext.Current.CancellationToken);
            await Insert(connection, null, "rules", now, "unknown", now, TestContext.Current.CancellationToken);
            await Insert(connection, "around", "rules", now, "sig", occurrence, TestContext.Current.CancellationToken);
            await Insert(connection, "recent", "rules", now.AddDays(-30), "expired", now.AddDays(-30), TestContext.Current.CancellationToken);
            var rows = await store.LoadRetainedOccurrencesAsync(now.AddDays(-3), "rules", TestContext.Current.CancellationToken);
            var row = Assert.Single(rows); Assert.Equal("sig", row.Signature); Assert.Equal(IncidentCategory.Graphics, row.Category); Assert.Equal(occurrence.Date, row.TimestampUtc.Date);
        }
        finally { SqliteConnection.ClearAllPools(); directory.Delete(true); }
    }

    private static async Task Insert(SqliteConnection connection, string? origin, string rules, DateTimeOffset finished, string signature, DateTimeOffset occurred, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("N"); await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO scans(id,started_utc,finished_utc,rules_version,analysis_type) VALUES($id,$s,$f,$r,$a); INSERT INTO incidents(id,scan_id,occurred_utc,category,severity,signature,summary_json) VALUES($i,$id,$o,'Graphics','Low',$sig,'{}');";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$i", Guid.NewGuid().ToString("N")); command.Parameters.AddWithValue("$s", finished.AddMinutes(-1).ToString("O")); command.Parameters.AddWithValue("$f", finished.ToString("O")); command.Parameters.AddWithValue("$r", rules); command.Parameters.AddWithValue("$a", (object?)origin ?? DBNull.Value); command.Parameters.AddWithValue("$o", occurred.ToString("O")); command.Parameters.AddWithValue("$sig", signature); await command.ExecuteNonQueryAsync(token);
    }
}
