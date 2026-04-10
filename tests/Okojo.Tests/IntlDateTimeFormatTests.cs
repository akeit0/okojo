using Okojo.Runtime;

namespace Okojo.Tests;

public class IntlDateTimeFormatTests
{
    [Test]
    public void Intl_DateTimeFormat_ResolvedOptions_And_Surface_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const dtf = new Intl.DateTimeFormat("en-US-u-nu-arab", {
                                  timeZone: "utc",
                                  year: "numeric",
                                  month: "2-digit",
                                  day: "2-digit",
                                  hour: "numeric",
                                  minute: "numeric",
                                  timeZoneName: "short"
                                });
                                [
                                  typeof dtf.formatToParts,
                                  typeof dtf.formatRange,
                                  typeof dtf.formatRangeToParts,
                                  JSON.stringify(dtf.resolvedOptions())
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "function|function|function|{\"locale\":\"en-US-u-nu-arab\",\"calendar\":\"gregory\",\"numberingSystem\":\"arab\",\"timeZone\":\"UTC\",\"hourCycle\":\"h12\",\"hour12\":true,\"year\":\"numeric\",\"month\":\"2-digit\",\"day\":\"2-digit\",\"hour\":\"numeric\",\"minute\":\"numeric\",\"timeZoneName\":\"short\"}"));
    }

    [Test]
    public void Intl_DateTimeFormat_Range_And_DayPeriod_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const day = new Intl.DateTimeFormat("en", { dayPeriod: "narrow", hour: "numeric", timeZone: "UTC" }).format(0);
                                const range = new Intl.DateTimeFormat("en-US", { year: "numeric", month: "numeric", day: "numeric", timeZone: "UTC" }).formatRange(0, 0);
                                [day, range].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("12 at night|1/1/1970"));
    }

    [Test]
    public void Intl_DateTimeFormat_Constructor_ToObject_And_Locale_Precendence_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const primitiveLocale = new Intl.DateTimeFormat("en-US", 42).resolvedOptions().locale;
                                const hcWithHour = new Intl.DateTimeFormat("en-US-u-hc-h11", { hour: "2-digit", hour12: false }).resolvedOptions().locale;
                                const hcWithoutHour = new Intl.DateTimeFormat("en-US-u-hc-h11", {}).resolvedOptions().locale;
                                const nuInvalidOption = new Intl.DateTimeFormat("en-u-nu-arab", { numberingSystem: "invalid" }).resolvedOptions().locale;
                                [primitiveLocale, hcWithHour, hcWithoutHour, nuInvalidOption].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("en-US|en-US|en-US-u-hc-h11|en-u-nu-arab"));
    }

    [Test]
    public void Intl_DateTimeFormat_FractionalSecond_And_Undefined_Range_Handling_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const parts = new Intl.DateTimeFormat("en", {
                                  minute: "numeric",
                                  second: "numeric",
                                  fractionalSecondDigits: undefined,
                                  timeZone: "UTC"
                                }).formatToParts(Date.UTC(2019, 7, 10, 1, 2, 3, 234)).map(p => `${p.type}:${p.value}`).join(",");

                                let rangeError = "no";
                                try {
                                  new Intl.DateTimeFormat("en").formatRange(undefined, 0);
                                } catch (e) {
                                  rangeError = e instanceof TypeError ? "type" : e.name;
                                }

                                [parts, rangeError].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("minute:02,literal::,second:03|type"));
    }

    [Test]
    public void Date_ToLocaleString_Preserves_LocaleMatcher_And_Invalid_Date_String()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let rangeError = false;
                                try { new Date().toLocaleString([], { localeMatcher: null }); } catch (e) { rangeError = e && e.name === "RangeError"; }
                                [
                                  rangeError,
                                  new Date(NaN).toLocaleString(),
                                  new Date(Infinity).toLocaleDateString(),
                                  new Date(-Infinity).toLocaleTimeString()
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("true|Invalid Date|Invalid Date|Invalid Date"));
    }

    [Test]
    public void Intl_DateTimeFormat_Constructor_Uses_ToObject_For_Primitive_Options()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const a = new Intl.DateTimeFormat(["en-US"], true).resolvedOptions();
                                const b = new Intl.DateTimeFormat(["en-US"], Object(true)).resolvedOptions();
                                a.locale === b.locale &&
                                a.calendar === b.calendar &&
                                a.year === b.year &&
                                a.month === b.month &&
                                a.day === b.day;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Intl_DateTimeFormat_TimeZone_Identifiers_Are_Case_Normalized()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                [
                                  new Intl.DateTimeFormat("en", { timeZone: "america/new_york" }).resolvedOptions().timeZone,
                                  new Intl.DateTimeFormat("en", { timeZone: "AMERICA/NEW_YORK" }).resolvedOptions().timeZone,
                                  new Intl.DateTimeFormat("en", { timeZone: "asia/kolkata" }).resolvedOptions().timeZone,
                                  new Intl.DateTimeFormat("en", { timeZone: "eTc/gMt+8" }).resolvedOptions().timeZone
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("America/New_York|America/New_York|Asia/Kolkata|Etc/GMT+8"));
    }

    [Test]
    public void Intl_SupportedValuesOf_Calendar_Is_Stable_And_Sorted()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                Intl.supportedValuesOf("calendar").join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "buddhist|chinese|coptic|dangi|ethioaa|ethiopic|gregory|hebrew|indian|islamic-civil|islamic-tbla|islamic-umalqura|iso8601|japanese|persian|roc"));
    }

    [Test]
    public void Intl_DateTimeFormat_Chinese_Year_Uses_RelatedYear_And_YearName()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                new Intl.DateTimeFormat("zh-u-ca-chinese", { year: "numeric" })
                                  .formatToParts(new Date(2019, 5, 1))
                                  .map(part => `${part.type}:${part.value}`)
                                  .join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("relatedYear:2019|yearName:己亥|literal:年"));
    }

    [Test]
    public void Intl_DateTimeFormat_Chinese_Calendar_Default_Parts_Expose_Lunisolar_Date()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                new Intl.DateTimeFormat("en-US-u-ca-chinese", { timeZone: "UTC" })
                                  .formatToParts(new Date("2000-01-01T00:00Z"))
                                  .filter(part => part.type !== "literal")
                                  .map(part => `${part.type}:${part.value}`)
                                  .join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("month:11|day:25|relatedYear:1999"));
    }

    [Test]
    public void Intl_DateTimeFormat_Dangi_Calendar_Range_Parts_Expose_Lunisolar_Date()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                new Intl.DateTimeFormat("en-US-u-ca-dangi", { timeZone: "UTC" })
                                  .formatRangeToParts(new Date("1900-01-01T00:00Z"), new Date("2050-01-01T00:00Z"))
                                  .filter(part => part.type !== "literal")
                                  .map(part => `${part.source}:${part.type}:${part.value}`)
                                  .join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "startRange:month:12|startRange:day:1|startRange:relatedYear:1899|endRange:month:12|endRange:day:8|endRange:relatedYear:2049"));
    }

    [Test]
    public void Intl_DateTimeFormat_Applies_TimeClip_At_Ecma_Boundaries()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const start = -8640000000000000;
                                const end = 8640000000000000;
                                const dtf = new Intl.DateTimeFormat();

                                let below = "ok";
                                let above = "ok";
                                try { dtf.format(start - 1); } catch (e) { below = e.name; }
                                try { dtf.format(end + 1); } catch (e) { above = e.name; }

                                [
                                  below,
                                  typeof dtf.format(start),
                                  typeof dtf.formatToParts(end),
                                  typeof dtf.formatRange(start, 0),
                                  typeof dtf.formatRangeToParts(0, end),
                                  above
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("RangeError|string|object|string|object|RangeError"));
    }

    [Test]
    public void Intl_DateTimeFormat_Uses_Proleptic_Gregorian_Years_Without_Year_Zero()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const format = new Intl.DateTimeFormat("en-US", { year: "numeric", era: "short", timeZone: "UTC" });
                                [
                                  format.format(-62151602400000).includes("1 BC"),
                                  format.format(-8640000000000000).includes("271822 BC")
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("true|true"));
    }

    [Test]
    public void Intl_DateTimeFormat_Era_Parts_Distinguish_Multi_Era_Calendars()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                function newDate(year, month, day) {
                                  const date = new Date(0);
                                  date.setUTCFullYear(year, month, day);
                                  date.setUTCHours(0, 0, 0, 0);
                                  return date;
                                }

                                function era(calendar, year) {
                                  const dtf = new Intl.DateTimeFormat("en", { calendar, era: "long", year: "numeric", timeZone: "UTC" });
                                  const parts = dtf.formatToParts(newDate(year, 5, 15));
                                  const eraPart = parts.find(part => part.type === "era");
                                  return eraPart ? eraPart.value : "<none>";
                                }

                                [
                                  era("gregory", -100) !== era("gregory", 2025),
                                  era("japanese", 1850) !== era("japanese", 2025),
                                  era("roc", 1900) !== era("roc", 2025),
                                  era("ethiopic", 0) !== era("ethiopic", 2025),
                                  era("chinese", 2025)
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("true|true|true|true|<none>"));
    }

    [Test]
    public void Intl_DateTimeFormat_Japanese_Era_Table_Covers_Current_Historical_Buckets()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                function newDate(year) {
                                  const date = new Date(0);
                                  date.setUTCFullYear(year, 5, 15);
                                  date.setUTCHours(0, 0, 0, 0);
                                  return date;
                                }

                                const dtf = new Intl.DateTimeFormat("en", { calendar: "japanese", era: "long", year: "numeric", timeZone: "UTC" });
                                [2025, 1990, 1930, 1915, 1870, 1800, 1500]
                                  .map(year => dtf.formatToParts(newDate(year)).find(part => part.type === "era").value)
                                  .join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("Reiwa|Heisei|Showa|Taisho|Meiji|Edo|Ancient"));
    }
}
