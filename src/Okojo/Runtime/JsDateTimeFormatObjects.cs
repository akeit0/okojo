using System.Globalization;
using System.Text;
using Okojo.Runtime.Intl;

namespace Okojo.Runtime;

internal sealed class JsDateTimeFormatObject : JsObject
{
    private const long MinNativeEpochMilliseconds = -62135596800000L;
    private const long MaxNativeEpochMilliseconds = 253402300799999L;

    private static readonly Dictionary<string, TimeSpan> KnownTimeZones = new(StringComparer.Ordinal)
    {
        ["Asia/Tokyo"] = TimeSpan.FromHours(9),
        ["Asia/Calcutta"] = TimeSpan.FromHours(5.5),
        ["Asia/Kolkata"] = TimeSpan.FromHours(5.5),
        ["Pacific/Apia"] = TimeSpan.FromHours(13),
        ["America/Los_Angeles"] = TimeSpan.FromHours(-8),
        ["America/Vancouver"] = TimeSpan.FromHours(-8),
        ["Europe/Prague"] = TimeSpan.FromHours(1)
    };

    private JsHostFunction? boundFormat;

    internal JsDateTimeFormatObject(
        JsRealm realm,
        JsObject prototype,
        string locale,
        string calendar,
        string numberingSystem,
        string timeZone,
        bool useDefaultTimeZoneForFormatting,
        string hourCycle,
        bool? hour12,
        string? weekday,
        string? era,
        string? year,
        string? month,
        string? day,
        string? dayPeriod,
        string? hour,
        string? minute,
        string? second,
        int? fractionalSecondDigits,
        string? timeZoneName,
        string? formatMatcher,
        string? dateStyle,
        string? timeStyle,
        CultureInfo cultureInfo) : base(realm)
    {
        Prototype = prototype;
        Locale = locale;
        Calendar = calendar;
        NumberingSystem = numberingSystem;
        TimeZone = timeZone;
        UseDefaultTimeZoneForFormatting = useDefaultTimeZoneForFormatting;
        HourCycle = hourCycle;
        Hour12 = hour12;
        Weekday = weekday;
        Era = era;
        Year = year;
        Month = month;
        Day = day;
        DayPeriod = dayPeriod;
        Hour = hour;
        Minute = minute;
        Second = second;
        FractionalSecondDigits = fractionalSecondDigits;
        TimeZoneName = timeZoneName;
        FormatMatcher = formatMatcher;
        DateStyle = dateStyle;
        TimeStyle = timeStyle;
        CultureInfo = cultureInfo;
    }

    internal string Locale { get; }
    internal string Calendar { get; }
    internal string NumberingSystem { get; }
    internal string TimeZone { get; }
    internal bool UseDefaultTimeZoneForFormatting { get; }
    internal string HourCycle { get; }
    internal bool? Hour12 { get; }
    internal string? Weekday { get; }
    internal string? Era { get; }
    internal string? Year { get; }
    internal string? Month { get; }
    internal string? Day { get; }
    internal string? DayPeriod { get; }
    internal string? Hour { get; }
    internal string? Minute { get; }
    internal string? Second { get; }
    internal int? FractionalSecondDigits { get; }
    internal string? TimeZoneName { get; }
    internal string? FormatMatcher { get; }
    internal string? DateStyle { get; }
    internal string? TimeStyle { get; }
    internal CultureInfo CultureInfo { get; }

    internal JsHostFunction GetOrCreateBoundFormat(JsRealm realm)
    {
        if (boundFormat is not null)
            return boundFormat;

        boundFormat = new(realm, static (in info) =>
        {
            var dateTimeFormat = (JsDateTimeFormatObject)((JsHostFunction)info.Function).UserData!;
            var value = info.Arguments.Length == 0 || info.Arguments[0].IsUndefined
                ? DateTimeOffset.Now.ToUnixTimeMilliseconds()
                : info.Realm.ToNumberSlowPath(info.Arguments[0]);
            return JsValue.FromString(dateTimeFormat.Format(value));
        }, string.Empty, 1)
        {
            UserData = this
        };
        return boundFormat;
    }

    internal string Format(double value)
    {
        var parts = BuildParts(GetDateTimeValue(value));
        var builder = new StringBuilder();
        foreach (var part in parts)
            builder.Append(part.Value);
        return Transliterate(builder.ToString());
    }

    internal JsArray FormatToParts(double value)
    {
        var parts = BuildParts(GetDateTimeValue(value));
        var result = Realm.CreateArrayObject();
        for (uint i = 0; i < parts.Count; i++)
            result.SetElement(i,
                JsValue.FromObject(CreatePartObject(parts[(int)i].Type, Transliterate(parts[(int)i].Value))));
        return result;
    }

    internal string FormatRange(double startValue, double endValue)
    {
        var startDateTime = GetDateTimeValue(startValue);
        var endDateTime = GetDateTimeValue(endValue);
        var startParts = BuildParts(startDateTime);
        var endParts = BuildParts(endDateTime);
        var start = Transliterate(JoinParts(startParts));
        var end = Transliterate(JoinParts(endParts));
        if (string.Equals(start, end, StringComparison.Ordinal))
            return start;

        if (TryCreateCompressedTextMonthRange(startParts, endParts, out var compressedParts))
            return Transliterate(string.Concat(compressedParts.Select(static p => p.Value)));

        return start + " – " + end;
    }

    internal JsArray FormatRangeToParts(double startValue, double endValue)
    {
        var startDateTime = GetDateTimeValue(startValue);
        var endDateTime = GetDateTimeValue(endValue);
        var startSourceParts = BuildParts(startDateTime);
        var endSourceParts = BuildParts(endDateTime);
        var startParts = CreatePartsArray(startSourceParts);
        var endParts = CreatePartsArray(endSourceParts);
        var start = Transliterate(JoinParts(startSourceParts));
        var end = Transliterate(JoinParts(endSourceParts));
        var result = Realm.CreateArrayObject();
        uint index = 0;

        if (string.Equals(start, end, StringComparison.Ordinal))
        {
            AppendRangeParts(result, ref index, startParts, "shared");
            return result;
        }

        if (TryCreateCompressedTextMonthRange(startSourceParts, endSourceParts, out var compressedParts))
        {
            for (var i = 0; i < compressedParts.Count; i++)
            {
                var part = compressedParts[i];
                result.SetElement(index++,
                    JsValue.FromObject(CreateRangePartObject(part.Type, Transliterate(part.Value), part.Source!)));
            }

            return result;
        }

        AppendRangeParts(result, ref index, startParts, "startRange");
        result.SetElement(index++, JsValue.FromObject(CreateRangePartObject("literal", " – ", "shared")));
        AppendRangeParts(result, ref index, endParts, "endRange");
        return result;
    }

    private FormatDateTimeValue GetDateTimeValue(double value)
    {
        if (!Intrinsics.TryTimeClipToEpochMillisecondsForIntl(value, out var milliseconds))
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid time value");

        if (TryGetNativeDateTimeValue(milliseconds, out var nativeValue))
            return nativeValue;

        return GetEcmaDateTimeValue(milliseconds);
    }

    private bool TryGetNativeDateTimeValue(long milliseconds, out FormatDateTimeValue value)
    {
        value = default;
        if (milliseconds is < MinNativeEpochMilliseconds or > MaxNativeEpochMilliseconds)
            return false;

        try
        {
            var instant = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            var zoned = ApplyTimeZone(instant);
            value = FormatDateTimeValue.FromDateTimeOffset(zoned);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private FormatDateTimeValue GetEcmaDateTimeValue(long milliseconds)
    {
        if (UseDefaultTimeZoneForFormatting)
            return FormatDateTimeValue.FromEcmaParts(Intrinsics.GetEcmaDateTimePartsForIntl(milliseconds, false));

        if (string.Equals(TimeZone, "UTC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "GMT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "Etc/UTC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "Etc/UCT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "Etc/GMT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "Etc/GMT0", StringComparison.OrdinalIgnoreCase))
            return FormatDateTimeValue.FromEcmaParts(Intrinsics.GetEcmaDateTimePartsForIntl(milliseconds, true));

        if (TryParseOffsetTimeZone(TimeZone, out var offset))
        {
            var zonedMilliseconds = checked(milliseconds + (long)offset.TotalMilliseconds);
            return FormatDateTimeValue.FromEcmaParts(Intrinsics.GetEcmaDateTimePartsForIntl(zonedMilliseconds, true));
        }

        if (TryParseEtcGmtTimeZone(TimeZone, out var etcGmtOffset))
        {
            var zonedMilliseconds = checked(milliseconds + (long)etcGmtOffset.TotalMilliseconds);
            return FormatDateTimeValue.FromEcmaParts(Intrinsics.GetEcmaDateTimePartsForIntl(zonedMilliseconds, true));
        }

        if (KnownTimeZones.TryGetValue(TimeZone, out var knownOffset))
        {
            var zonedMilliseconds = checked(milliseconds + (long)knownOffset.TotalMilliseconds);
            return FormatDateTimeValue.FromEcmaParts(Intrinsics.GetEcmaDateTimePartsForIntl(zonedMilliseconds, true));
        }

        return FormatDateTimeValue.FromEcmaParts(Intrinsics.GetEcmaDateTimePartsForIntl(milliseconds, true));
    }

    private DateTimeOffset ApplyTimeZone(DateTimeOffset instant)
    {
        if (UseDefaultTimeZoneForFormatting)
            return instant.ToLocalTime();
        if (string.Equals(TimeZone, "UTC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "GMT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "Etc/UTC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "Etc/UCT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "Etc/GMT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "Etc/GMT0", StringComparison.OrdinalIgnoreCase))
            return instant.ToUniversalTime();

        if (TryParseOffsetTimeZone(TimeZone, out var offset))
            return instant.ToUniversalTime().Add(offset);

        if (TryParseEtcGmtTimeZone(TimeZone, out var etcGmtOffset))
            return instant.ToUniversalTime().Add(etcGmtOffset);

        if (KnownTimeZones.TryGetValue(TimeZone, out var knownOffset))
            return instant.ToUniversalTime().Add(knownOffset);

        return instant.ToUniversalTime();
    }

    private List<DateTimePart> BuildParts(FormatDateTimeValue dateTime)
    {
        if (DateStyle is not null || TimeStyle is not null)
            return BuildStyleParts(dateTime);

        var parts = new List<DateTimePart>();
        var hasDate = Weekday is not null || Era is not null || Year is not null || Month is not null ||
                      Day is not null;
        var hasTime = Hour is not null || Minute is not null || Second is not null;

        if (!hasDate && DayPeriod is not null && !hasTime)
        {
            parts.Add(new("dayPeriod", GetDayPeriodText(dateTime.Hour)));
            return parts;
        }

        if (Weekday is not null)
            parts.Add(new("weekday", FormatWeekday(dateTime)));

        var hasDateFields = Year is not null || Month is not null || Day is not null;
        if (hasDateFields)
        {
            if (parts.Count > 0)
                parts.Add(new("literal", ", "));
            AppendDateParts(parts, dateTime);
        }

        if (Era is not null)
        {
            var era = FormatEra(dateTime.Year);
            if (era is not null)
            {
                if (parts.Count > 0)
                    parts.Add(new("literal", " "));
                parts.Add(new("era", era));
            }
        }

        if (hasTime)
        {
            if (parts.Count > 0)
                parts.Add(new("literal", ", "));
            AppendTimeParts(parts, dateTime);
        }

        if (parts.Count == 0)
            parts.Add(new("literal", FormatFullFallback(dateTime)));

        return parts;
    }

    private List<DateTimePart> BuildStyleParts(FormatDateTimeValue dateTime)
    {
        var parts = new List<DateTimePart>();
        var appendSeparator = false;
        if (DateStyle is not null)
        {
            AppendDateStyleParts(parts, dateTime);
            appendSeparator = true;
        }

        if (TimeStyle is not null)
        {
            if (appendSeparator)
                parts.Add(new("literal", ", "));
            AppendTimeStyleParts(parts, dateTime);
        }

        return parts;
    }

    private string JoinParts(List<DateTimePart> parts)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
            builder.Append(parts[i].Value);
        return builder.ToString();
    }

    private JsArray CreatePartsArray(List<DateTimePart> parts)
    {
        var result = Realm.CreateArrayObject();
        for (uint i = 0; i < parts.Count; i++)
            result.SetElement(i,
                JsValue.FromObject(CreatePartObject(parts[(int)i].Type, Transliterate(parts[(int)i].Value))));
        return result;
    }

    private bool TryCreateCompressedTextMonthRange(List<DateTimePart> startParts, List<DateTimePart> endParts,
        out List<RangeDateTimePart> result)
    {
        result = [];
        if (!(Locale.StartsWith("en-US", StringComparison.OrdinalIgnoreCase) &&
              DateStyle is null &&
              TimeStyle is null &&
              Month is "short" or "long" or "narrow" &&
              Day is not null &&
              Year is not null &&
              Weekday is null &&
              Era is null &&
              Hour is null &&
              Minute is null &&
              Second is null &&
              DayPeriod is null &&
              TimeZoneName is null))
            return false;

        var prefixLength = 0;
        while (prefixLength < startParts.Count &&
               prefixLength < endParts.Count &&
               startParts[prefixLength].Type == endParts[prefixLength].Type &&
               startParts[prefixLength].Value == endParts[prefixLength].Value)
            prefixLength++;

        var suffixLength = 0;
        while (suffixLength < startParts.Count - prefixLength &&
               suffixLength < endParts.Count - prefixLength &&
               startParts[startParts.Count - 1 - suffixLength].Type ==
               endParts[endParts.Count - 1 - suffixLength].Type &&
               startParts[startParts.Count - 1 - suffixLength].Value ==
               endParts[endParts.Count - 1 - suffixLength].Value)
            suffixLength++;

        if (suffixLength == 0)
            return false;

        for (var i = 0; i < prefixLength; i++)
            result.Add(new(startParts[i].Type, startParts[i].Value, "shared"));
        for (var i = prefixLength; i < startParts.Count - suffixLength; i++)
            result.Add(new(startParts[i].Type, startParts[i].Value, "startRange"));
        result.Add(new("literal", " – ", "shared"));
        for (var i = prefixLength; i < endParts.Count - suffixLength; i++)
            result.Add(new(endParts[i].Type, endParts[i].Value, "endRange"));
        for (var i = endParts.Count - suffixLength; i < endParts.Count; i++)
            result.Add(new(endParts[i].Type, endParts[i].Value, "shared"));
        return true;
    }

    private void AppendDateStyleParts(List<DateTimePart> parts, FormatDateTimeValue dateTime)
    {
        var style = DateStyle ?? "short";
        if (Locale.StartsWith("en-US", StringComparison.OrdinalIgnoreCase))
            switch (style)
            {
                case "full":
                    parts.Add(new("weekday", GetWeekdayText(dateTime, true)));
                    parts.Add(new("literal", ", "));
                    parts.Add(new("month", GetMonthText(dateTime, "long")));
                    parts.Add(new("literal", " "));
                    parts.Add(new("day", dateTime.Day.ToString(CultureInfo.InvariantCulture)));
                    parts.Add(new("literal", ", "));
                    parts.Add(new("year", GetDisplayYear(dateTime.Year).ToString(CultureInfo.InvariantCulture)));
                    return;
                case "long":
                case "medium":
                    parts.Add(new("month", GetMonthText(dateTime, "long")));
                    parts.Add(new("literal", " "));
                    parts.Add(new("day", dateTime.Day.ToString(CultureInfo.InvariantCulture)));
                    parts.Add(new("literal", ", "));
                    parts.Add(new("year", GetDisplayYear(dateTime.Year).ToString(CultureInfo.InvariantCulture)));
                    return;
                case "short":
                    parts.Add(new("month", dateTime.Month.ToString(CultureInfo.InvariantCulture)));
                    parts.Add(new("literal", "/"));
                    parts.Add(new("day", dateTime.Day.ToString(CultureInfo.InvariantCulture)));
                    parts.Add(new("literal", "/"));
                    parts.Add(new("year",
                        (GetDisplayYear(dateTime.Year) % 100).ToString("00", CultureInfo.InvariantCulture)));
                    return;
            }

        parts.Add(new("day", dateTime.Day.ToString(CultureInfo.InvariantCulture)));
        parts.Add(new("literal", "."));
        parts.Add(new("month", dateTime.Month.ToString(CultureInfo.InvariantCulture)));
        parts.Add(new("literal", "."));
        parts.Add(new("year", GetDisplayYear(dateTime.Year).ToString(CultureInfo.InvariantCulture)));
    }

    private void AppendTimeStyleParts(List<DateTimePart> parts, FormatDateTimeValue dateTime)
    {
        var style = TimeStyle ?? "short";
        var use12Hour = Uses12HourClock();
        var includeSeconds = style is "full" or "long" or "medium";
        var includeZone = style is "full" or "long";

        AppendTimeCoreParts(parts, dateTime, includeSeconds, includeZone, use12Hour);
    }

    private void AppendDateParts(List<DateTimePart> parts, FormatDateTimeValue dateTime)
    {
        if (TryGetLunisolarDate(dateTime, out var lunisolarDate))
        {
            AppendLunisolarDateParts(parts, lunisolarDate);
            return;
        }

        if (Locale.StartsWith("en-US", StringComparison.OrdinalIgnoreCase))
        {
            var textualMonth = Month is "short" or "long" or "narrow";
            var appended = false;
            if (Month is not null)
            {
                parts.Add(new("month", FormatMonth(dateTime)));
                appended = true;
                if (textualMonth && Day is not null)
                    parts.Add(new("literal", " "));
            }

            if (Day is not null)
            {
                if (appended && !textualMonth)
                    parts.Add(new("literal", "/"));
                parts.Add(new("day", FormatDay(dateTime)));
                appended = true;
            }

            if (Year is not null)
            {
                if (appended)
                    parts.Add(new("literal", textualMonth ? ", " : "/"));
                parts.Add(new("year", FormatYear(dateTime)));
            }

            return;
        }

        if (Day is not null)
            parts.Add(new("day", FormatDay(dateTime)));
        if (Month is not null)
        {
            if (parts.Count > 0 && parts[^1].Type != "literal")
                parts.Add(new("literal", "."));
            parts.Add(new("month", FormatMonth(dateTime)));
        }

        if (Year is not null)
        {
            if (parts.Count > 0 && parts[^1].Type != "literal")
                parts.Add(new("literal", "."));
            parts.Add(new("year", FormatYear(dateTime)));
        }
    }

    private bool TryGetLunisolarDate(FormatDateTimeValue dateTime, out OkojoLunisolarCalendarHelper.LunisolarDate date)
    {
        if (!dateTime.NativeDateTime.HasValue)
        {
            date = default;
            return false;
        }

        if (string.Equals(Calendar, "chinese", StringComparison.OrdinalIgnoreCase))
        {
            date = OkojoLunisolarCalendarHelper.GetChineseDate(dateTime.NativeDateTime.Value.DateTime);
            return true;
        }

        if (string.Equals(Calendar, "dangi", StringComparison.OrdinalIgnoreCase))
        {
            date = OkojoLunisolarCalendarHelper.GetDangiDate(dateTime.NativeDateTime.Value.DateTime);
            return true;
        }

        date = default;
        return false;
    }

    private void AppendLunisolarDateParts(List<DateTimePart> parts, OkojoLunisolarCalendarHelper.LunisolarDate date)
    {
        if (Locale.StartsWith("en-US", StringComparison.OrdinalIgnoreCase))
        {
            var textualMonth = Month is "short" or "long" or "narrow";
            var appended = false;
            if (Month is not null)
            {
                parts.Add(new("month", FormatLunisolarMonth(date)));
                appended = true;
                if (textualMonth && Day is not null)
                    parts.Add(new("literal", " "));
            }

            if (Day is not null)
            {
                if (appended && !textualMonth)
                    parts.Add(new("literal", "/"));
                parts.Add(new("day", FormatLunisolarDay(date)));
                appended = true;
            }

            if (Year is not null)
            {
                if (appended)
                    parts.Add(new("literal", textualMonth ? ", " : "/"));
                AppendLunisolarYearParts(parts, date, false);
            }

            return;
        }

        if (Day is not null)
            parts.Add(new("day", FormatLunisolarDay(date)));
        if (Month is not null)
        {
            if (parts.Count > 0 && parts[^1].Type != "literal")
                parts.Add(new("literal", "."));
            parts.Add(new("month", FormatLunisolarMonth(date)));
        }

        if (Year is not null)
        {
            if (parts.Count > 0 && parts[^1].Type != "literal")
                parts.Add(new("literal", "."));
            AppendLunisolarYearParts(parts, date, Locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        }
    }

    private void AppendLunisolarYearParts(List<DateTimePart> parts, OkojoLunisolarCalendarHelper.LunisolarDate date,
        bool includeYearLiteral)
    {
        parts.Add(new("relatedYear", Year switch
        {
            "2-digit" => (date.RelatedYear % 100).ToString("00", CultureInfo.InvariantCulture),
            _ => date.RelatedYear.ToString(CultureInfo.InvariantCulture)
        }));

        if (string.Equals(Calendar, "chinese", StringComparison.OrdinalIgnoreCase) &&
            (Locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || (Month is null && Day is null)))
        {
            parts.Add(new("yearName", date.YearName));
            if (includeYearLiteral)
                parts.Add(new("literal", "年"));
        }
    }

    private void AppendTimeParts(List<DateTimePart> parts, FormatDateTimeValue dateTime)
    {
        AppendTimeCoreParts(parts, dateTime, Second is not null, TimeZoneName is not null, Uses12HourClock());
    }

    private void AppendTimeCoreParts(List<DateTimePart> parts, FormatDateTimeValue dateTime, bool includeSeconds,
        bool includeZone, bool use12Hour)
    {
        var includeHour = Hour is not null || TimeStyle is not null;
        var hourValue = use12Hour ? dateTime.Hour % 12 == 0 ? 12 : dateTime.Hour % 12 : dateTime.Hour;
        if (includeHour)
            parts.Add(new("hour", FormatInteger(hourValue, Hour, TimeStyle is not null ? 1 : null)));
        if (Minute is not null || TimeStyle is not null)
        {
            if (parts.Count > 0 && parts[^1].Type == "hour")
                parts.Add(new("literal", ":"));
            parts.Add(new("minute", dateTime.Minute.ToString("00", CultureInfo.InvariantCulture)));
        }

        if (includeSeconds)
        {
            parts.Add(new("literal", ":"));
            parts.Add(new("second", dateTime.Second.ToString("00", CultureInfo.InvariantCulture)));
        }

        if (FractionalSecondDigits is not null)
        {
            var milliseconds =
                dateTime.Millisecond.ToString("000", CultureInfo.InvariantCulture)[..FractionalSecondDigits.Value];
            parts.Add(new("literal", "."));
            parts.Add(new("fractionalSecond", milliseconds));
        }

        if (use12Hour && includeHour)
        {
            parts.Add(new("literal", " "));
            parts.Add(new("dayPeriod", GetDayPeriodText(dateTime.Hour, DayPeriod is null)));
        }
        else if (DayPeriod is not null)
        {
            parts.Add(new("literal", " "));
            parts.Add(new("dayPeriod", GetDayPeriodText(dateTime.Hour)));
        }

        if (includeZone)
        {
            parts.Add(new("literal", " "));
            parts.Add(new("timeZoneName", GetTimeZoneName()));
        }
    }

    private string FormatWeekday(FormatDateTimeValue dateTime)
    {
        return Weekday switch
        {
            "narrow" => GetWeekdayText(dateTime, false)[0].ToString(),
            "short" => GetWeekdayText(dateTime, false),
            _ => GetWeekdayText(dateTime, true)
        };
    }

    private string FormatYear(FormatDateTimeValue dateTime)
    {
        var year = GetDisplayYear(dateTime.Year);
        return Year switch
        {
            "2-digit" => (year % 100).ToString("00", CultureInfo.InvariantCulture),
            _ => year.ToString(CultureInfo.InvariantCulture)
        };
    }

    private string FormatMonth(FormatDateTimeValue dateTime)
    {
        return Month switch
        {
            "2-digit" => dateTime.Month.ToString("00", CultureInfo.InvariantCulture),
            "numeric" => dateTime.Month.ToString(CultureInfo.InvariantCulture),
            "narrow" => GetMonthText(dateTime, "long")[0].ToString(),
            "short" => GetMonthText(dateTime, "short"),
            _ => GetMonthText(dateTime, "long")
        };
    }

    private string FormatDay(FormatDateTimeValue dateTime)
    {
        return Day switch
        {
            "2-digit" => dateTime.Day.ToString("00", CultureInfo.InvariantCulture),
            _ => dateTime.Day.ToString(CultureInfo.InvariantCulture)
        };
    }

    private int GetDisplayYear(int isoYear)
    {
        if (Era is not null && isoYear <= 0)
            return 1 - isoYear;
        return isoYear;
    }

    private string? FormatEra(int isoYear)
    {
        return OkojoIntlCalendarData.FormatEra(Calendar, isoYear, Era!);
    }

    private string GetWeekdayText(FormatDateTimeValue dateTime, bool longForm)
    {
        if (dateTime.NativeDateTime.HasValue)
            return dateTime.NativeDateTime.Value.ToString(longForm ? "dddd" : "ddd", CultureInfo);

        var names = longForm ? CultureInfo.DateTimeFormat.DayNames : CultureInfo.DateTimeFormat.AbbreviatedDayNames;
        return names[dateTime.WeekdayIndex];
    }

    private string GetMonthText(FormatDateTimeValue dateTime, string width)
    {
        if (dateTime.NativeDateTime.HasValue)
            return width switch
            {
                "short" => dateTime.NativeDateTime.Value.ToString("MMM", CultureInfo),
                _ => dateTime.NativeDateTime.Value.ToString("MMMM", CultureInfo)
            };

        return width switch
        {
            "short" => CultureInfo.DateTimeFormat.AbbreviatedMonthNames[dateTime.Month - 1],
            _ => CultureInfo.DateTimeFormat.MonthNames[dateTime.Month - 1]
        };
    }

    private string FormatFullFallback(FormatDateTimeValue dateTime)
    {
        var builder = new StringBuilder();
        builder.Append(GetMonthText(dateTime, "long"));
        builder.Append(' ');
        builder.Append(dateTime.Day.ToString(CultureInfo.InvariantCulture));
        builder.Append(", ");
        builder.Append(GetDisplayYear(dateTime.Year).ToString(CultureInfo.InvariantCulture));
        if (Era is not null)
        {
            var era = FormatEra(dateTime.Year);
            if (era is not null)
            {
                builder.Append(' ');
                builder.Append(era);
            }
        }

        return builder.ToString();
    }

    private string FormatLunisolarMonth(OkojoLunisolarCalendarHelper.LunisolarDate date)
    {
        return Month switch
        {
            "2-digit" => date.Month.ToString("00", CultureInfo.InvariantCulture),
            _ => date.Month.ToString(CultureInfo.InvariantCulture)
        };
    }

    private string FormatLunisolarDay(OkojoLunisolarCalendarHelper.LunisolarDate date)
    {
        return Day switch
        {
            "2-digit" => date.Day.ToString("00", CultureInfo.InvariantCulture),
            _ => date.Day.ToString(CultureInfo.InvariantCulture)
        };
    }

    private string FormatInteger(int value, string? format, int? minimumDigits = null)
    {
        if (format == "2-digit")
            return value.ToString("00", CultureInfo.InvariantCulture);
        if (minimumDigits == 2)
            return value.ToString("00", CultureInfo.InvariantCulture);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private string GetDayPeriodText(int hour, bool defaultAmPm = false)
    {
        if (defaultAmPm)
            return hour < 12 ? "AM" : "PM";

        if (hour == 12)
            return DayPeriod switch
            {
                "narrow" => "n",
                "short" => "noon",
                _ => "noon"
            };

        if (hour >= 6 && hour < 12)
            return "in the morning";
        if (hour > 12 && hour < 18)
            return "in the afternoon";
        if (hour >= 18 && hour < 21)
            return "in the evening";
        return "at night";
    }

    private bool Uses12HourClock()
    {
        if (Hour12.HasValue)
            return Hour12.Value;
        return HourCycle is "h11" or "h12";
    }

    private string GetTimeZoneName()
    {
        var effectiveTimeZoneName =
            TimeZoneName ?? (TimeStyle == "full" ? "full" : TimeStyle == "long" ? "long" : "short");
        if (string.Equals(TimeZone, "UTC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "Etc/UTC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "GMT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "Etc/GMT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "Etc/UCT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TimeZone, "Etc/GMT0", StringComparison.OrdinalIgnoreCase))
            return effectiveTimeZoneName switch
            {
                "full" => "Coordinated Universal Time",
                "long" => "UTC",
                "short" => "UTC",
                "shortOffset" => "GMT",
                "longOffset" => "GMT+00:00",
                "shortGeneric" => "GMT",
                "longGeneric" => "GMT",
                _ => "UTC"
            };

        if (TryParseOffsetTimeZone(TimeZone, out var offset))
        {
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            var abs = offset.Duration();
            return (TimeZoneName is "longOffset" ? "GMT" : "GMT") + sign +
                   abs.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }

        if (TryParseEtcGmtTimeZone(TimeZone, out var etcGmtOffset))
        {
            var sign = etcGmtOffset < TimeSpan.Zero ? "-" : "+";
            var abs = etcGmtOffset.Duration();
            return "GMT" + sign + abs.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }

        if (KnownTimeZones.TryGetValue(TimeZone, out var knownOffset))
        {
            var sign = knownOffset < TimeSpan.Zero ? "-" : "+";
            var abs = knownOffset.Duration();
            return effectiveTimeZoneName switch
            {
                "long" or "full" => "GMT" + sign + abs.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                _ => "GMT" + sign + abs.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
            };
        }

        return TimeZone;
    }

    private static bool TryParseEtcGmtTimeZone(string timeZone, out TimeSpan offset)
    {
        offset = default;
        if (!timeZone.StartsWith("Etc/GMT", StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = timeZone["Etc/GMT".Length..];
        if (suffix.Length < 2 || (suffix[0] != '+' && suffix[0] != '-'))
            return false;

        if (!int.TryParse(suffix[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) || hours > 23)
            return false;

        var sign = suffix[0] == '+' ? -1 : 1;
        offset = TimeSpan.FromHours(sign * hours);
        return true;
    }

    private string Transliterate(string text)
    {
        return string.Equals(NumberingSystem, "latn", StringComparison.OrdinalIgnoreCase)
            ? text
            : OkojoIntlNumberingSystemData.TransliterateDigits(text, NumberingSystem);
    }

    private JsPlainObject CreatePartObject(string type, string value)
    {
        var part = new JsPlainObject(Realm.IntlPartObjectShape);
        part.SetNamedSlotUnchecked(JsRealm.IntlPartTypeSlot, JsValue.FromString(type));
        part.SetNamedSlotUnchecked(JsRealm.IntlPartValueSlot, JsValue.FromString(value));
        return part;
    }

    private JsPlainObject CreateRangePartObject(string type, string value, string source)
    {
        var part = new JsPlainObject(Realm.IntlRangePartObjectShape);
        part.SetNamedSlotUnchecked(JsRealm.IntlRangePartTypeSlot, JsValue.FromString(type));
        part.SetNamedSlotUnchecked(JsRealm.IntlRangePartValueSlot, JsValue.FromString(value));
        part.SetNamedSlotUnchecked(JsRealm.IntlRangePartSourceSlot, JsValue.FromString(source));
        return part;
    }

    private void AppendRangeParts(JsArray result, ref uint index, JsArray parts, string source)
    {
        for (uint i = 0; i < parts.Length; i++)
        {
            if (!parts.TryGetElement(i, out var entry) || !entry.TryGetObject(out var entryObject))
                continue;
            if (!entryObject.TryGetProperty("type", out var typeValue) || !typeValue.IsString)
                continue;
            if (!entryObject.TryGetProperty("value", out var valueValue) || !valueValue.IsString)
                continue;
            result.SetElement(index++,
                JsValue.FromObject(CreateRangePartObject(typeValue.AsString(), valueValue.AsString(), source)));
        }
    }

    private static bool TryParseOffsetTimeZone(string timeZone, out TimeSpan offset)
    {
        offset = default;
        if (timeZone.Length != 6 || (timeZone[0] != '+' && timeZone[0] != '-') || timeZone[3] != ':')
            return false;
        if (!char.IsAsciiDigit(timeZone[1]) || !char.IsAsciiDigit(timeZone[2]) ||
            !char.IsAsciiDigit(timeZone[4]) || !char.IsAsciiDigit(timeZone[5]))
            return false;

        var hours = (timeZone[1] - '0') * 10 + (timeZone[2] - '0');
        var minutes = (timeZone[4] - '0') * 10 + (timeZone[5] - '0');
        if (hours > 23 || minutes > 59)
            return false;

        offset = new(hours, minutes, 0);
        if (timeZone[0] == '-')
            offset = -offset;
        return true;
    }

    private readonly record struct DateTimePart(string Type, string Value);

    private readonly record struct RangeDateTimePart(string Type, string Value, string Source);

    private readonly record struct FormatDateTimeValue(
        int Year,
        int Month,
        int Day,
        int Hour,
        int Minute,
        int Second,
        int Millisecond,
        int WeekdayIndex,
        DateTimeOffset? NativeDateTime)
    {
        internal static FormatDateTimeValue FromDateTimeOffset(DateTimeOffset value)
        {
            return new(
                value.Year,
                value.Month,
                value.Day,
                value.Hour,
                value.Minute,
                value.Second,
                value.Millisecond,
                (int)value.DayOfWeek,
                value);
        }

        internal static FormatDateTimeValue FromEcmaParts(Intrinsics.OkojoEcmaDateTimeParts value)
        {
            return new(
                value.Year,
                value.Month,
                value.Day,
                value.Hour,
                value.Minute,
                value.Second,
                value.Millisecond,
                value.WeekdayIndex,
                null);
        }
    }
}
