using Microsoft.Data.Sqlite;
using Pds.Core;

var directory = Path.Combine(Path.GetTempPath(), "PdsChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var passed = 0;
try
{
    var path = Path.Combine(directory, "records.db");
    var repository = new PlanRepository(path);
    var day = new DateOnly(2026, 10, 15);
    var empty = repository.Load(day);
    Check(empty.Revision == 0 && empty.Plans.Count == 0, "Empty day");
    var plan = PlanEntry.Create("걷기 ' ; DROP TABLE plans; --", "선택 메모", "09:00", "09:30");
    var saved = repository.Save(empty with { Plans = [plan] });
    Check(saved.Revision == 1, "Initial save revision");
    var reopened = new PlanRepository(path).Load(day);
    Check(reopened.Plans.Single() == plan, "Reopen database preserves title, time and note / parameterized SQL");
    var completed = repository.Save(reopened with { Plans = [plan with { Done = true }] });
    Check(repository.Counts(day, day)[day] == new DayCount(1, 1), "Counts follow completed save");
    Expect<SaveConflictException>(() => repository.Save(saved with { Plans = [] }), "Stale edits cannot overwrite saved data");
    Check(repository.Load(day).Plans.Count == 1, "Conflict preserves current records");
    Expect<ArgumentException>(() => repository.Save(completed with { Plans = [PlanEntry.Create("")] }), "Blank title rejected");
    Expect<ArgumentException>(() => repository.Save(completed with { Plans = [PlanEntry.Create("운동", start: "22:00", end: "09:00")] }), "Reversed times rejected");
    Check(repository.Load(day).Revision == completed.Revision, "Validation failure has no partial write");
    var next = day.AddDays(1);
    var nextSaved = repository.Save(repository.Load(next) with { Plans = [PlanEntry.Create("다음 날 계획")] });
    Expect<SqliteException>(() => repository.Save(nextSaved with { Plans = [plan] }), "Primary key collision rolls back entire day replacement");
    Check(repository.Load(next).Plans.Single().Title == "다음 날 계획" && repository.Load(next).Revision == nextSaved.Revision, "Transaction rollback preserves previous day content and revision");
    var amended = repository.Save(completed with { Plans = [plan with { Title = "수정된 계획", Done = true }] });
    Check(repository.Load(day).Plans.Single().Id == plan.Id && repository.Load(day).Plans.Single().Title == "수정된 계획", "Edit preserves identity");
    repository.Save(amended with { Plans = [] });
    Check(repository.Load(day).Plans.Count == 0 && !repository.Counts(day, day).ContainsKey(day), "Delete removes calendar counts");
    Check(repository.Counts(next, next).Count == 1 && repository.Counts(day.AddDays(-1), day).Count == 0, "Date range boundaries");
    using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
    { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=13"; command.ExecuteNonQuery(); }
    Expect<InvalidDataException>(() => new PlanRepository(path), "Unknown newer schema is not overwritten");
    Check(Pds.Desktop.SelectedCalendarColors.Saturday("#E3BACA") == "#1C4C91"
        && Pds.Desktop.SelectedCalendarColors.Contrast("#1C4C91", "#E3BACA") >= 4.5, "Saturday stays readable on selected pink in both themes");
    Check(Pds.Desktop.SelectedCalendarColors.Contrast(Pds.Desktop.SelectedCalendarColors.Sunday("#E3BACA"), "#E3BACA") >= 4.5, "Sunday stays readable on selected pink");
    Check(Pds.Desktop.SelectedCalendarColors.Ink("#E3BACA") == "#211F25"
        && Pds.Desktop.SelectedCalendarColors.Contrast("#211F25", "#E3BACA") >= 4.5, "Save label uses dark ink on pink in both themes");
    var channels = new[] { 0, 64, 128, 192, 255 };
    var accents = from red in channels from green in channels from blue in channels select $"#{red:X2}{green:X2}{blue:X2}";
    Check(accents.All(color => Pds.Desktop.SelectedCalendarColors.Contrast(Pds.Desktop.SelectedCalendarColors.Saturday(color), color) >= 4.5
        && Pds.Desktop.SelectedCalendarColors.Contrast(Pds.Desktop.SelectedCalendarColors.Sunday(color), color) >= 4.5
        && Pds.Desktop.SelectedCalendarColors.Contrast(Pds.Desktop.SelectedCalendarColors.Ink(color), color) >= 4.5), "Weekend and save label contrast for 125 custom accent colors");
    var workoutPath = Path.Combine(directory, "workouts.db");
    var workoutRepository = new PlanRepository(workoutPath);
    var walk = WorkoutEntry.Create("walking", 1800, null, "걷기 기록");
    var stairs = WorkoutEntry.Create("stairs", null, 20);
    var pushups = WorkoutEntry.Create("pushups", null, 30);
    var plank = WorkoutEntry.Create("plank", 90, null);
    var originalPlan = PlanEntry.Create("운동과 함께 저장하는 계획");
    var workoutSaved = workoutRepository.Save(workoutRepository.Load(day) with { Plans = [originalPlan], Workouts = [walk, stairs, pushups, plank] });
    var workoutReopened = new PlanRepository(workoutPath).Load(day);
    Check(workoutReopened.Workouts.SequenceEqual(new[] { walk, stairs, pushups, plank }) && workoutReopened.Plans.Single() == originalPlan, "Workout durations, quantities, notes and plans survive reopening");
    Check(workoutRepository.Counts(day, day)[day] == new DayCount(1, 0) { Workouts = 4, WorkoutSeconds = 1890, TimedWorkouts = 2 }, "Counts aggregate durations without converting quantity-only records to time");
    Check(ExerciseCatalog.All.Count == 22 && ExerciseCatalog.All.Select(e => e.Id).Distinct().Count() == 22 && ExerciseCatalog.Categories.Count == 5
        && ExerciseCatalog.Get("cycling-indoor").Id != ExerciseCatalog.Get("cycling-outdoor").Id
        && ExerciseCatalog.All.Any(e => e.Id == "squash") && !ExerciseCatalog.All.Any(e => e.Name == "배구"), "Stable catalog keeps 22 sports and separates indoor/outdoor cycling");
    Expect<ArgumentException>(() => workoutRepository.Save(workoutSaved with { Workouts = [walk with { DurationSeconds = 0 }] }), "Zero workout duration rejected");
    Expect<ArgumentException>(() => workoutRepository.Save(workoutSaved with { Workouts = [walk with { DurationSeconds = -1 }] }), "Negative duration rejected");
    Expect<ArgumentException>(() => workoutRepository.Save(workoutSaved with { Workouts = [stairs with { Quantity = 0 }] }), "Zero quantity rejected");
    Expect<ArgumentException>(() => workoutRepository.Save(workoutSaved with { Workouts = [walk with { ExerciseId = "unknown" }] }), "Unknown exercise rejected");
    Expect<ArgumentException>(() => workoutRepository.Save(workoutSaved with { Workouts = [walk, walk] }), "Duplicate workout identity rejected");
    Check(workoutRepository.Load(day).Revision == workoutSaved.Revision, "Invalid workouts never partially save a day");
    var workoutEdited = workoutRepository.Save(workoutSaved with { Workouts = [walk with { DurationSeconds = 2400 }, stairs, pushups, plank] });
    Check(workoutRepository.Load(day).Workouts.Count == 4 && workoutRepository.Load(day).Workouts[0].Id == walk.Id && workoutRepository.Counts(day, day)[day].WorkoutSeconds == 2490, "Workout edit preserves identity and refreshes total time");
    Expect<SaveConflictException>(() => workoutRepository.Save(workoutSaved with { Workouts = [] }), "Stale workout edit cannot overwrite newer data");
    var collisionDay = workoutRepository.Save(workoutRepository.Load(next) with { Plans = [PlanEntry.Create("보호할 계획")], Workouts = [WorkoutEntry.Create("tennis", 3600, null)] });
    Expect<SqliteException>(() => workoutRepository.Save(collisionDay with { Plans = [], Workouts = [walk] }), "Cross-day workout ID collision rolls back plan and workout changes together");
    var afterCollision = workoutRepository.Load(next);
    Check(afterCollision.Revision == collisionDay.Revision && afterCollision.Plans.Single().Title == "보호할 계획" && afterCollision.Workouts.Single().ExerciseId == "tennis", "Failed day save preserves plans, workouts and revision");
    workoutRepository.Save(workoutEdited with { Plans = [], Workouts = [stairs] });
    Check(workoutRepository.Load(day).Workouts.Single() == stairs && workoutRepository.Counts(day, day)[day].WorkoutSeconds == 0, "Deleting plans keeps independent quantity-only workout");
    var migrationPath = Path.Combine(directory, "version-one.db");
    using (var connection = new SqliteConnection($"Data Source={migrationPath};Pooling=False"))
    {
        connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE days (date TEXT PRIMARY KEY, revision INTEGER NOT NULL);
            CREATE TABLE plans (id TEXT PRIMARY KEY,date TEXT NOT NULL REFERENCES days(date),position INTEGER NOT NULL,title TEXT NOT NULL,note TEXT NOT NULL,done INTEGER NOT NULL CHECK(done IN(0,1)),start_time TEXT,end_time TEXT);
            INSERT INTO days VALUES('2026-10-15',7);
            INSERT INTO plans VALUES('cbb10f97-4b1e-4d39-b44a-79aa8c951c54','2026-10-15',0,'이전 버전 계획','이전 메모',1,'09:00','09:30');
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
    }
    var migrated = new PlanRepository(migrationPath).Load(day);
    Check(migrated.Revision == 7 && migrated.Plans.Single().Title == "이전 버전 계획" && migrated.Plans.Single().Done && migrated.Plans.Single().Note == "이전 메모" && migrated.Workouts.Count == 0, "Schema v1 upgrade preserves existing plans, completion, memo and revision");
    var clock = new ManualClock(); var meditation = new MeditationSession(clock);
    clock.Advance(TimeSpan.FromHours(1));
    Check(meditation.State == MeditationState.Ready && meditation.Remaining == TimeSpan.FromMinutes(5), "Meditation does not auto-start");
    meditation.Start(); clock.Advance(TimeSpan.FromSeconds(65));
    Check(meditation.Remaining == TimeSpan.FromSeconds(235), "Meditation counts from chosen start");
    meditation.Close();
    Check(meditation.State == MeditationState.Ready && meditation.Remaining == TimeSpan.FromMinutes(5), "Meditation can close midway without completion");
    meditation.Start(); clock.Advance(TimeSpan.FromMinutes(6));
    Check(meditation.Remaining == TimeSpan.Zero && meditation.State == MeditationState.Completed, "Meditation completes after five elapsed minutes even without ticks");
    var pipeName = "PdsChecks-" + Guid.NewGuid().ToString("N"); var requests = 0;
    using (var server = new Pds.Desktop.InstanceActivation(pipeName))
    {
        server.Listen(() => { Interlocked.Increment(ref requests); return Task.CompletedTask; }, ex => throw new InvalidOperationException("Activation listener failed", ex));
        using var client = new Pds.Desktop.InstanceActivation(pipeName);
        Check(await client.RequestAsync(TimeSpan.FromSeconds(2)) && requests == 1, "Second-launch request reaches existing instance and is acknowledged");
        Check(await client.RequestAsync(TimeSpan.FromSeconds(2)) && requests == 2, "Activation accepts repeated launches");
    }
    using (var absent = new Pds.Desktop.InstanceActivation("PdsAbsent-" + Guid.NewGuid().ToString("N")))
        Check(!await absent.RequestAsync(TimeSpan.FromMilliseconds(100)), "Missing activation endpoint returns without an indefinite wait");
    Check(Pds.Desktop.InstanceActivation.NameFor(directory) != Pds.Desktop.InstanceActivation.NameFor(Path.Combine(directory, "isolated")), "Isolated test data cannot target the personal activation endpoint");
    EnergyChecks.Run(directory, Check);
    ReviewChecks.Run(directory, Check);
    CompletionChecks.Run(directory, Check);
    AnniversaryChecks.Run(directory, Check);
    AnniversaryOriginChecks.Run(directory, Check);
    HolidayChecks.Run(Check);
    JournalChecks.Run(directory, Check);
    StudyOverviewChecks.Run(directory, Check);
    AttachmentChecks.Run(directory, Check);
    FixedCostChecks.Run(directory, Check);
    RecordToolsChecks.Run(directory, Check);
    FeedbackBatchChecks.Run(directory, Check);
    Console.WriteLine($"PASS: {passed} persistence, review, workout, energy, weight, timer, activation and contrast checks.");
}
finally
{
    SqliteConnection.ClearAllPools();
    var resolved = Path.GetFullPath(directory);
    var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
    if (!resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("PdsChecks-", StringComparison.Ordinal))
        throw new InvalidOperationException("Refusing to clean an unexpected test directory");
    foreach(var file in Directory.EnumerateFiles(resolved,"*",SearchOption.AllDirectories))File.SetAttributes(file,FileAttributes.Normal);
    Directory.Delete(resolved, recursive: true);
}

void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); passed++; Console.WriteLine("PASS " + label); }
void Expect<T>(Action action, string label) where T : Exception
{ try { action(); } catch (T) { Check(true, label); return; } throw new InvalidOperationException(label + " did not fail as expected"); }

sealed class ManualClock : TimeProvider
{
    private long timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => timestamp;
    public void Advance(TimeSpan duration) => timestamp += duration.Ticks;
}
