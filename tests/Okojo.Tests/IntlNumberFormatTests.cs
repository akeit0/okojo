using Okojo.Runtime;

namespace Okojo.Tests;

public class IntlNumberFormatTests
{
    [Test]
    public void Intl_NumberFormat_Basic_Surface_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const desc = Object.getOwnPropertyDescriptor(Intl, "NumberFormat");
                                const proto = Intl.NumberFormat.prototype;
                                const tag = Object.getOwnPropertyDescriptor(proto, Symbol.toStringTag);
                                const nf = new Intl.NumberFormat("en-US");
                                const options = nf.resolvedOptions();
                                [
                                  typeof Intl.NumberFormat,
                                  desc.writable,
                                  desc.enumerable,
                                  desc.configurable,
                                  Object.getPrototypeOf(Intl.NumberFormat) === Function.prototype,
                                  Object.getPrototypeOf(proto) === Object.prototype,
                                  tag.value,
                                  options.style,
                                  options.minimumIntegerDigits,
                                  options.minimumFractionDigits,
                                  options.maximumFractionDigits,
                                  options.useGrouping
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "function|true|false|true|true|true|Intl.NumberFormat|decimal|1|0|3|auto"));
    }

    [Test]
    public void Intl_NumberFormat_Format_And_Bound_Getter_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const nf = new Intl.NumberFormat("en-US");
                                const format = nf.format;
                                [
                                  typeof format,
                                  nf.format(123456.78),
                                  format(123456.78),
                                  nf.format(),
                                  nf.format(undefined)
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("function|123,456.78|123,456.78|NaN|NaN"));
    }

    [Test]
    public void Intl_NumberFormat_SupportedLocalesOf_And_Options_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const supported = Intl.NumberFormat.supportedLocalesOf(["en-US", "zxx"]);
                                const percent = new Intl.NumberFormat("en-US", { style: "percent", signDisplay: "always" });
                                const arab = new Intl.NumberFormat("en-US-u-nu-arab", { numberingSystem: "arab" });
                                [
                                  supported.length,
                                  supported[0],
                                  percent.format(0.25),
                                  arab.resolvedOptions().numberingSystem,
                                  arab.format(1234.5)
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("1|en-US|+25%|arab|١٬٢٣٤٫٥"));
    }

    [Test]
    public void Intl_NumberFormat_Notation_And_ToLocaleString_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const scientific = new Intl.NumberFormat("de-DE", { notation: "scientific" });
                                const engineering = new Intl.NumberFormat("de-DE", { notation: "engineering" });
                                const compact = new Intl.NumberFormat("en-US", { notation: "compact", compactDisplay: "short" });
                                [
                                  scientific.format(543),
                                  engineering.format(543000),
                                  compact.format(987654321),
                                  (123).toLocaleString(undefined, { style: "unit", unit: "kilometer-per-hour", unitDisplay: "short" })
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("5,43E2|543E3|988M|123 km/h"));
    }

    [Test]
    public void Number_ToLocaleString_Matches_Intl_NumberFormat_For_Default_Path()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const values = [0, -0, 1, -1, 5.5, 123.44501, 0.00000000000000000000000000000123, 12344501000000000000000000000000000, Infinity, -Infinity, NaN];
                                const nf = new Intl.NumberFormat();
                                const refValues = values.map(v => nf.format(v));
                                const actual = values.map(v => v.toLocaleString());
                                JSON.stringify(actual) === JSON.stringify(refValues);
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Intl_NumberFormat_Currency_Digits_And_Rounding_ResolvedOptions_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const usd = new Intl.NumberFormat([], { style: "currency", currency: "USD" }).resolvedOptions();
                                const sig = new Intl.NumberFormat([], { maximumSignificantDigits: 1, roundingMode: "floor" }).resolvedOptions();
                                [
                                  usd.minimumFractionDigits,
                                  usd.maximumFractionDigits,
                                  sig.minimumSignificantDigits,
                                  sig.maximumSignificantDigits,
                                  sig.roundingMode
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("2|2|1|1|floor"));
    }

    [Test]
    public void Intl_NumberFormat_Locale_Specific_Grouping_And_Units_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const india = new Intl.NumberFormat("en-IN");
                                const koShort = new Intl.NumberFormat("ko-KR", { style: "unit", unit: "kilometer-per-hour", unitDisplay: "short" });
                                const koLong = new Intl.NumberFormat("ko-KR", { style: "unit", unit: "kilometer-per-hour", unitDisplay: "long" });
                                const jaLong = new Intl.NumberFormat("ja-JP", { style: "unit", unit: "kilometer-per-hour", unitDisplay: "long" });
                                [
                                  india.format(100000),
                                  koShort.format(-987),
                                  koLong.format(-987),
                                  jaLong.format(-987)
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("1,00,000|-987km/h|시속 -987킬로미터|時速 -987 キロメートル"));
    }

    [Test]
    public void Intl_NumberFormat_ResolvedOptions_Cleans_Invalid_NumberingSystem_Extensions()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const a = new Intl.NumberFormat("en-u-nu-invalid", { numberingSystem: "invalid2" }).resolvedOptions();
                                const b = new Intl.NumberFormat("en-u-nu-arab", { numberingSystem: "invalid" }).resolvedOptions();
                                const c = new Intl.NumberFormat("en-u-nu-latn", { numberingSystem: "arab" }).resolvedOptions();
                                [
                                  a.locale,
                                  a.numberingSystem,
                                  b.locale,
                                  b.numberingSystem,
                                  c.locale,
                                  c.numberingSystem
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("en|latn|en-u-nu-arab|arab|en|arab"));
    }

    [Test]
    public void Intl_NumberFormat_UseGrouping_And_Option_Filtering_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const a = new Intl.NumberFormat("en-US", { useGrouping: true }).resolvedOptions();
                                const b = new Intl.NumberFormat("en-US", { useGrouping: null }).resolvedOptions();
                                const c = new Intl.NumberFormat("en-US", { notation: "compact", useGrouping: "false" }).resolvedOptions();
                                const d = new Intl.NumberFormat("en-US-u-cu-krw", { currency: "USD" }).resolvedOptions();
                                [
                                  a.useGrouping,
                                  String(b.useGrouping),
                                  c.useGrouping,
                                  d.locale,
                                  String(d.currency),
                                  "currency" in d
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("always|false|min2|en-US|undefined|false"));
    }

    [Test]
    public void Intl_NumberFormat_Unit_And_RoundingIncrement_Validation_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let unitError = "";
                                let roundingError = "";
                                try { new Intl.NumberFormat([], { style: "unit", unit: "acre-foot" }); } catch (e) { unitError = e.name; }
                                try { new Intl.NumberFormat([], { roundingIncrement: 5000.1 }); } catch (e) { roundingError = e.name; }
                                [unitError, roundingError].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("RangeError|RangeError"));
    }

    [Test]
    public void Intl_NumberFormat_Compact_EnIn_And_Adlm_Digits_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const compact = new Intl.NumberFormat("en-IN", { notation: "compact" });
                                const adlm = new Intl.NumberFormat("en-US-u-nu-adlm", { numberingSystem: "adlm", useGrouping: false });
                                [compact.format(100000), adlm.format(123)].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("1L|\uD83A\uDD51\uD83A\uDD52\uD83A\uDD53"));
    }

    [Test]
    public void Intl_NumberFormat_ExactDecimal_And_SignificantDigits_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const decimal = new Intl.NumberFormat("en-US", { maximumFractionDigits: 20 });
                                const sig = new Intl.NumberFormat("ja-JP-u-nu-arab", { useGrouping: false, minimumSignificantDigits: 3, maximumSignificantDigits: 5 });
                                const auto = new Intl.NumberFormat("ja-JP-u-nu-arab", { useGrouping: false, roundingPriority: "auto", minimumSignificantDigits: 2, minimumFractionDigits: 2 });
                                const more = new Intl.NumberFormat("ja-JP-u-nu-arab", { useGrouping: false, roundingPriority: "morePrecision", maximumSignificantDigits: 2, maximumFractionDigits: 2 });
                                const less = new Intl.NumberFormat("ja-JP-u-nu-arab", { useGrouping: false, roundingPriority: "lessPrecision", minimumSignificantDigits: 2, minimumFractionDigits: 2 });
                                const mixed = new Intl.NumberFormat("ja-JP-u-nu-arab", { useGrouping: false, roundingPriority: "morePrecision", minimumSignificantDigits: 1, minimumFractionDigits: 3 });
                                const huge = new Intl.NumberFormat("ja-JP-u-nu-arab", { useGrouping: false, minimumIntegerDigits: 3, minimumFractionDigits: 1, maximumFractionDigits: 3 });
                                [
                                  decimal.format("100000"),
                                  decimal.format("1.0000000000000001"),
                                  sig.format("0"),
                                  auto.format("1"),
                                  more.format("1.23"),
                                  less.format("1"),
                                  mixed.format(1.1),
                                  huge.format("12344501000000000000000000000000000")
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "100,000|1.0000000000000001|\u0660\u066B\u0660\u0660|\u0661\u066B\u0660|\u0661\u066B\u0662\u0663|\u0661\u066B\u0660\u0660|\u0661\u066B\u0661|١٢٣٤٤٥٠١٠٠٠٠٠٠٠٠٠٠٠٠٠٠٠٠٠٠٠٠٠٠٠٠٠٠٠٫٠"));
    }

    [Test]
    public void Intl_NumberFormat_FormatToParts_Matches_Format_For_Exact_Decimal_Path()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const nf = new Intl.NumberFormat("de", { maximumSignificantDigits: 3 });
                                [nf.format(123456.789), nf.formatToParts(123456.789).map(p => p.value).join("")].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("123.000|123.000"));
    }

    [Test]
    public void Intl_NumberFormat_ArabExt_Numbering_System_Uses_Extended_Arabic_Digits()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                new Intl.NumberFormat("en-u-nu-arabext", { useGrouping: false, minimumFractionDigits: 1 }).format(1234.5);
                                """);

        Assert.That(result.AsString(), Is.EqualTo("۱۲۳۴٫۵"));
    }

    [Test]
    public void Intl_NumberFormat_Accounting_Negative_Sign_Uses_Rounded_Zero_And_Locale_Symbol()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const nf = new Intl.NumberFormat("zh-TW", {
                                  style: "currency",
                                  currency: "USD",
                                  currencySign: "accounting",
                                  signDisplay: "negative"
                                });
                                [
                                  nf.format(-987),
                                  nf.format(-0.0001),
                                  nf.format(-0)
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("(US$987.00)|US$0.00|US$0.00"));
    }

    [Test]
    public void Intl_NumberFormat_Currency_FormatToParts_Uses_Locale_Patterns()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const en = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", currencySign: "accounting", signDisplay: "always" });
                                const de = new Intl.NumberFormat("de-DE", { style: "currency", currency: "USD", currencySign: "accounting", signDisplay: "always" });
                                [
                                  en.formatToParts(-987).map(p => `${p.type}:${p.value}`).join(","),
                                  en.format(987),
                                  de.formatToParts(-987).map(p => `${p.type}:${p.value}`).join(","),
                                  de.format(987),
                                  new Intl.NumberFormat("ko-KR", { style: "currency", currency: "USD", currencySign: "accounting" }).format(987)
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "literal:(,currency:$,integer:987,decimal:.,fraction:00,literal:)|+$987.00|" +
            "minusSign:-,integer:987,decimal:,,fraction:00,literal:\u00A0,currency:$|+987,00\u00A0$|US$987.00"));
    }

    [Test]
    public void Intl_NumberFormat_Unit_And_Percent_FormatToParts_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const ko = new Intl.NumberFormat("ko-KR", { style: "unit", unit: "kilometer-per-hour", unitDisplay: "long" });
                                const zh = new Intl.NumberFormat("zh-TW", { style: "unit", unit: "kilometer-per-hour", unitDisplay: "long" });
                                const percent = new Intl.NumberFormat("en-US", { style: "percent" });
                                [
                                  ko.formatToParts(-987).map(p => `${p.type}:${p.value}`).join(","),
                                  zh.formatToParts(987).map(p => `${p.type}:${p.value}`).join(","),
                                  percent.formatToParts(-123).map(p => `${p.type}:${p.value}`).join(",")
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "unit:시속,literal: ,minusSign:-,integer:987,unit:킬로미터|" +
            "unit:每小時,literal: ,integer:987,literal: ,unit:公里|" +
            "minusSign:-,integer:12,group:,,integer:300,percentSign:%"));
    }

    [Test]
    public void Intl_NumberFormat_Compact_And_Exponent_FormatToParts_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const en = new Intl.NumberFormat("en-US", { notation: "compact", compactDisplay: "short" });
                                const de = new Intl.NumberFormat("de-DE", { notation: "compact", compactDisplay: "short" });
                                const sci = new Intl.NumberFormat("en-US", { notation: "scientific" });
                                const eng = new Intl.NumberFormat("en-US", { notation: "engineering" });
                                [
                                  en.formatToParts(987654321).map(p => `${p.type}:${p.value}`).join(","),
                                  de.format(10000),
                                  sci.formatToParts(0.000345).map(p => `${p.type}:${p.value}`).join(","),
                                  eng.formatToParts(543000).map(p => `${p.type}:${p.value}`).join(","),
                                  new Intl.NumberFormat("ja-JP", { notation: "compact", compactDisplay: "short" }).format(987654321),
                                  new Intl.NumberFormat("ko-KR", { notation: "compact", compactDisplay: "short" }).format(9876)
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "integer:988,compact:M|10.000|integer:3,decimal:.,fraction:45,exponentSeparator:E,exponentMinusSign:-,exponentInteger:4|" +
            "integer:543,exponentSeparator:E,exponentInteger:3|9.9億|9.9천"));
    }

    [Test]
    public void Intl_NumberFormat_FormatRange_Basic_Cases_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const en = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 });
                                const enAlways = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", signDisplay: "always" });
                                const pt = new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR", maximumFractionDigits: 0 });
                                [
                                  typeof Intl.NumberFormat.prototype.formatRange,
                                  typeof Intl.NumberFormat.prototype.formatRangeToParts,
                                  en.formatRange(3, 5),
                                  en.formatRange(2.9, 3.1),
                                  enAlways.formatRange(2.9, 3.1),
                                  pt.formatRange(3, 5),
                                  new Intl.NumberFormat("en-US").formatRange("987654321987654321", "987654321987654322")
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "function|function|$3 – $5|~$3|+$2.90–3.10|3 - 5\u00A0€|987,654,321,987,654,321–987,654,321,987,654,322"));
    }

    [Test]
    public void Intl_NumberFormat_FormatRangeToParts_Basic_Cases_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const nf = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 });
                                const a = nf.formatRangeToParts(3, 5).map(p => `${p.type}:${p.value}:${p.source}`).join(",");
                                const b = nf.formatRangeToParts(1, 1).map(p => `${p.type}:${p.value}:${p.source}`).join(",");
                                const c = typeof new Intl.NumberFormat().formatRangeToParts(23n, 12n);
                                [a, b, c].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "currency:$:startRange,integer:3:startRange,literal: – :shared,currency:$:endRange,integer:5:endRange|" +
            "approximatelySign:~:shared,currency:$:shared,integer:1:shared|object"));
    }
}
