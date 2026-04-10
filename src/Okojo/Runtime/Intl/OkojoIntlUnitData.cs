using System.Collections.Frozen;

namespace Okojo.Runtime.Intl;

internal static class OkojoIntlUnitData
{
    private static readonly FrozenSet<string> RelativeTimeFormatUnits = new[]
    {
        "second", "seconds", "minute", "minutes", "hour", "hours", "day", "days",
        "week", "weeks", "month", "months", "quarter", "quarters", "year", "years"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> SimpleSanctionedUnits = new[]
    {
        "acre", "bit", "byte", "celsius", "centimeter", "day", "degree", "fahrenheit",
        "fluid-ounce", "foot", "gallon", "gigabit", "gigabyte", "gram", "hectare", "hour",
        "inch", "kilobit", "kilobyte", "kilogram", "kilometer", "liter", "megabit",
        "megabyte", "meter", "microsecond", "mile", "mile-scandinavian", "milliliter",
        "millimeter", "millisecond", "minute", "month", "nanosecond", "ounce", "percent",
        "petabyte", "pound", "second", "stone", "terabit", "terabyte", "week", "yard", "year"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly string[] SupportedValuesOfUnits =
        [.. SimpleSanctionedUnits.OrderBy(static value => value, StringComparer.Ordinal)];

    internal static string[] GetSupportedValues()
    {
        return SupportedValuesOfUnits;
    }

    internal static bool IsRelativeTimeFormatUnit(string unit)
    {
        return RelativeTimeFormatUnits.Contains(unit);
    }

    internal static bool IsSimpleSanctionedUnit(string unit)
    {
        return SimpleSanctionedUnits.Contains(unit);
    }
}
