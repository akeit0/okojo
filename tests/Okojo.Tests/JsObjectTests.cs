using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;
// Dont add tests here any more, add them to the appropriate test class (e.g. OkojoPrototypeTests, OkojoContextTests, etc.) instead. This class should only

public class JsObjectTests
{
    [Test]
    public void TestIntrinsicPrototypeObjectKinds()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function t() {
                let np = Object.getPrototypeOf(Object(1));
                let bp = Object.getPrototypeOf(Object(true));
                let sp = Object.getPrototypeOf(Object("x"));
                let fp = Object.getPrototypeOf(function(){});
                if (np === bp) return false;
                if (np === sp) return false;
                if (np === fp) return false;
                if (np !== Object.getPrototypeOf(Object(2))) return false;
                if (bp !== Object.getPrototypeOf(Object(false))) return false;
                if (sp !== Object.getPrototypeOf(Object("y"))) return false;
                return true;
            }
            t();
            """));
        realm.Execute(script);
        Assert.That(realm.Accumulator.IsBool, Is.True);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TestPlainObjectSetAndGetNamedProperty()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var o = new JsPlainObject(realm);
        o.SetProperty("x", JsValue.FromInt32(7));

        var found = o.TryGetProperty("x", out var value);

        Assert.That(found, Is.True);
        Assert.That(value.IsInt32, Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void TestPlainObjectPrototypeLookup()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var proto = new JsPlainObject(realm);
        proto.SetProperty("x", JsValue.FromInt32(11));

        var o = new JsPlainObject(realm) { Prototype = proto };

        var found = o.TryGetProperty("x", out var value);

        Assert.That(found, Is.True);
        Assert.That(value.IsInt32, Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(11));
    }

    [Test]
    public void TestPlainObjectOwnPropertyShadowsPrototype()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var proto = new JsPlainObject(realm);
        proto.SetProperty("x", JsValue.FromInt32(11));

        var o = new JsPlainObject(realm) { Prototype = proto };
        o.SetProperty("x", JsValue.FromInt32(12));

        var found = o.TryGetProperty("x", out var value);

        Assert.That(found, Is.True);
        Assert.That(value.IsInt32, Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(12));
    }

    [Test]
    public void TestObjectLiteralCreatesPlainObjectInVm()
    {
        var program = JavaScriptParser.ParseScript("function t(){ return {}; } t();");
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsObject, Is.True);
        Assert.That(realm.Accumulator.AsObject(), Is.TypeOf<JsPlainObject>());
    }

    [Test]
    public void TestNamedPropertyIcPopulatesAfterExecution()
    {
        var program = JavaScriptParser.ParseScript("function t(){ let o={x:1}; return o.x; } t();");
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(program);
        var func = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(static f => f.Name == "t");
        var funcScript = func.Script;

        Assert.That(funcScript.NamedPropertyIcEntries, Is.Not.Null);
        Assert.That(funcScript.NamedPropertyIcEntries!.Length, Is.GreaterThan(0));
        Assert.That(funcScript.NamedPropertyIcEntries!.Any(static e => e.Shape is not null), Is.False);

        realm.Execute(script);

        Assert.That(funcScript.NamedPropertyIcEntries.Any(static e => e.Shape is not null), Is.True);
    }

    [Test]
    public void TestDefaultNamedPropertyFlagsAreOpen()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var o = new JsPlainObject(realm);
        o.SetProperty("x", JsValue.FromInt32(1));

        var found = o.TryGetOwnPropertyFlags("x", out var flags);

        Assert.That(found, Is.True);
        Assert.That(flags, Is.EqualTo(JsShapePropertyFlags.Open));
    }

    [Test]
    public void TestNonWritablePropertyRejectsAssignment()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var o = new JsPlainObject(realm);
        o.DefineDataProperty("x", JsValue.FromInt32(1),
            JsShapePropertyFlags.Enumerable | JsShapePropertyFlags.Configurable);

        o.SetProperty("x", JsValue.FromInt32(2));
        var found = o.TryGetProperty("x", out var value);

        Assert.That(found, Is.True);
        Assert.That(value.IsInt32, Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(1));
        Assert.That(o.TryGetOwnPropertyFlags("x", out var flags), Is.True);
        Assert.That((flags & JsShapePropertyFlags.Writable) == 0, Is.True);
    }


    [Test]
    public void TestSlotInfoBothAccessorUsesSetterAtSlotPlusOne()
    {
        var info = new SlotInfo(5, JsShapePropertyFlags.BothAccessor);
        Assert.That(info.Slot, Is.EqualTo(5));
        Assert.That(info.AccessorSetterSlot, Is.EqualTo(6));
    }

    [Test]
    public void TestAccessorGetterInvocationFromNamedPropertyLoad()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var o = new JsPlainObject(realm);
        JsObject? seenThis = null;

        var getter = new JsHostFunction(realm, (in info) =>
        {
            var runtime = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var f = info.Function;
            Assert.That(args.Length, Is.EqualTo(0));
            Assert.That(thisValue.TryGetObject(out var obj), Is.True);
            seenThis = obj;
            return JsValue.FromInt32(42);
        }, "get_x", 0);

        o.DefineAccessorProperty("x", getter, null,
            JsShapePropertyFlags.Enumerable | JsShapePropertyFlags.Configurable | JsShapePropertyFlags.HasGetter);
        realm.Global["o"] = JsValue.FromObject(o);

        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("o.x;"));
        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
        Assert.That(seenThis, Is.SameAs(o));
    }

    [Test]
    public void TestAccessorSetterInvocationFromNamedPropertyStore()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var o = new JsPlainObject(realm);
        JsObject? seenThis = null;
        var seenArg = JsValue.Undefined;

        var setter = new JsHostFunction(realm, (in info) =>
        {
            var runtime = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var isStrict = info.Function;
            Assert.That(args.Length, Is.EqualTo(1));
            Assert.That(thisValue.TryGetObject(out var obj), Is.True);
            seenThis = obj;
            seenArg = args[0];
            return JsValue.Undefined;
        }, "set_x", 1);

        o.DefineAccessorProperty("x", null, setter,
            JsShapePropertyFlags.Enumerable | JsShapePropertyFlags.Configurable | JsShapePropertyFlags.HasSetter);
        realm.Global["o"] = JsValue.FromObject(o);

        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("o.x = 9;"));
        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(9)); // assignment expression result
        Assert.That(seenThis, Is.SameAs(o));
        Assert.That(seenArg.IsInt32, Is.True);
        Assert.That(seenArg.Int32Value, Is.EqualTo(9));
    }

    [Test]
    public void TestObjectLiteralBytecodeGetterSetterUseThis()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var program = JavaScriptParser.ParseScript("""
                                                   function t() {
                                                       let o = {
                                                           _x: 1,
                                                           get x() { return this._x + 1; },
                                                           set x(v) { this._x = this._x + v; }
                                                       };
                                                       o.x = 3;
                                                       return o.x;
                                                   }
                                                   t() + t();
                                                   """);
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(10));
    }

    [Test]
    public void TestCallPropertyBindsThisForHostFunction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var o = new JsPlainObject(realm);
        o.SetProperty("x", JsValue.FromInt32(7));
        o.DefineDataProperty("m", JsValue.FromObject(new JsHostFunction(realm, "m", 0, (in info) =>
        {
            var runtime = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var f = info.Function;
            var self = thisValue.AsObject();
            _ = self.TryGetProperty("x", out var x);
            return x;
        })), JsShapePropertyFlags.Open);
        realm.Global["o"] = JsValue.FromObject(o);

        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("o.m();"));
        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void TestCallPropertyBindsThisForBytecodeFunction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function t() {
                let o = {
                    x: 3,
                    m: function() { return this.x + 1; }
                };
                return o.m();
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(4));
    }

    [Test]
    public void TestArrayLiteralCreatesOkojoArray()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("[];"));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsObject, Is.True);
        Assert.That(realm.Accumulator.AsObject(), Is.TypeOf<JsArray>());
    }

    [Test]
    public void TestArrayLiteralIndexedReadAndLength()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function t() {
                let a = [1, 2, 3];
                return a[1] + a.length;
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(5));
    }

    [Test]
    public void TestArraySparseFallbackPreservesElementsAndLength()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function t() {
                let a = [];
                a[0] = 1;
                a[1000] = 2;
                return a[0] + a[1000] + a.length;
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1004));
    }

    [Test]
    public void TestObjectPrototypeHasOwnProperty()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function t() {
                let o = { x: 1 };
                return o.hasOwnProperty("x");
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsBool, Is.True);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TestObjectPrototypeToString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function t() {
                let o = {};
                return o.toString();
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("[object Object]"));
    }

    [Test]
    public void TestObjectPrototypeValueOfIdentity()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function t() {
                let o = {};
                return o.valueOf() === o;
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsBool, Is.True);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TestObjectCreateAndGetPrototypeOf()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function t() {
                let p = {};
                let o = Object.create(p);
                return Object.getPrototypeOf(o) === p;
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsBool, Is.True);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TestObjectSetPrototypeOf()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function t() {
                let p = { x: 3 };
                let o = {};
                Object.setPrototypeOf(o, p);
                return o.x;
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(3));
    }

    [Test]
    public void TestObjectGetOwnPropertyDescriptorData()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function t() {
                let o = { x: 5 };
                let d = Object.getOwnPropertyDescriptor(o, "x");
                let w = d.writable;
                let e = d.enumerable;
                let c = d.configurable;
                if (!w) return 0;
                if (!e) return 0;
                if (!c) return 0;
                return d.value + 3;
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsInt32, Is.True);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(8));
    }

    [Test]
    public void TestObjectConstructorBoxesNumberBooleanString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function t() {
                let a = Object(1).toString();
                let b = Object(true).toString();
                let c = Object("x").toString();
                if (a !== "1") return false;
                if (b !== "true") return false;
                if (c !== "x") return false;
                return true;
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsBool, Is.True);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TestObjectGetPrototypeOfBoxesPrimitive()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function t() {
                return Object.getPrototypeOf(1) === Object.getPrototypeOf(Object(1));
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsBool, Is.True);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void TestObjectGetOwnPropertyDescriptorOnStringPrimitive()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function t() {
                let d = Object.getOwnPropertyDescriptor("ab", "0");
                if (d === undefined) return "missing";
                return d.value;
            }
            t();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("a"));
    }

    // Dont add tests here any more, add them to the appropriate test class (e.g. OkojoPrototypeTests, OkojoContextTests, etc.) instead. This class should only
}
