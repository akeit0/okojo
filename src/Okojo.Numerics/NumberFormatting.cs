using System.Diagnostics;
using System.Globalization;
using System.Numerics;

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
        return Math.Abs(number) <= 9007199254740991d && number == Math.Truncate(number);
    }

    /// <summary>Formats a <see cref="double"/> per ECMAScript <c>Number::toString(10)</c>.</summary>
    public static string ToString(double number)
    {
        if (IsIntegralSafe(number))
            return ((long)number).ToString(CultureInfo.InvariantCulture);

        Span<char> buffer = stackalloc char[MaxLength];
        TryFormatShortest(number, buffer, out var written);
        return new string(buffer.Slice(0, written));
    }

    /// <summary>Formats a finite number in exponential notation with the shortest significand.</summary>
    internal static string ToExponentialString(double number)
    {
        if (number == 0d)
            return "0e+0";

        Span<char> digits = stackalloc char[MaxSignificantDigits];
        var success = TryGenerateShortestDigits(
            Math.Abs(number),
            digits,
            out var digitCount,
            out var decimalPoint
        );
        Debug.Assert(success);
        Span<char> buffer = stackalloc char[MaxLength];
        var signLength = number < 0 ? 1 : 0;
        if (signLength != 0)
            buffer[0] = '-';
        var length = WriteScientific(digits[..digitCount], decimalPoint - 1, buffer[signLength..]);
        return new string(buffer[..(signLength + length)]);
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

        if (IsIntegralSafe(number))
            return ((long)number).TryFormat(
                destination,
                out charsWritten,
                provider: CultureInfo.InvariantCulture
            );

        return TryFormatShortest(number, destination, out charsWritten);
    }

    private static bool TryFormatShortest(
        double number,
        Span<char> destination,
        out int charsWritten
    )
    {
        charsWritten = 0;
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
        if (
            !TryGenerateShortestDigits(
                Math.Abs(number),
                digits,
                out var digitCount,
                out var decimalPoint
            )
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
            position += WriteScientific(
                digits[..digitCount],
                decimalPoint - 1,
                destination[position..]
            );
        }

        charsWritten = position;
        return true;
    }

    private static int WriteScientific(
        ReadOnlySpan<char> digits,
        int exponent,
        Span<char> destination
    )
    {
        destination[0] = digits[0];
        var position = 1;
        if (digits.Length > 1)
        {
            destination[position++] = '.';
            digits[1..].CopyTo(destination[position..]);
            position += digits.Length - 1;
        }
        return position + WriteExponent(exponent, destination[position..]);
    }

    internal static int WriteExponent(int exponent, Span<char> destination)
    {
        destination[0] = 'e';
        destination[1] = exponent < 0 ? '-' : '+';
        var magnitude = Math.Abs(exponent);
        var position = 2;
        if (magnitude >= 100)
            destination[position++] = (char)('0' + magnitude / 100);
        if (magnitude >= 10)
            destination[position++] = (char)('0' + magnitude / 10 % 10);
        destination[position++] = (char)('0' + magnitude % 10);
        return position;
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
        // A normal power of two straddles two spacings. Outside the range where
        // the exact value already gives the shortest decimal, check both
        // rounding boundaries explicitly.
        var bits = BitConverter.DoubleToUInt64Bits(magnitude);
        if ((bits & ((1UL << 52) - 1)) == 0)
        {
            var binaryExponent = (int)(bits >> 52) - 1023;
            if (binaryExponent is < -22 or > 53)
            {
                GeneratePowerOfTwoDigits(
                    magnitude,
                    binaryExponent,
                    digits,
                    out digitCount,
                    out decimalPoint
                );
                return true;
            }
        }

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

    private static void GeneratePowerOfTwoDigits(
        double magnitude,
        int binaryExponent,
        Span<char> digits,
        out int digitCount,
        out int decimalPoint
    )
    {
        // Measure the value and its boundaries in units of 2^(binaryExponent-54).
        // The transition from subnormal to normal has equal spacing on both sides.
        var binaryUnit = binaryExponent - 54;
        var unit = BigInteger.One << Math.Max(0, binaryUnit);
        var denominator = BigInteger.One << Math.Max(0, -binaryUnit);
        var exponent = (int)Math.Floor(Math.Log10(magnitude));
        if (exponent < 0)
            unit *= BigInteger.Pow(10, -exponent);
        else
            denominator *= BigInteger.Pow(10, exponent);

        var numerator = unit << 54;
        // Correct Log10 if its floating-point result landed on the wrong side of a decade.
        if (numerator < denominator)
        {
            exponent--;
            unit *= 10;
            numerator *= 10;
        }
        else if (numerator >= denominator * 10)
        {
            exponent++;
            denominator *= 10;
        }

        var lowerMargin = binaryExponent == -1022 ? unit << 1 : unit;
        var upperMargin = unit << 1;
        var precision = 1;
        while (true)
        {
            var lower = BigInteger.DivRem(numerator, denominator, out var remainder);
            // The boundary points round to this value, whose binary significand is even.
            var lowerFits = remainder <= lowerMargin;
            var upperFits = denominator - remainder <= upperMargin;
            if (lowerFits || upperFits)
            {
                var twiceRemainder = remainder << 1;
                if (
                    upperFits
                    && (
                        !lowerFits
                        || twiceRemainder > denominator
                        || (twiceRemainder == denominator && !lower.IsEven)
                    )
                )
                    lower++;

                Span<char> rounded = stackalloc char[MaxSignificantDigits + 1];
                ((ulong)lower).TryFormat(
                    rounded,
                    out digitCount,
                    provider: CultureInfo.InvariantCulture
                );
                decimalPoint = exponent + 1 + (digitCount - precision);
                TrimTrailingZeros(rounded, ref digitCount);
                rounded[..digitCount].CopyTo(digits);
                return;
            }
            numerator *= 10;
            lowerMargin *= 10;
            upperMargin *= 10;
            precision++;
        }
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
