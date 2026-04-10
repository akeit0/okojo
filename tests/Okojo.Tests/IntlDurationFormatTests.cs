using Okojo.Runtime;

namespace Okojo.Tests;

public class IntlDurationFormatTests
{
    [Test]
    public void Intl_DurationFormat_Constructor_Surface_And_ResolvedOptions_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const desc = Object.getOwnPropertyDescriptor(Intl, "DurationFormat");
                                const supportedDesc = Object.getOwnPropertyDescriptor(Intl.DurationFormat, "supportedLocalesOf");
                                const tagDesc = Object.getOwnPropertyDescriptor(Intl.DurationFormat.prototype, Symbol.toStringTag);
                                const df = new Intl.DurationFormat("en-u-nu-arab", { style: "digital", numberingSystem: "arab", hours: "numeric" });
                                const options = df.resolvedOptions();
                                [
                                  typeof Intl.DurationFormat === "function",
                                  desc.writable,
                                  desc.enumerable,
                                  desc.configurable,
                                  Intl.DurationFormat.length,
                                  typeof Intl.DurationFormat.supportedLocalesOf === "function",
                                  supportedDesc.configurable,
                                  Object.prototype.toString.call(df),
                                  options.locale,
                                  options.numberingSystem,
                                  options.style,
                                  options.hours,
                                  options.minutes,
                                  options.seconds,
                                  tagDesc.value,
                                  Intl.DurationFormat.supportedLocalesOf(["en", "zxx"]).join("|")
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "true|true|false|true|0|true|true|[object Intl.DurationFormat]|en-u-nu-arab|arab|digital|numeric|2-digit|2-digit|Intl.DurationFormat|en"));
    }

    [Test]
    public void Intl_DurationFormat_Formats_Mixed_And_Digital_Durations()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const mixed = new Intl.DurationFormat("en", { minutes: "numeric", seconds: "numeric" });
                                const digital = new Intl.DurationFormat("en", { style: "digital", fractionalDigits: 3 });
                                [
                                  mixed.format({ days: 5, hours: 1, minutes: 2, seconds: 3 }),
                                  digital.format({ hours: 7, minutes: 8, seconds: 9, milliseconds: 123, microseconds: 456, nanoseconds: 789 })
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("5 days, 1 hr, 2:03|7:08:09.123"));
    }

    [Test]
    public void Intl_DurationFormat_FormatToParts_And_String_Parsing_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                function dump(parts) {
                                  return parts.map(p => [p.type, p.value, p.unit === undefined ? "" : p.unit].join(":")).join("/");
                                }
                                const df = new Intl.DurationFormat("en", { style: "digital", fractionalDigits: 3 });
                                [
                                  dump(df.formatToParts({ hours: 7, minutes: 8, seconds: 9, milliseconds: 123 })),
                                  new Intl.DurationFormat("en").format("P1Y2M3W4DT5H6M7.00800901S")
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Does.StartWith(
            "integer:7:hour/literal:::/" +
            "integer:08:minute/literal:::/" +
            "integer:09:second/decimal:.:second/fraction:123:second|1 yr"));
    }

    [Test]
    public void Intl_DurationFormat_Validation_And_Option_Order_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const order = [];
                                new Intl.DurationFormat(undefined, {
                                  get localeMatcher() { order.push("localeMatcher"); return undefined; },
                                  get numberingSystem() { order.push("numberingSystem"); return undefined; },
                                  get style() { order.push("style"); return undefined; }
                                });

                                let typeError = false;
                                let rangeError = false;
                                try { new Intl.DurationFormat().formatToParts({}); } catch (e) { typeError = e && e.name === "TypeError"; }
                                try { new Intl.DurationFormat().formatToParts({ seconds: 2.5 }); } catch (e) { rangeError = e && e.name === "RangeError"; }

                                [order.join("|"), typeError, rangeError].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("localeMatcher|numberingSystem|style|true|true"));
    }

    [Test]
    public void Intl_DurationFormat_Explicit_Unit_Styles_Default_Display_To_Always()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const duration = {
                                  years: 1,
                                  months: 2,
                                  weeks: 3,
                                  days: 0,
                                  hours: 4,
                                  minutes: 5,
                                  seconds: 6,
                                  milliseconds: 7,
                                  microseconds: 8,
                                  nanoseconds: 9
                                };
                                const opts = {
                                  years: "narrow",
                                  months: "narrow",
                                  weeks: "narrow",
                                  days: "short",
                                  hours: "short",
                                  minutes: "short",
                                  seconds: "long",
                                  milliseconds: "long",
                                  microseconds: "long",
                                  nanoseconds: "narrow"
                                };
                                const df = new Intl.DurationFormat("es", {
                                  years: "narrow",
                                  months: "narrow",
                                  weeks: "narrow",
                                  days: "short",
                                  hours: "short",
                                  minutes: "short",
                                  seconds: "long",
                                  milliseconds: "long",
                                  microseconds: "long",
                                  nanoseconds: "narrow"
                                });
                                const options = df.resolvedOptions();
                                const parts = [];
                                for (const unit in duration) {
                                  parts.push(new Intl.NumberFormat("es", {
                                    style: "unit",
                                    unit: unit.slice(0, -1),
                                    unitDisplay: opts[unit]
                                  }).format(duration[unit]));
                                }
                                const expected = new Intl.ListFormat("es", { type: "unit", style: "short" }).format(parts);
                                [
                                  options.daysDisplay,
                                  options.nanosecondsDisplay,
                                  df.format(duration),
                                  expected
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "always|always|1a, 2m, 3sem, 0 d, 4 h, 5 min, 6 segundos, 7 milisegundos, 8 microsegundos, 9ns|1a, 2m, 3sem, 0 d, 4 h, 5 min, 6 segundos, 7 milisegundos, 8 microsegundos, 9ns"));
    }

    [Test]
    public void Intl_DurationFormat_Supports_Enumerated_NumberingSystems()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const numberingSystems = Intl.supportedValuesOf("numberingSystem");
                                let allValid = true;
                                for (const numberingSystem of numberingSystems) {
                                  const resolved = new Intl.DurationFormat("en", { numberingSystem }).resolvedOptions().numberingSystem;
                                  if (resolved !== numberingSystem) {
                                    allValid = false;
                                    break;
                                  }
                                }
                                [numberingSystems.length > 0, allValid].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("true|true"));
    }

    [Test]
    public void Intl_DurationFormat_Digital_Style_Does_Not_Group_Time_Units()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const df = new Intl.DurationFormat("en", { style: "digital" });
                                [
                                  df.format({ hours: 1234567, minutes: 20, seconds: 45 }),
                                  df.format({ hours: 12, minutes: 1234567, seconds: 20 }),
                                  df.format({ hours: 12, minutes: 34, seconds: 1234567 }),
                                  df.format({ hours: 12, minutes: 34, seconds: 56, milliseconds: 1234567 }),
                                  df.format({ days: 1234567, hours: 3, minutes: 20, seconds: 45 }),
                                  df.format({ days: 1234567, hours: 2345678, minutes: 3456789, seconds: 4567890 })
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "1234567:20:45|12:1234567:20|12:34:1234567|12:34:1290.567|1,234,567 days, 3:20:45|1,234,567 days, 2345678:3456789:4567890"));
    }
}
