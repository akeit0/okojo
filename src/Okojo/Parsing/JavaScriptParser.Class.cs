namespace Okojo.Parsing;

internal sealed partial class JsParser
{
    private IReadOnlyList<JsExpression> ParseDecoratorList()
    {
        var decorators = new List<JsExpression>();
        while (Match(JsTokenKind.At))
            decorators.Add(ParseAssignment(true));
        return decorators;
    }

    private JsClassDeclaration ParseClassDeclaration(IReadOnlyList<JsExpression>? decorators = null,
        int? startOverride = null)
    {
        using var depthCounter = AddDepth();
        MarkNestedFunctionSyntaxSeen();
        var start = startOverride ?? current.Position;
        if (current.Kind != JsTokenKind.ReservedWord || !CurrentSourceTextEquals("class"))
            throw Error("Expected class declaration", current.Position);

        Next();
        var classNameToken = Expect(JsTokenKind.Identifier);
        var className = ParseCheckedIdentifierName(classNameToken);
        var classExpr = ParseClassExpressionCore(className.Name, className.NameId, start, decorators);
        return At(new JsClassDeclaration(className.Name, classExpr, className.NameId), start);
    }

    private JsClassExpression ParseClassExpression(bool allowUnnamed, IReadOnlyList<JsExpression>? decorators = null,
        int? startOverride = null)
    {
        using var depthCounter = AddDepth();
        MarkNestedFunctionSyntaxSeen();
        var start = startOverride ?? current.Position;
        if (current.Kind != JsTokenKind.ReservedWord || !CurrentSourceTextEquals("class"))
            throw Error("Expected class expression", current.Position);

        Next();
        string? name = null;
        var nameId = -1;
        if (current.Kind == JsTokenKind.Identifier)
        {
            var identifier = ParseCheckedIdentifierName(current);
            name = identifier.Name;
            nameId = identifier.NameId;
            Next();
        }
        else if (!allowUnnamed)
        {
            throw Error("Expected class name", current.Position);
        }

        var expr = ParseClassExpressionCore(name, nameId, start, decorators);
        return At(expr, start);
    }
}
