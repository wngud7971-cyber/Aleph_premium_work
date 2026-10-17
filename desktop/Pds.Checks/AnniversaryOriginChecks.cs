using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pds.Core;

static class AnniversaryOriginChecks
{
    public static void Run(string directory, Action<bool, string> check)
    {
        void Expect<T>(Action action, string label) where T : Exception
        { try { action(); } catch (T) { check(true, label); return; } throw new InvalidOperationException(label); }
        var template = Anniversary.Create("생일", AnniversaryKind.Birthday, AnniversaryCalendar.Solar, 1, 1);
        var solarOrigin = new AnniversaryOrigin(2025, 7, 25, AnniversaryCalendar.Solar);
        var converted = AnniversaryOrigins.Apply(template, solarOrigin, AnniversaryCalendar.Lunar);
        check(converted is { Month: 6, Day: 1, LeapMonth: true } && converted.Origin == solarOrigin, "Solar birth date automatically resolves KASI 2025 leap six with origin preserved");
        check(AnniversaryDates.InYear(converted, 2025)?.Date == new DateOnly(2025, 7, 25) && AnniversaryDates.InYear(converted, 2026) is null,
            "Automatically inferred leap birthday follows user's skip-year policy");
        var lunarOrigin = new AnniversaryOrigin(2026, 1, 1, AnniversaryCalendar.Lunar);
        var solarAnnual = AnniversaryOrigins.Apply(template, lunarOrigin, AnniversaryCalendar.Solar);
        check(solarAnnual is { Month: 2, Day: 17, LeapMonth: false } && AnniversaryDates.InYear(solarAnnual, 2027)?.Date == new DateOnly(2027, 2, 17), "Original lunar year-month-day converts once to chosen solar birthday rule");
        check(AnniversaryOrigins.Candidates(2025, 6, 1, AnniversaryCalendar.Lunar).Count == 2, "Ambiguous original lunar birthday exposes both regular and leap candidates");
        check(AnniversaryOrigins.Candidates(2026, 6, 1, AnniversaryCalendar.Lunar) is { Count: 1 } single && !single[0].LeapMonth,
            "Non-leap birth year resolves regular month without a user question");
        check(AnniversaryOrigins.Candidates(2025, 6, 30, AnniversaryCalendar.Lunar) is { Count: 1 } shortLeap && !shortLeap[0].LeapMonth,
            "Day thirty rules out short leap month and resolves original date automatically");
        Expect<ArgumentException>(() => AnniversaryOrigins.Candidates(2026, 2, 30, AnniversaryCalendar.Lunar), "Nonexistent original lunar birthday is rejected instead of clamped");
        Expect<ArgumentException>(() => AnniversaryOrigins.ToSolar(new(2025, 2, 29, AnniversaryCalendar.Solar)), "Birth year rejects February 29 in a non-leap year");
        var feb = AnniversaryOrigins.Apply(template, new(2024, 2, 29, AnniversaryCalendar.Solar), AnniversaryCalendar.Solar);
        check(AnniversaryDates.InYear(feb, 2025)?.Date == new DateOnly(2025, 2, 28) && AnniversaryDates.InYear(feb, 2023) is null,
            "Birthday begins on original date and later uses explicit missing-day rule");
        var december = AnniversaryOrigins.Apply(template, new(2025, 12, 1, AnniversaryCalendar.Lunar), AnniversaryCalendar.Lunar);
        check(AnniversaryDates.Between([december], new(2026, 1, 1), new(2026, 1, 31)).Single().Date == new DateOnly(2026, 1, 19),
            "Original lunar year remains correct when birthday falls in next solar January");
        Expect<ArgumentException>(() => (converted with { Month = 5 }).Validate(), "Mismatched original and annual birthday rejects invalid persisted rule");
        var repository = new PlanRepository(Path.Combine(directory, "birth-origin.db"));
        var saved = repository.SaveAnniversary(converted);
        check(new PlanRepository(repository.DatabasePath).LoadAnniversaries().Single().Origin == solarOrigin, "Full birth date survives reopening without changing original input calendar");
        var backup = repository.CreateBackup(); var restore = new PlanRepository(Path.Combine(directory, "birth-origin-restored.db"));
        restore.RestoreBackup(BackupFile.Parse(BackupFile.Serialize(backup)));
        check(backup.Version == 3 && restore.LoadAnniversaries().Single().Origin == solarOrigin, "Backup v3 preserves complete origin birthday and inferred lunar rule");
        var oldEntry = saved with { Origin = null };
        var v2 = backup with { Version = 2, Anniversaries = [oldEntry] };
        var node = JsonNode.Parse(BackupFile.Serialize(v2))!.AsObject();
        foreach (var item in node["Anniversaries"]!.AsArray()) item!.AsObject().Remove("Origin");
        check(BackupFile.Parse(node.ToJsonString()).Anniversaries.Single().Origin is null, "Legacy v2 backup without birth year stays readable without inventing one");
        Expect<InvalidDataException>(() => BackupFile.Serialize(backup with { Version = 2 }), "Full original birth date cannot be mislabeled as legacy backup v2");
        repository.SaveAnniversary(saved with { Origin = null });
        using (var c = new SqliteConnection("Data Source=" + repository.DatabasePath))
        { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA user_version=6"; cmd.ExecuteNonQuery(); }
        var upgraded = new PlanRepository(repository.DatabasePath).LoadAnniversaries().Single();
        check(upgraded.Id == saved.Id && upgraded.LeapMonth && upgraded.Origin is null, "Schema six upgrades existing month-day rule without inventing birth year");
    }
}
