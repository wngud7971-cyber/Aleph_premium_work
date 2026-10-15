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
    { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=2"; command.ExecuteNonQuery(); }
    Expect<InvalidDataException>(() => new PlanRepository(path), "Unknown newer schema is not overwritten");
    Console.WriteLine($"PASS: {passed} persistence and validation checks.");
}
finally
{
    SqliteConnection.ClearAllPools();
    var resolved = Path.GetFullPath(directory);
    var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
    if (!resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("PdsChecks-", StringComparison.Ordinal))
        throw new InvalidOperationException("Refusing to clean an unexpected test directory");
    Directory.Delete(resolved, recursive: true);
}

void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); passed++; Console.WriteLine("PASS " + label); }
void Expect<T>(Action action, string label) where T : Exception
{ try { action(); } catch (T) { Check(true, label); return; } throw new InvalidOperationException(label + " did not fail as expected"); }
