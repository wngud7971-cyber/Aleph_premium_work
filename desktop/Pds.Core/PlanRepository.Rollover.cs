using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pds.Core;

public sealed record PlanOrigin(Guid PlanId, DateOnly OriginalDate, DateTimeOffset? CreatedUtc);
public sealed record PlanMove(Guid PlanId, DateOnly From, DateOnly To, DateOnly CutoffDate, DateTimeOffset ProcessedUtc,
    string? PreviousStart, string? PreviousEnd, string Reason, int MissedCutoffs);
public sealed record RolloverResult(int Moved, int Deferred, DateOnly CutoffDate);

public sealed partial class PlanRepository
{
    private void WritePlanMetadata(SqliteConnection connection, SqliteTransaction transaction, PlanEntry entry, DateOnly date)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO plan_metadata(plan_id,original_date,created_utc,actual_date) VALUES($id,$date,$created,$actual)
            ON CONFLICT(plan_id) DO UPDATE SET actual_date=$actual
            """;
        command.Parameters.AddWithValue("$id", entry.Id.ToString()); command.Parameters.AddWithValue("$date", Key(date));
        command.Parameters.AddWithValue("$created", clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$actual", entry.ActualDate is DateOnly actual ? Key(actual) : DBNull.Value);
        command.ExecuteNonQuery();
    }

    public static DateOnly LatestCutoff(DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone ?? TimeZoneInfo.Local);
        var date = DateOnly.FromDateTime(local.DateTime);
        return local.Hour >= 23 ? date : date.AddDays(-1);
    }

    public RolloverResult Rollover(DateTimeOffset now, IReadOnlySet<DateOnly>? protectedDates = null, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var cutoff = LatestCutoff(now, zone); var target = cutoff.AddDays(1);
        var wallTime = cutoff.ToDateTime(new TimeOnly(23, 0));
        var cutoffInstant = new DateTimeOffset(wallTime, zone.GetUtcOffset(wallTime));
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            SELECT p.id,p.date,p.start_time,p.end_time,m.created_utc FROM plans p JOIN plan_metadata m ON m.plan_id=p.id
            WHERE p.done=0 AND p.date <= $cutoff ORDER BY p.date,p.position,p.id
            """;
        command.Parameters.AddWithValue("$cutoff", Key(cutoff));
        var candidates = new List<(Guid Id, DateOnly From, string? Start, string? End, DateTimeOffset? Created)>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) candidates.Add((Guid.Parse(reader.GetString(0)),
            DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture)));
        var changed = new HashSet<DateOnly>(); var moved = 0; var deferred = 0;
        foreach (var candidate in candidates)
        {
            // New late-night input waits for the next cutoff. Legacy creation times remain unknown.
            if (candidate.Created > cutoffInstant) continue;
            if (protectedDates?.Contains(candidate.From) == true || protectedDates?.Contains(target) == true) { deferred++; continue; }
            using var move = connection.CreateCommand(); move.Transaction = transaction;
            move.CommandText = "INSERT OR IGNORE INTO days(date,revision) VALUES($target,0); UPDATE plans SET date=$target,position=(SELECT COALESCE(MAX(position),-1)+1 FROM plans WHERE date=$target),start_time=NULL,end_time=NULL WHERE id=$id";
            move.Parameters.AddWithValue("$target", Key(target)); move.Parameters.AddWithValue("$id", candidate.Id.ToString()); move.ExecuteNonQuery();
            move.CommandText = "INSERT INTO plan_rollovers(plan_id,from_date,to_date,cutoff_date,processed_utc,previous_start,previous_end,reason,missed_cutoffs) VALUES($id,$from,$target,$cutoff,$processed,$start,$end,$reason,$missed)";
            move.Parameters.AddWithValue("$from", Key(candidate.From)); move.Parameters.AddWithValue("$cutoff", Key(cutoff));
            move.Parameters.AddWithValue("$processed", now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            move.Parameters.AddWithValue("$start", (object?)candidate.Start ?? DBNull.Value); move.Parameters.AddWithValue("$end", (object?)candidate.End ?? DBNull.Value);
            move.Parameters.AddWithValue("$reason", candidate.From < cutoff ? "catch-up" : "23:00");
            var firstEligible = candidate.From;
            if (candidate.Created is DateTimeOffset created)
            {
                var createdLocal = TimeZoneInfo.ConvertTime(created, zone);
                var createdCutoffDate = DateOnly.FromDateTime(createdLocal.DateTime).AddDays(createdLocal.TimeOfDay > TimeSpan.FromHours(23) ? 1 : 0);
                if (createdCutoffDate > firstEligible) firstEligible = createdCutoffDate;
            }
            move.Parameters.AddWithValue("$missed", Math.Max(1, target.DayNumber - firstEligible.DayNumber)); move.ExecuteNonQuery();
            changed.Add(candidate.From); changed.Add(target); moved++;
        }
        foreach (var date in changed)
        {
            using var revision = connection.CreateCommand(); revision.Transaction = transaction;
            revision.CommandText = "UPDATE days SET revision=revision+1 WHERE date=$date";
            revision.Parameters.AddWithValue("$date", Key(date)); revision.ExecuteNonQuery();
        }
        transaction.Commit(); return new(moved, deferred, cutoff);
    }

    public (PlanOrigin? Origin, IReadOnlyList<PlanMove> Moves) LoadPlanHistory(Guid id)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction(deferred: true);
        var origin = ReadOrigins(connection, transaction).FirstOrDefault(p => p.PlanId == id);
        var moves = ReadMoves(connection, transaction).Where(p => p.PlanId == id).ToArray();
        transaction.Commit(); return (origin, moves);
    }
    private static List<PlanOrigin> ReadOrigins(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT plan_id,original_date,created_utc FROM plan_metadata ORDER BY plan_id";
        var result = new List<PlanOrigin>(); using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture)));
        return result;
    }
    private static List<PlanMove> ReadMoves(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT plan_id,from_date,to_date,cutoff_date,processed_utc,previous_start,previous_end,reason,missed_cutoffs FROM plan_rollovers ORDER BY id";
        var result = new List<PlanMove>(); using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture), DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetInt32(8)));
        return result;
    }
}
