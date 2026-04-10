using Okojo.Runtime;

namespace Okojo.Tests;

public class IntlPluralRulesTests
{
    [Test]
    public void Intl_PluralRules_Basic_Surface_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const desc = Object.getOwnPropertyDescriptor(Intl, "PluralRules");
                                const proto = Intl.PluralRules.prototype;
                                const tag = Object.getOwnPropertyDescriptor(proto, Symbol.toStringTag);
                                const pr = new Intl.PluralRules("en-US");
                                const options = pr.resolvedOptions();
                                [
                                  typeof Intl.PluralRules,
                                  desc.writable,
                                  desc.enumerable,
                                  desc.configurable,
                                  Object.getPrototypeOf(Intl.PluralRules) === Function.prototype,
                                  Object.getPrototypeOf(proto) === Object.prototype,
                                  tag.value,
                                  options.type,
                                  options.notation,
                                  options.minimumIntegerDigits,
                                  options.minimumFractionDigits,
                                  options.maximumFractionDigits,
                                  options.pluralCategories.join(",")
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "function|true|false|true|true|true|Intl.PluralRules|cardinal|standard|1|0|3|one,other"));
    }

    [Test]
    public void Intl_PluralRules_Select_And_SupportedLocalesOf_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const supported = Intl.PluralRules.supportedLocalesOf(["en-US", "zxx"]);
                                const en = new Intl.PluralRules("en-US");
                                const ar = new Intl.PluralRules("ar");
                                [
                                  supported.length,
                                  supported[0],
                                  en.select(1),
                                  en.select(2),
                                  ar.select(0),
                                  ar.select(2),
                                  ar.select(7)
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("1|en-US|one|other|zero|two|few"));
    }

    [Test]
    public void Intl_PluralRules_Notation_And_SelectRange_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const frStandard = new Intl.PluralRules("fr", { notation: "standard" });
                                const frCompact = new Intl.PluralRules("fr", { notation: "compact" });
                                [
                                  frStandard.select(1e6),
                                  frStandard.select(1.5e6),
                                  frCompact.select(1.5e6),
                                  frCompact.selectRange(1, 2)
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("many|other|many|other"));
    }

    [Test]
    public void Intl_PluralRules_ResolvedOptions_Tracks_Significant_Digits_And_Category_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const sig = new Intl.PluralRules("en", { minimumSignificantDigits: 1, maximumSignificantDigits: 2 }).resolvedOptions();
                                const sl = new Intl.PluralRules("sl").resolvedOptions();
                                [
                                  sig.minimumSignificantDigits,
                                  sig.maximumSignificantDigits,
                                  "minimumFractionDigits" in sig,
                                  "maximumFractionDigits" in sig,
                                  sl.pluralCategories.join(",")
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("1|2|false|false|one,two,few,other"));
    }
}
