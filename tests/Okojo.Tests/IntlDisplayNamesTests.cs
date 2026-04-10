using Okojo.Runtime;

namespace Okojo.Tests;

public class IntlDisplayNamesTests
{
    [Test]
    public void Intl_DisplayNames_Basic_Surface_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const dn = new Intl.DisplayNames("en-US", {
                                  type: "language",
                                  style: "short",
                                  fallback: "code",
                                  languageDisplay: "standard"
                                });
                                [
                                  typeof Intl.DisplayNames,
                                  typeof dn.of("en-US"),
                                  Intl.DisplayNames.supportedLocalesOf(["en-US", "zxx"]).join(","),
                                  JSON.stringify(dn.resolvedOptions()),
                                  Object.prototype.toString.call(dn)
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "function|string|en-US|{\"locale\":\"en-US\",\"style\":\"short\",\"type\":\"language\",\"fallback\":\"code\",\"languageDisplay\":\"standard\"}|[object Intl.DisplayNames]"));
    }

    [Test]
    public void Intl_DisplayNames_Canonicalizes_And_Validates_Codes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const language = new Intl.DisplayNames("en", { type: "language" });
                                const region = new Intl.DisplayNames("en", { type: "region" });
                                const calendar = new Intl.DisplayNames("en", { type: "calendar" });
                                const field = new Intl.DisplayNames("en", { type: "dateTimeField" });

                                let invalidLanguage = false;
                                try { language.of("root"); } catch (e) { invalidLanguage = e.name === "RangeError"; }

                                [
                                  language.of("en-us"),
                                  region.of("us"),
                                  typeof calendar.of("gregory"),
                                  typeof field.of("timeZoneName"),
                                  invalidLanguage
                                ].join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo("English (United States)|United States|string|string|true"));
    }

    [Test]
    public void Intl_DisplayNames_Requires_Object_Options_And_Valid_Receiver()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                let optionsThrows = false;
                                try { new Intl.DisplayNames([], undefined); } catch (e) { optionsThrows = e.name === "TypeError"; }

                                let receiverThrows = false;
                                try { Intl.DisplayNames.prototype.resolvedOptions.call({}); } catch (e) { receiverThrows = e.name === "TypeError"; }

                                optionsThrows && receiverThrows;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
