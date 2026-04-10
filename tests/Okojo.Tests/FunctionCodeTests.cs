using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class FunctionCodeTests
{
    [Test]
    public void StrictAccessorGetter_Receives_Primitive_ThisValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   'use strict';
                                                                   Object.defineProperty(Object.prototype, "x", { get: function () { return this; } });
                                                                   (5).x === 5 && typeof (5).x === "number";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void StrictBlockFunctionDeclaration_DoesNotLeak_OutsideBlock()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var err1, err2;
                                                                   (function() {
                                                                     'use strict';
                                                                     try { f; } catch (exception) { err1 = exception; }
                                                                     { function f() {} }
                                                                     try { f; } catch (exception) { err2 = exception; }
                                                                   }());
                                                                   err1 && err1.constructor === ReferenceError &&
                                                                     err2 && err2.constructor === ReferenceError;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void SloppyObjectMethod_DetachedCall_UsesGlobalThis()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var global = (function() { return this; }());
                                                                   var thisValue = null;
                                                                   var method = {
                                                                     method() {
                                                                       thisValue = this;
                                                                     }
                                                                   }.method;
                                                                   method();
                                                                   thisValue === global;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void SloppyObjectGeneratorMethod_DetachedCall_UsesGlobalThis()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var global = (function() { return this; }());
                                                                   var thisValue = null;
                                                                   var method = {
                                                                     *method() {
                                                                       thisValue = this;
                                                                     }
                                                                   }.method;
                                                                   method().next();
                                                                   thisValue === global;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BlockFunctionDeclaration_IsInitializedWithinStrictBlock()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function f5(one) {
                                                                     'use strict';
                                                                     var x = one + 1;
                                                                     let y = one + 2;
                                                                     const u = one + 4;
                                                                     {
                                                                       let z = one + 3;
                                                                       const v = one + 5;
                                                                       function f() {
                                                                         return [one, x, y, z, u, v].join(',');
                                                                       }
                                                                       return f();
                                                                     }
                                                                   }
                                                                   f5(1);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1,2,3,4,5,6"));
    }
}
