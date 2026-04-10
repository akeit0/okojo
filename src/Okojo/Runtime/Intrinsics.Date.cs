using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private const double MaxTimeClip = 8.64e15;
    private const double MsPerSecond = 1000d;
    private const double MsPerMinute = 60d * MsPerSecond;
    private const double MsPerHour = 60d * MsPerMinute;
    private const double MsPerDay = 24d * MsPerHour;

    [GeneratedRegex(
        @"^(?<year>[+-]?\d{4,6})-(?<month>\d{2})-(?<day>\d{2})(?:T(?<hour>\d{2}):(?<minute>\d{2})(?::(?<second>\d{2})(?:\.(?<millisecond>\d{1,3}))?)?(?<offset>Z|[+-]\d{2}:\d{2})?)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EcmaIsoDateTimeRegex();

    [GeneratedRegex(
        @"^(?<year>[+-]?\d{4,6})(?:-(?<month>\d{2})(?:-(?<day>\d{2}))?)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EcmaPartialDateRegex();

    [GeneratedRegex(
        @"^(?<weekday>[A-Za-z]{3}) (?<monthName>[A-Za-z]{3}) (?<day>\d{2}) (?<year>[+-]?\d{4,6}) (?<hour>\d{2}):(?<minute>\d{2}):(?<second>\d{2}) GMT(?<sign>[+-])(?<offsetHour>\d{2})(?::?(?<offsetMinute>\d{2}))?(?: \(.+\))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex LegacyLocalDateStringRegex();

    private JsHostFunction CreateDateConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;

            if (!info.IsConstruct)
                return FormatDateLocalString(realm.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds());

            var timeValue = args.Length switch
            {
                0 => realm.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                1 => DateConstructorSingleArgumentToTimeValue(realm, args[0]),
                _ => DateConstructorMultipleArgumentsToTimeValue(realm, args)
            };

            var prototype = GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee,
                callee.Realm.DatePrototype);
            return new JsDateObject(realm, timeValue) { Prototype = prototype };
        }, "Date", 7, true);
    }

    private void InstallDateConstructorBuiltins()
    {
        var nowFn = new JsHostFunction(Realm,
            static (in info) => { return new(info.Realm.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds()); },
            "now", 0);

        var parseFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var text = args.Length == 0 ? "undefined" : realm.ToJsStringSlowPath(args[0]);
            return new(ParseDateString(text));
        }, "parse", 1);

        var utcFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0)
                return JsValue.NaN;

            var year = realm.ToNumberSlowPath(args[0]);
            var month = args.Length > 1 ? realm.ToNumberSlowPath(args[1]) : 0d;
            var date = args.Length > 2 ? realm.ToNumberSlowPath(args[2]) : 1d;
            var hours = args.Length > 3 ? realm.ToNumberSlowPath(args[3]) : 0d;
            var minutes = args.Length > 4 ? realm.ToNumberSlowPath(args[4]) : 0d;
            var seconds = args.Length > 5 ? realm.ToNumberSlowPath(args[5]) : 0d;
            var milliseconds = args.Length > 6 ? realm.ToNumberSlowPath(args[6]) : 0d;

            if (!AreFiniteDateComponents(year, month, date, hours, minutes, seconds, milliseconds))
                return JsValue.NaN;

            var fullYear = MakeFullYear(year);
            var day = MakeDay(fullYear, month, date);
            var time = MakeTime(hours, minutes, seconds, milliseconds);
            return new(TimeClip(MakeDate(day, time)));
        }, "UTC", 7);

        var toStringFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.toString");
            return FormatDateLocalString(timeValue);
        }, "toString", 0);

        var toDateStringFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.toDateString");
            return FormatDateDateString(timeValue);
        }, "toDateString", 0);

        var toTimeStringFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.toTimeString");
            return FormatDateTimeString(timeValue);
        }, "toTimeString", 0);

        var toUtcStringFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.toUTCString");
            return FormatDateUtcString(timeValue);
        }, "toUTCString", 0);

        var toLocaleDateStringFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.toLocaleDateString");
            return FormatDateWithIntl(info.Realm, timeValue, info.Arguments, true,
                false);
        }, "toLocaleDateString", 0);

        var toLocaleStringFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.toLocaleString");
            return FormatDateWithIntl(info.Realm, timeValue, info.Arguments, true,
                true);
        }, "toLocaleString", 0);

        var toLocaleTimeStringFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.toLocaleTimeString");
            return FormatDateWithIntl(info.Realm, timeValue, info.Arguments, false,
                true);
        }, "toLocaleTimeString", 0);

        var toIsoStringFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.toISOString");
            if (double.IsNaN(timeValue))
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid time value");
            return FormatDateIsoString(timeValue);
        }, "toISOString", 0);

        var toJsonFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var primitive = realm.ToPrimitiveSlowPath(thisValue, false);
            if (primitive.IsNumber && (double.IsNaN(primitive.NumberValue) || double.IsInfinity(primitive.NumberValue)))
                return JsValue.Null;

            if (!realm.TryToObject(thisValue, out var obj))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot convert undefined or null to object");

            if (!obj.TryGetPropertyAtom(realm, IdToIsoString, out var toIsoValue, out _) ||
                !toIsoValue.TryGetObject(out var toIsoObj) || toIsoObj is not JsFunction toIsoFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "toISOString is not callable");

            return realm.InvokeFunction(toIsoFn, thisValue, ReadOnlySpan<JsValue>.Empty);
        }, "toJSON", 1);

        var valueOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.valueOf");
            return new(timeValue);
        }, "valueOf", 0);

        var getTimeFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getTime");
            return new(timeValue);
        }, "getTime", 0);

        var getDateFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getDate");
            return GetDateComponentValue(timeValue, DateComponent.Date, false);
        }, "getDate", 0);

        var getDayFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getDay");
            return GetDateComponentValue(timeValue, DateComponent.Weekday, false);
        }, "getDay", 0);

        var getFullYearFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getFullYear");
            return GetDateComponentValue(timeValue, DateComponent.Year, false);
        }, "getFullYear", 0);

        var getHoursFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getHours");
            return GetDateComponentValue(timeValue, DateComponent.Hour, false);
        }, "getHours", 0);

        var getMillisecondsFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getMilliseconds");
            return GetDateComponentValue(timeValue, DateComponent.Millisecond, false);
        }, "getMilliseconds", 0);

        var getMinutesFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getMinutes");
            return GetDateComponentValue(timeValue, DateComponent.Minute, false);
        }, "getMinutes", 0);

        var getMonthFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getMonth");
            return GetDateComponentValue(timeValue, DateComponent.MonthZeroBased, false);
        }, "getMonth", 0);

        var getSecondsFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getSeconds");
            return GetDateComponentValue(timeValue, DateComponent.Second, false);
        }, "getSeconds", 0);

        var getTimezoneOffsetFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getTimezoneOffset");
            if (double.IsNaN(timeValue))
                return JsValue.NaN;
            return new(GetTimezoneOffsetMinutes(timeValue));
        }, "getTimezoneOffset", 0);

        var getUtcDateFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getUTCDate");
            return GetDateComponentValue(timeValue, DateComponent.Date, true);
        }, "getUTCDate", 0);

        var getUtcDayFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getUTCDay");
            return GetDateComponentValue(timeValue, DateComponent.Weekday, true);
        }, "getUTCDay", 0);

        var getUtcFullYearFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getUTCFullYear");
            return GetDateComponentValue(timeValue, DateComponent.Year, true);
        }, "getUTCFullYear", 0);

        var getUtcHoursFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getUTCHours");
            return GetDateComponentValue(timeValue, DateComponent.Hour, true);
        }, "getUTCHours", 0);

        var getUtcMillisecondsFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getUTCMilliseconds");
            return GetDateComponentValue(timeValue, DateComponent.Millisecond, true);
        }, "getUTCMilliseconds", 0);

        var getUtcMinutesFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getUTCMinutes");
            return GetDateComponentValue(timeValue, DateComponent.Minute, true);
        }, "getUTCMinutes", 0);

        var getUtcMonthFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getUTCMonth");
            return GetDateComponentValue(timeValue, DateComponent.MonthZeroBased, true);
        }, "getUTCMonth", 0);

        var getUtcSecondsFn = new JsHostFunction(Realm, (in info) =>
        {
            var timeValue = ThisDateValue(info.Realm, info.ThisValue, "Date.prototype.getUTCSeconds");
            return GetDateComponentValue(timeValue, DateComponent.Second, true);
        }, "getUTCSeconds", 0);

        var setTimeFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (!info.ThisValue.TryGetObject(out var obj) || obj is not JsDateObject date)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Date.prototype.setTime called on incompatible receiver");

            var time = args.Length == 0 ? double.NaN : realm.ToNumberSlowPath(args[0]);
            var clipped = TimeClip(time);
            date.TimeValue = clipped;
            return new(clipped);
        }, "setTime", 1);

        var setDateFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                return SetDateLike(info, "Date.prototype.setDate", false,
                    static (realm, args, ref parts) =>
                    {
                        parts = parts with { Day = ToDateSetterValue(realm, args, 0) };
                    });
            }, "setDate", 1);

        var setFullYearFn = new JsHostFunction(Realm, (in info) =>
        {
            return SetDateLike(info, "Date.prototype.setFullYear", false,
                static (realm, args, ref parts) =>
                {
                    parts = parts with
                    {
                        Year = ToDateSetterValue(realm, args, 0),
                        Month = args.Length > 1 ? ToDateSetterValue(realm, args, 1) + 1 : parts.Month,
                        Day = args.Length > 2 ? ToDateSetterValue(realm, args, 2) : parts.Day
                    };
                }, true, true);
        }, "setFullYear", 3);

        var setHoursFn = new JsHostFunction(Realm, (in info) =>
        {
            return SetDateLike(info, "Date.prototype.setHours", false,
                static (realm, args, ref parts) =>
                {
                    parts = parts with
                    {
                        Hour = ToDateSetterValue(realm, args, 0),
                        Minute = args.Length > 1 ? ToDateSetterValue(realm, args, 1) : parts.Minute,
                        Second = args.Length > 2 ? ToDateSetterValue(realm, args, 2) : parts.Second,
                        Millisecond = args.Length > 3 ? ToDateSetterValue(realm, args, 3) : parts.Millisecond
                    };
                });
        }, "setHours", 4);

        var setMillisecondsFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                return SetDateLike(info, "Date.prototype.setMilliseconds", false,
                    static (realm, args, ref parts) =>
                    {
                        parts = parts with { Millisecond = ToDateSetterValue(realm, args, 0) };
                    });
            }, "setMilliseconds", 1);

        var setMinutesFn = new JsHostFunction(Realm, (in info) =>
        {
            return SetDateLike(info, "Date.prototype.setMinutes", false,
                static (realm, args, ref parts) =>
                {
                    parts = parts with
                    {
                        Minute = ToDateSetterValue(realm, args, 0),
                        Second = args.Length > 1 ? ToDateSetterValue(realm, args, 1) : parts.Second,
                        Millisecond = args.Length > 2 ? ToDateSetterValue(realm, args, 2) : parts.Millisecond
                    };
                });
        }, "setMinutes", 3);

        var setMonthFn = new JsHostFunction(Realm, (in info) =>
        {
            return SetDateLike(info, "Date.prototype.setMonth", false,
                static (realm, args, ref parts) =>
                {
                    parts = parts with
                    {
                        Month = ToDateSetterValue(realm, args, 0) + 1,
                        Day = args.Length > 1 ? ToDateSetterValue(realm, args, 1) : parts.Day
                    };
                });
        }, "setMonth", 2);

        var setSecondsFn = new JsHostFunction(Realm, (in info) =>
        {
            return SetDateLike(info, "Date.prototype.setSeconds", false,
                static (realm, args, ref parts) =>
                {
                    parts = parts with
                    {
                        Second = ToDateSetterValue(realm, args, 0),
                        Millisecond = args.Length > 1 ? ToDateSetterValue(realm, args, 1) : parts.Millisecond
                    };
                });
        }, "setSeconds", 2);

        var setUtcDateFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                return SetDateLike(info, "Date.prototype.setUTCDate", true,
                    static (realm, args, ref parts) =>
                    {
                        parts = parts with { Day = ToDateSetterValue(realm, args, 0) };
                    });
            }, "setUTCDate", 1);

        var setUtcFullYearFn = new JsHostFunction(Realm, (in info) =>
        {
            return SetDateLike(info, "Date.prototype.setUTCFullYear", true,
                static (realm, args, ref parts) =>
                {
                    parts = parts with
                    {
                        Year = ToDateSetterValue(realm, args, 0),
                        Month = args.Length > 1 ? ToDateSetterValue(realm, args, 1) + 1 : parts.Month,
                        Day = args.Length > 2 ? ToDateSetterValue(realm, args, 2) : parts.Day
                    };
                }, true, true);
        }, "setUTCFullYear", 3);

        var setUtcHoursFn = new JsHostFunction(Realm, (in info) =>
        {
            return SetDateLike(info, "Date.prototype.setUTCHours", true,
                static (realm, args, ref parts) =>
                {
                    parts = parts with
                    {
                        Hour = ToDateSetterValue(realm, args, 0),
                        Minute = args.Length > 1 ? ToDateSetterValue(realm, args, 1) : parts.Minute,
                        Second = args.Length > 2 ? ToDateSetterValue(realm, args, 2) : parts.Second,
                        Millisecond = args.Length > 3 ? ToDateSetterValue(realm, args, 3) : parts.Millisecond
                    };
                });
        }, "setUTCHours", 4);

        var setUtcMillisecondsFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                return SetDateLike(info, "Date.prototype.setUTCMilliseconds", true,
                    static (realm, args, ref parts) =>
                    {
                        parts = parts with { Millisecond = ToDateSetterValue(realm, args, 0) };
                    });
            }, "setUTCMilliseconds", 1);

        var setUtcMinutesFn = new JsHostFunction(Realm, (in info) =>
        {
            return SetDateLike(info, "Date.prototype.setUTCMinutes", true,
                static (realm, args, ref parts) =>
                {
                    parts = parts with
                    {
                        Minute = ToDateSetterValue(realm, args, 0),
                        Second = args.Length > 1 ? ToDateSetterValue(realm, args, 1) : parts.Second,
                        Millisecond = args.Length > 2 ? ToDateSetterValue(realm, args, 2) : parts.Millisecond
                    };
                });
        }, "setUTCMinutes", 3);

        var setUtcMonthFn = new JsHostFunction(Realm, (in info) =>
        {
            return SetDateLike(info, "Date.prototype.setUTCMonth", true,
                static (realm, args, ref parts) =>
                {
                    parts = parts with
                    {
                        Month = ToDateSetterValue(realm, args, 0) + 1,
                        Day = args.Length > 1 ? ToDateSetterValue(realm, args, 1) : parts.Day
                    };
                });
        }, "setUTCMonth", 2);

        var setUtcSecondsFn = new JsHostFunction(Realm, (in info) =>
        {
            return SetDateLike(info, "Date.prototype.setUTCSeconds", true,
                static (realm, args, ref parts) =>
                {
                    parts = parts with
                    {
                        Second = ToDateSetterValue(realm, args, 0),
                        Millisecond = args.Length > 1 ? ToDateSetterValue(realm, args, 1) : parts.Millisecond
                    };
                });
        }, "setUTCSeconds", 2);

        var toPrimitiveFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var thisValue = info.ThisValue;
            if (!thisValue.IsObject)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Date.prototype [Symbol.toPrimitive] called on incompatible receiver");

            if (args.Length == 0 || !args[0].IsString)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Invalid hint");

            var hint = args[0].AsString();
            var preferString = hint switch
            {
                "string" => true,
                "default" => true,
                "number" => false,
                _ => throw new JsRuntimeException(JsErrorKind.TypeError, "Invalid hint")
            };

            return OrdinaryDateToPrimitive(realm, thisValue, preferString);
        }, "[Symbol.toPrimitive]", 1);

        Span<PropertyDefinition> protoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(DateConstructor)),
            PropertyDefinition.Mutable(IdGetDate, JsValue.FromObject(getDateFn)),
            PropertyDefinition.Mutable(IdGetDay, JsValue.FromObject(getDayFn)),
            PropertyDefinition.Mutable(IdGetFullYear, JsValue.FromObject(getFullYearFn)),
            PropertyDefinition.Mutable(IdGetHours, JsValue.FromObject(getHoursFn)),
            PropertyDefinition.Mutable(IdGetMilliseconds, JsValue.FromObject(getMillisecondsFn)),
            PropertyDefinition.Mutable(IdGetMinutes, JsValue.FromObject(getMinutesFn)),
            PropertyDefinition.Mutable(IdGetMonth, JsValue.FromObject(getMonthFn)),
            PropertyDefinition.Mutable(IdGetSeconds, JsValue.FromObject(getSecondsFn)),
            PropertyDefinition.Mutable(IdGetTime, JsValue.FromObject(getTimeFn)),
            PropertyDefinition.Mutable(IdGetTimezoneOffset, JsValue.FromObject(getTimezoneOffsetFn)),
            PropertyDefinition.Mutable(IdGetUtcDate, JsValue.FromObject(getUtcDateFn)),
            PropertyDefinition.Mutable(IdGetUtcDay, JsValue.FromObject(getUtcDayFn)),
            PropertyDefinition.Mutable(IdGetUtcFullYear, JsValue.FromObject(getUtcFullYearFn)),
            PropertyDefinition.Mutable(IdGetUtcHours, JsValue.FromObject(getUtcHoursFn)),
            PropertyDefinition.Mutable(IdGetUtcMilliseconds, JsValue.FromObject(getUtcMillisecondsFn)),
            PropertyDefinition.Mutable(IdGetUtcMinutes, JsValue.FromObject(getUtcMinutesFn)),
            PropertyDefinition.Mutable(IdGetUtcMonth, JsValue.FromObject(getUtcMonthFn)),
            PropertyDefinition.Mutable(IdGetUtcSeconds, JsValue.FromObject(getUtcSecondsFn)),
            PropertyDefinition.Mutable(IdSetDate, JsValue.FromObject(setDateFn)),
            PropertyDefinition.Mutable(IdSetFullYear, JsValue.FromObject(setFullYearFn)),
            PropertyDefinition.Mutable(IdSetHours, JsValue.FromObject(setHoursFn)),
            PropertyDefinition.Mutable(IdSetMilliseconds, JsValue.FromObject(setMillisecondsFn)),
            PropertyDefinition.Mutable(IdSetMinutes, JsValue.FromObject(setMinutesFn)),
            PropertyDefinition.Mutable(IdSetMonth, JsValue.FromObject(setMonthFn)),
            PropertyDefinition.Mutable(IdSetSeconds, JsValue.FromObject(setSecondsFn)),
            PropertyDefinition.Mutable(IdSetTime, JsValue.FromObject(setTimeFn)),
            PropertyDefinition.Mutable(IdSetUtcDate, JsValue.FromObject(setUtcDateFn)),
            PropertyDefinition.Mutable(IdSetUtcFullYear, JsValue.FromObject(setUtcFullYearFn)),
            PropertyDefinition.Mutable(IdSetUtcHours, JsValue.FromObject(setUtcHoursFn)),
            PropertyDefinition.Mutable(IdSetUtcMilliseconds, JsValue.FromObject(setUtcMillisecondsFn)),
            PropertyDefinition.Mutable(IdSetUtcMinutes, JsValue.FromObject(setUtcMinutesFn)),
            PropertyDefinition.Mutable(IdSetUtcMonth, JsValue.FromObject(setUtcMonthFn)),
            PropertyDefinition.Mutable(IdSetUtcSeconds, JsValue.FromObject(setUtcSecondsFn)),
            PropertyDefinition.Mutable(IdToLocaleDateString, JsValue.FromObject(toLocaleDateStringFn)),
            PropertyDefinition.Mutable(IdToLocaleString, JsValue.FromObject(toLocaleStringFn)),
            PropertyDefinition.Mutable(IdToLocaleTimeString, JsValue.FromObject(toLocaleTimeStringFn)),
            PropertyDefinition.Mutable(IdToDateString, JsValue.FromObject(toDateStringFn)),
            PropertyDefinition.Mutable(IdToIsoString, JsValue.FromObject(toIsoStringFn)),
            PropertyDefinition.Mutable(IdToJson, JsValue.FromObject(toJsonFn)),
            PropertyDefinition.Mutable(IdToString, JsValue.FromObject(toStringFn)),
            PropertyDefinition.Mutable(IdToTimeString, JsValue.FromObject(toTimeStringFn)),
            PropertyDefinition.Mutable(IdToUtcString, JsValue.FromObject(toUtcStringFn)),
            PropertyDefinition.Mutable(IdValueOf, JsValue.FromObject(valueOfFn)),
            PropertyDefinition.Const(IdSymbolToPrimitive, JsValue.FromObject(toPrimitiveFn),
                configurable: true)
        ];
        DatePrototype.DefineNewPropertiesNoCollision(Realm, protoDefs);

        Span<PropertyDefinition> ctorDefs =
        [
            PropertyDefinition.Mutable(IdNow, JsValue.FromObject(nowFn)),
            PropertyDefinition.Mutable(IdParse, JsValue.FromObject(parseFn)),
            PropertyDefinition.Mutable(IdUTC, JsValue.FromObject(utcFn))
        ];
        DateConstructor.InitializePrototypeProperty(DatePrototype);
        DateConstructor.DefineNewPropertiesNoCollision(Realm, ctorDefs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double DateConstructorSingleArgumentToTimeValue(JsRealm realm, in JsValue value)
    {
        if (value.TryGetObject(out var obj) && obj is JsDateObject dateObject)
            return dateObject.TimeValue;

        var primitive = value.IsObject ? ToPrimitiveDefaultSlowPath(realm, value) : value;
        if (primitive.IsString)
            return ParseDateString(primitive.AsString());
        return TimeClip(realm.ToNumberSlowPath(primitive));
    }

    private static double DateConstructorMultipleArgumentsToTimeValue(JsRealm realm, ReadOnlySpan<JsValue> args)
    {
        var year = realm.ToNumberSlowPath(args[0]);
        var month = args.Length > 1 ? realm.ToNumberSlowPath(args[1]) : 0d;
        var date = args.Length > 2 ? realm.ToNumberSlowPath(args[2]) : 1d;
        var hours = args.Length > 3 ? realm.ToNumberSlowPath(args[3]) : 0d;
        var minutes = args.Length > 4 ? realm.ToNumberSlowPath(args[4]) : 0d;
        var seconds = args.Length > 5 ? realm.ToNumberSlowPath(args[5]) : 0d;
        var milliseconds = args.Length > 6 ? realm.ToNumberSlowPath(args[6]) : 0d;

        if (!AreFiniteDateComponents(year, month, date, hours, minutes, seconds, milliseconds))
            return double.NaN;

        var fullYear = MakeFullYear(year);
        var day = MakeDay(fullYear, month, date);
        var time = MakeTime(hours, minutes, seconds, milliseconds);
        var localTime = MakeDate(day, time);

        var localYear = (int)Math.Truncate(fullYear);
        var localMonth = (int)Math.Truncate(month) + 1;
        var localDay = (int)Math.Truncate(date);
        var localHour = (int)Math.Truncate(hours);
        var localMinute = (int)Math.Truncate(minutes);
        var localSecond = (int)Math.Truncate(seconds);
        var localMillisecond = (int)Math.Truncate(milliseconds);
        NormalizeDateTimeParts(ref localYear, ref localMonth, ref localDay, ref localHour, ref localMinute,
            ref localSecond, ref localMillisecond);

        return TimeClip(localTime - GetLocalOffsetMilliseconds(localYear, localMonth, localDay, localHour, localMinute,
            localSecond, localMillisecond));
    }

    private static JsValue OrdinaryDateToPrimitive(JsRealm realm, JsValue thisValue, bool preferString)
    {
        var first = preferString ? IdToString : IdValueOf;
        var second = preferString ? IdValueOf : IdToString;
        var obj = thisValue.AsObject();

        if (TryInvokeOrdinaryDatePrimitiveMethod(realm, obj, thisValue, first, out var primitive))
            return primitive;
        if (TryInvokeOrdinaryDatePrimitiveMethod(realm, obj, thisValue, second, out primitive))
            return primitive;

        throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot convert object to primitive value");
    }

    private static bool TryInvokeOrdinaryDatePrimitiveMethod(JsRealm realm, JsObject obj, JsValue thisValue,
        int methodAtom, out JsValue primitive)
    {
        if (obj.TryGetPropertyAtom(realm, methodAtom, out var candidate, out _) &&
            candidate.TryGetObject(out var fnObj) && fnObj is JsFunction fn)
        {
            var value = realm.InvokeFunction(fn, thisValue, ReadOnlySpan<JsValue>.Empty);
            if (!value.IsObject)
            {
                primitive = value;
                return true;
            }
        }

        primitive = JsValue.Undefined;
        return false;
    }

    private static JsValue ToPrimitiveDefaultSlowPath(JsRealm realm, in JsValue value)
    {
        if (!value.IsObject)
            return value;

        var obj = value.AsObject();
        if (obj.TryGetPropertyAtom(realm, IdSymbolToPrimitive, out var exoticToPrim, out _) &&
            !exoticToPrim.IsUndefined && !exoticToPrim.IsNull)
        {
            if (!exoticToPrim.TryGetObject(out var exoticObj) || exoticObj is not JsFunction exoticFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "@@toPrimitive is not callable");

            var hint = JsValue.FromString("default");
            var hintArgs = MemoryMarshal.CreateReadOnlySpan(ref hint, 1);
            var exoticResult = realm.InvokeFunction(exoticFn, obj, hintArgs);
            if (exoticResult.IsObject)
                throw new JsRuntimeException(JsErrorKind.TypeError, "@@toPrimitive must return a primitive value");
            return exoticResult;
        }

        if (TryInvokeOrdinaryDatePrimitiveMethod(realm, obj, obj, IdValueOf, out var primitive))
            return primitive;
        if (TryInvokeOrdinaryDatePrimitiveMethod(realm, obj, obj, IdToString, out primitive))
            return primitive;

        throw new JsRuntimeException(JsErrorKind.TypeError, "Cannot convert object to primitive value");
    }

    internal static double ThisDateValue(JsRealm realm, JsValue thisValue, string methodName)
    {
        if (!thisValue.TryGetObject(out var obj) || obj is not JsDateObject date)
            throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} called on incompatible receiver");
        return date.TimeValue;
    }

    private static JsValue SetDateLike(in CallInfo info, string methodName, bool utc, DatePartsMutator mutator,
        bool useDefaultDateOnNaN = false, bool seedNaNFromUtcZero = false)
    {
        var realm = info.Realm;
        var args = info.Arguments;
        if (!info.ThisValue.TryGetObject(out var obj) || obj is not JsDateObject date)
            throw new JsRuntimeException(JsErrorKind.TypeError, $"{methodName} called on incompatible receiver");

        var currentTime = date.TimeValue;
        var initialNaN = double.IsNaN(currentTime);
        var parts = initialNaN
            ? GetDateTimeParts(0d, seedNaNFromUtcZero || utc)
            : GetDateTimeParts(currentTime, utc);

        mutator(realm, args, ref parts);

        if (initialNaN && !useDefaultDateOnNaN)
            return JsValue.NaN;

        var updated = ComposeTimeValue(parts, utc);
        updated = TimeClip(updated);
        date.TimeValue = updated;
        return new(updated);
    }

    private static int ToDateSetterValue(JsRealm realm, ReadOnlySpan<JsValue> args, int index)
    {
        var number = index < args.Length ? realm.ToNumberSlowPath(args[index]) : double.NaN;
        if (double.IsNaN(number) || double.IsInfinity(number))
            return int.MinValue;
        return (int)Math.Truncate(number);
    }

    private static double ComposeTimeValue(DateTimeParts parts, bool utc)
    {
        if (parts.Year == int.MinValue ||
            parts.Month == int.MinValue ||
            parts.Day == int.MinValue ||
            parts.Hour == int.MinValue ||
            parts.Minute == int.MinValue ||
            parts.Second == int.MinValue ||
            parts.Millisecond == int.MinValue)
            return double.NaN;

        var day = MakeDay(parts.Year, parts.Month - 1, parts.Day);
        var time = MakeTime(parts.Hour, parts.Minute, parts.Second, parts.Millisecond);
        var composed = MakeDate(day, time);
        if (utc)
            return composed;

        var localYear = parts.Year;
        var localMonth = parts.Month;
        var localDay = parts.Day;
        var localHour = parts.Hour;
        var localMinute = parts.Minute;
        var localSecond = parts.Second;
        var localMillisecond = parts.Millisecond;
        NormalizeDateTimeParts(ref localYear, ref localMonth, ref localDay, ref localHour, ref localMinute,
            ref localSecond, ref localMillisecond);

        return composed - GetLocalOffsetMilliseconds(localYear, localMonth, localDay, localHour, localMinute,
            localSecond, localMillisecond);
    }

    private static JsValue GetDateComponentValue(double timeValue, DateComponent component, bool utc)
    {
        if (double.IsNaN(timeValue))
            return JsValue.NaN;

        var parts = GetDateTimeParts(timeValue, utc);
        double result = component switch
        {
            DateComponent.Year => parts.Year,
            DateComponent.MonthZeroBased => parts.Month - 1,
            DateComponent.Date => parts.Day,
            DateComponent.Weekday => parts.WeekdayIndex,
            DateComponent.Hour => parts.Hour,
            DateComponent.Minute => parts.Minute,
            DateComponent.Second => parts.Second,
            _ => parts.Millisecond
        };
        return new(result);
    }

    private static double GetTimezoneOffsetMinutes(double timeValue)
    {
        if (double.IsNaN(timeValue))
            return double.NaN;

        var epochMilliseconds = (long)Math.Truncate(timeValue);
        var localParts = GetDateTimeParts(timeValue, false);
        var utcFromLocal = MakeDate(
            MakeDay(localParts.Year, localParts.Month - 1, localParts.Day),
            MakeTime(localParts.Hour, localParts.Minute, localParts.Second, localParts.Millisecond));
        return (epochMilliseconds - utcFromLocal) / MsPerMinute;
    }

    private static bool AreFiniteDateComponents(params double[] values)
    {
        for (var i = 0; i < values.Length; i++)
            if (double.IsNaN(values[i]) || double.IsInfinity(values[i]))
                return false;

        return true;
    }

    private static double ParseDateString(string text)
    {
        if (TryParseEcmaDateTime(text, out var timeValue) ||
            TryParsePartialEcmaDate(text, out timeValue) ||
            TryParseRfc1123Date(text, out timeValue) ||
            TryParseLegacyLocalDateString(text, out timeValue))
            return TimeClip(timeValue);

        return double.NaN;
    }

    private static bool TryParseEcmaDateTime(string text, out double timeValue)
    {
        var match = EcmaIsoDateTimeRegex().Match(text);
        if (!match.Success)
        {
            timeValue = double.NaN;
            return false;
        }

        var yearText = match.Groups["year"].Value;
        if (yearText == "-000000")
        {
            timeValue = double.NaN;
            return false;
        }

        var year = int.Parse(yearText, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        var hasTime = match.Groups["hour"].Success;

        var hour = 0;
        var minute = 0;
        var second = 0;
        var millisecond = 0;
        if (hasTime)
        {
            hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
            minute = int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture);
            second = match.Groups["second"].Success
                ? int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture)
                : 0;

            if (match.Groups["millisecond"].Success)
            {
                var msText = match.Groups["millisecond"].Value;
                millisecond = msText.Length switch
                {
                    1 => int.Parse(msText, CultureInfo.InvariantCulture) * 100,
                    2 => int.Parse(msText, CultureInfo.InvariantCulture) * 10,
                    _ => int.Parse(msText, CultureInfo.InvariantCulture)
                };
            }
        }

        if (!IsValidDateParts(year, month, day) || !IsValidTimeParts(hour, minute, second, millisecond))
        {
            timeValue = double.NaN;
            return false;
        }

        if (hour == 24)
        {
            if (minute != 0 || second != 0 || millisecond != 0)
            {
                timeValue = double.NaN;
                return false;
            }

            hour = 0;
            day++;
            NormalizeDateParts(ref year, ref month, ref day);
        }

        var dayValue = MakeDay(year, month - 1, day);
        var timePart = MakeTime(hour, minute, second, millisecond);
        timeValue = MakeDate(dayValue, timePart);

        var offsetText = match.Groups["offset"].Success ? match.Groups["offset"].Value : string.Empty;
        if (!hasTime || string.IsNullOrEmpty(offsetText) || offsetText == "Z")
        {
            if (hasTime && string.IsNullOrEmpty(offsetText))
                timeValue -= GetLocalOffsetMilliseconds(year, month, day, hour, minute, second, millisecond);
            return true;
        }

        var offsetHours = int.Parse(offsetText.AsSpan(1, 2), CultureInfo.InvariantCulture);
        var offsetMinutes = int.Parse(offsetText.AsSpan(4, 2), CultureInfo.InvariantCulture);
        var sign = offsetText[0] == '-' ? -1 : 1;
        timeValue -= sign * ((offsetHours * 60d + offsetMinutes) * MsPerMinute);
        return true;
    }

    private static bool TryParsePartialEcmaDate(string text, out double timeValue)
    {
        var match = EcmaPartialDateRegex().Match(text);
        if (!match.Success)
        {
            timeValue = double.NaN;
            return false;
        }

        var yearText = match.Groups["year"].Value;
        if (yearText == "-000000")
        {
            timeValue = double.NaN;
            return false;
        }

        var year = int.Parse(yearText, CultureInfo.InvariantCulture);
        var month = match.Groups["month"].Success
            ? int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture)
            : 1;
        var day = match.Groups["day"].Success ? int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture) : 1;

        if (!IsValidDateParts(year, month, day))
        {
            timeValue = double.NaN;
            return false;
        }

        timeValue = MakeDate(MakeDay(year, month - 1, day), 0d);
        return true;
    }

    private static bool TryParseRfc1123Date(string text, out double timeValue)
    {
        if (DateTimeOffset.TryParseExact(text, "r", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            timeValue = dto.ToUnixTimeMilliseconds();
            return true;
        }

        timeValue = double.NaN;
        return false;
    }

    private static bool TryParseLegacyLocalDateString(string text, out double timeValue)
    {
        var match = LegacyLocalDateStringRegex().Match(text);
        if (!match.Success)
        {
            timeValue = double.NaN;
            return false;
        }

        var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
        var month = ParseMonthName(match.Groups["monthName"].Value);
        var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture);
        var second = int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture);
        var offsetHour = int.Parse(match.Groups["offsetHour"].Value, CultureInfo.InvariantCulture);
        var offsetMinute = match.Groups["offsetMinute"].Success
            ? int.Parse(match.Groups["offsetMinute"].Value, CultureInfo.InvariantCulture)
            : 0;
        var sign = match.Groups["sign"].Value == "-" ? -1 : 1;

        if (!IsValidDateParts(year, month, day) || !IsValidTimeParts(hour, minute, second, 0))
        {
            timeValue = double.NaN;
            return false;
        }

        timeValue = MakeDate(MakeDay(year, month - 1, day), MakeTime(hour, minute, second, 0));
        timeValue -= sign * ((offsetHour * 60d + offsetMinute) * MsPerMinute);
        return true;
    }

    private static bool IsValidDateParts(int year, int month, int day)
    {
        if (month < 1 || month > 12 || day < 1)
            return false;

        return day <= DaysInMonth(year, month);
    }

    private static bool IsValidTimeParts(int hour, int minute, int second, int millisecond)
    {
        return hour is >= 0 and <= 24 &&
               minute is >= 0 and < 60 &&
               second is >= 0 and < 60 &&
               millisecond is >= 0 and < 1000;
    }

    private static void NormalizeDateParts(ref int year, ref int month, ref int day)
    {
        while (month < 1)
        {
            month += 12;
            year--;
        }

        while (month > 12)
        {
            month -= 12;
            year++;
        }

        while (day < 1)
        {
            month--;
            if (month < 1)
            {
                month = 12;
                year--;
            }

            day += DaysInMonth(year, month);
        }

        while (day > DaysInMonth(year, month))
        {
            day -= DaysInMonth(year, month);
            month++;
            if (month > 12)
            {
                month = 1;
                year++;
            }
        }
    }

    private static void NormalizeDateTimeParts(ref int year, ref int month, ref int day, ref int hour, ref int minute,
        ref int second, ref int millisecond)
    {
        NormalizeCarry(ref second, ref millisecond, 1000);
        NormalizeCarry(ref minute, ref second, 60);
        NormalizeCarry(ref hour, ref minute, 60);
        NormalizeCarry(ref day, ref hour, 24);
        NormalizeDateParts(ref year, ref month, ref day);
    }

    private static void NormalizeCarry(ref int upper, ref int lower, int radix)
    {
        if (lower >= 0)
        {
            upper += lower / radix;
            lower %= radix;
            return;
        }

        var borrow = (-lower + radix - 1) / radix;
        upper -= borrow;
        lower += borrow * radix;
    }

    private static int DaysInMonth(int year, int month)
    {
        return month switch
        {
            1 or 3 or 5 or 7 or 8 or 10 or 12 => 31,
            4 or 6 or 9 or 11 => 30,
            2 => IsLeapYear(year) ? 29 : 28,
            _ => 0
        };
    }

    private static bool IsLeapYear(int year)
    {
        if (year % 4 != 0)
            return false;
        if (year % 100 != 0)
            return true;
        return year % 400 == 0;
    }

    private static int ParseMonthName(string monthName)
    {
        return monthName switch
        {
            "Jan" => 1,
            "Feb" => 2,
            "Mar" => 3,
            "Apr" => 4,
            "May" => 5,
            "Jun" => 6,
            "Jul" => 7,
            "Aug" => 8,
            "Sep" => 9,
            "Oct" => 10,
            "Nov" => 11,
            "Dec" => 12,
            _ => 0
        };
    }

    private static double MakeFullYear(double year)
    {
        if (double.IsNaN(year))
            return double.NaN;
        var truncated = Math.Truncate(year);
        if (truncated >= 0d && truncated <= 99d)
            return truncated + 1900d;
        return truncated;
    }

    private static double MakeDay(double year, double month, double date)
    {
        if (!AreFiniteDateComponents(year, month, date))
            return double.NaN;

        var y = (long)Math.Truncate(year);
        var m = (long)Math.Truncate(month);
        var dt = (long)Math.Truncate(date);
        var ym = y + FloorDiv(m, 12L);
        var mn = (int)Mod(m, 12L);
        return DaysFromCivil(ym, mn + 1, 1) + dt - 1;
    }

    private static double MakeTime(double hour, double minute, double second, double millisecond)
    {
        if (!AreFiniteDateComponents(hour, minute, second, millisecond))
            return double.NaN;

        return Math.Truncate(hour) * MsPerHour +
               Math.Truncate(minute) * MsPerMinute +
               Math.Truncate(second) * MsPerSecond +
               Math.Truncate(millisecond);
    }

    private static double MakeDate(double day, double time)
    {
        if (double.IsNaN(day) || double.IsNaN(time))
            return double.NaN;
        return day * MsPerDay + time;
    }

    private static double TimeClip(double time)
    {
        if (double.IsNaN(time) || double.IsInfinity(time) || Math.Abs(time) > MaxTimeClip)
            return double.NaN;

        return Math.Truncate(time) + 0d;
    }

    private static int FloorDiv(int value, int divisor)
    {
        var q = value / divisor;
        var r = value % divisor;
        if (r != 0 && r < 0 != divisor < 0)
            q--;
        return q;
    }

    private static long FloorDiv(long value, long divisor)
    {
        var q = value / divisor;
        var r = value % divisor;
        if (r != 0 && r < 0 != divisor < 0)
            q--;
        return q;
    }

    private static long Mod(long value, long divisor)
    {
        var result = value % divisor;
        if (result < 0)
            result += Math.Abs(divisor);
        return result;
    }

    private static int Mod(int value, int divisor)
    {
        var result = value % divisor;
        if (result < 0)
            result += Math.Abs(divisor);
        return result;
    }

    private static long DaysFromCivil(long year, int month, int day)
    {
        year -= month <= 2 ? 1 : 0;
        var era = (year >= 0 ? year : year - 399) / 400;
        var yoe = year - era * 400;
        var doy = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1;
        var doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
        return era * 146097 + doe - 719468;
    }

    private static double GetLocalOffsetMilliseconds(int year, int month, int day, int hour, int minute, int second,
        int millisecond)
    {
        var offsetYear = year is < 1 or > 9999 ? MapToEquivalentTimeZoneYear(year) : year;

        var unspecified = new DateTime(offsetYear, month, day, hour, minute, second, millisecond,
            DateTimeKind.Unspecified);
        var offset = TimeZoneInfo.Local.GetUtcOffset(unspecified);
        return offset.TotalMilliseconds;
    }

    private static string FormatDateLocalString(double timeValue)
    {
        if (double.IsNaN(timeValue))
            return "Invalid Date";

        var local = GetDateTimeParts(timeValue, false);
        return string.Create(CultureInfo.InvariantCulture,
            $"{local.WeekdayName} {local.MonthName} {local.Day:00} {FormatDateYear(local.Year)} {local.Hour:00}:{local.Minute:00}:{local.Second:00} {FormatTimeZoneString(timeValue, local)}");
    }

    private static JsValue FormatDateWithIntl(JsRealm realm, double timeValue, ReadOnlySpan<JsValue> arguments,
        bool dateDefaults, bool timeDefaults)
    {
        if (double.IsNaN(timeValue) || double.IsInfinity(timeValue))
            return JsValue.FromString("Invalid Date");

        if (!realm.GlobalObject.TryGetPropertyAtom(realm, realm.Atoms.InternNoCheck("Intl"), out var intlValue,
                out _) ||
            !intlValue.TryGetObject(out var intlObject) ||
            !intlObject.TryGetPropertyAtom(realm, realm.Atoms.InternNoCheck("DateTimeFormat"), out var ctorValue,
                out _) ||
            !ctorValue.TryGetObject(out var ctorObject) ||
            ctorObject is not JsFunction ctorFn)
            return JsValue.FromString(dateDefaults
                ? timeDefaults ? FormatDateLocalString(timeValue) : FormatDateDateString(timeValue)
                : FormatDateTimeString(timeValue));

        var locales = arguments.Length > 0 ? arguments[0] : JsValue.Undefined;
        var options = arguments.Length > 1
            ? CreateDateLocaleOptions(realm, arguments[1], dateDefaults, timeDefaults)
            : CreateDateLocaleOptions(realm, JsValue.Undefined, dateDefaults, timeDefaults);
        var dateTimeFormatValue =
            realm.ConstructWithExplicitNewTarget(ctorFn, [locales, options], JsValue.FromObject(ctorFn), -1);
        if (dateTimeFormatValue.TryGetObject(out var dateTimeFormatObject) &&
            dateTimeFormatObject is JsDateTimeFormatObject dateTimeFormat)
            return JsValue.FromString(dateTimeFormat.Format(timeValue));

        return JsValue.FromString(dateDefaults
            ? timeDefaults ? FormatDateLocalString(timeValue) : FormatDateDateString(timeValue)
            : FormatDateTimeString(timeValue));
    }

    private static JsValue CreateDateLocaleOptions(JsRealm realm, JsValue optionsValue, bool dateDefaults,
        bool timeDefaults)
    {
        var options = new JsPlainObject(realm)
        {
            Prototype = realm.ObjectPrototype
        };

        var hasDateOption = false;
        var hasTimeOption = false;

        if (!optionsValue.IsUndefined && !optionsValue.IsNull)
        {
            if (!realm.TryToObject(optionsValue, out var source))
                throw new JsRuntimeException(JsErrorKind.TypeError, "options must be an object");

            CopyDateLocaleOptionIfPresent(realm, source, options, "localeMatcher");
            CopyDateLocaleOptionIfPresent(realm, source, options, "weekday", ref hasDateOption);
            CopyDateLocaleOptionIfPresent(realm, source, options, "era");
            CopyDateLocaleOptionIfPresent(realm, source, options, "year", ref hasDateOption);
            CopyDateLocaleOptionIfPresent(realm, source, options, "month", ref hasDateOption);
            CopyDateLocaleOptionIfPresent(realm, source, options, "day", ref hasDateOption);
            CopyDateLocaleOptionIfPresent(realm, source, options, "dayPeriod", ref hasTimeOption);
            CopyDateLocaleOptionIfPresent(realm, source, options, "hour", ref hasTimeOption);
            CopyDateLocaleOptionIfPresent(realm, source, options, "minute", ref hasTimeOption);
            CopyDateLocaleOptionIfPresent(realm, source, options, "second", ref hasTimeOption);
            CopyDateLocaleOptionIfPresent(realm, source, options, "fractionalSecondDigits", ref hasTimeOption);
            CopyDateLocaleOptionIfPresent(realm, source, options, "dateStyle");
            CopyDateLocaleOptionIfPresent(realm, source, options, "timeStyle");
            CopyDateLocaleOptionIfPresent(realm, source, options, "calendar");
            CopyDateLocaleOptionIfPresent(realm, source, options, "numberingSystem");
            CopyDateLocaleOptionIfPresent(realm, source, options, "hour12");
            CopyDateLocaleOptionIfPresent(realm, source, options, "hourCycle");
            CopyDateLocaleOptionIfPresent(realm, source, options, "timeZone");
            CopyDateLocaleOptionIfPresent(realm, source, options, "timeZoneName");
            CopyDateLocaleOptionIfPresent(realm, source, options, "formatMatcher");
        }

        var addDateDefaults = dateDefaults && (!timeDefaults ? !hasDateOption : !hasDateOption && !hasTimeOption);
        var addTimeDefaults = timeDefaults && (!dateDefaults ? !hasTimeOption : !hasDateOption && !hasTimeOption);

        if (addDateDefaults)
        {
            options.DefineDataPropertyAtom(realm, realm.Atoms.InternNoCheck("year"), JsValue.FromString("numeric"),
                JsShapePropertyFlags.Open);
            options.DefineDataPropertyAtom(realm, realm.Atoms.InternNoCheck("month"), JsValue.FromString("numeric"),
                JsShapePropertyFlags.Open);
            options.DefineDataPropertyAtom(realm, realm.Atoms.InternNoCheck("day"), JsValue.FromString("numeric"),
                JsShapePropertyFlags.Open);
        }

        if (addTimeDefaults)
        {
            options.DefineDataPropertyAtom(realm, realm.Atoms.InternNoCheck("hour"), JsValue.FromString("numeric"),
                JsShapePropertyFlags.Open);
            options.DefineDataPropertyAtom(realm, realm.Atoms.InternNoCheck("minute"), JsValue.FromString("numeric"),
                JsShapePropertyFlags.Open);
            options.DefineDataPropertyAtom(realm, realm.Atoms.InternNoCheck("second"), JsValue.FromString("numeric"),
                JsShapePropertyFlags.Open);
        }

        return JsValue.FromObject(options);
    }

    private static void CopyDateLocaleOptionIfPresent(JsRealm realm, JsObject source, JsPlainObject target,
        string name)
    {
        var atom = realm.Atoms.InternNoCheck(name);
        if (source.TryGetPropertyAtom(realm, atom, out var value, out _) && !value.IsUndefined)
            target.DefineDataPropertyAtom(realm, atom, value, JsShapePropertyFlags.Open);
    }

    private static void CopyDateLocaleOptionIfPresent(JsRealm realm, JsObject source, JsPlainObject target,
        string name, ref bool present)
    {
        var atom = realm.Atoms.InternNoCheck(name);
        if (source.TryGetPropertyAtom(realm, atom, out var value, out _) && !value.IsUndefined)
        {
            target.DefineDataPropertyAtom(realm, atom, value, JsShapePropertyFlags.Open);
            present = true;
        }
    }

    private static string FormatDateDateString(double timeValue)
    {
        if (double.IsNaN(timeValue))
            return "Invalid Date";

        var local = GetDateTimeParts(timeValue, false);
        return string.Create(CultureInfo.InvariantCulture,
            $"{local.WeekdayName} {local.MonthName} {local.Day:00} {FormatDateYear(local.Year)}");
    }

    private static string FormatDateTimeString(double timeValue)
    {
        if (double.IsNaN(timeValue))
            return "Invalid Date";

        var local = GetDateTimeParts(timeValue, false);
        return string.Create(CultureInfo.InvariantCulture,
            $"{local.Hour:00}:{local.Minute:00}:{local.Second:00} {FormatTimeZoneString(timeValue, local)}");
    }

    private static string FormatDateUtcString(double timeValue)
    {
        if (double.IsNaN(timeValue))
            return "Invalid Date";

        var parts = GetUtcDateTimeParts((long)timeValue);
        return string.Create(CultureInfo.InvariantCulture,
            $"{parts.WeekdayName}, {parts.Day:00} {parts.MonthName} {parts.Year:0000} {parts.Hour:00}:{parts.Minute:00}:{parts.Second:00} GMT");
    }

    private static string FormatDateIsoString(double timeValue)
    {
        var parts = GetUtcDateTimeParts((long)timeValue);
        var year = FormatIsoYear(parts.Year);
        return string.Create(CultureInfo.InvariantCulture,
            $"{year}-{parts.Month:00}-{parts.Day:00}T{parts.Hour:00}:{parts.Minute:00}:{parts.Second:00}.{parts.Millisecond:000}Z");
    }

    private static string FormatDateYear(int year)
    {
        if (year >= 0)
            return year.ToString(year <= 9999 ? "D4" : "D5", CultureInfo.InvariantCulture);

        var absYear = year == int.MinValue ? int.MaxValue : Math.Abs(year);
        return "-" + absYear.ToString(absYear <= 9999 ? "D4" : "D5", CultureInfo.InvariantCulture);
    }

    private static string FormatIsoYear(int year)
    {
        return year is >= 0 and <= 9999
            ? year.ToString("D4", CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture,
                $"{(year < 0 ? "-" : "+")}{Math.Abs(year):D6}");
    }

    private static string FormatTimeZoneString(double timeValue, DateTimeParts localParts)
    {
        var offsetMinutes = -(int)Math.Truncate(GetTimezoneOffsetMinutes(timeValue));
        var sign = offsetMinutes < 0 ? '-' : '+';
        var absoluteOffset = Math.Abs(offsetMinutes);
        var hour = absoluteOffset / 60;
        var minute = absoluteOffset % 60;
        return string.Create(CultureInfo.InvariantCulture, $"GMT{sign}{hour:00}{minute:00}");
    }

    private static DateTimeParts GetDateTimeParts(double timeValue, bool utc)
    {
        var epochMilliseconds = (long)Math.Truncate(timeValue);
        if (utc)
            return GetUtcDateTimeParts(epochMilliseconds);

        if (epochMilliseconds is >= -62135596800000L and <= 253402300799999L)
        {
            var local = DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds).ToLocalTime();
            return new(
                local.Year,
                local.Month,
                local.Day,
                local.Hour,
                local.Minute,
                local.Second,
                local.Millisecond,
                (int)local.DayOfWeek);
        }

        return GetOutOfRangeLocalDateTimeParts(epochMilliseconds);
    }

    private static DateTimeParts GetOutOfRangeLocalDateTimeParts(long epochMilliseconds)
    {
        var approximateOffset = GetLocalOffsetMillisecondsForEpoch(epochMilliseconds);
        var localEpochMilliseconds = epochMilliseconds + (long)Math.Truncate(approximateOffset);
        var localParts = GetUtcDateTimeParts(localEpochMilliseconds);
        var correctedOffset = GetLocalOffsetMilliseconds(localParts.Year, localParts.Month, localParts.Day,
            localParts.Hour, localParts.Minute, localParts.Second, localParts.Millisecond);

        if ((long)Math.Truncate(correctedOffset) != (long)Math.Truncate(approximateOffset))
        {
            localEpochMilliseconds = epochMilliseconds + (long)Math.Truncate(correctedOffset);
            localParts = GetUtcDateTimeParts(localEpochMilliseconds);
        }

        return localParts;
    }

    private static double GetLocalOffsetMillisecondsForEpoch(long epochMilliseconds)
    {
        var utcParts = GetUtcDateTimeParts(epochMilliseconds);
        return GetLocalOffsetMilliseconds(utcParts.Year, utcParts.Month, utcParts.Day, utcParts.Hour, utcParts.Minute,
            utcParts.Second, utcParts.Millisecond);
    }

    private static DateTimeParts GetUtcDateTimeParts(long epochMilliseconds)
    {
        var day = FloorDiv(epochMilliseconds, (long)MsPerDay);
        var timeWithinDay = epochMilliseconds - day * (long)MsPerDay;
        if (timeWithinDay < 0)
        {
            timeWithinDay += (long)MsPerDay;
            day--;
        }

        CivilFromDays((int)day, out var year, out var month, out var date);
        var hour = (int)(timeWithinDay / (long)MsPerHour);
        timeWithinDay %= (long)MsPerHour;
        var minute = (int)(timeWithinDay / (long)MsPerMinute);
        timeWithinDay %= (long)MsPerMinute;
        var second = (int)(timeWithinDay / (long)MsPerSecond);
        var millisecond = (int)(timeWithinDay % (long)MsPerSecond);
        var weekdayIndex = Mod((int)(day + 4), 7);

        return new(
            year,
            month,
            date,
            hour,
            minute,
            second,
            millisecond,
            weekdayIndex);
    }

    private static void CivilFromDays(int days, out int year, out int month, out int day)
    {
        days += 719468;
        var era = (days >= 0 ? days : days - 146096) / 146097;
        var doe = days - era * 146097;
        var yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
        year = yoe + era * 400;
        var doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        var mp = (5 * doy + 2) / 153;
        day = doy - (153 * mp + 2) / 5 + 1;
        month = mp + (mp < 10 ? 3 : -9);
        year += month <= 2 ? 1 : 0;
    }

    private static int MapToEquivalentTimeZoneYear(int year)
    {
        return 2000 + Mod(year - 2000, 400);
    }

    internal static bool TryTimeClipToEpochMillisecondsForIntl(double timeValue, out long epochMilliseconds)
    {
        var clipped = TimeClip(timeValue);
        if (double.IsNaN(clipped))
        {
            epochMilliseconds = 0;
            return false;
        }

        epochMilliseconds = checked((long)clipped);
        return true;
    }

    internal static OkojoEcmaDateTimeParts GetEcmaDateTimePartsForIntl(long epochMilliseconds, bool utc)
    {
        var parts = utc
            ? GetUtcDateTimeParts(epochMilliseconds)
            : GetDateTimeParts(epochMilliseconds, false);

        return new(
            parts.Year,
            parts.Month,
            parts.Day,
            parts.Hour,
            parts.Minute,
            parts.Second,
            parts.Millisecond,
            parts.WeekdayIndex);
    }

    private enum DateComponent
    {
        Year,
        MonthZeroBased,
        Date,
        Weekday,
        Hour,
        Minute,
        Second,
        Millisecond
    }

    private delegate void DatePartsMutator(JsRealm realm, ReadOnlySpan<JsValue> args, ref DateTimeParts parts);

    internal readonly record struct OkojoEcmaDateTimeParts(
        int Year,
        int Month,
        int Day,
        int Hour,
        int Minute,
        int Second,
        int Millisecond,
        int WeekdayIndex);

    private readonly record struct DateTimeParts(
        int Year,
        int Month,
        int Day,
        int Hour,
        int Minute,
        int Second,
        int Millisecond,
        int WeekdayIndex)
    {
        internal string WeekdayName => WeekdayIndex switch
        {
            0 => "Sun",
            1 => "Mon",
            2 => "Tue",
            3 => "Wed",
            4 => "Thu",
            5 => "Fri",
            _ => "Sat"
        };

        internal string MonthName => Month switch
        {
            1 => "Jan",
            2 => "Feb",
            3 => "Mar",
            4 => "Apr",
            5 => "May",
            6 => "Jun",
            7 => "Jul",
            8 => "Aug",
            9 => "Sep",
            10 => "Oct",
            11 => "Nov",
            _ => "Dec"
        };
    }
}
