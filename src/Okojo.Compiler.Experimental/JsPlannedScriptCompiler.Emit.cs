using Okojo.Bytecode;
using Okojo.Parsing;

namespace Okojo.Compiler.Experimental;

internal sealed partial class JsPlannedScriptCompiler
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

        builder.EmitLda(JsOpCode.LdaSmiWide, unchecked((byte)(value & 0xFF)), unchecked((byte)((value >> 8) & 0xFF)));
    }

    private static bool TryGetSmallIntLiteral(JsExpression expression, out int value)
    {
        switch (expression)
        {
            case JsLiteralExpression { Value: int int32 }:
                value = int32;
                return true;
            case JsLiteralExpression { Value: long int64 } when int64 >= sbyte.MinValue && int64 <= sbyte.MaxValue:
                value = (int)int64;
                return true;
            case JsLiteralExpression { Value: double number } when Math.Truncate(number) == number &&
                                                                    number >= sbyte.MinValue &&
                                                                    number <= sbyte.MaxValue:
                value = (int)number;
                return true;
            default:
                value = default;
                return false;
        }
    }

    private void EmitLdar(int register)
    {
        if (register <= byte.MaxValue)
            builder.EmitLda(JsOpCode.Ldar, (byte)register);
        else
            builder.EmitLda(JsOpCode.LdarWide, (byte)(register & 0xFF), (byte)((register >> 8) & 0xFF));
    }

    private void EmitLdaLexicalLocal(int register)
    {
        if (register <= byte.MaxValue)
            builder.EmitLda(JsOpCode.LdaLexicalLocal, (byte)register);
        else
            builder.EmitLda(JsOpCode.LdaLexicalLocalWide, (byte)(register & 0xFF), (byte)((register >> 8) & 0xFF));
    }

    private void EmitStar(int register)
    {
        if (register <= byte.MaxValue)
            builder.Emit(JsOpCode.Star, (byte)register);
        else
            builder.Emit(JsOpCode.StarWide, (byte)(register & 0xFF), (byte)((register >> 8) & 0xFF));
    }

    private void EmitStaLexicalLocal(int register)
    {
        if (register <= byte.MaxValue)
            builder.Emit(JsOpCode.StaLexicalLocal, (byte)register);
        else
            builder.Emit(JsOpCode.StaLexicalLocalWide, (byte)(register & 0xFF), (byte)((register >> 8) & 0xFF));
    }

    private void EmitFunctionContextSetup()
    {
        if (rootContextSlotCount == 0)
            return;
        EmitCreateFunctionContextWithCells(rootContextSlotCount);
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
            builder.Emit(JsOpCode.CreateFunctionContextWithCellsWide,
                (byte)(slotCount & 0xFF),
                (byte)((slotCount >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException("CreateFunctionContextWithCells operands exceed ushort operand capacity.");
    }

    private void EmitLdaCurrentContextSlot(int slot)
    {
        if ((uint)slot <= byte.MaxValue)
        {
            builder.EmitLda(JsOpCode.LdaCurrentContextSlot, (byte)slot);
            return;
        }

        builder.EmitLda(JsOpCode.LdaCurrentContextSlotWide, (byte)(slot & 0xFF), (byte)((slot >> 8) & 0xFF));
    }

    private void EmitStaCurrentContextSlot(int slot)
    {
        if ((uint)slot <= byte.MaxValue)
        {
            builder.Emit(JsOpCode.StaCurrentContextSlot, (byte)slot);
            return;
        }

        builder.Emit(JsOpCode.StaCurrentContextSlotWide, (byte)(slot & 0xFF), (byte)((slot >> 8) & 0xFF));
    }

    private void EmitLdaContextSlot(int slot, int depth)
    {
        if ((uint)depth > byte.MaxValue)
            throw new InvalidOperationException("Context access operands exceed byte operand capacity.");

        if ((uint)slot <= byte.MaxValue)
        {
            builder.EmitLda(JsOpCode.LdaContextSlot, (byte)slot, (byte)depth);
            return;
        }

        builder.EmitLda(JsOpCode.LdaContextSlotWide,
            (byte)(slot & 0xFF),
            (byte)((slot >> 8) & 0xFF),
            (byte)depth);
    }

    private void EmitStaContextSlot(int slot, int depth)
    {
        if ((uint)depth > byte.MaxValue)
            throw new InvalidOperationException("Context access operands exceed byte operand capacity.");

        if ((uint)slot <= byte.MaxValue)
        {
            builder.Emit(JsOpCode.StaContextSlot, (byte)slot, (byte)depth);
            return;
        }

        builder.Emit(JsOpCode.StaContextSlotWide,
            (byte)(slot & 0xFF),
            (byte)((slot >> 8) & 0xFF),
            (byte)depth);
    }

    private void EmitAddRegister(int register)
    {
        EmitRegisterWithSlotOp(JsOpCode.Add, register);
    }

    private void EmitSubRegister(int register)
    {
        EmitRegisterWithSlotOp(JsOpCode.Sub, register);
    }

    private void EmitTestRegister(JsOpCode op, int register)
    {
        EmitRegisterWithSlotOp(op, register);
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

    private void EmitAddSmi(int value)
    {
        if (value is < sbyte.MinValue or > sbyte.MaxValue)
            throw new NotSupportedException("JsPlannedScriptCompiler supports only small AddSmi immediates.");
        builder.Emit(JsOpCode.AddSmi, unchecked((byte)(sbyte)value), 0);
    }

    private void EmitSubSmi(int value)
    {
        if (value is < sbyte.MinValue or > sbyte.MaxValue)
            throw new NotSupportedException("JsPlannedScriptCompiler supports only small SubSmi immediates.");
        builder.Emit(JsOpCode.SubSmi, unchecked((byte)(sbyte)value), 0);
    }

    private void EmitJump(BytecodeBuilder.Label target)
    {
        builder.EmitJump(JsOpCode.Jump, target);
    }

    private void EmitJumpIfToBooleanFalse(BytecodeBuilder.Label target)
    {
        builder.EmitJumpIfFalsy(JsOpCode.JumpIfToBooleanFalse, target);
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
            builder.Emit(JsOpCode.CreateClosureWide,
                (byte)(idx & 0xFF),
                (byte)((idx >> 8) & 0xFF),
                flags);
            return;
        }

        throw new InvalidOperationException("CreateClosure operands exceed ushort operand capacity.");
    }

    private void EmitPopContext()
    {
        builder.Emit(JsOpCode.PopContext);
    }
}
