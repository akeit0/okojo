namespace Okojo.JavaScript.Bytecode;

internal static class BytecodeInfo
{
    /// <summary>
    ///     Byte length of the operand region at Single scale. For the nine
    ///     prefix-scalable ops (see <see cref="SupportsOperandScalePrefix"/>) this
    ///     equals the uniform operand COUNT (each operand widens under Wide /
    ///     ExtraWide prefixes). For every other opcode it is simply the fixed
    ///     total operand BYTE size - Wide-suffixed opcodes encode their wide
    ///     layout directly and never take scale prefixes. R6 audit note: these
    ///     two unit families previously shared one misleadingly named function;
    ///     keep new opcodes in the correct family.
    /// </summary>
    public static int GetSingleScaleByteLength(JsOpCode op)
    {
        return op switch
        {
            JsOpCode.LdaUndefined
            or JsOpCode.LdaNull
            or JsOpCode.LdaTheHole
            or JsOpCode.LdaTrue
            or JsOpCode.LdaFalse
            or JsOpCode.LdaZero
            or JsOpCode.PushContextAcc
            or JsOpCode.PopContext
            or JsOpCode.LogicalNot
            or JsOpCode.TypeOf
            or JsOpCode.ToName
            or JsOpCode.ToNumber
            or JsOpCode.ToNumeric
            or JsOpCode.ToString
            or JsOpCode.LdaCurrentFunction
            or JsOpCode.LdaThis
            or JsOpCode.LdaNewTarget
            or JsOpCode.CreateEmptyObjectLiteral
            or JsOpCode.CreateEmptyArrayLiteral
            or JsOpCode.Inc
            or JsOpCode.Dec
            or JsOpCode.CreateMappedArguments
            or JsOpCode.Return
            or JsOpCode.Throw
            or JsOpCode.Debugger
            or JsOpCode.PopTry
            or JsOpCode.Wide
            or JsOpCode.ExtraWide
            or JsOpCode.BitwiseNot
            or JsOpCode.Negate => 0,

            JsOpCode.LdaSmi
            or JsOpCode.LdaNumericConstant
            or JsOpCode.LdaStringConstant
            or JsOpCode.Ldar
            or JsOpCode.LdaLexicalLocal
            or JsOpCode.Star
            or JsOpCode.StaLexicalLocal
            or JsOpCode.PushContext
            or JsOpCode.LdaCurrentContextSlot
            or JsOpCode.StaCurrentContextSlot
            or JsOpCode.LdaCurrentContextSlotNoTdz
            or JsOpCode.CreateBlockContext
            or JsOpCode.ForInEnumerate
            or JsOpCode.ForInNext
            or JsOpCode.ForInStep
            or JsOpCode.CreateRestParameter
            or JsOpCode.LdaKeyedProperty
            or JsOpCode.CreateFunctionContext
            or JsOpCode.CreateFunctionContextWithCells
            or JsOpCode.BitwiseNot
            or JsOpCode.Negate => 1,

            JsOpCode.Mov
            or JsOpCode.LdaGlobal
            or JsOpCode.StaGlobal
            or JsOpCode.StaGlobalInit
            or JsOpCode.StaGlobalFuncDecl
            or JsOpCode.CreateClosure
            or JsOpCode.TypeOfGlobal
            or JsOpCode.GetNamedPropertyFromSuper
            or JsOpCode.LdaTypedConst
            or JsOpCode.LdaModuleVariable
            or JsOpCode.StaModuleVariable
            or JsOpCode.Jump
            or JsOpCode.JumpIfTrue
            or JsOpCode.JumpIfFalse
            or JsOpCode.JumpIfToBooleanTrue
            or JsOpCode.JumpIfToBooleanFalse
            or JsOpCode.JumpIfNull
            or JsOpCode.JumpIfUndefined
            or JsOpCode.JumpIfNotUndefined
            or JsOpCode.JumpIfJsReceiver
            or JsOpCode.PushTry
            or JsOpCode.SwitchOnSmi
            or JsOpCode.LdaNumericConstantWide
            or JsOpCode.LdaSmiWide
            or JsOpCode.LdarWide
            or JsOpCode.LdaLexicalLocalWide
            or JsOpCode.StarWide
            or JsOpCode.StaLexicalLocalWide
            or JsOpCode.LdaCurrentContextSlotWide
            or JsOpCode.StaCurrentContextSlotWide
            or JsOpCode.LdaCurrentContextSlotNoTdzWide
            or JsOpCode.CreateFunctionContextWithCellsWide
            or JsOpCode.CreateObjectLiteral
            or JsOpCode.LdaContextSlot
            or JsOpCode.StaContextSlot
            or JsOpCode.LdaContextSlotNoTdz
            or JsOpCode.StaKeyedProperty
            or JsOpCode.DefineOwnKeyedProperty
            or JsOpCode.DefineOwnKeyedPropertyNoName
            or JsOpCode.Add
            or JsOpCode.Sub
            or JsOpCode.Mul
            or JsOpCode.Div
            or JsOpCode.Mod
            or JsOpCode.Exp
            or JsOpCode.AddSmi
            or JsOpCode.SubSmi
            or JsOpCode.MulSmi
            or JsOpCode.ModSmi
            or JsOpCode.ExpSmi
            or JsOpCode.TestLessThanSmi
            or JsOpCode.TestGreaterThanSmi
            or JsOpCode.TestLessThanOrEqualSmi
            or JsOpCode.TestGreaterThanOrEqualSmi
            or JsOpCode.BitwiseOr
            or JsOpCode.BitwiseXor
            or JsOpCode.BitwiseAnd
            or JsOpCode.ShiftLeft
            or JsOpCode.ShiftRight
            or JsOpCode.ShiftRightLogical
            or JsOpCode.TestEqual
            or JsOpCode.TestNotEqual
            or JsOpCode.TestEqualStrict
            or JsOpCode.TestLessThan
            or JsOpCode.TestGreaterThan
            or JsOpCode.TestLessThanOrEqual
            or JsOpCode.TestGreaterThanOrEqual
            or JsOpCode.TestInstanceOf
            or JsOpCode.TestIn
            or JsOpCode.CreateArrayLiteral
            or JsOpCode.CreateArrayLiteralWithLength => 2,

            JsOpCode.CreateObjectLiteralWide
            or JsOpCode.ResumeGenerator
            or JsOpCode.SwitchOnGeneratorState
            or JsOpCode.LdaContextSlotWide
            or JsOpCode.StaContextSlotWide
            or JsOpCode.LdaContextSlotNoTdzWide
            or JsOpCode.LdaTypedConstWide
            or JsOpCode.JumpLoop
            or JsOpCode.LdaNamedProperty
            or JsOpCode.StaNamedProperty
            or JsOpCode.CallRuntime
            or JsOpCode.InvokeIntrinsic
            or JsOpCode.CreateClosureWide => 3,

            JsOpCode.CallAny or JsOpCode.CallUndefinedReceiver or JsOpCode.Construct => 3,

            JsOpCode.InitializeNamedProperty
            or JsOpCode.InitializeArrayElement
            or JsOpCode.LdaGlobalWide
            or JsOpCode.StaGlobalWide
            or JsOpCode.StaGlobalInitWide
            or JsOpCode.StaGlobalFuncDeclWide
            or JsOpCode.TypeOfGlobalWide
            or JsOpCode.GetNamedPropertyFromSuperWide
            or JsOpCode.MovWide
            or JsOpCode.LdaSmiExtraWide
            or JsOpCode.SuspendGenerator
            or JsOpCode.CallProperty => 4,

            JsOpCode.GetPrivateField => 7,

            JsOpCode.InitPrivateField or JsOpCode.InitPrivateMethod or JsOpCode.SetPrivateField =>
                8,
            JsOpCode.LdaNamedPropertyWide or JsOpCode.StaNamedPropertyWide => 6,

            JsOpCode.InitPrivateAccessor => 9,

            _ => 0,
        };
    }

    public static int GetInstructionLength(JsOpCode op)
    {
        return 1 + GetSingleScaleByteLength(op);
    }

    public static int GetInstructionLength(ReadOnlySpan<byte> code, int pc)
    {
        if (
            !TryDecodeInstructionHeader(
                code,
                pc,
                out _,
                out _,
                out _,
                out _,
                out var instructionLength
            )
        )
            throw new InvalidOperationException($"Truncated instruction stream at pc {pc}.");

        return instructionLength;
    }

    public static bool TryDecodeInstructionHeader(
        ReadOnlySpan<byte> code,
        int pc,
        out JsOpCode op,
        out OperandScale scale,
        out int operandStart,
        out int operandByteCount,
        out int instructionLength
    )
    {
        op = default;
        scale = OperandScale.Single;
        operandStart = 0;
        operandByteCount = 0;
        instructionLength = 0;

        if ((uint)pc >= (uint)code.Length)
            return false;

        var cursor = pc;
        var rawOp = (JsOpCode)code[cursor++];
        if (IsOperandScalePrefix(rawOp))
        {
            scale = GetOperandScale(rawOp);
            if ((uint)cursor >= (uint)code.Length)
                return false;
            rawOp = (JsOpCode)code[cursor++];
        }

        op = rawOp;
        operandStart = cursor;
        operandByteCount = GetOperandByteCount(op, scale);
        instructionLength = cursor - pc + operandByteCount;
        return operandStart + operandByteCount <= code.Length;
    }

    public static bool IsOperandScalePrefix(JsOpCode op)
    {
        return op is JsOpCode.Wide or JsOpCode.ExtraWide;
    }

    public static OperandScale GetOperandScale(JsOpCode prefix)
    {
        return prefix switch
        {
            JsOpCode.Wide => OperandScale.Wide,
            JsOpCode.ExtraWide => OperandScale.ExtraWide,
            _ => OperandScale.Single,
        };
    }

    public static JsOpCode GetOperandScalePrefix(OperandScale scale)
    {
        return scale switch
        {
            OperandScale.Wide => JsOpCode.Wide,
            OperandScale.ExtraWide => JsOpCode.ExtraWide,
            _ => throw new ArgumentOutOfRangeException(nameof(scale)),
        };
    }

    public static bool SupportsOperandScalePrefix(JsOpCode op)
    {
        return op
            is JsOpCode.CallAny
                or JsOpCode.CallUndefinedReceiver
                or JsOpCode.CallProperty
                or JsOpCode.CallRuntime
                or JsOpCode.TestEqualStrict
                or JsOpCode.LdaKeyedProperty
                or JsOpCode.StaKeyedProperty
                or JsOpCode.DefineOwnKeyedProperty
                or JsOpCode.DefineOwnKeyedPropertyNoName
                or JsOpCode.Construct;
    }

    public static int GetOperandByteCount(JsOpCode op, OperandScale scale)
    {
        var operandCount = GetSingleScaleByteLength(op);
        if (scale == OperandScale.Single || !SupportsOperandScalePrefix(op))
            return operandCount;

        return checked(operandCount * (int)scale);
    }

    public static int ReadUnsignedOperand(
        ReadOnlySpan<byte> operands,
        int operandIndex,
        OperandScale scale
    )
    {
        var width = (int)scale;
        var offset = operandIndex * width;
        return scale switch
        {
            OperandScale.Single => operands[offset],
            OperandScale.Wide => operands[offset] | (operands[offset + 1] << 8),
            OperandScale.ExtraWide => operands[offset]
                | (operands[offset + 1] << 8)
                | (operands[offset + 2] << 16)
                | (operands[offset + 3] << 24),
            _ => throw new ArgumentOutOfRangeException(nameof(scale)),
        };
    }

    public static bool IsPureAccumulatorLoad(JsOpCode op)
    {
        return op
            is JsOpCode.LdaUndefined
                or JsOpCode.LdaNull
                or JsOpCode.LdaTheHole
                or JsOpCode.LdaTrue
                or JsOpCode.LdaFalse
                or JsOpCode.LdaZero
                or JsOpCode.LdaSmi
                or JsOpCode.LdaSmiWide
                or JsOpCode.LdaSmiExtraWide
                or JsOpCode.LdaNumericConstant
                or JsOpCode.LdaNumericConstantWide
                or JsOpCode.LdaStringConstant
                or JsOpCode.LdaTypedConst
                or JsOpCode.LdaTypedConstWide
                or JsOpCode.Ldar
                or JsOpCode.LdarWide
                or JsOpCode.LdaCurrentFunction
                // LdaThis is deliberately excluded: it throws for uninitialized
                // derived-constructor this, so it is observable and must not be elided.
                or JsOpCode.LdaNewTarget;
    }

    public static bool OverwritesAccumulatorWithoutReading(JsOpCode op)
    {
        return IsPureAccumulatorLoad(op);
    }

    internal enum OperandScale : byte
    {
        Single = 1,
        Wide = 2,
        ExtraWide = 4,
    }
}
