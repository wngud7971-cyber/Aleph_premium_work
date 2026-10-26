using System.Globalization;
using System.Text.RegularExpressions;

namespace Pds.Core;

public enum MoneyKind { Expense, Income }

public sealed record MoneyEntry(Guid Id, MoneyKind Kind, long Won, string Category, string Note)
{
    public string Title { get; init; } = "";
    public bool IsFixed { get; init; }
    public bool RepeatMonthly { get; init; }
    public Guid? FixedCostId { get; init; }
    public bool IsSaving => Kind == MoneyKind.Expense && Category == "적금·저축";
    public const long MaxWon = 999_999_999_999;
    public static IReadOnlyList<string> ExpenseCategories { get; } = Array.AsReadOnly(new[] { "식비·카페", "교통", "주거·생활", "쇼핑", "건강·운동", "문화·여가", "교육", "통신", "구독비", "보험", "적금·저축", "경조사", "기타 지출" });
    public static IReadOnlyList<string> IncomeCategories { get; } = Array.AsReadOnly(new[] { "급여", "용돈", "부수입", "기타 수입" });
    public static IReadOnlyList<string> Categories(MoneyKind kind) => kind == MoneyKind.Income ? IncomeCategories : ExpenseCategories;
    public static MoneyEntry Create(MoneyKind kind, long won, string category, string note = "")
        => new(Guid.NewGuid(), kind, won, category.Trim(), note.Trim());
    public static long ParseWon(string input)
    {
        var text = input.Trim();
        if (!Regex.IsMatch(text, @"^(?:[0-9]+|[0-9]{1,3}(?:,[0-9]{3})+)$", RegexOptions.CultureInvariant)
            || !long.TryParse(text.Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var won)
            || won < 1 || won > MaxWon)
            throw new ArgumentException("금액은 1~999,999,999,999원 사이의 정수로 입력해 주세요. 예: 12,000");
        return won;
    }
    public void Validate()
    {
        if (Id == Guid.Empty || !Enum.IsDefined(Kind) || Won < 1 || Won > MaxWon)
            throw new ArgumentException("수입·지출의 구분과 금액을 확인해 주세요.");
        if (Category is null || !Categories(Kind).Contains(Category)) throw new ArgumentException("수입·지출에 맞는 소비 유형을 선택해 주세요.");
        if (Title is null || Title.Length>200 || IsFixed && string.IsNullOrWhiteSpace(Title))
            throw new ArgumentException("고정 지출은 내역명을 1~200자로 입력해 주세요.");
        if (IsFixed && Kind!=MoneyKind.Expense || RepeatMonthly && !IsFixed || FixedCostId is not null && (!IsFixed || FixedCostId==Guid.Empty)
            || RepeatMonthly && FixedCostId is Guid ruleId && ruleId!=Id)
            throw new ArgumentException("고정 지출과 매월 반복 설정을 확인해 주세요.");
        if (Note is null || Note.Length > 2000) throw new ArgumentException("가계부 메모는 2,000자까지 입력할 수 있어요.");
    }
}

public sealed record StudyEntry(Guid Id, string Title, string Content, string Summary)
{
    public int? DurationMinutes { get; init; }
    public static int? ParseDuration(string hours, string minutes)
    {
        hours=hours.Trim(); minutes=minutes.Trim();
        if(hours.Length==0 && minutes.Length==0)return null;
        static int Number(string value)=>value.Length==0?0:
            Regex.IsMatch(value,@"^[0-9]+$",RegexOptions.CultureInvariant) && int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var n)?n:-1;
        var h=Number(hours);var m=Number(minutes);
        if(h is <0 or >24 || m is <0 or >59 || h*60+m is <1 or >1440)
            throw new ArgumentException("학습 시간은 1분~24시간으로 입력해 주세요. 분 칸은 0~59이며, 미입력도 가능해요.");
        return h*60+m;
    }
    public static string DurationLabel(int? minutes)=>minutes is int n?$"{n/60}시간 {n%60}분":"시간 미입력";
    public IReadOnlyList<StudyAttachment> Attachments { get; init; } = [];
    public bool Equals(StudyEntry? other) => other is not null && Id==other.Id && Title==other.Title && Content==other.Content
        && Summary==other.Summary && DurationMinutes==other.DurationMinutes && Attachments.SequenceEqual(other.Attachments);
    public override int GetHashCode()
    {
        var hash=new HashCode(); hash.Add(Id); hash.Add(Title); hash.Add(Content); hash.Add(Summary); hash.Add(DurationMinutes);
        foreach (var file in Attachments) hash.Add(file); return hash.ToHashCode();
    }
    public static StudyEntry Create(string title, string content = "", string summary = "")
        => new(Guid.NewGuid(), title.Trim(), content.Trim(), summary.Trim());
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Title) || Title.Trim().Length > 200)
            throw new ArgumentException("학습 제목은 1~200자로 입력해 주세요.");
        if(DurationMinutes is <1 or >1440)throw new ArgumentException("학습 시간은 1분~24시간으로 입력하거나 비워 주세요.");
        if (Content is null || Content.Length > 10000) throw new ArgumentException("학습 내용은 10,000자까지 입력할 수 있어요.");
        if (Summary is null || Summary.Length > 2000) throw new ArgumentException("핵심 요약은 2,000자까지 입력할 수 있어요.");
        if (Attachments is null || Attachments.Count>StudyAttachment.MaxPerStudy || Attachments.Select(a=>a.Id).Distinct().Count()!=Attachments.Count)
            throw new ArgumentException("학습 하나에 서로 다른 파일을 최대 10개 첨부할 수 있어요.");
        foreach (var file in Attachments) file.Validate();
    }
}

public sealed record MoneyCategoryTotal(string Category, long Won, int Records);
public sealed record JournalStatistics(DateRange Range, long Income, long Expense, int MoneyRecords, int Studies,
    IReadOnlyList<MoneyCategoryTotal> Categories)
{
    public long Savings { get; init; }
    public long Consumption => Expense - Savings;
    public long FixedTotal { get; init; }
    public int FixedRecords { get; init; }
    public IReadOnlyList<FixedCostLine> FixedLines { get; init; } = [];
    public long Balance => Income - Expense;
}
