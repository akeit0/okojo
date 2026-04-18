using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class EvalTests
{
    [Test]
    public void GlobalEval_IsDefinedFunction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   typeof eval;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("function"));
    }

    [Test]
    public void GlobalEval_AsyncFunctionDeclarationCompletionMatchesTest262Cases()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var a = eval('async function f() {}');
                                                                   var b = eval('1; async function g() {}');
                                                                   a === undefined && b === 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalEval_FunctionDeclarationOnly_ReturnsUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   eval('function h() {}') === undefined;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalEval_HashbangComment_IsAcceptedAtSourceStart()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var a = eval('#!\n');
                                                                   var b = eval('#!\n1');
                                                                   var c = eval('#!2\n');
                                                                   a === undefined && b === 1 && c === undefined;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalEval_TriviaOnlyCommentSources_ReturnUndefined_WithoutChangingBindings()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   delete globalThis.xx;
                                                                   var a = eval('/*var xx = 1*/');
                                                                   var b = eval('//var xx = 1');
                                                                   a === undefined && b === undefined && typeof xx === 'undefined';
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void GlobalEval_LineCommentLineTerminator_StillParsesFollowingCode()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var yy = 0;
                                                                   eval("//var \u2028yy = -1") === -1 && yy === -1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void IndirectEval_CompileTimeContinueError_IsSyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let caught;
                                                                   try {
                                                                     (0, eval)("continue;");
                                                                   } catch (e) {
                                                                     caught = e;
                                                                   }

                                                                   caught && caught.constructor === SyntaxError;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void StrictIndirectEval_VarDeclaration_DoesNot_Leak_Global_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   delete globalThis.foo;
                                                                   (0, eval)('"use strict"; var foo = 88;');
                                                                   !('foo' in globalThis);
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void StrictIndirectEval_FunctionDeclaration_DoesNot_Leak_Caller_Or_Global_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var typeofInside;
                                                                   delete globalThis.fun;

                                                                   (function() {
                                                                     (0, eval)("'use strict'; function fun(){}");
                                                                     typeofInside = typeof fun;
                                                                   }());

                                                                   typeofInside === "undefined" && typeof globalThis.fun === "undefined";
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void SloppyIndirectEval_VarDeclaration_Creates_Configurable_Global_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   delete globalThis.x;
                                                                   var initial;
                                                                   (0, eval)('initial = x; var x = 9;');
                                                                   var d = Object.getOwnPropertyDescriptor(globalThis, 'x');
                                                                   initial === undefined &&
                                                                     d.value === 9 &&
                                                                     d.writable === true &&
                                                                     d.enumerable === true &&
                                                                     d.configurable === true;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void SloppyIndirectEval_FunctionDeclaration_Updates_Configurable_Global_Binding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var initial = null;
                                                                   Object.defineProperty(globalThis, 'f', {
                                                                     enumerable: false,
                                                                     writable: false,
                                                                     configurable: true
                                                                   });
                                                                   (0, eval)('initial = f; function f() { return 345; }');
                                                                   var d = Object.getOwnPropertyDescriptor(globalThis, 'f');
                                                                   typeof initial === 'function' &&
                                                                     initial() === 345 &&
                                                                     d.writable === true &&
                                                                     d.enumerable === true &&
                                                                     d.configurable === true;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void SloppyIndirectEval_FunctionValidation_Is_Atomic()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   delete globalThis.shouldNotBeDefined1;
                                                                   delete globalThis.shouldNotBeDefined2;
                                                                   var caught;
                                                                   try {
                                                                     (0, eval)("function shouldNotBeDefined1() {} function NaN() {} function shouldNotBeDefined2() {}");
                                                                   } catch (e) {
                                                                     caught = e;
                                                                   }
                                                                   caught &&
                                                                     caught.constructor === TypeError &&
                                                                     Object.getOwnPropertyDescriptor(globalThis, 'shouldNotBeDefined1') === undefined &&
                                                                     Object.getOwnPropertyDescriptor(globalThis, 'shouldNotBeDefined2') === undefined;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void SloppyIndirectEval_VarDeclaration_Colliding_With_Global_Lexical_Throws_SyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let x;
                                                                   var caught;
                                                                   try {
                                                                     (0, eval)('var x;');
                                                                   } catch (e) {
                                                                     caught = e;
                                                                   }
                                                                   caught && caught.constructor === SyntaxError;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void SloppyIndirectEval_VarDeclaration_On_NonExtensible_Global_Throws_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   delete globalThis.unlikelyVariableName;
                                                                   var caught;
                                                                   Object.preventExtensions(globalThis);
                                                                   try {
                                                                     (0, eval)('var unlikelyVariableName;');
                                                                   } catch (e) {
                                                                     caught = e;
                                                                   }
                                                                   caught &&
                                                                     caught.constructor === TypeError &&
                                                                     Object.getOwnPropertyDescriptor(globalThis, 'unlikelyVariableName') === undefined;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
