using System.Globalization;
using System.Numerics;

namespace Okojo.Runtime;

internal static class JsNumberPrecisionFormatting
{
    public static string FormatExponential(double value, int fractionDigits)
    {
        var digits = RoundToSignificantDigits(value, fractionDigits + 1);
        return BuildExponentialString(digits, fractionDigits + 1);
    }

    public static string FormatPrecision(double value, int precision)
    {
        var digits = RoundToSignificantDigits(value, precision);
        if (digits.Exponent < -6 || digits.Exponent >= precision)
            return BuildExponentialString(digits, precision);

        return BuildFixedString(digits, precision);
    }

    private static string BuildExponentialString(SignificantDigits digits, int significantDigits)
    {
        var sign = digits.Negative ? "-" : string.Empty;
        var mantissa = significantDigits == 1
            ? digits.Digits[..1]
            : digits.Digits[..1] + "." + digits.Digits[1..significantDigits];
        var exponentSign = digits.Exponent >= 0 ? "+" : "-";
        var absExponent = Math.Abs(digits.Exponent);
        return sign + mantissa + "e" + exponentSign + absExponent.ToString(CultureInfo.InvariantCulture);
    }

    private static string BuildFixedString(SignificantDigits digits, int precision)
    {
        var sign = digits.Negative ? "-" : string.Empty;
        var decimalPoint = digits.Exponent + 1;

        if (decimalPoint <= 0)
            return sign + "0." + new string('0', -decimalPoint) + digits.Digits;

        if (decimalPoint >= digits.Digits.Length)
            return sign + digits.Digits + new string('0', decimalPoint - digits.Digits.Length);

        return sign + digits.Digits[..decimalPoint] + "." + digits.Digits[decimalPoint..];
    }

    public static SignificantDigits RoundToSignificantDigits(double value, int significantDigits)
    {
        if (significantDigits < 1)
            throw new ArgumentOutOfRangeException(nameof(significantDigits));

        if (value == 0d)
            return new(BitConverter.DoubleToInt64Bits(value) < 0, 0, "0".PadRight(significantDigits, '0'));

        var (negative, numerator, denominator) = ToExactRational(value);
        var exponent = EstimateDecimalExponent(value, numerator, denominator);
        var scale = significantDigits - 1 - exponent;

        var scaledNumerator = numerator;
        var scaledDenominator = denominator;
        if (scale >= 0)
            scaledNumerator *= BigInteger.Pow(10, scale);
        else
            scaledDenominator *= BigInteger.Pow(10, -scale);

        var quotient = BigInteger.DivRem(scaledNumerator, scaledDenominator, out var remainder);
        if (remainder != 0 && remainder * 2 >= scaledDenominator)
            quotient += BigInteger.One;

        var threshold = BigInteger.Pow(10, significantDigits);
        if (quotient >= threshold)
        {
            quotient /= 10;
            exponent++;
        }

        var digits = quotient.ToString(CultureInfo.InvariantCulture);
        if (digits.Length < significantDigits)
            digits = digits.PadLeft(significantDigits, '0');

        return new(negative, exponent, digits);
    }

    private static (bool Negative, BigInteger Numerator, BigInteger Denominator) ToExactRational(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var negative = bits < 0;
        var magnitude = (ulong)(bits & 0x7FFF_FFFF_FFFF_FFFFL);
        var exponentBits = (int)((magnitude >> 52) & 0x7FFUL);
        var mantissaBits = magnitude & 0x000F_FFFF_FFFF_FFFFUL;

        BigInteger significand;
        int exponent2;
        if (exponentBits == 0)
        {
            significand = mantissaBits;
            exponent2 = -1022 - 52;
        }
        else
        {
            significand = (1UL << 52) | mantissaBits;
            exponent2 = exponentBits - 1023 - 52;
        }

        if (exponent2 >= 0)
            return (negative, significand << exponent2, BigInteger.One);

        return (negative, significand, BigInteger.One << -exponent2);
    }

    private static int EstimateDecimalExponent(double value, BigInteger numerator, BigInteger denominator)
    {
        var exponent = (int)Math.Floor(Math.Log10(Math.Abs(value)));
        if (CompareToPowerOf10(numerator, denominator, exponent + 1) >= 0)
        {
            do
            {
                exponent++;
            } while (CompareToPowerOf10(numerator, denominator, exponent + 1) >= 0);

            return exponent;
        }

        while (CompareToPowerOf10(numerator, denominator, exponent) < 0)
            exponent--;
        return exponent;
    }

    private static int CompareToPowerOf10(BigInteger numerator, BigInteger denominator, int exponent)
    {
        if (exponent >= 0)
            return numerator.CompareTo(denominator * BigInteger.Pow(10, exponent));
        return (numerator * BigInteger.Pow(10, -exponent)).CompareTo(denominator);
    }

    internal readonly record struct SignificantDigits(bool Negative, int Exponent, string Digits);
}
