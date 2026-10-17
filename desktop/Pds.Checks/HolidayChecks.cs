using Pds.Core;

static class HolidayChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(KoreanHolidays.InYear(2025).Count == 19 && KoreanHolidays.InYear(2026).Count == 22 && KoreanHolidays.InYear(2027).Count == 24,
            "Verified Korean holiday snapshots contain 19, 22 and 24 unique dates");
        check(KoreanHolidays.On(new(2025, 5, 5))?.Names.Count == 2 && KoreanHolidays.On(new(2025, 5, 6))?.Label.Contains("대체공휴일") == true,
            "Overlapping Children's Day and Buddha birthday keep both names and observed holiday");
        check(KoreanHolidays.On(new(2025, 1, 27))?.Label == "임시공휴일" && KoreanHolidays.On(new(2025, 6, 3))?.Label.Contains("대통령선거") == true,
            "Government-designated 2025 temporary and election holidays are included");
        check(KoreanHolidays.On(new(2025, 5, 1)) is null && KoreanHolidays.On(new(2025, 7, 17)) is null
            && KoreanHolidays.On(new(2026, 5, 1))?.Label == "노동절" && KoreanHolidays.On(new(2026, 7, 17))?.Label == "제헌절",
            "2026 Labor and Constitution holiday designation does not rewrite 2025 history");
        check(KoreanHolidays.On(new(2026, 3, 2))?.Label.Contains("삼일절") == true && KoreanHolidays.On(new(2026, 10, 5))?.Label.Contains("개천절") == true,
            "2026 March and October substitute holidays are present");
        check(KoreanHolidays.On(new(2026, 9, 24)) is not null && KoreanHolidays.On(new(2026, 9, 26)) is not null
            && KoreanHolidays.On(new(2026, 9, 28)) is null && KoreanHolidays.On(new(2026, 6, 8)) is null,
            "Saturday Chuseok and Memorial Day do not invent Monday substitute holidays");
        check(KoreanHolidays.On(new(2026, 6, 3))?.Label.Contains("지방선거") == true,
            "2026 nationwide local election appears on the correct date");
        check(KoreanHolidays.On(new(2027, 5, 3))?.Label.Contains("노동절") == true && KoreanHolidays.On(new(2027, 7, 19))?.Label.Contains("제헌절") == true,
            "2027 observed Labor and Constitution dates follow published calendar");
        check(KoreanHolidays.On(new(2027, 2, 9))?.Label.Contains("설날") == true && KoreanHolidays.On(new(2027, 12, 27))?.Label.Contains("성탄절") == true,
            "2027 lunar New Year and Christmas observed dates are included");
        check(KoreanHolidays.On(new(2026, 5, 8)) is null && KoreanHolidays.On(new(2026, 10, 1)) is null,
            "Ordinary anniversaries and Armed Forces Day are not mislabeled national holidays");
        check(!KoreanHolidays.Covers(2028) && KoreanHolidays.InYear(2028).Count == 0 && KoreanHolidays.Covers(2027),
            "Unverified holiday years are explicit rather than inferred from current rules");
    }
}
