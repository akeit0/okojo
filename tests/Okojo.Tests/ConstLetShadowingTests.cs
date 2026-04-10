using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ConstLetShadowingTests
{
    [Test]
    public void ForConstHead_DoesNotOverwriteOuterConstBindings()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const x = "outer_x";
                                                                   const y = "outer_y";
                                                                   var i = 0;

                                                                   for (const x = "inner_x"; i < 1; i++) {
                                                                     const y = "inner_y";
                                                                     if (x !== "inner_x") throw new Error("inner-x");
                                                                     if (y !== "inner_y") throw new Error("inner-y");
                                                                   }
                                                                   x === "outer_x" && y === "outer_y";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
