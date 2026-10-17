using System.Collections.Frozen;

namespace Pds.Core;

public sealed record KoreanHoliday(DateOnly Date, IReadOnlyList<string> Names)
{
    public string Label => string.Join(" · ", Names);
}

// Published calendar facts, with later government announcements applied.
// Sources and corrections: desktop/korean-holiday-sources-20261017.md.
public static class KoreanHolidays
{
    public const int FirstYear = 2025, LastYear = 2027;
    public const string Revision = "2026-10-17";
    private static readonly FrozenDictionary<DateOnly, KoreanHoliday> Dates = Create();
    public static bool Covers(int year) => year is >= FirstYear and <= LastYear;
    public static KoreanHoliday? On(DateOnly date) => Dates.GetValueOrDefault(date);
    public static IReadOnlyList<KoreanHoliday> InYear(int year) => Dates.Values.Where(h => h.Date.Year == year).OrderBy(h => h.Date).ToArray();
    private static FrozenDictionary<DateOnly, KoreanHoliday> Create()
    {
        var rows = new List<(DateOnly Date, string Name)>();
        void Add(int year, int month, int day, string name) => rows.Add((new(year, month, day), name));
        void Span(int year, int month, int first, int last, string name)
        { for (var day = first; day <= last; day++) Add(year, month, day, name); }
        foreach (var year in new[] { 2025, 2026, 2027 })
        {
            Add(year, 1, 1, "신정"); Add(year, 3, 1, "삼일절"); Add(year, 5, 5, "어린이날");
            Add(year, 6, 6, "현충일"); Add(year, 8, 15, "광복절"); Add(year, 10, 3, "개천절");
            Add(year, 10, 9, "한글날"); Add(year, 12, 25, "성탄절");
            if (year >= 2026) { Add(year, 5, 1, "노동절"); Add(year, 7, 17, "제헌절"); }
        }
        Add(2025, 1, 27, "임시공휴일"); Span(2025, 1, 28, 30, "설날 연휴");
        Add(2025, 3, 3, "대체공휴일(삼일절)"); Add(2025, 5, 5, "부처님오신날");
        Add(2025, 5, 6, "대체공휴일(부처님오신날)"); Add(2025, 6, 3, "제21대 대통령선거");
        Span(2025, 10, 5, 7, "추석 연휴"); Add(2025, 10, 8, "대체공휴일(추석)");
        Span(2026, 2, 16, 18, "설날 연휴"); Add(2026, 3, 2, "대체공휴일(삼일절)");
        Add(2026, 5, 24, "부처님오신날"); Add(2026, 5, 25, "대체공휴일(부처님오신날)");
        Add(2026, 6, 3, "전국동시지방선거"); Add(2026, 8, 17, "대체공휴일(광복절)");
        Span(2026, 9, 24, 26, "추석 연휴"); Add(2026, 10, 5, "대체공휴일(개천절)");
        Span(2027, 2, 6, 8, "설날 연휴"); Add(2027, 2, 9, "대체공휴일(설날)");
        Add(2027, 5, 3, "대체공휴일(노동절)"); Add(2027, 5, 13, "부처님오신날");
        Add(2027, 7, 19, "대체공휴일(제헌절)"); Add(2027, 8, 16, "대체공휴일(광복절)");
        Span(2027, 9, 14, 16, "추석 연휴"); Add(2027, 10, 4, "대체공휴일(개천절)");
        Add(2027, 10, 11, "대체공휴일(한글날)"); Add(2027, 12, 27, "대체공휴일(성탄절)");
        return rows.GroupBy(r => r.Date).ToFrozenDictionary(g => g.Key, g => new KoreanHoliday(g.Key, Array.AsReadOnly(g.Select(r => r.Name).Distinct().ToArray())));
    }
}
