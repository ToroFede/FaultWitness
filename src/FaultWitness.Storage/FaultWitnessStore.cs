using System.Text.Json;
using FaultWitness.Core;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace FaultWitness.Storage;

public sealed record ScanHistoryMetadata(string AnalysisType, DateTimeOffset? RequestedFromUtc, DateTimeOffset? RequestedToUtc,
    long? DurationMilliseconds, int? NeedsAttention, int? WorthKnowing, int? Background, string? CoverageSummary);
public sealed record StoredScan(string Id, DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc, string RulesVersion,
    ScanHistoryMetadata? Metadata, IReadOnlyList<StoredIncident> Incidents);
public sealed record StoredIncident(string Id, DateTimeOffset OccurredUtc, string Category, string Severity, string Signature, string SummaryJson);

/// <summary>Stores compact scan results only; raw event log data is intentionally not retained.</summary>
public sealed class FaultWitnessStore(string databasePath)
{
    private readonly string connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS scans (id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, finished_utc TEXT NOT NULL, rules_version TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS incidents (id TEXT PRIMARY KEY, scan_id TEXT NOT NULL, occurred_utc TEXT NOT NULL, category TEXT NOT NULL, severity TEXT NOT NULL, signature TEXT NOT NULL, summary_json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS action_journal (id TEXT PRIMARY KEY, action_key TEXT NOT NULL, before_state TEXT NOT NULL, after_state TEXT NOT NULL, recorded_utc TEXT NOT NULL);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await EnsureScanColumnAsync(connection, "analysis_type", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureScanColumnAsync(connection, "requested_from_utc", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureScanColumnAsync(connection, "requested_to_utc", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureScanColumnAsync(connection, "duration_ms", "INTEGER NULL", cancellationToken).ConfigureAwait(false);
        await EnsureScanColumnAsync(connection, "attention_count", "INTEGER NULL", cancellationToken).ConfigureAwait(false);
        await EnsureScanColumnAsync(connection, "knowing_count", "INTEGER NULL", cancellationToken).ConfigureAwait(false);
        await EnsureScanColumnAsync(connection, "background_count", "INTEGER NULL", cancellationToken).ConfigureAwait(false);
        await EnsureScanColumnAsync(connection, "coverage_summary", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await using var version = connection.CreateCommand(); version.CommandText = "PRAGMA user_version = 2;";
        await version.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveScanAsync(ScanResult result, string ruleVersion, CancellationToken cancellationToken) => SaveScanAsync(result, ruleVersion, null, cancellationToken);

    public async Task SaveScanAsync(ScanResult result, string ruleVersion, ScanHistoryMetadata? metadata, CancellationToken cancellationToken)
    {
        var scanId = Guid.NewGuid().ToString("N");
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO scans (id,started_utc,finished_utc,rules_version,analysis_type,requested_from_utc,requested_to_utc,duration_ms,attention_count,knowing_count,background_count,coverage_summary)
            VALUES ($id,$started,$finished,$rules,$type,$from,$to,$duration,$attention,$knowing,$background,$coverage)
            """, [("$id", scanId), ("$started", result.StartedUtc.ToString("O")), ("$finished", result.FinishedUtc.ToString("O")), ("$rules", ruleVersion),
                ("$type", metadata?.AnalysisType), ("$from", metadata?.RequestedFromUtc?.ToString("O")), ("$to", metadata?.RequestedToUtc?.ToString("O")),
                ("$duration", metadata?.DurationMilliseconds?.ToString(CultureInfo.InvariantCulture)), ("$attention", metadata?.NeedsAttention?.ToString(CultureInfo.InvariantCulture)), ("$knowing", metadata?.WorthKnowing?.ToString(CultureInfo.InvariantCulture)),
                ("$background", metadata?.Background?.ToString(CultureInfo.InvariantCulture)), ("$coverage", metadata?.CoverageSummary)], cancellationToken).ConfigureAwait(false);
        foreach (var incident in result.Incidents)
        {
            var compact = new
            {
                incident.Id, incident.StartTimeUtc, incident.EndTimeUtc, incident.Category, incident.Severity, incident.Signature,
                Findings = incident.Findings.Select(finding => new { finding.RuleId, finding.Strength }),
                ChangeContext = incident.ChangeContext is { } context
                    ? context with { TotalChangeCount = Math.Max(context.TotalChangeCount, incident.RelatedChanges.Count) } : null,
                RelatedChanges = incident.RelatedChanges.OrderBy(related => related.Relevance)
                    .ThenBy(related => related.OffsetFromFirstObservation.Duration()).Take(12)
                    .Select(related => related with { Change = SystemChangePrivacy.Sanitize(related.Change) }).ToArray()
            };
            await ExecuteAsync(connection, transaction, "INSERT INTO incidents VALUES ($id,$scan,$time,$category,$severity,$signature,$json)", [("$id", incident.Id.ToString("N")), ("$scan", scanId), ("$time", incident.StartTimeUtc.ToString("O")), ("$category", incident.Category.ToString()), ("$severity", incident.Severity.ToString()), ("$signature", incident.Signature), ("$json", JsonSerializer.Serialize(compact))], cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StoredScan>> LoadScansAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var scans = new List<StoredScan>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,started_utc,finished_utc,rules_version,analysis_type,requested_from_utc,requested_to_utc,duration_ms,attention_count,knowing_count,background_count,coverage_summary
            FROM scans ORDER BY finished_utc DESC LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            scans.Add(new(id, DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture), reader.GetString(3),
                Metadata(reader), await LoadIncidentsAsync(connectionString, id, cancellationToken).ConfigureAwait(false)));
        }
        return scans;
    }

    /// <summary>Returns exact retained occurrences eligible for first-observation enrichment.</summary>
    public async Task<IReadOnlyList<RetainedOccurrence>> LoadRetainedOccurrencesAsync(DateTimeOffset cutoffUtc, string rulesVersion, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.signature,i.category,i.occurred_utc,i.summary_json
            FROM incidents i JOIN scans s ON s.id=i.scan_id
            WHERE s.finished_utc >= $cutoff AND s.rules_version=$rules
              AND s.analysis_type IN ('recent','around')
            ORDER BY i.occurred_utc ASC
            """;
        command.Parameters.AddWithValue("$cutoff", cutoffUtc.ToString("O"));
        command.Parameters.AddWithValue("$rules", rulesVersion);
        var result = new List<RetainedOccurrence>();
        var seen = new HashSet<(string Signature, IncidentCategory Category, DateTimeOffset Timestamp)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var signature = reader.GetString(0);
            if (!Enum.TryParse<IncidentCategory>(reader.GetString(1), out var category)) continue;
            if (!DateTimeOffset.TryParse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var occurred)) continue;
            var timestamp = occurred;
            try
            {
                using var summary = JsonDocument.Parse(reader.GetString(3));
                if (summary.RootElement.ValueKind == JsonValueKind.Object && summary.RootElement.TryGetProperty("ChangeContext", out var context) && context.ValueKind == JsonValueKind.Object &&
                    context.TryGetProperty("FirstObservedUtc", out var observed) && observed.ValueKind == JsonValueKind.String &&
                    observed.TryGetDateTimeOffset(out var first) && first <= occurred) timestamp = first;
            }
            catch (JsonException) { }
            if (seen.Add((signature, category, occurred))) result.Add(new(signature, category, occurred));
            if (seen.Add((signature, category, timestamp))) result.Add(new(signature, category, timestamp));
        }
        return result;
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null, "DELETE FROM incidents; DELETE FROM scans; DELETE FROM action_journal;", [], cancellationToken).ConfigureAwait(false);
    }

    public async Task PruneAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, transaction,
            "DELETE FROM incidents WHERE scan_id IN (SELECT id FROM scans WHERE finished_utc < $before); DELETE FROM scans WHERE finished_utc < $before;",
            [("$before", beforeUtc.ToString("O"))], cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ScanHistoryMetadata? Metadata(SqliteDataReader reader)
    {
        if (reader.IsDBNull(4)) return null;
        DateTimeOffset? Date(int i) => reader.IsDBNull(i) ? null : DateTimeOffset.Parse(reader.GetString(i), CultureInfo.InvariantCulture);
        long? Long(int i) => reader.IsDBNull(i) ? null : reader.GetInt64(i);
        int? Int(int i) => reader.IsDBNull(i) ? null : reader.GetInt32(i);
        return new(reader.GetString(4), Date(5), Date(6), Long(7), Int(8), Int(9), Int(10), reader.IsDBNull(11) ? null : reader.GetString(11));
    }

    private static async Task<IReadOnlyList<StoredIncident>> LoadIncidentsAsync(string cs, string scanId, CancellationToken token)
    {
        await using var connection = new SqliteConnection(cs); await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT id,occurred_utc,category,severity,signature,summary_json FROM incidents WHERE scan_id=$scan ORDER BY occurred_utc DESC";
        command.Parameters.AddWithValue("$scan", scanId); var result = new List<StoredIncident>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(new(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        return result;
    }

    private static async Task EnsureScanColumnAsync(SqliteConnection connection, string name, string definition, CancellationToken token)
    {
        await using var inspect = connection.CreateCommand(); inspect.CommandText = "PRAGMA table_info(scans);";
        await using var reader = await inspect.ExecuteReaderAsync(token).ConfigureAwait(false); var found = false;
        while (await reader.ReadAsync(token).ConfigureAwait(false)) found |= string.Equals(reader.GetString(1), name, StringComparison.OrdinalIgnoreCase);
        if (found) return;
        await using var alter = connection.CreateCommand(); alter.CommandText = $"ALTER TABLE scans ADD COLUMN {name} {definition};";
        await alter.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, (string Name, string? Value)[] parameters, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value is null ? DBNull.Value : value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

}
