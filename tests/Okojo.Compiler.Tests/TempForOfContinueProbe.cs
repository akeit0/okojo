using Okojo.JavaScript;
using Okojo.JavaScript.Compiler.Experimental;
using Okojo.JavaScript.Embedding;

namespace Okojo.Compiler.Tests;

public sealed class TempForOfContinueProbe
{
    [Test]
    public void Dump_ContinueLabelFromForOf()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var script = new JsPlannedScriptCompiler(realm).Compile(
            """
            globalThis.__n = 0;
            L: do {
              for (var x of [1]) {
                __n += 1;
                continue L;
              }
            } while (false);
            """
        );
        realm.Execute(script);
        Assert.That(realm.Evaluate("__n").Int32Value, Is.EqualTo(1));
    }
}
