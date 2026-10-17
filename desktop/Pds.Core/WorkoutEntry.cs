namespace Pds.Core;

public enum WorkoutInputKind { Time, Repetitions, Floors }
public sealed record Exercise(string Id, string Category, string Name, WorkoutInputKind Input)
{
    public override string ToString() => Name;
}
public static class ExerciseCatalog
{
    public static IReadOnlyList<string> Categories { get; } = Array.AsReadOnly(new[] { "걷기·계단", "러닝", "사이클", "맨몸운동", "구기·라켓" });
    public static IReadOnlyList<Exercise> All { get; } = Array.AsReadOnly(new Exercise[]
    {
        new("walking", "걷기·계단", "걷기", WorkoutInputKind.Time),
        new("stairs", "걷기·계단", "계단 걷기", WorkoutInputKind.Floors),
        new("hiking", "걷기·계단", "등산·하이킹", WorkoutInputKind.Time),
        new("running", "러닝", "야외 러닝", WorkoutInputKind.Time),
        new("treadmill", "러닝", "러닝머신", WorkoutInputKind.Time),
        new("cycling-indoor", "사이클", "실내 자전거", WorkoutInputKind.Time),
        new("cycling-outdoor", "사이클", "실외 자전거", WorkoutInputKind.Time),
        new("pushups", "맨몸운동", "팔굽혀펴기", WorkoutInputKind.Repetitions),
        new("situps", "맨몸운동", "윗몸 일으키기", WorkoutInputKind.Repetitions),
        new("plank", "맨몸운동", "플랭크", WorkoutInputKind.Time),
        new("squats", "맨몸운동", "스쿼트", WorkoutInputKind.Repetitions),
        new("lunges", "맨몸운동", "런지", WorkoutInputKind.Repetitions),
        new("burpees", "맨몸운동", "버피", WorkoutInputKind.Repetitions),
        new("pullups", "맨몸운동", "턱걸이", WorkoutInputKind.Repetitions),
        new("jump-rope", "맨몸운동", "줄넘기", WorkoutInputKind.Repetitions),
        new("futsal", "구기·라켓", "풋살", WorkoutInputKind.Time),
        new("soccer", "구기·라켓", "축구", WorkoutInputKind.Time),
        new("basketball", "구기·라켓", "농구", WorkoutInputKind.Time),
        new("badminton", "구기·라켓", "배드민턴", WorkoutInputKind.Time),
        new("tennis", "구기·라켓", "테니스", WorkoutInputKind.Time),
        new("table-tennis", "구기·라켓", "탁구", WorkoutInputKind.Time),
        new("squash", "구기·라켓", "스쿼시", WorkoutInputKind.Time)
    });
    public static Exercise Get(string id) => All.FirstOrDefault(e => e.Id == id)
        ?? throw new ArgumentException("운동 종목을 선택해 주세요.");
}

// Quantity-only records do not manufacture durations or calories.
public sealed record WorkoutEntry(Guid Id, string ExerciseId, int? DurationSeconds, int? Quantity, string Note)
{
    public int? ActiveSeconds { get; init; }
    public EnergyInput? Calculation { get; init; }
    public EnergySnapshot? Energy { get; init; }
    public static WorkoutEntry Create(string exerciseId, int? durationSeconds, int? quantity, string note = "")
        => new(Guid.NewGuid(), exerciseId, durationSeconds, quantity, note.Trim());
    public void Validate()
    {
        var exercise = ExerciseCatalog.Get(ExerciseId);
        if (Id == Guid.Empty) throw new ArgumentException("운동 기록 ID가 올바르지 않아요.");
        if (Note.Length > 2000) throw new ArgumentException("메모는 2,000자까지 입력할 수 있어요.");
        if (DurationSeconds is <= 0 or > 86400) throw new ArgumentException("운동 시간은 0초보다 크고 24시간 이하여야 해요.");
        if (Quantity is <= 0) throw new ArgumentException("횟수·층수는 1 이상의 정수로 입력해 주세요.");
        if (exercise.Input == WorkoutInputKind.Time && (DurationSeconds is null || Quantity is not null))
            throw new ArgumentException("이 종목은 운동 시간을 입력해 주세요.");
        if (exercise.Input == WorkoutInputKind.Floors && Quantity is null)
            throw new ArgumentException("계단은 오른 층수를 입력해 주세요.");
        if (exercise.Input == WorkoutInputKind.Repetitions && Quantity is null && DurationSeconds is null)
            throw new ArgumentException("횟수 또는 실제 수행 시간을 입력해 주세요.");
        if (ActiveSeconds is int active && (ExerciseId != "futsal" || active <= 0 || DurationSeconds is null || active > DurationSeconds))
            throw new ArgumentException("풋살 경기 시간은 0보다 크고 총 참여 시간 이하여야 해요.");
        Calculation?.Validate();
        Energy?.Validate(this);
    }
}
