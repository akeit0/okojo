namespace Okojo.Globalization;

/// <summary>
///     Options for <see cref="PluralRules"/>. String values match the ECMA-402
///     plural-rules option names exactly.
/// </summary>
public sealed class PluralRulesOptions
{
    /// <summary><c>"cardinal"</c> or <c>"ordinal"</c>.</summary>
    public string PluralRuleType { get; set; } = "cardinal";

    /// <summary><c>"standard"</c>, <c>"compact"</c>, <c>"scientific"</c>, or <c>"engineering"</c>.</summary>
    public string Notation { get; set; } = "standard";
}

/// <summary>
///     Options for <see cref="Collator"/>. String values match the ECMA-402
///     collator option names exactly.
/// </summary>
public sealed class CollatorOptions
{
    /// <summary><c>"sort"</c> or <c>"search"</c>.</summary>
    public string Usage { get; set; } = "sort";

    /// <summary><c>"base"</c>, <c>"accent"</c>, <c>"case"</c>, or <c>"variant"</c>.</summary>
    public string Sensitivity { get; set; } = "variant";

    public bool IgnorePunctuation { get; set; }

    /// <summary><c>"default"</c>, <c>"phonebk"</c>, <c>"ducet"</c>, <c>"emoji"</c>, or <c>"eor"</c>.</summary>
    public string Collation { get; set; } = "default";

    public bool Numeric { get; set; }

    /// <summary><c>"false"</c>, <c>"upper"</c>, or <c>"lower"</c>.</summary>
    public string CaseFirst { get; set; } = "false";
}

/// <summary>
///     Options for <see cref="ListFormat"/>. String values match the ECMA-402
///     list-format option names exactly.
/// </summary>
public sealed class ListFormatOptions
{
    /// <summary><c>"conjunction"</c>, <c>"disjunction"</c>, or <c>"unit"</c>.</summary>
    public string Type { get; set; } = "conjunction";

    /// <summary><c>"long"</c>, <c>"short"</c>, or <c>"narrow"</c>.</summary>
    public string Style { get; set; } = "long";
}

/// <summary>
///     Options for <see cref="RelativeTimeFormat"/>. String values match the ECMA-402
///     relative-time-format option names exactly.
/// </summary>
public sealed class RelativeTimeFormatOptions
{
    /// <summary>Unicode locale extension numbering system, e.g. <c>"latn"</c>.</summary>
    public string NumberingSystem { get; set; } = "latn";

    /// <summary><c>"long"</c>, <c>"short"</c>, or <c>"narrow"</c>.</summary>
    public string Style { get; set; } = "long";

    /// <summary><c>"always"</c> or <c>"auto"</c>.</summary>
    public string Numeric { get; set; } = "always";
}

/// <summary>
///     Options for <see cref="NumberFormat"/>. String values match the ECMA-402
///     number-format option names exactly.
/// </summary>
public sealed class NumberFormatOptions
{
    /// <summary>Unicode locale extension numbering system, e.g. <c>"latn"</c>.</summary>
    public string NumberingSystem { get; set; } = "latn";

    /// <summary><c>"decimal"</c>, <c>"percent"</c>, <c>"currency"</c>, or <c>"unit"</c>.</summary>
    public string Style { get; set; } = "decimal";

    public string? Currency { get; set; }

    /// <summary><c>"code"</c>, <c>"symbol"</c>, <c>"narrowSymbol"</c>, or <c>"name"</c>.</summary>
    public string CurrencyDisplay { get; set; } = "symbol";

    /// <summary><c>"standard"</c> or <c>"accounting"</c>.</summary>
    public string CurrencySign { get; set; } = "standard";

    public string? Unit { get; set; }

    /// <summary><c>"short"</c>, <c>"narrow"</c>, or <c>"long"</c>.</summary>
    public string UnitDisplay { get; set; } = "short";

    /// <summary><c>"standard"</c>, <c>"scientific"</c>, <c>"engineering"</c>, or <c>"compact"</c>.</summary>
    public string Notation { get; set; } = "standard";

    /// <summary><c>"short"</c> or <c>"long"</c>.</summary>
    public string CompactDisplay { get; set; } = "short";

    public int MinimumIntegerDigits { get; set; } = 1;
    public int MinimumFractionDigits { get; set; }
    public int MaximumFractionDigits { get; set; } = 3;
    public int? MinimumSignificantDigits { get; set; }
    public int? MaximumSignificantDigits { get; set; }
    public bool MinimumSignificantDigitsExplicit { get; set; }
    public bool MaximumSignificantDigitsExplicit { get; set; }

    /// <summary><c>"auto"</c>, <c>"always"</c>, <c>"min2"</c>, or <c>"false"</c>.</summary>
    public string UseGrouping { get; set; } = "auto";

    /// <summary><c>"auto"</c>, <c>"never"</c>, <c>"always"</c>, <c>"exceptZero"</c>, or <c>"negative"</c>.</summary>
    public string SignDisplay { get; set; } = "auto";

    /// <summary><c>"ceil"</c>, <c>"floor"</c>, <c>"expand"</c>, <c>"trunc"</c>, <c>"halfCeil"</c>,
    /// <c>"halfFloor"</c>, <c>"halfExpand"</c>, <c>"halfTrunc"</c>, or <c>"halfEven"</c>.</summary>
    public string RoundingMode { get; set; } = "halfExpand";

    /// <summary><c>"auto"</c>, <c>"morePrecision"</c>, or <c>"lessPrecision"</c>.</summary>
    public string RoundingPriority { get; set; } = "auto";

    public int RoundingIncrement { get; set; } = 1;

    /// <summary><c>"auto"</c> or <c>"stripIfInteger"</c>.</summary>
    public string TrailingZeroDisplay { get; set; } = "auto";
}

/// <summary>
///     Options for <see cref="DateTimeFormat"/>. String values match the ECMA-402
///     date-time-format option names exactly.
/// </summary>
public sealed class DateTimeFormatOptions
{
    /// <summary><c>"gregory"</c>, <c>"japanese"</c>, <c>"buddhist"</c>, <c>"islamic"</c>, etc.</summary>
    public string Calendar { get; set; } = "gregory";

    /// <summary>Unicode locale extension numbering system, e.g. <c>"latn"</c>.</summary>
    public string NumberingSystem { get; set; } = "latn";

    /// <summary>IANA time zone name, e.g. <c>"UTC"</c> or <c>"America/Los_Angeles"</c>.</summary>
    public string TimeZone { get; set; } = "UTC";

    public bool UseDefaultTimeZoneForFormatting { get; set; }

    /// <summary><c>"h11"</c>, <c>"h12"</c>, <c>"h23"</c>, or <c>"h24"</c>.</summary>
    public string HourCycle { get; set; } = "h23";

    public bool? Hour12 { get; set; }
    public string? Weekday { get; set; }
    public string? Era { get; set; }
    public string? Year { get; set; }
    public string? Month { get; set; }
    public string? Day { get; set; }
    public string? DayPeriod { get; set; }
    public string? Hour { get; set; }
    public string? Minute { get; set; }
    public string? Second { get; set; }
    public int? FractionalSecondDigits { get; set; }
    public string? TimeZoneName { get; set; }

    /// <summary><c>"basic"</c> or <c>"best fit"</c>.</summary>
    public string FormatMatcher { get; set; } = "basic";

    /// <summary><c>"full"</c>, <c>"long"</c>, <c>"medium"</c>, or <c>"short"</c>.</summary>
    public string? DateStyle { get; set; }

    /// <summary><c>"full"</c>, <c>"long"</c>, <c>"medium"</c>, or <c>"short"</c>.</summary>
    public string? TimeStyle { get; set; }
}
