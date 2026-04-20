using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Okojo.Bytecode;

namespace Okojo.Runtime;

public sealed partial class JsRealm
{
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
    private static int ReadScaledUnsignedOperand(ref byte pc, ref int operandOffset,
        BytecodeInfo.OperandScale operandScale)
    {
        if (operandScale == BytecodeInfo.OperandScale.Single)
            return Unsafe.Add(ref pc, operandOffset++);
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
            operandScale is BytecodeInfo.OperandScale.Single or BytecodeInfo.OperandScale.Wide
                or BytecodeInfo.OperandScale.ExtraWide);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ThrowInvalidOperandScale(BytecodeInfo.OperandScale operandScale)
    {
        throw new ArgumentOutOfRangeException(nameof(operandScale));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckExecutionSlowPath(Span<JsValue> fullStack, int fp, ref byte bytecode, ref byte opcodePc,
        JsOpCode currentOpcode, ref ulong nextCheck)
    {
        Agent.ExecutionCheckPolicy.CheckSlowPath(this, fullStack, fp, ref bytecode, ref opcodePc, currentOpcode,
            ref nextCheck);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckDebuggerSlowPath(Span<JsValue> fullStack, int fp, ref byte bytecode, ref byte opcodePc,
        bool breakpointHit)
    {
        var executionCheckPolicy = Agent.ExecutionCheckPolicy;
        if (!executionCheckPolicy.HasDebugger)
            return;

        if (breakpointHit)
            executionCheckPolicy.EmitBreakpointCheckpoint(this, fullStack, fp, ref bytecode, ref opcodePc);
        else
            executionCheckPolicy.EmitDebuggerStatementCheckpoint(this, fullStack, fp, ref bytecode, ref opcodePc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool HandleDebuggerOpcode(Span<JsValue> fullStack, int fp, ref byte bytecode, ref byte opcodePc)
    {
        var currentBytecodeFunc = (CurrentCallFrame.Function as JsBytecodeFunction)!;
        var breakpointHit = Agent.TryRestoreBreakpointForHit(currentBytecodeFunc.Script,
            GetPcOffset(ref bytecode, ref opcodePc), out _, out _, out _);
        if (breakpointHit &&
            (Agent.ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.Breakpoint) == 0)
            return true;

        if (breakpointHit ||
            (Agent.ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.DebuggerStatement) != 0)
            CheckDebuggerSlowPath(fullStack, fp, ref bytecode, ref opcodePc, breakpointHit);

        return breakpointHit;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EmitExecutionBoundaryCheckpoint(
        Span<JsValue> fullStack,
        int fp,
        ExecutionCheckpointKind kind,
        ref byte bytecode,
        ref byte pc)
    {
        Agent.ExecutionCheckPolicy.EmitBoundaryCheckpoint(this, fullStack, fp, kind, ref bytecode, ref pc);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetPcOffset(ref byte bytecode, ref byte pc)
    {
        return checked((int)Unsafe.ByteOffset(ref bytecode, ref pc));
    }

    [Conditional("DEBUG")]
    private static void ValidateAtomizedNameConstant(int atom, string message)
    {
        if (atom < 0)
            throw new InvalidOperationException(message);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TypeOfGlobal(JsOpCode op, JsScript script, ref byte bytecode, ref int pc,
        int[] atomizedStringConstants,
        ref JsValue acc)
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
    private int TypeOfGlobal(JsOpCode op, JsScript script, ref byte bytecode, ref byte pc,
        int[] atomizedStringConstants,
        ref JsValue acc)
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
        return ResolvePrivateBrandToken(currentFunc, brandId) ?? Agent.GetLegacyPrivateBrandToken(brandId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsObject ResolvePrivateInitBrandToken(
        JsObject target,
        JsBytecodeFunction currentFunc,
        int brandId)
    {
        return ResolvePrivateBrandToken(currentFunc, brandId) ??
               (target is JsFunction functionTarget
                   ? functionTarget
                   : Agent.GetLegacyPrivateBrandToken(brandId));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InitPrivateFieldValue(
        JsObject target,
        JsBytecodeFunction currentFunc,
        int brandId,
        int slotIndex,
        in JsValue value)
    {
        if (!target.IsExtensible)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Cannot add private member to non-extensible object");

        Agent.InitPrivateField(target, ResolvePrivateInitBrandToken(target, currentFunc, brandId), slotIndex, value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InitPrivateAccessorValue(
        JsObject target,
        JsBytecodeFunction currentFunc,
        int brandId,
        int slotIndex,
        JsFunction? getter,
        JsFunction? setter)
    {
        if (!target.IsExtensible)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Cannot add private member to non-extensible object");

        Agent.InitPrivateAccessor(target, ResolvePrivateInitBrandToken(target, currentFunc, brandId), slotIndex, getter,
            setter);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InitPrivateMethodValue(
        JsObject target,
        JsBytecodeFunction currentFunc,
        int brandId,
        int slotIndex,
        JsFunction method)
    {
        if (!target.IsExtensible)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Cannot add private member to non-extensible object");

        Agent.InitPrivateMethod(target, ResolvePrivateInitBrandToken(target, currentFunc, brandId), slotIndex, method);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryGetPrivateSlotValue(
        JsObject target,
        JsBytecodeFunction currentFunc,
        int brandId,
        int slotIndex,
        out JsValue value)
    {
        return Agent.TryGetPrivateSlot(target, ResolvePrivateStorageBrandToken(currentFunc, brandId), slotIndex,
            out value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TrySetPrivateFieldValue(
        JsObject target,
        JsBytecodeFunction currentFunc,
        int brandId,
        int slotIndex,
        in JsValue value)
    {
        return Agent.TrySetPrivateField(target, ResolvePrivateStorageBrandToken(currentFunc, brandId), slotIndex,
            value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T ThrowPrivateAccessorTypeError<T>(string detailCode, string message)
    {
        throw TypeError(detailCode, message);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowPrivateFieldReadBrandError(JsBytecodeFunction currentFunc, int brandId, int slotIndex)
    {
        var privateName = GetPrivateFieldDebugNameOrDefault(currentFunc.Script, brandId, slotIndex);
        throw TypeErrorInRealm(currentFunc.Realm, "PRIVATE_FIELD_BRAND",
            $"Cannot read private member {privateName} from an object whose class did not declare it");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowPrivateFieldWriteBrandError(JsBytecodeFunction currentFunc, int brandId, int slotIndex)
    {
        var privateName = GetPrivateFieldDebugNameOrDefault(currentFunc.Script, brandId, slotIndex);
        throw TypeErrorInRealm(currentFunc.Realm, "PRIVATE_FIELD_BRAND",
            $"Cannot write private member {privateName} from an object whose class did not declare it");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ExecuteInitPrivateField(
        JsBytecodeFunction currentFunc,
        ref JsValue registers,
        ref byte pc)
    {
        int objReg = pc;
        ref var targetRef = ref Unsafe.Add(ref registers, objReg);
        if (!targetRef.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        int valueReg = Unsafe.Add(ref pc, 1);
        var brandId = Unsafe.Add(ref pc, 2) | (Unsafe.Add(ref pc, 3) << 8);
        var slotIndex = Unsafe.Add(ref pc, 4) | (Unsafe.Add(ref pc, 5) << 8);
        var value = Unsafe.Add(ref registers, valueReg);
        InitPrivateFieldValue(target, currentFunc, brandId, slotIndex, value);
        acc = JsValue.Undefined;
        return 6;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ExecuteInitPrivateAccessor(
        JsBytecodeFunction currentFunc,
        ref JsValue registers,
        ref byte pc)
    {
        int objReg = pc;
        ref var targetRef = ref Unsafe.Add(ref registers, objReg);
        if (!targetRef.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        int getterReg = Unsafe.Add(ref pc, 1);
        int setterReg = Unsafe.Add(ref pc, 2);
        var brandId = Unsafe.Add(ref pc, 3) | (Unsafe.Add(ref pc, 4) << 8);
        var slotIndex = Unsafe.Add(ref pc, 5) | (Unsafe.Add(ref pc, 6) << 8);
        var getterValue = Unsafe.Add(ref registers, getterReg);
        var setterValue = Unsafe.Add(ref registers, setterReg);
        var getter = getterValue.IsUndefined
            ? null
            : getterValue.TryGetObject(out var getterObj) && getterObj is JsFunction getterFn
                ? getterFn
                : ThrowPrivateAccessorTypeError<JsFunction?>("PRIVATE_ACCESSOR_GETTER",
                    "Private accessor getter must be function or undefined");
        var setter = setterValue.IsUndefined
            ? null
            : setterValue.TryGetObject(out var setterObj) && setterObj is JsFunction setterFn
                ? setterFn
                : ThrowPrivateAccessorTypeError<JsFunction?>("PRIVATE_ACCESSOR_SETTER",
                    "Private accessor setter must be function or undefined");
        InitPrivateAccessorValue(target, currentFunc, brandId, slotIndex, getter, setter);
        acc = JsValue.Undefined;
        return 7;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ExecuteInitPrivateMethod(
        JsBytecodeFunction currentFunc,
        ref JsValue registers,
        ref byte pc)
    {
        int objReg = pc;
        ref var targetRef = ref Unsafe.Add(ref registers, objReg);
        if (!targetRef.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        int methodReg = Unsafe.Add(ref pc, 1);
        var brandId = Unsafe.Add(ref pc, 2) | (Unsafe.Add(ref pc, 3) << 8);
        var slotIndex = Unsafe.Add(ref pc, 4) | (Unsafe.Add(ref pc, 5) << 8);
        var methodValue = Unsafe.Add(ref registers, methodReg);
        if (!methodValue.TryGetObject(out var methodObj) || methodObj is not JsFunction)
            ThrowTypeError("PRIVATE_METHOD_VALUE", "Private method value must be function");

        InitPrivateMethodValue(target, currentFunc, brandId, slotIndex, (JsFunction)methodObj);
        acc = JsValue.Undefined;
        return 6;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ExecuteGetPrivateField(
        JsBytecodeFunction currentFunc,
        ref JsValue registers,
        ref byte pc)
    {
        int objReg = pc;
        ref var targetRef = ref Unsafe.Add(ref registers, objReg);
        if (!targetRef.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        var brandId = Unsafe.Add(ref pc, 1) | (Unsafe.Add(ref pc, 2) << 8);
        var slotIndex = Unsafe.Add(ref pc, 3) | (Unsafe.Add(ref pc, 4) << 8);
        if (!TryGetPrivateSlotValue(target, currentFunc, brandId, slotIndex, out var privateSlotValue))
            ThrowPrivateFieldReadBrandError(currentFunc, brandId, slotIndex);

        if (privateSlotValue.TryGetObject(out var memberObj) &&
            memberObj is JsPrivateAccessorDescriptor accessor)
        {
            if (accessor.Getter is null)
            {
                var privateName = GetPrivateFieldDebugNameOrDefault(currentFunc.Script, brandId, slotIndex);
                ThrowTypeError("PRIVATE_ACCESSOR_GETTER",
                    $"Cannot read private member {privateName} without getter");
            }

            acc = InvokeFunction(accessor.Getter, target, ReadOnlySpan<JsValue>.Empty);
        }

        else if (privateSlotValue.TryGetObject(out memberObj) &&
                 memberObj is JsPrivateMethodDescriptor method)
        {
            acc = method.Method;
        }
        else
        {
            acc = privateSlotValue;
        }

        return 5;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ExecuteSetPrivateField(
        JsBytecodeFunction currentFunc,
        ref JsValue registers,
        ref byte pc)
    {
        int objReg = pc;
        ref var targetRef = ref Unsafe.Add(ref registers, objReg);
        if (!targetRef.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        int valueReg = Unsafe.Add(ref pc, 1);
        var brandId = Unsafe.Add(ref pc, 2) | (Unsafe.Add(ref pc, 3) << 8);
        var slotIndex = Unsafe.Add(ref pc, 4) | (Unsafe.Add(ref pc, 5) << 8);
        var value = Unsafe.Add(ref registers, valueReg);
        if (!TryGetPrivateSlotValue(target, currentFunc, brandId, slotIndex, out var existingPrivateValue))
            ThrowPrivateFieldWriteBrandError(currentFunc, brandId, slotIndex);

        if (existingPrivateValue.TryGetObject(out var memberObj) &&
            memberObj is JsPrivateAccessorDescriptor accessor)
        {
            if (accessor.Setter is null)
            {
                var privateName = GetPrivateFieldDebugNameOrDefault(currentFunc.Script, brandId, slotIndex);
                ThrowTypeError("PRIVATE_ACCESSOR_SETTER",
                    $"Cannot write private member {privateName} without setter");
            }

            var arg = MemoryMarshal.CreateReadOnlySpan(in value, 1);
            _ = InvokeFunction(accessor.Setter, target, arg);
            acc = value;
            return 6;
        }

        if (existingPrivateValue.TryGetObject(out memberObj) &&
            memberObj is JsPrivateMethodDescriptor)
        {
            var privateName = GetPrivateFieldDebugNameOrDefault(currentFunc.Script, brandId, slotIndex);
            ThrowTypeError("PRIVATE_METHOD_ASSIGN",
                $"Cannot assign to private method {privateName}");
        }

        if (!TrySetPrivateFieldValue(target, currentFunc, brandId, slotIndex, value))
            ThrowTypeError("PRIVATE_FIELD_INTERNAL", "Invalid private field write state");

        acc = value;
        return 6;
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
    private int HandleSwitchOnGeneratorState(ref byte bytecode, JsScript script, ref byte pc, int fp)
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
        out int pcUsed)
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        var result = HandleSuspendGenerator(ref bytecode, fullStack, ref registers, stopAtCallerFp, ref fp,
            ref pcOffset,
            ref acc);
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
        out int pcUsed)
    {
        var startOffset = GetPcOffset(ref bytecode, ref pc);
        var pcOffset = startOffset;
        var result = HandleResumeGenerator(ref bytecode, fullStack, ref registers, stopAtCallerFp, ref fp, ref pcOffset,
            ref acc);
        pcUsed = pcOffset - startOffset;
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowLdaGlobalReferenceError(int atom)
    {
        throw new JsRuntimeException(JsErrorKind.ReferenceError, $"{Atoms.AtomToString(atom)} is not defined",
            "GLOBAL_NOT_DEFINED");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryCatchRunCoreException(Exception e, ref byte pc, int stopAtCallerFp, ref int startPc,
        out JsRuntimeException? ex)
    {
        var isJsRuntimeException = e is JsRuntimeException;
        ex = e as JsRuntimeException ?? WrapUnexpectedRuntimeException(e);
        var currentFunc = Unsafe.As<JsBytecodeFunction>(Unsafe.As<JsValue, CallFrame>(ref Stack[fp]).Function);
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
        try
        {
            var fullStack = Stack.AsSpan();

            ref var pc = ref Unsafe.NullRef<byte>();
            ref var acc = ref this.acc;
            ref var fp = ref this.fp;

        ReloadFrame:
            var currentFunc =
                Unsafe.As<JsBytecodeFunction>(Unsafe.As<JsValue, CallFrame>(ref fullStack[fp]).Function);
            ref var bytecode = ref MemoryMarshal.GetArrayDataReference(currentFunc.Script.Bytecode);
            pc = ref Unsafe.Add(ref bytecode, startPc);
            startPc = 0;
            ref var nextCheck = ref Agent.ExecutionCheckCountdown;
            var objectPool = currentFunc.Script.ObjectConstants;
            var atomizedStringConstants = currentFunc.Script.AtomizedStringConstants;
            ref var registerRef = ref fullStack[fp + HeaderSize];
            var namedPropertyIcEntries = currentFunc.Script.NamedPropertyIcEntries;

            while (true)
            {
                var operandScale = BytecodeInfo.OperandScale.Single;
                ref var opcodePc = ref Unsafe.NullRef<byte>();
                double num1, num2;
                int intNum1, intNum2;
                int reg;
                ref var slotRef = ref Unsafe.NullRef<JsValue>();
                try
                {
                NextOp:
                    opcodePc = ref pc;
                    var op = (JsOpCode)opcodePc;
                    pc = ref Unsafe.Add(ref pc, 1);

                    if (--nextCheck == 0)
                        CheckExecutionSlowPath(fullStack, fp, ref bytecode, ref opcodePc, op, ref nextCheck);
                    switch (op)
                    {
                        case JsOpCode.Wide:
                            operandScale = BytecodeInfo.OperandScale.Wide;
                            goto NextOp;
                        case JsOpCode.ExtraWide:
                            operandScale = BytecodeInfo.OperandScale.ExtraWide;
                            goto NextOp;
                        case JsOpCode.LdaZero: acc = JsValue.FromInt32(0); break;
                        case JsOpCode.LdaUndefined: acc = JsValue.Undefined; break;
                        case JsOpCode.LdaNull: acc = JsValue.Null; break;
                        case JsOpCode.LdaTheHole: acc = JsValue.TheHole; break;
                        case JsOpCode.LdaTrue: acc = JsValue.True; break;
                        case JsOpCode.LdaFalse: acc = JsValue.False; break;

                        case JsOpCode.LdaNumericConstant:
                            {
                                acc = new(currentFunc.Script.NumericConstants[pc]);
                                pc = ref Unsafe.Add(ref pc, 1);
                            }
                            break;
                        case JsOpCode.LdaNumericConstantWide:
                            {
                                acc = new(currentFunc.Script.NumericConstants[Unsafe.ReadUnaligned<ushort>(ref pc)]);
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
                                var tag = (Tag)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                int idx;
                                if (op == JsOpCode.LdaTypedConstWide)
                                {
                                    idx = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    idx = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                acc = new(tag, obj: objectPool[idx]);
                            }
                            break;
                        case JsOpCode.LdaThis:
                            acc = fullStack[fp + OffsetThisValue];
                            if (acc.IsTheHole)
                                ThrowSuperNotCalled();
                            break;
                        case JsOpCode.LdaNewTarget:
                            acc = CurrentCallFrame.FrameKind == CallFrameKind.GeneratorFrame
                                ? JsValue.Undefined
                                : Unsafe.Add(ref registerRef, OffsetExtra0 - HeaderSize);
                            break;

                        case JsOpCode.CreateClosure:
                        case JsOpCode.CreateClosureWide:
                            {
                                var isWide = op == JsOpCode.CreateClosureWide;
                                int idx;
                                if (isWide)
                                {
                                    idx = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    idx = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                pc = ref Unsafe.Add(ref pc, 1); // flags (unused for now)

                                acc = BindClosureIfNeeded((JsBytecodeFunction)objectPool[idx]);
                            }
                            break;

                        case JsOpCode.LdaCurrentFunction:
                            acc = Unsafe.As<JsObject>(Unsafe.Subtract(ref registerRef, HeaderSize).Obj!); break;
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
                            {
                                var parent = GetCurrentContext(fullStack);
                                int slotCount;
                                if (op == JsOpCode.CreateFunctionContextWithCellsWide)
                                {
                                    slotCount = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    slotCount = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                JsContext o;
                                if (parent is null &&
                                    Agent.TryGetCurrentModuleRuntimeBindings(out var activeModuleBindings))
                                {
                                    if (activeModuleBindings.TopLevelContext is not null)
                                    {
                                        o = activeModuleBindings.TopLevelContext;
#if DEBUG
                                        if (o.Slots.Length != slotCount)
                                            throw new InvalidOperationException(
                                                "Shared module context slot count mismatch.");
#endif
                                    }
                                    else
                                    {
                                        o = new(parent, slotCount)
                                        {
                                            ModuleBindings = activeModuleBindings
                                        };
                                    }
                                }
                                else
                                {
                                    o = new(parent, slotCount);
                                }

                                acc = JsValue.FromObject(o);
                                if (op is JsOpCode.CreateFunctionContextWithCells
                                    or JsOpCode.CreateFunctionContextWithCellsWide)
                                {
                                    SetFrameContext(fullStack, fp, o);
                                    if (parent is null && CurrentCallFrame.FrameKind == CallFrameKind.ScriptFrame)
                                        RegisterGlobalLexicalBindings(currentFunc.Script, o);
                                }
                            }
                            break;
                        case JsOpCode.PushContext:
                            {
                                SetFrameContext(fullStack, fp, Unsafe.Add(ref registerRef, pc).Obj as JsContext);
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
                                var current = GetCurrentContext(fullStack);
                                SetFrameContext(fullStack, fp, current?.Parent);
                            }
                            break;
                        case JsOpCode.LdaCurrentContextSlot:
                        case JsOpCode.LdaCurrentContextSlotWide:
                        case JsOpCode.LdaCurrentContextSlotNoTdz:
                        case JsOpCode.LdaCurrentContextSlotNoTdzWide:
                        case JsOpCode.StaCurrentContextSlot:
                        case JsOpCode.StaCurrentContextSlotWide:
                            {
                                var ctx = GetCurrentContext(fullStack) ??
                                          throw new InvalidOperationException("No current context.");

                                int slotIndex;
                                if (op is JsOpCode.LdaCurrentContextSlotWide or JsOpCode.LdaCurrentContextSlotNoTdzWide
                                    or JsOpCode.StaCurrentContextSlotWide)
                                {
                                    slotIndex = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    slotIndex = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                slotRef = ref ctx.Slots[slotIndex];
                                if (op is JsOpCode.LdaCurrentContextSlot or JsOpCode.LdaCurrentContextSlotWide)
                                    acc = ThrowIfTheHole(slotRef);
                                else if (op is JsOpCode.LdaCurrentContextSlotNoTdz
                                         or JsOpCode.LdaCurrentContextSlotNoTdzWide)
                                    acc = slotRef;
                                else
                                    slotRef = acc;
                            }
                            break;
                        case JsOpCode.LdaContextSlot:
                        case JsOpCode.LdaContextSlotWide:
                        case JsOpCode.LdaContextSlotNoTdz:
                        case JsOpCode.LdaContextSlotNoTdzWide:
                        case JsOpCode.StaContextSlot:
                        case JsOpCode.StaContextSlotWide:
                            {
                                if (op is JsOpCode.LdaContextSlotWide or JsOpCode.LdaContextSlotNoTdzWide
                                    or JsOpCode.StaContextSlotWide)
                                {
                                    intNum1 = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    intNum1 = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                // context depth
                                intNum2 = pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                var ctx = GetContextAtDepth(fullStack, intNum2);
                                slotRef = ref ctx.Slots[intNum1];
                                if (op is JsOpCode.LdaContextSlot or JsOpCode.LdaContextSlotWide)
                                    acc = ThrowIfTheHole(slotRef);
                                else if (op is JsOpCode.LdaContextSlotNoTdz or JsOpCode.LdaContextSlotNoTdzWide)
                                    acc = slotRef;
                                else
                                    slotRef = acc;
                            }
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
                                if ((op == JsOpCode.LdaLexicalLocal || op == JsOpCode.LdaLexicalLocalWide) && acc.IsTheHole)
                                    ThrowHole();
                                pc = ref Unsafe.Add(ref pc,
                                    op is JsOpCode.LdarWide or JsOpCode.LdaLexicalLocalWide ? 2 : 1);
                            }
                            break;
                        case JsOpCode.LdaModuleVariable:
                            {
                                int cellIndex = (sbyte)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                int depth = pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                acc = ThrowIfTheHole(Agent.LoadCurrentModuleVariable(this, cellIndex, depth));
                            }
                            break;
                        case JsOpCode.Star:
                        case JsOpCode.StarWide:
                            {
                                reg = op == JsOpCode.StarWide
                                    ? Unsafe.ReadUnaligned<ushort>(ref pc)
                                    : pc;
                                Unsafe.Add(ref registerRef, reg) = acc;
                                pc = ref Unsafe.Add(ref pc, op == JsOpCode.StarWide ? 2 : 1);
                            }
                            break;
                        case JsOpCode.StaModuleVariable:
                            {
                                int cellIndex = (sbyte)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                int depth = pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                Agent.StoreCurrentModuleVariable(this, cellIndex, depth, acc);
                            }
                            break;
                        case JsOpCode.Mov:
                        case JsOpCode.MovWide:
                            {
                                int srcReg;
                                int dstReg;
                                if (op == JsOpCode.MovWide)
                                {
                                    srcReg = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    dstReg = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref pc, 2));
                                    pc = ref Unsafe.Add(ref pc, 4);
                                }
                                else
                                {
                                    srcReg = pc;
                                    dstReg = Unsafe.Add(ref pc, 1);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }

                                slotRef = ref Unsafe.Add(ref registerRef, srcReg);
                                Unsafe.Add(ref registerRef, dstReg) = slotRef;
                            }
                            break;
                        case JsOpCode.StaLexicalLocal:
                        case JsOpCode.StaLexicalLocalWide:
                            {
                                reg = op == JsOpCode.StaLexicalLocalWide
                                    ? Unsafe.ReadUnaligned<ushort>(ref pc)
                                    : pc;
                                pc = ref Unsafe.Add(ref pc, op == JsOpCode.StaLexicalLocalWide ? 2 : 1);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                if (slotRef.IsTheHole)
                                    ThrowHole();
                                slotRef = acc;
                            }
                            break;
                        case JsOpCode.LdaGlobal:
                        case JsOpCode.LdaGlobalWide:
                            {
                                int nameIdx;
                                int icSlot;
                                if (op == JsOpCode.LdaGlobal)
                                {
                                    nameIdx = pc;
                                    icSlot = Unsafe.Add(ref pc, 1);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    nameIdx = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    icSlot = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref pc, 2));
                                    pc = ref Unsafe.Add(ref pc, 4);
                                }

                                var atom = atomizedStringConstants[nameIdx];
                                if (TryGetGlobalBindingByAtom(currentFunc.Script, icSlot, atom, out var val))
                                    acc = val;
                                else
                                    ThrowLdaGlobalReferenceError(atom);
                            }
                            break;
                        case JsOpCode.StaGlobal:
                        case JsOpCode.StaGlobalWide:
                        case JsOpCode.StaGlobalInit:
                        case JsOpCode.StaGlobalInitWide:
                        case JsOpCode.StaGlobalFuncDecl:
                        case JsOpCode.StaGlobalFuncDeclWide:
                            {
                                int nameIdx;
                                int icSlot;
                                if (op is not JsOpCode.StaGlobalWide and not JsOpCode.StaGlobalInitWide
                                    and not JsOpCode.StaGlobalFuncDeclWide)
                                {
                                    nameIdx = pc;
                                    icSlot = Unsafe.Add(ref pc, 1);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    nameIdx = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    icSlot = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref pc, 2));
                                    pc = ref Unsafe.Add(ref pc, 4);
                                }

                                var atom = atomizedStringConstants[nameIdx];
                                var isInitializationStore = op is JsOpCode.StaGlobalInit or JsOpCode.StaGlobalInitWide
                                    or JsOpCode.StaGlobalFuncDecl or JsOpCode.StaGlobalFuncDeclWide;
                                var useFunctionDeclarationSemantics = op is JsOpCode.StaGlobalFuncDecl
                                    or JsOpCode.StaGlobalFuncDeclWide;
                                StoreGlobalByAtom(currentFunc.Script, icSlot, atom,
                                    isInitializationStore, useFunctionDeclarationSemantics, currentFunc.IsStrict);
                            }
                            break;
                        case JsOpCode.TypeOfGlobal:
                        case JsOpCode.TypeOfGlobalWide:
                            {
                                pc = ref Unsafe.Add(ref pc,
                                    TypeOfGlobal(op, currentFunc.Script, ref bytecode, ref pc, atomizedStringConstants,
                                        ref acc));
                            }
                            break;
                        case JsOpCode.CreateMappedArguments:
                            {
                                CreateArgumentsObjectForFrame(fp);
                            }
                            break;
                        case JsOpCode.CreateRestParameter:
                            {
                                int startIndex = pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                CreateRestParameterForFrame(fp, startIndex);
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
                            {
                                var isWide = op == JsOpCode.CreateObjectLiteralWide;
                                int boilerplateIdx;
                                if (isWide)
                                {
                                    boilerplateIdx = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    boilerplateIdx = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                pc = ref Unsafe.Add(ref pc, 1); // flags (unused)

                                var obj = new JsPlainObject((StaticNamedPropertyLayout)objectPool[boilerplateIdx]);
                                acc = obj;
                            }
                            break;
                        case JsOpCode.CreateArrayLiteral:
                            {
                                int boilerplateIdx = Unsafe.ReadUnaligned<ushort>(ref pc);
                                pc = ref Unsafe.Add(ref pc, 2);
                                if (objectPool[boilerplateIdx] is JsValue[] literalElements)
                                    acc = CreateArrayObject(literalElements);
                                else if (objectPool[boilerplateIdx] is int arrayLength && arrayLength >= 0)
                                    acc = CreateArrayObjectWithLength(arrayLength);
                                else
                                    acc = CreateArrayObject();
                            }
                            break;
                        case JsOpCode.InitializeNamedProperty:
                            {
                                reg = Unsafe.ReadUnaligned<ushort>(ref pc);
                                pc = ref Unsafe.Add(ref pc, 2);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                var slot = pc | (Unsafe.Add(ref pc, 1) << 8);
                                pc = ref Unsafe.Add(ref pc, 2);
                                var obj = slotRef.AsObject();
                                obj.InitializeLiteralNamedSlot(slot, acc);
                            }
                            break;

                        case JsOpCode.LdaNamedProperty:
                        case JsOpCode.LdaNamedPropertyWide:
                            {
                                var isWide = op == JsOpCode.LdaNamedPropertyWide;
                                reg = isWide
                                    ? Unsafe.ReadUnaligned<ushort>(ref pc)
                                    : pc;
                                pc = ref Unsafe.Add(ref pc, isWide ? 2 : 1);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                int nameIdx;
                                if (isWide)
                                {
                                    nameIdx = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    nameIdx = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                var atom = atomizedStringConstants[nameIdx];

                                ValidateAtomizedNameConstant(atom,
                                    "LdaNamedProperty requires atomized name constant.");
                                int icSlot;
                                if (isWide)
                                {
                                    icSlot = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    icSlot = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                var receiverIsObject = slotRef.TryGetObject(out var obj);
                                if (!receiverIsObject)
                                    obj = ToObjectForPropertyAccessSlowPath(this, slotRef);
                                if (CanUseNamedPropertyIc(namedPropertyIcEntries, icSlot, receiverIsObject, obj!, atom,
                                        out var ic))
                                {
                                    acc = obj!.GetNamedByCachedSlotInfo(this, ic.SlotInfo);
                                    break;
                                }

                                var found = receiverIsObject
                                    ? obj!.TryGetPropertyAtom(this, atom, out var value, out var slotInfo)
                                    : obj!.TryGetPropertyAtomWithReceiverValue(this, slotRef, atom, out value,
                                        out slotInfo);
                                acc = value;

                                if (found && CanCacheNamedPropertyResult(receiverIsObject, obj, slotInfo))
                                    UpdateNamedPropertyIc(namedPropertyIcEntries, icSlot, obj, atom, slotInfo);
                            }
                            break;
                        case JsOpCode.GetNamedPropertyFromSuper:
                        case JsOpCode.GetNamedPropertyFromSuperWide:
                            {
                                var isWide = op == JsOpCode.GetNamedPropertyFromSuperWide;
                                int nameIdx;
                                if (isWide)
                                {
                                    nameIdx = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    nameIdx = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                var atom = atomizedStringConstants[nameIdx];
                                ValidateAtomizedNameConstant(atom,
                                    "GetNamedPropertyFromSuper requires atomized name constant.");
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
                            }
                            break;

                        case JsOpCode.LdaKeyedProperty:
                            {
                                var operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
                                pc = ref Unsafe.Add(ref pc, operandOffset);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                if (!slotRef.TryGetObject(out var obj))
                                    obj = ToObjectForPropertyAccessSlowPath(this, slotRef);

                                if (acc.IsInt32)
                                {
                                    var key = acc.Int32Value;
                                    if (key >= 0)
                                        if (obj.TryGetElement((uint)key, out var value))
                                        {
                                            acc = value;
                                            break;
                                        }
                                }

                                acc = LoadKeyedPropertySlowPath(this, obj, acc);
                            }
                            break;

                        case JsOpCode.StaNamedProperty:
                        case JsOpCode.StaNamedPropertyWide:
                            {
                                var isWide = op == JsOpCode.StaNamedPropertyWide;
                                reg = isWide
                                    ? Unsafe.ReadUnaligned<ushort>(ref pc)
                                    : pc;
                                pc = ref Unsafe.Add(ref pc, isWide ? 2 : 1);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                int nameIdx;
                                if (isWide)
                                {
                                    nameIdx = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    nameIdx = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                int icSlot;
                                if (isWide)
                                {
                                    icSlot = Unsafe.ReadUnaligned<ushort>(ref pc);
                                    pc = ref Unsafe.Add(ref pc, 2);
                                }
                                else
                                {
                                    icSlot = pc;
                                    pc = ref Unsafe.Add(ref pc, 1);
                                }

                                var atom = atomizedStringConstants[nameIdx];
                                ValidateAtomizedNameConstant(atom,
                                    "StaNamedProperty requires atomized name constant.");
                                var receiverIsObject = slotRef.TryGetObject(out var obj);
                                if (!receiverIsObject) obj = ToObjectForPropertyAccessSlowPath(this, slotRef);

                                if (CanUseNamedPropertyIc(namedPropertyIcEntries, icSlot, receiverIsObject, obj!, atom,
                                        out var ic))
                                {
                                    var ok = obj!.SetNamedByCachedSlotInfo(this, ic.SlotInfo, acc);
                                    if (!ok && currentFunc.IsStrict)
                                        ThrowTypeError("ASSIGN_READONLY", "Cannot assign to read only property");
                                    break;
                                }

                                var stored = obj!.TrySetPropertyAtom(this, atom, acc, out var slotInfo);
                                if (!stored && currentFunc.IsStrict)
                                    ThrowTypeError("ASSIGN_READONLY", "Cannot assign to read only property");

                                if (CanCacheNamedPropertyResult(receiverIsObject, obj, slotInfo))
                                    UpdateNamedPropertyIc(namedPropertyIcEntries, icSlot, obj, atom, slotInfo);
                            }
                            break;

                        case JsOpCode.StaKeyedProperty:
                            {
                                var operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                var keyReg = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
                                pc = ref Unsafe.Add(ref pc, operandOffset);
                                if (!slotRef.TryGetObject(out var obj))
                                    obj = ToObjectForPropertyAccessSlowPath(this, slotRef);
                                var keyVal = Unsafe.Add(ref registerRef, keyReg);

                                if (keyVal.IsInt32)
                                {
                                    var key = keyVal.Int32Value;
                                    if (key >= 0)
                                    {
                                        var index = (uint)key;
                                        var ok = obj.TrySetOwnElement(index, acc, out var hadOwnElement);
                                        if (hadOwnElement)
                                        {
                                            if (!ok && currentFunc.IsStrict)
                                                ThrowTypeError("ASSIGN_READONLY", "Cannot assign to read only property");
                                            break;
                                        }
                                    }
                                }

                                var valueToStore = acc;
                                StoreKeyedPropertySlowPath(this, obj, keyVal, valueToStore, currentFunc.IsStrict);
                            }
                            break;
                        case JsOpCode.InitializeArrayElement:
                            {
                                reg = Unsafe.ReadUnaligned<ushort>(ref pc);
                                pc = ref Unsafe.Add(ref pc, 2);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                uint index = Unsafe.ReadUnaligned<ushort>(ref pc);
                                pc = ref Unsafe.Add(ref pc, 2);
                                if (slotRef.TryGetObject(out var obj) &&
                                    obj is JsArray array &&
                                    array.CanDefineElementAtIndex(index))
                                {
                                    array.InitializeLiteralElement(index, acc);
                                    break;
                                }

                                if (!slotRef.TryGetObject(out obj))
                                    obj = ToObjectForPropertyAccessSlowPath(this, slotRef);
                                StoreKeyedPropertySlowPath(this, obj!, JsValue.FromInt32((int)index), acc,
                                    currentFunc.IsStrict);
                            }
                            break;
                        case JsOpCode.DefineOwnKeyedProperty:
                            {
                                var operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                var keyReg = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
                                pc = ref Unsafe.Add(ref pc, operandOffset);

                                if (!slotRef.TryGetObject(out var obj))
                                    obj = ToObjectForPropertyAccessSlowPath(this, slotRef);

                                var keyVal = Unsafe.Add(ref registerRef, keyReg);
                                PropertyInitializationOperations.DefineOwnDataPropertyByKey(this, obj!, keyVal,
                                    acc);
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
                            pc = ref Unsafe.Add(ref pc,
                                ExecuteInitPrivateField(currentFunc, ref registerRef, ref pc));
                            break;
                        case JsOpCode.InitPrivateAccessor:
                            pc = ref Unsafe.Add(ref pc,
                                ExecuteInitPrivateAccessor(currentFunc, ref registerRef, ref pc));
                            break;
                        case JsOpCode.InitPrivateMethod:
                            pc = ref Unsafe.Add(ref pc,
                                ExecuteInitPrivateMethod(currentFunc, ref registerRef, ref pc));
                            break;
                        case JsOpCode.GetPrivateField:
                            pc = ref Unsafe.Add(ref pc,
                                ExecuteGetPrivateField(currentFunc, ref registerRef, ref pc));
                            break;
                        case JsOpCode.SetPrivateField:
                            pc = ref Unsafe.Add(ref pc,
                                ExecuteSetPrivateField(currentFunc, ref registerRef, ref pc));
                            break;

                        case JsOpCode.Add:
                        case JsOpCode.Sub:
                        case JsOpCode.Mul:
                        case JsOpCode.Div:
                        case JsOpCode.Mod:
                        case JsOpCode.Exp:
                            {
                                AssertValidOperandScale(operandScale);
                                var operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale); // slot
                                pc = ref Unsafe.Add(ref pc, operandOffset);

                                if (op is JsOpCode.Add or JsOpCode.Sub or JsOpCode.Mul)
                                    if (slotRef.IsInt32 && acc.IsInt32)
                                    {
                                        intNum1 = slotRef.Int32Value;
                                        intNum2 = acc.Int32Value;
                                        var res = op switch
                                        {
                                            JsOpCode.Add => (long)intNum1 + intNum2,
                                            JsOpCode.Sub => (long)intNum1 - intNum2,
                                            JsOpCode.Mul => (long)intNum1 * intNum2,
                                            _ => 0L
                                        };
                                        if (res <= int.MaxValue && res >= int.MinValue)
                                        {
                                            acc = JsValue.FromInt32((int)res);
                                            break;
                                        }

                                        acc = new(res);
                                        break;
                                    }

                                if (slotRef.IsNumber && acc.IsNumber)
                                {
                                    num1 = slotRef.FastNumberValue;
                                    num2 = acc.FastNumberValue;
                                    num1 = op switch
                                    {
                                        JsOpCode.Add => num1 + num2,
                                        JsOpCode.Sub => num1 - num2,
                                        JsOpCode.Mul => num1 * num2,
                                        JsOpCode.Div => num1 / num2,
                                        JsOpCode.Mod => num1 % num2,
                                        JsOpCode.Exp => NumberExponentiate(num1, num2),
                                        _ => 0 // throw makes no sense, and throw or eliminating default cause deoptimization, so just return 0 which will be ignored anyway.
                                    };
                                    acc = new(num1);
                                    break;
                                }

                                acc = HandleArithmeticNonNumberSlowPath(this, op, slotRef, acc);
                                break;
                            }

                        case JsOpCode.AddSmi:
                        case JsOpCode.SubSmi:
                            {
                                int imm = (sbyte)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                pc = ref Unsafe.Add(ref pc, 1); // slot
                                if (acc.IsInt32)
                                {
                                    var res = (long)acc.Int32Value + imm * (op == JsOpCode.AddSmi ? 1 : -1);
                                    ;
                                    if (res <= int.MaxValue && res >= int.MinValue)
                                    {
                                        acc = JsValue.FromInt32((int)res);
                                        break;
                                    }

                                    acc = new(res);
                                    break;
                                }

                                if (acc.IsFloat64)
                                {
                                    ref var num = ref Unsafe.As<JsValue, double>(ref acc);
                                    num = op == JsOpCode.AddSmi ? num + imm : num - imm;
                                    break;
                                }

                                if (op == JsOpCode.AddSmi)
                                    acc = AddSmiSlowPath(this, acc, imm);
                                else acc = HandleArithmeticNonNumberSmiSlowPath(this, JsOpCode.SubSmi, acc, imm);
                            }
                            break;
                        case JsOpCode.Inc:
                        case JsOpCode.Dec:
                            intNum1 = op == JsOpCode.Inc ? 1 : -1;
                            if (acc.IsInt32)
                            {
                                var res = (long)acc.Int32Value + intNum1;
                                if (res <= int.MaxValue && res >= int.MinValue)
                                    acc = JsValue.FromInt32((int)res);
                                else acc = new(res);
                            }
                            else if (acc.IsFloat64)
                            {
                                acc = new(acc.FastFloat64Value + intNum1);
                            }
                            else
                            {
                                acc = acc.U == JsValue.JsBigIntBits
                                    ? IncrementBigIntSlowPath(acc, intNum1)
                                    : IncrementSlowPath(this, acc, intNum1);
                            }

                            break;
                        case JsOpCode.MulSmi:
                            {
                                int imm = (sbyte)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                pc = ref Unsafe.Add(ref pc, 1); // slot
                                if (acc.IsInt32)
                                    acc = Mul(acc, imm);
                                else if (acc.IsNumber)
                                    acc = new(acc.FastNumberValue * imm);
                                else
                                    acc = HandleArithmeticNonNumberSmiSlowPath(this, JsOpCode.MulSmi, acc, imm);
                            }
                            break;
                        case JsOpCode.ModSmi:
                            {
                                // imm
                                intNum1 = (sbyte)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                pc = ref Unsafe.Add(ref pc, 1); // slot
                                if (acc.IsInt32 && intNum1 != 0)
                                {
                                    var v = acc.Int32Value;
                                    var result = v % intNum1;
                                    acc = result == 0 && v < 0
                                        ? new(-0.0d)
                                        : JsValue.FromInt32(result);
                                }
                                else if (acc.IsFloat64)
                                {
                                    acc = new(acc.FastFloat64Value % intNum1);
                                }
                                else
                                {
                                    acc = HandleArithmeticNonNumberSmiSlowPath(this, JsOpCode.ModSmi, acc, intNum1);
                                }
                            }
                            break;
                        case JsOpCode.ExpSmi:
                            {
                                // imm
                                intNum1 = (sbyte)pc;
                                pc = ref Unsafe.Add(ref pc, 1);
                                if (acc.IsNumber)
                                    acc = new(NumberExponentiate(acc.FastNumberValue, intNum1));
                                else
                                    acc = HandleArithmeticNonNumberSmiSlowPath(this, JsOpCode.ExpSmi, acc, intNum1);

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
                                    acc = new(this.ToNumberSlowPath(acc));
                                }
                            }
                            break;
                        case JsOpCode.ToString:
                            if (!acc.IsString)
                                acc = JsValue.FromString(this.ToJsStringSlowPath(acc));
                            break;
                        case JsOpCode.ToNumeric:
                            {
                                if (acc.IsNumeric)
                                {
                                    // already numeric
                                }
                                else
                                {
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
                                    var v = acc.Int32Value;
                                    if (v == 0)
                                    {
                                        acc = new(-0d);
                                        break;
                                    }

                                    if (v != int.MinValue)
                                    {
                                        acc = JsValue.FromInt32(-v);
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
                                var operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
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
                                        _ => false
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
                                var operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale); // slot
                                pc = ref Unsafe.Add(ref pc, operandOffset);
                                acc = op switch
                                {
                                    JsOpCode.TestEqualStrict => StrictEquals(slotRef, acc),
                                    JsOpCode.TestEqual => AbstractEquals(this, slotRef, acc),
                                    JsOpCode.TestNotEqual => !AbstractEquals(this, slotRef, acc),
                                    _ => false
                                }
                                    ? JsValue.True
                                    : JsValue.False;
                                break;
                            }
                        case JsOpCode.TestInstanceOf:
                            {
                                AssertValidOperandScale(operandScale);
                                var operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale); // slot
                                pc = ref Unsafe.Add(ref pc, operandOffset);
                                InstanceOfSlowPath(this, slotRef);
                                break;
                            }
                        case JsOpCode.TestIn:
                            {
                                AssertValidOperandScale(operandScale);
                                var operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale); // slot
                                pc = ref Unsafe.Add(ref pc, operandOffset);
                                InOperatorSlowPath(slotRef);
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
                                        _ => false
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
                                var operandOffset = 0;
                                reg = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);
                                ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale); // slot
                                pc = ref Unsafe.Add(ref pc, operandOffset);

                                if (slotRef.IsInt32 && acc.IsInt32)
                                {
                                    intNum1 = slotRef.Int32Value;
                                    intNum2 = acc.Int32Value;
                                    if (op == JsOpCode.ShiftRightLogical)
                                    {
                                        var result = (uint)intNum1 >> (intNum2 & 0x1F);
                                        acc = result <= int.MaxValue
                                            ? JsValue.FromInt32((int)result)
                                            : new((double)result);
                                        break;
                                    }

                                    acc = JsValue.FromInt32(op switch
                                    {
                                        JsOpCode.BitwiseAnd => intNum1 & intNum2,
                                        JsOpCode.BitwiseOr => intNum1 | intNum2,
                                        JsOpCode.BitwiseXor => intNum1 ^ intNum2,
                                        JsOpCode.ShiftLeft => intNum1 << (intNum2 & 0x1F),
                                        JsOpCode.ShiftRight => intNum1 >> (intNum2 & 0x1F),
                                        _ => 0
                                    });
                                    break;
                                }

                                if (slotRef.U == JsValue.JsBigIntBits && acc.U == JsValue.JsBigIntBits)
                                {
                                    acc = HandleBigIntBitwiseFastSlowPath(op, slotRef, acc);
                                    break;
                                }

                                acc = HandleBitwiseSlowPath(this, op, slotRef, acc);
                            }
                            break;
                        case JsOpCode.Jump:
                            {
                                pc = ref Unsafe.Add(ref pc, 2 + Unsafe.ReadUnaligned<short>(ref pc));
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
                                var taken = EvaluateJumpCondition(op, acc);
                                if (taken)
                                    pc = ref Unsafe.Add(ref pc, 2 + Unsafe.ReadUnaligned<short>(ref pc));
                                else
                                    pc = ref Unsafe.Add(ref pc, 2);
                            }
                            break;
                        case JsOpCode.PushTry:
                            {
                                pc = ref Unsafe.Add(ref pc, 2);
                                PushExceptionHandler(fp, GetPcOffset(ref bytecode, ref pc) +
                                                         Unsafe.ReadUnaligned<short>(ref Unsafe.Subtract(ref pc, 2)),
                                    StackTop);
                            }
                            break;
                        case JsOpCode.PopTry:
                            PopCurrentExceptionHandlerForFrame(fp);
                            break;

                        case JsOpCode.CallUndefinedReceiver:
                        case JsOpCode.CallProperty:
                        case JsOpCode.Construct:
                            {
                                var operandOffset = 0;
                                AssertValidOperandScale(operandScale);
                                reg = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
                                slotRef = ref Unsafe.Add(ref registerRef, reg);

                                var okojoCallee = slotRef.Obj as JsFunction;
                                if (okojoCallee is not null)
                                {
                                    var receiverReg = -1;
                                    var isConstruct = op == JsOpCode.Construct;
                                    if (!isConstruct && op != JsOpCode.CallUndefinedReceiver)
                                        receiverReg = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);

                                    intNum1 = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
                                    intNum2 = ReadScaledUnsignedOperand(ref pc, ref operandOffset, operandScale);
                                    pc = ref Unsafe.Add(ref pc, operandOffset);
                                    var allowTailCall = !isConstruct &&
                                                        currentFunc.IsStrict &&
                                                        pc == (byte)JsOpCode.Return;
                                    if ((Agent.ExecutionCheckpointHookBits &
                                         (int)ExecutionCheckpointHooks.Call) != 0)
                                        EmitExecutionBoundaryCheckpoint(fullStack, fp, ExecutionCheckpointKind.Call,
                                            ref bytecode, ref opcodePc);
                                    if (okojoCallee.NamedPropertyLayout.Owner != this)
                                    {
                                        DispatchCrossRealm(okojoCallee, receiverReg, intNum1, intNum2, isConstruct,
                                            GetPcOffset(ref bytecode, ref pc),
                                            ref registerRef);
                                    }
                                    else if (TryDispatchVmStackInvocation(okojoCallee, receiverReg, intNum1, intNum2,
                                                 isConstruct, allowTailCall, GetPcOffset(ref bytecode, ref pc),
                                                 ref currentFunc,
                                                 ref registerRef))
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
                                pc = ref Unsafe.Add(ref pc,
                                    CallRuntime(this, currentFunc, ref bytecode, ref pc, ref registerRef, fp,
                                        operandScale));

                                [MethodImpl(MethodImplOptions.NoInlining)]
                                static int CallRuntime(
                                    JsRealm realm,
                                    JsBytecodeFunction currentFunc,
                                    ref byte bytecode,
                                    ref byte pc,
                                    ref JsValue registerRef,
                                    int fp,
                                    BytecodeInfo.OperandScale operandScale)
                                {
                                    var startOffset = GetPcOffset(ref bytecode, ref pc);
                                    var opcodePc = startOffset - 1;
                                    var pcOffset = startOffset;
                                    AssertValidOperandScale(operandScale);
                                    var runtimeId = ReadByteOrU16(ref bytecode, ref pcOffset,
                                        operandScale != BytecodeInfo.OperandScale.Single);
                                    var argStart = ReadByteOrU16(ref bytecode, ref pcOffset,
                                        operandScale != BytecodeInfo.OperandScale.Single);
                                    var argCount = ReadByteOrU16(ref bytecode, ref pcOffset,
                                        operandScale != BytecodeInfo.OperandScale.Single);
                                    try
                                    {
                                        SRuntimeHandlers[runtimeId]!(realm, currentFunc.Script, opcodePc, ref registerRef,
                                            fp,
                                            argStart, argCount, ref realm.acc);
                                    }
                                    catch (Exception ex) when (ex is IndexOutOfRangeException or InvalidOperationException)
                                    {
                                        throw new InvalidOperationException(
                                            $"CallRuntime failed: pc={opcodePc}, runtimeId={runtimeId}, argStart={argStart}, argCount={argCount}, scale={operandScale}",
                                            ex);
                                    }

                                    return pcOffset - startOffset;
                                }
                            }
                            break;
                        case JsOpCode.SwitchOnSmi:
                            {
                                pc = ref Unsafe.Add(ref pc,
                                    HandleSwitchOnSmi(ref bytecode, currentFunc.Script, ref pc, acc));
                            }
                            break;
                        case JsOpCode.SwitchOnGeneratorState:
                            {
                                pc = ref Unsafe.Add(ref pc,
                                    HandleSwitchOnGeneratorState(ref bytecode, currentFunc.Script, ref pc, fp));
                            }
                            break;
                        case JsOpCode.SuspendGenerator:
                            {
                                int pcUsed;
                                if (HandleSuspendGenerator(ref bytecode, fullStack, ref registerRef, stopAtCallerFp,
                                        ref fp, ref pc, ref acc, out pcUsed) == GeneratorDispatchResult.ReturnFromRun)
                                {
                                    pc = ref Unsafe.Add(ref pc, pcUsed);
                                    return;
                                }

                                pc = ref Unsafe.Add(ref pc, pcUsed);
                                goto ReloadFrame;
                            }
                        case JsOpCode.ResumeGenerator:
                            {
                                int pcUsed;
                                switch (HandleResumeGenerator(ref bytecode, fullStack, ref registerRef, stopAtCallerFp,
                                            ref fp,
                                            ref pc, ref acc, out pcUsed))
                                {
                                    case GeneratorDispatchResult.ReturnFromRun:
                                        pc = ref Unsafe.Add(ref pc, pcUsed);
                                        return;
                                    case GeneratorDispatchResult.ReloadFrame:
                                        pc = ref Unsafe.Add(ref pc, pcUsed);
                                        goto ReloadFrame;
                                }

                                pc = ref Unsafe.Add(ref pc, pcUsed);
                            }
                            break;
                        case JsOpCode.Throw:
                            ThrowJsValue(acc);
                            break;

                        case JsOpCode.Return:
                            {
                                if (fp == 0) return;

                                if ((Agent.ExecutionCheckpointHookBits &
                                     (int)ExecutionCheckpointHooks.Return) != 0)
                                    EmitExecutionBoundaryCheckpoint(fullStack, fp, ExecutionCheckpointKind.Return,
                                        ref bytecode, ref opcodePc);

                                ref var callFrame =
                                    ref Unsafe.As<JsValue, CallFrame>(ref Unsafe.Subtract(ref registerRef, HeaderSize));
                                var generatorReturn = callFrame.FrameKind == CallFrameKind.GeneratorFrame;
                                var constructorReturn = (callFrame.Flags & CallFrameFlag.IsConstructorCall) != 0;
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

                                    if (!fastForOfStepMode && !asyncDriver) acc = CreateIteratorResultObject(acc, true);
                                }

                                var top = StackTop;
                                StackTop = fp;
                                RemoveExceptionHandlersForFrame(fp);
                                fp = callFrame.CallerFp;
                                startPc = callFrame.CallerPc;
                                fullStack[StackTop..top]
                                    .Fill(JsValue
                                        .Undefined); // Clear registers of the frame being popped to avoid keeping references to objects longer than needed.

                                if (!generatorReturn && constructorReturn)
                                    acc = CompleteConstructResult(acc, constructorThis, constructorFlags);

                                if (stopAtCallerFp >= 0 && fp == stopAtCallerFp) return;

                                if (fp == 0 && startPc == 0) return;

                                goto ReloadFrame;
                            }
                        case JsOpCode.Debugger:
                            {
                                if ((Agent.ExecutionCheckpointHookBits &
                                     ((int)ExecutionCheckpointHooks.DebuggerStatement |
                                      (int)ExecutionCheckpointHooks.Breakpoint)) != 0 &&
                                    HandleDebuggerOpcode(fullStack, fp, ref bytecode, ref opcodePc))
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
                    if (TryCatchRunCoreException(e, ref opcodePc, stopAtCallerFp, ref startPc, out var newEx))
                        goto ReloadFrame;

                    if (newEx is not null) throw newEx;
                    throw;
                }
            }
        }

        finally
        {
            managedRunDepth--;
        }
    }
}
