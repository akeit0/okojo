using Okojo.Parsing;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private HashSet<int>? TryCollectForInOfPerIterationContextSlots(JsForInOfStatement stmt)
    {
        if (currentContextSlotById.Count == 0)
            return null;
        if (stmt.Left is not JsVariableDeclarationStatement declStmt ||
            declStmt.Kind is not (JsVariableDeclarationKind.Let or JsVariableDeclarationKind.Const))
            return null;

        var slots = Vm.RentCompileHashSet<int>(4);
        foreach (var boundName in GetForInOfHeadBoundIdentifiers(stmt))
        {
            if (!TryResolveForInOfPerIterationBindingSymbolId(stmt, boundName, out var symbolId))
                continue;
            if (!IsCapturedByChildBinding(symbolId))
                continue;
            if (TryGetCurrentContextSlot(symbolId, out _))
                _ = slots.Add(symbolId);
        }

        if (slots.Count == 0)
        {
            Vm.ReturnCompileHashSet(slots);
            return null;
        }

        return slots;
    }

    private bool TryResolveForInOfPerIterationBindingSymbolId(
        JsForInOfStatement stmt,
        BoundIdentifier boundIdentifier,
        out int symbolId)
    {
        if (TryResolveWrappedForInOfBodyBindingSymbolId(stmt, boundIdentifier, out symbolId))
            return true;

        if (TryResolveLocalBinding(
                new CompilerIdentifierName(boundIdentifier.Name, boundIdentifier.NameId),
                out var resolved))
        {
            symbolId = resolved.SymbolId;
            return true;
        }

        symbolId = default;
        return false;
    }

    private bool TryResolveWrappedForInOfBodyBindingSymbolId(
        JsForInOfStatement stmt,
        BoundIdentifier boundIdentifier,
        out int symbolId)
    {
        symbolId = default;

        if (stmt.Left is not JsVariableDeclarationStatement { Declarators.Count: 1 } declStmt)
            return false;
        if (!declStmt.Declarators[0].Name.StartsWith("$forpat_", StringComparison.Ordinal))
            return false;
        if (stmt.Body is not JsBlockStatement bodyBlock)
            return false;
        if (!nestedBlockLexicals.TryGetValue(bodyBlock.Position, out var bindings))
            return false;

        foreach (var binding in bindings)
        {
            if (!binding.Matches(new CompilerIdentifierName(boundIdentifier.Name, boundIdentifier.NameId)))
                continue;

            symbolId = binding.InternalSymbolId;
            return true;
        }

        return false;
    }
}
