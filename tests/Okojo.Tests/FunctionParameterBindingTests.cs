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
        using var program = FlatJavaScriptParser.ParseScript(
            """
            function f({ a } = {}, ...[rest]) {}
            """
        );

        var statements = program.ChildRange(program[program.Root].Arg0, program[program.Root].Arg1);
        Assert.That(statements.Length, Is.EqualTo(1));
        ref readonly var declaration = ref program[statements[0]];
        Assert.That(declaration.Kind, Is.EqualTo(AstKind.FunctionDeclaration));
        var function = program.GetFunction(declaration.Arg0);
        var parameters = program.GetParameters(function);
        Assert.That(
            parameters.ToArray().Select(parameter => parameter.Kind),
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
