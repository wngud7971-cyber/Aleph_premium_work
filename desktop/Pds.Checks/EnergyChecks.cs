using Microsoft.Data.Sqlite;
using Pds.Core;

static class EnergyChecks
{
    public static void Run(string directory, Action<bool, string> check)
    {
        void Expect<T>(Action action, string label) where T : Exception
        { try { action(); } catch (T) { check(true, label); return; } throw new InvalidOperationException(label); }
        var today = DateOnly.FromDateTime(DateTime.Today);
        var repository = new PlanRepository(Path.Combine(directory, "energy.db"));
        check(repository.LoadBodyWeight() is null, "No fabricated initial weight");
        Expect<ArgumentException>(() => repository.SaveBodyWeight(0m, today), "Zero body weight rejected");
        Expect<ArgumentException>(() => repository.SaveBodyWeight(70.25m, today), "Body weight limited to one decimal place");
        var profile = repository.SaveBodyWeight(70m, today);
        check(new PlanRepository(repository.DatabasePath).LoadBodyWeight() == profile, "Body weight and entered date survive reopening");
        check(profile.IsCurrent(today.AddDays(13)) && !profile.IsCurrent(today.AddDays(14)) && !profile.IsCurrent(today.AddDays(-1)), "Weight expires at 14-day boundary and future entry dates are invalid");
        check(EnergyCatalog.All.Select(p => p.Code).Distinct().Count() == EnergyCatalog.All.Count
            && ExerciseCatalog.All.All(e => EnergyCatalog.For(e.Id).Any()), "All 22 sports have explicit verified presets with distinct activity codes");
        var input = new EnergyInput("simple", "17190", null, 70m, true);
        var walk = WorkoutEntry.Create("walking", 1800, null) with { Calculation = input };
        var calculated = WorkoutEnergy.Calculate(walk, input, profile, today).Snapshot!;
        check(calculated.Kcal == 133m && calculated.Met == 3.8m && calculated.Code == "17190", "Walking 70kg 30min uses official 3.8 MET for 133 kcal");
        check(EnergySnapshot.Estimate(6m, 70m, 1800) == 210m && EnergySnapshot.Display(682.5m) == 683m, "Energy formula and midpoint rounding match specification");
        check(WorkoutEnergy.Calculate(walk, input, null, today).Snapshot is null
            && WorkoutEnergy.Calculate(walk, input, profile, today.AddDays(14)).Snapshot is null
            && WorkoutEnergy.Calculate(walk, input, profile, today.AddDays(-1)).Snapshot is null, "Missing, expired and future-dated profiles cannot produce new kcal");
        check(WorkoutEnergy.Calculate(walk, input with { AdultConfirmed = false }, profile, today).Snapshot is null, "Adult basis must be explicitly confirmed");
        var reps = WorkoutEntry.Create("pushups", null, 40);
        check(WorkoutEnergy.Calculate(reps, input with { PresetCode = "02022" }, profile, today).Snapshot is null, "Repetitions alone cannot produce kcal");
        var futsalInput = input with { PresetCode = "15195" };
        var futsal = WorkoutEntry.Create("futsal", 7200, null) with { Calculation = futsalInput };
        check(WorkoutEnergy.Calculate(futsal, futsalInput, profile, today).Snapshot is null, "Two-hour futsal does not silently assume active minutes");
        var pattern = WorkoutEnergy.FutsalPattern(6);
        check(pattern.TotalSeconds == 7200 && pattern.ActiveSeconds == 5400, "Six confirmed 15+5 cycles propose 120 total and 90 active minutes");
        var futsalEnergy = WorkoutEnergy.Calculate(futsal with { ActiveSeconds = 5400 }, futsalInput, profile, today).Snapshot!;
        var editedFutsalEnergy = WorkoutEnergy.Calculate(futsal with { ActiveSeconds = 4500 }, futsalInput, profile, today).Snapshot!;
        check(futsalEnergy.Kcal == 819m && editedFutsalEnergy.Kcal == 682.5m, "Futsal uses active 90 or 75 minutes without charging rest");
        Expect<ArgumentException>(() => (futsal with { ActiveSeconds = 7201 }).Validate(), "Active minutes exceeding total rejected");
        Expect<ArgumentException>(() => WorkoutEnergy.FutsalPattern(0), "Invalid futsal cycles rejected");
        var cycle = WorkoutEntry.Create("cycling-indoor", 1800, null);
        var detail = new EnergyInput("detail", null, 50m, 70m, true);
        check(WorkoutEnergy.Calculate(cycle, detail, profile, today).Snapshot?.Kcal == 140m
            && WorkoutEnergy.Calculate(cycle, detail with { DetailValue = 100m }, profile, today).Snapshot?.Kcal == 210m, "Exact published indoor wattage conditions select 4.0 or 6.0 MET");
        check(WorkoutEnergy.Calculate(cycle, detail with { DetailValue = 55m }, profile, today).Snapshot is null, "Unsupported wattage does not interpolate or fall back to general");
        check(EnergyCatalog.Resolve("cycling-outdoor", input with { PresetCode = "01200" }) is null, "Indoor preset cannot leak into outdoor cycling");
        check(EnergyCatalog.Resolve("walking", detail with { DetailValue = 3.218688m })?.Code == "17152"
            && EnergyCatalog.Resolve("walking", detail with { DetailValue = 4.1m }) is null, "Speed converts exact km/h to mph boundaries while preserving unsupported gaps");
        check(EnergyCatalog.Resolve("walking", input with { DetailValue = 999m })?.Code == "17190", "Simple mode ignores hidden detail values");
        var savedWalk = walk with { Energy = calculated };
        var saved = repository.Save(repository.Load(today) with { Workouts = [savedWalk] });
        check(new PlanRepository(repository.DatabasePath).Load(today).Workouts.Single() == savedWalk, "Energy input, source, formula, MET, raw kcal and weight snapshot survive reopening");
        var changedProfile = repository.SaveBodyWeight(75m, today);
        check(changedProfile.Id > profile.Id && repository.Load(today).Workouts.Single().Energy == calculated, "Updating current weight preserves historical kcal");
        var expired = repository.SaveBodyWeight(75m, today.AddDays(-14));
        var preserved = repository.Save(saved with { Workouts = [savedWalk with { Note = "메모만 수정" }] });
        check(repository.Load(today).Workouts.Single().Energy == calculated, "Expired profile still allows an unchanged historical calculation to be saved");
        var rejectedNew = savedWalk with { Id = Guid.NewGuid() };
        Expect<ArgumentException>(() => repository.Save(repository.Load(today.AddDays(1)) with { Workouts = [rejectedNew] }), "Storage also rejects new kcal when default weight is expired");
        check(repository.Load(today.AddDays(1)).Revision == 0, "Expired-weight rejection leaves no partial day write");
        var renewed = repository.SaveBodyWeight(75m, today);
        check(renewed.Kg == expired.Kg && renewed.EnteredOn == today && renewed.Id > expired.Id && renewed.IsCurrent(today), "Same-value explicit weight re-entry resets the 14-day period");
        var partial = repository.Save(preserved with { Workouts = [savedWalk, reps] });
        var totals = repository.LoadEnergyTotals(today, today);
        check(totals.Workouts == 2 && totals.Calculated == 1 && totals.Kcal == 133m, "Energy totals exclude missing calculations and report included count");
        var rawA = savedWalk with { Id = Guid.NewGuid(), DurationSeconds = 4, Energy = null };
        rawA = rawA with { Energy = WorkoutEnergy.Calculate(rawA, input, renewed, today).Snapshot };
        var rawB = rawA with { Id = Guid.NewGuid() };
        var rounded = repository.Save(partial with { Workouts = [rawA, rawB] });
        var rawTotal = repository.LoadEnergyTotals(today, today).Kcal!.Value;
        check(rawTotal == rawA.Energy!.Kcal + rawB.Energy!.Kcal
            && EnergySnapshot.Display(rawTotal) != EnergySnapshot.Display(rawA.Energy.Kcal) + EnergySnapshot.Display(rawB.Energy.Kcal), "Totals add raw kcal before a single final rounding");
        Expect<ArgumentException>(() => repository.Save(rounded with { Workouts = [rawA with { Energy = rawA.Energy with { Kcal = 999m } }] }), "Inconsistent energy snapshot rejected atomically");
        repository.Save(rounded with { Workouts = [] });
        check(repository.LoadEnergyTotals(today, today) == new EnergyTotals(0, 0, null), "Deleting workouts removes energy and empty totals remain null");

        var v2Path = Path.Combine(directory, "version-two.db");
        using (var connection = new SqliteConnection($"Data Source={v2Path};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = """
                CREATE TABLE days(date TEXT PRIMARY KEY,revision INTEGER NOT NULL);
                CREATE TABLE plans(id TEXT PRIMARY KEY,date TEXT NOT NULL REFERENCES days(date),position INTEGER NOT NULL,title TEXT NOT NULL,note TEXT NOT NULL,done INTEGER NOT NULL,start_time TEXT,end_time TEXT);
                CREATE TABLE workouts(id TEXT PRIMARY KEY,date TEXT NOT NULL REFERENCES days(date),position INTEGER NOT NULL,exercise_id TEXT NOT NULL,duration_seconds INTEGER,quantity INTEGER,note TEXT NOT NULL);
                INSERT INTO days VALUES('2026-10-15',4);
                INSERT INTO workouts VALUES('85b8b242-c690-415d-a0f7-cb5af6791579','2026-10-15',0,'stairs',NULL,20,'이전 층수 기록');
                PRAGMA user_version=2;
                """; command.ExecuteNonQuery();
        }
        var v2 = new PlanRepository(v2Path).Load(new DateOnly(2026, 10, 15));
        check(v2.Revision == 4 && v2.Workouts.Single().Quantity == 20 && v2.Workouts.Single().Energy is null && v2.Workouts.Single().Note == "이전 층수 기록", "Schema v2 upgrade preserves quantity-only workouts without inventing kcal");
    }
}
