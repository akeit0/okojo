using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

/// <summary>
///     C4 root-list completion-sink elision: suppression must not change any
///     observable completion value (V8 reference behavior recorded in the
///     feature note).
/// </summary>
public class C4CompletionElisionTests
{
    private static object Eval(string source)
    {
        var realm = JsRuntime.Create().DefaultRealm;
        return realm.Eval(source).TryRead<object>(out var value) ? value : realm.Accumulator;
    }

    [Test]
    public void Completion_SequentialExpressions_LastWins()
    {
        Assert.That(Eval("5; 6;"), Is.EqualTo(6d));
    }

    [Test]
    public void Completion_EmptyStatement_CarriesPreviousValue()
    {
        Assert.That(Eval("1; ;"), Is.EqualTo(1d));
    }

    [Test]
    public void Completion_LeadingEmpty_ThenValue()
    {
        Assert.That(Eval("; 1;"), Is.EqualTo(1d));
    }

    [Test]
    public void Completion_V8Reference_IfWithoutElse_ResetsCompletion()
    {
        // V8 (rewriter.cc arms-disagree reset): result is undefined, not 1.
        Assert.That(Eval("1; if (false) 2;"), Is.EqualTo(null));
    }

    [Test]
    public void Completion_LoopWritesOverwrittenByFinalGuarantee()
    {
        Assert.That(Eval("for (let i = 0; i < 2; i++) { i; } 42;"), Is.EqualTo(42d));
    }

    [Test]
    public void Completion_LoopCarry_WhenLastStatementCarries()
    {
        // The loop's last capture is carried by the var declaration, then read.
        Assert.That(
            Eval("var acc = 0; for (let i = 0; i < 3; i++) { acc = i; } acc;"),
            Is.EqualTo(2d)
        );
    }

    [Test]
    public void Completion_SuppressedEarlyTraffic_NestedBlocksAndLabels()
    {
        Assert.That(
            Eval(
                """
                lbl: {
                    100;
                    if (true) break lbl;
                    200;
                }
                300;
                """
            ),
            Is.EqualTo(300d)
        );
    }

    [Test]
    public void Completion_TryFinally_KeepsFinallyCompletion()
    {
        Assert.That(
            Eval(
                """
                try {
                    1;
                } finally {
                    2;
                }
                3;
                """
            ),
            Is.EqualTo(3d)
        );
    }

    [Test]
    public void Completion_ZeroIterationLoop_CarriesThenResetByNextValue()
    {
        Assert.That(Eval("7; for (let i = 0; i < 0; i++) { 9; } 8;"), Is.EqualTo(8d));
    }
}
