namespace Okojo.Parsing;

internal sealed partial class JsParser
{
    private bool CurrentSourceTextEquals(string text)
    {
        return current.SourceLength == text.Length && GetTokenSourceSpan(current).SequenceEqual(text.AsSpan());
    }

    private ReadOnlySpan<char> GetTokenSourceSpan(in JsToken token)
    {
        return source.AsSpan(token.Position, token.SourceLength);
    }

    private string GetTokenSourceText(in JsToken token)
    {
        return source.Substring(token.Position, token.SourceLength);
    }

    private string GetTokenDisplayText(in JsToken token)
    {
        return GetTokenSourceText(token);
    }

    private string GetIdentifierText(in JsToken token)
    {
        return token.DataIndex >= 0 ? lexer.GetIdentifierLiteral(token) : GetTokenSourceText(token);
    }

    private int GetIdentifierId(in JsToken token)
    {
        return token.IdentifierId;
    }

    private int GetOrAddIdentifierId(string text)
    {
        return lexer.AddIdentifierLiteral(text);
    }

    private ParsedIdentifierName ParseCheckedIdentifierName(JsToken token, bool isParamDeclaration = false)
    {
        var name = GetIdentifierText(token);
        var nameId = GetIdentifierId(token);
        EnsureIdentifierAllowedInCurrentMode(name, nameId, token.Position, isParamDeclaration);
        return new(name, nameId, token.Position);
    }

    private ParsedIdentifierName ExpectCheckedIdentifierName(bool isParamDeclaration = false)
    {
        if (!IsBindingIdentifierToken(current.Kind))
            throw Error($"Expected Identifier but found {current.Kind}", current.Position);

        var token = current;
        Next();
        return ParseCheckedIdentifierName(token, isParamDeclaration);
    }

    private static bool IsBindingIdentifierToken(JsTokenKind kind)
    {
        return kind is JsTokenKind.Identifier or JsTokenKind.Of;
    }

    private string ConsumeIdentifierText()
    {
        var name = GetIdentifierText(current);
        Next();
        return name;
    }

    private ParsedPropertyName ParsePropertyName(bool allowPrivateIdentifier, bool deriveComputedLiteralKeyText)
    {
        using var depthCounter = AddDepth();
        if (current.Kind == JsTokenKind.LeftBracket)
        {
            Next();
            var computedKey = ParseAssignment(true);
            Expect(JsTokenKind.RightBracket);
            return new(
                deriveComputedLiteralKeyText ? GetComputedLiteralPropertyKeyText(computedKey) : string.Empty,
                -1,
                computedKey,
                true,
                false,
                false);
        }

        if (allowPrivateIdentifier && current.Kind == JsTokenKind.PrivateIdentifier)
        {
            var identifierId = GetIdentifierId(current);
            var key = GetPrivateIdentifierText(current);
            Next();
            return new(
                key,
                identifierId,
                null,
                false,
                true,
                false);
        }

        if (IsIdentifierNameToken(current.Kind))
        {
            var tokenKind = current.Kind;
            var identifierId = GetIdentifierId(current);
            var key = ConsumeIdentifierText();
            return new(
                key,
                identifierId,
                null,
                false,
                false,
                tokenKind == JsTokenKind.Identifier || (tokenKind == JsTokenKind.Let && !strictMode));
        }

        if (current.Kind is JsTokenKind.String or JsTokenKind.Number or JsTokenKind.BigInt)
        {
            var key = GetPropertyKeyText(current);
            Next();
            return new(
                key,
                -1,
                null,
                false,
                false,
                false);
        }

        throw Error("Expected object property key", current.Position);
    }

    private string? GetComputedLiteralPropertyKeyText(JsExpression computedKey)
    {
        return computedKey switch
        {
            JsLiteralExpression { Value: string s } => s,
            JsLiteralExpression { Value: double d } => JsValue.NumberToJsString(d),
            JsLiteralExpression { Value: JsBigInt bi } => bi.Value.ToString(),
            _ => null
        };
    }

    private JsIdentifierExpression CreateIdentifierExpression(string name)
    {
        return new(name, GetOrAddIdentifierId(name));
    }

    private JsVariableDeclarator CreateVariableDeclarator(string name, JsExpression? initializer)
    {
        return new(name, initializer, GetOrAddIdentifierId(name));
    }

    private JsVariableDeclarator CreateVariableDeclarator(JsIdentifierExpression identifier,
        JsExpression? initializer)
    {
        var nameId = identifier.NameId >= 0 ? identifier.NameId : GetOrAddIdentifierId(identifier.Name);
        return new(identifier.Name, initializer, nameId);
    }

    private static bool TryAddIdentifierKey(HashSet<int>? idSet, HashSet<string>? textSet, int nameId, string name)
    {
        if (nameId >= 0)
            return idSet?.Add(nameId) ?? true;

        return textSet?.Add(name) ?? true;
    }

    private string GetPrivateIdentifierText(in JsToken token)
    {
        return token.DataIndex >= 0 ? "#" + lexer.GetIdentifierLiteral(token) : GetTokenSourceText(token);
    }

    private readonly record struct ParsedIdentifierName(string Name, int NameId, int Position);

    private readonly record struct ParsedPropertyName(
        string? Key,
        int IdentifierId,
        JsExpression? ComputedKey,
        bool IsComputed,
        bool IsPrivate,
        bool ShorthandAllowed);
}
