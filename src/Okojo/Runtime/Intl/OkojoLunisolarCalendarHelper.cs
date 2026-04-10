using System.Globalization;

namespace Okojo.Runtime.Intl;

internal static class OkojoLunisolarCalendarHelper
{
    private static ChineseLunisolarCalendar? chineseCalendar;
    private static KoreanLunisolarCalendar? koreanCalendar;

    private static readonly string[] HeavenlyStems =
    [
        "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"
    ];

    private static readonly string[] EarthlyBranches =
    [
        "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥"
    ];

    private static ChineseLunisolarCalendar ChineseCalendar => chineseCalendar ??= new();
    private static KoreanLunisolarCalendar KoreanCalendar => koreanCalendar ??= new();

    internal static LunisolarDate GetChineseDate(DateTime dateTime)
    {
        if (dateTime < ChineseCalendar.MinSupportedDateTime || dateTime > ChineseCalendar.MaxSupportedDateTime)
            return GetLunisolarDate(dateTime, KoreanCalendar);

        return GetLunisolarDate(dateTime, ChineseCalendar);
    }

    internal static LunisolarDate GetDangiDate(DateTime dateTime)
    {
        return GetLunisolarDate(dateTime, KoreanCalendar);
    }

    private static LunisolarDate GetLunisolarDate(DateTime dateTime, EastAsianLunisolarCalendar calendar)
    {
        if (dateTime < calendar.MinSupportedDateTime)
            dateTime = calendar.MinSupportedDateTime;
        else if (dateTime > calendar.MaxSupportedDateTime)
            dateTime = calendar.MaxSupportedDateTime;

        var year = calendar.GetYear(dateTime);
        var month = calendar.GetMonth(dateTime);
        var day = calendar.GetDayOfMonth(dateTime);

        var leapMonth = calendar.GetLeapMonth(year);
        var isLeapMonth = leapMonth > 0 && month == leapMonth;
        var displayMonth = month;
        if (leapMonth > 0 && month >= leapMonth)
            displayMonth = month - 1;

        var relatedYear = year;
        var sexagenaryYear = calendar.GetSexagenaryYear(dateTime);
        var yearName = GetSexagenaryYearName(sexagenaryYear);

        return new(relatedYear, yearName, displayMonth, day, isLeapMonth);
    }

    private static string GetSexagenaryYearName(int sexagenaryYear)
    {
        var index = sexagenaryYear - 1;
        return HeavenlyStems[index % 10] + EarthlyBranches[index % 12];
    }

    internal readonly struct LunisolarDate(int relatedYear, string yearName, int month, int day, bool isLeapMonth)
    {
        internal int RelatedYear { get; } = relatedYear;
        internal string YearName { get; } = yearName;
        internal int Month { get; } = month;
        internal int Day { get; } = day;
        internal bool IsLeapMonth { get; } = isLeapMonth;
    }
}
