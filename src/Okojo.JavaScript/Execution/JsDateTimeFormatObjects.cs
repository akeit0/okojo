using System.Globalization;
using System.Text;
using Okojo.Globalization;

namespace Okojo.JavaScript.Execution;

internal sealed class JsDateTimeFormatObject : JsObject
{
    private const long MinNativeEpochMilliseconds = -62135596800000L;
    private const long MaxNativeEpochMilliseconds = 253402300799999L;

    private readonly DateTimeFormat core;
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
        CultureInfo cultureInfo
    )
        : base(realm)
    {
        Prototype = prototype;
        core = new(
            locale,
            calendar,
            numberingSystem,
            timeZone,
            useDefaultTimeZoneForFormatting,
            hourCycle,
            hour12,
            weekday,
            era,
            year,
            month,
            day,
            dayPeriod,
            hour,
            minute,
            second,
            fractionalSecondDigits,
            timeZoneName,
            formatMatcher,
            dateStyle,
            timeStyle,
            cultureInfo
        );
    }

    internal string Locale => core.Locale;
    internal string Calendar => core.Calendar;
    internal string NumberingSystem => core.NumberingSystem;
    internal string TimeZone => core.TimeZone;
    internal bool UseDefaultTimeZoneForFormatting => core.UseDefaultTimeZoneForFormatting;
    internal string HourCycle => core.HourCycle;
    internal bool? Hour12 => core.Hour12;
    internal string? Weekday => core.Weekday;
    internal string? Era => core.Era;
    internal string? Year => core.Year;
    internal string? Month => core.Month;
    internal string? Day => core.Day;
    internal string? DayPeriod => core.DayPeriod;
    internal string? Hour => core.Hour;
    internal string? Minute => core.Minute;
    internal string? Second => core.Second;
    internal int? FractionalSecondDigits => core.FractionalSecondDigits;
    internal string? TimeZoneName => core.TimeZoneName;
    internal string? FormatMatcher => core.FormatMatcher;
    internal string? DateStyle => core.DateStyle;
    internal string? TimeStyle => core.TimeStyle;
    internal CultureInfo CultureInfo => core.CultureInfo;

    internal JsHostFunction GetOrCreateBoundFormat(JsRealm realm)
    {
        if (boundFormat is not null)
            return boundFormat;

        boundFormat = new(
            realm,
            static (in info) =>
            {
                var dateTimeFormat = (JsDateTimeFormatObject)
                    ((JsHostFunction)info.Function).UserData!;
                var value =
                    info.Arguments.Length == 0 || info.Arguments[0].IsUndefined
                        ? DateTimeOffset.Now.ToUnixTimeMilliseconds()
                        : info.Realm.ToNumberSlowPath(info.Arguments[0]);
                return JsValue.FromString(dateTimeFormat.Format(value));
            },
            string.Empty,
            1
        )
        {
            UserData = this,
        };
        return boundFormat;
    }

    internal string Format(double value)
    {
        var parts = core.BuildParts(GetDateTimeValue(value));
        var builder = new StringBuilder();
        foreach (var part in parts)
            builder.Append(part.Value);
        return Transliterate(builder.ToString());
    }

    internal JsArray FormatToParts(double value)
    {
        var parts = core.BuildParts(GetDateTimeValue(value));
        var result = Realm.CreateArrayObject();
        for (uint i = 0; i < parts.Count; i++)
            result.SetElement(
                i,
                JsValue.FromObject(
                    CreatePartObject(parts[(int)i].Type, Transliterate(parts[(int)i].Value))
                )
            );
        return result;
    }

    internal string FormatRange(double startValue, double endValue)
    {
        var startDateTime = GetDateTimeValue(startValue);
        var endDateTime = GetDateTimeValue(endValue);
        var startParts = core.BuildParts(startDateTime);
        var endParts = core.BuildParts(endDateTime);
        var start = Transliterate(JoinParts(startParts));
        var end = Transliterate(JoinParts(endParts));
        if (string.Equals(start, end, StringComparison.Ordinal))
            return start;

        if (core.TryCreateCompressedTextMonthRange(startParts, endParts, out var compressedParts))
            return Transliterate(string.Concat(compressedParts.Select(static p => p.Value)));

        return start + " – " + end;
    }

    internal JsArray FormatRangeToParts(double startValue, double endValue)
    {
        var startDateTime = GetDateTimeValue(startValue);
        var endDateTime = GetDateTimeValue(endValue);
        var startSourceParts = core.BuildParts(startDateTime);
        var endSourceParts = core.BuildParts(endDateTime);
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

        if (
            core.TryCreateCompressedTextMonthRange(
                startSourceParts,
                endSourceParts,
                out var compressedParts
            )
        )
        {
            for (var i = 0; i < compressedParts.Count; i++)
            {
                var part = compressedParts[i];
                result.SetElement(
                    index++,
                    JsValue.FromObject(
                        CreateRangePartObject(part.Type, Transliterate(part.Value), part.Source!)
                    )
                );
            }

            return result;
        }

        AppendRangeParts(result, ref index, startParts, "startRange");
        result.SetElement(
            index++,
            JsValue.FromObject(CreateRangePartObject("literal", " – ", "shared"))
        );
        AppendRangeParts(result, ref index, endParts, "endRange");
        return result;
    }

    private DateTimeValue GetDateTimeValue(double value)
    {
        if (!Intrinsics.TryTimeClipToEpochMillisecondsForIntl(value, out var milliseconds))
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid time value");

        if (TryGetNativeDateTimeValue(milliseconds, out var nativeValue))
            return nativeValue;

        return GetEcmaDateTimeValue(milliseconds);
    }

    private bool TryGetNativeDateTimeValue(long milliseconds, out DateTimeValue value)
    {
        value = default;
        if (milliseconds is < MinNativeEpochMilliseconds or > MaxNativeEpochMilliseconds)
            return false;

        try
        {
            var instant = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            var zoned = ApplyTimeZone(instant);
            value = DateTimeValue.FromDateTimeOffset(zoned);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private DateTimeValue GetEcmaDateTimeValue(long milliseconds)
    {
        if (UseDefaultTimeZoneForFormatting)
            return ToDateTimeValue(Intrinsics.GetEcmaDateTimePartsForIntl(milliseconds, false));

        if (
            string.Equals(TimeZone, "UTC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TimeZone, "GMT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TimeZone, "Etc/UTC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TimeZone, "Etc/UCT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TimeZone, "Etc/GMT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TimeZone, "Etc/GMT0", StringComparison.OrdinalIgnoreCase)
        )
            return ToDateTimeValue(Intrinsics.GetEcmaDateTimePartsForIntl(milliseconds, true));

        if (TryParseOffsetTimeZone(TimeZone, out var offset))
        {
            var zonedMilliseconds = checked(milliseconds + (long)offset.TotalMilliseconds);
            return ToDateTimeValue(Intrinsics.GetEcmaDateTimePartsForIntl(zonedMilliseconds, true));
        }

        if (TryParseEtcGmtTimeZone(TimeZone, out var etcGmtOffset))
        {
            var zonedMilliseconds = checked(milliseconds + (long)etcGmtOffset.TotalMilliseconds);
            return ToDateTimeValue(Intrinsics.GetEcmaDateTimePartsForIntl(zonedMilliseconds, true));
        }

        if (KnownTimeZones.TryGetValue(TimeZone, out var knownOffset))
        {
            var zonedMilliseconds = checked(milliseconds + (long)knownOffset.TotalMilliseconds);
            return ToDateTimeValue(Intrinsics.GetEcmaDateTimePartsForIntl(zonedMilliseconds, true));
        }

        return ToDateTimeValue(Intrinsics.GetEcmaDateTimePartsForIntl(milliseconds, true));
    }

    private static DateTimeValue ToDateTimeValue(Intrinsics.OkojoEcmaDateTimeParts value)
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
            null
        );
    }

    private DateTimeOffset ApplyTimeZone(DateTimeOffset instant)
    {
        if (UseDefaultTimeZoneForFormatting)
            return instant.ToLocalTime();
        if (
            string.Equals(TimeZone, "UTC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TimeZone, "GMT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TimeZone, "Etc/UTC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TimeZone, "Etc/UCT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TimeZone, "Etc/GMT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TimeZone, "Etc/GMT0", StringComparison.OrdinalIgnoreCase)
        )
            return instant.ToUniversalTime();

        if (TryParseOffsetTimeZone(TimeZone, out var offset))
            return instant.ToUniversalTime().Add(offset);

        if (TryParseEtcGmtTimeZone(TimeZone, out var etcGmtOffset))
            return instant.ToUniversalTime().Add(etcGmtOffset);

        if (KnownTimeZones.TryGetValue(TimeZone, out var knownOffset))
            return instant.ToUniversalTime().Add(knownOffset);

        return instant.ToUniversalTime();
    }

    private static readonly Dictionary<string, TimeSpan> KnownTimeZones = new(
        StringComparer.Ordinal
    )
    {
        ["Asia/Tokyo"] = TimeSpan.FromHours(9),
        ["Asia/Calcutta"] = TimeSpan.FromHours(5.5),
        ["Asia/Kolkata"] = TimeSpan.FromHours(5.5),
        ["Pacific/Apia"] = TimeSpan.FromHours(13),
        ["America/Los_Angeles"] = TimeSpan.FromHours(-8),
        ["America/Vancouver"] = TimeSpan.FromHours(-8),
        ["Europe/Prague"] = TimeSpan.FromHours(1),
    };

    private static bool TryParseEtcGmtTimeZone(string timeZone, out TimeSpan offset)
    {
        offset = default;
        if (!timeZone.StartsWith("Etc/GMT", StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = timeZone["Etc/GMT".Length..];
        if (suffix.Length < 2 || (suffix[0] != '+' && suffix[0] != '-'))
            return false;

        if (
            !int.TryParse(
                suffix[1..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var hours
            )
            || hours > 23
        )
            return false;

        var sign = suffix[0] == '+' ? -1 : 1;
        offset = TimeSpan.FromHours(sign * hours);
        return true;
    }

    private static bool TryParseOffsetTimeZone(string timeZone, out TimeSpan offset)
    {
        offset = default;
        if (
            timeZone.Length != 6
            || (timeZone[0] != '+' && timeZone[0] != '-')
            || timeZone[3] != ':'
        )
            return false;
        if (
            !char.IsAsciiDigit(timeZone[1])
            || !char.IsAsciiDigit(timeZone[2])
            || !char.IsAsciiDigit(timeZone[4])
            || !char.IsAsciiDigit(timeZone[5])
        )
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

    private string JoinParts(List<IntlPart> parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts)
            builder.Append(part.Value);
        return builder.ToString();
    }

    private JsArray CreatePartsArray(List<IntlPart> parts)
    {
        var result = Realm.CreateArrayObject();
        for (uint i = 0; i < parts.Count; i++)
            result.SetElement(
                i,
                JsValue.FromObject(
                    CreatePartObject(parts[(int)i].Type, Transliterate(parts[(int)i].Value))
                )
            );
        return result;
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
            if (!entryObject.TryGetPropertyByAtom(IdType, out var typeValue) || !typeValue.IsString)
                continue;
            if (
                !entryObject.TryGetPropertyByAtom(IdValue, out var valueValue)
                || !valueValue.IsString
            )
                continue;

            result.SetElement(
                index++,
                JsValue.FromObject(
                    CreateRangePartObject(typeValue.AsString(), valueValue.AsString(), source)
                )
            );
        }
    }

    private string Transliterate(string text)
    {
        return NumberingSystemData.TransliterateDigits(text, NumberingSystem);
    }
}
