using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class Test262AsyncFunctionRegressionsTests
{
    [Test]
    public void HarnessStyle_FunctionPrototypeCallBind_AndArrayHelpers_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var __join = Function.prototype.call.bind(Array.prototype.join);
                                                                   var __push = Function.prototype.call.bind(Array.prototype.push);
                                                                   var __propertyIsEnumerable = Function.prototype.call.bind(Object.prototype.propertyIsEnumerable);
                                                                   var arr = [1, 2];
                                                                   __push(arr, 3);
                                                                   (__join(arr, ",") === "1,2,3") &&
                                                                   (Array.isArray(arr) === true) &&
                                                                   (__propertyIsEnumerable({ x: 1 }, "x") === true);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void AsyncFunction_DefaultParameterThrow_RejectsPromise()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   var y = null;
                                                                   async function foo(x = y()) {}
                                                                   foo().then(function () { globalThis.out = 1; }, function () { globalThis.out = 2; });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void Arguments_Object_IsMapped_ForNonStrictSimpleParameters()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function f(a) {
                                                                     arguments[0] = 2;
                                                                     var first = a === 2;
                                                                     a = 3;
                                                                     var second = arguments[0] === 3;
                                                                     return first && second && arguments.length === 1;
                                                                   }
                                                                   f(1);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_Object_IsUnmapped_ForNonSimpleParameters()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function f(a = 10) {
                                                                     arguments[0] = 2;
                                                                     var first = a === 1;
                                                                     a = 3;
                                                                     var second = arguments[0] === 2;
                                                                     return first && second && arguments.length === 1;
                                                                   }
                                                                   f(1);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
