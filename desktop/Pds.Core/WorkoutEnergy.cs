namespace Pds.Core;

public sealed record BodyWeight(long Id, decimal Kg, DateOnly EnteredOn)
{
    public DateOnly DueOn => EnteredOn.AddDays(14);
    public bool IsCurrent(DateOnly today) => EnteredOn <= today && today < DueOn;
    public static void Validate(decimal kg)
    {
        if (kg <= 0 || decimal.Round(kg, 1) != kg) throw new ArgumentException("체중은 0보다 큰 kg 값으로 소수 첫째 자리까지 입력해 주세요.");
    }
}

public sealed record EnergyPreset(string Code, string Label, decimal Met, string SourcePath, string[] Exercises,
    decimal? Minimum = null, decimal? Maximum = null, string? DetailUnit = null)
{
    public string SourceUrl => "https://pacompendium.com/" + SourcePath + "/";
    public override string ToString() => Label;
}

public static class EnergyCatalog
{
    public const string Version = "2024 Adult Compendium; verified 2026-10-16";
    // These are discrete published conditions. Gaps are intentionally not interpolated.
    public static IReadOnlyList<EnergyPreset> All { get; } = Array.AsReadOnly(new EnergyPreset[]
    {
        new("17152", "느린 평지 걷기 · 2.0–2.4 mph", 2.8m, "walking", ["walking"], 2m, 2.4m, "mph"),
        new("17190", "보통 평지 걷기 · 2.8–3.4 mph", 3.8m, "walking", ["walking"], 2.8m, 3.4m, "mph"),
        new("17200", "빠른 평지 걷기 · 3.5–3.9 mph", 4.8m, "walking", ["walking"], 3.5m, 3.9m, "mph"),
        new("17133", "느린 계단 오르기", 4.5m, "walking", ["stairs"]),
        new("17131", "일반 계단 오르기", 6.8m, "walking", ["stairs"]),
        new("17134", "빠른 오르기 · 한 계단씩", 9.3m, "walking", ["stairs"]),
        new("17070", "계단 내려오기", 3.5m, "walking", ["stairs"]),
        new("17138", "계단 오르내리기", 7.5m, "walking", ["stairs"]),
        new("17081", "짐 없이 들길·언덕 천천히", 3.8m, "walking", ["hiking"]),
        new("17082", "짐 없이 들길·언덕 보통 속도", 5.3m, "walking", ["hiking"]),
        new("17080", "크로스컨트리 하이킹", 6m, "walking", ["hiking"]),
        new("12020", "일반 조깅 · 스스로 정한 페이스", 7.5m, "running", ["running"]),
        new("12030", "달리기 · 5.0–5.2 mph", 8.5m, "running", ["running"], 5m, 5.2m, "mph"),
        new("12045", "달리기 · 5.5–5.8 mph", 9m, "running", ["running"], 5.5m, 5.8m, "mph"),
        new("12050", "달리기 · 6.0–6.3 mph", 9.3m, "running", ["running"], 6m, 6.3m, "mph"),
        new("17349", "러닝머신 평지 걷기 · 2.0–2.4 mph", 3m, "walking", ["treadmill"], 2m, 2.4m, "mph"),
        new("17355", "러닝머신 평지 걷기 · 3.0–3.4 mph", 3.8m, "walking", ["treadmill"], 3m, 3.4m, "mph"),
        new("17358", "러닝머신 평지 걷기 · 3.5–3.9 mph", 4.8m, "walking", ["treadmill"], 3.5m, 3.9m, "mph"),
        new("01200", "실내 자전거 일반 · 출력 미확인", 6.8m, "bicycling", ["cycling-indoor"]),
        new("01214", "실내 자전거 · 50 W", 4m, "bicycling", ["cycling-indoor"], 50m, 50m, "W"),
        new("01216", "실내 자전거 · 60 W", 5m, "bicycling", ["cycling-indoor"], 60m, 60m, "W"),
        new("01218", "실내 자전거 · 70–80 W", 5.8m, "bicycling", ["cycling-indoor"], 70m, 80m, "W"),
        new("01220", "실내 자전거 · 90–100 W", 6m, "bicycling", ["cycling-indoor"], 90m, 100m, "W"),
        new("01224", "실내 자전거 · 101–125 W", 6.8m, "bicycling", ["cycling-indoor"], 101m, 125m, "W"),
        new("01228", "실내 자전거 · 126–150 W", 8m, "bicycling", ["cycling-indoor"], 126m, 150m, "W"),
        new("01015", "실외 자전거 · 스스로 선택한 가벼운 페이스", 4.3m, "bicycling", ["cycling-outdoor"]),
        new("01016", "실외 자전거 · 스스로 선택한 중간 페이스", 7m, "bicycling", ["cycling-outdoor"]),
        new("01017", "실외 자전거 · 스스로 선택한 강한 페이스", 9m, "bicycling", ["cycling-outdoor"]),
        new("01020", "실외 자전거 · 10.0–11.9 mph", 6.8m, "bicycling", ["cycling-outdoor"], 10m, 11.9m, "mph"),
        new("01030", "실외 자전거 · 12.0–13.9 mph", 8m, "bicycling", ["cycling-outdoor"], 12m, 13.9m, "mph"),
        new("01040", "실외 자전거 · 14.0–15.9 mph", 10m, "bicycling", ["cycling-outdoor"], 14m, 15.9m, "mph"),
        new("02022", "중간 강도 맨몸 체조", 3.8m, "conditioning-exercise", ["pushups", "situps", "pullups", "lunges"]),
        new("02020", "강한 맨몸 체조", 7.5m, "conditioning-exercise", ["pushups", "situps", "pullups", "burpees"]),
        new("02024", "플랭크 포함 가벼운 맨몸 체조", 2.8m, "conditioning-exercise", ["plank"]),
        new("02056", "맨몸 저항 운동 · 일반", 3m, "conditioning-exercise", ["squats", "lunges"]),
        new("02057", "맨몸 저항 운동 · 높은 강도", 6.5m, "conditioning-exercise", ["squats", "lunges"]),
        new("02068", "줄넘기 일반 운동", 11m, "conditioning-exercise", ["jump-rope"]),
        new("15195", "풋살 · 실제 경기 구간", 7.8m, "sports", ["futsal"]),
        new("15610", "축구 · 취미·일반", 7m, "sports", ["soccer"]),
        new("15605", "축구 · 경쟁 경기", 9.5m, "sports", ["soccer"]),
        new("15050", "농구 · 비경기 일반", 6m, "sports", ["basketball"]),
        new("15040", "농구 · 경기", 8m, "sports", ["basketball"]),
        new("15070", "농구 · 슛 연습", 5m, "sports", ["basketball"]),
        new("15030", "배드민턴 · 취미 단식·복식", 5.5m, "sports", ["badminton"]),
        new("15020", "배드민턴 · 경쟁", 7m, "sports", ["badminton"]),
        new("15675", "테니스 · 일반 중간 강도", 6.8m, "sports", ["tennis"]),
        new("15680", "테니스 · 복식 (15680 기준)", 6m, "sports", ["tennis"]),
        new("15690", "테니스 · 단식", 8m, "sports", ["tennis"]),
        new("15660", "탁구", 4m, "sports", ["table-tennis"]),
        new("15652", "스쿼시 · 일반", 7.3m, "sports", ["squash"])
    });
    public static IEnumerable<EnergyPreset> For(string exercise) => All.Where(p => p.Exercises.Contains(exercise));
    public static bool HasDetail(string exercise) => For(exercise).Any(p => p.DetailUnit is not null);
    public static EnergyPreset? Resolve(string exercise, EnergyInput input)
    {
        if (input.Mode == "simple") return For(exercise).SingleOrDefault(p => p.Code == input.PresetCode);
        if (input.DetailValue is not decimal value) return null;
        var unit = exercise == "cycling-indoor" ? "W" : "mph";
        if (unit == "mph") value /= 1.609344m;
        return For(exercise).SingleOrDefault(p => p.DetailUnit == unit && value >= p.Minimum && value <= p.Maximum);
    }
}

public sealed record EnergyInput(string Mode, string? PresetCode, decimal? DetailValue, decimal? WeightKg, bool AdultConfirmed)
{
    public void Validate()
    {
        if (Mode is not ("simple" or "detail")) throw new ArgumentException("계산 입력 방식을 확인해 주세요.");
        if (WeightKg is decimal kg) BodyWeight.Validate(kg);
        if (DetailValue is <= 0) throw new ArgumentException("속도·출력은 0보다 큰 값이어야 해요.");
    }
}
public sealed record EnergySnapshot(string ExerciseId, string Code, string Label, decimal Met, decimal WeightKg,
    DateOnly WeightEnteredOn, int Seconds, decimal Kcal, string SourceUrl, string SourceVersion, string FormulaVersion)
{
    public const string Formula = "MET-kg-hours-v1";
    public static decimal Estimate(decimal met, decimal kg, int seconds)
    {
        try { return checked(met * kg * seconds / 3600m); }
        catch (OverflowException) { throw new ArgumentException("계산 수치가 너무 커요. 체중과 시간을 확인해 주세요."); }
    }
    public static decimal Display(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
    public void Validate(WorkoutEntry workout)
    {
        if (ExerciseId != workout.ExerciseId || Met <= 0 || Seconds <= 0 || Seconds > workout.DurationSeconds
            || workout.DurationSeconds is null || FormulaVersion != Formula || Kcal != Estimate(Met, WeightKg, Seconds)
            || workout.Calculation is null || workout.Calculation.WeightKg != WeightKg || !workout.Calculation.AdultConfirmed
            || (workout.ExerciseId == "futsal" && Seconds != workout.ActiveSeconds)
            || (workout.ExerciseId != "futsal" && Seconds != workout.DurationSeconds)
            || (workout.Calculation.Mode == "simple" && workout.Calculation.PresetCode != Code))
            throw new ArgumentException("kcal 계산 기준과 운동 기록이 맞지 않아요.");
        BodyWeight.Validate(WeightKg);
        if (!Uri.TryCreate(SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "pacompendium.com")
            throw new ArgumentException("kcal 출처를 확인해 주세요.");
    }
}
public sealed record EnergyResult(EnergySnapshot? Snapshot, string Reason);
public static class WorkoutEnergy
{
    public static EnergyResult Calculate(WorkoutEntry workout, EnergyInput input, BodyWeight? profile, DateOnly today)
    {
        input.Validate();
        if (!input.AdultConfirmed) return new(null, "19~59세 성인 기준 적용 여부를 확인해 주세요.");
        if (profile is null) return new(null, "기본 체중을 먼저 등록해 주세요. 운동 기록만 저장할 수 있어요.");
        if (!profile.IsCurrent(today)) return new(null, "체중을 다시 입력해 주세요. 새 kcal 계산은 14일마다 갱신이 필요해요.");
        if (input.WeightKg is not decimal kg) return new(null, "이 운동에 적용할 체중을 입력해 주세요.");
        var preset = EnergyCatalog.Resolve(workout.ExerciseId, input);
        if (preset is null) return new(null, "해당 조건의 계산 기준이 없어요. 기준을 선택하거나 운동 기록만 저장하세요.");
        var seconds = workout.ExerciseId == "futsal" ? workout.ActiveSeconds : workout.DurationSeconds;
        if (seconds is null) return new(null, workout.ExerciseId == "futsal" ? "실제 경기 시간을 입력·확인해 주세요." : "kcal에는 실제 수행 시간이 필요해요. 횟수·층수만으로 계산하지 않아요.");
        if (seconds <= 0 || workout.DurationSeconds is null || seconds > workout.DurationSeconds) throw new ArgumentException("계산 시간은 0보다 크고 총 운동 시간 이하여야 해요.");
        return new(new(workout.ExerciseId, preset.Code, preset.Label, preset.Met, kg, profile.EnteredOn, seconds.Value,
            EnergySnapshot.Estimate(preset.Met, kg, seconds.Value), preset.SourceUrl, EnergyCatalog.Version, EnergySnapshot.Formula), "");
    }
    public static (int TotalSeconds, int ActiveSeconds) FutsalPattern(int cycles)
    {
        if (cycles is < 1 or > 72) throw new ArgumentException("완료한 20분 주기 횟수는 1~72로 입력해 주세요.");
        return (cycles * 20 * 60, cycles * 15 * 60);
    }
}
