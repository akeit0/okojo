using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public partial class FunctionParameterBindingTests
{
    [Test]
    public void ParseScript_FunctionParameterBindingKinds_Preserve_Pattern_And_RestPattern()
    {
        var program = JavaScriptParser.ParseScript("""
                                                   function f({ a } = {}, ...[rest]) {}
                                                   """);

        var function = program.Statements[0] as JsFunctionDeclaration;
        Assert.That(function, Is.Not.Null);
        Assert.That(function!.ParameterBindingKinds, Is.EqualTo(new[]
        {
            JsFormalParameterBindingKind.Pattern,
            JsFormalParameterBindingKind.RestPattern
        }));
    }

    [Test]
    public void CompileFunction_Uses_Explicit_RestBindingKind_Without_RestIndex()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var function = new JsFunctionExpression(
            "collect",
            ["rest"],
            new(
            [
                new JsReturnStatement(new JsIdentifierExpression("rest"))
            ], false),
            parameterBindingKinds: [JsFormalParameterBindingKind.Rest],
            restParameterIndex: -1);

        var compiled = compiler.CompileFunction(function);
        JsValue[] args = [JsValue.FromInt32(10), JsValue.FromInt32(20)];
        var result = realm.InvokeFunction(compiled, JsValue.Undefined, args);

        var array = result.AsObject() as JsArray;
        Assert.That(array, Is.Not.Null);
        Assert.That(array!.Length, Is.EqualTo(2u));
        Assert.That(array.TryGetElement(0, out var first), Is.True);
        Assert.That(array.TryGetElement(1, out var second), Is.True);
        Assert.That(first.Int32Value, Is.EqualTo(10));
        Assert.That(second.Int32Value, Is.EqualTo(20));
    }
}
