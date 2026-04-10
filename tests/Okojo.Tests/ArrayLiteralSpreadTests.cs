using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ArrayLiteralSpreadTests
{
    [Test]
    public void ArrayLiteral_Spread_Appends_Iterable_Values_In_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var values = ["a", "b"];
            var out = [0, ...values, 3];
            out.length === 4 &&
            out[0] === 0 &&
            out[1] === "a" &&
            out[2] === "b" &&
            out[3] === 3;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayLiteral_Spread_Throws_For_NonIterable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var ok = false;
            try {
              [...123];
            } catch (e) {
              ok = e && e.name === "TypeError";
            }
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayLiteral_And_ObjectSpread_Proxy_Repro_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var VALUE_LITERAL = "VALUE_LITERAL";
            var VALUE_GET = "VALUE_GET";
            var dontEnumSymbol = Symbol("dont_enum_symbol");
            var enumerableSymbol = Symbol("enumerable_symbol");
            var dontEnumKeys = [dontEnumSymbol, "dontEnumString", "0"];
            var enumerableKeys = [enumerableSymbol, "enumerableString", "1"];
            var ownKeysResult = [...dontEnumKeys, ...enumerableKeys];
            var getOwnKeys = [];
            var getKeys = [];
            var proxy = new Proxy({}, {
              getOwnPropertyDescriptor: function(_target, key) {
                getOwnKeys.push(key);
                var isEnumerable = enumerableKeys.indexOf(key) !== -1;
                return { value: "ignored", writable: false, enumerable: isEnumerable, configurable: true };
              },
              get: function(_target, key) {
                getKeys.push(key);
                return VALUE_GET;
              },
              ownKeys: function() {
                return ownKeysResult;
              }
            });
            var result = { [enumerableSymbol]: VALUE_LITERAL, enumerableString: VALUE_LITERAL, [1]: VALUE_LITERAL, ...proxy };
            getOwnKeys.length === 6 &&
            getKeys.length === 3 &&
            result[enumerableSymbol] === VALUE_GET &&
            result.enumerableString === VALUE_GET &&
            result[1] === VALUE_GET;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayLiteral_Defines_Own_Elements_Despite_Readonly_ArrayPrototype_Index()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Object.defineProperty(Array.prototype, "0", {
              value: 100,
              writable: false,
              configurable: true
            });

            const arr = [101];
            const ok =
              Object.prototype.hasOwnProperty.call(arr, "0") &&
              arr[0] === 101 &&
              Object.getOwnPropertyDescriptor(arr, "0") !== undefined;

            delete Array.prototype[0];
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayLiteral_Spread_Defines_Own_Elements_Despite_Readonly_ArrayPrototype_Index()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Object.defineProperty(Array.prototype, "0", {
              value: 100,
              writable: false,
              configurable: true
            });

            const arr = [...[101]];
            const ok =
              Object.prototype.hasOwnProperty.call(arr, "0") &&
              arr[0] === 101 &&
              Object.getOwnPropertyDescriptor(arr, "0") !== undefined;

            delete Array.prototype[0];
            ok;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
