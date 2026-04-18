using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class FunctionPrototypeApplyAndHasInstanceTests
{
    [Test]
    public void FunctionPrototypeApply_RejectsPrimitiveArgArray()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                function fn() {}
                                var ok1 = false;
                                var ok2 = false;
                                var ok3 = false;
                                try { fn.apply(null, true); } catch (e) { ok1 = e && e.name === "TypeError"; }
                                try { fn.apply(null, "1,2,3"); } catch (e) { ok2 = e && e.name === "TypeError"; }
                                try { fn.apply(null, Symbol("s")); } catch (e) { ok3 = e && e.name === "TypeError"; }
                                ok1 && ok2 && ok3;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeSymbolHasInstance_HasExpectedDescriptor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var desc = Object.getOwnPropertyDescriptor(Function.prototype, Symbol.hasInstance);
                                typeof Function.prototype[Symbol.hasInstance] === "function" &&
                                desc.value === Function.prototype[Symbol.hasInstance] &&
                                desc.writable === false &&
                                desc.enumerable === false &&
                                desc.configurable === false;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeSymbolHasInstance_HandlesCallableAndNonCallableReceivers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function F() {}
                                                                   var instance = new F();
                                                                   Function.prototype[Symbol.hasInstance].call(F, instance) === true &&
                                                                   Function.prototype[Symbol.hasInstance].call({}, instance) === false &&
                                                                   Function.prototype[Symbol.hasInstance].call(undefined, instance) === false;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeSymbolHasInstance_UsesObservablePrototypeWalk()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                function F() {}
                                var ok1 = false;
                                var ok2 = false;
                                var o = new Proxy({}, {
                                  getPrototypeOf: function() { throw new Error("boom"); }
                                });
                                var o2 = Object.create(o);
                                try { F[Symbol.hasInstance](o); } catch (e) { ok1 = e && e.message === "boom"; }
                                try { F[Symbol.hasInstance](o2); } catch (e) { ok2 = e && e.message === "boom"; }
                                ok1 && ok2;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void FunctionConstructor_Prototype_HasExpectedAttributes()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var desc = Object.getOwnPropertyDescriptor(Function, "prototype");
                                desc.value === Function.prototype &&
                                desc.writable === false &&
                                desc.enumerable === false &&
                                desc.configurable === false;
                                """);

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

        var result = realm.Eval("""
                                var ok = false;
                                try { OtherFunctionPrototype.apply.call({}, {}, []); } catch (e) { ok = e && e.constructor === OtherTypeError; }
                                var C = OtherEval("(class {})");
                                try { C(); } catch (e) { ok = ok && e && e.constructor === OtherTypeError; }
                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
