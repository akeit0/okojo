using Okojo.Bytecode;
using Okojo.Parsing;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private bool TryEmitSelfBinaryAssignmentFastPath(string resolvedLeftName, string sourceLeftName, JsExpression rhs)
    {
        if (rhs is not JsBinaryExpression bin) return false;
        if (bin.Left is not JsIdentifierExpression leftRef) return false;
        if (!TryGetResolvedAliasSymbolId(CompilerIdentifierName.From(leftRef), out var leftRefSymbolId,
                out var resolvedLeftRefName)) return false;
        if (!string.Equals(resolvedLeftRefName, resolvedLeftName, StringComparison.Ordinal)) return false;
        if (!TryGetResolvedAliasSymbolId(new CompilerIdentifierName(resolvedLeftName), out var lhsSymbolId, out _))
            return false;
        if (!TryGetFastPathLocalRegister(lhsSymbolId, out var lhsReg, out var needsTdzReadCheck)) return false;
        if (needsTdzReadCheck) return false;

        if (TryGetSmiImmediate(bin.Right, out var rhsSmi) && IsSmiSpecializableBinaryOperator(bin.Operator))
        {
            VisitExpression(bin.Left);
            switch (bin.Operator)
            {
                case JsBinaryOperator.Add: EmitImmediateSlotOp(JsOpCode.AddSmi, rhsSmi); break;
                case JsBinaryOperator.Subtract: EmitImmediateSlotOp(JsOpCode.SubSmi, rhsSmi); break;
                case JsBinaryOperator.Multiply: EmitImmediateSlotOp(JsOpCode.MulSmi, rhsSmi); break;
                case JsBinaryOperator.Modulo: EmitImmediateSlotOp(JsOpCode.ModSmi, rhsSmi); break;
                case JsBinaryOperator.Exponentiate: EmitImmediateSlotOp(JsOpCode.ExpSmi, rhsSmi); break;
                case JsBinaryOperator.LessThan: EmitImmediateSlotOp(JsOpCode.TestLessThanSmi, rhsSmi); break;
                case JsBinaryOperator.GreaterThan: EmitImmediateSlotOp(JsOpCode.TestGreaterThanSmi, rhsSmi); break;
                case JsBinaryOperator.LessThanOrEqual:
                    EmitImmediateSlotOp(JsOpCode.TestLessThanOrEqualSmi, rhsSmi); break;
                case JsBinaryOperator.GreaterThanOrEqual:
                    EmitImmediateSlotOp(JsOpCode.TestGreaterThanOrEqualSmi, rhsSmi); break;
                default: return false;
            }

            StoreIdentifier(resolvedLeftName, sourceNameForDebug: sourceLeftName);
            return true;
        }

        if (!TryMapBinaryOperatorToOkojoOpCode(bin.Operator, out var op))
            return false;

        VisitExpression(bin.Right);
        EmitRegisterSlotOp(op, lhsReg);
        StoreIdentifier(resolvedLeftName, sourceNameForDebug: sourceLeftName);
        return true;
    }

    private bool TryEmitCompoundAssignmentFastPath(
        string resolvedLeftName,
        string sourceLeftName,
        JsAssignmentOperator compoundOperator,
        JsExpression rhs)
    {
        if (!TryGetResolvedAliasSymbolId(new CompilerIdentifierName(resolvedLeftName), out var lhsSymbolId, out _) ||
            !TryGetFastPathLocalRegister(lhsSymbolId, out var lhsReg, out var needsTdzReadCheck))
            return false;
        if (needsTdzReadCheck)
            return false;
        if (!TryMapCompoundAssignmentOperatorToOkojoOpCode(compoundOperator, out var op))
            return false;

        VisitExpression(rhs);
        EmitRegisterSlotOp(op, lhsReg);
        StoreIdentifier(resolvedLeftName, sourceNameForDebug: sourceLeftName);
        return true;
    }

    private bool TryEmitLiteralInitializerDirectToLocal(JsVariableDeclarationKind kind, string resolvedLocalName,
        JsExpression initializer)
    {
        if (TryGetModuleVariableBinding(resolvedLocalName, out _))
            return false;
        if (!TryGetResolvedAliasSymbolId(new CompilerIdentifierName(resolvedLocalName), out var targetSymbolId,
                out _) ||
            !TryGetFastPathLocalRegister(targetSymbolId, out var targetReg, out _))
            return false;

        if (initializer is JsArrayExpression arrExpr)
        {
            EmitArrayLiteralIntoRegister(arrExpr, targetReg);
            return true;
        }

        if (initializer is JsObjectExpression objExpr)
        {
            EmitObjectLiteralIntoRegister(objExpr, targetReg);
            return true;
        }

        return false;
    }

    private bool TryEmitDirectLocalToLocalMoveForInitializer(
        string resolvedTargetName,
        JsExpression initializer,
        bool isInitialization,
        string sourceNameForDebug)
    {
        if (TryGetModuleVariableBinding(resolvedTargetName, out _))
            return false;
        if (initializer is not JsIdentifierExpression id)
            return false;

        if (!TryGetResolvedAliasSymbolId(new CompilerIdentifierName(resolvedTargetName), out var targetSymbolId,
                out _) ||
            !TryGetFastPathLocalRegister(targetSymbolId, out var targetReg, out var targetNeedsTdzReadCheck))
            return false;
        if (!TryGetResolvedAliasSymbolId(CompilerIdentifierName.From(id), out var sourceSymbolId, out _) ||
            !TryGetFastPathLocalRegister(sourceSymbolId, out var sourceReg, out var sourceNeedsTdzReadCheck))
            return false;
        if (sourceNeedsTdzReadCheck)
            return false;

        if (!isInitialization && IsConstLocalBinding(targetSymbolId))
        {
            EmitThrowConstAssignErrorRuntime(resolvedTargetName);
            return true;
        }

        if (!isInitialization && targetNeedsTdzReadCheck)
        {
            // Keep TDZ write check semantics for non-init lexical writes.
            EmitMoveRegister(sourceReg, targetReg);
            var writePc = builder.CodeLength;
            EmitStoreLexicalLocal(targetReg);
            builder.AddTdzReadDebugName(writePc, sourceNameForDebug);
            return true;
        }

        EmitMoveRegister(sourceReg, targetReg);
        return true;
    }

    private static bool TryGetSmiImmediate(JsExpression expr, out sbyte value)
    {
        value = 0;
        if (expr is not JsLiteralExpression { Value: double d }) return false;
        if (IsNegativeZero(d)) return false;
        if (d % 1 != 0) return false;
        if (d < sbyte.MinValue || d > sbyte.MaxValue) return false;
        value = (sbyte)d;
        return true;
    }

    private static bool IsSmiSpecializableBinaryOperator(JsBinaryOperator op)
    {
        return op is JsBinaryOperator.Add or JsBinaryOperator.Subtract or JsBinaryOperator.Multiply
            or JsBinaryOperator.Modulo or JsBinaryOperator.Exponentiate or JsBinaryOperator.LessThan
            or JsBinaryOperator.GreaterThan or JsBinaryOperator.LessThanOrEqual or JsBinaryOperator.GreaterThanOrEqual;
    }

    private bool TryGetPlainLocalReadRegister(JsExpression expr, out int reg)
    {
        reg = -1;
        if (expr is not JsIdentifierExpression id) return false;
        var hasResolvedLocalBinding = TryResolveLocalBinding(CompilerIdentifierName.From(id), out var resolvedBinding);

        if (parent is null &&
            (TryGetModuleVariableBinding(id.Name, out _) ||
             (hasResolvedLocalBinding &&
              resolvedBinding.Name is var resolvedForModule &&
              !string.Equals(resolvedForModule, id.Name, StringComparison.Ordinal) &&
              TryGetModuleVariableBinding(resolvedForModule, out _))))
            return false;

        if (!hasResolvedLocalBinding ||
            !TryGetFastPathLocalRegister(resolvedBinding.SymbolId, out reg, out var needsLexicalTdzReadCheck))
            return false;
        if (needsLexicalTdzReadCheck)
            return false;
        return true;
    }

    private bool TryGetContiguousPlainLocalArgumentRegisters(IReadOnlyList<JsExpression> arguments, out int argStart)
    {
        argStart = -1;
        if (arguments.Count == 0)
            return true;
        if (!TryGetPlainLocalReadRegister(arguments[0], out argStart))
            return false;

        for (var i = 1; i < arguments.Count; i++)
            if (!TryGetPlainLocalReadRegister(arguments[i], out var reg) || reg != argStart + i)
                return false;

        return true;
    }
}
