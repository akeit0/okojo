using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace Okojo.Numerics;

/// <summary>ECMAScript fixed and significant-digit Number formatting.</summary>
public static class NumberPrecisionFormatting
{
    private const int MaxFractionDigits = 100;
    private const int ScientificBufferLength = 1 + 1 + 1 + MaxFractionDigits + 5;

    private static ReadOnlySpan<ulong> PowersOfFive =>
        [
            1,
            5,
            25,
            125,
            625,
            3125,
            15625,
            78125,
            390625,
            1953125,
            9765625,
            48828125,
            244140625,
            1220703125,
            6103515625,
            30517578125,
            152587890625,
            762939453125,
        ];

    /// <summary>Formats a number with the requested count of decimal places.</summary>
    public static string FormatFixed(double value, int fractionDigits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fractionDigits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fractionDigits, MaxFractionDigits);
        var magnitude = Math.Abs(value);
        if (!(magnitude < 1e21))
            return NumberFormatting.ToString(value);

        // Sign, up to 21 integer digits, decimal point and fraction.
        Span<char> buffer = stackalloc char[1 + 21 + 1 + MaxFractionDigits];
        // For |value| < 10^21 and at most 17 fraction digits, value * 10^digits
        // and the unshifted significand * 5^digits both fit in UInt128.
        if (fractionDigits < PowersOfFive.Length)
        {
            var signLength = value < 0 ? 1 : 0;
            if (signLength != 0)
                buffer[0] = '-';
            var scaled = RoundScaledMagnitude(magnitude, fractionDigits);
            var charsWritten = WriteFixedDigits(scaled, fractionDigits, buffer[signLength..]);
            return new string(buffer[..(signLength + charsWritten)]);
        }

        var length = FormatDecimal(value == 0 ? 0d : value, 'F', fractionDigits, buffer);
        if (RoundsDownAtMidpoint(value, fractionDigits))
            buffer[length - 1]++;
        return new string(buffer[..length]);
    }

    private static UInt128 RoundScaledMagnitude(double magnitude, int fractionDigits)
    {
        var bits = BitConverter.DoubleToUInt64Bits(magnitude);
        var biasedExponent = (int)(bits >> 52);
        var shift = 1075 - Math.Max(biasedExponent, 1) - fractionDigits;
        if (shift >= 128)
            return 0;

        var significand = (bits & ((1UL << 52) - 1)) | (1UL << 52);
        // Zero and subnormals already returned above. The first discarded bit
        // gives round-half-up directly, including exact halfway values.
        var product = (UInt128)significand * PowersOfFive[fractionDigits];
        return shift > 0 ? (product >> shift) + ((product >> (shift - 1)) & 1) : product << -shift;
    }

    private static int WriteFixedDigits(UInt128 scaled, int fractionDigits, Span<char> buffer)
    {
        scaled.TryFormat(buffer, out var length, provider: CultureInfo.InvariantCulture);
        if (fractionDigits == 0)
            return length;

        var point = length - fractionDigits;
        if (point <= 0)
        {
            var prefixLength = 2 - point;
            buffer[..length].CopyTo(buffer[prefixLength..]);
            buffer[..prefixLength].Fill('0');
            buffer[1] = '.';
            return fractionDigits + 2;
        }

        buffer[point..length].CopyTo(buffer[(point + 1)..]);
        buffer[point] = '.';
        return length + 1;
    }

    /// <summary>Formats a number with a decimal exponent and the requested fraction digits.</summary>
    public static string FormatExponential(double value, int fractionDigits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fractionDigits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fractionDigits, MaxFractionDigits);
        return FormatSignificant(value, fractionDigits, preferFixed: false);
    }

    /// <summary>Formats a number with the requested count of significant digits.</summary>
    public static string FormatPrecision(double value, int precision)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(precision, MaxFractionDigits);
        return FormatSignificant(value, precision - 1, preferFixed: true);
    }

    private static string FormatSignificant(double value, int fractionDigits, bool preferFixed)
    {
        if (!double.IsFinite(value))
            return NumberFormatting.ToString(value);

        Span<char> buffer = stackalloc char[ScientificBufferLength];
        var signLength = value < 0 ? 1 : 0;
        if (
            TryFormatScaledSignificant(
                Math.Abs(value),
                fractionDigits,
                preferFixed,
                buffer[signLength..],
                out var written
            )
        )
        {
            if (signLength != 0)
                buffer[0] = '-';
            return new string(buffer[..(signLength + written)]);
        }

        var length = FormatDecimal(value == 0 ? 0d : value, 'E', fractionDigits, buffer);
        // The invariant E format always emits 'E', a sign and three exponent digits.
        var exponentStart = length - 5;
        var exponent =
            100 * (buffer[length - 3] - '0')
            + 10 * (buffer[length - 2] - '0')
            + (buffer[length - 1] - '0');
        if (buffer[exponentStart + 1] == '-')
            exponent = -exponent;

        if (RoundsDownAtMidpoint(value, fractionDigits - exponent))
            buffer[exponentStart - 1]++;

        if (preferFixed && exponent >= -6 && exponent <= fractionDigits)
        {
            length = PlaceDecimalPoint(buffer[signLength..], fractionDigits, exponent);
            return new string(buffer[..(signLength + length)]);
        }

        var exponentLength = NumberFormatting.WriteExponent(exponent, buffer[exponentStart..]);
        return new string(buffer[..(exponentStart + exponentLength)]);
    }

    private static bool TryFormatScaledSignificant(
        double magnitude,
        int fractionDigits,
        bool preferFixed,
        Span<char> buffer,
        out int length
    )
    {
        length = 0;
        if (fractionDigits >= PowersOfFive.Length - 1 || magnitude is < 0.1 or >= 1e16)
            return false;

        var integral = (ulong)magnitude;
        // log10(2) ≈ 1233 / 4096. The integer decade comparison corrects the
        // estimate, leaving only exponent -1 for magnitudes below one.
        var exponent = integral == 0 ? -1 : (BitOperations.Log2(integral) * 1233) >> 12;
        if (integral >= PowerOfTen(exponent + 1))
            exponent++;

        var scale = fractionDigits - exponent;
        ulong scaled;
        if (scale < 0)
        {
            // Multiples of ten have integer midpoints, so truncating the
            // input cannot change which side of a rounding boundary it is on.
            var divisor = PowerOfTen(-scale);
            scaled = (integral + divisor / 2) / divisor;
        }
        else
        {
            scaled = (ulong)RoundScaledMagnitude(magnitude, scale);
        }

        if (scaled == PowerOfTen(fractionDigits + 1))
        {
            scaled /= 10;
            exponent++;
        }

        if (preferFixed && exponent <= fractionDigits)
            length = WriteFixedDigits(scaled, fractionDigits - exponent, buffer);
        else
        {
            length = WriteFixedDigits(scaled, fractionDigits, buffer);
            length += NumberFormatting.WriteExponent(exponent, buffer[length..]);
        }
        return true;
    }

    private static ulong PowerOfTen(int exponent) => PowersOfFive[exponent] << exponent;

    private static int PlaceDecimalPoint(Span<char> mantissa, int fractionDigits, int exponent)
    {
        if (exponent < 0)
        {
            var firstDigit = mantissa[0];
            var prefixLength = 1 - exponent; // "0." followed by any leading zeroes.
            mantissa.Slice(2, fractionDigits).CopyTo(mantissa[(prefixLength + 1)..]);
            mantissa[..prefixLength].Fill('0');
            mantissa[1] = '.';
            mantissa[prefixLength] = firstDigit;
            return prefixLength + 1 + fractionDigits;
        }

        mantissa.Slice(2, exponent).CopyTo(mantissa[1..]);
        if (exponent == fractionDigits)
            return fractionDigits + 1;
        mantissa[exponent + 1] = '.';
        return fractionDigits + 2;
    }

    private static int FormatDecimal(double value, char specifier, int precision, Span<char> buffer)
    {
        Span<char> format = stackalloc char[4];
        format[0] = specifier;
        precision.TryFormat(
            format[1..],
            out var digitsWritten,
            provider: CultureInfo.InvariantCulture
        );
        var success = value.TryFormat(
            buffer,
            out var length,
            format[..(digitsWritten + 1)],
            CultureInfo.InvariantCulture
        );
        Debug.Assert(success);
        return length;
    }

    private static bool RoundsDownAtMidpoint(double value, int decimalScale)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        var biasedExponent = (int)((bits >> 52) & 0x7ff);
        var significand = bits & ((1UL << 52) - 1);
        if (biasedExponent != 0)
            significand |= 1UL << 52;
        if (significand == 0)
            return false;

        var trailingZeroes = BitOperations.TrailingZeroCount(significand);
        var binaryExponent = Math.Max(biasedExponent, 1) - 1075 + trailingZeroes;
        if (binaryExponent + decimalScale != -1)
            return false;

        // At a tie the scaled numerator is odd. Since 5^k is 1 modulo 4,
        // the lower integer is even exactly when the odd significand is 1 modulo 4.
        significand >>= trailingZeroes;
        if ((significand & 3) != 1)
            return false;

        // A negative scale divides by 5^(-scale). The tie requires exact divisibility.
        for (var scale = decimalScale; scale < 0; scale++)
        {
            if (significand % 5 != 0)
                return false;
            significand /= 5;
        }
        return true;
    }
}
