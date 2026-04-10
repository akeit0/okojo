using Okojo.Parsing;
using Okojo.Values;

namespace Okojo.Tests;

public class BigIntLexerTests
{
    [TestCase("1n", "1n", "1")]
    [TestCase("1_000n", "1_000n", "1000")]
    [TestCase("0b101n", "0b101n", "5")]
    [TestCase("0b1010_0101n", "0b1010_0101n", "165")]
    [TestCase("0o77n", "0o77n", "63")]
    [TestCase("0o7_7n", "0o7_7n", "63")]
    [TestCase("0xffn", "0xffn", "255")]
    [TestCase("0xff_ffn", "0xff_ffn", "65535")]
    public void Lexer_Produces_BigInt_Token_For_Integer_N_Suffix(string source, string text, string expected)
    {
        var lexer = new JsLexer(source);

        var token = lexer.NextToken();

        Assert.That(token.Kind, Is.EqualTo(JsTokenKind.BigInt));
        Assert.That(source.AsSpan(token.Position, token.SourceLength).ToString(), Is.EqualTo(text));
        var literal = lexer.GetBigIntLiteral(token);
        Assert.That(literal, Is.TypeOf<JsBigInt>());
        Assert.That(literal.Value.ToString(), Is.EqualTo(expected));
        Assert.That(lexer.NextToken().Kind, Is.EqualTo(JsTokenKind.Eof));
    }

    [TestCase("1.5n")]
    [TestCase("1e3n")]
    [TestCase("1__0n")]
    [TestCase("1_n")]
    [TestCase("0x_ffn")]
    public void Lexer_Rejects_Non_Integer_BigInt_Forms(string source)
    {
        var lexer = new JsLexer(source);

        Assert.That(() => lexer.NextToken(), Throws.InstanceOf<JsParseException>());
    }
}
