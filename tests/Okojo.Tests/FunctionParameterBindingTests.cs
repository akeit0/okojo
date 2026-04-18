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
}
