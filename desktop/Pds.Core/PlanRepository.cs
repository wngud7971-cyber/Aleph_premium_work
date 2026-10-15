using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pds.Core;

public sealed class PlanRepository
{
    private readonly string connectionString;
    public string DatabasePath { get; }

    public PlanRepository(string path)
    {
        DatabasePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString();
        using var connection = Open();
        using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        var schema = Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (schema > 1) throw new InvalidDataException("이 앱보다 새로운 형식의 기록이에요. 최신 앱으로 열어 주세요.");
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
            PRAGMA user_version = 1;
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
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM days WHERE date=$date";
        command.Parameters.AddWithValue("$date", Key(date));
        var revision = Convert.ToInt64(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
        command.CommandText = "SELECT id,title,note,done,start_time,end_time FROM plans WHERE date=$date ORDER BY position";
        var entries = new List<PlanEntry>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                entries.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        transaction.Commit();
        return new(date, revision, entries);
    }

    public DaySnapshot Save(DaySnapshot snapshot)
    {
        foreach (var plan in snapshot.Plans) plan.Validate();
        if (snapshot.Plans.Select(p => p.Id).Distinct().Count() != snapshot.Plans.Count)
            throw new ArgumentException("같은 계획이 중복되어 있어요.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM days WHERE date=$date";
        command.Parameters.AddWithValue("$date", Key(snapshot.Date));
        var current = Convert.ToInt64(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
        if (current != snapshot.Revision) throw new SaveConflictException();
        command.CommandText = "INSERT INTO days(date,revision) VALUES($date,$revision) ON CONFLICT(date) DO UPDATE SET revision=$revision";
        command.Parameters.AddWithValue("$revision", current + 1);
        command.ExecuteNonQuery();
        command.CommandText = "DELETE FROM plans WHERE date=$date";
        command.ExecuteNonQuery();
        for (var i = 0; i < snapshot.Plans.Count; i++)
        {
            var entry = snapshot.Plans[i];
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
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
        }
        transaction.Commit();
        return snapshot with { Revision = current + 1 };
    }

    public IReadOnlyDictionary<DateOnly, DayCount> Counts(DateOnly from, DateOnly to)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT date,COUNT(*),SUM(done) FROM plans WHERE date >= $from AND date <= $to GROUP BY date";
        command.Parameters.AddWithValue("$from", Key(from));
        command.Parameters.AddWithValue("$to", Key(to));
        using var reader = command.ExecuteReader();
        var counts = new Dictionary<DateOnly, DayCount>();
        while (reader.Read()) counts[DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture)] = new(reader.GetInt32(1), reader.GetInt32(2));
        return counts;
    }

    private static string Key(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
