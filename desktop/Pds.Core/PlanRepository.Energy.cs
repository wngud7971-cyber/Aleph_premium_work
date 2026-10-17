using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Pds.Core;

public sealed record EnergyTotals(int Workouts, int Calculated, decimal? Kcal);
public sealed partial class PlanRepository
{
    private static BodyWeight? ReadCurrentWeight(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT id,kg,entered_on FROM body_weights ORDER BY id DESC LIMIT 1";
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetInt64(0), decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
            DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture)) : null;
    }
    public BodyWeight? LoadBodyWeight() { using var connection = Open(); return ReadCurrentWeight(connection); }
    public BodyWeight SaveBodyWeight(decimal kg, DateOnly enteredOn)
    {
        BodyWeight.Validate(kg);
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO body_weights(kg,entered_on) VALUES($kg,$date); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$kg", kg.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$date", Key(enteredOn));
        var id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        transaction.Commit(); return new(id, kg, enteredOn);
    }
    private static void VerifyFreshCalculations(SqliteConnection connection, SqliteTransaction transaction, DaySnapshot day)
    {
        foreach (var workout in day.Workouts.Where(w => w.Energy is not null))
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT c.energy_json FROM workout_calculations c JOIN workouts w ON w.id=c.workout_id WHERE w.id=$id AND w.date=$date";
            command.Parameters.AddWithValue("$id", workout.Id.ToString()); command.Parameters.AddWithValue("$date", Key(day.Date));
            var previous = command.ExecuteScalar() as string;
            if (previous is not null && JsonSerializer.Deserialize<EnergySnapshot>(previous) == workout.Energy) continue;
            var weight = ReadCurrentWeight(connection, transaction);
            if (weight is null || !weight.IsCurrent(DateOnly.FromDateTime(DateTime.Today)))
                throw new ArgumentException("체중을 다시 입력한 뒤 kcal을 계산해 주세요. 운동 기록만 저장할 수도 있어요.");
        }
    }
    public EnergyTotals LoadEnergyTotals(DateOnly from, DateOnly to)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT c.energy_json FROM workouts w LEFT JOIN workout_calculations c ON c.workout_id=w.id WHERE w.date >= $from AND w.date <= $to";
        command.Parameters.AddWithValue("$from", Key(from)); command.Parameters.AddWithValue("$to", Key(to));
        var count = 0; var calculated = 0; decimal total = 0;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            count++;
            if (reader.IsDBNull(0)) continue;
            var energy = JsonSerializer.Deserialize<EnergySnapshot>(reader.GetString(0));
            if (energy is null) continue;
            calculated++; total += energy.Kcal;
        }
        return new(count, calculated, calculated == 0 ? null : total);
    }
}
