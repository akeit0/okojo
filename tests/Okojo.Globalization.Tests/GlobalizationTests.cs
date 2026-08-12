namespace Okojo.Globalization.Tests;

[TestFixture]
public class LocaleTests
{
    [TestCase("en-US", true)]
    [TestCase("en", true)]
    [TestCase("zh-Hans-CN", true)]
    [TestCase("und", true)]
    [TestCase("en_US", false)]
    [TestCase("", false)]
    [TestCase("_", false)]
    [TestCase("x", false)]
    [TestCase("en-a", false)]
    [TestCase("e", false)]
    public void IsStructurallyValidLanguageTag_MatchesBaseline(string tag, bool expected)
    {
        Assert.That(Locale.IsStructurallyValidLanguageTag(tag), Is.EqualTo(expected));
    }

    [TestCase("EN-us", "en-US")]
    [TestCase("zh-hans-cn", "zh-Hans-CN")]
    [TestCase("iw", "he")]
    [TestCase("ji", "yi")]
    [TestCase("art-lojban", "jbo")]
    public void CanonicalizeUnicodeLocaleId_Normalizes(string input, string expected)
    {
        Assert.That(Locale.CanonicalizeUnicodeLocaleId(input), Is.EqualTo(expected));
    }

    [TestCase("en_US", "en_us")]
    public void CanonicalizeUnicodeLocaleId_KeepsUnderscoreVariant(string input, string expected)
    {
        Assert.That(Locale.CanonicalizeUnicodeLocaleId(input), Is.EqualTo(expected));
    }

    [Test]
    public void TryGetValidatedCanonicalLocale_AcceptsValidTags()
    {
        Assert.That(Locale.TryGetValidatedCanonicalLocale("en-US", out var canonical), Is.True);
        Assert.That(canonical, Is.EqualTo("en-US"));
    }

    [Test]
    public void TryGetValidatedCanonicalLocale_RejectsInvalidTags()
    {
        Assert.That(Locale.TryGetValidatedCanonicalLocale("en_US", out _), Is.False);
        Assert.That(Locale.TryGetValidatedCanonicalLocale("", out _), Is.False);
    }

    [TestCase("en-u-ca-gregory", "en")]
    [TestCase("en-US", "en-US")]
    [TestCase("ja-JP-u-ca-japanese-hc-h12", "ja-JP")]
    public void RemoveUnicodeExtensions_StripsUnicodeExtension(string input, string expected)
    {
        Assert.That(Locale.RemoveUnicodeExtensions(input), Is.EqualTo(expected));
    }

    [TestCase("en-u-ca-gregory", true)]
    [TestCase("en-US", false)]
    public void ContainsUnicodeExtension_DetectsExtension(string input, bool expected)
    {
        Assert.That(Locale.ContainsUnicodeExtension(input), Is.EqualTo(expected));
    }

    [Test]
    public void ParseLanguageTag_SplitsComponents()
    {
        var parsed = Locale.ParseLanguageTag("zh-Hans-CN");
        Assert.Multiple(() =>
        {
            Assert.That(parsed.Language, Is.EqualTo("zh"));
            Assert.That(parsed.Script, Is.EqualTo("Hans"));
            Assert.That(parsed.Region, Is.EqualTo("CN"));
        });
    }

    [Test]
    public void ParseLanguageTag_CapturesUnicodeExtension()
    {
        var parsed = Locale.ParseLanguageTag("en-u-ca-gregory");
        Assert.Multiple(() =>
        {
            Assert.That(parsed.Language, Is.EqualTo("en"));
            Assert.That(parsed.Extensions, Is.Not.Null);
            Assert.That(parsed.Extensions!.Count, Is.EqualTo(1));
            Assert.That(parsed.Extensions[0].Type, Is.EqualTo('u'));
            Assert.That(parsed.Extensions[0].Parts, Does.Contain("gregory"));
        });
    }

    [TestCase("en-US", "en")]
    [TestCase("zh-Hans", "zh")]
    [TestCase("und", "und")]
    public void GetLanguageSubtag_ReturnsLanguage(string input, string expected)
    {
        Assert.That(Locale.GetLanguageSubtag(input), Is.EqualTo(expected));
    }

    [TestCase("abcd123", true)]
    [TestCase("abcd12", true)]
    [TestCase("1abc", true)]
    [TestCase("abc", false)]
    public void IsValidVariant_MatchesVariantShape(string part, bool expected)
    {
        Assert.That(Locale.IsValidVariant(part), Is.EqualTo(expected));
    }
}

[TestFixture]
public class IntlDataTests
{
    [TestCase("latn", true)]
    [TestCase("arab", true)]
    [TestCase("bogus", false)]
    public void OkojoIntlNumberingSystemData_IsSupported(string system, bool expected)
    {
        Assert.That(OkojoIntlNumberingSystemData.IsSupported(system), Is.EqualTo(expected));
    }

    [Test]
    public void OkojoIntlNumberingSystemData_TransliteratesArabicDigits()
    {
        Assert.That(OkojoIntlNumberingSystemData.TransliterateDigits("123", "arab"), Is.EqualTo("\u0661\u0662\u0663"));
    }

    [TestCase("gregory", true)]
    [TestCase("japanese", true)]
    [TestCase("bogus", false)]
    public void OkojoIntlCalendarData_IsSupportedCalendar(string calendar, bool expected)
    {
        Assert.That(OkojoIntlCalendarData.IsSupportedCalendar(calendar), Is.EqualTo(expected));
    }

    [Test]
    public void LikelySubtags_AddsLikelySubtags()
    {
        var maximized = LikelySubtags.AddLikelySubtags("zh");
        Assert.That(maximized, Is.EqualTo("zh-Hans-CN"));
    }

    [Test]
    public void LikelySubtags_RemovesLikelySubtags()
    {
        var minimized = LikelySubtags.RemoveLikelySubtags("zh-Hans-CN");
        Assert.That(minimized, Is.EqualTo("zh"));
    }

    [Test]
    public void OkojoIntlTimeZoneData_Canonicalizes()
    {
        Assert.That(OkojoIntlTimeZoneData.TryGetCanonicalTimeZone("UTC", out var canonical), Is.True);
        Assert.That(canonical, Is.EqualTo("UTC"));
    }
}
