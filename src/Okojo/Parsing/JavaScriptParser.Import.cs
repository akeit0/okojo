namespace Okojo.Parsing;

internal sealed partial class JsParser
{
    private JsExpression ParseImportPrimaryExpression(int start)
    {
        if (Peek().Kind == JsTokenKind.Dot)
            return ParseImportMetaExpression(start);
        if (Peek().Kind == JsTokenKind.LeftParen)
            return ParseImportCallExpression(start);
        throw Error($"Unexpected token '{GetTokenSourceText(current)}'", current.Position);
    }

    private JsExpression ParseImportMetaExpression(int start)
    {
        Next(); // import
        Expect(JsTokenKind.Dot);
        if (!(current.Kind == JsTokenKind.Identifier && CurrentSourceTextEquals("meta")))
            throw Error($"Expected Identifier but found {current.Kind}", current.Position);

        Next(); // meta
        if (!isModule)
            throw Error("Cannot use import.meta outside a module", start);
        return At<JsExpression>(new JsImportMetaExpression(), start);
    }

    private JsExpression ParseImportCallExpression(int start)
    {
        Next(); // import
        Expect(JsTokenKind.LeftParen);
        var argument = ParseAssignment(true);
        JsExpression? options = null;
        if (Match(JsTokenKind.Comma) && current.Kind != JsTokenKind.RightParen)
        {
            options = ParseAssignment(true);
            _ = Match(JsTokenKind.Comma);
        }

        Expect(JsTokenKind.RightParen);
        return At<JsExpression>(new JsImportCallExpression(argument, options), start);
    }
}
