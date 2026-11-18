using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class FunctionPrototypeApplyAndHasInstanceTests
{
    [Test]
    public void FunctionPrototypeInvocation_PreservesFramesAndArgumentChanges()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            function target(a, b) { "use strict"; return [this, a, b]; }
            function forward() { return target.apply(7, arguments); }
            function mapped(a) { a = 9; return target.apply(7, arguments); }
            function changed() {
                delete arguments[0];
                Object.setPrototypeOf(arguments, { 0: 8 });
                return target.apply(7, arguments);
            }
            function accessor() {
                Object.defineProperty(arguments, "0", { get() { return 6; } });
                return target.apply(7, arguments);
            }
            function resized() { arguments.length = 1; return target.apply(7, arguments); }
            function fail() { throw 12; }
            var caught = 0;
            try { fail.call(); } catch (e) { caught += e; }
            try { fail.apply(null, null); } catch (e) { caught += e; }
            finally { caught++; }
            var replacement = function () { return 11; };
            target.call = replacement;
            var customCall = target.call(null);
            delete target.call;
            target.apply = replacement;
            var customApply = forward(1, 2);
            delete target.apply;
            forward(1, 2).join() === "7,1,2" &&
            target.call(7, 1, 2).join() === "7,1,2" &&
            target.call()[0] === undefined &&
            mapped(1)[1] === 9 && changed(1)[1] === 8 &&
            accessor(1)[1] === 6 && resized(1, 2)[2] === undefined &&
            caught === 25 && customCall === 11 && customApply === 11;
            """
        );

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeApply_RejectsPrimitiveArgArray()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            function fn() {}
            var ok1 = false;
            var ok2 = false;
            var ok3 = false;
            try { fn.apply(null, true); } catch (e) { ok1 = e && e.name === "TypeError"; }
            try { fn.apply(null, "1,2,3"); } catch (e) { ok2 = e && e.name === "TypeError"; }
            try { fn.apply(null, Symbol("s")); } catch (e) { ok3 = e && e.name === "TypeError"; }
            ok1 && ok2 && ok3;
            """
        );

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeSymbolHasInstance_HasExpectedDescriptor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var desc = Object.getOwnPropertyDescriptor(Function.prototype, Symbol.hasInstance);
            typeof Function.prototype[Symbol.hasInstance] === "function" &&
            desc.value === Function.prototype[Symbol.hasInstance] &&
            desc.writable === false &&
            desc.enumerable === false &&
            desc.configurable === false;
            """
        );

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeSymbolHasInstance_HandlesCallableAndNonCallableReceivers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function F() {}
                var instance = new F();
                Function.prototype[Symbol.hasInstance].call(F, instance) === true &&
                Function.prototype[Symbol.hasInstance].call({}, instance) === false &&
                Function.prototype[Symbol.hasInstance].call(undefined, instance) === false;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void InstanceOf_BoundFunctionUsesTargetsCurrentHasInstance()
    {
        using var runtime = JsRuntime.Create();
        var result = runtime.DefaultRealm.Eval(
            """
            function F() {}
            const bound = F.bind(null).bind(null);
            Object.defineProperty(F, Symbol.hasInstance, { value: value => value === 42 });
            42 instanceof bound &&
            !(new F() instanceof bound) &&
            Function.prototype[Symbol.hasInstance].call(bound, 42);
            """
        );
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeSymbolHasInstance_UsesObservablePrototypeWalk()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            function F() {}
            var ok1 = false;
            var ok2 = false;
            var ok3 = false;
            var o = new Proxy({}, {
              getPrototypeOf: function() { throw new Error("boom"); }
            });
            var o2 = Object.create(o);
            try { F[Symbol.hasInstance](o); } catch (e) { ok1 = e && e.message === "boom"; }
            try { F[Symbol.hasInstance](o2); } catch (e) { ok2 = e && e.message === "boom"; }
            try { o2 instanceof F; } catch (e) { ok3 = e && e.message === "boom"; }
            ok1 && ok2 && ok3;
            """
        );

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void FunctionConstructor_Prototype_HasExpectedAttributes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            var desc = Object.getOwnPropertyDescriptor(Function, "prototype");
            desc.value === Function.prototype &&
            desc.writable === false &&
            desc.enumerable === false &&
            desc.configurable === false;
            """
        );

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeApply_And_ClassCall_Use_Function_Realm_For_TypeErrors()
    {
        var engine = JsRuntime.Create();
        var realm = engine.DefaultRealm;
        var otherRealm = realm.Agent.CreateRealm();
        realm.Global["OtherFunctionPrototype"] = JsValue.FromObject(otherRealm.FunctionPrototype);
        realm.Global["OtherEval"] = otherRealm.Global["eval"];
        realm.Global["OtherTypeError"] = JsValue.FromObject(otherRealm.TypeErrorConstructor);

        var result = realm.Eval(
            """
            var ok = false;
            try { OtherFunctionPrototype.apply.call({}, {}, []); } catch (e) { ok = e && e.constructor === OtherTypeError; }
            var C = OtherEval("(class {})");
            try { C(); } catch (e) { ok = ok && e && e.constructor === OtherTypeError; }
            ok;
            """
        );

        Assert.That(result.IsTrue, Is.True);
    }
}
