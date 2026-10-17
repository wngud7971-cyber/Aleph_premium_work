using System.Globalization;
using System.Text.Json.Serialization;

namespace Pds.Core;

public enum AnniversaryKind { Birthday, Memorial, Other }
public enum AnniversaryCalendar { Solar, Lunar }
public enum MissingLeapMonth { RegularMonth, SkipYear }
public enum MissingAnniversaryDay { LastDayOfMonth, SkipYear }

public sealed record Anniversary(Guid Id, long Revision, string Name, AnniversaryKind Kind,
    AnniversaryCalendar Calendar, int Month, int Day, bool LeapMonth = false,
    MissingLeapMonth LeapPolicy = MissingLeapMonth.SkipYear,
    MissingAnniversaryDay DayPolicy = MissingAnniversaryDay.LastDayOfMonth, string Note = "")
{
    public AnniversaryOrigin? Origin { get; init; }
    [JsonIgnore] public string DisplayLabel => $"{KindLabel}: {Name}";
    public static Anniversary Create(string name, AnniversaryKind kind, AnniversaryCalendar calendar, int month, int day)
        => new(Guid.NewGuid(), 0, name, kind, calendar, month, day);
    public string KindLabel => Kind switch { AnniversaryKind.Birthday => "생일", AnniversaryKind.Memorial => "기일", _ => "기념일" };
    public string DateLabel => $"{(Calendar == AnniversaryCalendar.Lunar ? "음력" : "양력")} {(LeapMonth ? "윤" : "")}{Month}월 {Day}일 · 매년";
    public void Validate()
    {
        if (Id == Guid.Empty || Revision < 0 || Revision == long.MaxValue || string.IsNullOrWhiteSpace(Name) || Name.Length > 100
            || Note is null || Note.Length > 1000 || !Enum.IsDefined(Kind) || !Enum.IsDefined(Calendar)
            || !Enum.IsDefined(LeapPolicy) || !Enum.IsDefined(DayPolicy) || Month is < 1 or > 12 || Day < 1
            || Calendar == AnniversaryCalendar.Solar && (LeapMonth || Day > DateTime.DaysInMonth(2000, Month))
            || Calendar == AnniversaryCalendar.Lunar && Day > 30)
            throw new ArgumentException("이름과 날짜를 확인해 주세요. 양력은 실제 월일, 음력은 1~12월·1~30일을 입력해요.");
        if (Origin is not null)
        {
            var expected = AnniversaryOrigins.Apply(this, Origin, Calendar);
            if (Month != expected.Month || Day != expected.Day || LeapMonth != expected.LeapMonth)
                throw new ArgumentException("원본 생년월일과 매년 표시할 날짜가 맞지 않아요.");
        }
    }
}

public sealed record AnniversaryOccurrence(Anniversary Entry, DateOnly Date, int SourceYear, bool Adjusted);

public static class AnniversaryDates
{
    public const int MinLunarYear = 1900, MaxLunarYear = 2050;
    public static AnniversaryOccurrence? InYear(Anniversary entry, int year)
    {
        entry.Validate();
        if (year is < 1 or > 9999) throw new ArgumentOutOfRangeException(nameof(year));
        var month = entry.Month; var adjusted = false;
        if (entry.Calendar == AnniversaryCalendar.Solar)
        {
            var days = DateTime.DaysInMonth(year, month);
            if (entry.Day > days && entry.DayPolicy == MissingAnniversaryDay.SkipYear) return null;
            return Occurrence(entry, new(year, month, Math.Min(entry.Day, days)), year, entry.Day > days);
        }
        if (year is < MinLunarYear or > MaxLunarYear)
            throw new ArgumentOutOfRangeException(nameof(year), "음력 자동 환산은 음력 1900~2050년을 지원해요.");
        var calendar = new KoreanLunisolarCalendar();
        // .NET inserts the leap month into the numbered sequence (1..13).
        var leap = calendar.GetLeapMonth(year);
        if (entry.LeapMonth)
        {
            if (leap == month + 1) month = leap;
            else if (entry.LeapPolicy == MissingLeapMonth.SkipYear) return null;
            else { adjusted = true; if (leap > 0 && month >= leap) month++; }
        }
        else if (leap > 0 && month >= leap) month++;
        var monthDays = calendar.GetDaysInMonth(year, month);
        if (entry.Day > monthDays && entry.DayPolicy == MissingAnniversaryDay.SkipYear) return null;
        var date = DateOnly.FromDateTime(calendar.ToDateTime(year, month, Math.Min(entry.Day, monthDays), 0, 0, 0, 0));
        return Occurrence(entry, date, year, adjusted || entry.Day > monthDays);
    }

    private static AnniversaryOccurrence? Occurrence(Anniversary entry, DateOnly date, int year, bool adjusted)
        => entry.Origin is not null && date < AnniversaryOrigins.ToSolar(entry.Origin) ? null : new(entry, date, year, adjusted);

    public static IReadOnlyList<AnniversaryOccurrence> Between(IEnumerable<Anniversary> entries, DateOnly from, DateOnly to)
    {
        new DateRange(from, to).Validate();
        var results = new List<AnniversaryOccurrence>();
        foreach (var entry in entries)
        {
            var start = entry.Calendar == AnniversaryCalendar.Lunar ? Math.Max(MinLunarYear, from.Year - 1) : from.Year;
            var end = entry.Calendar == AnniversaryCalendar.Lunar ? Math.Min(MaxLunarYear, to.Year) : to.Year;
            for (var year = start; year <= end; year++)
                if (InYear(entry, year) is { } occurrence && occurrence.Date >= from && occurrence.Date <= to) results.Add(occurrence);
        }
        return results.OrderBy(o => o.Date).ThenBy(o => o.Entry.Name, StringComparer.Ordinal).ToArray();
    }
}
