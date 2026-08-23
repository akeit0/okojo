using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal abstract partial class JsPlannedCompilerBase
{
    private void EmitSmi(int value)
    {
        if (value == 0)
        {
            builder.EmitLda(JsOpCode.LdaZero);
            return;
        }

        if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
        {
            builder.EmitLda(JsOpCode.LdaSmi, unchecked((byte)(sbyte)value));
            return;
        }

        if (value is >= short.MinValue and <= short.MaxValue)
        {
            builder.EmitLda(
                JsOpCode.LdaSmiWide,
                unchecked((byte)(value & 0xFF)),
                unchecked((byte)((value >> 8) & 0xFF))
            );
            return;
        }

        builder.EmitLda(
            JsOpCode.LdaSmiExtraWide,
            unchecked((byte)(value & 0xFF)),
            unchecked((byte)((value >> 8) & 0xFF)),
            unchecked((byte)((value >> 16) & 0xFF)),
            unchecked((byte)((value >> 24) & 0xFF))
        );
    }

    private void EmitNumericConstant(double value)
    {
        var index = builder.AddNumericConstant(value);
        if ((uint)index <= byte.MaxValue)
        {
            builder.EmitLda(JsOpCode.LdaNumericConstant, (byte)index);
            return;
        }

        if ((uint)index <= ushort.MaxValue)
        {
            builder.EmitLda(
                JsOpCode.LdaNumericConstantWide,
                (byte)(index & 0xFF),
                (byte)((index >> 8) & 0xFF)
            );
            return;
        }

        throw new InvalidOperationException("Numeric constant pool exceeds ushort capacity.");
    }

    private void EmitStringConstant(int index)
    {
        if ((uint)index <= byte.MaxValue)
        {
            builder.EmitLda(JsOpCode.LdaStringConstant, (byte)index);
            return;
        }

        if ((uint)index <= ushort.MaxValue)
        {
            builder.EmitLda(
                JsOpCode.LdaTypedConstWide,
                (byte)Tag.JsTagString,
                (byte)(index & 0xFF),
                (byte)((index >> 8) & 0xFF)
            );
            return;
        }

        throw new InvalidOperationException("String constant pool exceeds ushort capacity.");
    }

    private void EmitTypedConstant(Tag tag, int index)
    {
        if ((uint)index <= byte.MaxValue)
        {
            builder.EmitLda(JsOpCode.LdaTypedConst, (byte)tag, (byte)index);
            return;
        }

        if ((uint)index <= ushort.MaxValue)
        {
            builder.EmitLda(
                JsOpCode.LdaTypedConstWide,
                (byte)tag,
                (byte)(index & 0xFF),
                (byte)((index >> 8) & 0xFF)
            );
            return;
        }

        throw new InvalidOperationException("Typed constant pool exceeds ushort capacity.");
    }

    protected void EmitLdar(int register)
    {
        if (register <= byte.MaxValue)
            builder.EmitLda(JsOpCode.Ldar, (byte)register);
        else
            builder.EmitLda(
                JsOpCode.LdarWide,
                (byte)(register & 0xFF),
                (byte)((register >> 8) & 0xFF)
            );
    }

    private void EmitLdaLexicalLocal(int register)
    {
        if (register <= byte.MaxValue)
            builder.EmitLda(JsOpCode.LdaLexicalLocal, (byte)register);
        else
            builder.EmitLda(
                JsOpCode.LdaLexicalLocalWide,
                (byte)(register & 0xFF),
                (byte)((register >> 8) & 0xFF)
            );
    }

    private void EmitStar(int register)
    {
        if (register <= byte.MaxValue)
            builder.Emit(JsOpCode.Star, (byte)register);
        else
            builder.Emit(
                JsOpCode.StarWide,
                (byte)(register & 0xFF),
                (byte)((register >> 8) & 0xFF)
            );
    }

    private void EmitStaLexicalLocal(int register)
    {
        if (register <= byte.MaxValue)
            builder.Emit(JsOpCode.StaLexicalLocal, (byte)register);
        else
            builder.Emit(
                JsOpCode.StaLexicalLocalWide,
                (byte)(register & 0xFF),
                (byte)((register >> 8) & 0xFF)
            );
    }

    protected void EmitFunctionContextSetup()
    {
        if (rootContextSlotCount == 0)
            return;
        EmitCreateFunctionContextWithCells(rootContextSlotCount);
        EmitRootContextBindings();
    }

    protected void EmitScopeLexicalHoleInitialization()
    {
        var scope = activeScopes.Peek();
        for (var i = 0; i < scope.Bindings.Count; i++)
        {
            var binding = scope.Bindings[i];
            if (
                binding.Planned.Kind
                    is CompilerCollectedBindingKind.Parameter
                        or CompilerCollectedBindingKind.Var
                        or CompilerCollectedBindingKind.FunctionDeclaration
                        or CompilerCollectedBindingKind.FunctionNameSelf
                || binding.Planned.StorageKind
                    is not (
                        CompilerPlannedStorageKind.LexicalRegister
                        or CompilerPlannedStorageKind.ContextSlot
                    )
            )
                continue;
            builder.EmitLda(JsOpCode.LdaTheHole);
            EmitStore(binding, isInitialization: true);
        }
    }

    protected void EmitFunctionSelfBinding()
    {
        var rootScope = activeScopes.Peek();
        for (var i = 0; i < rootScope.Bindings.Count; i++)
        {
            var binding = rootScope.Bindings[i];
            if (binding.Planned.Kind != CompilerCollectedBindingKind.FunctionNameSelf)
                continue;
            builder.EmitLda(JsOpCode.LdaCurrentFunction);
            EmitStore(binding, isInitialization: true);
        }
    }

    private void EmitCreateFunctionContextWithCells(int slotCount)
    {
        if ((uint)slotCount <= byte.MaxValue)
        {
            builder.Emit(JsOpCode.CreateFunctionContextWithCells, (byte)slotCount);
            return;
        }

        if ((uint)slotCount <= ushort.MaxValue)
        {
            builder.Emit(
                JsOpCode.CreateFunctionContextWithCellsWide,
                (byte)(slotCount & 0xFF),
                (byte)((slotCount >> 8) & 0xFF)
            );
            return;
        }

        throw new InvalidOperationException(
            "CreateFunctionContextWithCells operands exceed ushort operand capacity."
        );
    }

    private void EmitLdaCurrentContextSlot(int slot, bool skipTdz = false)
    {
        if ((uint)slot <= byte.MaxValue)
        {
            builder.EmitLda(
                skipTdz ? JsOpCode.LdaCurrentContextSlotNoTdz : JsOpCode.LdaCurrentContextSlot,
                (byte)slot
            );
            return;
        }

        builder.EmitLda(
            skipTdz ? JsOpCode.LdaCurrentContextSlotNoTdzWide : JsOpCode.LdaCurrentContextSlotWide,
            (byte)(slot & 0xFF),
            (byte)((slot >> 8) & 0xFF)
        );
    }

    protected void EmitStaCurrentContextSlot(int slot)
    {
        if ((uint)slot <= byte.MaxValue)
        {
            builder.Emit(JsOpCode.StaCurrentContextSlot, (byte)slot);
            return;
        }

        builder.Emit(
            JsOpCode.StaCurrentContextSlotWide,
            (byte)(slot & 0xFF),
            (byte)((slot >> 8) & 0xFF)
        );
    }

    private void EmitLdaContextSlot(int slot, int depth)
    {
        if ((uint)depth > byte.MaxValue)
            throw new InvalidOperationException(
                "Context access operands exceed byte operand capacity."
            );

        if ((uint)slot <= byte.MaxValue)
        {
            builder.EmitLda(JsOpCode.LdaContextSlot, (byte)slot, (byte)depth);
            return;
        }

        builder.EmitLda(
            JsOpCode.LdaContextSlotWide,
            (byte)(slot & 0xFF),
            (byte)((slot >> 8) & 0xFF),
            (byte)depth
        );
    }

    private void EmitStaContextSlot(int slot, int depth)
    {
        if ((uint)depth > byte.MaxValue)
            throw new InvalidOperationException(
                "Context access operands exceed byte operand capacity."
            );

        if ((uint)slot <= byte.MaxValue)
        {
            builder.Emit(JsOpCode.StaContextSlot, (byte)slot, (byte)depth);
            return;
        }

        builder.Emit(
            JsOpCode.StaContextSlotWide,
            (byte)(slot & 0xFF),
            (byte)((slot >> 8) & 0xFF),
            (byte)depth
        );
    }

    private void EmitRegisterWithSlotOp(JsOpCode op, int register)
    {
        if (register <= byte.MaxValue)
            builder.Emit(op, (byte)register, 0);
        else
        {
            builder.Emit(JsOpCode.Wide);
            builder.Emit(op, (byte)(register & 0xFF), (byte)((register >> 8) & 0xFF), 0, 0);
        }
    }

    private void EmitImmediateWithSlotOp(JsOpCode op, int value)
    {
        if (value is < sbyte.MinValue or > sbyte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));
        builder.Emit(op, unchecked((byte)(sbyte)value), 0);
    }

    private void EmitGlobalAccess(string name, JsOpCode narrow, JsOpCode wide)
    {
        var nameIndex = builder.AddAtomizedStringConstant(name);
        var feedbackSlot = builder.GetOrAllocateGlobalBindingFeedbackSlot(name);
        if ((uint)nameIndex <= byte.MaxValue && (uint)feedbackSlot <= byte.MaxValue)
        {
            builder.Emit(narrow, (byte)nameIndex, (byte)feedbackSlot);
            return;
        }
        if ((uint)nameIndex <= ushort.MaxValue && (uint)feedbackSlot <= ushort.MaxValue)
        {
            builder.Emit(
                wide,
                (byte)(nameIndex & 0xFF),
                (byte)(nameIndex >> 8),
                (byte)(feedbackSlot & 0xFF),
                (byte)(feedbackSlot >> 8)
            );
            return;
        }
        throw new InvalidOperationException("Global operands exceed ushort capacity.");
    }

    private void EmitJump(BytecodeBuilder.Label target)
    {
        builder.EmitJump(JsOpCode.Jump, target);
    }

    private void EmitJumpIfToBooleanFalse(BytecodeBuilder.Label target)
    {
        builder.EmitJumpIfFalsy(JsOpCode.JumpIfToBooleanFalse, target);
    }

    private void EmitJumpIfToBooleanTrue(BytecodeBuilder.Label target)
    {
        builder.EmitJumpIfTruethy(JsOpCode.JumpIfToBooleanTrue, target);
    }

    private void EmitJumpIfNull(BytecodeBuilder.Label target)
    {
        builder.EmitJump(JsOpCode.JumpIfNull, target);
    }

    private void EmitJumpIfUndefined(BytecodeBuilder.Label target)
    {
        builder.EmitJump(JsOpCode.JumpIfUndefined, target);
    }

    private void EmitCreateClosureByIndex(int idx, byte flags = 0)
    {
        if ((uint)idx <= byte.MaxValue)
        {
            builder.Emit(JsOpCode.CreateClosure, (byte)idx, flags);
            return;
        }

        if ((uint)idx <= ushort.MaxValue)
        {
            builder.Emit(
                JsOpCode.CreateClosureWide,
                (byte)(idx & 0xFF),
                (byte)((idx >> 8) & 0xFF),
                flags
            );
            return;
        }

        throw new InvalidOperationException(
            "CreateClosure operands exceed ushort operand capacity."
        );
    }

    private void EmitPopContext()
    {
        builder.Emit(JsOpCode.PopContext);
    }
}
