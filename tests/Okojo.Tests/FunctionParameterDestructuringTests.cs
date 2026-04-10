using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class FunctionParameterDestructuringTests
{
    [Test]
    public void Function_ObjectPattern_Property_Default_Binds_Local()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            (function({ y = 33 }) {
              return y;
            })({});
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(33));
    }

    [Test]
    public void Function_ArrayPattern_Element_Binds_Local()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            (function([x]) {
              return x;
            })([42]);
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Function_ObjectPattern_Default_Can_Capture_Bound_Name_At_Compile_Time()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            (function() {
              return function({ alpha = 33, beta = function betaDefault() { return alpha; } } = {}) {
                return beta();
              };
            })();
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsObject, Is.True);
    }

    [Test]
    public void Function_ParameterPattern_Defaults_Assign_Context_Slots_For_Captured_Bindings_At_Compile_Time()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function makeDestructuringHeavy() {
              return function run(
                [
                  first,
                  second = first + 1
                ] = [1, 2],
                {
                  alpha: renamedAlpha = second,
                  beta = function betaDefault() { return renamedAlpha; }
                } = {}
              ) {
                return first + second + beta();
              };
            }

            makeDestructuringHeavy();
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsObject, Is.True);
    }

    [Test]
    public void Function_ObjectPattern_NestedObjectValue_Binds_Local()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            (function({ outer: { inner } }) {
              return inner;
            })({ outer: { inner: 7 } });
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void Function_ObjectPattern_Rest_Binds_Remaining_Enumerable_Own_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            (function({ keep, ...rest }) {
              return rest.a + ":" + rest.b + ":" + ("keep" in rest);
            })({ keep: 1, a: 2, b: 3 });
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("2:3:false"));
    }

    [Test]
    public void Function_ObjectPattern_Default_AnonymousFunction_Gets_Bound_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            (function({ cover = function () {} }) {
              return cover.name;
            })({});
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("cover"));
    }

    [Test]
    public void Function_ObjectPattern_WholeParameter_Default_Is_Applied_Before_Destructuring()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            (function({ x } = { x: 23 }) {
              return x;
            })();
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(23));
    }

    [Test]
    public void Function_ArrayPattern_Rest_Binds_Remaining_Values()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            (function([...x] = [1, 2, 3]) {
              return x.length + ":" + x[0] + ":" + x[2];
            })();
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("3:1:3"));
    }

    [Test]
    public void Function_ArrayPattern_Rest_Binds_Local_In_Strict_Mode()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            "use strict";
            (function([...x] = [1, 2, 3]) {
              return x[1];
            })();
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void Function_ArrayPattern_Default_AnonymousFunction_Gets_Bound_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            (function([fn = function () {}, xFn = function x() {}] = []) {
              return fn.name + ":" + xFn.name;
            })();
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("fn:x"));
    }

    [Test]
    public void Function_ArrayPattern_Nested_Object_Default_Binds_Local()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            (function([{ x, y, z } = { x: 44, y: 55, z: 66 }]) {
              return x + y + z;
            })([{ x: 11, y: 22, z: 33 }]);
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(66));
    }

    [Test]
    public void Function_ArrayPattern_Rest_ObjectPattern_Binds_Array_Indices_And_Length()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let length = "outer";
            (function([...{ 0: v, 1: w, 2: x, 3: y, length: z }]) {
              return v + ":" + w + ":" + x + ":" + String(y) + ":" + z + ":" + length;
            })([7, 8, 9]);
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("7:8:9:undefined:3:outer"));
    }

    [Test]
    public void Function_ArrayPattern_Default_AnonymousClass_Gets_Bound_Name()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            (function([cls = class {}, xCls = class X {}, xCls2 = class { static name() {} }] = []) {
              return cls.name + ":" + xCls.name + ":" + (xCls2.name === "xCls2");
            })();
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("cls:X:false"));
    }

    [Test]
    public void Function_ArrayPattern_Rest_Exact_Strict_Shaped_FunctionExpression_Binds_Local()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            "use strict";
            var callCount = 0;
            var compareArray = function(actual, expected) {
              return Array.isArray(actual)
                && actual.length === expected.length
                && actual[0] === expected[0];
            };
            var f;
            f = function([...x] = [1]) {
              if (!Array.isArray(x)) throw new Error("not array");
              if (!compareArray(x, [1])) throw new Error("wrong contents");
              callCount = callCount + 1;
            };
            f();
            callCount;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void Function_ArrayPattern_Rest_With_Host_Method_Call_Keeps_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var assertObject = new JsPlainObject(realm);
        assertObject.SetProperty("compareArray", JsValue.FromObject(new JsHostFunction(realm, static (in info) =>
        {
            var args = info.Arguments;
            var actualObject = args[0].AsObject();
            Assert.That(actualObject, Is.TypeOf<JsArray>());
            var actualArray = (JsArray)actualObject;
            Assert.That(actualArray.Length, Is.EqualTo(1u));
            Assert.That(actualArray.TryGetElement(0, out var actualValue), Is.True);
            Assert.That(actualValue.Int32Value, Is.EqualTo(1));
            return JsValue.Undefined;
        }, "compareArray", 2)));
        realm.Global["assert"] = JsValue.FromObject(assertObject);

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            "use strict";
            var callCount = 0;
            var f;
            f = function([...x] = [1]) {
              assert.compareArray(x, [1]);
              callCount = callCount + 1;
            };
            f();
            callCount;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }
}
