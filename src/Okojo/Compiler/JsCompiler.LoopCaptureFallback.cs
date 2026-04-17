using Okojo.Parsing;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private void MarkForLoopHeadBindingsCapturedByNestedFunctionFallback(JsForStatement statement)
    {
        if (!statement.BodyMayCreateNestedFunction)
            return;
        if (statement.Init is not JsVariableDeclarationStatement initDeclaration ||
            !initDeclaration.Kind.IsLexical())
            return;

        foreach (var declarator in initDeclaration.Declarators)
            if (TryResolveLocalBinding(new CompilerIdentifierName(declarator.Name, declarator.NameId),
                    out var resolved))
            {
                forcedAliasContextSlotSymbolIds.Add(resolved.SymbolId);
                MarkCapturedByChildBinding(resolved.SymbolId);
            }
    }

    private void MarkForInOfHeadBindingsCapturedByNestedFunctionFallback(JsForInOfStatement statement)
    {
        if (!statement.BodyMayCreateNestedFunction)
            return;
        if (statement.Left is not JsVariableDeclarationStatement leftDeclaration ||
            !leftDeclaration.Kind.IsLexical())
            return;

        foreach (var boundIdentifier in GetForInOfHeadBoundIdentifiers(statement))
        {
            if (!TryResolveForInOfPerIterationBindingSymbolId(statement, boundIdentifier, out var symbolId))
                continue;

            forcedAliasContextSlotSymbolIds.Add(symbolId);
            MarkCapturedByChildBinding(symbolId);
        }
    }
}
