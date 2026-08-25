using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class TypeofTests
{
    [Test]
    public void Typeof_UndeclaredIdentifier_ReturnsUndefinedString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                typeof doesNotExist;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("undefined"));
    }

    [Test]
    public void Typeof_GlobalFunction_IsFunction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                function f() {}
                typeof f;
                """
            )
        );

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("function"));
    }
}