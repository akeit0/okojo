using System.Globalization;

namespace Okojo.Runtime;

internal static class JsNumberFormatting
{
    public static string ToJsString(double number)
    {
        if (double.IsNaN(number))
            return "NaN";
        if (double.IsPositiveInfinity(number))
            return "Infinity";
        if (double.IsNegativeInfinity(number))
            return "-Infinity";
        if (number == 0d)
            return "0";

        var roundTrip = number.ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = roundTrip.IndexOfAny(['E', 'e']);
        if (exponentIndex < 0)
            return roundTrip;

        var abs = Math.Abs(number);
        if (abs >= 1e21 || abs < 1e-6)
            return NormalizeExponent(roundTrip, exponentIndex);

        return ExpandExponent(roundTrip, exponentIndex);
    }

    private static string NormalizeExponent(string roundTrip, int exponentIndex)
    {
        var mantissa = roundTrip[..exponentIndex];
        var exponent = roundTrip[(exponentIndex + 1)..];
        var sign = exponent[0];
        var digits = exponent[1..].TrimStart('0');
        if (digits.Length == 0)
            digits = "0";
        return mantissa.ToLowerInvariant() + "e" + sign + digits;
    }

    private static string ExpandExponent(string roundTrip, int exponentIndex)
    {
        var negative = roundTrip[0] == '-';
        var mantissaStart = negative ? 1 : 0;
        var mantissa = roundTrip[mantissaStart..exponentIndex];
        var decimalPoint = mantissa.IndexOf('.');
        var digits = decimalPoint >= 0 ? mantissa.Remove(decimalPoint, 1) : mantissa;
        var integerDigits = decimalPoint >= 0 ? decimalPoint : mantissa.Length;
        var exponent = int.Parse(roundTrip[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
        var pointPosition = integerDigits + exponent;

        string expanded;
        if (pointPosition <= 0)
            expanded = "0." + new string('0', -pointPosition) + digits;
        else if (pointPosition >= digits.Length)
            expanded = digits + new string('0', pointPosition - digits.Length);
        else
            expanded = digits[..pointPosition] + "." + digits[pointPosition..];

        if (expanded.Contains('.'))
            expanded = expanded.TrimEnd('0').TrimEnd('.');

        return negative ? "-" + expanded : expanded;
    }
}
