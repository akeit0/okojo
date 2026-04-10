using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Parsing;

namespace Okojo.Compiler.Experimental;

internal sealed partial class JsPlannedFunctionCompiler
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
            case JsBinaryExpression binary when TryEmitComparisonExpression(binary):
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
                throw new NotSupportedException($"JsPlannedFunctionCompiler does not support expression '{expression.GetType().Name}'.");
        }
    }

    private void EmitIdentifierAssignment(string name, JsAssignmentOperator op, JsExpression right)
    {
        var hasLocalBinding = TryResolveBindingAccess(name, out var binding, out var contextDepth);
        var hasInheritedCapture = inheritedCaptures.TryGetValue(name, out var inheritedCapture);
        if (!hasLocalBinding && !hasInheritedCapture)
            throw new NotSupportedException($"JsPlannedFunctionCompiler does not support assignment to '{name}'.");

        switch (op)
        {
            case JsAssignmentOperator.Assign:
                EmitExpression(right);
                if (hasLocalBinding)
                    EmitStore(binding, contextDepth);
                else
                    EmitStoreInheritedCapture(inheritedCapture);
                return;
            case JsAssignmentOperator.AddAssign:
                EmitIdentifierLoad(name);
                EmitAddRightExpression(right);
                if (hasLocalBinding)
                    EmitStore(binding, contextDepth);
                else
                    EmitStoreInheritedCapture(inheritedCapture);
                return;
            case JsAssignmentOperator.SubtractAssign:
                EmitIdentifierLoad(name);
                EmitSubRightExpression(right);
                if (hasLocalBinding)
                    EmitStore(binding, contextDepth);
                else
                    EmitStoreInheritedCapture(inheritedCapture);
                return;
            default:
                throw new NotSupportedException($"JsPlannedFunctionCompiler does not support assignment operator '{op}'.");
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
                throw new NotSupportedException($"JsPlannedFunctionCompiler does not support literal '{literal.Text}'.");
        }
    }

    private void EmitIdentifierLoad(string name)
    {
        if (!TryResolveBindingAccess(name, out var binding, out var contextDepth))
        {
            if (inheritedCaptures.TryGetValue(name, out var inherited))
            {
                EmitLdaContextSlot(inherited.Slot, GetInheritedCaptureDepth(inherited));
                return;
            }

            throw new NotSupportedException($"JsPlannedFunctionCompiler does not support unbound identifier '{name}'.");
        }

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
                throw new NotSupportedException($"JsPlannedFunctionCompiler does not support loading '{name}' from {binding.Planned.StorageKind}.");
        }
    }

    private void EmitStore(BindingStorage binding)
    {
        EmitStore(binding, 0);
    }

    private void EmitStore(BindingStorage binding, int contextDepth)
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
                throw new NotSupportedException($"JsPlannedFunctionCompiler does not support storing '{binding.Planned.Name}' in {binding.Planned.StorageKind}.");
        }
    }

    private void EmitStoreInheritedCapture(CapturedBindingAccess access)
    {
        EmitStaContextSlot(access.Slot, GetInheritedCaptureDepth(access));
    }
}
