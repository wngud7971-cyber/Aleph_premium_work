using System.Globalization;

namespace Pds.Core;

public sealed record PlanEntry(Guid Id, string Title, string Note, bool Done, string? Start, string? End)
{
    public DateOnly? ActualDate { get; init; }
    public static PlanEntry Create(string title, string note = "", string? start = null, string? end = null)
        => new(Guid.NewGuid(), title.Trim(), note.Trim(), false, start, end);

    public void Validate()
    {
        if (!Done && ActualDate is not null) throw new ArgumentException("미완료 계획에는 실행 날짜를 지정할 수 없어요.");
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Title) || Title.Trim().Length > 200)
            throw new ArgumentException("계획 제목은 1~200자로 입력해 주세요.");
        if (Note.Length > 2000) throw new ArgumentException("메모는 2,000자까지 입력할 수 있어요.");
        if ((Start is null) != (End is null)) throw new ArgumentException("시작·종료 시간을 모두 입력해 주세요.");
        if (Start is not null && (!TryTime(Start, out var begin) || !TryTime(End!, out var finish) || begin >= finish))
            throw new ArgumentException("시간은 HH:mm 형식이며 종료가 시작보다 늦어야 해요.");
    }

    private static bool TryTime(string text, out TimeOnly value)
        => TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}

public sealed record DaySnapshot(DateOnly Date, long Revision, IReadOnlyList<PlanEntry> Plans)
{
    public IReadOnlyList<WorkoutEntry> Workouts { get; init; } = [];
    public DayReview? Review { get; init; }
}
public sealed record DayCount(int Plans, int Done)
{
    public int Workouts { get; init; }
    public long WorkoutSeconds { get; init; }
    public int TimedWorkouts { get; init; }
}
public sealed class SaveConflictException() : Exception("다른 작업에서 기록이 변경됐어요. 입력을 보관하고 다시 확인해 주세요.");
