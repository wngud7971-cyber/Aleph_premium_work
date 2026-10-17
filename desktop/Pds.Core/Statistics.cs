using System.Globalization;

namespace Pds.Core;

public sealed record DateRange(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
    public bool Contains(DateOnly date) => date >= From && date <= To;
    public void Validate() { if (From > To || Days > 36600) throw new ArgumentException("조회 기간은 시작일부터 종료일까지 100년 이내로 선택해 주세요."); }
    public static DateRange Week(DateOnly anchor) { var start = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7)); return new(start, start.AddDays(6)); }
    public static DateRange Month(DateOnly anchor) => new(new(anchor.Year, anchor.Month, 1), new(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month)));
    public DateRange? PreviousComparable(DateOnly today, bool monthly)
    {
        if (From > today) return null;
        var previousFrom = monthly ? From.AddMonths(-1) : From.AddDays(-7);
        var previousEnd = monthly ? new DateOnly(previousFrom.Year, previousFrom.Month, DateTime.DaysInMonth(previousFrom.Year, previousFrom.Month)) : previousFrom.AddDays(6);
        if (today <= To) previousEnd = monthly ? previousFrom.AddDays(Math.Min(today.Day - 1, DateTime.DaysInMonth(previousFrom.Year, previousFrom.Month) - 1)) : previousFrom.AddDays(today.DayNumber - From.DayNumber);
        return new(previousFrom, previousEnd);
    }
}
public sealed record WorkoutFilter(string? Category = null, string? ExerciseId = null)
{
    public bool Includes(WorkoutEntry entry) => (Category is null || ExerciseCatalog.Get(entry.ExerciseId).Category == Category) && (ExerciseId is null || entry.ExerciseId == ExerciseId);
}
public sealed record WorkoutStatistics(int Records, int Days, int TimedRecords, long Seconds, int Calculated, decimal? Kcal)
{
    public decimal? AverageSeconds => Days == 0 ? null : (decimal)Seconds / Days;
}
public sealed record StatisticsRow(string Label, int DateOccurrences, int Plans, int Done, int ActualDone, WorkoutStatistics Workout, int ReviewDays);
public sealed record StatisticsReport(DateRange Range, int Plans, int Done, int UnknownActualDates, int ReviewDays, WorkoutStatistics Workout,
    IReadOnlyList<StatisticsRow> Days, IReadOnlyList<StatisticsRow> Weekdays, IReadOnlyList<StatisticsRow> Exercises)
{
    public IReadOnlyList<StatisticsRow> Categories { get; init; } = [];
    public decimal? CompletionRate => Plans == 0 ? null : (decimal)Done / Plans * 100;
    public static StatisticsReport Create(IReadOnlyList<DaySnapshot> source, DateRange range, WorkoutFilter? filter = null)
    {
        range.Validate(); filter ??= new();
        var planned = source.Where(d => range.Contains(d.Date)).ToArray();
        var workouts = planned.SelectMany(d => d.Workouts.Where(filter.Includes).Select(w => (d.Date, Entry: w))).ToArray();
        var plannedByDate = planned.ToDictionary(d => d.Date);
        var workoutsByDate = workouts.ToLookup(w => w.Date);
        var completedByDate = source.SelectMany(d => d.Plans).Where(p => p.Done && p.ActualDate is not null)
            .GroupBy(p => p.ActualDate!.Value).ToDictionary(g => g.Key, g => g.Count());
        WorkoutStatistics Sum(IEnumerable<(DateOnly Date, WorkoutEntry Entry)> entries)
        {
            var items = entries.ToArray(); var calculated = items.Where(w => w.Entry.Energy is not null).ToArray();
            return new(items.Length, items.Select(w => w.Date).Distinct().Count(), items.Count(w => w.Entry.DurationSeconds is not null),
                items.Sum(w => (long)(w.Entry.DurationSeconds ?? 0)), calculated.Length, calculated.Length == 0 ? null : calculated.Sum(w => w.Entry.Energy!.Kcal));
        }
        StatisticsRow Row(string label, IReadOnlySet<DateOnly> dates)
        {
            var days = dates.Where(plannedByDate.ContainsKey).Select(d => plannedByDate[d]).ToArray();
            return new(label, dates.Count, days.Sum(d => d.Plans.Count), days.Sum(d => d.Plans.Count(p => p.Done)),
                dates.Sum(d => completedByDate.GetValueOrDefault(d)),
                Sum(dates.SelectMany(d => workoutsByDate[d])), days.Count(d => d.Review is not null));
        }
        var dates = Enumerable.Range(0, range.Days).Select(i => range.From.AddDays(i)).ToArray();
        var daily = dates.Select(d => Row(d.ToString("MM/dd", CultureInfo.InvariantCulture), new HashSet<DateOnly> { d })).ToArray();
        var names = new[] { "월", "화", "수", "목", "금", "토", "일" };
        var weekday = Enumerable.Range(0, 7).Select(i => Row(names[i], dates.Where(d => ((int)d.DayOfWeek + 6) % 7 == i).ToHashSet())).ToArray();
        var exercises = ExerciseCatalog.All.Where(e => workouts.Any(w => w.Entry.ExerciseId == e.Id))
            .Select(e => new StatisticsRow(e.Name, 0, 0, 0, 0, Sum(workouts.Where(w => w.Entry.ExerciseId == e.Id)), 0)).ToArray();
        return new(range, planned.Sum(d => d.Plans.Count), planned.Sum(d => d.Plans.Count(p => p.Done)),
            planned.Sum(d => d.Plans.Count(p => p.Done && p.ActualDate is null)), planned.Count(d => d.Review is not null), Sum(workouts), daily, weekday, exercises)
        { Categories = ExerciseCatalog.Categories.Where(c => workouts.Any(w => ExerciseCatalog.Get(w.Entry.ExerciseId).Category == c))
            .Select(c => new StatisticsRow(c, 0, 0, 0, 0, Sum(workouts.Where(w => ExerciseCatalog.Get(w.Entry.ExerciseId).Category == c)), 0)).ToArray() };
    }
}

public sealed partial class PlanRepository
{
    public StatisticsReport Statistics(DateRange range, WorkoutFilter? filter = null)
    {
        range.Validate(); using var connection = Open(); using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT date FROM days WHERE date >= $from AND date <= $to UNION SELECT p.date FROM plans p JOIN plan_metadata m ON m.plan_id=p.id WHERE m.actual_date >= $from AND m.actual_date <= $to ORDER BY date";
        command.Parameters.AddWithValue("$from", Key(range.From)); command.Parameters.AddWithValue("$to", Key(range.To));
        var dates = new List<DateOnly>(); using (var reader = command.ExecuteReader()) while (reader.Read()) dates.Add(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture));
        var days = dates.Select(d => ReadDay(connection, transaction, d)).ToArray(); transaction.Commit();
        return StatisticsReport.Create(days, range, filter);
    }
}
