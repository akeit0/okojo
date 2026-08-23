using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal abstract partial class JsPlannedCompilerBase
{
    private void EmitExpression(FlatAst ast, int nodeIndex)
    {
        ref readonly var node = ref ast[nodeIndex];
        switch (node.Kind)
        {
            case AstKind.NullLiteral:
                builder.EmitLda(JsOpCode.LdaNull);
                return;
            case AstKind.BooleanLiteral:
                builder.EmitLda(node.Arg0 != 0 ? JsOpCode.LdaTrue : JsOpCode.LdaFalse);
                return;
            case AstKind.NumericLiteral:
                EmitNumericLiteral(ast.GetNumber(node.Arg0));
                return;
            case AstKind.StringLiteral:
                EmitStringLiteral(ast.GetString(node.Arg0));
                return;
            case AstKind.Identifier:
                EmitIdentifierLoad(ast.GetString(node.Arg0));
                return;
            case AstKind.AssignmentExpression when ast[node.Arg0].Kind == AstKind.Identifier:
                EmitIdentifierAssignment(
                    ast,
                    ast.GetString(ast[node.Arg0].Arg0),
                    (JsAssignmentOperator)node.Arg2,
                    node.Arg1
                );
                return;
            case AstKind.AssignmentExpression:
                throw new NotSupportedException(
                    $"{CompilerName} supports only identifier assignment targets."
                );
            case AstKind.BinaryExpression:
                EmitBinaryExpression(ast, node);
                return;
            case AstKind.UnaryExpression:
                EmitUnaryExpression(ast, node);
                return;
            case AstKind.UpdateExpression:
                EmitUpdateExpression(ast, node);
                return;
            case AstKind.ConditionalExpression:
                EmitConditionalExpression(ast, node);
                return;
            case AstKind.SequenceExpression:
                EmitSequenceExpression(ast, node);
                return;
            default:
                throw new NotSupportedException(
                    $"{CompilerName} does not support flat expression '{node.Kind}'."
                );
        }
    }

    private void EmitBinaryExpression(FlatAst ast, AstNode node)
    {
        var op = (JsBinaryOperator)node.Arg2;
        if (op is JsBinaryOperator.LogicalAnd or JsBinaryOperator.LogicalOr)
        {
            EmitExpression(ast, node.Arg0);
            var end = builder.CreateLabel();
            if (op == JsBinaryOperator.LogicalAnd)
                EmitJumpIfToBooleanFalse(end);
            else
                EmitJumpIfToBooleanTrue(end);
            EmitExpression(ast, node.Arg1);
            builder.BindLabel(end);
            return;
        }

        if (op == JsBinaryOperator.NullishCoalescing)
        {
            EmitExpression(ast, node.Arg0);
            var evaluateRight = builder.CreateLabel();
            var end = builder.CreateLabel();
            EmitJumpIfNull(evaluateRight);
            EmitJumpIfUndefined(evaluateRight);
            EmitJump(end);
            builder.BindLabel(evaluateRight);
            EmitExpression(ast, node.Arg1);
            builder.BindLabel(end);
            return;
        }

        EmitExpression(ast, node.Arg0);
        if (
            TryGetSmallIntLiteral(ast, node.Arg1, out var rhsSmi)
            && TryMapSmiBinaryOpcode(op, out var smiOpcode)
        )
        {
            EmitImmediateWithSlotOp(smiOpcode, rhsSmi);
            return;
        }

        var lhsRegister = builder.AllocateTemporaryRegister();
        try
        {
            EmitStar(lhsRegister);
            EmitExpression(ast, node.Arg1);
            if (op == JsBinaryOperator.StrictNotEqual)
            {
                EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, lhsRegister);
                builder.Emit(JsOpCode.LogicalNot);
                return;
            }

            if (!TryMapBinaryOpcode(op, out var opcode))
                throw new NotSupportedException(
                    $"{CompilerName} does not support binary operator '{op}'."
                );
            EmitRegisterWithSlotOp(opcode, lhsRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegister(lhsRegister);
        }
    }

    private void EmitUnaryExpression(FlatAst ast, AstNode node)
    {
        var op = (JsUnaryOperator)node.Arg1;
        if (op == JsUnaryOperator.Delete)
            throw new NotSupportedException(
                $"{CompilerName} does not support the delete operator yet."
            );

        EmitExpression(ast, node.Arg0);
        switch (op)
        {
            case JsUnaryOperator.Minus:
                builder.Emit(JsOpCode.ToNumeric);
                builder.Emit(JsOpCode.Negate);
                return;
            case JsUnaryOperator.Plus:
                builder.Emit(JsOpCode.ToNumber);
                return;
            case JsUnaryOperator.LogicalNot:
                builder.Emit(JsOpCode.LogicalNot);
                return;
            case JsUnaryOperator.BitwiseNot:
                builder.Emit(JsOpCode.ToNumeric);
                builder.Emit(JsOpCode.BitwiseNot);
                return;
            case JsUnaryOperator.Typeof:
                builder.Emit(JsOpCode.TypeOf);
                return;
            case JsUnaryOperator.Void:
                builder.EmitLda(JsOpCode.LdaUndefined);
                return;
            default:
                throw new NotSupportedException(
                    $"{CompilerName} does not support unary operator '{op}'."
                );
        }
    }

    private void EmitConditionalExpression(FlatAst ast, AstNode node)
    {
        var alternate = builder.CreateLabel();
        var end = builder.CreateLabel();
        EmitExpression(ast, node.Arg0);
        EmitJumpIfToBooleanFalse(alternate);
        EmitExpression(ast, node.Arg1);
        EmitJump(end);
        builder.BindLabel(alternate);
        EmitExpression(ast, node.Arg2);
        builder.BindLabel(end);
    }

    private void EmitUpdateExpression(FlatAst ast, AstNode node)
    {
        ref readonly var argument = ref ast[node.Arg0];
        if (argument.Kind != AstKind.Identifier)
            throw new NotSupportedException(
                $"{CompilerName} supports only identifier update targets."
            );

        var name = ast.GetString(argument.Arg0);
        var hasLocalBinding = TryResolveBindingAccess(name, out var binding, out var contextDepth);
        var hasExternalBinding = TryResolveExternalBinding(
            name,
            out var externalBinding,
            out var externalDepth
        );
        if (!hasLocalBinding && !hasExternalBinding)
            throw new NotSupportedException($"{CompilerName} does not support update of '{name}'.");

        EmitIdentifierLoad(name);
        var oldValueRegister = node.Arg2 == 0 ? builder.AllocateTemporaryRegister() : -1;
        try
        {
            if (oldValueRegister >= 0)
                EmitStar(oldValueRegister);
            builder.Emit(
                (JsUpdateOperator)node.Arg1 == JsUpdateOperator.Increment
                    ? JsOpCode.Inc
                    : JsOpCode.Dec
            );
            EmitResolvedIdentifierStore(
                hasLocalBinding,
                binding,
                contextDepth,
                externalBinding,
                externalDepth
            );
            if (oldValueRegister >= 0)
                EmitLdar(oldValueRegister);
        }
        finally
        {
            if (oldValueRegister >= 0)
                builder.ReleaseTemporaryRegister(oldValueRegister);
        }
    }

    private void EmitSequenceExpression(FlatAst ast, AstNode node)
    {
        var expressions = ast.ChildRange(node.Arg0, node.Arg1);
        if (expressions.Length == 0)
        {
            builder.EmitLda(JsOpCode.LdaUndefined);
            return;
        }

        for (var i = 0; i < expressions.Length; i++)
            EmitExpression(ast, expressions[i]);
    }

    private void EmitIdentifierAssignment(
        FlatAst ast,
        string name,
        JsAssignmentOperator op,
        int right
    )
    {
        var hasLocalBinding = TryResolveBindingAccess(name, out var binding, out var contextDepth);
        var hasExternalBinding = TryResolveExternalBinding(
            name,
            out var externalBinding,
            out var externalDepth
        );
        if (!hasLocalBinding && !hasExternalBinding)
            throw new NotSupportedException(
                $"{CompilerName} does not support assignment to '{name}'."
            );

        switch (op)
        {
            case JsAssignmentOperator.Assign:
                EmitExpression(ast, right);
                EmitResolvedIdentifierStore(
                    hasLocalBinding,
                    binding,
                    contextDepth,
                    externalBinding,
                    externalDepth
                );
                return;
            case JsAssignmentOperator.AddAssign:
            case JsAssignmentOperator.SubtractAssign:
            case JsAssignmentOperator.MultiplyAssign:
            case JsAssignmentOperator.ExponentiateAssign:
            case JsAssignmentOperator.DivideAssign:
            case JsAssignmentOperator.ModuloAssign:
            case JsAssignmentOperator.ShiftLeftAssign:
            case JsAssignmentOperator.ShiftRightAssign:
            case JsAssignmentOperator.ShiftRightLogicalAssign:
            case JsAssignmentOperator.BitwiseAndAssign:
            case JsAssignmentOperator.BitwiseOrAssign:
            case JsAssignmentOperator.BitwiseXorAssign:
                EmitIdentifierLoad(name);
                EmitCompoundRightExpression(ast, op, right);
                EmitResolvedIdentifierStore(
                    hasLocalBinding,
                    binding,
                    contextDepth,
                    externalBinding,
                    externalDepth
                );
                return;
            case JsAssignmentOperator.LogicalAndAssign:
            case JsAssignmentOperator.LogicalOrAssign:
            case JsAssignmentOperator.NullishCoalescingAssign:
                EmitShortCircuitIdentifierAssignment(
                    ast,
                    name,
                    op,
                    right,
                    hasLocalBinding,
                    binding,
                    contextDepth,
                    externalBinding,
                    externalDepth
                );
                return;
            default:
                throw new NotSupportedException(
                    $"{CompilerName} does not support assignment operator '{op}'."
                );
        }
    }

    private void EmitCompoundRightExpression(FlatAst ast, JsAssignmentOperator op, int right)
    {
        var binaryOp = MapCompoundAssignmentOperator(op);
        if (
            TryGetSmallIntLiteral(ast, right, out var rhsSmi)
            && TryMapSmiBinaryOpcode(binaryOp, out var smiOpcode)
        )
        {
            EmitImmediateWithSlotOp(smiOpcode, rhsSmi);
            return;
        }

        var lhsRegister = builder.AllocateTemporaryRegister();
        try
        {
            EmitStar(lhsRegister);
            EmitExpression(ast, right);
            if (!TryMapBinaryOpcode(binaryOp, out var opcode))
                throw new NotSupportedException(
                    $"{CompilerName} does not support assignment operator '{op}'."
                );
            EmitRegisterWithSlotOp(opcode, lhsRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegister(lhsRegister);
        }
    }

    private void EmitShortCircuitIdentifierAssignment(
        FlatAst ast,
        string name,
        JsAssignmentOperator op,
        int right,
        bool hasLocalBinding,
        BindingStorage binding,
        int contextDepth,
        CapturedBindingAccess externalBinding,
        int externalDepth
    )
    {
        EmitIdentifierLoad(name);
        var assign = builder.CreateLabel();
        var end = builder.CreateLabel();
        switch (op)
        {
            case JsAssignmentOperator.LogicalAndAssign:
                EmitJumpIfToBooleanFalse(end);
                break;
            case JsAssignmentOperator.LogicalOrAssign:
                EmitJumpIfToBooleanTrue(end);
                break;
            case JsAssignmentOperator.NullishCoalescingAssign:
                EmitJumpIfNull(assign);
                EmitJumpIfUndefined(assign);
                EmitJump(end);
                builder.BindLabel(assign);
                break;
        }

        EmitExpression(ast, right);
        EmitResolvedIdentifierStore(
            hasLocalBinding,
            binding,
            contextDepth,
            externalBinding,
            externalDepth
        );
        builder.BindLabel(end);
    }

    private static bool TryMapBinaryOpcode(JsBinaryOperator op, out JsOpCode opcode)
    {
        opcode = op switch
        {
            JsBinaryOperator.Add => JsOpCode.Add,
            JsBinaryOperator.Subtract => JsOpCode.Sub,
            JsBinaryOperator.Multiply => JsOpCode.Mul,
            JsBinaryOperator.Divide => JsOpCode.Div,
            JsBinaryOperator.Modulo => JsOpCode.Mod,
            JsBinaryOperator.Exponentiate => JsOpCode.Exp,
            JsBinaryOperator.BitwiseAnd => JsOpCode.BitwiseAnd,
            JsBinaryOperator.BitwiseOr => JsOpCode.BitwiseOr,
            JsBinaryOperator.BitwiseXor => JsOpCode.BitwiseXor,
            JsBinaryOperator.ShiftLeft => JsOpCode.ShiftLeft,
            JsBinaryOperator.ShiftRight => JsOpCode.ShiftRight,
            JsBinaryOperator.ShiftRightLogical => JsOpCode.ShiftRightLogical,
            JsBinaryOperator.Equal => JsOpCode.TestEqual,
            JsBinaryOperator.NotEqual => JsOpCode.TestNotEqual,
            JsBinaryOperator.StrictEqual => JsOpCode.TestEqualStrict,
            JsBinaryOperator.LessThan => JsOpCode.TestLessThan,
            JsBinaryOperator.GreaterThan => JsOpCode.TestGreaterThan,
            JsBinaryOperator.LessThanOrEqual => JsOpCode.TestLessThanOrEqual,
            JsBinaryOperator.GreaterThanOrEqual => JsOpCode.TestGreaterThanOrEqual,
            JsBinaryOperator.In => JsOpCode.TestIn,
            JsBinaryOperator.Instanceof => JsOpCode.TestInstanceOf,
            _ => default,
        };
        return opcode != default;
    }

    private static bool TryMapSmiBinaryOpcode(JsBinaryOperator op, out JsOpCode opcode)
    {
        opcode = op switch
        {
            JsBinaryOperator.Add => JsOpCode.AddSmi,
            JsBinaryOperator.Subtract => JsOpCode.SubSmi,
            JsBinaryOperator.Multiply => JsOpCode.MulSmi,
            JsBinaryOperator.Modulo => JsOpCode.ModSmi,
            JsBinaryOperator.Exponentiate => JsOpCode.ExpSmi,
            JsBinaryOperator.LessThan => JsOpCode.TestLessThanSmi,
            JsBinaryOperator.GreaterThan => JsOpCode.TestGreaterThanSmi,
            JsBinaryOperator.LessThanOrEqual => JsOpCode.TestLessThanOrEqualSmi,
            JsBinaryOperator.GreaterThanOrEqual => JsOpCode.TestGreaterThanOrEqualSmi,
            _ => default,
        };
        return opcode != default;
    }

    private static JsBinaryOperator MapCompoundAssignmentOperator(JsAssignmentOperator op)
    {
        return op switch
        {
            JsAssignmentOperator.AddAssign => JsBinaryOperator.Add,
            JsAssignmentOperator.SubtractAssign => JsBinaryOperator.Subtract,
            JsAssignmentOperator.MultiplyAssign => JsBinaryOperator.Multiply,
            JsAssignmentOperator.ExponentiateAssign => JsBinaryOperator.Exponentiate,
            JsAssignmentOperator.DivideAssign => JsBinaryOperator.Divide,
            JsAssignmentOperator.ModuloAssign => JsBinaryOperator.Modulo,
            JsAssignmentOperator.ShiftLeftAssign => JsBinaryOperator.ShiftLeft,
            JsAssignmentOperator.ShiftRightAssign => JsBinaryOperator.ShiftRight,
            JsAssignmentOperator.ShiftRightLogicalAssign => JsBinaryOperator.ShiftRightLogical,
            JsAssignmentOperator.BitwiseAndAssign => JsBinaryOperator.BitwiseAnd,
            JsAssignmentOperator.BitwiseOrAssign => JsBinaryOperator.BitwiseOr,
            JsAssignmentOperator.BitwiseXorAssign => JsBinaryOperator.BitwiseXor,
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
        };
    }

    private void EmitNumericLiteral(double number)
    {
        if (Math.Truncate(number) == number && number >= int.MinValue && number <= int.MaxValue)
        {
            EmitSmi((int)number);
            return;
        }

        EmitNumericConstant(number);
    }

    private void EmitStringLiteral(string value)
    {
        EmitStringConstant(builder.AddObjectConstant(value));
    }

    private static bool TryGetSmallIntLiteral(FlatAst ast, int nodeIndex, out int value)
    {
        ref readonly var node = ref ast[nodeIndex];
        if (node.Kind == AstKind.NumericLiteral)
        {
            var number = ast.GetNumber(node.Arg0);
            if (
                Math.Truncate(number) == number
                && number >= sbyte.MinValue
                && number <= sbyte.MaxValue
            )
            {
                value = (int)number;
                return true;
            }
        }

        value = default;
        return false;
    }

    private void EmitIdentifierLoad(string name)
    {
        if (!TryResolveBindingAccess(name, out var binding, out var contextDepth))
        {
            if (TryResolveExternalBinding(name, out var externalBinding, out var externalDepth))
            {
                EmitLdaContextSlot(externalBinding.Slot, externalDepth);
                return;
            }

            throw new NotSupportedException(
                $"{CompilerName} does not support unbound identifier '{name}'."
            );
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
                throw new NotSupportedException(
                    $"{CompilerName} does not support loading '{name}' from {binding.Planned.StorageKind}."
                );
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
                throw new NotSupportedException(
                    $"{CompilerName} does not support storing '{binding.Planned.Name}' in {binding.Planned.StorageKind}."
                );
        }
    }

    private void EmitResolvedIdentifierStore(
        bool hasLocalBinding,
        BindingStorage binding,
        int contextDepth,
        CapturedBindingAccess externalBinding,
        int externalDepth
    )
    {
        if (hasLocalBinding)
        {
            EmitStore(binding, contextDepth);
            return;
        }

        EmitStaContextSlot(externalBinding.Slot, externalDepth);
    }
}
