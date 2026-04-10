namespace Okojo.Parsing;

internal sealed partial class JsParser
{
    private object GetLiteralValue(in JsToken token)
    {
        return token.Kind switch
        {
            JsTokenKind.Number => token.NumberLiteral,
            JsTokenKind.String => lexer.GetStringLiteral(token),
            JsTokenKind.Template => lexer.GetStringLiteral(token),
            JsTokenKind.BigInt => lexer.GetBigIntLiteral(token),
            _ => throw new InvalidOperationException($"Token kind '{token.Kind}' does not have a literal value.")
        };
    }

    private string GetStringLiteralText(in JsToken token)
    {
        return token.Kind switch
        {
            JsTokenKind.String => lexer.GetStringLiteral(token),
            JsTokenKind.Template => lexer.GetStringLiteral(token),
            _ => throw new InvalidOperationException($"Token kind '{token.Kind}' does not have a string literal.")
        };
    }

    private string GetPropertyKeyText(in JsToken token)
    {
        return token.Kind switch
        {
            JsTokenKind.String => GetStringLiteralText(token),
            JsTokenKind.Number => JsValue.NumberToJsString(token.NumberLiteral),
            JsTokenKind.BigInt => lexer.GetBigIntLiteral(token).Value.ToString(),
            JsTokenKind.Identifier or JsTokenKind.PrivateIdentifier => GetIdentifierText(token),
            _ => GetTokenSourceText(token)
        };
    }
}
