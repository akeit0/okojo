using Okojo.JavaScript;
using Okojo.JavaScript.Compiler.Experimental;
using Okojo.JavaScript.Embedding;

namespace Okojo.Compiler.Tests;

public sealed class TempCptnProbe
{
    private static string Run(string source)
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(source);
        realm.Execute(script);
        return realm.Accumulator.IsUndefined ? "undefined" : realm.Accumulator.ToString() ?? "?";
    }

    [Test]
    public void Dump_WhileBreak()
    {
        Assert.That(Run("1; while (true) { break; }"), Is.EqualTo("undefined"));
    }

    [Test]
    public void Dump_WhileBodyValue()
    {
        Assert.That(Run("2; while (true) { 3; break; }"), Is.EqualTo("3"));
    }

    [Test]
    public void Dump_LabeledDoContinueOuter()
    {
        Assert.That(
            Run("4; outer: do { while (true) { continue outer; } } while (false)"),
            Is.EqualTo("undefined")
        );
    }

    [Test]
    public void Dump_EvalLabeledDoContinueOuter()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var result = realm.Evaluate(
            "eval('4; outer: do { while (true) { continue outer; } } while (false)');"
        );
        TestContext.Progress.WriteLine("EVAL RESULT: " + result);
    }

    [Test]
    public void Dump_EvalWhileBreak()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var result = realm.Evaluate("eval('1; while (true) { break; }');");
        TestContext.Progress.WriteLine("EVAL RESULT2: " + result);
    }

    [Test]
    public void Dump_EvalVarCarry()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var a = realm.Evaluate("eval('7; var t8;');");
        var b = realm.Evaluate("eval('var u = 2;');");
        var c = realm.Evaluate(
            "eval('99; do { -99; try { 39 } catch (e) { -1 } finally { 42; continue; -3 }; -77 } while (false);');"
        );
        TestContext.Progress.WriteLine($"VARCARRY a={a} b={b} tryfinally={c}");
    }
}
