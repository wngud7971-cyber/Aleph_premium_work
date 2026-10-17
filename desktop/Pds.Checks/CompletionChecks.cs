using Microsoft.Data.Sqlite;
using Pds.Core;

static class CompletionChecks
{
    public static void Run(string directory, Action<bool, string> check)
    {
        void Expect<T>(Action action, string label) where T : Exception
        { try { action(); } catch (T) { check(true, label); return; } throw new InvalidOperationException(label); }
        var day = new DateOnly(2026, 10, 12); var today = DateOnly.FromDateTime(DateTime.Today);
        var clock = new WallClock(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.FromHours(9)));
        var repository = new PlanRepository(Path.Combine(directory, "completion.db"), clock);
        var done = PlanEntry.Create("완료") with { Done = true, ActualDate = day.AddDays(1) };
        var legacyDone = PlanEntry.Create("날짜 모름") with { Done = true };
        var incomplete = PlanEntry.Create("이월", "메모", "09:00", "10:00");
        var fourth = PlanEntry.Create("완료2") with { Done = true, ActualDate = day };
        var walk = WorkoutEntry.Create("walking", 1800, null); var cycle = WorkoutEntry.Create("cycling-indoor", 1200, null);
        var saved = repository.Save(repository.Load(day) with { Plans = [done, legacyDone, incomplete, fourth], Workouts = [walk, cycle], Review = new("첫날 회고", "", "") });
        repository.Save(repository.Load(day.AddDays(2)) with { Workouts = [WorkoutEntry.Create("badminton", 2400, null)], Review = new("", "", "다음 행동") });
        var week = DateRange.Week(day); var stats = repository.Statistics(week);
        check(stats.Plans == 4 && stats.Done == 3 && stats.CompletionRate == 75m && stats.ReviewDays == 2, "Weekly reference fixture: four plans, three done, 75 percent, two reviews");
        check(stats.Workout.Records == 3 && stats.Workout.Days == 2 && stats.Workout.Seconds == 5400 && stats.Workout.AverageSeconds == 2700m, "Weekly 30+20 and 40 minutes gives 90 total and 45 per exercise day");
        check(stats.Weekdays[0].ActualDone == 1 && stats.Weekdays[1].ActualDone == 1 && stats.UnknownActualDates == 1, "Weekday completion uses explicit actual dates and discloses unknown legacy dates");
        check(stats.Workout.Kcal is null && stats.Workout.Calculated == 0 && stats.Exercises.Count == 3, "Missing kcal stays missing and exercises remain separate");
        var filtered = repository.Statistics(week, new("사이클"));
        check(filtered.Plans == 4 && filtered.ReviewDays == 2 && filtered.Workout.Records == 1 && filtered.Workout.Seconds == 1200, "Workout category filter leaves plans and reviews unchanged");
        var empty = repository.Statistics(new(day.AddDays(-30), day.AddDays(-25)));
        check(empty.CompletionRate is null && empty.Workout.AverageSeconds is null && empty.Workout.Kcal is null, "Empty statistics do not manufacture rates, averages or kcal");
        check(stats.Days.Sum(d => d.Workout.Seconds) == stats.Workout.Seconds && stats.Weekdays.Sum(d => d.Workout.Records) == stats.Workout.Records, "Chart rows and headline statistics share the same totals");
        var shortRange = repository.Statistics(new(day, day.AddDays(1)));
        check(shortRange.Weekdays[0].DateOccurrences == 1 && shortRange.Weekdays[2].DateOccurrences == 0, "Weekday denominators count actual calendar occurrences");
        check(DateRange.Week(new(2027, 1, 1)) == new DateRange(new(2026, 12, 28), new(2027, 1, 3)), "Weeks start Monday across year boundaries");
        check(DateRange.Month(new(2024, 2, 29)).Days == 29 && DateRange.Month(new(2026, 2, 1)).Days == 28, "Monthly range respects leap February");
        var month = DateRange.Month(new(2026, 3, 30));
        check(month.PreviousComparable(new(2026, 3, 30), true) == new DateRange(new(2026, 2, 1), new(2026, 2, 28)), "Ongoing month comparison clamps to shorter previous month");
        check(week.PreviousComparable(day.AddDays(2), false) == new DateRange(day.AddDays(-7), day.AddDays(-5)) && week.PreviousComparable(day.AddDays(-1), false) is null, "Ongoing weeks compare equal elapsed weekdays; future periods have no comparison");
        Expect<ArgumentException>(() => (done with { Done = false }).Validate(), "Incomplete plans reject actual completion dates");
        Expect<ArgumentException>(() => repository.Statistics(new(day.AddDays(1), day)), "Reversed statistics range rejected");

        var zone = TimeZoneInfo.CreateCustomTimeZone("PdsTestKorea", TimeSpan.FromHours(9), "Korea", "Korea");
        var before = new DateTimeOffset(2026, 10, 12, 22, 59, 59, TimeSpan.FromHours(9));
        check(repository.Rollover(before, zone: zone).Moved == 0, "No rollover before local 23:00");
        var cutoff = before.AddSeconds(1);
        var deferred = repository.Rollover(cutoff, new HashSet<DateOnly> { day.AddDays(1) }, zone);
        check(deferred.Moved == 0 && deferred.Deferred == 1 && repository.Load(day).Revision == saved.Revision, "Dirty destination is protected without changing its source revision");
        var result = repository.Rollover(cutoff, zone: zone); var destination = repository.Load(day.AddDays(1)); var source = repository.Load(day);
        check(result.Moved == 1 && source.Plans.Count == 3 && destination.Plans.Single().Id == incomplete.Id && destination.Plans.Single().Start is null && destination.Plans.Single().End is null, "Rollover moves the same ID once and clears old times");
        check(source.Workouts.SequenceEqual(saved.Workouts) && source.Review == saved.Review && source.Plans.All(p => p.Done), "Rollover preserves completed plans, workouts and review");
        var history = repository.LoadPlanHistory(incomplete.Id);
        check(history.Origin?.OriginalDate == day && history.Moves.Single().PreviousStart == "09:00" && history.Moves.Single().CutoffDate == day, "Rollover retains original date and previous time in history");
        Expect<SaveConflictException>(() => repository.Save(saved), "Old source snapshot cannot undo a rollover");
        check(repository.Rollover(cutoff, zone: zone).Moved == 0 && repository.LoadPlanHistory(incomplete.Id).Moves.Count == 1, "Repeated cutoff is idempotent");
        check(repository.Rollover(before, zone: zone).Moved == 0 && repository.Load(day.AddDays(1)).Plans.Count == 1, "Clock moving backward does not move a plan backward");
        clock.Now = cutoff.AddMinutes(5);
        var late = PlanEntry.Create("23시 뒤 입력"); var future = PlanEntry.Create("미래 계획");
        repository.Save(repository.Load(day) with { Plans = source.Plans.Concat([late]).ToArray() });
        repository.Save(repository.Load(day.AddDays(10)) with { Plans = [future] });
        check(repository.Rollover(cutoff.AddMinutes(6), zone: zone).Moved == 0 && repository.Load(day).Plans.Any(p => p.Id == late.Id), "New input after cutoff waits for the next cutoff");
        var catchup = repository.Rollover(cutoff.AddDays(4), zone: zone);
        check(catchup.Moved == 2 && repository.Load(day.AddDays(5)).Plans.Count == 2 && repository.Load(day.AddDays(10)).Plans.Single().Id == future.Id, "Restart catch-up goes directly to final target and leaves future plans alone");
        check(repository.LoadPlanHistory(incomplete.Id).Moves.Last().Reason == "catch-up" && repository.LoadPlanHistory(late.Id).Moves.Single().MissedCutoffs == 4, "Catch-up counts cutoffs after creation without fabricated daily moves");
        var afterRoll = repository.Statistics(week);
        check(afterRoll.Plans == 5 && afterRoll.Done == 3, "Scheduled statistics follow current plan dates without duplicates");
        var outside = PlanEntry.Create("다른 주 배정") with { Done = true, ActualDate = day };
        repository.Save(repository.Load(day.AddDays(-8)) with { Plans = [outside] });
        check(repository.Statistics(week).Weekdays[0].ActualDone == 2 && repository.Statistics(week).Plans == 5, "Actual completion from a different scheduled period enters weekday statistics only");

        var weight = repository.SaveBodyWeight(70m, today);
        var energyInput = new EnergyInput("simple", "17190", null, 70m, true);
        var energyWorkout = WorkoutEntry.Create("walking", 13, null) with { Calculation = energyInput };
        energyWorkout = energyWorkout with { Energy = WorkoutEnergy.Calculate(energyWorkout, energyInput, weight, today).Snapshot };
        repository.Save(repository.Load(today.AddDays(-100)) with { Workouts = [energyWorkout, energyWorkout with { Id = Guid.NewGuid() }] });
        var energyStats = repository.Statistics(new(today.AddDays(-100), today.AddDays(-100)));
        check(energyStats.Workout.Kcal == energyWorkout.Energy!.Kcal * 2 && energyStats.Workout.Calculated == 2, "Statistics sum raw kcal before display rounding");
        repository.SaveBodyWeight(70m, today.AddDays(-20)); // Historic backup must restore with an expired current profile.
        repository.SavePreferencesJson("{\"Dark\":true,\"Accent\":\"#E3BACA\"}");
        var backup = repository.CreateBackup(); var backupPath = Path.Combine(directory, "manual-backup.json");
        var beforeExport = repository.Load(day);
        BackupFile.Write(backupPath, backup);
        check(repository.Load(day).Revision == beforeExport.Revision && repository.Load(day).Plans.SequenceEqual(beforeExport.Plans), "Export leaves the database and revisions unchanged");
        var parsed = BackupFile.Read(backupPath);
        check(parsed.Plans == backup.Plans && parsed.Workouts == backup.Workouts && parsed.Reviews == backup.Reviews && parsed.Moves.SequenceEqual(backup.Moves), "JSON backup round-trips records and rollover history");
        var restored = new PlanRepository(Path.Combine(directory, "restored.db")); restored.RestoreBackup(parsed);
        check(restored.Load(day).Plans.SequenceEqual(repository.Load(day).Plans) && restored.Load(day).Review == repository.Load(day).Review, "Restore keeps plan content, actual dates and review");
        check(restored.Load(today.AddDays(-100)).Workouts.SequenceEqual(repository.Load(today.AddDays(-100)).Workouts) && restored.LoadBodyWeight() == repository.LoadBodyWeight(), "Historic energy snapshots and expired weight history restore without recalculation");
        check(restored.LoadPreferencesJson() == backup.PreferencesJson && restored.LoadPlanHistory(incomplete.Id).Moves.SequenceEqual(history.Moves.Concat(repository.LoadPlanHistory(incomplete.Id).Moves.Skip(1))), "Settings and full rollover history restore with the records");
        var restoreStale = restored.Load(day);
        restored.Save(restored.Load(day.AddDays(100)) with { Plans = [PlanEntry.Create("복원 시 교체할 기록")] });
        restored.RestoreBackup(parsed);
        check(restored.Load(day.AddDays(100)).Plans.Count == 0, "Restore replaces existing records instead of merging");
        Expect<SaveConflictException>(() => restored.Save(restoreStale), "Snapshots from before restore cannot overwrite restored data");
        var invalid = parsed with { Days = parsed.Days.Concat([parsed.Days[0]]).ToArray() };
        Expect<InvalidDataException>(() => restored.RestoreBackup(invalid), "Duplicate backup dates rejected before replacement");
        check(restored.Load(day).Plans.SequenceEqual(repository.Load(day).Plans), "Rejected backup leaves existing records unchanged");
        Expect<InvalidDataException>(() => BackupFile.Parse(BackupFile.Serialize(parsed).Replace("\"Version\": 3", "\"Version\": 999")), "Unknown backup version rejected");
        Expect<InvalidDataException>(() => BackupFile.Parse("{broken}"), "Malformed JSON rejected");
        Expect<InvalidDataException>(() => BackupFile.Parse(BackupFile.Serialize(parsed).Replace("\"Format\":", "\"Unexpected\":0,\"Format\":")), "Unknown backup fields rejected");
        var invalidSport = parsed with { Days = [new DaySnapshot(day, 1, []) { Workouts = [WorkoutEntry.Create("unknown-sport", 10, null)] }], Origins = [], Moves = [] };
        Expect<ArgumentException>(() => restored.RestoreBackup(invalidSport), "Unknown exercise rejected before live replacement");
        var beforeFailedExport = File.ReadAllText(backupPath);
        Expect<InvalidDataException>(() => BackupFile.Write(backupPath, invalid), "Invalid export cannot overwrite an existing backup file");
        check(File.ReadAllText(backupPath) == beforeFailedExport, "Previous backup survives failed export validation");
        // Inject a late SQL failure to prove a replacement rolls back as a single transaction.
        using (var connection = new SqliteConnection("Data Source=" + restored.DatabasePath))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TRIGGER reject_restore BEFORE INSERT ON reviews BEGIN SELECT RAISE(ABORT,'test rollback'); END;"; command.ExecuteNonQuery(); }
        var revisionBeforeFailure = restored.Load(day).Revision;
        Expect<SqliteException>(() => restored.RestoreBackup(parsed), "Late restore write failure is surfaced");
        check(restored.Load(day).Revision == revisionBeforeFailure && restored.Load(day).Plans.SequenceEqual(repository.Load(day).Plans) && restored.LoadPreferencesJson() == backup.PreferencesJson, "Late restore failure rolls back records, revisions and settings together");
        check(Pds.Desktop.StartupRegistration.Command(@"C:\Program Files\PDS\Pds.Desktop.exe") == '"' + @"C:\Program Files\PDS\Pds.Desktop.exe" + '"', "Automatic startup quotes paths containing spaces without touching the registry");
        Expect<ArgumentException>(() => Pds.Desktop.StartupRegistration.Command("relative.exe"), "Automatic startup rejects ambiguous relative paths");
        var categoryItems = new[] { new DaySnapshot(day, 0, []) { Workouts = [WorkoutEntry.Create("cycling-indoor", 600, null), WorkoutEntry.Create("cycling-outdoor", 1200, null)] } };
        var categoryStats = StatisticsReport.Create(categoryItems, week);
        check(categoryStats.Categories.Single().Workout.Days == 1 && categoryStats.Categories.Single().Workout.Seconds == 1800 && categoryStats.Exercises.Count == 2, "Category days use a date union across indoor and outdoor cycling");
        var v4Path = Path.Combine(directory, "schema-four.db"); var v4 = new PlanRepository(v4Path, clock);
        var legacyDay = new DateOnly(2026, 1, 1);
        var legacySnapshot = v4.Save(v4.Load(legacyDay) with { Plans = [PlanEntry.Create("과거 미완료"), legacyDone with { Id = Guid.NewGuid() }] });
        using (var connection = new SqliteConnection("Data Source=" + v4Path))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE plan_rollovers; DROP TABLE plan_metadata; DROP TABLE preferences; PRAGMA user_version=4;"; command.ExecuteNonQuery(); }
        var migrated = new PlanRepository(v4Path, clock);
        check(migrated.Load(legacyDay).Plans.SequenceEqual(legacySnapshot.Plans) && migrated.LoadPlanHistory(legacySnapshot.Plans[0].Id).Origin?.CreatedUtc is null, "Schema four upgrade preserves records without inventing execution or creation dates");
        check(migrated.Rollover(cutoff, zone: zone).Moved == 1 && migrated.Load(legacyDay).Plans.Single().Done, "Legacy incomplete plan can catch up while legacy completed plan remains");
        var edge = new PlanRepository(Path.Combine(directory, "sunday-rollover.db"), new WallClock(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.FromHours(9))));
        var sunday = new DateOnly(2026, 10, 18); edge.Save(edge.Load(sunday) with { Plans = [PlanEntry.Create("일요일 미완료")] });
        edge.Rollover(new DateTimeOffset(2026, 10, 18, 23, 0, 0, TimeSpan.FromHours(9)), zone: zone);
        check(edge.Statistics(DateRange.Week(sunday)).CompletionRate is null && edge.Statistics(DateRange.Week(sunday.AddDays(1))).Plans == 1, "Sunday rollover removes prior-week denominator and enters next week once");
        var moved = edge.Load(sunday.AddDays(1)); var removedId = moved.Plans.Single().Id;
        edge.Save(moved with { Plans = [] });
        check(edge.LoadPlanHistory(removedId).Origin is null && edge.LoadPlanHistory(removedId).Moves.Count == 0, "Confirmed plan deletion removes its metadata and rollover history");
        var exactClock = new WallClock(new DateTimeOffset(2026, 10, 12, 23, 0, 0, TimeSpan.FromHours(9)));
        var exact = new PlanRepository(Path.Combine(directory, "exact-cutoff.db"), exactClock);
        var exactPlan = PlanEntry.Create("정각에 저장"); exact.Save(exact.Load(day) with { Plans = [exactPlan] });
        exact.Rollover(exactClock.Now.AddDays(2), zone: zone);
        check(exact.LoadPlanHistory(exactPlan.Id).Moves.Single().MissedCutoffs == 3, "An entry saved exactly at 23:00 includes that cutoff in later catch-up history");
        var lateClock = new WallClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.FromHours(9)));
        var lateRepo = new PlanRepository(Path.Combine(directory, "late-workouts.db"), lateClock);
        var friday = new DateOnly(2026, 10, 16);
        lateRepo.Save(lateRepo.Load(friday) with { Plans = [PlanEntry.Create("이월할 계획")], Workouts = [WorkoutEntry.Create("walking", 1800, null)] });
        lateClock.Now = lateClock.Now.AddHours(11).AddMinutes(59);
        lateRepo.Rollover(lateClock.Now, zone: zone);
        var night = lateRepo.Load(friday);
        lateRepo.Save(night with { Workouts = night.Workouts.Concat([WorkoutEntry.Create("pushups", null, 30)]).ToArray() });
        var nightStats = lateRepo.Statistics(new(friday.AddDays(-27), friday));
        check(lateRepo.Load(friday).Plans.Count == 0 && lateRepo.Load(friday).Workouts.Count == 2 && lateRepo.Load(friday.AddDays(1)).Workouts.Count == 0,
            "23:59 exercise entry stays on its performed date while incomplete plans alone roll over");
        check(nightStats.Weekdays[4].Workout.Records == 2 && nightStats.Weekdays[4].Workout.Days == 1 && nightStats.Workout.Seconds == 1800,
            "Friday statistics include after-23:00 and quantity-only exercise records");
        lateClock.Now = lateClock.Now.AddMinutes(1);
        night = lateRepo.Load(friday);
        lateRepo.Save(night with { Workouts = night.Workouts.Concat([WorkoutEntry.Create("badminton", 1200, null)]).ToArray() });
        lateRepo.Rollover(lateClock.Now, zone: zone);
        var afterMidnight = lateRepo.Statistics(new(friday.AddDays(-27), friday.AddDays(1)));
        check(afterMidnight.Weekdays[4].Workout.Records == 3 && afterMidnight.Weekdays[4].Workout.Seconds == 3000 && afterMidnight.Weekdays[5].Workout.Records == 0,
            "A next-day late log updates the original performed weekday without moving workouts");
        check(Pds.Desktop.WindowPlacement.Fit(1326, 22, 940, 700, 0, 0, 1920, 1032) == (980d, 22d), "Expanding a compact window at the right edge keeps the entire general window visible");
        check(Pds.Desktop.WindowPlacement.Fit(-5000, 5000, 940, 700, 0, 0, 1920, 1032) == (0d, 332d), "Removed-screen coordinates recover inside the available work area");
        check(Pds.Desktop.WindowPlacement.Fit(-1000, -400, 600, 450, -1920, -200, 1920, 1080) == (-1000d, -200d), "Window fitting respects negative work-area coordinates");
    }
    private sealed class WallClock(DateTimeOffset now) : TimeProvider
    { public DateTimeOffset Now { get; set; } = now; public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime(); }
}
