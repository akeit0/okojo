using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ParserDepthGuardTests
{
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
}
