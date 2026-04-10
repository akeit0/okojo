using System.Text;
using Okojo.Runtime;

namespace Okojo.Tests;

public class IntlFeatureTests
{
    [Test]
    public void Intl_GlobalObject_And_ToStringTag_Are_Installed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                const globalDesc = Object.getOwnPropertyDescriptor(globalThis, "Intl");
                                const tagDesc = Object.getOwnPropertyDescriptor(Intl, Symbol.toStringTag);

                                typeof Intl === "object" &&
                                Object.getPrototypeOf(Intl) === Object.prototype &&
                                Object.isExtensible(Intl) === true &&
                                globalDesc.writable === true &&
                                globalDesc.enumerable === false &&
                                globalDesc.configurable === true &&
                                tagDesc.value === "Intl" &&
                                tagDesc.writable === false &&
                                tagDesc.enumerable === false &&
                                tagDesc.configurable === true &&
                                Object.prototype.toString.call(Intl) === "[object Intl]";
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Intl_GetCanonicalLocales_Canonicalizes_And_Validates()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                function sameArray(a, b) {
                                  if (a.length !== b.length) return false;
                                  for (let i = 0; i < a.length; i++) {
                                    if (a[i] !== b[i]) return false;
                                  }
                                  return true;
                                }

                                let ok = true;
                                const desc = Object.getOwnPropertyDescriptor(Intl, "getCanonicalLocales");
                                ok = ok && typeof Intl.getCanonicalLocales === "function";
                                ok = ok && Intl.getCanonicalLocales.length === 1;
                                ok = ok && Intl.getCanonicalLocales.name === "getCanonicalLocales";
                                ok = ok && desc.writable === true;
                                ok = ok && desc.enumerable === false;
                                ok = ok && desc.configurable === true;
                                ok = ok && sameArray(Intl.getCanonicalLocales(), []);
                                ok = ok && sameArray(Intl.getCanonicalLocales("ab-cd"), ["ab-CD"]);
                                ok = ok && sameArray(Intl.getCanonicalLocales(["ab-cd", "FF", "ab-cd"]), ["ab-CD", "ff"]);
                                ok = ok && sameArray(Intl.getCanonicalLocales({ a: 0 }), []);

                                let nullThrows = false;
                                try {
                                  Intl.getCanonicalLocales(null);
                                } catch (e) {
                                  nullThrows = e && e.name === "TypeError";
                                }
                                ok = ok && nullThrows;

                                let invalidThrows = false;
                                try {
                                  Intl.getCanonicalLocales("x-private");
                                } catch (e) {
                                  invalidThrows = e && e.name === "RangeError";
                                }
                                ok = ok && invalidThrows;

                                let typeThrows = false;
                                try {
                                  Intl.getCanonicalLocales([1]);
                                } catch (e) {
                                  typeThrows = e && e.name === "TypeError";
                                }
                                ok = ok && typeThrows;

                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Large_ObjectLiteral_ConstantPool_Uses_Wide_Index()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new StringBuilder();
        script.AppendLine("const values = [");
        for (var i = 0; i < 320; i++)
            script.Append("  \"s").Append(i).AppendLine("\",");
        script.AppendLine("];");
        script.AppendLine("const obj = { alpha: 1, beta: 2, gamma: 3 };");
        script.AppendLine("values.length === 320 && obj.alpha === 1 && obj.beta === 2 && obj.gamma === 3;");

        var result = realm.Eval(script.ToString());
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Intl_SupportedValuesOf_Unit_Remains_Stable_And_Sorted()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                Intl.supportedValuesOf("unit").join("|");
                                """);

        Assert.That(result.AsString(), Is.EqualTo(
            "acre|bit|byte|celsius|centimeter|day|degree|fahrenheit|fluid-ounce|foot|gallon|gigabit|gigabyte|gram|hectare|hour|inch|kilobit|kilobyte|kilogram|kilometer|liter|megabit|megabyte|meter|microsecond|mile|mile-scandinavian|milliliter|millimeter|millisecond|minute|month|nanosecond|ounce|percent|petabyte|pound|second|stone|terabit|terabyte|week|yard|year"));
    }
}
