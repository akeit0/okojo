using Okojo.Runtime;

namespace Okojo.Tests;

public class IntlCollatorTests
{
    [Test]
    public void Intl_Collator_Basic_Surface_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const collator = Intl.Collator("de", {
                                  usage: "search",
                                  sensitivity: "base",
                                  numeric: true,
                                  caseFirst: "upper",
                                  ignorePunctuation: true
                                });
                                const compare = collator.compare;
                                [
                                  typeof Intl.Collator,
                                  typeof compare,
                                  compare("a2", "a10"),
                                  compare("A", "a"),
                                  Intl.Collator.supportedLocalesOf(["de", "zz"]).join(","),
                                  JSON.stringify(collator.resolvedOptions())
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "function|function|-1|-1|de|{\"locale\":\"de\",\"usage\":\"search\",\"sensitivity\":\"base\",\"ignorePunctuation\":true,\"collation\":\"default\",\"numeric\":true,\"caseFirst\":\"upper\"}"));
    }

    [Test]
    public void Intl_Collator_Compare_Getter_Is_Stable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const collator = new Intl.Collator("en");
                                collator.compare === collator.compare;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Intl_SupportedLocalesOf_Is_Not_Tainted_By_Array_Prototype_Setters()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const desc = Object.getOwnPropertyDescriptor(Array.prototype, "0");
                                Object.defineProperty(Array.prototype, "0", {
                                  configurable: true,
                                  set() { throw new Error("tainted"); }
                                });
                                try {
                                  const locale = new Intl.Collator().resolvedOptions().locale;
                                  const out = Intl.Collator.supportedLocalesOf([locale, locale]);
                                  out.length === 1 && out[0] === locale;
                                } finally {
                                  if (desc) Object.defineProperty(Array.prototype, "0", desc);
                                  else delete Array.prototype[0];
                                }
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
