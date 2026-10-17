using System.Globalization;

namespace Pds.Core;

public sealed record AnniversaryOrigin(int Year, int Month, int Day, AnniversaryCalendar Calendar, bool LeapMonth = false)
{
    public string Label => $"입력: {(Calendar == AnniversaryCalendar.Solar ? "양력" : "음력")} {Year}년 {(LeapMonth ? "윤" : "")}{Month}월 {Day}일";
}

public static class AnniversaryOrigins
{
    public static DateOnly ToSolar(AnniversaryOrigin origin)
    {
        if (!Enum.IsDefined(origin.Calendar) || origin.Year is < 1 or > 9999 || origin.Month is < 1 or > 12 || origin.Day < 1)
            throw new ArgumentException("연도·월·일과 양력·음력을 확인해 주세요.");
        if (origin.Calendar == AnniversaryCalendar.Solar)
        {
            if (origin.LeapMonth || origin.Day > DateTime.DaysInMonth(origin.Year, origin.Month))
                throw new ArgumentException("입력한 연도에 없는 양력 날짜예요. 생년월일을 확인해 주세요.");
            return new(origin.Year, origin.Month, origin.Day);
        }
        if (origin.Year is < AnniversaryDates.MinLunarYear or > AnniversaryDates.MaxLunarYear)
            throw new ArgumentException("음력 원본 날짜는 1900~2050년을 입력해 주세요.");
        var calendar = new KoreanLunisolarCalendar(); var leap = calendar.GetLeapMonth(origin.Year);
        var month = origin.Month;
        if (origin.LeapMonth)
        {
            if (leap != month + 1) throw new ArgumentException("입력한 연도에는 해당 윤달이 없어요.");
            month = leap;
        }
        else if (leap > 0 && month >= leap) month++;
        if (origin.Day > calendar.GetDaysInMonth(origin.Year, month))
            throw new ArgumentException("입력한 연도·월에는 해당 음력 날짜가 없어요.");
        return DateOnly.FromDateTime(calendar.ToDateTime(origin.Year, month, origin.Day, 0, 0, 0, 0));
    }
    public static IReadOnlyList<AnniversaryOrigin> Candidates(int year, int month, int day, AnniversaryCalendar calendar)
    {
        if (calendar == AnniversaryCalendar.Solar)
        {
            var solar = new AnniversaryOrigin(year, month, day, calendar); ToSolar(solar); return [solar];
        }
        if (calendar != AnniversaryCalendar.Lunar) throw new ArgumentException("날짜 기준을 선택해 주세요.");
        var candidates = new List<AnniversaryOrigin>();
        foreach (var leap in new[] { false, true })
        {
            var origin = new AnniversaryOrigin(year, month, day, calendar, leap);
            try { ToSolar(origin); candidates.Add(origin); } catch (ArgumentException) { }
        }
        if (candidates.Count == 0) throw new ArgumentException("입력한 연도·월·일에 해당하는 날짜가 없어요. 음력은 1900~2050년을 지원해요.");
        return candidates;
    }
    public static Anniversary Apply(Anniversary entry, AnniversaryOrigin origin, AnniversaryCalendar recurringCalendar)
    {
        var solar = ToSolar(origin);
        if (!Enum.IsDefined(recurringCalendar)) throw new ArgumentException("매년 반복할 날짜 기준을 선택해 주세요.");
        if (recurringCalendar == AnniversaryCalendar.Solar)
            return entry with { Origin = origin, Calendar = recurringCalendar, Month = solar.Month, Day = solar.Day, LeapMonth = false };
        var calendar = new KoreanLunisolarCalendar(); var time = solar.ToDateTime(TimeOnly.MinValue);
        if (time < calendar.MinSupportedDateTime || time > calendar.MaxSupportedDateTime)
            throw new ArgumentException("이 생년월일은 음력 환산 지원 범위 밖이에요.");
        var year = calendar.GetYear(time); var month = calendar.GetMonth(time); var leap = calendar.GetLeapMonth(year);
        if (year is < AnniversaryDates.MinLunarYear or > AnniversaryDates.MaxLunarYear)
            throw new ArgumentException("음력 자동 환산은 음력 1900~2050년 범위에서 지원해요.");
        return entry with { Origin = origin, Calendar = recurringCalendar, Month = leap > 0 && month >= leap ? month - 1 : month,
            Day = calendar.GetDayOfMonth(time), LeapMonth = leap > 0 && month == leap };
    }
}
