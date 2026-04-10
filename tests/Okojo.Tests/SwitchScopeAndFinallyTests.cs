using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class SwitchScopeAndFinallyTests
{
    [Test]
    public void Switch_LetBinding_DoesNotLeakOutsideSwitch()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   switch (0) {
                                                                     case 0:
                                                                       let x = 1;
                                                                       break;
                                                                   }
                                                                   typeof x === "undefined";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Switch_CaseLexical_TdzAcrossCases()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function f(v) {
                                                                     switch (v) {
                                                                       case 0:
                                                                         let x = 1;
                                                                         return x;
                                                                       case 1:
                                                                         return x; // TDZ when entering case 1 directly
                                                                       default:
                                                                         return 9;
                                                                     }
                                                                   }
                                                                   f(1);
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Is.EqualTo("Cannot access 'x' before initialization"));
    }

    [Test]
    public void Switch_DuplicateLetAcrossCases_IsEarlySyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var ex = Assert.Throws<JsParseException>(() =>
            compiler.Compile(JavaScriptParser.ParseScript("""
                                                          switch (0) {
                                                            case 0: let x = 1; break;
                                                            case 1: let x = 2; break;
                                                          }
                                                          """)));

        Assert.That(ex, Is.Not.Null);
    }

    [Test]
    public void Switch_Discriminant_IsEvaluated_Outside_SwitchLexicalEnvironment()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 'outside';
                                                                   let probeExpr;
                                                                   switch (probeExpr = function() { return x; }, null) {
                                                                     case null:
                                                                       let x = 'inside';
                                                                   }
                                                                   probeExpr() === 'outside';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Switch_CaseTest_Uses_SwitchLexicalEnvironment()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 'outside';
                                                                   let probeSelector;
                                                                   switch (null) {
                                                                     case probeSelector = function() { return x; }, null:
                                                                       let x = 'inside';
                                                                   }
                                                                   probeSelector() === 'inside';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Switch_BreakInsideTryFinally_RunsFinally()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let log = 0;
                                                                   switch (1) {
                                                                     case 1:
                                                                       try {
                                                                         break;
                                                                       } finally {
                                                                         log = 1;
                                                                       }
                                                                     default:
                                                                       log = 2;
                                                                   }
                                                                   log === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Switch_ReturnInsideTryFinally_RunsFinallyThenReturns()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let g = 0;
                                                                   function f() {
                                                                     switch (1) {
                                                                       case 1:
                                                                         try {
                                                                           return 7;
                                                                         } finally {
                                                                           g = 3;
                                                                         }
                                                                     }
                                                                     return 0;
                                                                   }
                                                                   f() === 7 && g === 3;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Switch_ThrowInsideTryFinally_RunsFinallyThenThrows()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let g = 0;
                                                                   let caught = 0;
                                                                   function f() {
                                                                     switch (1) {
                                                                       case 1:
                                                                         try {
                                                                           throw 5;
                                                                         } finally {
                                                                           g = 9;
                                                                         }
                                                                     }
                                                                   }
                                                                   try {
                                                                     f();
                                                                   } catch (e) {
                                                                     caught = e;
                                                                   }
                                                                   g === 9 && caught === 5;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_HeadLexical_TdzCoversRightHandExpression()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 'outside';
                                                                   let probeExpr;
                                                                   for (let x of (probeExpr = function() { return typeof x; }, ['inside'])) {
                                                                     break;
                                                                   }
                                                                   probeExpr();
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Is.EqualTo("Cannot access 'x' before initialization"));
    }

    [Test]
    public void ForIn_HeadLexical_TdzCoversRightHandExpression()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 'outside';
                                                                   let probeExpr;
                                                                   for (let x in { key: probeExpr = function() { return typeof x; } }) {
                                                                     break;
                                                                   }
                                                                   probeExpr();
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Is.EqualTo("Cannot access 'x' before initialization"));
    }

    [Test]
    public void ForOf_BodyUsesLoopHeadLexicalBinding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 'outside';
                                                                   let probeDecl, probeBody;
                                                                   for (let [x, _ = probeDecl = function() { return x; }] of [['inside']]) {
                                                                     probeBody = function() { return x; };
                                                                   }
                                                                   probeDecl() === 'inside' && probeBody() === 'inside';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForIn_BodyUsesLoopHeadLexicalBinding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 'outside';
                                                                   let probeDecl, probeBody;
                                                                   for (let [x, _ = probeDecl = function() { return x; }] in { i: 1 }) {
                                                                     probeBody = function() { return x; };
                                                                   }
                                                                   probeDecl() === 'i' && probeBody() === 'i';
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ForOf_RhsClosure_UsesTdzHeadEnvironment_EvenWithDestructuringBodyBindings()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var probeBefore = function() { return x; };
                                                                   let x = 'outside';
                                                                   var probeExpr, probeDecl, probeBody;

                                                                   for (
                                                                       let [x, _, __ = probeDecl = function() { return x; }]
                                                                       of
                                                                       [['inside', probeExpr = function() { typeof x; }]]
                                                                     )
                                                                     probeBody = function() { return x; };

                                                                   probeBefore() === 'outside';
                                                                   probeExpr();
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Is.EqualTo("Cannot access 'x' before initialization"));
    }

    [Test]
    public void ForIn_RhsClosure_UsesTdzHeadEnvironment_EvenWithDestructuringBodyBindings()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 'outside';
                                                                   var probeDecl, probeExpr, probeBody;

                                                                   for (
                                                                       let [x, _ = probeDecl = function() { return x; }]
                                                                       in
                                                                       { i: probeExpr = function() { typeof x; } }
                                                                     )
                                                                     probeBody = function() { return x; };

                                                                   probeExpr();
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Is.EqualTo("Cannot access 'x' before initialization"));
    }
}
