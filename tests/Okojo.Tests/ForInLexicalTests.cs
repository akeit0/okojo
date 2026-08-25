using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class ForInLexicalTests
{
    [Test]
    public void ForIn_LetCapture_UsesFreshBindingPerIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function fn(x) {
                  let a = [];
                  for (let p in x) {
                    a.push(function () { return p; });
                  }
                  let k = 0;
                  for (let q in x) {
                    if (q !== a[k]()) return false;
                    ++k;
                  }
                  return true;
                }
                fn({a : [0], b : 1, c : {v : 1}, get d() {}, set e(x) {}});
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void VarLetIdentifier_InSloppyMode_ParsesAndEvaluatesInObjectShorthand()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                var let = 1;
                var object = {let};
                object.let === 1;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}