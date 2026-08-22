using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public partial class FunctionParameterBindingTests
{
    [Test]
    public void ParseScript_FunctionParameterBindingKinds_Preserve_Pattern_And_RestPattern()
    {
        var program = JavaScriptParser.ParseScript(
            """
            function f({ a } = {}, ...[rest]) {}
            """
        );

        var function = program.Statements[0] as JsFunctionDeclaration;
        Assert.That(function, Is.Not.Null);
        Assert.That(
            function!.ParameterBindingKinds,
            Is.EqualTo(
                new[]
                {
                    JsFormalParameterBindingKind.Pattern,
                    JsFormalParameterBindingKind.RestPattern,
                }
            )
        );
    }
}
