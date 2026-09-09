using System.Globalization;
using System.Text.Json;
using FaultWitness.Core;
using Microsoft.Data.Sqlite;

namespace FaultWitness.Storage;

/// <summary>Durable, privacy constrained journal for crash-capture actions.</summary>
public sealed class CaptureJournalStore(string databasePath) : ICaptureJournal
{
    private readonly string _connectionString = BuildConnectionString(databasePath);
    private static string BuildConnectionString(string path)
    {
        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        return new SqliteConnectionStringBuilder { DataSource = full, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
    }

    internal static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS crash_capture_actions (
              action_id TEXT PRIMARY KEY,
              operation INTEGER NOT NULL,
              target_executable TEXT NOT NULL,
              timestamp_utc TEXT NOT NULL,
              elevation_required INTEGER NOT NULL,
              previous_key_exists INTEGER NOT NULL,
              previous_dump_type INTEGER NULL,
              previous_dump_count INTEGER NULL,
              previous_dump_folder TEXT NULL,
              previous_other_fingerprint TEXT NOT NULL,
              previous_supported INTEGER NOT NULL,
              requested_key_exists INTEGER NOT NULL,
              requested_dump_type INTEGER NULL,
              requested_dump_count INTEGER NULL,
              requested_dump_folder TEXT NULL,
              requested_other_fingerprint TEXT NOT NULL,
              requested_supported INTEGER NOT NULL,
              result INTEGER NOT NULL,
              observed_key_exists INTEGER NULL,
              observed_dump_type INTEGER NULL,
              observed_dump_count INTEGER NULL,
              observed_dump_folder TEXT NULL,
              observed_other_fingerprint TEXT NULL,
              observed_supported INTEGER NULL,
              rollback_available INTEGER NOT NULL,
              rollback_status TEXT NOT NULL,
              native_error INTEGER NULL,
              schema_version INTEGER NOT NULL,
              app_version TEXT NOT NULL,
              updated_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_crash_capture_actions_updated ON crash_capture_actions(updated_utc DESC);
            """;
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        var current = Convert.ToInt32(await version.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (current < 3)
        {
            await using var upgrade = connection.CreateCommand();
            upgrade.CommandText = "PRAGMA user_version = 3;";
            await upgrade.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    public async Task SaveAsync(CaptureJournalEntry entry, CancellationToken token)
    {
        Validate(entry);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, token).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO crash_capture_actions
            (action_id,operation,target_executable,timestamp_utc,elevation_required,
             previous_key_exists,previous_dump_type,previous_dump_count,previous_dump_folder,previous_other_fingerprint,previous_supported,
             requested_key_exists,requested_dump_type,requested_dump_count,requested_dump_folder,requested_other_fingerprint,requested_supported,
             result,observed_key_exists,observed_dump_type,observed_dump_count,observed_dump_folder,observed_other_fingerprint,observed_supported,
             rollback_available,rollback_status,native_error,schema_version,app_version,updated_utc)
            VALUES ($id,$op,$target,$time,$elev,$pk,$pt,$pc,$pf,$ph,$ps,$rk,$rt,$rc,$rf,$rh,$rs,$result,$ok,$ot,$oc,$of,$oh,$os,$ra,$rb,$error,$schema,$app,$updated)
            ON CONFLICT(action_id) DO UPDATE SET
              result=$result, observed_key_exists=$ok, observed_dump_type=$ot, observed_dump_count=$oc,
              observed_dump_folder=$of, observed_other_fingerprint=$oh, observed_supported=$os,
              rollback_available=$ra, rollback_status=$rb, native_error=$error, updated_utc=$updated
            WHERE operation=$op AND target_executable=$target AND timestamp_utc=$time AND elevation_required=$elev AND schema_version=$schema AND app_version=$app
              AND previous_key_exists=$pk AND IFNULL(previous_dump_type,-1)=IFNULL($pt,-1)
              AND IFNULL(previous_dump_count,-1)=IFNULL($pc,-1) AND IFNULL(previous_dump_folder,'')=IFNULL($pf,'')
              AND previous_other_fingerprint=$ph AND previous_supported=$ps
              AND requested_key_exists=$rk AND IFNULL(requested_dump_type,-1)=IFNULL($rt,-1)
              AND IFNULL(requested_dump_count,-1)=IFNULL($rc,-1) AND IFNULL(requested_dump_folder,'')=IFNULL($rf,'')
              AND requested_other_fingerprint=$rh AND requested_supported=$rs;
            """;
        Add(command, entry);
        var changed = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        if (changed == 0) throw new InvalidOperationException("Capture action identity does not match the stored action.");
        await PruneAsync(connection, tx, token).ConfigureAwait(false);
        await tx.CommitAsync(token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CaptureJournalEntry>> LoadAsync(CancellationToken token)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, token).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM crash_capture_actions ORDER BY CASE WHEN result=$pending OR rollback_available<>0 THEN 0 ELSE 1 END, updated_utc DESC LIMIT 1000";
        command.Parameters.AddWithValue("$pending", (int)CaptureResultCode.Pending);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<CaptureJournalEntry>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            try { var entry = Read(reader); Validate(entry); result.Add(entry); } catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        return result;
    }

    private static void Add(SqliteCommand c, CaptureJournalEntry e)
    {
        var p = e.PreviousState; var r = e.RequestedState; var o = e.ObservedState;
        c.Parameters.AddWithValue("$id", e.ActionId.ToString("D")); c.Parameters.AddWithValue("$op", (int)e.Operation); c.Parameters.AddWithValue("$target", e.TargetExecutable); c.Parameters.AddWithValue("$time", e.TimestampUtc.ToString("O")); c.Parameters.AddWithValue("$elev", e.ElevationRequired ? 1 : 0);
        State(c, "previous", p); State(c, "requested", r); c.Parameters.AddWithValue("$result", (int)e.Result); NullableState(c, "observed", o);
        c.Parameters.AddWithValue("$ra", e.RollbackAvailable ? 1 : 0); c.Parameters.AddWithValue("$rb", e.RollbackStatus); c.Parameters.AddWithValue("$error", (object?)e.NativeError ?? DBNull.Value); c.Parameters.AddWithValue("$schema", e.SchemaVersion); c.Parameters.AddWithValue("$app", e.AppVersion); c.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
    }
    private static void State(SqliteCommand c, string n, LocalDumpState s) { c.Parameters.AddWithValue("$"+n[0]+"k", s.KeyExists?1:0); c.Parameters.AddWithValue("$"+n[0]+"t", (object?)s.DumpType??DBNull.Value); c.Parameters.AddWithValue("$"+n[0]+"c", (object?)s.DumpCount??DBNull.Value); c.Parameters.AddWithValue("$"+n[0]+"f", (object?)s.DumpFolder??DBNull.Value); c.Parameters.AddWithValue("$"+n[0]+"h", s.OtherValuesFingerprint); c.Parameters.AddWithValue("$"+n[0]+"s", s.Supported?1:0); }
    private static void NullableState(SqliteCommand c, string n, LocalDumpState? s) { var q=n[0]; c.Parameters.AddWithValue("$"+q+"k", (object?)(s is null ? null : s.KeyExists?1:0)??DBNull.Value); c.Parameters.AddWithValue("$"+q+"t", (object?)s?.DumpType??DBNull.Value); c.Parameters.AddWithValue("$"+q+"c", (object?)s?.DumpCount??DBNull.Value); c.Parameters.AddWithValue("$"+q+"f", (object?)s?.DumpFolder??DBNull.Value); c.Parameters.AddWithValue("$"+q+"h", (object?)s?.OtherValuesFingerprint??DBNull.Value); c.Parameters.AddWithValue("$"+q+"s", (object?)(s is null ? null : s.Supported?1:0)??DBNull.Value); }

    private static CaptureJournalEntry Read(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)), (CaptureOperation)r.GetInt32(1), r.GetString(2), DateTimeOffset.Parse(r.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), r.GetInt32(4)!=0, ReadState(r,5,"previous"), ReadState(r,11,"requested"), (CaptureResultCode)r.GetInt32(17), r.IsDBNull(18)?null:ReadState(r,18,"observed"), r.GetInt32(24)!=0, r.GetString(25), r.IsDBNull(26)?null:r.GetInt32(26), r.GetInt32(27), r.GetString(28));
    private static LocalDumpState ReadState(SqliteDataReader r, int i, string _) => new(r.IsDBNull(i)?false:r.GetInt32(i)!=0, r.IsDBNull(i+1)?null:r.GetInt32(i+1), r.IsDBNull(i+2)?null:r.GetInt32(i+2), r.IsDBNull(i+3)?null:r.GetString(i+3), r.IsDBNull(i+4)?"":r.GetString(i+4), r.IsDBNull(i+5)||r.GetInt32(i+5)!=0);

    private static void Validate(CaptureJournalEntry e)
    {
        if (e.ActionId == Guid.Empty || !Enum.IsDefined(e.Operation) || !Enum.IsDefined(e.Result) || !CrashCapturePolicy.IsValidExecutable(e.TargetExecutable) || e.SchemaVersion != 1 || e.AppVersion.Length is < 1 or > 32 || !Version.TryParse(e.AppVersion, out _) || e.RollbackStatus is not ("NotRequested" or "Complete" or "AutomaticComplete" or "Failed") || !CrashCapturePolicy.IsSupportedState(e.PreviousState) || !CrashCapturePolicy.IsSupportedState(e.RequestedState) || (e.ObservedState is not null && !CrashCapturePolicy.IsSupportedState(e.ObservedState))) throw new ArgumentException("Invalid capture journal entry.");
    }
    private static async Task PruneAsync(SqliteConnection c, SqliteTransaction tx, CancellationToken token)
    {
        var protectedCodes = string.Join(",", new[] { CaptureResultCode.ApplyFailed, CaptureResultCode.VerificationFailed, CaptureResultCode.RollbackFailed, CaptureResultCode.Pending }.Select(x => (int)x));
        await using var cmd=c.CreateCommand(); cmd.Transaction=tx; cmd.CommandText=$"DELETE FROM crash_capture_actions WHERE result NOT IN ({protectedCodes}) AND rollback_available=0 AND updated_utc < $cutoff AND action_id NOT IN (SELECT action_id FROM crash_capture_actions WHERE result NOT IN ({protectedCodes}) AND rollback_available=0 ORDER BY updated_utc DESC LIMIT 500)"; cmd.Parameters.AddWithValue("$cutoff",DateTimeOffset.UtcNow.AddDays(-90).ToString("O")); await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
