using Okojo.Runtime;

namespace Okojo.Tests;

public class IntlRelativeTimeFormatTests
{
    [Test]
    public void Intl_RelativeTimeFormat_Constructor_And_ResolvedOptions_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const desc = Object.getOwnPropertyDescriptor(Intl, "RelativeTimeFormat");
                                const supportedDesc = Object.getOwnPropertyDescriptor(Intl.RelativeTimeFormat, "supportedLocalesOf");
                                const tagDesc = Object.getOwnPropertyDescriptor(Intl.RelativeTimeFormat.prototype, Symbol.toStringTag);
                                const rtf = new Intl.RelativeTimeFormat("en-u-nu-arab", { style: "short", numeric: "auto", numberingSystem: "arab" });
                                const options = rtf.resolvedOptions();
                                [
                                  typeof Intl.RelativeTimeFormat === "function",
                                  desc.writable,
                                  desc.enumerable,
                                  desc.configurable,
                                  Intl.RelativeTimeFormat.length,
                                  typeof Intl.RelativeTimeFormat.supportedLocalesOf === "function",
                                  supportedDesc.configurable,
                                  Object.prototype.toString.call(rtf),
                                  options.locale,
                                  options.style,
                                  options.numeric,
                                  options.numberingSystem,
                                  tagDesc.value,
                                  Intl.RelativeTimeFormat.supportedLocalesOf(["en", "zxx"]).join("|")
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "true|true|false|true|0|true|true|[object Intl.RelativeTimeFormat]|en-u-nu-arab|short|auto|arab|Intl.RelativeTimeFormat|en"));
    }

    [Test]
    public void Intl_RelativeTimeFormat_Formats_English_And_Polish()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const en = new Intl.RelativeTimeFormat("en-US", { numeric: "auto" });
                                const pl = new Intl.RelativeTimeFormat("pl-PL", { style: "short" });
                                [
                                  en.format(1000, "second"),
                                  en.format(0, "day"),
                                  en.format(-0, "second"),
                                  pl.format(2, "year"),
                                  pl.format(-1, "year")
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("in 1,000 seconds|today|now|za 2 lata|1 rok temu"));
    }

    [Test]
    public void Intl_RelativeTimeFormat_FormatToParts_Uses_Locale_Number_Parts()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                function dump(parts) {
                                  return parts.map(p => [p.type, p.value, p.unit === undefined ? "" : p.unit].join(":")).join("/");
                                }
                                const en = new Intl.RelativeTimeFormat("en-US", { style: "short" });
                                const pl = new Intl.RelativeTimeFormat("pl-PL", { style: "long" });
                                [
                                  dump(en.formatToParts(123456.78, "second")),
                                  dump(pl.formatToParts(123456.78, "year"))
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "literal:in :/integer:123:second/group:,:second/integer:456:second/decimal:.:second/fraction:78:second/literal: sec.:|literal:za :/integer:123:year/group:\u00A0:year/integer:456:year/decimal:,:year/fraction:78:year/literal: roku:"));
    }

    [Test]
    public void Intl_RelativeTimeFormat_Constructor_Option_Order_And_NumberingSystem_Validation_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const marker = new Error("marker");
                                let abrupt = false;
                                try {
                                  new Intl.RelativeTimeFormat("en", {
                                    get localeMatcher() { return "lookup"; },
                                    get numberingSystem() { throw marker; },
                                    get style() { throw new Error("wrong-order-style"); },
                                    get numeric() { throw new Error("wrong-order-numeric"); }
                                  });
                                } catch (e) {
                                  abrupt = e === marker;
                                }

                                let invalidThrows = false;
                                try {
                                  new Intl.RelativeTimeFormat("en", { numberingSystem: "latn-ca" });
                                } catch (e) {
                                  invalidThrows = e && e.name === "RangeError";
                                }

                                abrupt && invalidThrows;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Intl_RelativeTimeFormat_Constructor_Uses_ToObject_For_Primitive_Options()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                Object.defineProperties(Object.prototype, {
                                  style: { value: "short", configurable: true },
                                  numeric: { value: "auto", configurable: true }
                                });
                                try {
                                  const a = new Intl.RelativeTimeFormat([], true).resolvedOptions();
                                  const b = new Intl.RelativeTimeFormat([], "x").resolvedOptions();
                                  const c = new Intl.RelativeTimeFormat([], 7).resolvedOptions();
                                  const d = new Intl.RelativeTimeFormat([], Symbol()).resolvedOptions();
                                  [a.style, a.numeric, b.style, c.numeric, d.style].join("|");
                                } finally {
                                  delete Object.prototype.style;
                                  delete Object.prototype.numeric;
                                }
                                """);

        Assert.That(result.AsString(), Is.EqualTo("short|auto|short|auto|short"));
    }

    [Test]
    public void Intl_RelativeTimeFormat_Uses_Locale_NumberingSystem_Output()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const locales = ["en-US", "en-US-u-nu-arab", "en-US-u-nu-deva", "en-US-u-nu-hanidec"];
                                const seconds = 1234567890;
                                locales.map(locale => {
                                  const formatted = new Intl.RelativeTimeFormat(locale, { style: "short" }).format(seconds, "seconds");
                                  const expected = new Intl.NumberFormat(locale).format(seconds);
                                  return String(formatted.includes(expected));
                                }).join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("true|true|true|true"));
    }
}
