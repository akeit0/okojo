using System.Globalization;

namespace Okojo.Numerics;

/// <summary>
///     ECMAScript <c>Number::toString(10)</c> shortest-decimal conversion,
///     written directly into caller-provided buffers without allocating.
/// </summary>
public static class NumberFormatting
{
    /// <summary>
    ///     Upper bound for any ECMAScript decimal rendering of a double:
    ///     optional sign plus at most 21 characters of fixed notation, or an
    ///     exponential form of at most 25 characters.
    /// </summary>
    public const int MaxLength = 26;

    private const int MaxSignificantDigits = 17;

    /// <summary>
    ///     True when ECMAScript <c>Number::toString(10)</c> renders
    ///     <paramref name="number"/> as plain integer digits, i.e. an integral
    ///     value within the safe-integer range.
    /// </summary>
    public static bool IsIntegralSafe(double number)
    {
        return !double.IsNaN(number)
            && !double.IsInfinity(number)
            && Math.Abs(number) <= 9007199254740991d
            && number == Math.Floor(number);
    }

    /// <summary>Formats a <see cref="double"/> per ECMAScript <c>Number::toString(10)</c>.</summary>
    public static string ToString(double number)
    {
        Span<char> buffer = stackalloc char[MaxLength];
        TryFormat(number, buffer, out var written);
        return new string(buffer.Slice(0, written));
    }

    /// <summary>
    ///     Zero-allocation equivalent of <see cref="ToString(double)" />. The
    ///     shortest significant digits come from a single .NET "R" round-trip
    ///     rendering (span-based); only the ECMAScript-specific assembly runs
    ///     here, so nothing allocates and the cost is one format plus copies.
    /// </summary>
    public static bool TryFormat(double number, Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;
        if (destination.Length < MaxLength)
            return false;

        if (double.IsNaN(number))
        {
            "NaN".CopyTo(destination);
            charsWritten = 3;
            return true;
        }
        if (double.IsPositiveInfinity(number))
        {
            "Infinity".CopyTo(destination);
            charsWritten = 8;
            return true;
        }
        if (double.IsNegativeInfinity(number))
        {
            "-Infinity".CopyTo(destination);
            charsWritten = 9;
            return true;
        }

        Span<char> digits = stackalloc char[MaxSignificantDigits];
        var digitCount = 0;
        var decimalPoint = 0; // value == 0.digits * 10^decimalPoint

        if (number == 0d)
        {
            digits[0] = '0';
            digitCount = 1;
            decimalPoint = 1;
        }
        else if (
            !TryGenerateShortestDigits(Math.Abs(number), digits, out digitCount, out decimalPoint)
        )
        {
            return false;
        }

        var position = 0;

        // ECMAScript ToString(-0) is "0": only strictly negative values get
        // the sign.
        if (number < 0)
            destination[position++] = '-';

        // Rendering rules over digits[0..digitCount), n = decimalPoint.
        if (digitCount <= decimalPoint && decimalPoint <= 21)
        {
            for (var i = 0; i < digitCount; i++)
                destination[position++] = digits[i];
            for (var i = digitCount; i < decimalPoint; i++)
                destination[position++] = '0';
        }
        else if (0 < decimalPoint && decimalPoint <= 21)
        {
            for (var i = 0; i < decimalPoint; i++)
                destination[position++] = digits[i];
            destination[position++] = '.';
            for (var i = decimalPoint; i < digitCount; i++)
                destination[position++] = digits[i];
        }
        else if (-6 < decimalPoint && decimalPoint <= 0)
        {
            destination[position++] = '0';
            destination[position++] = '.';
            for (var i = decimalPoint; i < 0; i++)
                destination[position++] = '0';
            for (var i = 0; i < digitCount; i++)
                destination[position++] = digits[i];
        }
        else
        {
            destination[position++] = digits[0];
            if (digitCount > 1)
            {
                destination[position++] = '.';
                for (var i = 1; i < digitCount; i++)
                    destination[position++] = digits[i];
            }

            destination[position++] = 'e';
            var exponentValue = decimalPoint - 1;
            if (exponentValue >= 0)
                destination[position++] = '+';
            else
            {
                destination[position++] = '-';
                exponentValue = -exponentValue;
            }

            if (exponentValue >= 100)
            {
                destination[position++] = (char)('0' + exponentValue / 100);
                destination[position++] = (char)('0' + exponentValue / 10 % 10);
                destination[position++] = (char)('0' + exponentValue % 10);
            }
            else if (exponentValue >= 10)
            {
                destination[position++] = (char)('0' + exponentValue / 10);
                destination[position++] = (char)('0' + exponentValue % 10);
            }
            else
            {
                destination[position++] = (char)('0' + exponentValue);
            }
        }

        charsWritten = position;
        return true;
    }

    /// <summary>
    /// Extracts the shortest significant-digit string of <paramref
    /// name="magnitude"/> from one .NET "R" rendering, along with its base-10
    /// decimal point position (value == 0.digits * 10^decimalPoint).
    /// </summary>
    private static bool TryGenerateShortestDigits(
        double magnitude,
        Span<char> digits,
        out int digitCount,
        out int decimalPoint
    )
    {
        Span<char> probe = stackalloc char[32];
        if (!magnitude.TryFormat(probe, out var length, "R", CultureInfo.InvariantCulture))
        {
            digitCount = 0;
            decimalPoint = 0;
            return false;
        }

        var mantissa = probe.Slice(0, length);

        var eIndex = mantissa.IndexOfAny((Span<char>)['E', 'e']);
        if (eIndex >= 0)
        {
            // Exponential form d.ddde(+|-)xx: value == dd.dd * 10^expValue.
            var expPart = mantissa.Slice(eIndex + 1);
            var negativeExponent = expPart[0] == '-';
            if (expPart[0] is '+' or '-')
                expPart = expPart.Slice(1);
            var expValue = int.Parse(expPart, CultureInfo.InvariantCulture);
            if (negativeExponent)
                expValue = -expValue;
            mantissa = mantissa.Slice(0, eIndex);
            digitCount = CopyDigits(mantissa, digits);
            decimalPoint = expValue + 1;
        }
        else
        {
            // Fixed form: the dot index inside the combined digit run decides
            // the point position; leading zeros shift it further left.
            var dotIndex = mantissa.IndexOf('.');
            var integerDigits = dotIndex >= 0 ? dotIndex : mantissa.Length;

            var write = 0;
            var skippedLeadingZeros = 0;
            for (var i = 0; i < mantissa.Length; i++)
            {
                var c = mantissa[i];
                if (c == '.')
                    continue;
                if (write == 0 && c == '0')
                {
                    skippedLeadingZeros++;
                    continue;
                }
                digits[write++] = c;
            }

            digitCount = write;
            decimalPoint = integerDigits - skippedLeadingZeros;
        }

        TrimTrailingZeros(digits, ref digitCount);
        if (digitCount > 0 && !(digitCount == 1 && digits[0] == '0'))
            return true;

        digitCount = 1;
        digits[0] = '0';
        decimalPoint = 1;
        return true;
    }

    private static int CopyDigits(ReadOnlySpan<char> source, Span<char> digits)
    {
        var write = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != '.')
                digits[write++] = source[i];
        }
        return write;
    }

    private static void TrimTrailingZeros(Span<char> digits, ref int digitCount)
    {
        while (digitCount > 1 && digits[digitCount - 1] == '0')
            digitCount--;
    }
}
