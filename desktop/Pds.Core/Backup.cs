using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Pds.Core;

public sealed record PdsBackup(string Format, int Version, DateTimeOffset ExportedUtc, IReadOnlyList<DaySnapshot> Days,
    IReadOnlyList<PlanOrigin> Origins, IReadOnlyList<PlanMove> Moves, IReadOnlyList<BodyWeight> Weights, string? PreferencesJson)
{
    public IReadOnlyList<Anniversary> Anniversaries { get; init; } = [];
    public int Plans => Days.Sum(d => d.Plans.Count);
    public int Workouts => Days.Sum(d => d.Workouts.Count);
    public int Reviews => Days.Count(d => d.Review is not null);
}
public static class BackupFile
{
    public const int MaxBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32 };
    public static PdsBackup Parse(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("백업 파일이 32MB를 초과해요.");
        try
        {
            var backup = JsonSerializer.Deserialize<PdsBackup>(json, Options) ?? throw new InvalidDataException("백업이 비어 있어요.");
            Validate(backup); return backup;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException or OverflowException)
        { throw new InvalidDataException("백업 형식이나 기록 값이 올바르지 않아요. 기존 기록은 유지돼요.", ex); }
    }
    public static string Serialize(PdsBackup backup) { Validate(backup); return JsonSerializer.Serialize(backup, Options); }
    public static void Write(string path, PdsBackup backup)
    {
        var json = Serialize(backup);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("백업이 32MB를 초과해요.");
        var full = Path.GetFullPath(path); var temp = Path.Combine(Path.GetDirectoryName(full)!, ".pds-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(temp, json, new System.Text.UTF8Encoding(false)); File.Move(temp, full, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static PdsBackup Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new InvalidDataException("백업 파일이 32MB를 초과해요.");
        using var reader = new StreamReader(stream); return Parse(reader.ReadToEnd());
    }
    public static void Validate(PdsBackup backup)
    {
        if (backup.Format != "PDS-manual-backup" || backup.Version is not (1 or 2 or 3)) throw new InvalidDataException("지원하지 않는 백업 버전이에요.");
        if (backup.Days is null || backup.Origins is null || backup.Moves is null || backup.Weights is null || backup.Days.Count > 36600)
            throw new InvalidDataException("백업 목록이 올바르지 않아요.");
        if (backup.Days.Select(d => d.Date).Distinct().Count() != backup.Days.Count) throw new InvalidDataException("중복된 날짜가 있어요.");
        if (backup.Anniversaries is null || backup.Anniversaries.Count > 10000 || backup.Version == 1 && backup.Anniversaries.Count > 0
            || backup.Anniversaries.Select(a => a.Id).Distinct().Count() != backup.Anniversaries.Count) throw new InvalidDataException("기념일 목록이 올바르지 않아요.");
        foreach (var anniversary in backup.Anniversaries)
        { anniversary.Validate(); if (backup.Version < 3 && anniversary.Origin is not null) throw new InvalidDataException("원본 생년월일은 백업 v3에서 지원해요."); }
        var ids = new HashSet<Guid>(); var workoutIds = new HashSet<Guid>();
        foreach (var day in backup.Days)
        {
            if (day.Revision < 0 || day.Revision == long.MaxValue || day.Plans is null || day.Workouts is null) throw new InvalidDataException("기록 버전이 올바르지 않아요.");
            day.Review?.Validate();
            if (day.Review is not null && day.Review.Normalize() != day.Review) throw new InvalidDataException("회고 형식이 올바르지 않아요.");
            foreach (var plan in day.Plans) { plan.Validate(); if (!ids.Add(plan.Id)) throw new InvalidDataException("계획 ID가 중복되어 있어요."); }
            foreach (var workout in day.Workouts)
            {
                workout.Validate(); if (!workoutIds.Add(workout.Id)) throw new InvalidDataException("운동 ID가 중복되어 있어요.");
                if (workout.Energy is EnergySnapshot energy)
                {
                    var preset = EnergyCatalog.Resolve(workout.ExerciseId, workout.Calculation!);
                    if (preset is null || preset.Code != energy.Code || preset.Met != energy.Met || preset.Label != energy.Label || preset.SourceUrl != energy.SourceUrl || energy.SourceVersion != EnergyCatalog.Version)
                        throw new InvalidDataException("지원하지 않는 kcal 기준이 들어 있어요.");
                }
            }
        }
        if (backup.Origins.Count != ids.Count || !backup.Origins.Select(o => o.PlanId).ToHashSet().SetEquals(ids)) throw new InvalidDataException("계획의 최초 날짜 정보가 맞지 않아요.");
        if (backup.Moves.Select(m => (m.PlanId, m.CutoffDate)).Distinct().Count() != backup.Moves.Count) throw new InvalidDataException("이월 내역이 중복되어 있어요.");
        foreach (var move in backup.Moves)
        {
            if (!ids.Contains(move.PlanId) || move.To.DayNumber - move.From.DayNumber < move.MissedCutoffs || move.MissedCutoffs < 1 ||
                move.To.DayNumber - move.CutoffDate.DayNumber != 1 || move.Reason is not ("23:00" or "catch-up")) throw new InvalidDataException("이월 내역이 올바르지 않아요.");
            PlanEntry.Create("시간 확인", start: move.PreviousStart, end: move.PreviousEnd).Validate();
        }
        if (backup.Weights.Any(w => w.Id <= 0) || backup.Weights.Select(w => w.Id).Distinct().Count() != backup.Weights.Count) throw new InvalidDataException("체중 이력이 올바르지 않아요.");
        foreach (var weight in backup.Weights) BodyWeight.Validate(weight.Kg);
        if (backup.PreferencesJson is not null)
        {
            using var json = JsonDocument.Parse(backup.PreferencesJson);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("설정 형식이 올바르지 않아요.");
        }
    }
}

public sealed partial class PlanRepository
{
    public string? LoadPreferencesJson()
    { using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT json FROM preferences WHERE id=1"; return command.ExecuteScalar() as string; }
    public void SavePreferencesJson(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("설정 형식이 올바르지 않아요.");
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO preferences(id,json) VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=$json";
        command.Parameters.AddWithValue("$json", json); command.ExecuteNonQuery();
    }
    public PdsBackup CreateBackup(string? preferencesOverride = null)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT date FROM days ORDER BY date";
        var dates = new List<DateOnly>(); using (var reader = command.ExecuteReader()) while (reader.Read()) dates.Add(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture));
        var days = dates.Select(d => ReadDay(connection, transaction, d)).ToArray();
        var origins = ReadOrigins(connection, transaction); var moves = ReadMoves(connection, transaction);
        command.CommandText = "SELECT id,kg,entered_on FROM body_weights ORDER BY id";
        var weights = new List<BodyWeight>(); using (var reader = command.ExecuteReader()) while (reader.Read()) weights.Add(new(reader.GetInt64(0), decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture), DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        command.CommandText = "SELECT json FROM preferences WHERE id=1"; var preferences = preferencesOverride ?? command.ExecuteScalar() as string;
        var anniversaries = ReadAnniversaries(connection, transaction);
        transaction.Commit(); return new("PDS-manual-backup", 3, clock.GetUtcNow(), days, origins, moves, weights, preferences) { Anniversaries = anniversaries };
    }
    public void RestoreBackup(PdsBackup input)
    {
        // Freeze and validate a detached copy before touching the live database.
        var backup = BackupFile.Parse(BackupFile.Serialize(input));
        var origins = backup.Origins.ToDictionary(o => o.PlanId);
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var previousAnniversaries = ReadAnniversaries(connection, transaction).ToDictionary(a => a.Id, a => a.Revision);
        void Write(string sql, params (string Name, object? Value)[] parameters)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        Write("DELETE FROM anniversaries; DELETE FROM plans; DELETE FROM workouts; DELETE FROM reviews; DELETE FROM body_weights; DELETE FROM preferences; UPDATE days SET revision=revision+1;");
        foreach (var day in backup.Days)
        {
            Write("INSERT INTO days(date,revision) VALUES($date,$revision) ON CONFLICT(date) DO UPDATE SET revision=MAX(days.revision,$revision)", ("$date", Key(day.Date)), ("$revision", day.Revision + 1));
            for (var i = 0; i < day.Plans.Count; i++)
            {
                var p = day.Plans[i];
                Write("INSERT INTO plans(id,date,position,title,note,done,start_time,end_time) VALUES($id,$date,$pos,$title,$note,$done,$start,$end)",
                    ("$id", p.Id.ToString()), ("$date", Key(day.Date)), ("$pos", i), ("$title", p.Title), ("$note", p.Note), ("$done", p.Done), ("$start", p.Start), ("$end", p.End));
                var origin = origins[p.Id];
                Write("INSERT INTO plan_metadata(plan_id,original_date,created_utc,actual_date) VALUES($id,$original,$created,$actual)",
                    ("$id", p.Id.ToString()), ("$original", Key(origin.OriginalDate)), ("$created", origin.CreatedUtc?.ToString("O", CultureInfo.InvariantCulture)), ("$actual", p.ActualDate is DateOnly actual ? Key(actual) : null));
            }
            for (var i = 0; i < day.Workouts.Count; i++)
            {
                var w = day.Workouts[i];
                Write("INSERT INTO workouts(id,date,position,exercise_id,duration_seconds,quantity,note) VALUES($id,$date,$pos,$exercise,$seconds,$quantity,$note)",
                    ("$id", w.Id.ToString()), ("$date", Key(day.Date)), ("$pos", i), ("$exercise", w.ExerciseId), ("$seconds", w.DurationSeconds), ("$quantity", w.Quantity), ("$note", w.Note));
                if (w.ActiveSeconds is not null || w.Calculation is not null || w.Energy is not null)
                    Write("INSERT INTO workout_calculations(workout_id,active_seconds,input_json,energy_json) VALUES($id,$active,$input,$energy)", ("$id", w.Id.ToString()), ("$active", w.ActiveSeconds),
                        ("$input", w.Calculation is null ? null : JsonSerializer.Serialize(w.Calculation)), ("$energy", w.Energy is null ? null : JsonSerializer.Serialize(w.Energy)));
            }
            if (day.Review is DayReview review) Write("INSERT INTO reviews(date,good,difficult,next_action) VALUES($date,$good,$difficult,$next)",
                ("$date", Key(day.Date)), ("$good", review.Good), ("$difficult", review.Difficult), ("$next", review.NextAction));
        }
        foreach (var m in backup.Moves) Write("INSERT INTO plan_rollovers(plan_id,from_date,to_date,cutoff_date,processed_utc,previous_start,previous_end,reason,missed_cutoffs) VALUES($id,$from,$to,$cutoff,$processed,$start,$end,$reason,$missed)",
            ("$id", m.PlanId.ToString()), ("$from", Key(m.From)), ("$to", Key(m.To)), ("$cutoff", Key(m.CutoffDate)), ("$processed", m.ProcessedUtc.ToString("O", CultureInfo.InvariantCulture)),
            ("$start", m.PreviousStart), ("$end", m.PreviousEnd), ("$reason", m.Reason), ("$missed", m.MissedCutoffs));
        foreach (var w in backup.Weights) Write("INSERT INTO body_weights(id,kg,entered_on) VALUES($id,$kg,$date)", ("$id", w.Id), ("$kg", w.Kg.ToString(CultureInfo.InvariantCulture)), ("$date", Key(w.EnteredOn)));
        foreach (var original in backup.Anniversaries)
        {
            var a = original with { Revision = Math.Max(original.Revision, previousAnniversaries.GetValueOrDefault(original.Id)) + 1 };
            Write("INSERT INTO anniversaries(id,revision,name,json) VALUES($id,$revision,$name,$json)",
                ("$id", a.Id.ToString()), ("$revision", a.Revision), ("$name", a.Name), ("$json", JsonSerializer.Serialize(a)));
        }
        if (backup.PreferencesJson is not null) Write("INSERT INTO preferences(id,json) VALUES(1,$json)", ("$json", backup.PreferencesJson));
        transaction.Commit();
    }
}
