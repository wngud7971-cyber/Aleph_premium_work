namespace Pds.Core;

public sealed record DayReview(string Good, string Difficult, string NextAction)
{
    public void Validate()
    {
        if (Good.Length > 2000 || Difficult.Length > 2000 || NextAction.Length > 2000)
            throw new ArgumentException("회고는 각 칸에 2,000자까지 입력할 수 있어요.");
    }
    public DayReview? Normalize()
    {
        var normalized = new DayReview(Good.Trim(), Difficult.Trim(), NextAction.Trim());
        return string.IsNullOrWhiteSpace(normalized.Good) && string.IsNullOrWhiteSpace(normalized.Difficult)
            && string.IsNullOrWhiteSpace(normalized.NextAction) ? null : normalized;
    }
}
