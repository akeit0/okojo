using Okojo.Bytecode;
using Okojo.Parsing;

namespace Okojo.Compiler.Experimental;

internal sealed partial class JsPlannedScriptCompiler
{
    private void EmitExpression(JsExpression expression)
    {
        switch (expression)
        {
            case JsLiteralExpression literal:
                EmitLiteral(literal);
                return;
            case JsIdentifierExpression identifier:
                EmitIdentifierLoad(identifier.Name);
                return;
            case JsAssignmentExpression { Left: JsIdentifierExpression identifier, Right: var right } assignment:
                EmitIdentifierAssignment(identifier.Name, assignment.Operator, right);
                return;
            case JsBinaryExpression add when TryEmitComparisonExpression(add):
                return;
            case JsBinaryExpression { Operator: JsBinaryOperator.Add } add:
                EmitExpression(add.Left);
                if (TryGetSmallIntLiteral(add.Right, out var rhsSmi))
                {
                    EmitAddSmi(rhsSmi);
                    return;
                }

                var rhsRegister = builder.AllocateTemporaryRegister();
                try
                {
                    EmitExpression(add.Right);
                    EmitStar(rhsRegister);
                    EmitAddRegister(rhsRegister);
                }
                finally
                {
                    builder.ReleaseTemporaryRegister(rhsRegister);
                }

                return;
            default:
                throw new NotSupportedException($"JsPlannedScriptCompiler does not support expression '{expression.GetType().Name}'.");
        }
    }

    private void EmitIdentifierAssignment(string name, JsAssignmentOperator op, JsExpression right)
    {
        if (!TryResolveBindingAccess(name, out var binding, out var contextDepth))
            throw new NotSupportedException($"JsPlannedScriptCompiler does not support assignment to '{name}'.");

        switch (op)
        {
            case JsAssignmentOperator.Assign:
                EmitExpression(right);
                EmitStore(binding, contextDepth);
                return;
            case JsAssignmentOperator.AddAssign:
                EmitIdentifierLoad(name);
                EmitAddRightExpression(right);
                EmitStore(binding, contextDepth);
                return;
            case JsAssignmentOperator.SubtractAssign:
                EmitIdentifierLoad(name);
                EmitSubRightExpression(right);
                EmitStore(binding, contextDepth);
                return;
            default:
                throw new NotSupportedException($"JsPlannedScriptCompiler does not support assignment operator '{op}'.");
        }
    }

    private void EmitAddRightExpression(JsExpression right)
    {
        if (TryGetSmallIntLiteral(right, out var rhsSmi))
        {
            EmitAddSmi(rhsSmi);
            return;
        }

        var rhsRegister = builder.AllocateTemporaryRegister();
        try
        {
            EmitExpression(right);
            EmitStar(rhsRegister);
            EmitAddRegister(rhsRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegister(rhsRegister);
        }
    }

    private void EmitSubRightExpression(JsExpression right)
    {
        if (TryGetSmallIntLiteral(right, out var rhsSmi))
        {
            EmitSubSmi(rhsSmi);
            return;
        }

        var rhsRegister = builder.AllocateTemporaryRegister();
        try
        {
            EmitExpression(right);
            EmitStar(rhsRegister);
            EmitSubRegister(rhsRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegister(rhsRegister);
        }
    }

    private bool TryEmitComparisonExpression(JsBinaryExpression expression)
    {
        if (!TryMapComparisonOpcode(expression.Operator, out var opcode))
            return false;

        var lhsRegister = builder.AllocateTemporaryRegister();
        try
        {
            EmitExpression(expression.Left);
            EmitStar(lhsRegister);
            EmitExpression(expression.Right);
            EmitTestRegister(opcode, lhsRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegister(lhsRegister);
        }

        return true;
    }

    private static bool TryMapComparisonOpcode(JsBinaryOperator op, out JsOpCode opcode)
    {
        opcode = op switch
        {
            JsBinaryOperator.Equal => JsOpCode.TestEqual,
            JsBinaryOperator.StrictEqual => JsOpCode.TestEqualStrict,
            JsBinaryOperator.LessThan => JsOpCode.TestLessThan,
            JsBinaryOperator.GreaterThan => JsOpCode.TestGreaterThan,
            JsBinaryOperator.LessThanOrEqual => JsOpCode.TestLessThanOrEqual,
            JsBinaryOperator.GreaterThanOrEqual => JsOpCode.TestGreaterThanOrEqual,
            _ => default
        };
        return opcode != default;
    }

    private void EmitLiteral(JsLiteralExpression literal)
    {
        switch (literal.Value)
        {
            case null:
                builder.EmitLda(JsOpCode.LdaNull);
                return;
            case bool boolean:
                builder.EmitLda(boolean ? JsOpCode.LdaTrue : JsOpCode.LdaFalse);
                return;
            case int int32:
                EmitSmi(int32);
                return;
            case long int64 when int64 >= int.MinValue && int64 <= int.MaxValue:
                EmitSmi((int)int64);
                return;
            case double number when Math.Truncate(number) == number && number >= int.MinValue && number <= int.MaxValue:
                EmitSmi((int)number);
                return;
            default:
                throw new NotSupportedException($"JsPlannedScriptCompiler does not support literal '{literal.Text}'.");
        }
    }

    private void EmitIdentifierLoad(string name)
    {
        if (!TryResolveBindingAccess(name, out var binding, out var contextDepth))
            throw new NotSupportedException($"JsPlannedScriptCompiler does not support unbound identifier '{name}'.");

        switch (binding.Planned.StorageKind)
        {
            case CompilerPlannedStorageKind.LocalRegister:
                EmitLdar(binding.Register);
                return;
            case CompilerPlannedStorageKind.LexicalRegister:
                EmitLdaLexicalLocal(binding.Register);
                return;
            case CompilerPlannedStorageKind.ContextSlot:
                if (contextDepth == 0)
                    EmitLdaCurrentContextSlot(binding.Planned.StorageIndex);
                else
                    EmitLdaContextSlot(binding.Planned.StorageIndex, contextDepth);
                return;
            default:
                throw new NotSupportedException($"JsPlannedScriptCompiler does not support loading '{name}' from {binding.Planned.StorageKind}.");
        }
    }

    private void EmitStore(RootBindingStorage binding)
    {
        EmitStore(binding, 0);
    }

    private void EmitStore(RootBindingStorage binding, int contextDepth)
    {
        switch (binding.Planned.StorageKind)
        {
            case CompilerPlannedStorageKind.LocalRegister:
                EmitStar(binding.Register);
                return;
            case CompilerPlannedStorageKind.LexicalRegister:
                EmitStaLexicalLocal(binding.Register);
                return;
            case CompilerPlannedStorageKind.ContextSlot:
                if (contextDepth == 0)
                    EmitStaCurrentContextSlot(binding.Planned.StorageIndex);
                else
                    EmitStaContextSlot(binding.Planned.StorageIndex, contextDepth);
                return;
            default:
                throw new NotSupportedException($"JsPlannedScriptCompiler does not support storing '{binding.Planned.Name}' in {binding.Planned.StorageKind}.");
        }
    }
}
