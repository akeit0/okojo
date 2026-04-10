using Okojo.Runtime;

namespace Okojo.Tests;

public class IntlLocaleTests
{
    [Test]
    public void Intl_Locale_Constructor_And_Getters_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const desc = Object.getOwnPropertyDescriptor(Intl, "Locale");
                                const tagDesc = Object.getOwnPropertyDescriptor(Intl.Locale.prototype, Symbol.toStringTag);
                                const loc = new Intl.Locale("de-latn-de-fonipa-1996-u-ca-gregory-co-phonebk-hc-h23-kf-true-kn-false-nu-latn");
                                const replaced = new Intl.Locale(loc, {
                                  language: "ja",
                                  script: "jpan",
                                  region: "jp",
                                  variants: "Hepburn",
                                  calendar: "japanese",
                                  collation: "search",
                                  hourCycle: "h24",
                                  caseFirst: "false",
                                  numeric: "true",
                                  numberingSystem: "jpanfin",
                                  firstDayOfWeek: 0
                                });
                                [
                                  typeof Intl.Locale === "function",
                                  desc.writable,
                                  desc.enumerable,
                                  desc.configurable,
                                  Object.prototype.toString.call(loc),
                                  loc.toString(),
                                  loc.baseName,
                                  loc.language,
                                  loc.script,
                                  loc.region,
                                  loc.variants,
                                  loc.calendar,
                                  loc.collation,
                                  loc.hourCycle,
                                  loc.caseFirst,
                                  String(loc.numeric),
                                  loc.numberingSystem,
                                  replaced.toString(),
                                  replaced.firstDayOfWeek,
                                  tagDesc.value
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "true|true|false|true|[object Intl.Locale]|de-Latn-DE-1996-fonipa-u-ca-gregory-co-phonebk-hc-h23-kf-kn-false-nu-latn|de-Latn-DE-1996-fonipa|de|Latn|DE|1996-fonipa|gregory|phonebk|h23||false|latn|ja-Jpan-JP-hepburn-u-ca-japanese-co-search-fw-sun-hc-h24-kf-false-kn-nu-jpanfin|sun|Intl.Locale"));
    }

    [Test]
    public void Intl_Locale_Prototype_Info_Methods_Return_Shaped_Results()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                class CustomLocale extends Intl.Locale {
                                  constructor(tag, options) {
                                    super(tag, options);
                                    this.custom = true;
                                  }
                                }
                                const custom = new CustomLocale("en-US-u-fw-mon-hc-h12");
                                const textInfo = custom.getTextInfo();
                                const weekInfo = custom.getWeekInfo();
                                const timeZones = custom.getTimeZones();
                                [
                                  custom.custom,
                                  Object.getPrototypeOf(custom) === CustomLocale.prototype,
                                  Intl.Locale.prototype.toString.call(custom),
                                  Array.isArray(custom.getCalendars()) && custom.getCalendars().length > 0,
                                  Array.isArray(custom.getCollations()),
                                  Array.isArray(custom.getHourCycles()) && custom.getHourCycles().includes("h12"),
                                  Array.isArray(custom.getNumberingSystems()) && custom.getNumberingSystems().length > 0,
                                  Array.isArray(timeZones) && timeZones.length > 0,
                                  Reflect.ownKeys(textInfo).join(","),
                                  textInfo.direction === "ltr" || textInfo.direction === "rtl",
                                  Reflect.ownKeys(weekInfo).join(","),
                                  weekInfo.firstDay,
                                  weekInfo.weekend.join(",")
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "true|true|en-US-u-fw-mon-hc-h12|true|true|true|true|true|direction|true|firstDay,weekend|1|6,7"));
    }

    [Test]
    public void Intl_GetCanonicalLocales_Uses_Locale_Internal_Slot()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                class PatchedLocale extends Intl.Locale {
                                  toString() {
                                    return "wrong";
                                  }
                                }
                                const loc = new PatchedLocale("fa");
                                Intl.getCanonicalLocales([new Intl.Locale("ar"), "zh", loc]).join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("ar|zh|fa"));
    }

    [Test]
    public void Intl_Locale_Maximize_And_Minimize_Use_LikelySubtags()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                [
                                  new Intl.Locale("en").maximize().toString(),
                                  new Intl.Locale("en-Latn-US").minimize().toString(),
                                  new Intl.Locale("art-lojban").maximize().toString()
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("en-Latn-US|en|jbo-Latn-001"));
    }

    [Test]
    public void Intl_Locale_Duplicate_Unicode_Keywords_Keep_First_Occurrence()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                new Intl.Locale("da-u-ca-gregory-ca-buddhist").toString();
                                """);

        Assert.That(result.AsString(), Is.EqualTo("da-u-ca-gregory"));
    }
}
