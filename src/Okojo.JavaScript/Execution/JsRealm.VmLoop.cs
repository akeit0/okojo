using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Okojo.JavaScript.Bytecode;

namespace Okojo.JavaScript.Execution;

public sealed partial class JsRealm
{
#if OKOJO_VM_PROFILE
    private const int VmProfileOpcodeCount = 256;
    private static readonly long[] s_vmProfileOpcodeCounts = new long[VmProfileOpcodeCount];
    private static readonly long[] s_vmProfilePairCounts = new long[
        VmProfileOpcodeCount * VmProfileOpcodeCount
    ];
    private static long s_vmProfileRunEntries;
    private static long s_vmProfileFrameEntries;
#endif

    internal static string? GetVmOpcodeProfileReport()
    {
#if !OKOJO_VM_PROFILE
        return null;
#else
        var opcodeCounts = new List<(int Opcode, long Count)>();
        var totalOpcodes = 0L;
        for (var opcode = 0; opcode < VmProfileOpcodeCount; opcode++)
        {
            var count = s_vmProfileOpcodeCounts[opcode];
            if (count == 0)
                continue;

            totalOpcodes += count;
            opcodeCounts.Add((opcode, count));
        }

        opcodeCounts.Sort(
            static (left, right) =>
            {
                var countComparison = right.Count.CompareTo(left.Count);
                return countComparison != 0 ? countComparison : left.Opcode.CompareTo(right.Opcode);
            }
        );

        var pairCounts = new List<(int From, int To, long Count)>();
        var totalPairs = 0L;
        for (var pair = 0; pair < s_vmProfilePairCounts.Length; pair++)
        {
            var count = s_vmProfilePairCounts[pair];
            if (count == 0)
                continue;

            totalPairs += count;
            pairCounts.Add((pair >> 8, pair & 0xff, count));
        }

        pairCounts.Sort(
            static (left, right) =>
            {
                var countComparison = right.Count.CompareTo(left.Count);
                if (countComparison != 0)
                    return countComparison;

                var fromComparison = left.From.CompareTo(right.From);
                return fromComparison != 0 ? fromComparison : left.To.CompareTo(right.To);
            }
        );

        static string OpcodeName(int opcode) => Enum.GetName((JsOpCode)opcode) ?? $"0x{opcode:X2}";

        var report = new StringBuilder();
        report.AppendLine(
            $"[profile] run_entries={s_vmProfileRunEntries} frame_entries={s_vmProfileFrameEntries} "
                + $"total_opcodes={totalOpcodes} "
                + $"distinct_opcodes={opcodeCounts.Count} total_pairs={totalPairs} "
                + $"distinct_pairs={pairCounts.Count}"
        );
        foreach (var (opcode, count) in opcodeCounts)
            report.AppendLine(
                $"[profile-op] opcode={opcode} name={OpcodeName(opcode)} count={count}"
            );
        foreach (var (from, to, count) in pairCounts)
            report.AppendLine(
                $"[profile-pair] from={from} from_name={OpcodeName(from)} "
                    + $"to={to} to_name={OpcodeName(to)} count={count}"
            );

        return report.ToString();
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadByteOrU16(ReadOnlySpan<byte> code, ref int pc, bool wide)
    {
        return wide ? code[pc++] | (code[pc++] << 8) : code[pc++];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadByteOrU16(ref byte code, ref int pc, bool wide)
    {
        if (wide)
        {
            int result = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref code, pc));
            pc += 2;
            return result;
        }

        return Unsafe.Add(ref code, pc++);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadScaledUnsignedOperand(
        ref byte pc,
        ref int operandOffset,
        BytecodeInfo.OperandScale operandScale
    )
    {
        if (operandScale == BytecodeInfo.OperandScale.Single)
            return Unsafe.Add(ref pc, operandOffset++);

        return ReadScaledUnsignedOperandSlow(ref pc, ref operandOffset, operandScale);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadScaledUnsignedOperandSlow(
        ref byte pc,
        ref int operandOffset,
        BytecodeInfo.OperandScale operandScale
    )
    {
        if (operandScale == BytecodeInfo.OperandScale.Wide)
        {
            int value = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref pc, operandOffset));
            operandOffset += 2;
            return value;
        }

        if (operandScale == BytecodeInfo.OperandScale.ExtraWide)
        {
            var value = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref pc, operandOffset));
            operandOffset += 4;
            return value;
        }

        return ThrowInvalidOperandScale(operandScale);
    }

    [Conditional("DEBUG")]
    private static void AssertValidOperandScale(BytecodeInfo.OperandScale operandScale)
    {
        Debug.Assert(
            operandScale
                is BytecodeInfo.OperandScale.Single
                    or BytecodeInfo.OperandScale.Wide
                    or BytecodeInfo.OperandScale.ExtraWide
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ThrowInvalidOperandScale(BytecodeInfo.OperandScale operandScale)
    {
        throw new ArgumentOutOfRangeException(nameof(operandScale));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckExecutionSlowPath(
        Span<JsValue> fullStack,
        int fp,
        ref byte bytecode,
        ref byte opcodePc,
        JsOpCode currentOpcode,
        ref ulong nextCheck
    )
    {
        Agent.ExecutionCheckPolicy.CheckSlowPath(
            this,
            fullStack,
            fp,
            ref bytecode,
            ref opcodePc,
            currentOpcode,
            ref nextCheck
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckDebuggerSlowPath(
        Span<JsValue> fullStack,
        int fp,
        ref byte bytecode,
        ref byte opcodePc,
        bool breakpointHit
    )
    {
        var executionCheckPolicy = Agent.ExecutionCheckPolicy;
        if (!executionCheckPolicy.HasDebugger)
            return;

        if (breakpointHit)
            executionCheckPolicy.EmitBreakpointCheckpoint(
                this,
                fullStack,
                fp,
                ref bytecode,
                ref opcodePc
            );
        else
            executionCheckPolicy.EmitDebuggerStatementCheckpoint(
                this,
                fullStack,
                fp,
                ref bytecode,
                ref opcodePc
            );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool HandleDebuggerOpcode(
        Span<JsValue> fullStack,
        int fp,
        ref byte bytecode,
        ref byte opcodePc
    )
    {
        var currentBytecodeFunc = (CurrentCallFrame.Function as JsBytecodeFunction)!;
        var breakpointHit = Agent.TryRestoreBreakpointForHit(
            currentBytecodeFunc.Script,
            GetPcOffset(ref bytecode, ref opcodePc),
            out _,
            out _,
            out _
        );
        if (
            breakpointHit
            && (Agent.ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.Breakpoint) == 0
        )
            return true;

        if (
            breakpointHit
            || (Agent.ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.DebuggerStatement)
                != 0
        )
            CheckDebuggerSlowPath(fullStack, fp, ref bytecode, ref opcodePc, breakpointHit);

        return breakpointHit;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EmitExecutionBoundaryCheckpoint(
        Span<JsValue> fullStack,
        int fp,
        ExecutionCheckpointKind kind,
        ref byte bytecode,
        ref byte pc
    )
    {
        Agent.ExecutionCheckPolicy.EmitBoundaryCheckpoint(
            this,
            fullStack,
            fp,
            kind,
            ref bytecode,
            ref pc
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetPcOffset(ref byte bytecode, ref byte pc)
    {
        // A3: unchecked is provably safe - both refs point into the same
        // managed byte[] (max 2^31 elements), so the offset always fits int.
        // The former checked() emitted a dead overflow branch at every
        // inlined call site (handlers + slow paths).
        return unchecked((int)Unsafe.ByteOffset(ref bytecode, ref pc));
    }

    [Conditional("DEBUG")]
    private static void ValidateAtomizedNameConstant(int atom, string message)
    {
        if (atom < 0)
            throw new InvalidOperationException(message);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TypeOfGlobal(
        JsOpCode op,
        JsScript script,
        ref byte bytecode,
        ref int pc,
        int[] atomizedStringConstants,
        ref JsValue acc
    )
    {
        var isWide = op == JsOpCode.TypeOfGlobalWide;
        var nameIdx = ReadByteOrU16(ref bytecode, ref pc, isWide);
        var icSlot = ReadByteOrU16(ref bytecode, ref pc, isWide);
        var atom = atomizedStringConstants[nameIdx];
        if (TryGetGlobalBindingByAtom(script, icSlot, atom, out var val))
            acc = TypeOfValue(val);
        else
            acc = "undefined";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int TypeOfGlobal(
        JsOpCode op,
        JsScript script,
        ref byte bytecode,
        ref byte pc,
        int[] atomizedStringConstants,
        ref JsValue acc
    )
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        TypeOfGlobal(op, script, ref bytecode, ref pcOffset, atomizedStringConstants, ref acc);
        return pcOffset - startOffset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsObject? ResolvePrivateBrandToken(JsBytecodeFunction currentFunc, int brandId)
    {
        return currentFunc.TryResolvePrivateBrandToken(brandId, out var token) ? token : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsObject ResolvePrivateStorageBrandToken(JsBytecodeFunction currentFunc, int brandId)
    {
        return ResolvePrivateBrandToken(currentFunc, brandId)
            ?? Agent.GetLegacyPrivateBrandToken(brandId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsObject ResolvePrivateInitBrandToken(
        JsObject target,
        JsBytecodeFunction currentFunc,
        int brandId
    )
    {
        return ResolvePrivateBrandToken(currentFunc, brandId)
            ?? (
                target is JsFunction functionTarget
                    ? functionTarget
                    : Agent.GetLegacyPrivateBrandToken(brandId)
            );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InitPrivateFieldValue(
        JsObject target,
        JsBytecodeFunction currentFunc,
        int brandId,
        int slotIndex,
        in JsValue value
    )
    {
        if (!target.IsExtensible)
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Cannot add private member to non-extensible object"
            );

        Agent.InitPrivateField(
            target,
            ResolvePrivateInitBrandToken(target, currentFunc, brandId),
            slotIndex,
            value
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InitPrivateAccessorValue(
        JsObject target,
        JsBytecodeFunction currentFunc,
        int brandId,
        int slotIndex,
        JsFunction? getter,
        JsFunction? setter
    )
    {
        if (!target.IsExtensible)
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Cannot add private member to non-extensible object"
            );

        Agent.InitPrivateAccessor(
            target,
            ResolvePrivateInitBrandToken(target, currentFunc, brandId),
            slotIndex,
            getter,
            setter
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InitPrivateMethodValue(
        JsObject target,
        JsBytecodeFunction currentFunc,
        int brandId,
        int slotIndex,
        JsFunction method
    )
    {
        if (!target.IsExtensible)
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Cannot add private member to non-extensible object"
            );

        Agent.InitPrivateMethod(
            target,
            ResolvePrivateInitBrandToken(target, currentFunc, brandId),
            slotIndex,
            method
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryGetPrivateSlotValue(
        JsObject target,
        JsBytecodeFunction currentFunc,
        int brandId,
        int slotIndex,
        out JsValue value
    )
    {
        return Agent.TryGetPrivateSlot(
            target,
            ResolvePrivateStorageBrandToken(currentFunc, brandId),
            slotIndex,
            out value
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TrySetPrivateFieldValue(
        JsObject target,
        JsBytecodeFunction currentFunc,
        int brandId,
        int slotIndex,
        in JsValue value
    )
    {
        return Agent.TrySetPrivateField(
            target,
            ResolvePrivateStorageBrandToken(currentFunc, brandId),
            slotIndex,
            value
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T ThrowPrivateAccessorTypeError<T>(string detailCode, string message)
    {
        throw TypeError(detailCode, message);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowPrivateFieldReadBrandError(
        JsBytecodeFunction currentFunc,
        int brandId,
        int slotIndex
    )
    {
        var privateName = GetPrivateFieldDebugNameOrDefault(currentFunc.Script, brandId, slotIndex);
        throw TypeErrorInRealm(
            currentFunc.Realm,
            "PRIVATE_FIELD_BRAND",
            $"Cannot read private member {privateName} from an object whose class did not declare it"
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowPrivateFieldWriteBrandError(
        JsBytecodeFunction currentFunc,
        int brandId,
        int slotIndex
    )
    {
        var privateName = GetPrivateFieldDebugNameOrDefault(currentFunc.Script, brandId, slotIndex);
        throw TypeErrorInRealm(
            currentFunc.Realm,
            "PRIVATE_FIELD_BRAND",
            $"Cannot write private member {privateName} from an object whose class did not declare it"
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ExecuteInitPrivateField(
        JsBytecodeFunction currentFunc,
        ref JsValue registers,
        ref byte pc,
        ref JsValue acc
    )
    {
        int objReg = pc;
        ref var targetRef = ref Unsafe.Add(ref registers, objReg);
        if (!targetRef.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        int valueReg = Unsafe.Add(ref pc, 1);
        var brandId = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref pc, 2));
        var slotIndex = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref pc, 6));
        var value = Unsafe.Add(ref registers, valueReg);
        InitPrivateFieldValue(target, currentFunc, brandId, slotIndex, value);
        acc = JsValue.Undefined;
        return 8;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ExecuteInitPrivateAccessor(
        JsBytecodeFunction currentFunc,
        ref JsValue registers,
        ref byte pc,
        ref JsValue acc
    )
    {
        int objReg = pc;
        ref var targetRef = ref Unsafe.Add(ref registers, objReg);
        if (!targetRef.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        int getterReg = Unsafe.Add(ref pc, 1);
        int setterReg = Unsafe.Add(ref pc, 2);
        var brandId = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref pc, 3));
        var slotIndex = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref pc, 7));
        var getterValue = Unsafe.Add(ref registers, getterReg);
        var setterValue = Unsafe.Add(ref registers, setterReg);
        var getter =
            getterValue.IsUndefined ? null
            : getterValue.TryGetObject(out var getterObj) && getterObj is JsFunction getterFn
                ? getterFn
            : ThrowPrivateAccessorTypeError<JsFunction?>(
                "PRIVATE_ACCESSOR_GETTER",
                "Private accessor getter must be function or undefined"
            );
        var setter =
            setterValue.IsUndefined ? null
            : setterValue.TryGetObject(out var setterObj) && setterObj is JsFunction setterFn
                ? setterFn
            : ThrowPrivateAccessorTypeError<JsFunction?>(
                "PRIVATE_ACCESSOR_SETTER",
                "Private accessor setter must be function or undefined"
            );
        InitPrivateAccessorValue(target, currentFunc, brandId, slotIndex, getter, setter);
        acc = JsValue.Undefined;
        return 9;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ExecuteInitPrivateMethod(
        JsBytecodeFunction currentFunc,
        ref JsValue registers,
        ref byte pc,
        ref JsValue acc
    )
    {
        int objReg = pc;
        ref var targetRef = ref Unsafe.Add(ref registers, objReg);
        if (!targetRef.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        int methodReg = Unsafe.Add(ref pc, 1);
        var brandId = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref pc, 2));
        var slotIndex = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref pc, 6));
        var methodValue = Unsafe.Add(ref registers, methodReg);
        if (!methodValue.TryGetObject(out var methodObj) || methodObj is not JsFunction)
            ThrowTypeError("PRIVATE_METHOD_VALUE", "Private method value must be function");

        InitPrivateMethodValue(target, currentFunc, brandId, slotIndex, (JsFunction)methodObj);
        acc = JsValue.Undefined;
        return 8;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ExecuteGetPrivateField(
        JsBytecodeFunction currentFunc,
        ref JsValue registers,
        ref byte pc,
        ref JsValue acc
    )
    {
        int objReg = pc;
        ref var targetRef = ref Unsafe.Add(ref registers, objReg);
        if (!targetRef.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        var brandId = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref pc, 1));
        var slotIndex = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref pc, 5));
        if (
            !TryGetPrivateSlotValue(
                target,
                currentFunc,
                brandId,
                slotIndex,
                out var privateSlotValue
            )
        )
            ThrowPrivateFieldReadBrandError(currentFunc, brandId, slotIndex);

        if (
            privateSlotValue.TryGetObject(out var memberObj)
            && memberObj is JsPrivateAccessorDescriptor accessor
        )
        {
            if (accessor.Getter is null)
            {
                var privateName = GetPrivateFieldDebugNameOrDefault(
                    currentFunc.Script,
                    brandId,
                    slotIndex
                );
                ThrowTypeError(
                    "PRIVATE_ACCESSOR_GETTER",
                    $"Cannot read private member {privateName} without getter"
                );
            }

            acc = InvokeFunction(accessor.Getter, target, ReadOnlySpan<JsValue>.Empty);
        }
        else if (
            privateSlotValue.TryGetObject(out memberObj)
            && memberObj is JsPrivateMethodDescriptor method
        )
        {
            acc = method.Method;
        }
        else
        {
            acc = privateSlotValue;
        }

        return 7;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ExecuteSetPrivateField(
        JsBytecodeFunction currentFunc,
        ref JsValue registers,
        ref byte pc,
        ref JsValue acc
    )
    {
        int objReg = pc;
        ref var targetRef = ref Unsafe.Add(ref registers, objReg);
        if (!targetRef.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        int valueReg = Unsafe.Add(ref pc, 1);
        var brandId = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref pc, 2));
        var slotIndex = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref pc, 6));
        var value = Unsafe.Add(ref registers, valueReg);
        if (
            !TryGetPrivateSlotValue(
                target,
                currentFunc,
                brandId,
                slotIndex,
                out var existingPrivateValue
            )
        )
            ThrowPrivateFieldWriteBrandError(currentFunc, brandId, slotIndex);

        if (
            existingPrivateValue.TryGetObject(out var memberObj)
            && memberObj is JsPrivateAccessorDescriptor accessor
        )
        {
            if (accessor.Setter is null)
            {
                var privateName = GetPrivateFieldDebugNameOrDefault(
                    currentFunc.Script,
                    brandId,
                    slotIndex
                );
                ThrowTypeError(
                    "PRIVATE_ACCESSOR_SETTER",
                    $"Cannot write private member {privateName} without setter"
                );
            }

            var arg = MemoryMarshal.CreateReadOnlySpan(in value, 1);
            _ = InvokeFunction(accessor.Setter, target, arg);
            acc = value;
            return 8;
        }

        if (
            existingPrivateValue.TryGetObject(out memberObj)
            && memberObj is JsPrivateMethodDescriptor
        )
        {
            var privateName = GetPrivateFieldDebugNameOrDefault(
                currentFunc.Script,
                brandId,
                slotIndex
            );
            ThrowTypeError(
                "PRIVATE_METHOD_ASSIGN",
                $"Cannot assign to private method {privateName}"
            );
        }

        if (!TrySetPrivateFieldValue(target, currentFunc, brandId, slotIndex, value))
            ThrowTypeError("PRIVATE_FIELD_INTERNAL", "Invalid private field write state");

        acc = value;
        return 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int HandleSwitchOnSmi(ref byte bytecode, JsScript script, ref byte pc, in JsValue acc)
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        HandleSwitchOnSmi(script, ref pcOffset, acc);
        return pcOffset - startOffset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int HandleSwitchOnGeneratorState(
        ref byte bytecode,
        JsScript script,
        ref byte pc,
        int fp
    )
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        HandleSwitchOnGeneratorState(script, ref pcOffset, fp);
        return pcOffset - startOffset;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private GeneratorDispatchResult HandleSuspendGenerator(
        ref byte bytecode,
        Span<JsValue> fullStack,
        ref JsValue registers,
        int stopAtCallerFp,
        ref int fp,
        ref byte pc,
        ref JsValue acc,
        out int pcUsed
    )
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        var result = HandleSuspendGenerator(
            ref bytecode,
            fullStack,
            ref registers,
            stopAtCallerFp,
            ref fp,
            ref pcOffset,
            ref acc
        );
        pcUsed = pcOffset - startOffset;
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private GeneratorDispatchResult HandleResumeGenerator(
        ref byte bytecode,
        Span<JsValue> fullStack,
        ref JsValue registers,
        int stopAtCallerFp,
        ref int fp,
        ref byte pc,
        ref JsValue acc,
        out int pcUsed
    )
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        var result = HandleResumeGenerator(
            ref bytecode,
            fullStack,
            ref registers,
            stopAtCallerFp,
            ref fp,
            ref pcOffset,
            ref acc
        );
        pcUsed = pcOffset - startOffset;
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowLdaGlobalReferenceError(int atom)
    {
        throw new JsRuntimeException(
            JsErrorKind.ReferenceError,
            $"{Atoms.AtomToString(atom)} is not defined",
            "GLOBAL_NOT_DEFINED"
        );
    }

    // Opcode handler extraction convention (A2 hot/cold split):
    // a `ref byte pc` cursor can only be RESEATED in the caller's scope
    // (`pc = ref Unsafe.Add(...)` inside a callee rebinds the callee's own
    // ref slot and is lost on return). Handlers therefore take
    // `ref byte bytecode, ref byte pc`, decode through an int offset, and
    // RETURN the consumed operand length; arms apply
    // `pc = ref Unsafe.Add(ref pc, HandleXxx(...))`.
    // `ref acc` is safe to pass because the accumulator is a field alias and
    // handlers only write values through it.

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int HandleCreateClosure(
        JsOpCode op,
        object[] objectPool,
        ref byte bytecode,
        ref byte pc,
        ref JsValue acc
    )
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        var isWide = op == JsOpCode.CreateClosureWide;
        int idx;
        if (isWide)
        {
            idx = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref bytecode, pcOffset));
            pcOffset += 2;
        }
        else
        {
            idx = Unsafe.Add(ref bytecode, pcOffset);
            pcOffset += 1;
        }

        pcOffset += 1; // flags (unused for now)

        // A10: constant pool slot typed by the compiler as function constant -
        // cast is compiler-guaranteed, skip the runtime type test.
        acc = BindClosureIfNeeded(Unsafe.As<JsBytecodeFunction>(objectPool[idx]));
        return pcOffset - startOffset;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int HandleCreateFunctionContext(
        JsOpCode op,
        Span<JsValue> fullStack,
        int fp,
        JsScript script,
        ref byte bytecode,
        ref byte pc,
        ref JsValue acc
    )
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        var parent = GetCurrentContext(fullStack);
        int slotCount;
        if (op == JsOpCode.CreateFunctionContextWithCellsWide)
        {
            slotCount = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref bytecode, pcOffset));
            pcOffset += 2;
        }
        else
        {
            slotCount = Unsafe.Add(ref bytecode, pcOffset);
            pcOffset += 1;
        }

        JsContext o;
        var frameKind = CurrentCallFrame.FrameKind;
        if (
            parent is null
            && frameKind is CallFrameKind.ScriptFrame or CallFrameKind.GeneratorFrame
            && Agent.TryGetCurrentModuleRuntimeBindings(out var activeModuleBindings)
        )
        {
            if (activeModuleBindings.TopLevelContext is not null)
            {
                o = activeModuleBindings.TopLevelContext;
#if DEBUG
                if (o.Slots.Length != slotCount)
                    throw new InvalidOperationException(
                        "Shared module context slot count mismatch."
                    );
#endif
            }
            else
            {
                o = new(parent, slotCount) { ModuleBindings = activeModuleBindings };
            }
        }
        else
        {
            o = new(parent, slotCount);
        }

        acc = JsValue.FromObject(o);
        if (
            op
            is JsOpCode.CreateFunctionContextWithCells
                or JsOpCode.CreateFunctionContextWithCellsWide
        )
        {
            SetFrameContext(fullStack, fp, o);
            if (
                parent is null
                && CurrentCallFrame.FrameKind
                    is CallFrameKind.ScriptFrame
                        or CallFrameKind.GeneratorFrame
                && !script.SuppressTopLevelLexicalRegistration
            )
                RegisterGlobalLexicalBindings(script, o);
        }

        return pcOffset - startOffset;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int HandleCurrentContextSlotOp(
        JsOpCode op,
        Span<JsValue> fullStack,
        ref byte bytecode,
        ref byte pc,
        ref JsValue acc
    )
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        var ctx =
            GetCurrentContext(fullStack)
            ?? throw new InvalidOperationException("No current context.");

        int slotIndex;
        if (
            op
            is JsOpCode.LdaCurrentContextSlotWide
                or JsOpCode.LdaCurrentContextSlotNoTdzWide
                or JsOpCode.StaCurrentContextSlotWide
        )
        {
            slotIndex = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref bytecode, pcOffset));
            pcOffset += 2;
        }
        else
        {
            slotIndex = Unsafe.Add(ref bytecode, pcOffset);
            pcOffset += 1;
        }

        ref var slot = ref ctx.Slots[slotIndex];
        if (op is JsOpCode.LdaCurrentContextSlot or JsOpCode.LdaCurrentContextSlotWide)
            acc = ThrowIfTheHole(slot);
        else if (
            op is JsOpCode.LdaCurrentContextSlotNoTdz or JsOpCode.LdaCurrentContextSlotNoTdzWide
        )
            acc = slot;
        else
            slot = acc;

        return pcOffset - startOffset;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int HandleContextSlotOp(
        JsOpCode op,
        Span<JsValue> fullStack,
        ref byte bytecode,
        ref byte pc,
        ref JsValue acc
    )
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        int slotIndex;
        if (
            op
            is JsOpCode.LdaContextSlotWide
                or JsOpCode.LdaContextSlotNoTdzWide
                or JsOpCode.StaContextSlotWide
        )
        {
            slotIndex = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref bytecode, pcOffset));
            pcOffset += 2;
        }
        else
        {
            slotIndex = Unsafe.Add(ref bytecode, pcOffset);
            pcOffset += 1;
        }

        // context depth
        var depth = Unsafe.Add(ref bytecode, pcOffset);
        pcOffset += 1;
        var ctx = GetContextAtDepth(fullStack, depth);
        ref var slot = ref ctx.Slots[slotIndex];
        if (op is JsOpCode.LdaContextSlot or JsOpCode.LdaContextSlotWide)
            acc = ThrowIfTheHole(slot);
        else if (op is JsOpCode.LdaContextSlotNoTdz or JsOpCode.LdaContextSlotNoTdzWide)
            acc = slot;
        else
            slot = acc;

        return pcOffset - startOffset;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int HandleStaGlobal(
        JsOpCode op,
        JsScript script,
        bool isStrict,
        int[] atomizedStringConstants,
        ref byte bytecode,
        ref byte pc,
        ref JsValue acc
    )
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        int nameIdx;
        int icSlot;
        if (
            op
            is not JsOpCode.StaGlobalWide
                and not JsOpCode.StaGlobalInitWide
                and not JsOpCode.StaGlobalFuncDeclWide
        )
        {
            nameIdx = Unsafe.Add(ref bytecode, pcOffset);
            icSlot = Unsafe.Add(ref bytecode, pcOffset + 1);
            pcOffset += 2;
        }
        else
        {
            nameIdx = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref bytecode, pcOffset));
            icSlot = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref bytecode, pcOffset + 2));
            pcOffset += 4;
        }

        var atom = atomizedStringConstants[nameIdx];
        var isInitializationStore =
            op
            is JsOpCode.StaGlobalInit
                or JsOpCode.StaGlobalInitWide
                or JsOpCode.StaGlobalFuncDecl
                or JsOpCode.StaGlobalFuncDeclWide;
        var useFunctionDeclarationSemantics =
            op is JsOpCode.StaGlobalFuncDecl or JsOpCode.StaGlobalFuncDeclWide;
        StoreGlobalByAtom(
            script,
            icSlot,
            atom,
            isInitializationStore,
            useFunctionDeclarationSemantics,
            isStrict,
            ref acc
        );
        return pcOffset - startOffset;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int HandleGetNamedPropertyFromSuper(
        JsOpCode op,
        Span<JsValue> fullStack,
        int fp,
        int[] atomizedStringConstants,
        ref byte bytecode,
        ref byte pc,
        ref JsValue acc
    )
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        var isWide = op == JsOpCode.GetNamedPropertyFromSuperWide;
        int nameIdx;
        if (isWide)
        {
            nameIdx = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref bytecode, pcOffset));
            pcOffset += 2;
        }
        else
        {
            nameIdx = Unsafe.Add(ref bytecode, pcOffset);
            pcOffset += 1;
        }

        var atom = atomizedStringConstants[nameIdx];
        ValidateAtomizedNameConstant(
            atom,
            "GetNamedPropertyFromSuper requires atomized name constant."
        );
        var thisValue = fullStack[fp + OffsetThisValue];
        if (thisValue.IsTheHole)
            ThrowSuperNotCalled();
        if (!thisValue.TryGetObject(out var receiver))
            ThrowTypeError("SUPER_RECEIVER", "super receiver must be object");

        var superBase = RequireObjectSuperBaseForFrame(fp);
        if (superBase.TryGetPropertyAtomWithReceiver(this, receiver, atom, out var value, out _))
            acc = value;
        else
            acc = JsValue.Undefined;

        return pcOffset - startOffset;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int HandleCreateObjectLiteral(
        JsOpCode op,
        object[] objectPool,
        ref byte bytecode,
        ref byte pc,
        ref JsValue acc
    )
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        var isWide = op == JsOpCode.CreateObjectLiteralWide;
        int boilerplateIdx;
        if (isWide)
        {
            boilerplateIdx = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref bytecode, pcOffset));
            pcOffset += 2;
        }
        else
        {
            boilerplateIdx = Unsafe.Add(ref bytecode, pcOffset);
            pcOffset += 1;
        }

        pcOffset += 1; // flags (unused)

        // A10: boilerplate slot typed by the compiler as a static layout -
        // cast is compiler-guaranteed, skip the runtime type test.
        acc = new JsPlainObject(Unsafe.As<StaticNamedPropertyLayout>(objectPool[boilerplateIdx]));
        return pcOffset - startOffset;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsArray CreateArrayLiteralFromPoolSlowPath(object[] objectPool, int constantIndex)
    {
        var literal = objectPool[constantIndex];
        if (literal is JsValue[] elements)
            return CreateArrayObject(elements);
        if (literal is int length && length >= 0)
            return CreateArrayObjectWithLength(length);
        return CreateArrayObject();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryCatchRunCoreException(
        Exception e,
        ref byte pc,
        int stopAtCallerFp,
        ref int startPc,
        out JsRuntimeException? ex,
        ref JsValue acc
    )
    {
        var isJsRuntimeException = e is JsRuntimeException;
        ex = e as JsRuntimeException ?? WrapUnexpectedRuntimeException(e);
        var currentFrame = Unsafe.As<JsValue, CallFrame>(ref Stack[fp]);
        if (currentFrame.Function is not JsBytecodeFunction currentFunc)
        {
            CaptureExceptionStackIfMissing(ex, Stack, fp, currentFrame.CallerPc);
            return false;
        }

        var opcodePcOffset = GetPcOffset(ref currentFunc.Script.Bytecode[0], ref pc);
        CaptureExceptionStackIfMissing(ex, Stack, fp, opcodePcOffset);
        ResolveLazyRuntimeExceptionMessage(ex, currentFunc.Script, opcodePcOffset);
        if (ex is JsFatalRuntimeException)
            return false;

        if (!TryHandleJsRuntimeException(Stack, stopAtCallerFp, ref fp, out startPc))
        {
            if (!isJsRuntimeException)
                throw ex;
            ex = null;
            return false;
        }

        acc = ex.ThrownValue ?? CreateErrorObjectFromException(ex);
        return true;
    }

    private void Run(int stopAtCallerFp = -1, int startPc = 0)
    {
        managedRunDepth++;
        var acc = this.acc;
#if OKOJO_VM_PROFILE
        s_vmProfileRunEntries++;
        var previousOpcode = -1;
#endif
        try
        {
            var fullStack = Stack.AsSpan();

            ref var pc = ref Unsafe.NullRef<byte>();
            ref var fp = ref this.fp;

            ReloadFrame:
            var currentFunc = Unsafe.As<JsBytecodeFunction>(
                Unsafe.As<JsValue, CallFrame>(ref fullStack[fp]).Function
            );
            ref var bytecode = ref MemoryMarshal.GetArrayDataReference(currentFunc.Script.Bytecode);
            pc = ref Unsafe.Add(ref bytecode, startPc);
            startPc = 0;
#if OKOJO_VM_PROFILE
            s_vmProfileFrameEntries++;
            previousOpcode = -1;
#endif
            ref var nextCheck = ref Agent.ExecutionCheckCountdown;
            var objectPool = currentFunc.Script.ObjectConstants;
            var atomizedStringConstants = currentFunc.Script.AtomizedStringConstants;
            ref var registerRef = ref fullStack[fp + HeaderSize];
            var namedPropertyIcEntries = currentFunc.Script.NamedPropertyIcEntries;
            var prototypeNamedPropertyIcEntries = currentFunc
                .Script
                .PrototypeNamedPropertyIcEntries;

            while (true)
            {
                var operandScale = BytecodeInfo.OperandScale.Single;
                ref var opcodePc = ref Unsafe.NullRef<byte>();
                double num1,
                    num2;
                int intNum1,
                    intNum2;
                int reg;
                int operandOffset;
                ref var slotRef = ref Unsafe.NullRef<JsValue>();
                JsObject? obj;
                SlotInfo slotInfo;
                bool boolTemp;
                long longNum;
                try
                {
                    NextOp:
                    opcodePc = ref pc;
                    var op = (JsOpCode)opcodePc;
                    pc = ref Unsafe.Add(ref pc, 1);
                    if (--nextCheck == 0)
                    {
                        this.acc = acc;
                        CheckExecutionSlowPath(
                            fullStack,
                            fp,
                            ref bytecode,
                            ref opcodePc,
                            op,
                            ref nextCheck
                        );
                    }
#if OKOJO_VM_PROFILE
                    var opcodeValue = (byte)op;
                    s_vmProfileOpcodeCounts[opcodeValue]++;
                    if (previousOpcode >= 0)
                        s_vmProfilePairCounts[(previousOpcode << 8) | opcodeValue]++;
                    previousOpcode = opcodeValue;
#endif
                    switch (op)
                    {
                        case JsOpCode.Wide:
                            operandScale = BytecodeInfo.OperandScale.Wide;
                            goto NextOp;
                        case JsOpCode.ExtraWide:
                            operandScale = BytecodeInfo.OperandScale.ExtraWide;
                            goto NextOp;
                        case JsOpCode.LdaZero:
                            acc = JsValue.FromInt32(0);
                            break;
                        case JsOpCode.LdaUndefined:
                            acc = JsValue.Undefined;
                            break;
                        case JsOpCode.LdaNull:
                            acc = JsValue.Null;
                            break;
                        case JsOpCode.LdaTheHole:
                            acc = JsValue.TheHole;
                            break;
                        case JsOpCode.LdaTrue:
                            acc = JsValue.True;
                            break;
                        case JsOpCode.LdaFalse:
                            acc = JsValue.False;
                            break;

                        case JsOpCode.LdaNumericConstant:
                            {
                                acc = new(currentFunc.Script.NumericConstants[pc]);
                                pc = ref Unsafe.Add(ref pc, 1);
                            }
                            break;
                        case JsOpCode.LdaNumericConstantWide:
                            {
                                acc = new(
                                    currentFunc.Script.NumericConstants[
                                        Unsafe.ReadUnaligned<ushort>(ref pc)
                                    ]
                                );
                                pc = ref Unsafe.Add(ref pc, 2);
                            }
                            break;

                        case JsOpCode.LdaStringConstant:
                            {
                                acc = Unsafe.As<string>(objectPool[pc]);
                                pc = ref Unsafe.Add(ref pc, 1);
                            }
                            break;
                        case JsOpCode.LdaTypedConst:
                        case JsOpCode.LdaTypedConstWide:
                            {
                                intNum2 = pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                if (op == JsOpCode.LdaTypedConstWide)
                                {
                                    intNum1 = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    intNum1 = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                acc = new((Tag)intNum2, obj: objectPool[intNum1]);
                            }
                            break;
                        case JsOpCode.LdaThis:
                            acc = fullStack[fp + OffsetThisValue];
                            if (acc.IsTheHole)
                                ThrowSuperNotCalled();
                            break;
                        case JsOpCode.LdaNewTarget:
                            acc =
                                CurrentCallFrame.FrameKind == CallFrameKind.GeneratorFrame
                                    ? JsValue.Undefined
                                    : Unsafe.Add(ref registerRef, OffsetExtra0 - HeaderSize);
                            break;

                        case JsOpCode.CreateClosure:
                        case JsOpCode.CreateClosureWide:
                            pc = ref Unsafe.Add(
                                ref pc,
                                HandleCreateClosure(op, objectPool, ref bytecode, ref pc, ref acc)
                            );
                            break;

                        case JsOpCode.LdaCurrentFunction:
                            acc = Unsafe.As<JsObject>(
                                Unsafe.Subtract(ref registerRef, HeaderSize).Obj!
                            );
                            break;
                        case JsOpCode.LdaSmi:
                            acc = JsValue.FromInt32((sbyte)pc);
                            pc = ref Unsafe.Add(ref pc, 1);
                            break;
                        case JsOpCode.LdaSmiWide:
                            {
                                acc = JsValue.FromInt32(Unsafe.ReadUnaligned<short>(ref pc));
                                pc = ref Unsafe.Add(ref pc, 2);
                            }
                            break;
                        case JsOpCode.LdaSmiExtraWide:
                            {
                                acc = JsValue.FromInt32(Unsafe.ReadUnaligned<int>(ref pc));
                                pc = ref Unsafe.Add(ref pc, 4);
                            }
                            break;
                        case JsOpCode.CreateFunctionContext:
                        case JsOpCode.CreateFunctionContextWithCells:
                        case JsOpCode.CreateFunctionContextWithCellsWide:
                            pc = ref Unsafe.Add(
                                ref pc,
                                HandleCreateFunctionContext(
                                    op,
                                    fullStack,
                                    fp,
                                    currentFunc.Script,
                                    ref bytecode,
                                    ref pc,
                                    ref acc
                                )
                            );
                            break;
                        case JsOpCode.PushContext:
                            {
                                SetFrameContext(
                                    fullStack,
                                    fp,
                                    Unsafe.Add(ref registerRef, pc).Obj as JsContext
                                );
                                pc = ref Unsafe.Add(ref pc, 1);
                            }
                            break;
                        case JsOpCode.PushContextAcc:
                            {
                                SetFrameContext(fullStack, fp, acc.Obj as JsContext);
                            }
                            break;
                        case JsOpCode.PopContext:
                            {
                                SetFrameContext(
                                    fullStack,
                                    fp,
                                    GetCurrentContext(fullStack)?.Parent
                                );
                            }
                            break;
                        case JsOpCode.LdaCurrentContextSlot:
                        case JsOpCode.LdaCurrentContextSlotWide:
                        case JsOpCode.LdaCurrentContextSlotNoTdz:
                        case JsOpCode.LdaCurrentContextSlotNoTdzWide:
                        case JsOpCode.StaCurrentContextSlot:
                        case JsOpCode.StaCurrentContextSlotWide:
                            pc = ref Unsafe.Add(
                                ref pc,
                                HandleCurrentContextSlotOp(
                                    op,
                                    fullStack,
                                    ref bytecode,
                                    ref pc,
                                    ref acc
                                )
                            );
                            break;
                        case JsOpCode.LdaContextSlot:
                        case JsOpCode.LdaContextSlotWide:
                        case JsOpCode.LdaContextSlotNoTdz:
                        case JsOpCode.LdaContextSlotNoTdzWide:
                        case JsOpCode.StaContextSlot:
                        case JsOpCode.StaContextSlotWide:
                            pc = ref Unsafe.Add(
                                ref pc,
                                HandleContextSlotOp(op, fullStack, ref bytecode, ref pc, ref acc)
                            );
                            break;
                        case JsOpCode.Ldar:
                        case JsOpCode.LdarWide:
                        case JsOpCode.LdaLexicalLocal:
                        case JsOpCode.LdaLexicalLocalWide:
                            {
                                reg = op is JsOpCode.LdarWide or JsOpCode.LdaLexicalLocalWide
                                    ? Unsafe.ReadUnaligned<ushort>(ref pc)
                                    : pc;
                                acc = Unsafe.Add(ref registerRef, reg);
                                if (
                                    (
                                        op == JsOpCode.LdaLexicalLocal
                                        || op == JsOpCode.LdaLexicalLocalWide
                                    ) && acc.IsTheHole
                                )
                                    ThrowHole();
                                pc = ref Unsafe.Add(
                                    ref pc,
                                    op is JsOpCode.LdarWide or JsOpCode.LdaLexicalLocalWide ? 2 : 1
                                );
                            }
                            break;
                        case JsOpCode.LdaModuleVariable:
                            {
                                intNum1 = (sbyte)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                intNum2 = pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                acc = ThrowIfTheHole(
                                    Agent.LoadCurrentModuleVariable(this, intNum1, intNum2)
                                );
                            }
                            break;
                        case JsOpCode.Star:
                        case JsOpCode.StarWide:
                            {
                                reg =
                                    op == JsOpCode.StarWide
                                        ? Unsafe.ReadUnaligned<ushort>(ref pc)
                                        : pc;
                                Unsafe.Add(ref registerRef, reg) = acc;
                                pc = ref Unsafe.Add(ref pc, op == JsOpCode.StarWide ? 2 : 1);
                            }
                            break;
                        case JsOpCode.StaModuleVariable:
                            {
                                intNum1 = (sbyte)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                intNum2 = pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                Agent.StoreCurrentModuleVariable(this, intNum1, intNum2, acc);
                            }
                            break;
                        case JsOpCode.Mov:
                        case JsOpCode.MovWide:
                            {
                                if (op == JsOpCode.MovWide)
                                {
                                    intNum1 = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    intNum2 = Unsafe.ReadUnaligned<ushort>(
                                        ref Unsafe.Add(ref pc, 2)
                                    );
                                    pc = ref Unsafe.Add(ref pc, 4);
                                }
                                else
                                {
                                    intNum1 = pc;
                                    intNum2 = Unsafe.Add(ref pc, 1);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }

                                slotRef = ref Unsafe.Add(ref registerRef, intNum1);
                                Unsafe.Add(ref registerRef, intNum2) = slotRef;
                            }
                            break;
                        case JsOpCode.StaLexicalLocal:
                        case JsOpCode.StaLexicalLocalWide:
                            {
                                reg =
                                    op == JsOpCode.StaLexicalLocalWide
                                        ? Unsafe.ReadUnaligned<ushort>(ref pc)
                                        : pc;
                                pc = ref Unsafe.Add(
                                    ref pc,
                                    op == JsOpCode.StaLexicalLocalWide ? 2 : 1
                                );
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                if (slotRef.IsTheHole)
                                    ThrowHole();
                                slotRef = acc;
                            }
                            break;
                        case JsOpCode.LdaGlobal:
                        case JsOpCode.LdaGlobalWide:
                            {
                                if (op == JsOpCode.LdaGlobal)
                                {
                                    intNum1 = pc;
                                    intNum2 = Unsafe.Add(ref pc, 1);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    intNum1 = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    intNum2 = Unsafe.ReadUnaligned<ushort>(
                                        ref Unsafe.Add(ref pc, 2)
                                    );
                                    pc = ref Unsafe.Add(ref pc, 4);
                                }

                                intNum1 = atomizedStringConstants[intNum1];
                                if (
                                    !TryGetGlobalBindingByAtom(
                                        currentFunc.Script,
                                        intNum2,
                                        intNum1,
                                        out acc
                                    )
                                )
                                    ThrowLdaGlobalReferenceError(intNum1);
                            }
                            break;
                        case JsOpCode.StaGlobal:
                        case JsOpCode.StaGlobalWide:
                        case JsOpCode.StaGlobalInit:
                        case JsOpCode.StaGlobalInitWide:
                        case JsOpCode.StaGlobalFuncDecl:
                        case JsOpCode.StaGlobalFuncDeclWide:
                            pc = ref Unsafe.Add(
                                ref pc,
                                HandleStaGlobal(
                                    op,
                                    currentFunc.Script,
                                    currentFunc.IsStrict,
                                    atomizedStringConstants,
                                    ref bytecode,
                                    ref pc,
                                    ref acc
                                )
                            );
                            break;
                        case JsOpCode.TypeOfGlobal:
                        case JsOpCode.TypeOfGlobalWide:
                            {
                                pc = ref Unsafe.Add(
                                    ref pc,
                                    TypeOfGlobal(
                                        op,
                                        currentFunc.Script,
                                        ref bytecode,
                                        ref pc,
                                        atomizedStringConstants,
                                        ref acc
                                    )
                                );
                            }
                            break;
                        case JsOpCode.CreateMappedArguments:
                            {
                                CreateArgumentsObjectForFrame(fp, ref acc);
                            }
                            break;
                        case JsOpCode.CreateRestParameter:
                            {
                                intNum1 = pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                CreateRestParameterForFrame(fp, intNum1, ref acc);
                            }
                            break;

                        case JsOpCode.CreateEmptyObjectLiteral:
                            acc = new JsPlainObject(this);
                            break;
                        case JsOpCode.CreateEmptyArrayLiteral:
                            acc = CreateArrayObject();
                            break;

                        case JsOpCode.CreateObjectLiteral:
                        case JsOpCode.CreateObjectLiteralWide:
                            pc = ref Unsafe.Add(
                                ref pc,
                                HandleCreateObjectLiteral(
                                    op,
                                    objectPool,
                                    ref bytecode,
                                    ref pc,
                                    ref acc
                                )
                            );
                            break;
                        case JsOpCode.CreateArrayLiteral:
                            intNum1 = Unsafe.ReadUnaligned<ushort>(ref pc);
                            pc = ref Unsafe.Add(ref pc, 2);
                            acc = CreateArrayLiteralFromPoolSlowPath(objectPool, intNum1);
                            break;
                        case JsOpCode.CreateArrayLiteralWithLength:
                            intNum1 = Unsafe.ReadUnaligned<ushort>(ref pc);
                            pc = ref Unsafe.Add(ref pc, 2);
                            acc = CreateArrayObjectWithLength(intNum1);
                            break;
                        case JsOpCode.InitializeNamedProperty:
                            {
                                reg = Unsafe.ReadUnaligned<ushort>(ref pc);
                                pc = ref Unsafe.Add(ref pc, 2);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                intNum1 = pc | (Unsafe.Add(ref pc, 1) << 8);
                                pc = ref Unsafe.Add(ref pc, 2);
                                obj = slotRef.AsObject();
                                obj.InitializeLiteralNamedSlot(intNum1, acc);
                            }
                            break;

                        case JsOpCode.LdaNamedProperty:
                        case JsOpCode.LdaNamedPropertyWide:
                            {
                                boolTemp = op == JsOpCode.LdaNamedPropertyWide;
                                reg = boolTemp ? Unsafe.ReadUnaligned<ushort>(ref pc) : pc;
                                pc = ref Unsafe.Add(ref pc, boolTemp ? 2 : 1);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                if (boolTemp)
                                {
                                    intNum1 = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    intNum1 = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                intNum1 = atomizedStringConstants[intNum1];

                                ValidateAtomizedNameConstant(
                                    intNum1,
                                    "LdaNamedProperty requires atomized name constant."
                                );
                                if (boolTemp)
                                {
                                    intNum2 = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    intNum2 = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                boolTemp = slotRef.TryGetObject(out obj);
                                if (!boolTemp)
                                    obj = ToObjectForPropertyAccessSlowPath(this, slotRef);
                                if (
                                    CanUseNamedPropertyIc(
                                        namedPropertyIcEntries,
                                        intNum2,
                                        boolTemp,
                                        obj!,
                                        intNum1,
                                        out slotInfo
                                    )
                                )
                                {
                                    acc = obj!.GetNamedByCachedSlotInfo(this, slotInfo);
                                    break;
                                }

                                if (
                                    prototypeNamedPropertyIcEntries is not null
                                    && prototypeNamedPropertyIcEntries[intNum2].Holder is not null
                                    && TryGetNamedPropertyFromPrototypeIc(
                                        prototypeNamedPropertyIcEntries,
                                        intNum2,
                                        boolTemp,
                                        obj!,
                                        intNum1,
                                        out acc
                                    )
                                )
                                    break;

                                if (
                                    boolTemp
                                        ? obj!.TryGetPropertyAtom(
                                            this,
                                            intNum1,
                                            out acc,
                                            out slotInfo
                                        )
                                        : obj!.TryGetPropertyAtomWithReceiverValue(
                                            this,
                                            slotRef,
                                            intNum1,
                                            out acc,
                                            out slotInfo
                                        )
                                )
                                    UpdateNamedPropertyIcAfterGet(
                                        namedPropertyIcEntries,
                                        prototypeNamedPropertyIcEntries,
                                        intNum2,
                                        boolTemp,
                                        obj!,
                                        intNum1,
                                        slotInfo
                                    );
                            }
                            break;
                        case JsOpCode.GetNamedPropertyFromSuper:
                        case JsOpCode.GetNamedPropertyFromSuperWide:
                            pc = ref Unsafe.Add(
                                ref pc,
                                HandleGetNamedPropertyFromSuper(
                                    op,
                                    fullStack,
                                    fp,
                                    atomizedStringConstants,
                                    ref bytecode,
                                    ref pc,
                                    ref acc
                                )
                            );
                            break;

                        case JsOpCode.LdaKeyedProperty:
                            {
                                operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(
                                    ref pc,
                                    ref operandOffset,
                                    operandScale
                                );
                                pc = ref Unsafe.Add(ref pc, operandOffset);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                if (!slotRef.TryGetObject(out obj))
                                    obj = ToObjectForPropertyAccessSlowPath(this, slotRef);

                                if (acc.IsInt32)
                                {
                                    intNum1 = acc.Int32Value;
                                    if (intNum1 >= 0)
                                    {
                                        JsValue keyedValue;
                                        if (
                                            obj is JsArray
                                            && Unsafe
                                                .As<JsArray>(obj)
                                                .TryGetDenseElement((uint)intNum1, out keyedValue)
                                        )
                                        {
                                            acc = keyedValue;
                                            break;
                                        }

                                        if (obj.TryGetElement((uint)intNum1, out keyedValue))
                                        {
                                            acc = keyedValue;
                                            break;
                                        }
                                    }
                                }

                                acc = LoadKeyedPropertySlowPath(this, obj, acc);
                            }
                            break;

                        case JsOpCode.StaNamedProperty:
                        case JsOpCode.StaNamedPropertyWide:
                            {
                                if (op == JsOpCode.StaNamedPropertyWide)
                                {
                                    reg = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    intNum1 = Unsafe.ReadUnaligned<ushort>(
                                        ref Unsafe.Add(ref pc, 2)
                                    );
                                    intNum2 = Unsafe.ReadUnaligned<ushort>(
                                        ref Unsafe.Add(ref pc, 4)
                                    );
                                    pc = ref Unsafe.Add(ref pc, 6);
                                }
                                else
                                {
                                    reg = pc;
                                    intNum1 = Unsafe.Add(ref pc, 1);
                                    intNum2 = Unsafe.Add(ref pc, 2);
                                    pc = ref Unsafe.Add(ref pc, 3);
                                }

                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                intNum1 = atomizedStringConstants[intNum1];
                                ValidateAtomizedNameConstant(
                                    intNum1,
                                    "StaNamedProperty requires atomized name constant."
                                );
                                boolTemp = slotRef.TryGetObject(out obj);
                                if (!boolTemp)
                                    obj = ToObjectForPropertyAccessSlowPath(this, slotRef);

                                if (
                                    CanUseNamedPropertyIc(
                                        namedPropertyIcEntries,
                                        intNum2,
                                        boolTemp,
                                        obj!,
                                        intNum1,
                                        out slotInfo
                                    )
                                )
                                {
                                    if (
                                        !obj!.SetNamedByCachedSlotInfo(this, slotInfo, acc)
                                        && currentFunc.IsStrict
                                    )
                                        ThrowTypeError(
                                            "ASSIGN_READONLY",
                                            "Cannot assign to read only property"
                                        );
                                    break;
                                }

                                if (
                                    !obj!.TrySetPropertyAtom(this, intNum1, acc, out slotInfo)
                                    && currentFunc.IsStrict
                                )
                                    ThrowTypeError(
                                        "ASSIGN_READONLY",
                                        "Cannot assign to read only property"
                                    );

                                if (CanCacheNamedPropertyResult(boolTemp, obj, slotInfo))
                                    UpdateNamedPropertyIc(
                                        namedPropertyIcEntries,
                                        intNum2,
                                        obj,
                                        intNum1,
                                        slotInfo
                                    );
                            }
                            break;

                        case JsOpCode.StaKeyedProperty:
                            {
                                operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(
                                    ref pc,
                                    ref operandOffset,
                                    operandScale
                                );
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                intNum2 = ReadScaledUnsignedOperand(
                                    ref pc,
                                    ref operandOffset,
                                    operandScale
                                );
                                pc = ref Unsafe.Add(ref pc, operandOffset);
                                if (!slotRef.TryGetObject(out obj))
                                    obj = ToObjectForPropertyAccessSlowPath(this, slotRef);
                                slotRef = ref Unsafe.Add(ref registerRef, intNum2);

                                if (slotRef.IsInt32)
                                {
                                    intNum1 = slotRef.Int32Value;
                                    if (intNum1 >= 0)
                                    {
                                        if (
                                            !obj.TrySetOwnElement((uint)intNum1, acc, out boolTemp)
                                            && boolTemp
                                        )
                                        {
                                            if (currentFunc.IsStrict)
                                                ThrowTypeError(
                                                    "ASSIGN_READONLY",
                                                    "Cannot assign to read only property"
                                                );
                                            break;
                                        }
                                        if (boolTemp)
                                            break;
                                    }
                                }

                                StoreKeyedPropertySlowPath(
                                    this,
                                    obj,
                                    slotRef,
                                    acc,
                                    currentFunc.IsStrict
                                );
                            }
                            break;
                        case JsOpCode.InitializeArrayElement:
                            {
                                reg = Unsafe.ReadUnaligned<ushort>(ref pc);
                                pc = ref Unsafe.Add(ref pc, 2);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                intNum1 = Unsafe.ReadUnaligned<ushort>(ref pc);
                                pc = ref Unsafe.Add(ref pc, 2);
                                if (
                                    slotRef.TryGetObject(out obj)
                                    && obj is JsArray
                                    && Unsafe
                                        .As<JsArray>(obj)
                                        .CanDefineElementAtIndex((uint)intNum1)
                                )
                                {
                                    Unsafe
                                        .As<JsArray>(obj)
                                        .InitializeLiteralElement((uint)intNum1, acc);
                                    break;
                                }

                                if (!slotRef.TryGetObject(out obj))
                                    obj = ToObjectForPropertyAccessSlowPath(this, slotRef);
                                StoreKeyedPropertySlowPath(
                                    this,
                                    obj!,
                                    JsValue.FromInt32(intNum1),
                                    acc,
                                    currentFunc.IsStrict
                                );
                            }
                            break;
                        case JsOpCode.DefineOwnKeyedProperty:
                        case JsOpCode.DefineOwnKeyedPropertyNoName:
                            {
                                operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(
                                    ref pc,
                                    ref operandOffset,
                                    operandScale
                                );
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                intNum2 = ReadScaledUnsignedOperand(
                                    ref pc,
                                    ref operandOffset,
                                    operandScale
                                );
                                pc = ref Unsafe.Add(ref pc, operandOffset);

                                if (!slotRef.TryGetObject(out obj))
                                    obj = ToObjectForPropertyAccessSlowPath(this, slotRef);

                                slotRef = ref Unsafe.Add(ref registerRef, intNum2);
                                PropertyInitializationOperations.DefineOwnDataPropertyByKey(
                                    this,
                                    obj!,
                                    slotRef,
                                    acc,
                                    op == JsOpCode.DefineOwnKeyedProperty
                                );
                            }
                            break;
                        case JsOpCode.ForInEnumerate:
                            {
                                acc = ForInEnumerate(Unsafe.Add(ref registerRef, pc));
                                pc = ref Unsafe.Add(ref pc, 1);
                            }
                            break;
                        case JsOpCode.ForInNext:
                            {
                                acc = ForInNext(Unsafe.Add(ref registerRef, pc));
                                pc = ref Unsafe.Add(ref pc, 1);
                            }
                            break;
                        case JsOpCode.ForInStep:
                            {
                                ForInStep(Unsafe.Add(ref registerRef, pc));
                                pc = ref Unsafe.Add(ref pc, 1);
                            }
                            break;
                        case JsOpCode.InitPrivateField:
                            pc = ref Unsafe.Add(
                                ref pc,
                                ExecuteInitPrivateField(
                                    currentFunc,
                                    ref registerRef,
                                    ref pc,
                                    ref acc
                                )
                            );
                            break;
                        case JsOpCode.InitPrivateAccessor:
                            pc = ref Unsafe.Add(
                                ref pc,
                                ExecuteInitPrivateAccessor(
                                    currentFunc,
                                    ref registerRef,
                                    ref pc,
                                    ref acc
                                )
                            );
                            break;
                        case JsOpCode.InitPrivateMethod:
                            pc = ref Unsafe.Add(
                                ref pc,
                                ExecuteInitPrivateMethod(
                                    currentFunc,
                                    ref registerRef,
                                    ref pc,
                                    ref acc
                                )
                            );
                            break;
                        case JsOpCode.GetPrivateField:
                            pc = ref Unsafe.Add(
                                ref pc,
                                ExecuteGetPrivateField(
                                    currentFunc,
                                    ref registerRef,
                                    ref pc,
                                    ref acc
                                )
                            );
                            break;
                        case JsOpCode.SetPrivateField:
                            pc = ref Unsafe.Add(
                                ref pc,
                                ExecuteSetPrivateField(
                                    currentFunc,
                                    ref registerRef,
                                    ref pc,
                                    ref acc
                                )
                            );
                            break;

                        case JsOpCode.Add:
                        case JsOpCode.Sub:
                        case JsOpCode.Mul:
                        case JsOpCode.Div:
                        case JsOpCode.Mod:
                        case JsOpCode.Exp:
                        {
                            AssertValidOperandScale(operandScale);
                            operandOffset = 0;
                            reg = ReadScaledUnsignedOperand(
                                ref pc,
                                ref operandOffset,
                                operandScale
                            );
                            slotRef = ref Unsafe.Add(ref registerRef, reg);
                            ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale); // slot
                            pc = ref Unsafe.Add(ref pc, operandOffset);

                            if (op is JsOpCode.Add or JsOpCode.Sub or JsOpCode.Mul)
                                if (slotRef.IsInt32 && acc.IsInt32)
                                {
                                    intNum1 = slotRef.Int32Value;
                                    intNum2 = acc.Int32Value;
                                    longNum = op switch
                                    {
                                        JsOpCode.Add => (long)intNum1 + intNum2,
                                        JsOpCode.Sub => (long)intNum1 - intNum2,
                                        JsOpCode.Mul => (long)intNum1 * intNum2,
                                        _ => 0L,
                                    };
                                    if (longNum <= int.MaxValue && longNum >= int.MinValue)
                                    {
                                        acc = JsValue.FromInt32((int)longNum);
                                        break;
                                    }

                                    acc = new(longNum);
                                    break;
                                }

                            if (
                                op == JsOpCode.Sub
                                && Intrinsics.TryGetDateSubtraction(slotRef, acc, ref acc)
                            )
                                break;

                            if (slotRef.IsFloat64)
                            {
                                num1 = slotRef.FastFloat64Value;
                                if (acc.IsFloat64)
                                    num2 = acc.FastFloat64Value;
                                else if (acc.IsInt32)
                                    num2 = acc.Int32Value;
                                else
                                {
                                    acc = HandleArithmeticNonNumberSlowPath(this, op, slotRef, acc);
                                    break;
                                }
                            }
                            else if (slotRef.IsInt32)
                            {
                                num1 = slotRef.Int32Value;
                                if (acc.IsFloat64)
                                    num2 = acc.FastFloat64Value;
                                else if (acc.IsInt32)
                                    num2 = acc.Int32Value;
                                else
                                {
                                    acc = HandleArithmeticNonNumberSlowPath(this, op, slotRef, acc);
                                    break;
                                }
                            }
                            else
                            {
                                acc = HandleArithmeticNonNumberSlowPath(this, op, slotRef, acc);
                                break;
                            }

                            num1 = op switch
                            {
                                JsOpCode.Add => num1 + num2,
                                JsOpCode.Sub => num1 - num2,
                                JsOpCode.Mul => num1 * num2,
                                JsOpCode.Div => num1 / num2,
                                JsOpCode.Mod => num1 % num2,
                                JsOpCode.Exp => NumberExponentiate(num1, num2),
                                _ => 0, // throw makes no sense, and throw or eliminating default cause deoptimization, so just return 0 which will be ignored anyway.
                            };
                            acc = new(num1);
                            break;
                        }

                        case JsOpCode.AddSmi:
                        case JsOpCode.SubSmi:
                            {
                                intNum1 = (sbyte)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                pc = ref Unsafe.Add(ref pc, 1); // slot
                                if (acc.IsInt32)
                                {
                                    longNum =
                                        (long)acc.Int32Value
                                        + intNum1 * (op == JsOpCode.AddSmi ? 1 : -1);
                                    if (longNum <= int.MaxValue && longNum >= int.MinValue)
                                    {
                                        acc = JsValue.FromInt32((int)longNum);
                                        break;
                                    }

                                    acc = new(longNum);
                                    break;
                                }

                                if (acc.IsFloat64)
                                {
                                    ref var num = ref Unsafe.As<JsValue, double>(ref acc);
                                    num = op == JsOpCode.AddSmi ? num + intNum1 : num - intNum1;
                                    break;
                                }

                                if (op == JsOpCode.AddSmi)
                                    acc = AddSmiSlowPath(this, acc, intNum1);
                                else
                                    acc = HandleArithmeticNonNumberSmiSlowPath(
                                        this,
                                        JsOpCode.SubSmi,
                                        acc,
                                        intNum1
                                    );
                            }
                            break;
                        case JsOpCode.Inc:
                        case JsOpCode.Dec:
                            intNum1 = op == JsOpCode.Inc ? 1 : -1;
                            if (acc.IsInt32)
                            {
                                longNum = (long)acc.Int32Value + intNum1;
                                if (longNum <= int.MaxValue && longNum >= int.MinValue)
                                    acc = JsValue.FromInt32((int)longNum);
                                else
                                    acc = new(longNum);
                            }
                            else if (acc.IsFloat64)
                            {
                                acc = new(acc.FastFloat64Value + intNum1);
                            }
                            else
                            {
                                this.acc = acc;
                                acc =
                                    acc.U == JsValue.JsBigIntBits
                                        ? IncrementBigIntSlowPath(acc, intNum1)
                                        : IncrementSlowPath(this, acc, intNum1);
                            }

                            break;
                        case JsOpCode.MulSmi:
                            {
                                intNum1 = (sbyte)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                pc = ref Unsafe.Add(ref pc, 1); // slot
                                if (acc.IsInt32)
                                    acc = Mul(acc, intNum1);
                                else if (acc.IsNumber)
                                    acc = new(acc.FastNumberValue * intNum1);
                                else
                                    acc = HandleArithmeticNonNumberSmiSlowPath(
                                        this,
                                        JsOpCode.MulSmi,
                                        acc,
                                        intNum1
                                    );
                            }
                            break;
                        case JsOpCode.ModSmi:
                            {
                                // imm operand in intNum1
                                intNum1 = (sbyte)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                pc = ref Unsafe.Add(ref pc, 1); // slot
                                if (acc.IsInt32 && intNum1 != 0)
                                {
                                    intNum2 = acc.Int32Value;
                                    intNum1 = intNum2 % intNum1;
                                    acc =
                                        intNum1 == 0 && intNum2 < 0
                                            ? new(-0.0d)
                                            : JsValue.FromInt32(intNum1);
                                }
                                else if (acc.IsFloat64)
                                {
                                    acc = new(acc.FastFloat64Value % intNum1);
                                }
                                else
                                {
                                    acc = HandleArithmeticNonNumberSmiSlowPath(
                                        this,
                                        JsOpCode.ModSmi,
                                        acc,
                                        intNum1
                                    );
                                }
                            }
                            break;
                        case JsOpCode.ExpSmi:
                            {
                                // imm operand in intNum1
                                intNum1 = (sbyte)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                if (acc.IsNumber)
                                    acc = new(NumberExponentiate(acc.FastNumberValue, intNum1));
                                else
                                    acc = HandleArithmeticNonNumberSmiSlowPath(
                                        this,
                                        JsOpCode.ExpSmi,
                                        acc,
                                        intNum1
                                    );

                                pc = ref Unsafe.Add(ref pc, 1); // slot
                            }
                            break;

                        case JsOpCode.LogicalNot:
                            acc = ToBoolean(acc) ? JsValue.False : JsValue.True;
                            break;
                        case JsOpCode.TypeOf:
                            acc = TypeOfValue(acc);
                            break;
                        case JsOpCode.ToNumber:
                            {
                                if (acc.IsNumber)
                                {
                                    // already numeric
                                }
                                else
                                {
                                    this.acc = acc;
                                    acc = new(this.ToNumberSlowPath(acc));
                                }
                            }
                            break;
                        case JsOpCode.ToString:
                            if (!acc.IsString)
                            {
                                this.acc = acc;
                                acc = JsValue.FromString(this.ToJsStringSlowPath(acc));
                            }
                            break;
                        case JsOpCode.ToNumeric:
                            {
                                if (acc.IsNumeric)
                                {
                                    // already numeric
                                }
                                else
                                {
                                    this.acc = acc;
                                    acc = this.ToNumericSlowPath(acc);
                                }
                            }
                            break;
                        case JsOpCode.Negate:
                            {
                                if (acc.U == JsValue.JsBigIntBits)
                                {
                                    acc = acc.NegateBigInt();
                                    break;
                                }

                                if (acc.IsInt32)
                                {
                                    intNum1 = acc.Int32Value;
                                    if (intNum1 == 0)
                                    {
                                        acc = new(-0d);
                                        break;
                                    }

                                    if (intNum1 != int.MinValue)
                                    {
                                        acc = JsValue.FromInt32(-intNum1);
                                        break;
                                    }

                                    acc = new(-(double)int.MinValue);
                                }
                                else if (acc.IsFloat64)
                                {
                                    acc = new(-Unsafe.BitCast<ulong, double>(acc.U));
                                    break;
                                }

                                acc = JsValue.NaN;
                            }
                            break;
                        case JsOpCode.BitwiseNot:
                            {
                                if (acc.IsInt32)
                                {
                                    acc = JsValue.FromInt32(~acc.Int32Value);
                                    break;
                                }

                                if (acc.U == JsValue.JsBigIntBits)
                                {
                                    acc = BitwiseNotBigIntSlowPath(acc);
                                    break;
                                }

                                acc = JsValue.FromInt32(~ToInt32SlowPath(this, acc));
                            }
                            break;

                        case JsOpCode.TestLessThan:
                        case JsOpCode.TestGreaterThan:
                        case JsOpCode.TestLessThanOrEqual:
                        case JsOpCode.TestGreaterThanOrEqual:
                            {
                                AssertValidOperandScale(operandScale);
                                operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(
                                    ref pc,
                                    ref operandOffset,
                                    operandScale
                                );
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale); // slot
                                pc = ref Unsafe.Add(ref pc, operandOffset);

                                if (slotRef.IsNumber && acc.IsNumber)
                                {
                                    num1 = slotRef.FastNumberValue;
                                    num2 = acc.FastNumberValue;
                                    acc = op switch
                                    {
                                        JsOpCode.TestLessThan => num1 < num2,
                                        JsOpCode.TestGreaterThan => num1 > num2,
                                        JsOpCode.TestLessThanOrEqual => num1 <= num2,
                                        JsOpCode.TestGreaterThanOrEqual => num1 >= num2,
                                        _ => false,
                                    }
                                        ? JsValue.True
                                        : JsValue.False;
                                    break;
                                }

                                acc = HandleComparisonSlowPath(this, op, slotRef, acc);
                            }
                            break;

                        case JsOpCode.TestEqual:
                        case JsOpCode.TestNotEqual:
                        case JsOpCode.TestEqualStrict:
                        {
                            AssertValidOperandScale(operandScale);
                            operandOffset = 0;
                            reg = ReadScaledUnsignedOperand(
                                ref pc,
                                ref operandOffset,
                                operandScale
                            );
                            slotRef = ref Unsafe.Add(ref registerRef, reg);
                            ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale); // slot
                            pc = ref Unsafe.Add(ref pc, operandOffset);
                            acc = op switch
                            {
                                JsOpCode.TestEqualStrict => StrictEquals(slotRef, acc),
                                JsOpCode.TestEqual => AbstractEquals(this, slotRef, acc),
                                JsOpCode.TestNotEqual => !AbstractEquals(this, slotRef, acc),
                                _ => false,
                            }
                                ? JsValue.True
                                : JsValue.False;
                            break;
                        }
                        case JsOpCode.TestInstanceOf:
                        {
                            AssertValidOperandScale(operandScale);
                            operandOffset = 0;
                            reg = ReadScaledUnsignedOperand(
                                ref pc,
                                ref operandOffset,
                                operandScale
                            );
                            slotRef = ref Unsafe.Add(ref registerRef, reg);
                            ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale); // slot
                            pc = ref Unsafe.Add(ref pc, operandOffset);
                            this.acc = acc;
                            acc = InstanceOfSlowPath(this, slotRef, acc)
                                ? JsValue.True
                                : JsValue.False;
                            break;
                        }
                        case JsOpCode.TestIn:
                        {
                            AssertValidOperandScale(operandScale);
                            operandOffset = 0;
                            reg = ReadScaledUnsignedOperand(
                                ref pc,
                                ref operandOffset,
                                operandScale
                            );
                            slotRef = ref Unsafe.Add(ref registerRef, reg);
                            ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale); // slot
                            pc = ref Unsafe.Add(ref pc, operandOffset);
                            this.acc = acc;
                            acc = InOperatorSlowPath(this, slotRef, acc)
                                ? JsValue.True
                                : JsValue.False;
                            break;
                        }
                        case JsOpCode.TestLessThanSmi:
                        case JsOpCode.TestGreaterThanSmi:
                        case JsOpCode.TestLessThanOrEqualSmi:
                        case JsOpCode.TestGreaterThanOrEqualSmi:
                            {
                                num1 = (sbyte)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                pc = ref Unsafe.Add(ref pc, 1); // slot

                                if (acc.IsNumber)
                                {
                                    num2 = acc.FastNumberValue;
                                    acc = op switch
                                    {
                                        JsOpCode.TestLessThanSmi => num2 < num1,
                                        JsOpCode.TestGreaterThanSmi => num2 > num1,
                                        JsOpCode.TestLessThanOrEqualSmi => num2 <= num1,
                                        JsOpCode.TestGreaterThanOrEqualSmi => num2 >= num1,
                                        _ => false,
                                    }
                                        ? JsValue.True
                                        : JsValue.False;
                                    break;
                                }

                                acc = HandleComparisonSmiSlowPath(this, op, acc, num1);
                            }
                            break;

                        case JsOpCode.BitwiseAnd:
                        case JsOpCode.BitwiseOr:
                        case JsOpCode.BitwiseXor:
                        case JsOpCode.ShiftLeft:
                        case JsOpCode.ShiftRight:
                        case JsOpCode.ShiftRightLogical:
                            {
                                AssertValidOperandScale(operandScale);
                                operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(
                                    ref pc,
                                    ref operandOffset,
                                    operandScale
                                );
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale); // slot
                                pc = ref Unsafe.Add(ref pc, operandOffset);

                                if (slotRef.IsInt32 && acc.IsInt32)
                                {
                                    intNum1 = slotRef.Int32Value;
                                    intNum2 = acc.Int32Value;
                                    if (op == JsOpCode.ShiftRightLogical)
                                    {
                                        longNum = (uint)intNum1 >> (intNum2 & 0x1F);
                                        acc =
                                            longNum <= int.MaxValue
                                                ? JsValue.FromInt32((int)longNum)
                                                : new((double)longNum);
                                        break;
                                    }

                                    acc = JsValue.FromInt32(
                                        op switch
                                        {
                                            JsOpCode.BitwiseAnd => intNum1 & intNum2,
                                            JsOpCode.BitwiseOr => intNum1 | intNum2,
                                            JsOpCode.BitwiseXor => intNum1 ^ intNum2,
                                            JsOpCode.ShiftLeft => intNum1 << (intNum2 & 0x1F),
                                            JsOpCode.ShiftRight => intNum1 >> (intNum2 & 0x1F),
                                            _ => 0,
                                        }
                                    );
                                    break;
                                }

                                if (
                                    slotRef.U == JsValue.JsBigIntBits
                                    && acc.U == JsValue.JsBigIntBits
                                )
                                {
                                    acc = HandleBigIntBitwiseFastSlowPath(op, slotRef, acc);
                                    break;
                                }

                                acc = HandleBitwiseSlowPath(this, op, slotRef, acc);
                            }
                            break;
                        case JsOpCode.Jump:
                            {
                                pc = ref Unsafe.Add(
                                    ref pc,
                                    2 + Unsafe.ReadUnaligned<short>(ref pc)
                                );
                            }
                            break;
                        case JsOpCode.JumpIfTrue:
                        case JsOpCode.JumpIfFalse:
                        case JsOpCode.JumpIfToBooleanTrue:
                        case JsOpCode.JumpIfToBooleanFalse:
                        case JsOpCode.JumpIfNull:
                        case JsOpCode.JumpIfUndefined:
                        case JsOpCode.JumpIfNotUndefined:
                        case JsOpCode.JumpIfJsReceiver:
                            {
                                if (EvaluateJumpCondition(op, acc))
                                    pc = ref Unsafe.Add(
                                        ref pc,
                                        2 + Unsafe.ReadUnaligned<short>(ref pc)
                                    );
                                else
                                    pc = ref Unsafe.Add(ref pc, 2);
                            }
                            break;
                        case JsOpCode.PushTry:
                            {
                                pc = ref Unsafe.Add(ref pc, 2);
                                PushExceptionHandler(
                                    fp,
                                    GetPcOffset(ref bytecode, ref pc)
                                        + Unsafe.ReadUnaligned<short>(
                                            ref Unsafe.Subtract(ref pc, 2)
                                        ),
                                    StackTop,
                                    GetCurrentContext(fullStack)
                                );
                            }
                            break;
                        case JsOpCode.PopTry:
                            PopCurrentExceptionHandlerForFrame(fp);
                            break;

                        case JsOpCode.CallUndefinedReceiver:
                        case JsOpCode.CallProperty:
                        case JsOpCode.Construct:
                            {
                                operandOffset = 0;
                                AssertValidOperandScale(operandScale);
                                reg = ReadScaledUnsignedOperand(
                                    ref pc,
                                    ref operandOffset,
                                    operandScale
                                );
                                slotRef = ref Unsafe.Add(ref registerRef, reg);

                                var okojoCallee = slotRef.Obj as JsFunction;
                                if (okojoCallee is not null)
                                {
                                    reg = -1;
                                    if (
                                        op != JsOpCode.Construct
                                        && op != JsOpCode.CallUndefinedReceiver
                                    )
                                        reg = ReadScaledUnsignedOperand(
                                            ref pc,
                                            ref operandOffset,
                                            operandScale
                                        );

                                    intNum1 = ReadScaledUnsignedOperand(
                                        ref pc,
                                        ref operandOffset,
                                        operandScale
                                    );
                                    intNum2 = ReadScaledUnsignedOperand(
                                        ref pc,
                                        ref operandOffset,
                                        operandScale
                                    );
                                    pc = ref Unsafe.Add(ref pc, operandOffset);
                                    this.acc = acc;
                                    if (
                                        (
                                            Agent.ExecutionCheckpointHookBits
                                            & (int)ExecutionCheckpointHooks.Call
                                        ) != 0
                                    )
                                        EmitExecutionBoundaryCheckpoint(
                                            fullStack,
                                            fp,
                                            ExecutionCheckpointKind.Call,
                                            ref bytecode,
                                            ref opcodePc
                                        );
                                    if (okojoCallee.NamedPropertyLayout.Owner != this)
                                    {
                                        DispatchCrossRealm(
                                            okojoCallee,
                                            reg,
                                            intNum1,
                                            intNum2,
                                            op == JsOpCode.Construct,
                                            GetPcOffset(ref bytecode, ref pc),
                                            ref registerRef,
                                            ref acc
                                        );
                                    }
                                    else if (
                                        TryDispatchVmStackInvocation(
                                            okojoCallee,
                                            reg,
                                            intNum1,
                                            intNum2,
                                            op == JsOpCode.Construct,
                                            op != JsOpCode.Construct
                                                && currentFunc.IsStrict
                                                && pc == (byte)JsOpCode.Return,
                                            GetPcOffset(ref bytecode, ref pc),
                                            ref currentFunc,
                                            ref registerRef,
                                            ref acc
                                        )
                                    )
                                    {
                                        startPc = 0;
                                        goto ReloadFrame;
                                    }
                                }
                                else
                                {
                                    ThrowNonCallable(op == JsOpCode.Construct);
                                }
                            }
                            break;
                        case JsOpCode.CallRuntime:
                            {
                                this.acc = acc;
                                pc = ref Unsafe.Add(
                                    ref pc,
                                    CallRuntime(
                                        this,
                                        currentFunc,
                                        ref bytecode,
                                        ref pc,
                                        ref registerRef,
                                        fp,
                                        operandScale,
                                        ref acc
                                    )
                                );

                                [MethodImpl(MethodImplOptions.NoInlining)]
                                static int CallRuntime(
                                    JsRealm realm,
                                    JsBytecodeFunction currentFunc,
                                    ref byte bytecode,
                                    ref byte pc,
                                    ref JsValue registerRef,
                                    int fp,
                                    BytecodeInfo.OperandScale operandScale,
                                    ref JsValue acc
                                )
                                {
                                    var startOffset = GetPcOffset(ref bytecode, ref pc);
                                    var opcodePc = startOffset - 1;
                                    var pcOffset = startOffset;
                                    AssertValidOperandScale(operandScale);
                                    var runtimeId = ReadByteOrU16(
                                        ref bytecode,
                                        ref pcOffset,
                                        operandScale != BytecodeInfo.OperandScale.Single
                                    );
                                    var argStart = ReadByteOrU16(
                                        ref bytecode,
                                        ref pcOffset,
                                        operandScale != BytecodeInfo.OperandScale.Single
                                    );
                                    var argCount = ReadByteOrU16(
                                        ref bytecode,
                                        ref pcOffset,
                                        operandScale != BytecodeInfo.OperandScale.Single
                                    );
                                    try
                                    {
                                        SRuntimeHandlers[runtimeId]!(
                                            realm,
                                            currentFunc.Script,
                                            opcodePc,
                                            ref registerRef,
                                            fp,
                                            argStart,
                                            argCount,
                                            ref acc
                                        );
                                    }
                                    catch (Exception ex)
                                        when (ex
                                                is IndexOutOfRangeException
                                                    or InvalidOperationException
                                        )
                                    {
                                        throw new InvalidOperationException(
                                            $"CallRuntime failed: pc={opcodePc}, runtimeId={runtimeId}, argStart={argStart}, argCount={argCount}, scale={operandScale}",
                                            ex
                                        );
                                    }

                                    return pcOffset - startOffset;
                                }
                            }
                            break;
                        case JsOpCode.SwitchOnSmi:
                            {
                                pc = ref Unsafe.Add(
                                    ref pc,
                                    HandleSwitchOnSmi(ref bytecode, currentFunc.Script, ref pc, acc)
                                );
                            }
                            break;
                        case JsOpCode.SwitchOnGeneratorState:
                            {
                                pc = ref Unsafe.Add(
                                    ref pc,
                                    HandleSwitchOnGeneratorState(
                                        ref bytecode,
                                        currentFunc.Script,
                                        ref pc,
                                        fp
                                    )
                                );
                            }
                            break;
                        case JsOpCode.SuspendGenerator:
                        {
                            if (
                                HandleSuspendGenerator(
                                    ref bytecode,
                                    fullStack,
                                    ref registerRef,
                                    stopAtCallerFp,
                                    ref fp,
                                    ref pc,
                                    ref acc,
                                    out intNum1
                                ) == GeneratorDispatchResult.ReturnFromRun
                            )
                            {
                                pc = ref Unsafe.Add(ref pc, intNum1);
                                return;
                            }

                            pc = ref Unsafe.Add(ref pc, intNum1);
                            goto ReloadFrame;
                        }
                        case JsOpCode.ResumeGenerator:
                            {
                                switch (
                                    HandleResumeGenerator(
                                        ref bytecode,
                                        fullStack,
                                        ref registerRef,
                                        stopAtCallerFp,
                                        ref fp,
                                        ref pc,
                                        ref acc,
                                        out intNum1
                                    )
                                )
                                {
                                    case GeneratorDispatchResult.ReturnFromRun:
                                        pc = ref Unsafe.Add(ref pc, intNum1);
                                        return;
                                    case GeneratorDispatchResult.ReloadFrame:
                                        pc = ref Unsafe.Add(ref pc, intNum1);
                                        goto ReloadFrame;
                                }

                                pc = ref Unsafe.Add(ref pc, intNum1);
                            }
                            break;
                        case JsOpCode.Throw:
                            ThrowJsValue(acc);
                            break;

                        case JsOpCode.Return:
                        {
                            if (fp == 0)
                                return;

                            if (
                                (
                                    Agent.ExecutionCheckpointHookBits
                                    & (int)ExecutionCheckpointHooks.Return
                                ) != 0
                            )
                            {
                                this.acc = acc;
                                EmitExecutionBoundaryCheckpoint(
                                    fullStack,
                                    fp,
                                    ExecutionCheckpointKind.Return,
                                    ref bytecode,
                                    ref opcodePc
                                );
                            }

                            ref var callFrame = ref Unsafe.As<JsValue, CallFrame>(
                                ref Unsafe.Subtract(ref registerRef, HeaderSize)
                            );
                            var generatorReturn =
                                callFrame.FrameKind == CallFrameKind.GeneratorFrame;
                            boolTemp = (callFrame.Flags & CallFrameFlag.IsConstructorCall) != 0;
                            var constructorThis = callFrame.ThisValue;
                            var constructorFlags = callFrame.Flags;

                            if (generatorReturn)
                            {
                                var fastForOfStepMode = false;
                                var asyncDriver = false;
                                if (TryGetActiveGeneratorForFrame(fp, out var generator))
                                {
                                    asyncDriver = generator.IsAsyncDriver;
                                    fastForOfStepMode = generator.FastForOfStepMode;
                                    if (fastForOfStepMode)
                                        generator.FastForOfStepDone = true;
                                    FinalizeGenerator(generator);
                                    ClearActiveGeneratorForFrame(fp);
                                }

                                if (!fastForOfStepMode && !asyncDriver)
                                    acc = CreateIteratorResultObject(acc, true);
                            }

                            intNum1 = StackTop;
                            StackTop = fp;
                            RemoveExceptionHandlersForFrame(fp);
                            fp = callFrame.CallerFp;
                            startPc = callFrame.CallerPc;
                            fullStack[StackTop..intNum1].Fill(JsValue.Undefined); // Clear registers of the frame being popped to avoid keeping references to objects longer than needed.

                            if (!generatorReturn && boolTemp)
                                acc = CompleteConstructResult(
                                    acc,
                                    constructorThis,
                                    constructorFlags
                                );

                            if (stopAtCallerFp >= 0 && fp == stopAtCallerFp)
                                return;

                            if (fp == 0 && startPc == 0)
                                return;

                            goto ReloadFrame;
                        }
                        case JsOpCode.Debugger:
                        {
                            this.acc = acc;
                            if (
                                (
                                    Agent.ExecutionCheckpointHookBits
                                    & (
                                        (int)ExecutionCheckpointHooks.DebuggerStatement
                                        | (int)ExecutionCheckpointHooks.Breakpoint
                                    )
                                ) != 0
                                && HandleDebuggerOpcode(fullStack, fp, ref bytecode, ref opcodePc)
                            )
                                pc = ref opcodePc;
                            break;
                        }
                        default:
                        {
                            throw NotImplemented(op);

                            static Exception NotImplemented(JsOpCode op)
                            {
                                return new NotImplementedException($"Opcode {op} not implemented.");
                            }
                        }
                    }

                    operandScale = BytecodeInfo.OperandScale.Single;
                }
                catch (Exception e)
                {
                    this.acc = acc;
                    if (
                        TryCatchRunCoreException(
                            e,
                            ref opcodePc,
                            stopAtCallerFp,
                            ref startPc,
                            out var newEx,
                            ref acc
                        )
                    )
                        goto ReloadFrame;

                    if (newEx is not null)
                        throw newEx;
                    throw;
                }
            }
        }
        finally
        {
            this.acc = acc;
            managedRunDepth--;
        }
    }
}
