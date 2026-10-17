using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pds.Core;

static class AnniversaryChecks
{
    public static void Run(string directory, Action<bool, string> check)
    {
        void Expect<T>(Action action, string label) where T : Exception
        { try { action(); } catch (T) { check(true, label); return; } throw new InvalidOperationException(label); }
        var solar = Anniversary.Create("친구", AnniversaryKind.Birthday, AnniversaryCalendar.Solar, 10, 17);
        var lunar = Anniversary.Create("가족", AnniversaryKind.Memorial, AnniversaryCalendar.Lunar, 1, 1);
        check(AnniversaryDates.Between([solar], new(2026, 1, 1), new(2029, 12, 31)).Count == 4, "Annual solar dates recur exactly once per year without stored day duplicates");
        check(AnniversaryDates.InYear(lunar, 2026)?.Date == new DateOnly(2026, 2, 17), "Lunar New Year 2026 matches KASI calendar data");
        // KASI's 2025 month-start table, including leap six and month twelve in the next solar year.
        var months2025 = new[] { (1,1,29), (2,2,28), (3,3,29), (4,4,28), (5,5,27), (6,6,25), (7,8,23), (8,9,22), (9,10,21), (10,11,20), (11,12,20), (12,1,19) };
        check(months2025.All(x => AnniversaryDates.InYear(lunar with { Month = x.Item1 }, 2025)?.Date == new DateOnly(x.Item1 == 12 ? 2026 : 2025, x.Item2, x.Item3)), "Twelve regular lunar month starts match independent KASI 2025 fixtures");
        var leap = lunar with { Month = 6, LeapMonth = true };
        check(AnniversaryDates.InYear(leap, 2025)?.Date == new DateOnly(2025, 7, 25), "Leap lunar six maps to July 25 rather than regular June 25");
        check(AnniversaryDates.InYear(leap, 2026) is null, "User-selected missing-leap policy skips the year");
        check(AnniversaryDates.InYear(leap with { LeapPolicy = MissingLeapMonth.RegularMonth }, 2026) is { Date: var regular, Adjusted: true } && regular == new DateOnly(2026, 7, 14), "Optional explicit regular-month fallback preserves original leap rule");
        var feb = solar with { Month = 2, Day = 29 };
        check(AnniversaryDates.InYear(feb, 2028)?.Date == new DateOnly(2028, 2, 29) && AnniversaryDates.InYear(feb, 2027) is { Date: var febLast, Adjusted: true } && febLast == new DateOnly(2027, 2, 28), "February 29 uses selected last-day fallback and returns to actual leap day");
        check(AnniversaryDates.InYear(feb with { DayPolicy = MissingAnniversaryDay.SkipYear }, 2027) is null, "February 29 can explicitly skip a non-leap year");
        var thirty = lunar with { Month = 2, Day = 30 };
        check(AnniversaryDates.InYear(thirty, 2026) is { Date: var last, Adjusted: true } && last == new DateOnly(2026, 4, 16), "Lunar thirty in a short month uses its final day without drifting into next month");
        check(AnniversaryDates.InYear(thirty with { DayPolicy = MissingAnniversaryDay.SkipYear }, 2026) is null, "Short lunar month supports explicit skip policy");
        check(AnniversaryDates.Between([lunar with { Month = 12 }], new(2026, 1, 1), new(2026, 1, 31)).Single().Date == new DateOnly(2026, 1, 19), "January lookup includes previous lunar year month twelve");
        Expect<ArgumentException>(() => (solar with { Month = 4, Day = 31 }).Validate(), "Impossible solar date rejected before saving");
        Expect<ArgumentException>(() => (lunar with { Day = 31 }).Validate(), "Lunar day 31 rejected");
        Expect<ArgumentOutOfRangeException>(() => AnniversaryDates.InYear(lunar, 2051), "Unsupported lunar year is explicit rather than fabricated");
        check(AnniversaryDates.Between([solar, lunar], new(2052, 1, 1), new(2052, 12, 31)).Count == 1, "Outside lunar range solar anniversaries remain available");
        var path = Path.Combine(directory, "anniversaries.db"); var repository = new PlanRepository(path);
        var saved = repository.SaveAnniversary(solar); repository.SaveAnniversary(lunar); repository.SaveAnniversary(leap with { Id = Guid.NewGuid() });
        check(new PlanRepository(path).LoadAnniversaries().Count == 3, "Birthday memorial and leap rules survive process repository reopening");
        var edited = repository.SaveAnniversary(saved with { Name = "수정한 친구", Note = "메모" });
        check(edited.Id == saved.Id && repository.LoadAnniversaries().Count == 3, "Anniversary edit keeps identity and never adds a duplicate");
        Expect<SaveConflictException>(() => repository.SaveAnniversary(saved), "Stale anniversary edit cannot overwrite saved changes");
        var day = new DateOnly(2026, 10, 17); repository.Save(repository.Load(day) with { Plans = [PlanEntry.Create("이월 대상")] });
        repository.Rollover(new DateTimeOffset(2026, 10, 17, 23, 59, 0, TimeSpan.FromHours(9)));
        check(repository.LoadAnniversaries().Count == 3 && repository.Statistics(DateRange.Week(day)).Plans == 1, "Anniversaries stay fixed at 23:59 and never inflate plan statistics");
        var backup = repository.CreateBackup(); var json = BackupFile.Serialize(backup);
        check(backup.Version == 3 && backup.Anniversaries.Count == 3 && repository.LoadAnniversaries().Count == 3, "Backup v3 exports recurring rules and leaves originals intact");
        var restored = new PlanRepository(Path.Combine(directory, "anniversary-restore.db")); restored.RestoreBackup(BackupFile.Parse(json));
        check(restored.LoadAnniversaries().Select(a => a with { Revision = 0 }).SequenceEqual(repository.LoadAnniversaries().Select(a => a with { Revision = 0 })), "Full restore preserves original calendar leap policies IDs and notes");
        var before = restored.LoadAnniversaries().First(); restored.RestoreBackup(BackupFile.Parse(json));
        Expect<SaveConflictException>(() => restored.SaveAnniversary(before), "Restore invalidates older anniversary editor snapshots");
        var oldNode = JsonNode.Parse(json)!.AsObject(); oldNode["Version"] = 1; oldNode.Remove("Anniversaries");
        check(BackupFile.Parse(oldNode.ToJsonString()).Anniversaries.Count == 0, "Existing v1 backup without anniversary field remains readable");
        Expect<InvalidDataException>(() => BackupFile.Validate(backup with { Anniversaries = [edited, edited] }), "Duplicate anniversary identities reject backup before replacement");
        using (var c = new SqliteConnection("Data Source=" + restored.DatabasePath))
        { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "CREATE TRIGGER fail_anniversary_restore BEFORE INSERT ON anniversaries BEGIN SELECT RAISE(ABORT,'test'); END;"; cmd.ExecuteNonQuery(); }
        var preserved = restored.LoadAnniversaries();
        Expect<SqliteException>(() => restored.RestoreBackup(backup), "Late anniversary restore SQL failure is surfaced");
        check(restored.LoadAnniversaries().SequenceEqual(preserved), "Failed anniversary restore atomically preserves previous rules");
        repository.DeleteAnniversary(edited);
        check(repository.LoadAnniversaries().Count == 2, "Confirmed anniversary deletion removes future yearly display rule");
        Expect<SaveConflictException>(() => repository.SaveAnniversary(edited), "Deleted anniversary cannot be recreated by a stale editor");
        var legacy = new PlanRepository(Path.Combine(directory, "anniversary-schema-five.db")); legacy.Save(legacy.Load(day) with { Plans = [PlanEntry.Create("기존 기록")] });
        using (var c = new SqliteConnection("Data Source=" + legacy.DatabasePath))
        { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "DROP TABLE anniversaries; PRAGMA user_version=5;"; cmd.ExecuteNonQuery(); }
        var upgraded = new PlanRepository(legacy.DatabasePath);
        check(upgraded.Load(day).Plans.Single().Title == "기존 기록" && upgraded.LoadAnniversaries().Count == 0, "Schema five upgrades to seven without changing existing day records");
    }
}
