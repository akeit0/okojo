using System.Globalization;

namespace Okojo.Globalization;

public static class LunisolarCalendar
{
    private static ChineseLunisolarCalendar? chineseCalendar;
    private static KoreanLunisolarCalendar? koreanCalendar;

    private static readonly string[] HeavenlyStems =
    [
        "甲",
        "乙",
        "丙",
        "丁",
        "戊",
        "己",
        "庚",
        "辛",
        "壬",
        "癸",
    ];

    private static readonly string[] EarthlyBranches =
    [
        "子",
        "丑",
        "寅",
        "卯",
        "辰",
        "巳",
        "午",
        "未",
        "申",
        "酉",
        "戌",
        "亥",
    ];

    private static ChineseLunisolarCalendar ChineseCalendar => chineseCalendar ??= new();
    private static KoreanLunisolarCalendar KoreanCalendar => koreanCalendar ??= new();

    /// <summary>Returns the Chinese lunisolar date for a date-time.</summary>
    public static LunisolarDate GetChineseDate(DateTime dateTime)
    {
        if (
            dateTime < ChineseCalendar.MinSupportedDateTime
            || dateTime > ChineseCalendar.MaxSupportedDateTime
        )
            return GetLunisolarDate(dateTime, KoreanCalendar);

        return GetLunisolarDate(dateTime, ChineseCalendar);
    }

    /// <summary>Returns the Dangi (Korean) lunisolar date for a date-time.</summary>
    public static LunisolarDate GetDangiDate(DateTime dateTime)
    {
        return GetLunisolarDate(dateTime, KoreanCalendar);
    }

    private static LunisolarDate GetLunisolarDate(
        DateTime dateTime,
        EastAsianLunisolarCalendar calendar
    )
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

    /// <summary>A lunisolar calendar date (related year, sexagenary year name, month, day, leap-month flag).</summary>
    public readonly struct LunisolarDate(
        int relatedYear,
        string yearName,
        int month,
        int day,
        bool isLeapMonth
    )
    {
        /// <summary>The related (lunisolar) year.</summary>
        public int RelatedYear { get; } = relatedYear;

        /// <summary>The sexagenary year name (e.g. <c>甲子</c>).</summary>
        public string YearName { get; } = yearName;

        /// <summary>The month number.</summary>
        public int Month { get; } = month;

        /// <summary>The day of month.</summary>
        public int Day { get; } = day;

        /// <summary>True if the month is a leap month.</summary>
        public bool IsLeapMonth { get; } = isLeapMonth;
    }
}
