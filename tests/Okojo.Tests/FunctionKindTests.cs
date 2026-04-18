using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class FunctionKindTests
{
    [Test]
    public void GeneratorDeclaration_IsTaggedAsGeneratorKind()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function* G() {}
                                                                   """));

        var g = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "G");
        Assert.That(g.Kind, Is.EqualTo(JsBytecodeFunctionKind.Generator));
    }

    [Test]
    public void NormalDeclaration_IsTaggedAsNormalKind()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function F() {}
                                                                   """));

        var f = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(fn => fn.Name == "F");
        Assert.That(f.Kind, Is.EqualTo(JsBytecodeFunctionKind.Normal));
    }

    [Test]
    public void GeneratorExpression_IsTaggedAsGeneratorKind()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let x = function* H() {};
                                                                   """));

        var h = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "H");
        Assert.That(h.Kind, Is.EqualTo(JsBytecodeFunctionKind.Generator));
    }
}
