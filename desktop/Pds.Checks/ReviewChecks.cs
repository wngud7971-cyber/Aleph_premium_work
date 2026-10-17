using Microsoft.Data.Sqlite;
using Pds.Core;

static class ReviewChecks
{
    public static void Run(string directory, Action<bool, string> check)
    {
        void Expect<T>(Action action, string label) where T : Exception
        { try { action(); } catch (T) { check(true, label); return; } throw new InvalidOperationException(label); }
        var path = Path.Combine(directory, "reviews.db"); var repository = new PlanRepository(path);
        var day = new DateOnly(2026, 1, 31); var next = day.AddDays(1);
        check(repository.Load(day).Review is null && repository.CountReviewDays(day, next) == 0, "No fabricated initial review");
        var empty = repository.Save(repository.Load(day) with { Review = new(" \r\n\t", " ", "\u3000") });
        check(empty.Review is null && repository.Load(day).Review is null && repository.CountReviewDays(day, next) == 0, "Whitespace-only review creates no entry or review day");
        var first = new DayReview("  한 칸만 ' ; DROP TABLE reviews; --\n두 번째 줄  ", "", "");
        var saved = repository.Save(empty with { Review = first });
        check(new PlanRepository(path).Load(day).Review == first.Normalize(), "Single optional review field and multiline Unicode survive reopening / parameterized SQL");
        check(repository.Load(day).Plans.Count == 0 && repository.Load(day).Workouts.Count == 0 && repository.CountReviewDays(day, day) == 1, "Review alone is valid without plans or workouts");
        var full = new DayReview("잘한 점", "아쉬운 점", "다음 행동");
        var edited = repository.Save(saved with { Review = full });
        check(repository.Load(day).Review == full && repository.CountReviewDays(day, day) == 1, "Editing three review fields replaces the single daily entry");
        Expect<SaveConflictException>(() => repository.Save(saved with { Review = new("오래된 수정", "", "") }), "Stale review cannot overwrite a newer save");
        var plan = PlanEntry.Create("유지할 계획"); var workout = WorkoutEntry.Create("stairs", null, 12);
        var combined = repository.Save(edited with { Plans = [plan], Workouts = [workout] });
        Expect<ArgumentException>(() => repository.Save(combined with { Plans = [], Review = new(new string('가', 2001), "", "") }), "Oversized review rejects the whole day save");
        var afterInvalid = repository.Load(day);
        check(afterInvalid.Revision == combined.Revision && afterInvalid.Plans.Single() == plan && afterInvalid.Workouts.Single() == workout && afterInvalid.Review == full, "Invalid review preserves plans, workouts, review and revision");
        var nextSaved = repository.Save(repository.Load(next) with { Plans = [PlanEntry.Create("다른 날 계획")], Review = new("다른 날 회고", "", "") });
        Expect<SqliteException>(() => repository.Save(nextSaved with { Plans = [], Workouts = [workout], Review = new("실패할 회고", "", "") }), "Late workout collision rolls back the review and other day changes");
        var afterCollision = repository.Load(next);
        check(afterCollision.Revision == nextSaved.Revision && afterCollision.Plans.Count == 1 && afterCollision.Review == nextSaved.Review, "Transaction rollback preserves the previous review");
        check(repository.CountReviewDays(day, next) == 2 && repository.CountReviewDays(new DateOnly(2026, 1, 1), day) == 1
            && repository.CountReviewDays(next, new DateOnly(2026, 2, 28)) == 1, "Review-day counts respect inclusive month boundaries without duplication");
        var maximum = new DayReview(new string('가', 2000), new string('나', 2000), new string('다', 2000));
        var maxSaved = repository.Save(combined with { Review = maximum });
        check(repository.Load(day).Review == maximum, "Each of three review fields accepts 2000 characters");
        var deleted = repository.Save(maxSaved with { Review = new(" ", "\t", "\r\n") });
        var afterDelete = repository.Load(day);
        check(deleted.Review is null && afterDelete.Review is null && afterDelete.Plans.Single() == plan && afterDelete.Workouts.Single() == workout,
            "Clearing a review removes only the review while preserving plans and workouts");
        check(repository.CountReviewDays(day, day) == 0 && repository.CountReviewDays(day, next) == 1, "Deleted review is excluded from review-day counts");
        var lastOnly = repository.Save(deleted with { Review = new("", "", "다음 행동만 기록") });
        check(lastOnly.Review?.Good == "" && lastOnly.Review.Difficult == "" && lastOnly.Review.NextAction.Length > 0, "Next-action-only review is also valid");

        var v3Path = Path.Combine(directory, "version-three.db"); var old = new PlanRepository(v3Path);
        var today = DateOnly.FromDateTime(DateTime.Today); var weight = old.SaveBodyWeight(70m, today);
        var input = new EnergyInput("simple", "17190", null, 70m, true);
        var walk = WorkoutEntry.Create("walking", 1800, null) with { Calculation = input };
        walk = walk with { Energy = WorkoutEnergy.Calculate(walk, input, weight, today).Snapshot };
        var before = old.Save(old.Load(day) with { Plans = [plan], Workouts = [walk] });
        using (var connection = new SqliteConnection($"Data Source={v3Path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE reviews; PRAGMA user_version=3;"; command.ExecuteNonQuery();
        }
        var upgraded = new PlanRepository(v3Path); var restored = upgraded.Load(day);
        check(restored.Revision == before.Revision && restored.Plans.Single() == plan && restored.Workouts.Single() == walk
            && upgraded.LoadBodyWeight() == weight && restored.Review is null, "Schema v3 upgrade preserves plans, workouts, kcal and weight without inventing reviews");
    }
}
