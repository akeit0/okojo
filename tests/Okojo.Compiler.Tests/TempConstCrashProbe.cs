using Okojo.JavaScript;
using Okojo.JavaScript.Compiler.Experimental;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Parsing;

namespace Okojo.Compiler.Tests;

public sealed class TempConstCrashProbe
{
    [Test]
    public void Dump_ConstSyntaxFile()
    {
        var path = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "test262",
            "test",
            "language",
            "statements",
            "const",
            "syntax",
            "const.js"
        );
        var root = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "test262"
        );
        var harness = File.ReadAllText(
            Path.GetFullPath(Path.Combine(root, "harness", "assert.js"))
        );
        var source = File.ReadAllText(
            Path.GetFullPath(
                Path.Combine(
                    root,
                    "test",
                    "language",
                    "statements",
                    "const",
                    "syntax",
                    "const.js"
                )
            )
        );
        var combined = "'use strict';\n" + harness + "\n" + source;
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        using var ast = FlatJavaScriptParser.ParseScript(combined, "const.js");
        try
        {
            _ = new JsPlannedScriptCompiler(realm).Compile(ast, "const.js");
            TestContext.Progress.WriteLine("COMPILE OK");
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine(
                "CRASH: " + ex.GetType().Name + "\n" + ex.StackTrace
            );
            throw;
        }
    }
}
