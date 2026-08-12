using System.Collections.Frozen;

namespace Okojo.Globalization;

public static class UnitData
{
    private static readonly FrozenSet<string> RelativeTimeFormatUnits = new[]
    {
        "second",
        "seconds",
        "minute",
        "minutes",
        "hour",
        "hours",
        "day",
        "days",
        "week",
        "weeks",
        "month",
        "months",
        "quarter",
        "quarters",
        "year",
        "years",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> SimpleSanctionedUnits = new[]
    {
        "acre",
        "bit",
        "byte",
        "celsius",
        "centimeter",
        "day",
        "degree",
        "fahrenheit",
        "fluid-ounce",
        "foot",
        "gallon",
        "gigabit",
        "gigabyte",
        "gram",
        "hectare",
        "hour",
        "inch",
        "kilobit",
        "kilobyte",
        "kilogram",
        "kilometer",
        "liter",
        "megabit",
        "megabyte",
        "meter",
        "microsecond",
        "mile",
        "mile-scandinavian",
        "milliliter",
        "millimeter",
        "millisecond",
        "minute",
        "month",
        "nanosecond",
        "ounce",
        "percent",
        "petabyte",
        "pound",
        "second",
        "stone",
        "terabit",
        "terabyte",
        "week",
        "yard",
        "year",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly string[] SupportedValuesOfUnits =
    [
        .. SimpleSanctionedUnits.OrderBy(static value => value, StringComparer.Ordinal),
    ];

    /// <summary>Returns the sorted supported sanctioned unit values.</summary>
    public static string[] GetSupportedValues()
    {
        return SupportedValuesOfUnits;
    }

    /// <summary>Returns true if the unit is valid for RelativeTimeFormat.</summary>
    public static bool IsRelativeTimeFormatUnit(string unit)
    {
        return RelativeTimeFormatUnits.Contains(unit);
    }

    /// <summary>Returns true if the unit is a simple sanctioned unit.</summary>
    public static bool IsSimpleSanctionedUnit(string unit)
    {
        return SimpleSanctionedUnits.Contains(unit);
    }
}
