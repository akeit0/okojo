using Okojo.Parsing;
using Okojo.Values;

namespace Okojo.Tests;

public class BigIntParserTests
{
    [TestCase("1n", "1")]
    [TestCase("1_000_000_000_000_000_000n", "1000000000000000000")]
    [TestCase("0b101n", "5")]
    [TestCase("0o77n", "63")]
    [TestCase("0xffn", "255")]
    public void Parser_Accepts_BigInt_Literal_Expressions(string source, string expected)
    {
        var program = JavaScriptParser.ParseScript(source + ";");

        Assert.That(program.Statements, Has.Count.EqualTo(1));
        Assert.That(program.Statements[0], Is.TypeOf<JsExpressionStatement>());

        var expressionStatement = (JsExpressionStatement)program.Statements[0];
        Assert.That(expressionStatement.Expression, Is.TypeOf<JsLiteralExpression>());

        var literal = (JsLiteralExpression)expressionStatement.Expression;
        Assert.That(literal.Text, Is.EqualTo(source));
        Assert.That(literal.Value, Is.TypeOf<JsBigInt>());
        Assert.That(((JsBigInt)literal.Value!).Value.ToString(), Is.EqualTo(expected));
    }

    [TestCase("1.5n;")]
    [TestCase("1e3n;")]
    public void Parser_Rejects_Invalid_BigInt_Literal_Forms(string source)
    {
        Assert.That(() => JavaScriptParser.ParseScript(source), Throws.InstanceOf<JsParseException>());
    }
}
