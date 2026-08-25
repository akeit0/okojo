using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class LabeledControlFlowTests
{
    [Test]
    public void LabeledBreak_OnBlock_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                let x = 0;
                done: {
                  x = 1;
                  break done;
                  x = 2;
                }
                x === 1;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void LabeledContinue_ToOuterLoop_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                let c = 0;
                outer: for (let i = 0; i < 3; i++) {
                  for (let j = 0; j < 3; j++) {
                    if (j === 1) continue outer;
                    c = c + 1;
                  }
                }
                c === 3;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void LabeledBreak_ThroughFinally_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                let log = 0;
                outer: while (true) {
                  try {
                    break outer;
                  } finally {
                    log = 1;
                  }
                }
                log === 1;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void LabeledContinue_ThroughFinally_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            FlatJavaScriptParser.ParseScript(
                """
                let c = 0;
                outer: for (let i = 0; i < 2; i++) {
                  for (let j = 0; j < 2; j++) {
                    try {
                      c = c + 1;
                      continue outer;
                    } finally {
                      c = c + 10;
                    }
                  }
                }
                c === 22;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void LabeledBreak_UndefinedLabel_ThrowsSyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var ex = Assert.Throws<JsParseException>(() =>
            JsCompiler.Compile(
                realm,
                FlatJavaScriptParser.ParseScript(
                    """
                    while (true) { break missing; }
                    """
                )
            )
        );

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("Undefined label 'missing'"));
    }

    [Test]
    public void LabeledContinue_NonIterationLabel_ThrowsSyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var ex = Assert.Throws<JsParseException>(() =>
            JsCompiler.Compile(
                realm,
                FlatJavaScriptParser.ParseScript(
                    """
                    L: { continue L; }
                    """
                )
            )
        );

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("does not denote an iteration statement"));
    }
}
