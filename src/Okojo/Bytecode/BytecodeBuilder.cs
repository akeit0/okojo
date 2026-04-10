using System.Diagnostics;

namespace Okojo.Bytecode;

public sealed class BytecodeBuilder : IDisposable
{
    private readonly List<int> activeTemporaryRegisters;
    private readonly List<int> atomizedStringConstants;
    private readonly List<byte> code;
    private readonly Dictionary<int, int> debugSourceOffsets;
    private readonly List<int> freeTemporaryRegisters;
    private readonly List<int> generatorSwitchTargets;
    private readonly Dictionary<string, int> globalBindingFeedbackSlotByName;
    private readonly List<JumpInfo> jumps16ToPatch;
    private readonly Dictionary<int, int> labelPositions;
    private readonly List<JsLocalDebugInfo> localDebugInfos;
    private readonly List<double> numericConstants;
    private readonly List<object> objectConstants;
    private readonly Dictionary<long, string> privateFieldDebugNames;
    private readonly JsRealm realm;
    private readonly Dictionary<int, string> runtimeCallDebugNames;
    private readonly List<int> switchOnSmiTargets;
    private readonly List<SwitchOnSmiPatchInfo> switchOnSmiToPatch;
    private readonly Dictionary<int, string> tdzReadDebugNames;
    private int globalBindingFeedbackSlotCount;
    private int lastEmittedLength;
    private JsOpCode? lastEmittedOp;
    private byte lastEmittedOp1;
    private byte lastEmittedOp2;
    private int lastEmittedPc = -1;
    private ushort lastEmittedRegisterOperand0;
    private ushort lastEmittedRegisterOperand1;
    private int namedPropertyFeedbackSlotCount;
    private int pendingSourceOffset = -1;
    private bool returnedToPool;
    private string? sourceText;
    private bool strictDeclared;

    public BytecodeBuilder(JsRealm realm)
    {
        this.realm = realm;
        code = realm.RentCompileList<byte>(256);
        numericConstants = realm.RentCompileList<double>(32);
        objectConstants = realm.RentCompileList<object>(64);
        atomizedStringConstants = realm.RentCompileList<int>(64);
        generatorSwitchTargets = realm.RentCompileList<int>(16);
        switchOnSmiTargets = realm.RentCompileList<int>(32);
        jumps16ToPatch = realm.RentCompileList<JumpInfo>(64);
        switchOnSmiToPatch = realm.RentCompileList<SwitchOnSmiPatchInfo>(16);
        labelPositions = realm.RentCompileDictionary<int, int>(64);
        globalBindingFeedbackSlotByName = realm.RentCompileDictionary<string, int>(32);
        runtimeCallDebugNames = realm.RentCompileDictionary<int, string>(16);
        tdzReadDebugNames = realm.RentCompileDictionary<int, string>(32);
        privateFieldDebugNames = realm.RentCompileDictionary<long, string>(8);
        localDebugInfos = realm.RentCompileList<JsLocalDebugInfo>(32);
        debugSourceOffsets = realm.RentCompileDictionary<int, int>(128);
        freeTemporaryRegisters = realm.RentCompileList<int>(32);
        activeTemporaryRegisters = realm.RentCompileList<int>(64);
#if DEBUG
        freeTemporaryRegisterSet = realm.RentCompileHashSet<int>(32);
        reportedSuspiciousAccumulatorCopies = realm.RentCompileHashSet<long>(32);
#endif
    }

    public int RegisterCount { get; private set; }

    public int CodeLength => code.Count;
    public int ObjectConstantCount => objectConstants.Count;

    public int GeneratorSwitchTargetCount => generatorSwitchTargets.Count;
    public int SwitchOnSmiTargetCount => switchOnSmiTargets.Count;

    public void Dispose()
    {
        ReturnPooledCollections();
    }

    public Label CreateLabel()
    {
        return Label.Create();
    }

    public void BindLabel(Label label)
    {
        labelPositions[label.Id] = code.Count;
        InvalidateJumpTestSpecialization();
    }

    public void EmitJump(JsOpCode op, Label target)
    {
        jumps16ToPatch.Add(new(code.Count, target));
        Emit(op, 0, 0); // Placeholder for 16-bit offset
    }

    public void EmitJumpIfTruethy(JsOpCode op, Label target)
    {
        if (lastEmittedOp is { } o && IsTestOpcode(o)) op = JsOpCode.JumpIfTrue;
        jumps16ToPatch.Add(new(code.Count, target));
        Emit(op, 0, 0); // Placeholder for 16-bit offset
    }

    public void EmitJumpIfFalsy(JsOpCode op, Label target)
    {
        if (lastEmittedOp is { } o && IsTestOpcode(o)) op = JsOpCode.JumpIfFalse;
        jumps16ToPatch.Add(new(code.Count, target));
        Emit(op, 0, 0); // Placeholder for 16-bit offset
    }

    public void InvalidateJumpTestSpecialization()
    {
        lastEmittedOp = null;
        lastEmittedLength = 0;
        lastEmittedPc = -1;
        lastEmittedOp1 = 0;
        lastEmittedOp2 = 0;
        lastEmittedRegisterOperand0 = 0;
        lastEmittedRegisterOperand1 = 0;
    }

    private static bool IsTestOpcode(JsOpCode op)
    {
        return op is
            JsOpCode.TestEqual
            or JsOpCode.TestEqualStrict
            or JsOpCode.TestNotEqual
            or JsOpCode.TestLessThan
            or JsOpCode.TestLessThanOrEqual
            or JsOpCode.TestLessThanOrEqualSmi
            or JsOpCode.TestIn
            or JsOpCode.TestGreaterThan
            or JsOpCode.TestGreaterThanSmi
            or JsOpCode.TestGreaterThanOrEqual
            or JsOpCode.TestGreaterThanOrEqualSmi
            or JsOpCode.TestInstanceOf;
    }


    public void EmitJump(Label target)
    {
        EmitJump(JsOpCode.Jump, target);
    }

    public void EmitSwitchOnSmi(IReadOnlyList<Label> targets)
    {
        if (targets.Count > byte.MaxValue)
            throw new InvalidOperationException("SwitchOnSmi target count exceeds byte operand capacity.");
        var copied = new Label[targets.Count];
        for (var i = 0; i < targets.Count; i++)
            copied[i] = targets[i];
        switchOnSmiToPatch.Add(new(code.Count, copied));
        Emit(JsOpCode.SwitchOnSmi, 0, (byte)targets.Count);
    }

    public int AllocateRegister()
    {
        return AllocateTemporaryRegister();
    }

    public int AllocatePinnedRegister()
    {
        return RegisterCount++;
    }

    public int AllocateTemporaryRegister()
    {
        int reg;
        if (freeTemporaryRegisters.Count != 0)
        {
            var idx = freeTemporaryRegisters.Count - 1;
            reg = freeTemporaryRegisters[idx];
            freeTemporaryRegisters.RemoveAt(idx);
#if DEBUG
            freeTemporaryRegisterSet.Remove(reg);
#endif
        }
        else
        {
            reg = RegisterCount++;
        }

        activeTemporaryRegisters.Add(reg);
        return reg;
    }

    public int AllocateTemporaryRegisterBlock(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0)
            return -1;
        if (count == 1)
            return AllocateTemporaryRegister();

        Span<int> regs = stackalloc int[count];
        regs[0] = AllocateTemporaryRegister();
        var contiguous = true;
        for (var i = 1; i < count; i++)
        {
            regs[i] = AllocateTemporaryRegister();
            if (regs[i] != regs[i - 1] + 1)
                contiguous = false;
        }

        if (contiguous)
            return regs[0];

        for (var i = count - 1; i >= 0; i--)
            ReleaseTemporaryRegister(regs[i]);

        var start = RegisterCount;
        for (var i = 0; i < count; i++)
            activeTemporaryRegisters.Add(start + i);
        RegisterCount += count;
        return start;
    }

    public void ReleaseTemporaryRegister(int register)
    {
        if ((uint)register >= (uint)RegisterCount)
            throw new ArgumentOutOfRangeException(nameof(register));
        var activeIndex = FindActiveTemporaryRegisterIndex(register);
        if (activeIndex < 0)
            throw new InvalidOperationException($"Temporary register r{register} is not currently active.");
        activeTemporaryRegisters.RemoveAt(activeIndex);
#if DEBUG
        if (!freeTemporaryRegisterSet.Add(register))
            throw new InvalidOperationException($"Temporary register r{register} released more than once.");
#endif
        freeTemporaryRegisters.Add(register);
    }

    public int GetTemporaryRegisterScopeMarker()
    {
        return activeTemporaryRegisters.Count;
    }

    public bool TryGetActiveTemporaryRegisterRange(out int minRegister, out int maxRegister)
    {
        minRegister = int.MaxValue;
        maxRegister = -1;
        for (var i = 0; i < activeTemporaryRegisters.Count; i++)
        {
            var reg = activeTemporaryRegisters[i];
            if (reg < minRegister)
                minRegister = reg;
            if (reg > maxRegister)
                maxRegister = reg;
        }

        return maxRegister >= 0;
    }

    public void ReleaseTemporaryRegistersToMarker(int marker)
    {
        if ((uint)marker > (uint)activeTemporaryRegisters.Count)
            throw new ArgumentOutOfRangeException(nameof(marker));
        while (activeTemporaryRegisters.Count > marker)
        {
            var idx = activeTemporaryRegisters.Count - 1;
            var reg = activeTemporaryRegisters[idx];
            activeTemporaryRegisters.RemoveAt(idx);
#if DEBUG
            if (!freeTemporaryRegisterSet.Add(reg))
                throw new InvalidOperationException($"Temporary register r{reg} released more than once.");
#endif
            freeTemporaryRegisters.Add(reg);
        }
    }

    public int AllocateFeedbackSlot()
    {
        if (namedPropertyFeedbackSlotCount == ushort.MaxValue)
            throw new InvalidOperationException("Named-property feedback slot count exceeds ushort operand capacity.");
        return namedPropertyFeedbackSlotCount++;
    }

    public int AllocateGlobalBindingFeedbackSlot()
    {
        if (globalBindingFeedbackSlotCount == ushort.MaxValue)
            throw new InvalidOperationException("Global-binding feedback slot count exceeds ushort operand capacity.");
        return globalBindingFeedbackSlotCount++;
    }

    public int GetOrAllocateGlobalBindingFeedbackSlot(string name)
    {
        if (globalBindingFeedbackSlotByName.TryGetValue(name, out var existing))
            return existing;

        var slot = AllocateGlobalBindingFeedbackSlot();
        globalBindingFeedbackSlotByName[name] = slot;
        return slot;
    }

    public int AddNumericConstant(double value)
    {
        for (var i = 0; i < numericConstants.Count; i++)
            if (numericConstants[i] == value)
                return i;

        numericConstants.Add(value);
        return numericConstants.Count - 1;
    }

    public int AddObjectConstant(object value)
    {
        for (var i = 0; i < objectConstants.Count; i++)
            if (ReferenceEquals(objectConstants[i], value))
                return i;

        objectConstants.Add(value);
        return objectConstants.Count - 1;
    }

    public int AddAtomizedStringConstant(string value)
    {
        if (TryGetArrayIndexFromCanonicalString(value, out _))
            throw new InvalidOperationException(
                $"Atomized string constant cannot be a canonical array index: '{value}'.");

        var atom = realm.Atoms.InternNoCheck(value);
        for (var i = 0; i < atomizedStringConstants.Count; i++)
            if (atomizedStringConstants[i] == atom)
                return i;

        atomizedStringConstants.Add(atom);
        return atomizedStringConstants.Count - 1;
    }

    public int AddGeneratorSwitchTarget(int targetPc)
    {
        generatorSwitchTargets.Add(targetPc);
        return generatorSwitchTargets.Count - 1;
    }

    public void PatchByte(int codeIndex, byte value)
    {
        if ((uint)codeIndex >= (uint)code.Count)
            throw new ArgumentOutOfRangeException(nameof(codeIndex));
        code[codeIndex] = value;
    }

    public void Emit(JsOpCode op)
    {
        EmitCore(op);
    }

    public void EmitLda(JsOpCode op)
    {
        EmitLdaCore(op);
    }

    public void Emit(JsOpCode op, byte operand)
    {
        EmitCore(op, operand);
    }

    public void EmitLda(JsOpCode op, byte operand)
    {
        EmitLdaCore(op, operand);
    }

    public void Emit(JsOpCode op, byte op1, byte op2)
    {
        EmitCore(op, op1, op2);
    }

    public void EmitLda(JsOpCode op, byte op1, byte op2)
    {
        EmitLdaCore(op, op1, op2);
    }

    public void Emit(JsOpCode op, byte op1, byte op2, byte op3)
    {
        EmitCore(op, op1, op2, op3);
    }

    public void EmitLda(JsOpCode op, byte op1, byte op2, byte op3)
    {
        EmitLdaCore(op, op1, op2, op3);
    }

    public void Emit(JsOpCode op, byte op1, byte op2, byte op3, byte op4)
    {
        EmitCore(op, op1, op2, op3, op4);
    }

    public void EmitLda(JsOpCode op, byte op1, byte op2, byte op3, byte op4)
    {
        EmitLdaCore(op, op1, op2, op3, op4);
    }

    public void Emit(JsOpCode op, byte op1, byte op2, byte op3, byte op4, byte op5)
    {
        EmitCore(op, op1, op2, op3, op4, op5);
    }

    public void EmitLda(JsOpCode op, byte op1, byte op2, byte op3, byte op4, byte op5)
    {
        EmitLdaCore(op, op1, op2, op3, op4, op5);
    }

    public void Emit(JsOpCode op, byte op1, byte op2, byte op3, byte op4, byte op5, byte op6)
    {
        EmitCore(op, op1, op2, op3, op4, op5, op6);
    }

    public void EmitLda(JsOpCode op, byte op1, byte op2, byte op3, byte op4, byte op5, byte op6)
    {
        EmitLdaCore(op, op1, op2, op3, op4, op5, op6);
    }

    public void Emit(JsOpCode op, byte op1, byte op2, byte op3, byte op4, byte op5, byte op6, byte op7)
    {
        EmitCore(op, op1, op2, op3, op4, op5, op6, op7);
    }

    internal void Emit(JsOpCode op, ReadOnlySpan<byte> operands)
    {
        EmitCore(op, operands);
    }

    public void EmitLda(JsOpCode op, byte op1, byte op2, byte op3, byte op4, byte op5, byte op6, byte op7)
    {
        EmitLdaCore(op, op1, op2, op3, op4, op5, op6, op7);
    }

    private void EmitCore(JsOpCode op, params ReadOnlySpan<byte> operands)
    {
#if DEBUG
        ValidateEmit(op, operands);
#endif
        EmitUncheckedCore(op, operands);
    }

    private void EmitLdaCore(JsOpCode op, params ReadOnlySpan<byte> operands)
    {
        if (TryOmitRedundantAccumulatorLoad(op, operands))
            return;

#if DEBUG
        ValidateEmit(op, operands);
#endif
        TryReplacePreviousPureAccumulatorLoad(op, operands);
        EmitUncheckedCore(op, operands);
    }

    private void EmitUncheckedCore(JsOpCode op, ReadOnlySpan<byte> operands)
    {
        var instructionPc = code.Count;
        code.Add((byte)op);
        for (var i = 0; i < operands.Length; i++)
            code.Add(operands[i]);
        if (pendingSourceOffset >= 0)
        {
            debugSourceOffsets[instructionPc] = pendingSourceOffset;
            pendingSourceOffset = -1;
        }

        RememberLastEmit(op, operands);
    }

    private bool TryOmitRedundantAccumulatorLoad(JsOpCode op, ReadOnlySpan<byte> operands)
    {
        return (op == JsOpCode.Ldar || op == JsOpCode.LdarWide) &&
               TryDecodeRegisterOperands(op, operands, out var loadReg0, out _) &&
               (lastEmittedOp == JsOpCode.Star || lastEmittedOp == JsOpCode.StarWide) &&
               lastEmittedRegisterOperand0 == loadReg0 &&
               !IsPositionAnchored(code.Count);
    }

    private void TryReplacePreviousPureAccumulatorLoad(JsOpCode op, ReadOnlySpan<byte> operands)
    {
        if (!BytecodeInfo.IsPureAccumulatorLoad(op))
            return;
        if (lastEmittedOp is null || !BytecodeInfo.IsPureAccumulatorLoad(lastEmittedOp.Value))
            return;
        if (lastEmittedPc < 0 || lastEmittedLength <= 0)
            return;
        if (IsPositionAnchored(lastEmittedPc) || IsPositionAnchored(code.Count))
            return;

        code.RemoveRange(lastEmittedPc, lastEmittedLength);
    }

    private bool IsPositionAnchored(int pc)
    {
        if (debugSourceOffsets.ContainsKey(pc) || runtimeCallDebugNames.ContainsKey(pc) ||
            tdzReadDebugNames.ContainsKey(pc))
            return true;

        foreach (var boundPc in labelPositions.Values)
            if (boundPc == pc)
                return true;

        return false;
    }

    public void AddRuntimeCallDebugName(int instructionPc, string name)
    {
        runtimeCallDebugNames[instructionPc] = name;
    }

    public void AddTdzReadDebugName(int instructionPc, string name)
    {
        tdzReadDebugNames[instructionPc] = name;
    }

    public void AddPrivateFieldDebugName(long key, string name)
    {
        privateFieldDebugNames[key] = name;
    }

    public void AddLocalDebugInfo(JsLocalDebugInfo info)
    {
        localDebugInfos.Add(info);
    }

    public void SetSourceText(string? sourceText)
    {
        this.sourceText = sourceText;
    }

    public void SetStrictDeclared(bool strictDeclared)
    {
        this.strictDeclared = strictDeclared;
    }

    public void SetPendingSourceOffset(int sourceOffset)
    {
        pendingSourceOffset = sourceOffset;
    }

    public void ClearPendingSourceOffset()
    {
        pendingSourceOffset = -1;
    }

    public void AddDebugSourceOffset(int instructionPc, int sourceOffset)
    {
        debugSourceOffsets[instructionPc] = sourceOffset;
    }

    public JsScript ToScript()
    {
        foreach (var jump in jumps16ToPatch)
            if (labelPositions.TryGetValue(jump.Target.Id, out var targetPos))
            {
                var offset = targetPos - (jump.InstructionPos + 3); // +3 for op and 2-byte operand
                if (offset < short.MinValue || offset > short.MaxValue)
                    throw new InvalidOperationException("Jump16 offset out of range for 16-bit operand.");

                var s = (short)offset;
                code[jump.InstructionPos + 1] = (byte)(s & 0xFF);
                code[jump.InstructionPos + 2] = (byte)((s >> 8) & 0xFF);
            }
            else
            {
                throw new InvalidOperationException("Label not bound.");
            }

        foreach (var switchPatch in switchOnSmiToPatch)
        {
            var tableStart = switchOnSmiTargets.Count;
            if (tableStart > byte.MaxValue)
                throw new InvalidOperationException("SwitchOnSmi table start exceeds byte operand capacity.");

            code[switchPatch.InstructionPos + 1] = (byte)tableStart;
            for (var i = 0; i < switchPatch.Targets.Length; i++)
            {
                if (!labelPositions.TryGetValue(switchPatch.Targets[i].Id, out var targetPos))
                    throw new InvalidOperationException("SwitchOnSmi label not bound.");
                switchOnSmiTargets.Add(targetPos);
            }
        }

        int[]? debugPcOffsets = null;
        int[]? debugSourceOffsets = null;
        if (this.debugSourceOffsets.Count != 0)
            CopySortedIntMap(this.debugSourceOffsets, out debugPcOffsets, out debugSourceOffsets);

        string[]? debugNames = null;
        int[]? runtimeCallDebugPcs = null;
        int[]? runtimeCallDebugNameIndices = null;
        int[]? tdzReadDebugPcs = null;
        int[]? tdzReadDebugNameIndices = null;
        long[]? privateFieldDebugKeys = null;
        int[]? privateFieldDebugNameIndices = null;
        if (runtimeCallDebugNames.Count != 0 || tdzReadDebugNames.Count != 0 || privateFieldDebugNames.Count != 0)
        {
            var nameIndexByText = new Dictionary<string, int>(StringComparer.Ordinal);
            var names = new List<string>();

            static int InternName(Dictionary<string, int> map, List<string> namesList, string name)
            {
                if (map.TryGetValue(name, out var existing))
                    return existing;
                var index = namesList.Count;
                namesList.Add(name);
                map.Add(name, index);
                return index;
            }

            if (runtimeCallDebugNames.Count != 0)
                BuildSortedDebugNameTable(runtimeCallDebugNames, nameIndexByText, names,
                    out runtimeCallDebugPcs, out runtimeCallDebugNameIndices, static (value, map, list) =>
                        InternName(map, list, value));

            if (tdzReadDebugNames.Count != 0)
                BuildSortedDebugNameTable(tdzReadDebugNames, nameIndexByText, names,
                    out tdzReadDebugPcs, out tdzReadDebugNameIndices, static (value, map, list) =>
                        InternName(map, list, value));

            if (privateFieldDebugNames.Count != 0)
                BuildSortedDebugNameTable(privateFieldDebugNames, nameIndexByText, names,
                    out privateFieldDebugKeys, out privateFieldDebugNameIndices, static (value, map, list) =>
                        InternName(map, list, value));

            debugNames = names.ToArray();
        }

        GlobalBindingIcEntry[]? globalBindingIcEntries = null;
        if (globalBindingFeedbackSlotCount != 0)
            globalBindingIcEntries = new GlobalBindingIcEntry[globalBindingFeedbackSlotCount];

        return new(
            code.ToArray(),
            numericConstants.ToArray(),
            objectConstants.ToArray(),
            RegisterCount,
            atomizedStringConstants.ToArray(),
            strictDeclared,
            debugNames,
            runtimeCallDebugPcs,
            runtimeCallDebugNameIndices,
            tdzReadDebugPcs,
            tdzReadDebugNameIndices,
            namedPropertyFeedbackSlotCount == 0 ? null : new OkojoNamedPropertyIcEntry[namedPropertyFeedbackSlotCount],
            globalBindingIcEntries,
            debugPcOffsets,
            debugSourceOffsets,
            sourceText,
            FunctionSourceText: null,
            GeneratorSwitchTargets: generatorSwitchTargets.Count == 0 ? null : generatorSwitchTargets.ToArray(),
            SwitchOnSmiTargets: switchOnSmiTargets.Count == 0 ? null : switchOnSmiTargets.ToArray(),
            PrivateFieldDebugKeys: privateFieldDebugKeys,
            PrivateFieldDebugNameIndices: privateFieldDebugNameIndices,
            LocalDebugInfos: localDebugInfos.Count == 0 ? null : localDebugInfos.ToArray()
        );
    }

    private void ReturnPooledCollections()
    {
        if (returnedToPool)
            return;

        returnedToPool = true;
        realm.ReturnCompileList(code);
        realm.ReturnCompileList(numericConstants);
        realm.ReturnCompileList(objectConstants);
        realm.ReturnCompileList(atomizedStringConstants);
        realm.ReturnCompileList(generatorSwitchTargets);
        realm.ReturnCompileList(switchOnSmiTargets);
        realm.ReturnCompileList(jumps16ToPatch);
        realm.ReturnCompileList(switchOnSmiToPatch);
        realm.ReturnCompileDictionary(labelPositions);
        realm.ReturnCompileDictionary(globalBindingFeedbackSlotByName);
        realm.ReturnCompileDictionary(runtimeCallDebugNames);
        realm.ReturnCompileDictionary(tdzReadDebugNames);
        realm.ReturnCompileDictionary(privateFieldDebugNames);
        realm.ReturnCompileList(localDebugInfos);
        realm.ReturnCompileDictionary(debugSourceOffsets);
        realm.ReturnCompileList(freeTemporaryRegisters);
        realm.ReturnCompileList(activeTemporaryRegisters);
#if DEBUG
        realm.ReturnCompileHashSet(freeTemporaryRegisterSet);
        realm.ReturnCompileHashSet(reportedSuspiciousAccumulatorCopies);
#endif
    }

    private int FindActiveTemporaryRegisterIndex(int register)
    {
        for (var i = activeTemporaryRegisters.Count - 1; i >= 0; i--)
            if (activeTemporaryRegisters[i] == register)
                return i;

        return -1;
    }

    private static void CopySortedIntMap(Dictionary<int, int> source, out int[] keys, out int[] values)
    {
        keys = new int[source.Count];
        values = new int[source.Count];
        var cursor = 0;
        foreach (var key in source.Keys)
            keys[cursor++] = key;

        Array.Sort(keys);
        for (var i = 0; i < keys.Length; i++)
            values[i] = source[keys[i]];
    }

    private void OptimizeBytecode()
    {
        if (code.Count < 2)
            return;

        var instructions = DecodeInstructions();
        if (instructions.Count < 2)
            return;

        var protectedTargets = CollectProtectedInstructionTargets(instructions);
        var kept = ElideDeadPureAccumulatorLoads(instructions, protectedTargets);
        if (kept.Count == instructions.Count)
            return;

        RewriteBytecode(kept);
    }

    private List<DecodedInstruction> DecodeInstructions()
    {
        var decoded = new List<DecodedInstruction>(Math.Max(4, code.Count / 2));
        var pc = 0;
        while (pc < code.Count)
        {
            var oldPc = pc;
            if (!BytecodeInfo.TryDecodeInstructionHeader(code.ToArray(), pc, out var op, out var scale,
                    out var operandStart, out var operandByteCount, out var instructionLength))
                throw new InvalidOperationException($"Truncated instruction stream at pc {oldPc}.");

            var operands = new byte[operandByteCount];
            for (var i = 0; i < operandByteCount; i++)
                operands[i] = code[operandStart + i];
            decoded.Add(new(oldPc, op, scale, operands));
            pc += instructionLength;
        }

        return decoded;
    }

    private HashSet<int> CollectProtectedInstructionTargets(IReadOnlyList<DecodedInstruction> instructions)
    {
        var targets = new HashSet<int>();
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (TryGetRelativeTargetPc(instruction, out var targetPc))
                targets.Add(targetPc);
        }

        for (var i = 0; i < switchOnSmiTargets.Count; i++)
            targets.Add(switchOnSmiTargets[i]);
        for (var i = 0; i < generatorSwitchTargets.Count; i++)
            targets.Add(generatorSwitchTargets[i]);

        return targets;
    }

    private static List<DecodedInstruction> ElideDeadPureAccumulatorLoads(
        IReadOnlyList<DecodedInstruction> instructions,
        HashSet<int> protectedTargets)
    {
        var kept = new List<DecodedInstruction>(instructions.Count);
        for (var i = 0; i < instructions.Count; i++)
        {
            var current = instructions[i];
            if (kept.Count != 0)
            {
                var previous = kept[^1];
                if (!protectedTargets.Contains(previous.OldPc) &&
                    BytecodeInfo.IsPureAccumulatorLoad(previous.Op) &&
                    BytecodeInfo.OverwritesAccumulatorWithoutReading(current.Op))
                    kept.RemoveAt(kept.Count - 1);
            }

            kept.Add(current);
        }

        return kept;
    }

    private void RewriteBytecode(IReadOnlyList<DecodedInstruction> instructions)
    {
        var newPcByOldPc = new Dictionary<int, int>(instructions.Count + 1);
        var newPc = 0;
        for (var i = 0; i < instructions.Count; i++)
        {
            newPcByOldPc[instructions[i].OldPc] = newPc;
            newPc += instructions[i].Length;
        }

        newPcByOldPc[code.Count] = newPc;

        code.Clear();
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            var instructionNewPc = code.Count;
            if (instruction.Scale != BytecodeInfo.OperandScale.Single)
                code.Add((byte)BytecodeInfo.GetOperandScalePrefix(instruction.Scale));
            code.Add((byte)instruction.Op);
            var operands = (byte[])instruction.Operands.Clone();

            if (TryGetRelativeTargetPc(instruction, out var oldTargetPc))
            {
                var newTargetPc = MapPcToNextKeptInstructionOrEnd(oldTargetPc, newPcByOldPc);
                var offset = newTargetPc - (instructionNewPc + instruction.Length);
                if (offset < short.MinValue || offset > short.MaxValue)
                    throw new InvalidOperationException(
                        $"Optimized jump offset out of range for {instruction.Op} at pc {instructionNewPc}.");

                operands[0] = (byte)(offset & 0xFF);
                operands[1] = (byte)((offset >> 8) & 0xFF);
            }

            for (var j = 0; j < operands.Length; j++)
                code.Add(operands[j]);
        }

        RemapPcListInPlace(switchOnSmiTargets, newPcByOldPc);
        RemapPcListInPlace(generatorSwitchTargets, newPcByOldPc);
        RemapPcDictionaryInPlace(debugSourceOffsets, newPcByOldPc, PcRemapDirection.Previous);
        RemapPcDictionaryInPlace(runtimeCallDebugNames, newPcByOldPc, PcRemapDirection.Previous);
        RemapPcDictionaryInPlace(tdzReadDebugNames, newPcByOldPc, PcRemapDirection.Previous);
    }

    private static bool TryGetRelativeTargetPc(DecodedInstruction instruction, out int targetPc)
    {
        targetPc = 0;
        switch (instruction.Op)
        {
            case JsOpCode.Jump:
            case JsOpCode.JumpIfTrue:
            case JsOpCode.JumpIfFalse:
            case JsOpCode.JumpIfToBooleanTrue:
            case JsOpCode.JumpIfToBooleanFalse:
            case JsOpCode.JumpIfNull:
            case JsOpCode.JumpIfUndefined:
            case JsOpCode.JumpIfNotUndefined:
            case JsOpCode.JumpIfJsReceiver:
            case JsOpCode.PushTry:
            case JsOpCode.JumpLoop:
                var offset = (short)(instruction.Operands[0] | (instruction.Operands[1] << 8));
                targetPc = instruction.OldPc + instruction.Length + offset;
                return true;
            default:
                return false;
        }
    }

    private static int MapPcToNextKeptInstructionOrEnd(int oldPc, Dictionary<int, int> newPcByOldPc)
    {
        if (newPcByOldPc.TryGetValue(oldPc, out var mapped))
            return mapped;

        var nextOldPc = int.MaxValue;
        foreach (var key in newPcByOldPc.Keys)
            if (key >= oldPc && key < nextOldPc)
                nextOldPc = key;

        return nextOldPc == int.MaxValue ? newPcByOldPc.Values.Max() : newPcByOldPc[nextOldPc];
    }

    private static int MapPcToPreviousKeptInstructionOrStart(int oldPc, Dictionary<int, int> newPcByOldPc)
    {
        if (newPcByOldPc.TryGetValue(oldPc, out var mapped))
            return mapped;

        var previousOldPc = int.MinValue;
        foreach (var key in newPcByOldPc.Keys)
            if (key <= oldPc && key > previousOldPc)
                previousOldPc = key;

        if (previousOldPc != int.MinValue)
            return newPcByOldPc[previousOldPc];

        var firstOldPc = int.MaxValue;
        foreach (var key in newPcByOldPc.Keys)
            if (key < firstOldPc)
                firstOldPc = key;

        return firstOldPc == int.MaxValue ? 0 : newPcByOldPc[firstOldPc];
    }

    private static void RemapPcListInPlace(List<int> pcs, Dictionary<int, int> newPcByOldPc)
    {
        for (var i = 0; i < pcs.Count; i++)
            pcs[i] = MapPcToNextKeptInstructionOrEnd(pcs[i], newPcByOldPc);
    }

    private static void RemapPcDictionaryInPlace<TValue>(
        Dictionary<int, TValue> source,
        Dictionary<int, int> newPcByOldPc,
        PcRemapDirection remapDirection = PcRemapDirection.Next)
    {
        if (source.Count == 0)
            return;

        var remapped = new Dictionary<int, TValue>(source.Count);
        foreach (var pair in source)
        {
            var remappedPc = remapDirection == PcRemapDirection.Previous
                ? MapPcToPreviousKeptInstructionOrStart(pair.Key, newPcByOldPc)
                : MapPcToNextKeptInstructionOrEnd(pair.Key, newPcByOldPc);
            remapped[remappedPc] = pair.Value;
        }

        source.Clear();
        foreach (var pair in remapped)
            source.Add(pair.Key, pair.Value);
    }

    private static void BuildSortedDebugNameTable(
        Dictionary<int, string> source,
        Dictionary<string, int> nameIndexByText,
        List<string> names,
        out int[] keys,
        out int[] nameIndices,
        Func<string, Dictionary<string, int>, List<string>, int> nameIndexer)
    {
        keys = new int[source.Count];
        nameIndices = new int[source.Count];
        var cursor = 0;
        foreach (var key in source.Keys)
            keys[cursor++] = key;

        Array.Sort(keys);
        for (var i = 0; i < keys.Length; i++)
            nameIndices[i] = nameIndexer(source[keys[i]], nameIndexByText, names);
    }

    private static void BuildSortedDebugNameTable(
        Dictionary<long, string> source,
        Dictionary<string, int> nameIndexByText,
        List<string> names,
        out long[] keys,
        out int[] nameIndices,
        Func<string, Dictionary<string, int>, List<string>, int> nameIndexer)
    {
        keys = new long[source.Count];
        nameIndices = new int[source.Count];
        var cursor = 0;
        foreach (var key in source.Keys)
            keys[cursor++] = key;

        Array.Sort(keys);
        for (var i = 0; i < keys.Length; i++)
            nameIndices[i] = nameIndexer(source[keys[i]], nameIndexByText, names);
    }

    private void RememberLastEmit(JsOpCode op, ReadOnlySpan<byte> operands)
    {
        lastEmittedOp = op;
        lastEmittedOp1 = operands.Length > 0 ? operands[0] : (byte)0;
        lastEmittedOp2 = operands.Length > 1 ? operands[1] : (byte)0;
        if (TryDecodeRegisterOperands(op, operands, out var registerOperand0, out var registerOperand1))
        {
            lastEmittedRegisterOperand0 = (ushort)registerOperand0;
            lastEmittedRegisterOperand1 = (ushort)registerOperand1;
        }
        else
        {
            lastEmittedRegisterOperand0 = 0;
            lastEmittedRegisterOperand1 = 0;
        }

        lastEmittedPc = code.Count - (operands.Length + 1);
        lastEmittedLength = operands.Length + 1;
    }

    private static bool TryDecodeRegisterOperands(JsOpCode op, ReadOnlySpan<byte> operands, out int register0,
        out int register1)
    {
        return TryDecodeRegisterOperands(op, BytecodeInfo.OperandScale.Single, operands, out register0, out register1);
    }

    private static bool TryDecodeRegisterOperands(
        JsOpCode op,
        BytecodeInfo.OperandScale scale,
        ReadOnlySpan<byte> operands,
        out int register0,
        out int register1)
    {
        register0 = -1;
        register1 = -1;
        switch (op)
        {
            case JsOpCode.Ldar:
            case JsOpCode.LdaLexicalLocal:
            case JsOpCode.Star:
            case JsOpCode.StaLexicalLocal:
            case JsOpCode.PushContext:
            case JsOpCode.ForInEnumerate:
            case JsOpCode.ForInNext:
            case JsOpCode.ForInStep:
            case JsOpCode.LdaNamedProperty:
            case JsOpCode.LdaKeyedProperty:
            case JsOpCode.StaNamedProperty:
            case JsOpCode.CreateBlockContext:
            case JsOpCode.CreateRestParameter:
            case JsOpCode.CreateFunctionContext:
            case JsOpCode.CreateFunctionContextWithCells:
                if (operands.Length < (int)scale)
                    return false;
                register0 = BytecodeInfo.ReadUnsignedOperand(operands, 0, scale);
                return true;
            case JsOpCode.LdarWide:
            case JsOpCode.LdaLexicalLocalWide:
            case JsOpCode.StarWide:
            case JsOpCode.StaLexicalLocalWide:
                if (operands.Length < 2)
                    return false;
                register0 = operands[0] | (operands[1] << 8);
                return true;
            case JsOpCode.Mov:
                if (operands.Length < 2)
                    return false;
                register0 = BytecodeInfo.ReadUnsignedOperand(operands, 0, scale);
                register1 = BytecodeInfo.ReadUnsignedOperand(operands, 1, scale);
                return true;
            case JsOpCode.MovWide:
                if (operands.Length < 4)
                    return false;
                register0 = operands[0] | (operands[1] << 8);
                register1 = operands[2] | (operands[3] << 8);
                return true;
            case JsOpCode.LdaNamedPropertyWide:
            case JsOpCode.StaNamedPropertyWide:
            case JsOpCode.InitializeNamedProperty:
            case JsOpCode.CreateFunctionContextWithCellsWide:
                if (operands.Length < 2)
                    return false;
                register0 = operands[0] | (operands[1] << 8);
                return true;
            case JsOpCode.StaKeyedProperty:
            case JsOpCode.DefineOwnKeyedProperty:
                if (operands.Length < 2 * (int)scale)
                    return false;
                register0 = BytecodeInfo.ReadUnsignedOperand(operands, 0, scale);
                register1 = BytecodeInfo.ReadUnsignedOperand(operands, 1, scale);
                return true;
            case JsOpCode.CallAny:
            case JsOpCode.CallUndefinedReceiver:
            case JsOpCode.Construct:
                if (operands.Length < 3 * (int)scale)
                    return false;
                register0 = BytecodeInfo.ReadUnsignedOperand(operands, 0, scale);
                register1 = BytecodeInfo.ReadUnsignedOperand(operands, 1, scale);
                return true;
            case JsOpCode.CallProperty:
                if (operands.Length < 4 * (int)scale)
                    return false;
                register0 = BytecodeInfo.ReadUnsignedOperand(operands, 0, scale);
                register1 = BytecodeInfo.ReadUnsignedOperand(operands, 1, scale);
                return true;
            default:
                return false;
        }
    }

    public readonly record struct Label
    {
        private static int nextId = 1;
        public readonly int Id;

        private Label(int id)
        {
            Id = id;
        }

        public bool IsInitialized => Id != 0;

        public static Label Create()
        {
            var id = Interlocked.Increment(ref nextId);
            if (id == 0)
                id = Interlocked.Increment(ref nextId);
            return new(id);
        }
    }


    private readonly record struct JumpInfo(int InstructionPos, Label Target);

    private readonly record struct SwitchOnSmiPatchInfo(int InstructionPos, Label[] Targets);

    private readonly record struct DecodedInstruction(
        int OldPc,
        JsOpCode Op,
        BytecodeInfo.OperandScale Scale,
        byte[] Operands)
    {
        public int Length => (Scale == BytecodeInfo.OperandScale.Single ? 1 : 2) + Operands.Length;
    }

    private enum PcRemapDirection : byte
    {
        Next = 0,
        Previous = 1
    }
#if DEBUG
    private readonly HashSet<int> freeTemporaryRegisterSet;
    private readonly HashSet<long> reportedSuspiciousAccumulatorCopies;
#endif

#if DEBUG
    private void ValidateEmit(JsOpCode op, ReadOnlySpan<byte> operands)
    {
        ValidateRegisterOperands(op, operands);
        ValidateSuspiciousAccumulatorCopy(op, operands);
    }

    private void ValidateRegisterOperands(JsOpCode op, ReadOnlySpan<byte> operands)
    {
        switch (op)
        {
            case JsOpCode.Ldar:
            case JsOpCode.LdarWide:
            case JsOpCode.LdaLexicalLocal:
            case JsOpCode.LdaLexicalLocalWide:
            case JsOpCode.Star:
            case JsOpCode.StarWide:
            case JsOpCode.StaLexicalLocal:
            case JsOpCode.StaLexicalLocalWide:
            case JsOpCode.PushContext:
            case JsOpCode.ForInEnumerate:
            case JsOpCode.ForInNext:
            case JsOpCode.ForInStep:
            case JsOpCode.LdaNamedProperty:
            case JsOpCode.LdaNamedPropertyWide:
            case JsOpCode.LdaKeyedProperty:
            case JsOpCode.StaNamedProperty:
            case JsOpCode.StaNamedPropertyWide:
            case JsOpCode.InitializeNamedProperty:
            case JsOpCode.CreateBlockContext:
            case JsOpCode.CreateRestParameter:
            case JsOpCode.CreateFunctionContext:
            case JsOpCode.CreateFunctionContextWithCells:
                ValidateRegisterOperand(operands[0], op, 0);
                break;

            case JsOpCode.Mov:
            case JsOpCode.MovWide:
                if (TryDecodeRegisterOperands(op, operands, out var reg0, out var reg1))
                {
                    ValidateRegisterOperand(reg0, op, 0);
                    ValidateRegisterOperand(reg1, op, 1);
                }

                break;
        }
    }

    private void ValidateRegisterOperand(int register, JsOpCode op, int operandIndex)
    {
        // if (register >= registerCount)
        // {
        //     throw new InvalidOperationException(
        //         $"Emit validation failed for {op}: operand {operandIndex} references r{register}, but register count is {registerCount}.");
        // }
    }

    private void ValidateSuspiciousAccumulatorCopy(JsOpCode op, ReadOnlySpan<byte> operands)
    {
        if (op is not JsOpCode.Star and not JsOpCode.StarWide)
            return;
        if (lastEmittedOp is not JsOpCode.Ldar and not JsOpCode.LdarWide)
            return;
        if (!TryDecodeRegisterOperands(op, operands, out var destReg, out _))
            return;

        int sourceReg = lastEmittedRegisterOperand0;
        if (sourceReg == destReg)
            return;

        var key = ((long)sourceReg << 32) | (uint)destReg;
        if (reportedSuspiciousAccumulatorCopies.Add(key))
            Debug.WriteLine(
                $"[OkojoBytecodeBuilder] suspicious accumulator copy at pc {lastEmittedPc}: " +
                $"Ldar r{sourceReg} -> Star r{destReg}. Prefer Mov when accumulator preservation is not required.");
    }

#endif
}

public struct OkojoNamedPropertyIcEntry
{
    public StaticNamedPropertyLayout? Shape;
    public SlotInfo SlotInfo;
    public int NameAtom;
}

public enum GlobalBindingIcKind : byte
{
    Uninitialized = 0,
    Lexical = 1,
    LexicalConst = 2,
    NonLexical = 3
}

public struct GlobalBindingIcEntry
{
    public GlobalBindingIcKind Kind;
    public JsContext? LexicalContext;
    public int Slot;
    public int Version;
    public int NameAtom;
}
