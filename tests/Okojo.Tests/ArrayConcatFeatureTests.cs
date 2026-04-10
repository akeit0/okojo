using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ArrayConcatFeatureTests
{
    [Test]
    public void ArrayPrototype_Concat_Spreads_Arrays_And_Preserves_Holes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const a = [0];
            const b = [1, , 3];
            const c = a.concat(b, [4, 5]);

            c.length === 6 &&
            c[0] === 0 &&
            c[1] === 1 &&
            (1 in c) &&
            c[2] === undefined &&
            (2 in c) === false &&
            c[3] === 3 &&
            c[4] === 4 &&
            c[5] === 5;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Concat_Is_Generic_For_NonArray_Receivers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const result = Array.prototype.concat.call(true, 1, 2);
            result.length === 3 &&
            result[0] instanceof Boolean &&
            result[1] === 1 &&
            result[2] === 2;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Concat_Does_Not_Spread_TypedArrays_By_Default()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const ta = new Uint8Array([1, 2]);
            const result = [].concat(ta, ta);

            result.length === 2 &&
            result[0] === ta &&
            result[1] === ta;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Concat_Uses_SymbolIsConcatSpreadable_When_Present()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const obj = { length: 3, 0: "a", 2: "c" };
            obj[Symbol.isConcatSpreadable] = true;
            const result = [].concat(obj);

            const ta = new Uint8Array([1, 2]);
            ta[Symbol.isConcatSpreadable] = true;
            const spreadTypedArray = [].concat(ta);

            result.length === 3 &&
            result[0] === "a" &&
            result[1] === undefined &&
            (1 in result) === false &&
            result[2] === "c" &&
            spreadTypedArray.length === 2 &&
            spreadTypedArray[0] === 1 &&
            spreadTypedArray[1] === 2;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Concat_Treats_Explicit_Undefined_Spreadable_As_Array_Fallback()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const item = [];
            item[Symbol.isConcatSpreadable] = undefined;
            const result = [].concat(item);

            result.length === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Concat_Treats_Array_Proxies_As_Spreadable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const arrayProxy = new Proxy([], {});
            const arrayProxyProxy = new Proxy(arrayProxy, {});

            [].concat(arrayProxy).length === 0 &&
            [].concat(arrayProxyProxy).length === 0;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Concat_Spreads_Sloppy_Arguments_With_Duplicate_Parameters()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            var args = (function(a, a, a) {
              return arguments;
            })(1, 2, 3);
            args[Symbol.isConcatSpreadable] = true;

            var first = [].concat(args, args).join(",");
            Object.defineProperty(args, "length", { value: 6 });
            var second = [].concat(args);

            first === "1,2,3,1,2,3" &&
            second.length === 6 &&
            second[0] === 1 &&
            second[1] === 2 &&
            second[2] === 3 &&
            second[3] === undefined &&
            second[4] === undefined &&
            second[5] === undefined;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Concat_Throws_When_Spreadable_Length_Exceeds_MaxSafeInteger_Result()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let threwA = false;
            let threwB = false;

            const spreadable = {
              length: Number.MAX_SAFE_INTEGER,
              [Symbol.isConcatSpreadable]: true
            };

            try {
              [1].concat(spreadable);
            } catch (e) {
              threwA = e instanceof TypeError;
            }

            const proxy = new Proxy([], {
              get(_target, key) {
                if (key === "length") return Number.MAX_SAFE_INTEGER;
              }
            });

            try {
              [].concat(1, proxy);
            } catch (e) {
              threwB = e instanceof TypeError;
            }

            threwA && threwB;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrayPrototype_Concat_Defines_Own_Result_Elements_Despite_Readonly_ArrayPrototype_Index()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Object.defineProperty(Array.prototype, "0", {
              value: 100,
              writable: false,
              configurable: true
            });

            var fromArray = Array.prototype.concat.call([101]);
            var fromPrimitive = Array.prototype.concat.call(101);

            delete Array.prototype[0];

            [
              Object.prototype.hasOwnProperty.call(fromArray, "0"),
              fromArray[0],
              Object.getOwnPropertyDescriptor(fromArray, "0") !== undefined,
              Object.prototype.hasOwnProperty.call(fromPrimitive, "0"),
              fromPrimitive[0] instanceof Number,
              Object.getOwnPropertyDescriptor(fromPrimitive, "0") !== undefined
            ].join("|");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|101|true|true|true|true"));
    }

    [Test]
    public void ObjectKeys_Result_Array_Ignores_Readonly_ArrayPrototype_Index_During_Concat_Descriptor_Checks()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            Object.defineProperty(Array.prototype, "0", {
              value: 100,
              writable: false,
              configurable: true
            });

            const desc = Object.getOwnPropertyDescriptor([101].concat(), "0");
            const keys = Object.keys(desc).join(",");

            delete Array.prototype[0];
            keys;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("value,writable,enumerable,configurable"));
    }
}
