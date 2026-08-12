using System.Globalization;

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
    public void NumberingSystemData_IsSupported(string system, bool expected)
    {
        Assert.That(NumberingSystemData.IsSupported(system), Is.EqualTo(expected));
    }

    [Test]
    public void NumberingSystemData_TransliteratesArabicDigits()
    {
        Assert.That(NumberingSystemData.TransliterateDigits("123", "arab"), Is.EqualTo("\u0661\u0662\u0663"));
    }

    [TestCase("gregory", true)]
    [TestCase("japanese", true)]
    [TestCase("bogus", false)]
    public void CalendarData_IsSupportedCalendar(string calendar, bool expected)
    {
        Assert.That(CalendarData.IsSupportedCalendar(calendar), Is.EqualTo(expected));
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
    public void TimeZoneData_Canonicalizes()
    {
        Assert.That(TimeZoneData.TryGetCanonicalTimeZone("UTC", out var canonical), Is.True);
        Assert.That(canonical, Is.EqualTo("UTC"));
    }
}

[TestFixture]
public class PluralRulesTests
{
    private static PluralRules En() => new("en-US", "cardinal", "standard");

    [TestCase(1.0, "one")]
    [TestCase(2.0, "other")]
    [TestCase(0.0, "other")]
    public void EnglishCardinal_Selects(double value, string expected)
    {
        Assert.That(En().Select(value), Is.EqualTo(expected));
    }

    [Test]
    public void EnglishOrdinal_UsesOrdinalCategories()
    {
        var core = new PluralRules("en-US", "ordinal", "standard");
        Assert.That(core.Select(1), Is.EqualTo("one"));
        Assert.That(core.Select(2), Is.EqualTo("two"));
        Assert.That(core.Select(3), Is.EqualTo("few"));
        Assert.That(core.Select(7), Is.EqualTo("other"));
    }

    [Test]
    public void ArabicCardinal_UsesSixCategories()
    {
        var core = new PluralRules("ar", "cardinal", "standard");
        Assert.That(core.Select(0), Is.EqualTo("zero"));
        Assert.That(core.Select(1), Is.EqualTo("one"));
        Assert.That(core.Select(2), Is.EqualTo("two"));
        Assert.That(core.Select(3), Is.EqualTo("few"));
        Assert.That(core.Select(11), Is.EqualTo("many"));
    }

    [Test]
    public void GetPluralCategories_MatchesLocale()
    {
        Assert.That(En().GetPluralCategories(), Is.EqualTo(new[] { "one", "other" }));
        var ru = new PluralRules("ru", "cardinal", "standard").GetPluralCategories();
        Assert.That(ru, Is.EqualTo(new[] { "one", "few", "many", "other" }));
    }
}

[TestFixture]
public class StandaloneApiTests
{
    [Test]
    public void PluralRules_OptionsConstructor()
    {
        var rules = new PluralRules("en", new PluralRulesOptions { PluralRuleType = "ordinal" });
        Assert.That(rules.Select(2), Is.EqualTo("two"));
    }

    [Test]
    public void Collator_OptionsConstructor()
    {
        var collator = new Collator("en-US", new CollatorOptions { Numeric = true });
        Assert.That(collator.Compare("item2", "item10"), Is.LessThan(0));
    }

    [Test]
    public void ListFormat_OptionsConstructor()
    {
        var listFormat = new ListFormat("en", new ListFormatOptions { Style = "short" });
        Assert.That(listFormat.Format(["a", "b", "c"]), Is.EqualTo("a, b, & c"));
    }

    [Test]
    public void RelativeTimeFormat_OptionsConstructor()
    {
        var rtf = new RelativeTimeFormat("en", new RelativeTimeFormatOptions { Numeric = "auto" });
        Assert.That(rtf.Format(0, "day"), Is.EqualTo("today"));
    }

    [Test]
    public void NumberFormat_OptionsConstructor()
    {
        var nf = new NumberFormat("de-DE", new NumberFormatOptions
        {
            Style = "decimal",
            UseGrouping = "false",
            MinimumFractionDigits = 2
        });
        Assert.That(nf.Format(1234.5), Is.EqualTo("1234,50"));
    }

    [Test]
    public void DateTimeFormat_OptionsConstructor()
    {
        var dtf = new DateTimeFormat("en-US", new DateTimeFormatOptions
        {
            Year = "numeric",
            Month = "2-digit",
            Day = "2-digit"
        });
        var value = new DateTimeValue(1995, 12, 17, 3, 24, 56, 0, 0, null);
        var text = string.Concat(dtf.BuildParts(value).Select(p => p.Value));
        Assert.That(text, Does.Contain("1995"));
    }

    [Test]
    public void Locale_ResolvesCulture()
    {
        Assert.That(Locale.GetCultureInfo("en-US").Name, Is.EqualTo("en-US"));
        Assert.That(Locale.GetCompareInfo("fr-FR").Name, Is.EqualTo("fr-FR"));
    }
}

[TestFixture]
public class CollatorTests
{
    private static Collator EnCollator() =>
        new("en-US", "sort", "variant", false, "default", false, "false",
            CultureInfo.InvariantCulture.CompareInfo, CompareOptions.None);

    [Test]
    public void Compare_BasicOrdering()
    {
        var core = EnCollator();
        Assert.That(core.Compare("a", "b"), Is.LessThan(0));
        Assert.That(core.Compare("b", "a"), Is.GreaterThan(0));
        Assert.That(core.Compare("a", "a"), Is.EqualTo(0));
    }

    [Test]
    public void Compare_Numeric_OrdersDigitRunsNumerically()
    {
        var core = new Collator("en-US", "sort", "variant", false, "default", true, "false",
            CultureInfo.InvariantCulture.CompareInfo, CompareOptions.None);
        Assert.That(core.Compare("item2", "item10"), Is.LessThan(0));
        Assert.That(core.Compare("item10", "item2"), Is.GreaterThan(0));
    }

    [Test]
    public void Compare_CaseFirst_Upper_OrdersUpperCaseFirst()
    {
        var core = new Collator("en-US", "sort", "base", false, "default", false, "upper",
            CultureInfo.InvariantCulture.CompareInfo, CompareOptions.IgnoreCase);
        Assert.That(core.Compare("a", "A"), Is.GreaterThan(0));
    }
}

[TestFixture]
public class ListFormatTests
{
    [Test]
    public void Format_EnglishConjunction()
    {
        var core = new ListFormat("en-US", "conjunction", "long");
        Assert.That(core.Format(["a", "b"]), Is.EqualTo("a and b"));
        Assert.That(core.Format(["a", "b", "c"]), Is.EqualTo("a, b, and c"));
    }

    [Test]
    public void Format_EnglishDisjunction()
    {
        var core = new ListFormat("en-US", "disjunction", "long");
        Assert.That(core.Format(["a", "b"]), Is.EqualTo("a or b"));
        Assert.That(core.Format(["a", "b", "c"]), Is.EqualTo("a, b, or c"));
    }

    [Test]
    public void FormatToParts_ProducesElementAndLiteralParts()
    {
        var core = new ListFormat("en-US", "conjunction", "long");
        var parts = core.FormatToParts(["a", "b"]);
        Assert.That(parts.Count, Is.EqualTo(3));
        Assert.That(parts[0], Is.EqualTo(new IntlPart("element", "a")));
        Assert.That(parts[1], Is.EqualTo(new IntlPart("literal", " and ")));
        Assert.That(parts[2], Is.EqualTo(new IntlPart("element", "b")));
    }
}

[TestFixture]
public class RelativeTimeFormatTests
{
    private static RelativeTimeFormat En() =>
        new("en-US", "latn", "long", "always", CultureInfo.InvariantCulture);

    [Test]
    public void Format_Future()
    {
        Assert.That(En().Format(5, "day"), Is.EqualTo("in 5 days"));
    }

    [Test]
    public void Format_Past()
    {
        Assert.That(En().Format(-5, "day"), Is.EqualTo("5 days ago"));
    }

    [Test]
    public void Format_Auto_SpecialPhrases()
    {
        var core = new RelativeTimeFormat("en-US", "latn", "long", "auto", CultureInfo.InvariantCulture);
        Assert.That(core.Format(0, "day"), Is.EqualTo("today"));
        Assert.That(core.Format(-1, "day"), Is.EqualTo("yesterday"));
        Assert.That(core.Format(1, "day"), Is.EqualTo("tomorrow"));
    }
}

[TestFixture]
public class NumberFormatTests
{
    private static NumberFormat Decimal(string locale = "en-US", string grouping = "auto") =>
        new(locale, "latn", "decimal", null, "symbol", "standard", null, "short", "standard", "short",
            1, 0, 3, null, null, false, false, grouping, "auto", "halfExpand", "auto", 1, "auto",
            CultureInfo.InvariantCulture);

    [Test]
    public void Format_Decimal_Grouping()
    {
        Assert.That(Decimal().Format(1234567.89), Is.EqualTo("1,234,567.89"));
    }

    [Test]
    public void Format_NoGrouping()
    {
        Assert.That(Decimal(grouping: "false").Format(1234.5), Is.EqualTo("1234.5"));
    }

    [Test]
    public void Format_NaN_And_Infinity()
    {
        Assert.That(Decimal().Format(double.NaN), Is.EqualTo("NaN"));
        Assert.That(Decimal().Format(double.PositiveInfinity), Is.EqualTo("Infinity"));
        Assert.That(Decimal().Format(double.NegativeInfinity), Is.EqualTo("-Infinity"));
    }

    [Test]
    public void TryFormatExactString_RoundsBigIntegersExactly()
    {
        var core = Decimal();
        Assert.That(core.TryFormatExactString("12344501000000000000000000000000000", out var formatted), Is.True);
        Assert.That(formatted, Is.EqualTo("12,344,501,000,000,000,000,000,000,000,000,000"));
    }
}

[TestFixture]
public class DateTimeFormatTests
{
    private static DateTimeFormat ShortDate() =>
        new("en-US", "gregory", "latn", "UTC", false, "h23", null, null, null, "numeric", "2-digit",
            "2-digit", null, null, null, null, null, null, "basic", null, null, CultureInfo.InvariantCulture);

    [Test]
    public void BuildParts_FormatsDateFields()
    {
        var core = ShortDate();
        var value = new DateTimeValue(1995, 12, 17, 3, 24, 56, 0, 0, null);
        var parts = core.BuildParts(value);
        Assert.That(string.Concat(parts.Select(p => p.Value)), Does.Contain("12"));
        Assert.That(string.Concat(parts.Select(p => p.Value)), Does.Contain("17"));
        Assert.That(string.Concat(parts.Select(p => p.Value)), Does.Contain("1995"));
    }
}

