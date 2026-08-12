using System.Globalization;
using Okojo.Globalization;

namespace Okojo.Runtime;

internal sealed class JsPluralRulesObject : JsObject
{
    private readonly PluralRules core;

    internal JsPluralRulesObject(
        JsRealm realm,
        JsObject prototype,
        string locale,
        string pluralRuleType,
        string notation,
        int minimumIntegerDigits,
        int? minimumFractionDigits,
        int? maximumFractionDigits,
        int? minimumSignificantDigits,
        int? maximumSignificantDigits,
        string roundingMode,
        string roundingPriority,
        int roundingIncrement,
        string trailingZeroDisplay,
        CultureInfo cultureInfo
    )
        : base(realm)
    {
        Prototype = prototype;
        Locale = locale;
        PluralRuleType = pluralRuleType;
        Notation = notation;
        MinimumIntegerDigits = minimumIntegerDigits;
        MinimumFractionDigits = minimumFractionDigits;
        MaximumFractionDigits = maximumFractionDigits;
        MinimumSignificantDigits = minimumSignificantDigits;
        MaximumSignificantDigits = maximumSignificantDigits;
        RoundingMode = roundingMode;
        RoundingPriority = roundingPriority;
        RoundingIncrement = roundingIncrement;
        TrailingZeroDisplay = trailingZeroDisplay;
        CultureInfo = cultureInfo;
        core = new(locale, pluralRuleType, notation);
    }

    internal string Locale { get; }
    internal string PluralRuleType { get; }
    internal string Notation { get; }
    internal int MinimumIntegerDigits { get; }
    internal int? MinimumFractionDigits { get; }
    internal int? MaximumFractionDigits { get; }
    internal int? MinimumSignificantDigits { get; }
    internal int? MaximumSignificantDigits { get; }
    internal string RoundingMode { get; }
    internal string RoundingPriority { get; }
    internal int RoundingIncrement { get; }
    internal string TrailingZeroDisplay { get; }
    internal CultureInfo CultureInfo { get; }

    internal string Select(double n)
    {
        return core.Select(n);
    }

    internal string[] GetPluralCategories()
    {
        return core.GetPluralCategories();
    }
}
