using System.Text;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ParserDepthGuardTests
{
    private const string FuzzRegressionInput = "v[[k[[[{[[[[[[[[[[[[F=Qe[[[[[ )";

    [Test]
    public void ParseScript_DeeplyNestedArrayLiteral_ThrowsParseException()
    {
        var source = new string('[', 10_000);

        Assert.That(
            () => JavaScriptParser.ParseScript(source),
            Throws.InstanceOf<JsParseException>()
                .With.Message.Contains("Maximum parser recursion depth exceeded"));
    }

    [Test]
    public void ParseScript_DeeplyNestedUnaryExpression_ThrowsParseException()
    {
        var source = new string('!', 10_000) + "0;";

        Assert.That(
            () => JavaScriptParser.ParseScript(source),
            Throws.InstanceOf<JsParseException>()
                .With.Message.Contains("Maximum parser recursion depth exceeded"));
    }

    [Test]
    public void Evaluate_DeeplyNestedArrayLiteral_ThrowsParseException()
    {
        using var runtime = JsRuntime.Create();
        var source = new string('[', 10_000);

        Assert.That(
            () => runtime.MainRealm.Evaluate(source),
            Throws.InstanceOf<JsParseException>()
                .With.Message.Contains("Maximum parser recursion depth exceeded"));
    }

    [Test]
    public void ParseScript_MalformedFuzzRegressionInput_ThrowsParseException()
    {
        Assert.That(
            () => JavaScriptParser.ParseScript(FuzzRegressionInput),
            Throws.InstanceOf<JsParseException>()
                .With.Message.Contains("Unexpected token ')'"));
    }

    [Test]
    public void Evaluate_MalformedFuzzRegressionInput_ThrowsParseException()
    {
        using var runtime = JsRuntime.Create();

        Assert.That(
            () => runtime.MainRealm.Evaluate(FuzzRegressionInput),
            Throws.InstanceOf<JsParseException>()
                .With.Message.Contains("Unexpected token ')'"));
    }

    [Test]
    public void ParseScript_LongAdditiveChain_DoesNotOverflowParser()
    {
        var source = BuildRepeatedBinaryExpression("+", 5_000);

        Assert.That(() => JavaScriptParser.ParseScript(source), Throws.Nothing);
    }

    [Test]
    public void ParseScript_LongExponentiationChain_DoesNotOverflowParser()
    {
        var source = BuildRepeatedBinaryExpression("**", 2_000);

        Assert.That(() => JavaScriptParser.ParseScript(source), Throws.Nothing);
    }

    private static string BuildRepeatedBinaryExpression(string operatorText, int repeatCount)
    {
        var builder = new StringBuilder("0");
        for (var i = 0; i < repeatCount; i++)
            builder.Append(operatorText).Append('0');
        builder.Append(';');
        return builder.ToString();
    }
}
