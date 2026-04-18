using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class StackTraceTests
{
    [Test]
    public void UncaughtRuntimeException_CapturesFunctionFramesInOrder()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function inner() {
                                                                       let x = 1;
                                                                       x();
                                                                   }
                                                                   function outer() {
                                                                       inner();
                                                                   }
                                                                   outer();
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.StackFrames.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(ex.StackFrames[0].FunctionName, Is.EqualTo("inner"));
        Assert.That(ex.StackFrames[1].FunctionName, Is.EqualTo("outer"));
    }

    [Test]
    public void CaughtRuntimeErrorObject_ExposesStackProperty()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function inner() {
                                                                       let x = 1;
                                                                       x();
                                                                   }
                                                                   function outer() {
                                                                       inner();
                                                                   }
                                                                   let out = "";
                                                                   try {
                                                                       outer();
                                                                   } catch (e) {
                                                                       out = e.stack;
                                                                   }
                                                                   out;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        var stack = realm.Accumulator.AsString();
        Assert.That(stack, Does.Contain("TypeError: Not a function"));
        Assert.That(stack, Does.Contain("at inner"));
        Assert.That(stack, Does.Contain("at outer"));
    }

    [Test]
    public void CaughtRuntimeErrorObject_StackContainsSourceLineAndColumn()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function inner() {
                                                                       let x = 1;
                                                                       x();
                                                                   }
                                                                   try {
                                                                       inner();
                                                                   } catch (e) {
                                                                       e.stack;
                                                                   }
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        var stack = realm.Accumulator.AsString();
        Assert.That(stack, Does.Match(@"at inner @ \d+:\d+"));
    }

    [Test]
    public void UncaughtThrownErrorObject_Message_UsesErrorSummary_NotEmbeddedStack()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function inner() {
                                                                       throw new TypeError("boom");
                                                                   }
                                                                   inner();
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.DetailCode, Is.EqualTo("JS_THROW_VALUE"));
        Assert.That(ex.Message, Is.EqualTo("Throw: TypeError: boom"));
        Assert.That(ex.Message, Does.Not.Contain("stack:"));
        Assert.That(ex.StackFrames.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(ex.StackFrames[0].FunctionName, Is.EqualTo("inner"));
    }

    [Test]
    public void RecursiveRuntimeException_CondensesRepeatedFrames()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function recur(n) {
                                                                       if (n === 0) {
                                                                           let x = 1;
                                                                           x();
                                                                       }
                                                                       recur(n - 1);
                                                                   }
                                                                   recur(12);
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);

        var stack = ex!.FormatOkojoStackTrace(4);
        Assert.That(stack, Does.Contain("at recur"));
        Assert.That(stack, Does.Contain("... repeated"));
    }

    [Test]
    public void DeepRecursiveCalls_ThrowManagedStackOverflowAsJsRuntimeException()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["bounce"] = JsValue.FromObject(new JsHostFunction(realm, static (in info) =>
        {
            var innerRealm = info.Realm;
            var args = info.Arguments;
            var fn = (JsFunction)args[0].AsObject();
            return innerRealm.Call(fn, JsValue.Undefined, args[1]);
        }, "bounce", 2));

        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function recur(n) {
                                                                       if (n === 0) return 0;
                                                                       return bounce(recur, n - 1);
                                                                   }
                                                                   recur(5000);
                                                                   """));

        var ex = Assert.Throws<JsFatalRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.RangeError));
        Assert.That(ex.Message, Is.EqualTo("Maximum call stack size exceeded"));
        Assert.That(ex.InnerException, Is.TypeOf<StackOverflowException>());
    }

    [Test]
    public void StrictTailRecursiveCalls_ReusesVmFrame()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var callCount = 0;
                                                                   (function f(n) {
                                                                       "use strict";
                                                                       if (n === 0) {
                                                                           callCount += 1;
                                                                           return;
                                                                       }

                                                                       return f(n - 1);
                                                                   }(100000));
                                                                   callCount;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsNumber, Is.True);
        Assert.That(realm.Accumulator.NumberValue, Is.EqualTo(1));
    }
}
