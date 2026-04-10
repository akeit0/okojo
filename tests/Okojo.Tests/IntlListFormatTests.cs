using Okojo.Runtime;

namespace Okojo.Tests;

public class IntlListFormatTests
{
    [Test]
    public void Intl_ListFormat_Basic_Surface_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const lf = new Intl.ListFormat("en-US", { style: "short", type: "conjunction" });
                                [
                                  typeof Intl.ListFormat,
                                  lf.format(["foo", "bar", "baz"]),
                                  JSON.stringify(lf.formatToParts(["foo", "bar"])),
                                  Intl.ListFormat.supportedLocalesOf(["en-US", "zxx"]).join(","),
                                  JSON.stringify(lf.resolvedOptions())
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "function|foo, bar, & baz|[{\"type\":\"element\",\"value\":\"foo\"},{\"type\":\"literal\",\"value\":\" & \"},{\"type\":\"element\",\"value\":\"bar\"}]|en-US|{\"locale\":\"en-US\",\"type\":\"conjunction\",\"style\":\"short\"}"));
    }

    [Test]
    public void Intl_ListFormat_StringIterable_And_Spanish_Unit_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const lf = new Intl.ListFormat("es-ES", { style: "long", type: "unit" });
                                [
                                  lf.format("foo"),
                                  lf.format(["foo", "bar", "baz"]),
                                  JSON.stringify(lf.formatToParts(["foo", "bar", "baz"]))
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "f, o y o|foo, bar y baz|[{\"type\":\"element\",\"value\":\"foo\"},{\"type\":\"literal\",\"value\":\", \"},{\"type\":\"element\",\"value\":\"bar\"},{\"type\":\"literal\",\"value\":\" y \"},{\"type\":\"element\",\"value\":\"baz\"}]"));
    }

    [Test]
    public void Intl_ListFormat_Rejects_Primitive_Options()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const values = [null, true, false, "test", 7, Symbol(), 123456789n];
                                values.every(v => {
                                  try { new Intl.ListFormat([], v); return false; } catch (e) { return e && e.name === "TypeError"; }
                                });
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Intl_ListFormat_SupportedLocalesOf_Uses_ToObject_For_Primitive_Options()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let called = 0;
                                Object.defineProperty(Object.prototype, "localeMatcher", {
                                  configurable: true,
                                  get() { called++; return "best fit"; }
                                });
                                try {
                                  Intl.ListFormat.supportedLocalesOf([], true);
                                  Intl.ListFormat.supportedLocalesOf([], "test");
                                  Intl.ListFormat.supportedLocalesOf([], 7);
                                  called;
                                } finally {
                                  delete Object.prototype.localeMatcher;
                                }
                                """);

        Assert.That(result.Int32Value, Is.EqualTo(3));
    }
}
