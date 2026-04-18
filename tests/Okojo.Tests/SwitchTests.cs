using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class SwitchTests
{
    [Test]
    public void Switch_BasicMatch_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   switch (2) {
                                                                     case 1: x = 1; break;
                                                                     case 2: x = 2; break;
                                                                     default: x = 3;
                                                                   }
                                                                   x === 2;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Switch_Fallthrough_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   switch (1) {
                                                                     case 1: x = x + 1;
                                                                     case 2: x = x + 2; break;
                                                                     default: x = 99;
                                                                   }
                                                                   x === 3;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void LabeledSwitch_BreakLabel_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   done: switch (1) {
                                                                     case 1:
                                                                       x = 1;
                                                                       break done;
                                                                     default:
                                                                       x = 2;
                                                                   }
                                                                   x === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void LabeledSwitch_ContinueLabel_ThrowsSyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var ex = Assert.Throws<JsParseException>(() =>
            JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                          L: switch (1) {
                                                            case 1:
                                                              continue L;
                                                          }
                                                          """)));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("does not denote an iteration statement"));
    }

    [Test]
    public void StrictSwitch_CaseFunctionDeclaration_DoesNotLeakToFunctionScope()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t() {
                                                                     "use strict";
                                                                     var err1, err2;
                                                                     try { f; } catch (e) { err1 = e; }
                                                                     switch (1) {
                                                                       case 1:
                                                                         function f() { }
                                                                     }
                                                                     try { f; } catch (e) { err2 = e; }
                                                                     return err1 && err1.constructor === ReferenceError &&
                                                                            err2 && err2.constructor === ReferenceError;
                                                                   }
                                                                   t();
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Switch_FallthroughContinue_CompletionValue_Preserved()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   eval('8; do { switch ("a") { case "a": 9; case "b": 10; continue; default: } } while (false)') === 10;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Switch_Fallthrough_CompletionValue_Uses_Last_NonEmpty_Case_Value()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   eval("1; switch ('a') { case 'a': 2; case 'b': 3; }") === 3;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Switch_DefaultBreak_CompletionValue_Preserves_PreBreak_Value()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   eval("2; switch ('a') { default: case 'b': { 3; break; } }") === 3;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
