using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pds.Core;

static class JournalChecks
{
    public static void Run(string directory, Action<bool, string> check)
    {
        void Expect<T>(Action action, string label) where T : Exception
        { try { action(); } catch (T) { check(true, label); return; } throw new InvalidOperationException(label); }
        var path = Path.Combine(directory, "journal.db"); var repository = new PlanRepository(path,new JournalClock());
        var day = new DateOnly(2026, 10, 18); var next = day.AddDays(1);
        var empty = repository.Load(day);
        check(empty.Money.Count == 0 && empty.Studies.Count == 0, "No fabricated finance or study records");
        check(MoneyEntry.ParseWon(" 12,000 ") == 12000 && MoneyEntry.ParseWon("1") == 1 && MoneyEntry.ParseWon("999,999,999,999") == MoneyEntry.MaxWon, "Won integer and properly grouped boundaries parse exactly");
        foreach (var invalid in new[] { "", "0", "-1", "1.5", "12,34", "1e5", "₩100", "1,000,000,000,000" })
            Expect<ArgumentException>(() => MoneyEntry.ParseWon(invalid), "Reject invalid won: " + invalid);
        var meal = MoneyEntry.Create(MoneyKind.Expense, 12000, "식비·카페", "점심 ' ; DROP TABLE days; --");
        var bus = MoneyEntry.Create(MoneyKind.Expense, 1500, "교통");
        var salary = MoneyEntry.Create(MoneyKind.Income, 100000, "급여");
        var study = StudyEntry.Create("  C# 복습  ", "첫 줄\n둘째 줄 ' ; DROP TABLE study_entries; --", "자료형을 이해했다.");
        var saved = repository.Save(empty with { Money = [meal, bus, salary], Studies = [study] });
        var reopened = new PlanRepository(path).Load(day);
        check(reopened.Money.SequenceEqual(saved.Money) && reopened.Studies.SequenceEqual(saved.Studies), "Money, Korean multiline study and IDs survive database reopen / parameterized SQL");
        var week = repository.JournalStatistics(DateRange.Week(day));
        check(week.Income == 100000 && week.Expense == 13500 && week.Balance == 86500 && week.MoneyRecords == 3 && week.Studies == 1, "Weekly income, expense, difference and study counts are exact integers");
        check(week.Categories.Count == 2 && week.Categories[0] == new MoneyCategoryTotal("식비·카페",12000,1) && week.Categories.Sum(c => c.Won) == week.Expense, "Categories contain expenses only, descending amounts and reconcile to total");
        var nextSaved = repository.Save(repository.Load(next) with { Money = [MoneyEntry.Create(MoneyKind.Expense, 20000, "교육")], Studies = [StudyEntry.Create("제목만")] });
        check(repository.JournalStatistics(DateRange.Week(next)).Expense == 20000 && repository.JournalStatistics(DateRange.Week(day)).Expense == 13500, "Monday begins a new week; previous Sunday stays in previous week");
        check(repository.JournalStatistics(DateRange.Month(day)).Expense == 33500 && repository.JournalStatistics(new(day,day)).Expense == 13500, "Monthly and inclusive single-day boundaries");
        var edit = meal with { Won = 9000, Note = "수정" };
        var edited = repository.Save(saved with { Money = [edit,bus,salary], Studies = [study with { Summary = "수정 요약" }] });
        check(edited.Money.Count == 3 && repository.Load(day).Money[0].Id == meal.Id && repository.JournalStatistics(new(day,day)).Expense == 10500, "Editing preserves ID without duplicate amount in statistics");
        Expect<SaveConflictException>(() => repository.Save(saved with { Money = [] }), "Stale journal cannot overwrite newer revisions");
        Expect<ArgumentException>(() => repository.Save(edited with { Money = [edit with { Won = 0 }], Studies = [] }), "Invalid amount rejects whole atomic day save");
        Expect<ArgumentException>(() => repository.Save(edited with { Studies = [study with { Title = " " }] }), "Summary or body without study title rejected");
        Expect<ArgumentException>(() => repository.Save(edited with { Money = [edit with { Kind = MoneyKind.Income }] }), "Expense category cannot be mislabeled as income");
        Expect<ArgumentException>(() => repository.Save(edited with { Money = [edit,edit] }), "Duplicate finance IDs rejected before mutation");
        check(repository.Load(day).Revision == edited.Revision && repository.Load(day).Studies[0].Summary == "수정 요약", "All rejected journal inputs preserve existing records and revision");
        Expect<SqliteException>(() => repository.Save(nextSaved with { Money = [edit], Studies = [] }), "Cross-date money ID collision rolls back other records");
        check(repository.Load(next).Revision == nextSaved.Revision && repository.Load(next).Studies.Count == 1, "Late SQL failure preserves the target day's study and revision");
        var plan = PlanEntry.Create("23시 이월할 계획");
        edited = repository.Save(edited with { Plans = [plan] });
        repository.Rollover(new DateTimeOffset(2026,10,18,23,59,0,TimeSpan.FromHours(9)));
        var afterRollover = repository.Load(day);
        check(afterRollover.Plans.Count == 0 && afterRollover.Money.Count == 3 && afterRollover.Studies.Count == 1, "23:59 plan rollover never moves finance or study records");
        afterRollover = repository.Save(afterRollover with { Studies = afterRollover.Studies.Append(StudyEntry.Create("늦게 작성")).ToArray() });
        check(repository.JournalStatistics(new(day,day)).Studies == 2 && repository.Load(next).Studies.Count == 1, "Late study saves remain on chosen actual date");
        var backup = repository.CreateBackup(); var json = BackupFile.Serialize(backup); var parsed = BackupFile.Parse(json);
        check(backup.Version == 8 && parsed.MoneyRecords == 4 && parsed.Studies == 3 && repository.Load(day).Money.Count == 3, "Backup v4 exports both new record types without deleting originals");
        var target = new PlanRepository(Path.Combine(directory,"journal-restore.db")); target.RestoreBackup(parsed);
        check(target.Load(day).Money.SequenceEqual(afterRollover.Money) && target.Load(day).Studies.SequenceEqual(afterRollover.Studies), "Backup restore round-trips finance and learning text exactly");
        var invalidBackup = parsed with { Days = parsed.Days.Select(d => d.Date == day ? d with { Money = [edit with { Won = -1 }] } : d).ToArray() };
        Expect<ArgumentException>(() => target.RestoreBackup(invalidBackup), "Malformed journal backup rejected before deleting target records");
        check(target.Load(day).Money.Count == 3, "Failed restore retains target journal");
        var legacy = new PlanRepository(Path.Combine(directory,"legacy-journal.db"));
        legacy.Save(legacy.Load(day) with { Plans = [PlanEntry.Create("기존 계획")], Review = new("기존 회고","","") });
        var oldBackup = legacy.CreateBackup() with { Version = 3 };
        var oldNode = JsonNode.Parse(BackupFile.Serialize(oldBackup))!.AsObject();
        foreach (var node in oldNode["Days"]!.AsArray()) { node!.AsObject().Remove("Money"); node.AsObject().Remove("Studies"); }
        oldNode.Remove("MoneyRecords"); oldNode.Remove("Studies");
        foreach (var version in new[] { 1,2,3 })
        {
            oldNode["Version"] = version; target.RestoreBackup(BackupFile.Parse(oldNode.ToJsonString()));
            check(target.Load(day).Plans.Single().Title == "기존 계획" && target.Load(day).Review?.Good == "기존 회고" && target.Load(day).Money.Count == 0 && target.Load(day).Studies.Count == 0,
                "Legacy backup v" + version + " restores old data; full replacement clears new record types");
        }
        using (var c = new SqliteConnection($"Data Source={legacy.DatabasePath};Pooling=False"))
        { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "DROP TABLE money_entries; DROP TABLE study_entries; PRAGMA user_version=7"; cmd.ExecuteNonQuery(); }
        var migrated = new PlanRepository(legacy.DatabasePath).Load(day);
        check(migrated.Plans.Count == 1 && migrated.Review?.Good == "기존 회고" && migrated.Money.Count == 0 && migrated.Studies.Count == 0, "Schema 7 migration preserves previous records and initializes empty journal");
        var deleted = repository.Save(afterRollover with { Money = [], Studies = [] });
        check(repository.JournalStatistics(new(day,day)).MoneyRecords == 0 && repository.JournalStatistics(new(day,day)).Studies == 0 && repository.Load(next).Money.Count == 1, "Deleting journal updates totals and leaves other dates intact");
    }
    private sealed class JournalClock : TimeProvider
    {public override DateTimeOffset GetUtcNow()=>new(2026,10,18,12,0,0,TimeSpan.FromHours(9));}
}
