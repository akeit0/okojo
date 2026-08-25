using Okojo.JavaScript;
using Okojo.JavaScript.Compiler.Experimental;
using Okojo.JavaScript.Embedding;

namespace Okojo.Compiler.Tests;

public sealed class TempIfNoElseProbe
{
    [Test]
    public void Dump_IfNoElseCrash()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        foreach (var src in new[]
        {
            "2;", "{ 3; }", "if (true) { }",
            "if (true) { 3; }",
            "2; if (true) { 3; }",
            "eval('2;');",
            "eval('if (true) { 3; }');",
            "eval('2; if (true) { 3; }');",
        })
        {
            try
            {
                var r = realm.Evaluate(src);
                TestContext.Progress.WriteLine($"OK [{src}] => {r}");
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine(
                    $"FAIL [{src}] => {ex.GetType().Name}: {ex.Message}"
                );
            }
        }
    }
}
