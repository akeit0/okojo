using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class StatementCompletionRegressionTests
{
    [Test]
    public void Eval_WhileWithoutIteration_ReturnsUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('1; while (false) { }');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void Eval_WhileContinue_PreservesLastNonEmptyBodyCompletion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval("var c = 0, odds = 0; while (c < 10) { c++; if (((''+c/2).split('.')).length > 1) continue; odds++; }");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(4));
    }

    [Test]
    public void Eval_WhileBreak_ReturnsPriorBodyCompletion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval("while (1) { marker = 1; break; marker = 2; }");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void Eval_LabeledBlockBreak_DoesNotDropFollowingStatements()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval("var i = 0; woohoo:{ while(true){ i++; if (i == 10) { break woohoo; } } throw 1; } i;");
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(10));
    }

    [Test]
    public void Eval_TryCatch_EmptyTryCompletion_IsUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('1; try { } catch (err) { }');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void Eval_TryFinally_EmptyClauses_ReturnUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('1; try { } finally { }');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void Eval_TryFinally_PreservesTryCompletionWhenFinallyIsNormal()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('1; try { 3; } finally { 4; }');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(3));
    }

    [Test]
    public void Eval_TryCatchFinally_BreakOnlyFinalizer_ReturnsUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('99; do { -99; try { 39 } catch (e) { -1 } finally { break; -2 }; } while (false);');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void Eval_TryCatchFinally_BreakAfterValue_UsesFinalizerValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('99; do { -99; try { 39 } catch (e) { -1 } finally { 42; break; -2 }; } while (false);');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void Eval_IfElseBreak_ReturnsUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('1; do { 2; if (false) { } else { break; } } while (false)');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void Eval_DoWhileBreak_WithoutBodyValue_ReturnsUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('1; do { break; } while (false)');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void Eval_DoWhileBreak_PreservesPriorBodyValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('2; do { 3; break; } while (false)');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(3));
    }

    [Test]
    public void Eval_DoWhileContinue_WithoutBodyValue_ReturnsUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('4; do { continue; } while (false)');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void Eval_DoWhileContinue_PreservesPriorBodyValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('5; do { 6; continue; } while (false)');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(6));
    }

    [Test]
    public void Eval_VarStatement_PreservesPreviousCompletionValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('7; var test262id;');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void Eval_ForOfWithoutIteration_ReturnsUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('1; for (var a of []) { }');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void Eval_ForOfWithIteration_PreservesLastNonEmptyBodyCompletion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('2; for (var b of [0]) { 3; }');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(3));
    }

    [Test]
    public void Eval_ForInWithoutIteration_ReturnsUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('1; for (var a in {}) { }');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void Eval_ForInWithIteration_PreservesLastNonEmptyBodyCompletion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('2; for (var b in { x: 0 }) { 3; }');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(3));
    }

    [Test]
    public void Eval_ForWithoutIteration_ReturnsUndefined()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('1; for (var run = false; run; ) { }');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }

    [Test]
    public void Eval_ForBodyCompletion_DoesNotLeak_When_NoIterationOccurs()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            eval('2; for (var run = false; run; ) { 3; }');
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsUndefined, Is.True);
    }
}
