namespace Okojo.Numerics.Tests;

[TestFixture]
public class NumberFormattingTests
{
    [TestCase(double.NaN, "NaN")]
    [TestCase(double.PositiveInfinity, "Infinity")]
    [TestCase(double.NegativeInfinity, "-Infinity")]
    [TestCase(0.0, "0")]
    [TestCase(-0.0, "0")]
    [TestCase(1.0, "1")]
    [TestCase(-1.0, "-1")]
    [TestCase(123.456, "123.456")]
    [TestCase(0.000001, "0.000001")]
    [TestCase(0.0000001, "1e-7")]
    [TestCase(1e21, "1e+21")]
    [TestCase(1e-6, "0.000001")]
    [TestCase(12345678901234567890.0, "12345678901234567000")]
    public void ToJsString_MatchesEcmaNumberToString(double value, string expected)
    {
        Assert.That(NumberFormatting.ToString(value), Is.EqualTo(expected));
    }
}

[TestFixture]
public class NumberPrecisionFormattingTests
{
    [TestCase(123.456, 2, "1.23e+2")]
    [TestCase(123.456, 5, "1.23456e+2")]
    [TestCase(0.00123456, 3, "1.235e-3")]
    public void FormatExponential_ProducesExpected(double value, int fractionDigits, string expected)
    {
        Assert.That(NumberPrecisionFormatting.FormatExponential(value, fractionDigits), Is.EqualTo(expected));
    }

    [TestCase(123.456, 2, "1.2e+2")]
    [TestCase(123.456, 6, "123.456")]
    [TestCase(0.000123, 4, "0.0001230")]
    [TestCase(1234.5, 4, "1235")]
    public void FormatPrecision_ProducesExpected(double value, int precision, string expected)
    {
        Assert.That(NumberPrecisionFormatting.FormatPrecision(value, precision), Is.EqualTo(expected));
    }

    [TestCase(0.0, false, 0, "0")]
    [TestCase(1.0, false, 0, "1")]
    [TestCase(-2.5, true, 0, "25")]
    [TestCase(123.456, false, 2, "1235")]
    public void RoundToSignificantDigits_ProducesExpected(double value, bool negative, int exponent, string digits)
    {
        var result = NumberPrecisionFormatting.RoundToSignificantDigits(value, digits.Length);
        Assert.Multiple(() =>
        {
            Assert.That(result.Negative, Is.EqualTo(negative));
            Assert.That(result.Exponent, Is.EqualTo(exponent));
            Assert.That(result.Digits, Is.EqualTo(digits));
        });
    }
}

[TestFixture]
public class SumPreciseTests
{
    [Test]
    public void Sum_Empty_ReturnsZero()
    {
        Assert.That(SumPrecise.Sum([]), Is.EqualTo(0.0));
    }

    [Test]
    public void Sum_SingleValue_ReturnsValue()
    {
        Assert.That(SumPrecise.Sum([42.5]), Is.EqualTo(42.5));
    }

    [Test]
    public void Sum_LargeAndSmall_PreservesPrecision()
    {
        var value = SumPrecise.Sum([1e16, 1.0, -1e16]);
        Assert.That(value, Is.EqualTo(1.0));
    }

    [Test]
    public void Sum_Cancellation_ReturnsExactResult()
    {
        var value = SumPrecise.Sum([1.0, 1e-100, -1e-100]);
        Assert.That(value, Is.EqualTo(1.0));
    }
}
