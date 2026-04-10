using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class TypeofTests
{
    [Test]
    public void Typeof_UndeclaredIdentifier_ReturnsUndefinedString()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   typeof doesNotExist;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("undefined"));
    }

    [Test]
    public void Typeof_GlobalFunction_IsFunction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function f() {}
                                                                   typeof f;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("function"));
    }
}
