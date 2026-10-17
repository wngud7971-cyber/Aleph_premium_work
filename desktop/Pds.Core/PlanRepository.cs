using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Pds.Core;

public sealed partial class PlanRepository
{
    private readonly string connectionString;
    private readonly TimeProvider clock;
    public string DatabasePath { get; }

    public PlanRepository(string path, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        DatabasePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString();
        using var connection = Open();
        using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        var schema = Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (schema > 7) throw new InvalidDataException("이 앱보다 새로운 형식의 기록이에요. 최신 앱으로 열어 주세요.");
        using var transaction = connection.BeginTransaction();
        using var create = connection.CreateCommand();
        create.Transaction = transaction;
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS days (date TEXT PRIMARY KEY, revision INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS plans (
                id TEXT PRIMARY KEY, date TEXT NOT NULL REFERENCES days(date), position INTEGER NOT NULL,
                title TEXT NOT NULL, note TEXT NOT NULL, done INTEGER NOT NULL CHECK(done IN (0,1)),
                start_time TEXT, end_time TEXT);
            CREATE INDEX IF NOT EXISTS plans_date ON plans(date, position);
            CREATE TABLE IF NOT EXISTS workouts (
                id TEXT PRIMARY KEY, date TEXT NOT NULL REFERENCES days(date), position INTEGER NOT NULL,
                exercise_id TEXT NOT NULL, duration_seconds INTEGER CHECK(duration_seconds > 0 AND duration_seconds <= 86400),
                quantity INTEGER CHECK(quantity > 0), note TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS workouts_date ON workouts(date, position);
            CREATE TABLE IF NOT EXISTS workout_calculations (
                workout_id TEXT PRIMARY KEY REFERENCES workouts(id) ON DELETE CASCADE,
                active_seconds INTEGER, input_json TEXT, energy_json TEXT);
            CREATE TABLE IF NOT EXISTS body_weights (
                id INTEGER PRIMARY KEY AUTOINCREMENT, kg TEXT NOT NULL, entered_on TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS reviews (
                date TEXT PRIMARY KEY REFERENCES days(date), good TEXT NOT NULL CHECK(length(good)<=2000),
                difficult TEXT NOT NULL CHECK(length(difficult)<=2000), next_action TEXT NOT NULL CHECK(length(next_action)<=2000));
            CREATE TABLE IF NOT EXISTS plan_metadata (
                plan_id TEXT PRIMARY KEY REFERENCES plans(id) ON DELETE CASCADE,
                original_date TEXT NOT NULL, created_utc TEXT, actual_date TEXT);
            INSERT OR IGNORE INTO plan_metadata(plan_id,original_date) SELECT id,date FROM plans;
            CREATE INDEX IF NOT EXISTS plan_actual_date ON plan_metadata(actual_date);
            CREATE TABLE IF NOT EXISTS plan_rollovers (
                id INTEGER PRIMARY KEY AUTOINCREMENT, plan_id TEXT NOT NULL REFERENCES plans(id) ON DELETE CASCADE,
                from_date TEXT NOT NULL,to_date TEXT NOT NULL,cutoff_date TEXT NOT NULL,processed_utc TEXT NOT NULL,
                previous_start TEXT,previous_end TEXT,reason TEXT NOT NULL,missed_cutoffs INTEGER NOT NULL,
                UNIQUE(plan_id,cutoff_date));
            CREATE TABLE IF NOT EXISTS preferences (id INTEGER PRIMARY KEY CHECK(id=1),json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS anniversaries (id TEXT PRIMARY KEY,revision INTEGER NOT NULL,name TEXT NOT NULL,json TEXT NOT NULL);
            PRAGMA user_version = 7;
            """;
        create.ExecuteNonQuery();
        transaction.Commit();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var setup = connection.CreateCommand();
        setup.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        setup.ExecuteNonQuery();
        return connection;
    }

    public DaySnapshot Load(DateOnly date)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var result = ReadDay(connection, transaction, date);
        transaction.Commit();
        return result;
    }

    private static DaySnapshot ReadDay(SqliteConnection connection, SqliteTransaction transaction, DateOnly date)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM days WHERE date=$date";
        command.Parameters.AddWithValue("$date", Key(date));
        var revision = Convert.ToInt64(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
        command.CommandText = "SELECT p.id,p.title,p.note,p.done,p.start_time,p.end_time,m.actual_date FROM plans p LEFT JOIN plan_metadata m ON m.plan_id=p.id WHERE p.date=$date ORDER BY p.position";
        var entries = new List<PlanEntry>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                entries.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)) { ActualDate = reader.IsDBNull(6) ? null : DateOnly.ParseExact(reader.GetString(6), "yyyy-MM-dd", CultureInfo.InvariantCulture) });
        command.CommandText = "SELECT id,exercise_id,duration_seconds,quantity,note FROM workouts WHERE date=$date ORDER BY position";
        var workouts = new List<WorkoutEntry>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                workouts.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetString(4)));
        command.CommandText = "SELECT c.workout_id,c.active_seconds,c.input_json,c.energy_json FROM workout_calculations c JOIN workouts w ON w.id=c.workout_id WHERE w.date=$date";
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var index = workouts.FindIndex(w => w.Id == Guid.Parse(reader.GetString(0)));
                workouts[index] = workouts[index] with { ActiveSeconds = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    Calculation = reader.IsDBNull(2) ? null : JsonSerializer.Deserialize<EnergyInput>(reader.GetString(2)),
                    Energy = reader.IsDBNull(3) ? null : JsonSerializer.Deserialize<EnergySnapshot>(reader.GetString(3)) };
            }
        command.CommandText = "SELECT good,difficult,next_action FROM reviews WHERE date=$date";
        DayReview? review;
        using (var reader = command.ExecuteReader())
            review = reader.Read() ? new DayReview(reader.GetString(0), reader.GetString(1), reader.GetString(2)).Normalize() : null;
        return new(date, revision, entries) { Workouts = workouts, Review = review };
    }

    public DaySnapshot Save(DaySnapshot snapshot)
    {
        snapshot.Review?.Validate();
        var review = snapshot.Review?.Normalize();
        foreach (var plan in snapshot.Plans) plan.Validate();
        if (snapshot.Plans.Select(p => p.Id).Distinct().Count() != snapshot.Plans.Count)
            throw new ArgumentException("같은 계획이 중복되어 있어요.");
        foreach (var workout in snapshot.Workouts) workout.Validate();
        if (snapshot.Workouts.Select(w => w.Id).Distinct().Count() != snapshot.Workouts.Count)
            throw new ArgumentException("같은 운동 기록이 중복되어 있어요.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM days WHERE date=$date";
        command.Parameters.AddWithValue("$date", Key(snapshot.Date));
        var current = Convert.ToInt64(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
        if (current != snapshot.Revision) throw new SaveConflictException();
        VerifyFreshCalculations(connection, transaction, snapshot);
        command.CommandText = "INSERT INTO days(date,revision) VALUES($date,$revision) ON CONFLICT(date) DO UPDATE SET revision=$revision";
        command.Parameters.AddWithValue("$revision", current + 1);
        command.ExecuteNonQuery();
        command.CommandText = "SELECT id FROM plans WHERE date=$date";
        var existingIds = new HashSet<Guid>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) existingIds.Add(Guid.Parse(reader.GetString(0)));
        foreach (var removed in existingIds.Except(snapshot.Plans.Select(p => p.Id)))
        {
            using var delete = connection.CreateCommand(); delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM plans WHERE id=$id AND date=$date";
            delete.Parameters.AddWithValue("$id", removed.ToString()); delete.Parameters.AddWithValue("$date", Key(snapshot.Date)); delete.ExecuteNonQuery();
        }
        for (var i = 0; i < snapshot.Plans.Count; i++)
        {
            var entry = snapshot.Plans[i];
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = existingIds.Contains(entry.Id) ? """
                UPDATE plans SET position=$position,title=$title,note=$note,done=$done,start_time=$start,end_time=$end WHERE id=$id AND date=$date
                """ : """
                INSERT INTO plans(id,date,position,title,note,done,start_time,end_time)
                VALUES($id,$date,$position,$title,$note,$done,$start,$end)
                """;
            insert.Parameters.AddWithValue("$id", entry.Id.ToString());
            insert.Parameters.AddWithValue("$date", Key(snapshot.Date));
            insert.Parameters.AddWithValue("$position", i);
            insert.Parameters.AddWithValue("$title", entry.Title.Trim());
            insert.Parameters.AddWithValue("$note", entry.Note.Trim());
            insert.Parameters.AddWithValue("$done", entry.Done ? 1 : 0);
            insert.Parameters.AddWithValue("$start", (object?)entry.Start ?? DBNull.Value);
            insert.Parameters.AddWithValue("$end", (object?)entry.End ?? DBNull.Value);
            insert.ExecuteNonQuery();
            WritePlanMetadata(connection, transaction, entry, snapshot.Date);
        }
        command.CommandText = "DELETE FROM workouts WHERE date=$date";
        command.ExecuteNonQuery();
        for (var i = 0; i < snapshot.Workouts.Count; i++)
        {
            var entry = snapshot.Workouts[i];
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO workouts(id,date,position,exercise_id,duration_seconds,quantity,note)
                VALUES($id,$date,$position,$exercise,$seconds,$quantity,$note)
                """;
            insert.Parameters.AddWithValue("$id", entry.Id.ToString());
            insert.Parameters.AddWithValue("$date", Key(snapshot.Date));
            insert.Parameters.AddWithValue("$position", i);
            insert.Parameters.AddWithValue("$exercise", entry.ExerciseId);
            insert.Parameters.AddWithValue("$seconds", (object?)entry.DurationSeconds ?? DBNull.Value);
            insert.Parameters.AddWithValue("$quantity", (object?)entry.Quantity ?? DBNull.Value);
            insert.Parameters.AddWithValue("$note", entry.Note.Trim());
            insert.ExecuteNonQuery();
            if (entry.ActiveSeconds is not null || entry.Calculation is not null || entry.Energy is not null)
            {
                using var calculation = connection.CreateCommand(); calculation.Transaction = transaction;
                calculation.CommandText = "INSERT INTO workout_calculations(workout_id,active_seconds,input_json,energy_json) VALUES($id,$active,$input,$energy)";
                calculation.Parameters.AddWithValue("$id", entry.Id.ToString());
                calculation.Parameters.AddWithValue("$active", (object?)entry.ActiveSeconds ?? DBNull.Value);
                calculation.Parameters.AddWithValue("$input", entry.Calculation is null ? DBNull.Value : JsonSerializer.Serialize(entry.Calculation));
                calculation.Parameters.AddWithValue("$energy", entry.Energy is null ? DBNull.Value : JsonSerializer.Serialize(entry.Energy));
                calculation.ExecuteNonQuery();
            }
        }
        command.CommandText = "DELETE FROM reviews WHERE date=$date";
        command.ExecuteNonQuery();
        if (review is not null)
        {
            using var insert = connection.CreateCommand(); insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO reviews(date,good,difficult,next_action) VALUES($date,$good,$difficult,$next)";
            insert.Parameters.AddWithValue("$date", Key(snapshot.Date));
            insert.Parameters.AddWithValue("$good", review.Good); insert.Parameters.AddWithValue("$difficult", review.Difficult);
            insert.Parameters.AddWithValue("$next", review.NextAction); insert.ExecuteNonQuery();
        }
        transaction.Commit();
        return snapshot with { Revision = current + 1, Review = review };
    }

    public IReadOnlyDictionary<DateOnly, DayCount> Counts(DateOnly from, DateOnly to)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT date,SUM(plans),SUM(done),SUM(workouts),SUM(seconds),SUM(timed) FROM (
                SELECT date,COUNT(*) AS plans,SUM(done) AS done,0 AS workouts,0 AS seconds,0 AS timed
                FROM plans WHERE date >= $from AND date <= $to GROUP BY date
                UNION ALL
                SELECT date,0,0,COUNT(*),COALESCE(SUM(duration_seconds),0),COUNT(duration_seconds)
                FROM workouts WHERE date >= $from AND date <= $to GROUP BY date
            ) GROUP BY date
            """;
        command.Parameters.AddWithValue("$from", Key(from));
        command.Parameters.AddWithValue("$to", Key(to));
        using var reader = command.ExecuteReader();
        var counts = new Dictionary<DateOnly, DayCount>();
        while (reader.Read()) counts[DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture)] = new(reader.GetInt32(1), reader.GetInt32(2)) { Workouts = reader.GetInt32(3), WorkoutSeconds = reader.GetInt64(4), TimedWorkouts = reader.GetInt32(5) };
        return counts;
    }

    public int CountReviewDays(DateOnly from, DateOnly to)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM reviews WHERE date >= $from AND date <= $to";
        command.Parameters.AddWithValue("$from", Key(from)); command.Parameters.AddWithValue("$to", Key(to));
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
    private static string Key(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
