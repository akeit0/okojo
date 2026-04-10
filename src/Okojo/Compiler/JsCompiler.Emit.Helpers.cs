using Okojo.Bytecode;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private void EmitRegisterSlotOp(JsOpCode opcode, int register, int slot = 0)
    {
        if ((uint)register <= byte.MaxValue && (uint)slot <= byte.MaxValue)
        {
            EmitRaw(opcode, register, slot);
            return;
        }

        if ((uint)register <= ushort.MaxValue && (uint)slot <= ushort.MaxValue)
        {
            builder.Emit(JsOpCode.Wide);
            builder.Emit(
                opcode,
                (byte)(register & 0xFF),
                (byte)((register >> 8) & 0xFF),
                (byte)(slot & 0xFF),
                (byte)((slot >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException($"{opcode} operands exceed ushort operand capacity.");
    }

    private void EmitRegisterOp(JsOpCode opcode, int register)
    {
        if ((uint)register <= byte.MaxValue)
        {
            EmitRaw(opcode, register);
            return;
        }

        if ((uint)register <= ushort.MaxValue)
        {
            builder.Emit(JsOpCode.Wide);
            builder.Emit(
                opcode,
                (byte)(register & 0xFF),
                (byte)((register >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException($"{opcode} register operand exceeds ushort operand capacity.");
    }

    private void EmitRegisterRegisterOp(JsOpCode opcode, int register0, int register1)
    {
        if ((uint)register0 <= byte.MaxValue && (uint)register1 <= byte.MaxValue)
        {
            EmitRaw(opcode, register0, register1);
            return;
        }

        if ((uint)register0 <= ushort.MaxValue && (uint)register1 <= ushort.MaxValue)
        {
            builder.Emit(JsOpCode.Wide);
            builder.Emit(
                opcode,
                (byte)(register0 & 0xFF),
                (byte)((register0 >> 8) & 0xFF),
                (byte)(register1 & 0xFF),
                (byte)((register1 >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException($"{opcode} register operands exceed ushort operand capacity.");
    }

    private void EmitImmediateSlotOp(JsOpCode opcode, int immediate, int slot = 0)
    {
        if ((uint)immediate > byte.MaxValue || (uint)slot > byte.MaxValue)
            throw new InvalidOperationException($"{opcode} operands exceed byte operand capacity.");

        EmitRaw(opcode, immediate, slot);
    }

    private void EmitNoOperandOp(JsOpCode opcode)
    {
        EmitRaw(opcode);
    }

    private void EmitInitializeNamedProperty(int objectRegister, int slot)
    {
        if ((uint)objectRegister > ushort.MaxValue)
            throw new InvalidOperationException(
                "InitializeNamedProperty object register exceeds ushort operand capacity.");
        if ((uint)slot > ushort.MaxValue)
            throw new InvalidOperationException("InitializeNamedProperty slot exceeds ushort operand capacity.");

        EmitRaw(
            JsOpCode.InitializeNamedProperty,
            (byte)(objectRegister & 0xFF),
            (byte)((objectRegister >> 8) & 0xFF),
            (byte)(slot & 0xFF),
            (byte)((slot >> 8) & 0xFF));
    }

    private void EmitStarRegister(int register)
    {
        if ((uint)register <= byte.MaxValue)
        {
            builder.Emit(JsOpCode.Star, (byte)register);
            return;
        }

        if ((uint)register <= ushort.MaxValue)
        {
            builder.Emit(JsOpCode.StarWide,
                (byte)(register & 0xFF),
                (byte)((register >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException("Register operand exceeds ushort operand capacity.");
    }

    private void EmitLdaRegister(int register, bool lexical = false)
    {
        if ((uint)register <= byte.MaxValue)
        {
            builder.EmitLda(lexical ? JsOpCode.LdaLexicalLocal : JsOpCode.Ldar, (byte)register);
            return;
        }

        if ((uint)register <= ushort.MaxValue)
        {
            builder.EmitLda(
                lexical ? JsOpCode.LdaLexicalLocalWide : JsOpCode.LdarWide,
                (byte)(register & 0xFF),
                (byte)((register >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException("Register operand exceeds ushort operand capacity.");
    }

    private void EmitMoveRegister(int sourceRegister, int targetRegister)
    {
        if ((uint)sourceRegister <= byte.MaxValue && (uint)targetRegister <= byte.MaxValue)
        {
            EmitRaw(JsOpCode.Mov, (byte)sourceRegister, (byte)targetRegister);
            return;
        }

        if ((uint)sourceRegister <= ushort.MaxValue && (uint)targetRegister <= ushort.MaxValue)
        {
            EmitRaw(JsOpCode.MovWide,
                (byte)(sourceRegister & 0xFF),
                (byte)((sourceRegister >> 8) & 0xFF),
                (byte)(targetRegister & 0xFF),
                (byte)((targetRegister >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException("Register operand exceeds ushort operand capacity.");
    }

    private void EmitTestEqualStrictRegister(int register)
    {
        if ((uint)register <= byte.MaxValue)
        {
            EmitRaw(JsOpCode.TestEqualStrict, register, 0);
            return;
        }

        if ((uint)register <= ushort.MaxValue)
        {
            builder.Emit(JsOpCode.Wide);
            builder.Emit(
                JsOpCode.TestEqualStrict,
                (byte)(register & 0xFF),
                (byte)((register >> 8) & 0xFF),
                0,
                0);
            return;
        }

        throw new InvalidOperationException("TestEqualStrict register exceeds ushort operand capacity.");
    }

    private void EmitToNumeric()
    {
        builder.Emit(JsOpCode.ToNumeric);
    }

    private void EmitToNumber()
    {
        builder.Emit(JsOpCode.ToNumber);
    }

    private void EmitToString()
    {
        builder.Emit(JsOpCode.ToString);
    }

    private void EmitInc()
    {
        builder.Emit(JsOpCode.Inc);
    }

    private void EmitDec()
    {
        builder.Emit(JsOpCode.Dec);
    }

    private void EmitIncOrDec(bool isIncrement)
    {
        builder.Emit(isIncrement ? JsOpCode.Inc : JsOpCode.Dec);
    }

    private void EmitLogicalNot()
    {
        builder.Emit(JsOpCode.LogicalNot);
    }

    private void EmitTypeOf()
    {
        builder.Emit(JsOpCode.TypeOf);
    }

    private void EmitNegate()
    {
        builder.Emit(JsOpCode.Negate);
    }

    private void EmitBitwiseNot()
    {
        builder.Emit(JsOpCode.BitwiseNot);
    }

    private void EmitReturn()
    {
        builder.Emit(JsOpCode.Return);
    }

    private void EmitThrow()
    {
        builder.Emit(JsOpCode.Throw);
    }

    private void EmitDebugger()
    {
        builder.Emit(JsOpCode.Debugger);
    }

    private void EmitPopTry()
    {
        builder.Emit(JsOpCode.PopTry);
    }

    private void EmitPushContextAcc()
    {
        builder.Emit(JsOpCode.PushContextAcc);
    }

    private void EmitPopContext()
    {
        builder.Emit(JsOpCode.PopContext);
    }

    private void EmitCreateMappedArguments()
    {
        builder.Emit(JsOpCode.CreateMappedArguments);
    }

    private void EmitCreateEmptyArrayLiteral(int flags = 0)
    {
        builder.Emit(JsOpCode.CreateEmptyArrayLiteral);
    }

    private void EmitCreateArrayLiteralByIndex(int idx)
    {
        if ((uint)idx > ushort.MaxValue)
            throw new InvalidOperationException("CreateArrayLiteral constant index exceeds ushort operand capacity.");

        builder.Emit(JsOpCode.CreateArrayLiteral, (byte)(idx & 0xFF), (byte)((idx >> 8) & 0xFF));
    }

    private void EmitCreateEmptyObjectLiteral(int flags = 0)
    {
        builder.Emit(JsOpCode.CreateEmptyObjectLiteral);
    }

    private void EmitInitializeArrayElement(int targetRegister, int index)
    {
        if ((uint)targetRegister > ushort.MaxValue || (uint)index > ushort.MaxValue)
            throw new InvalidOperationException("InitializeArrayElement operands exceed ushort operand capacity.");

        builder.Emit(JsOpCode.InitializeArrayElement,
            (byte)targetRegister,
            (byte)(targetRegister >> 8),
            (byte)(index & 0xFF),
            (byte)((index >> 8) & 0xFF));
    }

    private void EmitLdaKeyedProperty(int targetRegister)
    {
        EmitRegisterOp(JsOpCode.LdaKeyedProperty, targetRegister);
    }

    private void EmitDefineOwnKeyedProperty(int targetRegister, int keyRegister)
    {
        EmitRegisterRegisterOp(JsOpCode.DefineOwnKeyedProperty, targetRegister, keyRegister);
    }

    private void EmitStaKeyedProperty(int targetRegister, int keyRegister)
    {
        EmitRegisterRegisterOp(JsOpCode.StaKeyedProperty, targetRegister, keyRegister);
    }

    private void EmitStaModuleVariable(int cellIndex, int depth)
    {
        if ((uint)cellIndex > byte.MaxValue || (uint)depth > byte.MaxValue)
            throw new InvalidOperationException("StaModuleVariable operands exceed byte operand capacity.");

        EmitRaw(JsOpCode.StaModuleVariable, cellIndex, depth);
    }

    private void EmitSwitchOnGeneratorState(int generatorRegister, int tableStart, int tableLength)
    {
        if ((uint)generatorRegister > byte.MaxValue || (uint)tableStart > byte.MaxValue ||
            (uint)tableLength > byte.MaxValue)
            throw new InvalidOperationException("SwitchOnGeneratorState operands exceed byte operand capacity.");

        EmitRaw(JsOpCode.SwitchOnGeneratorState, generatorRegister, tableStart, tableLength);
    }

    private void EmitSuspendGenerator(int generatorRegister, int firstRegister, int liveCount, int suspendId)
    {
        if ((uint)generatorRegister > byte.MaxValue || (uint)firstRegister > byte.MaxValue ||
            (uint)liveCount > byte.MaxValue ||
            (uint)suspendId > byte.MaxValue)
            throw new InvalidOperationException("SuspendGenerator operands exceed byte operand capacity.");

        EmitRaw(JsOpCode.SuspendGenerator, generatorRegister, firstRegister, liveCount, suspendId);
    }

    private void EmitResumeGenerator(int generatorRegister, int firstRegister, int liveCount)
    {
        if ((uint)generatorRegister > byte.MaxValue || (uint)firstRegister > byte.MaxValue ||
            (uint)liveCount > byte.MaxValue)
            throw new InvalidOperationException("ResumeGenerator operands exceed byte operand capacity.");

        EmitRaw(JsOpCode.ResumeGenerator, generatorRegister, firstRegister, liveCount);
    }

    private void EmitStoreLexicalLocal(int register)
    {
        if ((uint)register <= byte.MaxValue)
        {
            builder.Emit(JsOpCode.StaLexicalLocal, (byte)register);
            return;
        }

        if ((uint)register <= ushort.MaxValue)
        {
            builder.Emit(JsOpCode.StaLexicalLocalWide,
                (byte)(register & 0xFF),
                (byte)((register >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException("Register operand exceeds ushort operand capacity.");
    }

    private void EmitPrivateFieldOp(JsOpCode op, int objReg, int valueReg, int brandId, int slotIndex)
    {
        if ((uint)objReg > byte.MaxValue || (uint)valueReg > byte.MaxValue || (uint)slotIndex > ushort.MaxValue ||
            (uint)brandId > ushort.MaxValue)
            throw new NotSupportedException("Private field operands exceeded bytecode capacity.");

        EmitRaw(op, (byte)objReg, (byte)valueReg, (byte)(brandId & 0xFF), (byte)((brandId >> 8) & 0xFF),
            (byte)(slotIndex & 0xFF), (byte)((slotIndex >> 8) & 0xFF));
    }

    private void EmitPrivateFieldOp(JsOpCode op, int objReg, int brandId, int slotIndex)
    {
        if ((uint)objReg > byte.MaxValue || (uint)slotIndex > ushort.MaxValue || (uint)brandId > ushort.MaxValue)
            throw new NotSupportedException("Private field operands exceeded bytecode capacity.");

        EmitRaw(op, (byte)objReg, (byte)(brandId & 0xFF), (byte)((brandId >> 8) & 0xFF),
            (byte)(slotIndex & 0xFF), (byte)((slotIndex >> 8) & 0xFF));
    }

    private void EmitPrivateAccessorInitOp(int objReg, int getterReg, int setterReg, int brandId, int slotIndex)
    {
        if ((uint)objReg > byte.MaxValue || (uint)getterReg > byte.MaxValue || (uint)setterReg > byte.MaxValue ||
            (uint)slotIndex > ushort.MaxValue || (uint)brandId > ushort.MaxValue)
            throw new NotSupportedException("Private accessor operands exceeded bytecode capacity.");

        EmitRaw(JsOpCode.InitPrivateAccessor, (byte)objReg, (byte)getterReg, (byte)setterReg,
            (byte)(brandId & 0xFF), (byte)((brandId >> 8) & 0xFF),
            (byte)(slotIndex & 0xFF), (byte)((slotIndex >> 8) & 0xFF));
    }

    private void EmitPrivateMethodInitOp(int objReg, int methodReg, int brandId, int slotIndex)
    {
        if ((uint)objReg > byte.MaxValue || (uint)methodReg > byte.MaxValue || (uint)slotIndex > ushort.MaxValue ||
            (uint)brandId > ushort.MaxValue)
            throw new NotSupportedException("Private method operands exceeded bytecode capacity.");

        EmitRaw(JsOpCode.InitPrivateMethod, (byte)objReg, (byte)methodReg, (byte)(brandId & 0xFF),
            (byte)((brandId >> 8) & 0xFF), (byte)(slotIndex & 0xFF), (byte)((slotIndex >> 8) & 0xFF));
    }

    private void EmitCreateClosureByIndex(int idx, int feedbackSlot = 0, byte flags = 0)
    {
        if ((uint)idx <= byte.MaxValue)
        {
            EmitRaw(JsOpCode.CreateClosure, (byte)idx, flags);
            return;
        }

        if ((uint)idx <= ushort.MaxValue)
        {
            EmitRaw(JsOpCode.CreateClosureWide,
                (byte)(idx & 0xFF), (byte)((idx >> 8) & 0xFF),
                flags);
            return;
        }

        throw new InvalidOperationException("CreateClosure operands exceed ushort operand capacity.");
    }

    private void EmitCreateFunctionContextWithCells(int slotCount, int feedbackSlot = 0)
    {
        if ((uint)slotCount <= byte.MaxValue)
        {
            EmitRaw(JsOpCode.CreateFunctionContextWithCells, (byte)slotCount);
            return;
        }

        if ((uint)slotCount <= ushort.MaxValue)
        {
            EmitRaw(JsOpCode.CreateFunctionContextWithCellsWide,
                (byte)(slotCount & 0xFF),
                (byte)((slotCount >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException("CreateFunctionContextWithCells operands exceed ushort operand capacity.");
    }

    private void EmitLdaCurrentContextSlot(int slot, bool skipTdz = false)
    {
        if ((uint)slot <= byte.MaxValue)
        {
            builder.EmitLda(skipTdz ? JsOpCode.LdaCurrentContextSlotNoTdz : JsOpCode.LdaCurrentContextSlot, (byte)slot);
            return;
        }

        if ((uint)slot <= ushort.MaxValue)
        {
            EmitRaw(skipTdz ? JsOpCode.LdaCurrentContextSlotNoTdzWide : JsOpCode.LdaCurrentContextSlotWide,
                (byte)(slot & 0xFF),
                (byte)((slot >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException("Current context slot operand exceeds ushort operand capacity.");
    }

    private void EmitStaCurrentContextSlot(int slot)
    {
        if ((uint)slot <= byte.MaxValue)
        {
            EmitRaw(JsOpCode.StaCurrentContextSlot, (byte)slot);
            return;
        }

        if ((uint)slot <= ushort.MaxValue)
        {
            EmitRaw(JsOpCode.StaCurrentContextSlotWide,
                (byte)(slot & 0xFF),
                (byte)((slot >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException("Current context slot operand exceeds ushort operand capacity.");
    }

    private void EmitContextSlotAccess(JsOpCode narrowOp, JsOpCode wideOp, int contextRegister, int slot, int depth)
    {
        if ((uint)depth > byte.MaxValue)
            throw new InvalidOperationException("Context access operands exceed byte operand capacity.");

        if ((uint)slot <= byte.MaxValue)
        {
            EmitRaw(narrowOp, (byte)slot, (byte)depth);
            return;
        }

        if ((uint)slot <= ushort.MaxValue)
        {
            EmitRaw(wideOp,
                (byte)(slot & 0xFF),
                (byte)((slot >> 8) & 0xFF),
                (byte)depth);
            return;
        }

        throw new InvalidOperationException("Context slot operand exceeds ushort operand capacity.");
    }

    private void EmitLdaContextSlot(int contextRegister, int slot, int depth, bool skipTdz = false)
    {
        EmitContextSlotAccess(
            skipTdz ? JsOpCode.LdaContextSlotNoTdz : JsOpCode.LdaContextSlot,
            skipTdz ? JsOpCode.LdaContextSlotNoTdzWide : JsOpCode.LdaContextSlotWide,
            contextRegister,
            slot,
            depth);
    }

    private void EmitStaContextSlot(int contextRegister, int slot, int depth)
    {
        EmitContextSlotAccess(JsOpCode.StaContextSlot, JsOpCode.StaContextSlotWide, contextRegister, slot, depth);
    }

    private void EmitCreateObjectLiteralByIndex(int idx, int feedbackSlot = 0, byte flags = 0)
    {
        if ((uint)idx <= byte.MaxValue)
        {
            EmitRaw(JsOpCode.CreateObjectLiteral, (byte)idx, flags);
            return;
        }

        if ((uint)idx <= ushort.MaxValue)
        {
            EmitRaw(JsOpCode.CreateObjectLiteralWide,
                (byte)(idx & 0xFF), (byte)((idx >> 8) & 0xFF),
                flags);
            return;
        }

        throw new InvalidOperationException("CreateObjectLiteral operands exceed ushort operand capacity.");
    }

    private void EmitLdaGlobalByIndex(int nameIdx, int feedbackSlot)
    {
        EmitGlobalByIndex(JsOpCode.LdaGlobal, JsOpCode.LdaGlobalWide, nameIdx, feedbackSlot);
    }

    private void EmitStaGlobalByIndex(int nameIdx, int feedbackSlot, bool isInitialization)
    {
        EmitGlobalByIndex(
            isInitialization ? JsOpCode.StaGlobalInit : JsOpCode.StaGlobal,
            isInitialization ? JsOpCode.StaGlobalInitWide : JsOpCode.StaGlobalWide,
            nameIdx, feedbackSlot);
    }

    private void EmitStaGlobalFunctionDeclarationByIndex(int nameIdx, int feedbackSlot)
    {
        EmitGlobalByIndex(JsOpCode.StaGlobalFuncDecl, JsOpCode.StaGlobalFuncDeclWide, nameIdx, feedbackSlot);
    }

    private void EmitTypeOfGlobalByIndex(int nameIdx, int feedbackSlot)
    {
        EmitGlobalByIndex(JsOpCode.TypeOfGlobal, JsOpCode.TypeOfGlobalWide, nameIdx, feedbackSlot);
    }

    private void EmitGlobalByIndex(JsOpCode narrowOp, JsOpCode wideOp, int nameIdx, int feedbackSlot)
    {
        if ((uint)nameIdx <= byte.MaxValue && (uint)feedbackSlot <= byte.MaxValue)
        {
            EmitRaw(narrowOp, (byte)nameIdx, (byte)feedbackSlot);
            return;
        }

        if ((uint)nameIdx <= ushort.MaxValue && (uint)feedbackSlot <= ushort.MaxValue)
        {
            EmitRaw(wideOp,
                (byte)(nameIdx & 0xFF), (byte)((nameIdx >> 8) & 0xFF),
                (byte)(feedbackSlot & 0xFF), (byte)((feedbackSlot >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException("Global name/feedback operands exceed ushort operand capacity.");
    }

    private void EmitLdaNamedPropertyByIndex(int objReg, int nameIdx, int feedbackSlot)
    {
        EmitNamedPropertyByIndex(JsOpCode.LdaNamedProperty, JsOpCode.LdaNamedPropertyWide, objReg, nameIdx,
            feedbackSlot);
    }

    private void EmitStaNamedPropertyByIndex(int objReg, int nameIdx, int feedbackSlot)
    {
        EmitNamedPropertyByIndex(JsOpCode.StaNamedProperty, JsOpCode.StaNamedPropertyWide, objReg, nameIdx,
            feedbackSlot);
    }

    private void EmitGetNamedPropertyFromSuperByIndex(int nameIdx, int feedbackSlot)
    {
        if ((uint)nameIdx <= byte.MaxValue && (uint)feedbackSlot <= byte.MaxValue)
        {
            EmitRaw(JsOpCode.GetNamedPropertyFromSuper, (byte)nameIdx);
            return;
        }

        if ((uint)nameIdx <= ushort.MaxValue && (uint)feedbackSlot <= ushort.MaxValue)
        {
            EmitRaw(JsOpCode.GetNamedPropertyFromSuperWide,
                (byte)(nameIdx & 0xFF), (byte)((nameIdx >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException("GetNamedPropertyFromSuper operands exceed ushort operand capacity.");
    }

    private void EmitNamedPropertyByIndex(JsOpCode narrowOp, JsOpCode wideOp, int objReg, int nameIdx,
        int feedbackSlot)
    {
        if ((uint)objReg <= byte.MaxValue && (uint)nameIdx <= byte.MaxValue && (uint)feedbackSlot <= byte.MaxValue)
        {
            EmitRaw(narrowOp, (byte)objReg, (byte)nameIdx, (byte)feedbackSlot);
            return;
        }

        if ((uint)objReg <= ushort.MaxValue && (uint)nameIdx <= ushort.MaxValue &&
            (uint)feedbackSlot <= ushort.MaxValue)
        {
            EmitRaw(wideOp,
                (byte)(objReg & 0xFF),
                (byte)((objReg >> 8) & 0xFF),
                (byte)(nameIdx & 0xFF), (byte)((nameIdx >> 8) & 0xFF),
                (byte)(feedbackSlot & 0xFF), (byte)((feedbackSlot >> 8) & 0xFF));
            return;
        }

        throw new InvalidOperationException("Named property operands exceed ushort operand capacity.");
    }
}
