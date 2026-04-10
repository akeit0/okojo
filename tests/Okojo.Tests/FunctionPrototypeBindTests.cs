using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class FunctionPrototypeBindTests
{
    [Test]
    public void FunctionPrototypeBind_BindsThisAndLeadingArguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function add(a, b) { return this.base + a + b; }
                                                                   var bound = add.bind({ base: 10 }, 2);
                                                                   bound(3) === 15;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeBind_ResultHasNoOwnPrototypeProperty()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function F() {}
                                                                   var bound = F.bind(null);
                                                                   ("prototype" in bound) === false &&
                                                                   Object.prototype.hasOwnProperty.call(bound, "prototype") === false;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeBind_Construct_ForwardsToTargetConstructorSemantics()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function F(a, b) {
                                                                     this.sum = a + b;
                                                                     this.nt = new.target;
                                                                   }

                                                                   var bound = F.bind({ ignored: true }, 2);
                                                                   var o = new bound(3);
                                                                   o.sum === 5 &&
                                                                   o.nt === F &&
                                                                   Object.getPrototypeOf(o) === F.prototype;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeBind_UsesObservableNameProperty()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var target = Object.defineProperty(function() {}, "name", { value: "target" });
                                                                   var bound = target.bind(null);
                                                                   var desc = Object.getOwnPropertyDescriptor(bound, "name");
                                                                   bound.name === "bound target" &&
                                                                   desc.value === "bound target" &&
                                                                   desc.enumerable === false &&
                                                                   desc.writable === false &&
                                                                   desc.configurable === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeBind_NonStringNameFallsBackToEmptyString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var target = Object.defineProperty(function() {}, "name", { value: 23 });
                                                                   target.bind(null).name === "bound ";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototypeBind_NameGetterAbruptCompletionPropagates()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var ok = false;
                                var target = Object.defineProperty(function() {}, "name", {
                                  get: function() { throw new Error("boom"); }
                                });
                                try { target.bind(null); } catch (e) { ok = e.message === "boom"; }
                                ok;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }
}
