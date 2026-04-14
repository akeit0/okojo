using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Internals;
using Okojo.Parsing;

namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    private const int MaxGeneratorCorePoolSize = 512;

    internal const int HeaderSize = FrameLayout.HeaderSize;
    internal const int OffsetCallData = FrameLayout.OffsetCallData;
    internal const int OffsetCurrentContext = FrameLayout.OffsetCurrentContext;
    internal const int OffsetThisValue = FrameLayout.OffsetThisValue;
    internal const int OffsetExtra0 = FrameLayout.OffsetExtra0;
    internal const int MaxManagedRunDepth = 16;

    private static readonly RuntimeHandler?[] SRuntimeHandlers =
    [
        HandleRuntimeThrowConstAssignError, // ThrowConstAssignError = 0
        HandleIntrinsicGeneratorGetResumeMode, // GeneratorGetResumeMode = 1
        HandleIntrinsicGeneratorClearResumeState, // GeneratorClearResumeState = 2
        HandleRuntimeForOfFastPathLength, // ForOfFastPathLength = 4
        HandleRuntimeDeleteKeyedProperty, // DeleteKeyedProperty = 6
        HandleRuntimeDefineClassMethod, // DefineClassMethod = 11
        HandleRuntimeCallSuperConstructor, // CallSuperConstructor = 15
        HandleRuntimeSetClassHeritage, // SetClassHeritage = 16
        HandleRuntimeSuperSet, // SuperSet = 17
        HandleRuntimeCallSuperConstructorForwardAll, // CallSuperConstructorForwardAll = 18
        HandleRuntimeDefineClassAccessor, // DefineClassAccessor = 19
        HandleRuntimeDefineClassField, // DefineClassField = 20
        HandleRuntimeDefineObjectAccessor, // DefineObjectAccessor = 21
        HandleRuntimeGetCurrentModuleSetFunctionName, // GetCurrentModuleSetFunctionName = 22
        HandleRuntimeGetCurrentModuleImportMeta, // GetCurrentModuleImportMeta = 23
        HandleRuntimeNormalizePropertyKey, // NormalizePropertyKey = 24
        HandleRuntimeRequireObjectCoercible, // RequireObjectCoercible = 25
        HandleRuntimeLoadKeyedFromSuper, // LoadKeyedFromSuper = 26
        HandleRuntimeGetCurrentFunctionSuperBase, // GetCurrentFunctionSuperBase = 27
        HandleRuntimeGetObjectPrototypeForSuper, // GetObjectPrototypeForSuper = 28
        HandleRuntimeThrowDeleteSuperPropertyReference, // ThrowDeleteSuperPropertyReference = 29
        HandleRuntimeCreateRegExpLiteral, // CreateRegExpLiteral = 30
        HandleRuntimeGetTemplateObject, // GetTemplateObject = 31
        HandleRuntimeDestructureArrayAssignment, // DestructureArrayAssignment = 32
        HandleRuntimeDestructureArrayAssignmentMemberTargets, // DestructureArrayAssignmentMemberTargets = 33
        HandleRuntimeThrowParameterInitializerTdz, // ThrowParameterInitializerTdz = 34
        HandleRuntimeCallWithSpread, // CallWithSpread = 35
        HandleRuntimeConstructWithSpread, // ConstructWithSpread = 36
        HandleRuntimeCallSuperConstructorWithSpread, // CallSuperConstructorWithSpread = 37
        HandleRuntimeCopyDataProperties, // CopyDataProperties = 38
        HandleRuntimeAppendArraySpread, // AppendArraySpread = 39
        HandleRuntimeDeleteKeyedPropertyStrict, // DeleteKeyedPropertyStrict = 40
        HandleRuntimeThrowIteratorResultNotObject, // ThrowIteratorResultNotObject = 41
        Intrinsics.HandleRuntimeDynamicImport, // DynamicImport = 42
        HandleRuntimeCopyDataPropertiesExcluding, // CopyDataPropertiesExcluding = 43
        HandleRuntimeSetFunctionName, // SetFunctionName = 44
        HandleRuntimeSetFunctionInstanceFieldKey, // SetFunctionInstanceFieldKey = 45
        HandleRuntimeLoadCurrentFunctionInstanceFieldKey, // LoadCurrentFunctionInstanceFieldKey = 46
        HandleRuntimeWrapSyncIteratorForAsyncDelegate, // WrapSyncIteratorForAsyncDelegate = 47
        HandleRuntimeGetAsyncIteratorMethod, // GetAsyncIteratorMethod = 48
        HandleRuntimeGetIteratorMethod, // GetIteratorMethod = 49
        HandleRuntimeSetFunctionPrivateBrandToken, // SetFunctionPrivateBrandToken = 50
        HandleRuntimeSetFunctionPrivateMethodValue, // SetFunctionPrivateMethodValue = 51
        HandleRuntimeLoadCurrentFunctionPrivateMethodValue, // LoadCurrentFunctionPrivateMethodValue = 52
        HandleRuntimeSetFunctionPrivateBrandMapping, // SetFunctionPrivateBrandMapping = 53
        HandleRuntimeSetFunctionPrivateBrandMappingExact, // SetFunctionPrivateBrandMappingExact = 54
        HandleRuntimeCreateRestParameterFromArrayLike, // CreateRestParameterFromArrayLike = 55
        HandleRuntimeAsyncIteratorClose, // AsyncIteratorClose = 56
        HandleRuntimeCreateArrayDestructureIterator, // CreateArrayDestructureIterator = 57
        HandleRuntimeDestructureIteratorStepValue, // DestructureIteratorStepValue = 58
        HandleRuntimeDestructureIteratorClose, // DestructureIteratorClose = 59
        HandleRuntimeDestructureIteratorCloseBestEffort, // DestructureIteratorCloseBestEffort = 60
        HandleRuntimeDestructureIteratorRestArray, // DestructureIteratorRestArray = 61
        HandleRuntimeAsyncIteratorCloseBestEffort, // AsyncIteratorCloseBestEffort = 62
        HandleRuntimeSetFunctionMethodEnvironment, // SetFunctionMethodEnvironment = 63
        HandleRuntimeHasPrivateField, // HasPrivateField = 64
        HandleIntrinsicClassGetPrototypeAndSetConstructor, // ClassGetPrototypeAndSetConstructor = 65
        HandleIntrinsicGeneratorHasActiveDelegateIterator // GeneratorHasActiveDelegateIterator = 66
    ];

    private static readonly IntrinsicHandler?[] SIntrinsicHandlers =
    [
        HandleIntrinsicGeneratorGetResumeMode, // GeneratorGetResumeMode = 0
        HandleIntrinsicGeneratorClearResumeState, // GeneratorClearResumeState = 1
        HandleIntrinsicClassGetPrototypeAndSetConstructor, // ClassGetPrototypeAndSetConstructor = 2
        HandleIntrinsicGeneratorHasActiveDelegateIterator // GeneratorHasActiveDelegateIterator = 3
    ];

    private readonly GeneratorObjectCore completedAsyncGeneratorCore = new()
    {
        State = GeneratorState.Completed,
        Flags = GeneratorFlag.IsAsyncGenerator
    };

    private readonly GeneratorObjectCore completedGeneratorCore = new()
    {
        State = GeneratorState.Completed
    };

    private readonly Stack<GeneratorObjectCore> generatorCorePool = new();

    internal readonly JsValue[] Stack = new JsValue[1024 * 64];

    private JsValue acc;
    private int executionPhaseDepth;
    private int fp;
    private int managedRunDepth;
    public JsValue Accumulator => acc;

    public JsValue CurrentNewTarget => GetFrameNewTarget(fp);
    internal TimeProvider TimeProvider => Engine.TimeProvider;

    public ref readonly CallFrame CurrentCallFrame => ref Unsafe.As<JsValue, CallFrame>(ref Stack[fp]);
    internal ref CallFrame CurrentCallFrameRef => ref Unsafe.As<JsValue, CallFrame>(ref Stack[fp]);
    public CallInfo CurrentCallInfo => new(this, fp);

    internal int StackTop { get; private set; }

    public event Action<JsValue>? UnhandledRejection;

    internal void RaiseUnhandledRejection(in JsValue value)
    {
        UnhandledRejection?.Invoke(value);
    }

    public event Action<JsValue>? FinalizationRegistryCleanupError;

    public void Execute(JsScript script, bool pumpJobsAfterRun = true)
    {
        StackTop = 0;
        fp = 0;
        ClearExceptionHandlers();
        script.ArmBreakpoints();
        var rootFunc = new JsBytecodeFunction(this, script, "root", isStrict: script.StrictDeclared);
        PushFrame(rootFunc, 0, 0, 0, null, GlobalObject, JsValue.Undefined,
            CallFrameKind.ScriptFrame);
        BeginExecutionPhase();
        try
        {
            Run();
            if (pumpJobsAfterRun)
                PumpJobs();
        }
        finally
        {
            EndExecutionPhase();
        }
    }

    public void ExecuteProgram(JsProgram program, bool pumpJobsAfterRun = true)
    {
        Intrinsics.PrepareGlobalScriptDeclarationInstantiation(this, program);
        var script = JsCompiler.Compile(this, program);
        Execute(script, pumpJobsAfterRun);
    }

    public JsValue ExecuteProgramInline(JsProgram program)
    {
        Intrinsics.PrepareGlobalScriptDeclarationInstantiation(this, program);
        var script = JsCompiler.Compile(this, program);
        script.ArmBreakpoints();
        var root = new JsBytecodeFunction(this, script, "script", isStrict: script.StrictDeclared);
        var result = InvokeBytecodeFunction(root, GlobalObject, ReadOnlySpan<JsValue>.Empty, JsValue.Undefined,
            CallFrameKind.ScriptFrame);
        acc = result;
        return result.IsTheHole ? JsValue.Undefined : result;
    }

    private void RejectAsyncDriverException(JsPromiseObject promise, JsRuntimeException ex)
    {
        Intrinsics.RejectPromise(promise, ex.ThrownValue ?? CreateErrorObjectFromException(ex));
    }

    public void Execute(JsBytecodeFunction rootFunc, bool pumpJobsAfterRun = true)
    {
        StackTop = 0;
        fp = 0;
        ClearExceptionHandlers();
        rootFunc.Script.ArmBreakpoints();
        PushFrame(rootFunc, 0, 0, 0, rootFunc.BoundParentContext, JsValue.Undefined, JsValue.Undefined);
        BeginExecutionPhase();
        try
        {
            Run();
            if (pumpJobsAfterRun)
                PumpJobs();
        }
        finally
        {
            EndExecutionPhase();
        }
    }

    public void PumpJobs()
    {
        BeginExecutionPhase();
        try
        {
            if ((Agent.ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.Pump) != 0)
                Agent.ExecutionCheckPolicy.EmitBoundaryCheckpoint(this, Stack.AsSpan(), fp,
                    ExecutionCheckpointKind.Pump);
            Agent.PumpJobs();
        }
        finally
        {
            EndExecutionPhase();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PushFrame(JsBytecodeFunction func, int callerFp, int callerPc, int argCount,
        JsContext? context,
        in JsValue thisValue,
        in JsValue newTarget,
        CallFrameKind frameKind = CallFrameKind.FunctionFrame,
        CallFrameFlag flags = CallFrameFlag.None)
    {
        this.fp = StackTop;
        var fp = this.fp;
        ref var fpRef = ref Stack[fp];
        Unsafe.As<JsValue, CallFrame>(ref fpRef) =
            new(func, callerFp, argCount, callerPc, context, thisValue, frameKind, flags);

        Unsafe.Add(ref fpRef, OffsetExtra0) = frameKind == CallFrameKind.ConstructFrame || func.IsArrow
            ? newTarget
            : JsValue.Undefined;


        // Fresh frame entry accumulator must be undefined; otherwise an implicit
        // return can leak caller ACC into callee result.
        acc = JsValue.Undefined;

        StackTop = fp + HeaderSize + Math.Max(func.Script.RegisterCount, argCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref readonly CallFrame GetCallFrameAt(int framePointer)
    {
        return ref Unsafe.As<JsValue, CallFrame>(ref Stack[framePointer]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsValue GetFrameNewTarget(int framePointer)
    {
        ref readonly var frame = ref GetCallFrameAt(framePointer);
        if (frame.FrameKind == CallFrameKind.GeneratorFrame)
            return JsValue.Undefined;
        if ((frame.Flags & CallFrameFlag.IsConstructorCall) == 0)
            return JsValue.Undefined;
        return Stack[framePointer + OffsetExtra0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetActiveGeneratorForFrame(int fp, JsGeneratorObject generator)
    {
        Stack[fp + OffsetExtra0] = generator;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetActiveGeneratorForFrame(int fp, out JsGeneratorObject generator)
    {
        if (Stack[fp + OffsetExtra0].TryGetObject(out var obj) && obj is JsGeneratorObject activeGenerator)
        {
            generator = activeGenerator;
            return true;
        }

        generator = null!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearActiveGeneratorForFrame(int fp)
    {
        Stack[fp + OffsetExtra0] = JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinalizeGenerator(JsGeneratorObject generator)
    {
        var core = generator.Core;
        if (ReferenceEquals(core, completedGeneratorCore))
            return;
        if (ReferenceEquals(core, completedAsyncGeneratorCore))
            return;

        if ((core.Flags & GeneratorFlag.IsAsyncGenerator) != 0)
        {
            var preservedFlags =
                core.Flags & (GeneratorFlag.IsAsyncGenerator | GeneratorFlag.AsyncGeneratorRequestActive);
            var queue = core.AsyncRequestQueue;
            var activeRequest = core.ActiveAsyncRequest;
            core.SetCompletedDetached();
            core.Flags = preservedFlags;
            core.AsyncRequestQueue = queue;
            core.ActiveAsyncRequest = activeRequest;
            return;
        }

        if ((core.Flags & GeneratorFlag.FastForOfStepDone) != 0)
        {
            var preservedFlags = core.Flags & (GeneratorFlag.FastForOfStepDone | GeneratorFlag.IsAsyncGenerator);
            core.SetCompletedDetached();
            core.Flags = preservedFlags;
            return;
        }

        generator.Core = (core.Flags & GeneratorFlag.IsAsyncGenerator) != 0
            ? completedAsyncGeneratorCore
            : completedGeneratorCore;

        ReturnGeneratorCore(core);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private GeneratorObjectCore RentGeneratorCore(int initialSnapshotCount)
    {
        var core = generatorCorePool.Count != 0
            ? generatorCorePool.Pop()
            : new();

        core.RegisterSnapshotBuffer = new JsValue[initialSnapshotCount];
        core.State = GeneratorState.SuspendedStart;
        return core;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReturnGeneratorCore(GeneratorObjectCore core)
    {
        core.RegisterSnapshotBuffer = null!;
        core.SetCompletedDetached();
        if (generatorCorePool.Count >= MaxGeneratorCorePoolSize)
            return;
        generatorCorePool.Push(core);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static JsRuntimeException WrapUnexpectedRuntimeException(Exception e)
    {
        if (e is StackOverflowException)
            return new JsFatalRuntimeException(JsErrorKind.RangeError, "Maximum call stack size exceeded",
                innerException: e);

        return new(JsErrorKind.InternalError, "Internal error: " + e.Message, innerException: e);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfManagedRunDepthExceeded()
    {
        if (managedRunDepth >= MaxManagedRunDepth)
            throw new JsFatalRuntimeException(JsErrorKind.RangeError, "Maximum call stack size exceeded",
                "CALL_STACK_EXCEEDED",
                innerException: new StackOverflowException("Managed JsRealm.Run recursion depth exceeded."));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CreateArgumentsObjectForFrame(
        int frameFp)
    {
        ref readonly var callFrame = ref GetCurrentCallFrame(Stack, frameFp);
        var currentFunc = Unsafe.As<JsBytecodeFunction>(callFrame.Function);
        var args = GetFrameArgumentsSpan(frameFp);
        var actualCount = args.Length;
        var mapped = currentFunc.HasSimpleParameterList && !currentFunc.IsStrict && !currentFunc.IsArrow;
        int[]? mappedSlots = null;
        if (mapped && currentFunc.ArgumentsMappedSlots is not null)
        {
            var mappedLength = Math.Min(actualCount, currentFunc.ArgumentsMappedSlots.Length);
            mappedSlots = new int[mappedLength];
            Array.Copy(currentFunc.ArgumentsMappedSlots, mappedSlots, mappedLength);
        }

        acc = new JsArgumentsObject(this, args, mapped, mappedSlots,
            GetCurrentContextForFrame(frameFp));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CreateRestParameterForFrame(int frameFp, int startIndex)
    {
        var args = GetFrameArgumentsSpan(frameFp);
        var actualCount = args.Length;
        var from = Math.Max(0, Math.Min(startIndex, actualCount));
        var arr = CreateArrayObject();
        uint outIndex = 0;
        for (var i = from; i < actualCount; i++)
            arr.SetElement(outIndex++, args[i]);
        acc = arr;
    }

    private static long GetArrayLikeLengthLong(JsRealm realm, JsObject obj)
    {
        if (obj is JsTypedArrayObject typedArray)
            return typedArray.Length;
        if (obj is JsArray array)
            return array.Length;

        if (!obj.TryGetPropertyAtom(realm, IdLength, out var lengthValue, out _))
            return 0;

        var lengthNum = realm.ToNumberSlowPath(lengthValue);
        if (double.IsNaN(lengthNum) || lengthNum <= 0)
            return 0;

        const double maxSafeInteger = 9007199254740991d;
        return (long)Math.Min(maxSafeInteger, Math.Floor(lengthNum));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue CreateRestParameterFromArrayLike(in JsValue arrayLikeValue, int startIndex)
    {
        if (!this.TryToObject(arrayLikeValue, out var obj))
            ThrowTypeError("CREATE_REST_FROM_ARRAYLIKE_OBJECT", "rest source must be object");

        var lengthLong = GetArrayLikeLengthLong(this, obj);
        var actualCount = lengthLong <= 0
            ? 0
            : lengthLong >= int.MaxValue
                ? int.MaxValue
                : (int)lengthLong;
        var from = Math.Max(0, Math.Min(startIndex, actualCount));
        var arr = CreateArrayObject();
        uint outIndex = 0;
        for (var i = (uint)from; i < (uint)actualCount; i++)
            arr.SetElement(outIndex++, obj.TryGetElement(i, out var value) ? value : JsValue.Undefined);
        return arr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<JsValue> GetFrameArgumentsSpan(int frameFp)
    {
        ref readonly var callFrame = ref GetCurrentCallFrame(Stack, frameFp);
        var argCount = Math.Max(0, callFrame.ArgCount);
        return argCount == 0
            ? ReadOnlySpan<JsValue>.Empty
            : Stack.AsSpan(frameFp + HeaderSize, argCount);
    }

    private bool TryGetGlobalBinding(string name, out JsValue value)
    {
        var atom = Atoms.InternNoCheck(name);
        return TryGetGlobalBindingByAtom(atom, out value);
    }

    private void SetGlobalBinding(string name, in JsValue value)
    {
        var atom = Atoms.InternNoCheck(name);
        GlobalObject.DefineGlobalBindingAtom(atom, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetGlobalBindingByAtom(int atom, out JsValue value)
    {
        if (TryGetGlobalLexicalBindingValue(atom, out value))
            return true;

        return GlobalObject.TryGetPropertyAtom(this, atom, out value, out _);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long PackPrivateBrandSlotKey(int brandId, int slotIndex)
    {
        return ((long)brandId << 32) | (uint)slotIndex;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string GetPrivateFieldDebugNameOrDefault(JsScript script, int brandId, int slotIndex)
    {
        var keys = script.PrivateFieldDebugKeys;
        var nameIndices = script.PrivateFieldDebugNameIndices;
        var names = script.DebugNames;
        if (keys is null || nameIndices is null || names is null || keys.Length == 0 ||
            nameIndices.Length != keys.Length)
            return "#<private>";

        var packed = PackPrivateBrandSlotKey(brandId, slotIndex);
        var index = Array.BinarySearch(keys, packed);
        if (index < 0)
            return "#<private>";
        var nameIndex = nameIndices[index];
        return (uint)nameIndex < (uint)names.Length ? names[nameIndex] : "#<private>";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryGetScriptDebugNameByPc(JsScript script, int pc, int[]? debugPcs, int[]? nameIndices,
        out string name)
    {
        name = string.Empty;
        if (debugPcs is null || nameIndices is null || script.DebugNames is null ||
            debugPcs.Length == 0 || nameIndices.Length != debugPcs.Length)
            return false;

        var index = Array.BinarySearch(debugPcs, pc);
        if (index < 0)
            return false;
        var nameIndex = nameIndices[index];
        if ((uint)nameIndex >= (uint)script.DebugNames.Length)
            return false;
        name = script.DebugNames[nameIndex];
        return true;
    }


    internal JsValue InvokeFunction(JsFunction fn, JsValue thisValue, ReadOnlySpan<JsValue> args)
    {
        var callerAcc = acc;
        try
        {
            if (!ReferenceEquals(fn.Realm, this) && fn is not JsProxyFunction)
                return fn.Realm.InvokeFunction(fn, thisValue, args);

            if (fn is JsBytecodeFunction bytecodeFunc)
                return InvokeBytecodeFunction(bytecodeFunc, thisValue, args, JsValue.Undefined);

            return fn.InvokeNonBytecodeCall(this, thisValue, args, 0);
        }
        finally
        {
            acc = callerAcc;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsValue DispatchNonBytecodeFromStack(
        JsFunction fn,
        JsValue thisValue,
        int argOffset,
        int argCount,
        JsValue newTarget,
        int callerPc,
        CallFrameFlag flags,
        bool isConstruct)
    {
        Debug.Assert(fn is not JsBytecodeFunction);
        switch (fn)
        {
            case JsHostFunction host:
                return InvokeHostFunctionFromStackWithExitFrame(host, thisValue, callerPc, newTarget, flags, argOffset,
                    argCount);
            case JsBoundFunction bound:
                return isConstruct
                    ? bound.ConstructBoundFromStack(this, argOffset, argCount, newTarget, callerPc)
                    : bound.InvokeBoundFromStack(this, argOffset, argCount, callerPc);
            case JsProxyFunction proxy:
                return isConstruct
                    ? proxy.ConstructProxyFromStack(this, argOffset, argCount, newTarget, callerPc)
                    : proxy.InvokeProxyFromStack(this, thisValue, argOffset, argCount, callerPc);
            default:
                var args = Stack.AsSpan(argOffset, argCount);
                return isConstruct
                    ? fn.InvokeNonBytecodeConstruct(this, thisValue, args, newTarget, callerPc, flags)
                    : fn.InvokeNonBytecodeCall(this, thisValue, args, callerPc);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsValue DispatchCallFromStack(
        JsFunction fn,
        JsValue thisValue,
        int argOffset,
        int argCount,
        int callerPc)
    {
        if (!ReferenceEquals(fn.Realm, this) && fn is not JsProxyFunction)
        {
            var args = Stack.AsSpan(argOffset, argCount).ToArray();
            return fn.Realm.InvokeFunction(fn, thisValue, args);
        }

        return fn is JsBytecodeFunction bytecodeFunc
            ? InvokeBytecodeFunctionFromStackWindow(bytecodeFunc, thisValue, argOffset, argCount, JsValue.Undefined,
                callerPc)
            : DispatchNonBytecodeFromStack(fn, thisValue, argOffset, argCount, JsValue.Undefined, callerPc,
                CallFrameFlag.None, false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsValue DispatchConstructFromStack(
        JsFunction fn,
        in PreparedConstruct prepared,
        int argOffset,
        int argCount,
        int callerPc)
    {
        if (!ReferenceEquals(fn.Realm, this) && fn is not JsProxyFunction)
        {
            var args = Stack.AsSpan(argOffset, argCount).ToArray();
            return fn.Realm.ConstructWithExplicitNewTarget(fn, args, prepared.NewTarget, callerPc);
        }

        var result = fn is JsBytecodeFunction bytecodeFunc
            ? InvokeBytecodeFunctionFromStackWindow(bytecodeFunc, prepared.ThisValue, argOffset, argCount,
                prepared.NewTarget, callerPc, CallFrameKind.ConstructFrame, prepared.Flags)
            : DispatchNonBytecodeFromStack(fn, prepared.ThisValue, argOffset, argCount, prepared.NewTarget, callerPc,
                prepared.Flags, true);

        return CompleteConstructResult(result, prepared.ThisValue, prepared.Flags);
    }

    internal JsValue InvokeHostFunctionWithExitFrame(JsHostFunction hostFunc, JsValue thisValue,
        ReadOnlySpan<JsValue> args, int callerPc, JsValue newTarget, CallFrameFlag flags = CallFrameFlag.None)
    {
        var hostArgOffset = StackTop + HeaderSize;
        var callerFp = fp;
        var hostFp = StackTop;
        var callerAcc = acc;
        var fullStack = Stack.AsSpan();
        var requiredTop = hostFp + HeaderSize + args.Length;
        if (requiredTop > fullStack.Length)
            throw new StackOverflowException();

        args.CopyTo(fullStack[(hostFp + HeaderSize)..]);

        fp = hostFp;
        Unsafe.As<JsValue, CallFrame>(ref fullStack[hostFp]) = new(
            hostFunc,
            callerFp,
            args.Length,
            callerPc,
            GetCurrentContextForFrame(callerFp),
            thisValue,
            CallFrameKind.HostExitFrame,
            flags);
        fullStack[hostFp + FrameLayout.OffsetExtra0] = newTarget;
        acc = JsValue.Undefined;
        StackTop = requiredTop;

        try
        {
            var info = new CallInfo(this, hostFp, hostArgOffset);
            return hostFunc.BodyField(in info);
        }
        finally
        {
            RestoreExitFrame(hostFp, callerFp, callerAcc);
        }
    }

    internal JsValue InvokeHostFunctionFromStackWithExitFrame(
        JsHostFunction hostFunc,
        JsValue thisValue,
        int callerPc,
        JsValue newTarget,
        CallFrameFlag flags,
        int argOffset,
        int argCount)
    {
        var callerFp = fp;
        var hostFp = StackTop;
        var callerAcc = acc;
        var fullStack = Stack;
        var requiredTop = hostFp + HeaderSize;
        if (requiredTop > fullStack.Length)
            throw new StackOverflowException();

        fp = hostFp;
        Unsafe.As<JsValue, CallFrame>(ref fullStack[hostFp]) = new(
            hostFunc,
            callerFp,
            argCount,
            callerPc,
            GetCurrentContextForFrame(callerFp),
            thisValue,
            CallFrameKind.HostExitFrame,
            flags);
        fullStack[hostFp + FrameLayout.OffsetExtra0] = newTarget;
        acc = JsValue.Undefined;
        StackTop = requiredTop;

        try
        {
            return hostFunc.BodyField(new(this, hostFp, argOffset));
        }
        finally
        {
            RestoreExitFrame(hostFp, callerFp, callerAcc);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RestoreExitFrame(int exitFp, int callerFp, JsValue callerAcc)
    {
        var top = StackTop;
        if (top < exitFp)
            top = exitFp;
        StackTop = exitFp;
        fp = callerFp;
        acc = callerAcc;
        if (top > StackTop)
            Stack.AsSpan(StackTop, top - StackTop).Fill(JsValue.Undefined);
    }

    internal JsValue InvokeBytecodeFunction(JsBytecodeFunction func, JsValue thisValue, ReadOnlySpan<JsValue> args,
        JsValue newTarget, CallFrameKind frameKind = CallFrameKind.FunctionFrame,
        CallFrameFlag flags = CallFrameFlag.None)
    {
        thisValue = PrepareBytecodeThisValue(func, thisValue, frameKind == CallFrameKind.ConstructFrame);
        if (TryInvokeBytecodeNonOrdinary(func, thisValue, args, frameKind == CallFrameKind.ConstructFrame,
                out var specialResult))
            return specialResult;

        ThrowIfManagedRunDepthExceeded();

        var callerFp = fp;
        var newFp = StackTop;
        var fullStack = Stack.AsSpan();
        var registerWindowSize = Math.Max(func.Script.RegisterCount, args.Length);
        if (newFp + HeaderSize + registerWindowSize > fullStack.Length)
            throw new StackOverflowException();

        for (var i = 0; i < args.Length; i++)
            fullStack[newFp + HeaderSize + i] = args[i];
        fullStack.Slice(newFp + HeaderSize + args.Length, registerWindowSize - args.Length).Fill(JsValue.Undefined);

        func.Script.ArmBreakpoints();
        PushFrame(func, callerFp, 0, args.Length, func.BoundParentContext, thisValue,
            PrepareBytecodeNewTargetValue(func, newTarget), frameKind, flags);
        try
        {
            Run(callerFp);
            return acc;
        }
        catch
        {
            RestoreInvokeCallerStateOnThrow(newFp, callerFp);
            throw;
        }
    }

    internal JsValue InvokeBytecodeFunctionFromStackWindow(
        JsBytecodeFunction func,
        JsValue thisValue,
        int argOffset,
        int argCount,
        JsValue newTarget,
        int callerPc,
        CallFrameKind frameKind = CallFrameKind.FunctionFrame,
        CallFrameFlag flags = CallFrameFlag.None)
    {
        thisValue = PrepareBytecodeThisValue(func, thisValue, frameKind == CallFrameKind.ConstructFrame);
        var args = Stack.AsSpan(argOffset, argCount);
        if (TryInvokeBytecodeNonOrdinary(func, thisValue, args, frameKind == CallFrameKind.ConstructFrame,
                out var specialResult))
            return specialResult;

        ThrowIfManagedRunDepthExceeded();

        var callerFp = fp;
        var newFp = StackTop;
        var fullStack = Stack.AsSpan();
        var registerWindowSize = Math.Max(func.Script.RegisterCount, argCount);
        if (argOffset + argCount == StackTop)
            newFp = argOffset;
        if (newFp + HeaderSize + registerWindowSize > fullStack.Length)
            throw new StackOverflowException();

        PrepareBytecodeRegisterWindow(fullStack, newFp, argOffset, argCount, registerWindowSize);
        if (newFp != StackTop)
            StackTop = newFp;
        func.Script.ArmBreakpoints();
        PushFrame(func, callerFp, callerPc, argCount, func.BoundParentContext, thisValue,
            PrepareBytecodeNewTargetValue(func, newTarget), frameKind, flags);

        try
        {
            Run(callerFp);
            return acc;
        }
        catch
        {
            RestoreInvokeCallerStateOnThrow(newFp, callerFp);
            throw;
        }
    }

    internal JsValue InvokeBytecodeFunctionWithPrependedArguments(
        JsBytecodeFunction func,
        JsValue thisValue,
        ReadOnlySpan<JsValue> prependedArgs,
        ReadOnlySpan<JsValue> args,
        JsValue newTarget,
        int callerPc,
        CallFrameKind frameKind = CallFrameKind.FunctionFrame,
        CallFrameFlag flags = CallFrameFlag.None)
    {
        thisValue = PrepareBytecodeThisValue(func, thisValue, frameKind == CallFrameKind.ConstructFrame);

        var totalArgCount = prependedArgs.Length + args.Length;
        var callerFp = fp;
        var newFp = StackTop;
        var fullStack = Stack.AsSpan();
        var registerWindowSize = Math.Max(func.Script.RegisterCount, totalArgCount);
        var requiredTop = newFp + HeaderSize + registerWindowSize;
        if (requiredTop > fullStack.Length)
            throw new StackOverflowException();

        var mergedArgs = fullStack.Slice(newFp + HeaderSize, totalArgCount);
        prependedArgs.CopyTo(mergedArgs);
        args.CopyTo(mergedArgs[prependedArgs.Length..]);
        fullStack.Slice(newFp + HeaderSize + totalArgCount, registerWindowSize - totalArgCount).Fill(JsValue.Undefined);

        if (TryInvokeBytecodeNonOrdinary(func, thisValue, mergedArgs,
                frameKind == CallFrameKind.ConstructFrame, out var specialResult))
        {
            mergedArgs.Fill(JsValue.Undefined);
            return specialResult;
        }

        ThrowIfManagedRunDepthExceeded();

        func.Script.ArmBreakpoints();
        PushFrame(func, callerFp, callerPc, totalArgCount, func.BoundParentContext, thisValue,
            PrepareBytecodeNewTargetValue(func, newTarget), frameKind, flags);
        try
        {
            Run(callerFp);
            return acc;
        }
        catch
        {
            RestoreInvokeCallerStateOnThrow(newFp, callerFp);
            throw;
        }
    }

    internal JsValue InvokeBytecodeFunctionWithPrependedArguments(
        JsBytecodeFunction func,
        JsValue thisValue,
        ReadOnlySpan<JsValue> prependedArgs,
        int argOffset,
        int argCount,
        JsValue newTarget,
        int callerPc,
        CallFrameKind frameKind = CallFrameKind.FunctionFrame,
        CallFrameFlag flags = CallFrameFlag.None)
    {
        thisValue = PrepareBytecodeThisValue(func, thisValue, frameKind == CallFrameKind.ConstructFrame);

        var totalArgCount = prependedArgs.Length + argCount;
        var callerFp = fp;
        var newFp = StackTop;
        var fullStack = Stack.AsSpan();
        var registerWindowSize = Math.Max(func.Script.RegisterCount, totalArgCount);
        var requiredTop = newFp + HeaderSize + registerWindowSize;
        if (requiredTop > fullStack.Length)
            throw new StackOverflowException();

        var mergedArgs = fullStack.Slice(newFp + HeaderSize, totalArgCount);
        prependedArgs.CopyTo(mergedArgs);
        fullStack.Slice(argOffset, argCount).CopyTo(mergedArgs[prependedArgs.Length..]);
        fullStack.Slice(newFp + HeaderSize + totalArgCount, registerWindowSize - totalArgCount).Fill(JsValue.Undefined);

        if (TryInvokeBytecodeNonOrdinary(func, thisValue, mergedArgs,
                frameKind == CallFrameKind.ConstructFrame, out var specialResult))
        {
            mergedArgs.Fill(JsValue.Undefined);
            return specialResult;
        }

        ThrowIfManagedRunDepthExceeded();

        func.Script.ArmBreakpoints();
        PushFrame(func, callerFp, callerPc, totalArgCount, func.BoundParentContext, thisValue,
            PrepareBytecodeNewTargetValue(func, newTarget), frameKind, flags);
        try
        {
            Run(callerFp);
            return acc;
        }
        catch
        {
            RestoreInvokeCallerStateOnThrow(newFp, callerFp);
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsValue PrepareBytecodeThisValueNotConstruct(JsBytecodeFunction func, JsValue thisValue)
    {
        if (func.IsClassConstructor) ThrowNotConstructorCall(func);

        if (func.IsArrow)
        {
            if (func.BoundDerivedSuperCallState is null && func.LexicalThisContextSlot < 0)
                return func.BoundThisValue;
            return TryResolveArrowLexicalThisValue(func, out var lexicalThisValue)
                ? lexicalThisValue
                : func.BoundThisValue;
        }

        if (!func.IsStrict)
            return NormalizeSloppyThisValue(thisValue);
        return thisValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsValue PrepareBytecodeThisValue(JsBytecodeFunction func, JsValue thisValue, bool isConstruct)
    {
        if (func.IsClassConstructor && !isConstruct) ThrowNotConstructorCall(func);

        if (func.IsArrow)
        {
            if (func.BoundDerivedSuperCallState is null && func.LexicalThisContextSlot < 0)
                return func.BoundThisValue;
            return TryResolveArrowLexicalThisValue(func, out var lexicalThisValue)
                ? lexicalThisValue
                : func.BoundThisValue;
        }

        if (!isConstruct && !func.IsStrict)
            return NormalizeSloppyThisValue(thisValue);
        return thisValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNotConstructorCall(JsBytecodeFunction func)
    {
        throw new JsRuntimeException(JsErrorKind.TypeError,
            "Class constructor cannot be invoked without 'new'",
            "CLASS_CONSTRUCTOR_CALL", errorRealm: func.Realm);
    }

    private bool TryResolveArrowLexicalThisValue(JsBytecodeFunction func, out JsValue value)
    {
        if (func.BoundDerivedSuperCallState is { } derivedThisState)
        {
            if (TryGetLiveDerivedSuperCallFrameState(this, derivedThisState, out _, out var liveThisValue))
            {
                value = liveThisValue;
                return true;
            }

            if (derivedThisState.DerivedThisContext is not null &&
                derivedThisState.DerivedThisSlot >= 0 &&
                (uint)derivedThisState.DerivedThisSlot < (uint)derivedThisState.DerivedThisContext.Slots.Length)
            {
                value = derivedThisState.DerivedThisContext.Slots[derivedThisState.DerivedThisSlot];
                return true;
            }
        }

        if (func.LexicalThisContextSlot >= 0 &&
            TryResolveContextChain(func.BoundParentContext, func.LexicalThisContextDepth, out var context) &&
            (uint)func.LexicalThisContextSlot < (uint)context.Slots.Length)
        {
            value = context.Slots[func.LexicalThisContextSlot];
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue ResolveClosureLexicalNewTarget(JsBytecodeFunction currentFunction, JsValue frameNewTarget)
    {
        if (!currentFunction.IsArrow)
            return frameNewTarget;

        return currentFunction.BoundNewTargetValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue PrepareBytecodeNewTargetValue(JsBytecodeFunction func, JsValue requestedNewTarget)
    {
        return func.IsArrow ? func.BoundNewTargetValue : requestedNewTarget;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryResolveContextChain(JsContext? start, int depth, out JsContext context)
    {
        context = null!;
        if (start is null || depth < 0)
            return false;

        context = start;
        for (var i = 0; i < depth; i++)
        {
            if (context.Parent is not { } parent)
                return false;
            context = parent;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTailCallContinuation(ReadOnlySpan<byte> bytecode, int nextOpcodePc)
    {
        if ((uint)nextOpcodePc >= (uint)bytecode.Length)
            return false;
        if (bytecode[nextOpcodePc] == (byte)JsOpCode.Return)
            return true;
        if (bytecode[nextOpcodePc] != (byte)JsOpCode.Jump)
            return false;

        var jumpOperandPc = nextOpcodePc + 1;
        if (jumpOperandPc + 1 >= bytecode.Length)
            return false;

        var jumpTargetPc = jumpOperandPc + 2 + ReadJumpOffset16(bytecode, jumpOperandPc);
        return (uint)jumpTargetPc < (uint)bytecode.Length &&
               bytecode[jumpTargetPc] == (byte)JsOpCode.Return;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryInvokeBytecodeNonOrdinary(JsBytecodeFunction func, JsValue thisValue, ReadOnlySpan<JsValue> args,
        bool isConstruct, out JsValue result)
    {
        if (!isConstruct)
        {
            if (func.Kind is JsBytecodeFunctionKind.Generator or JsBytecodeFunctionKind.AsyncGenerator)
            {
                var generator = CreateGeneratorObject(func, thisValue, args);
                if (func.HasEagerGeneratorParameterBinding)
                {
                    PreflightGeneratorParameterBinding(generator);
                    RefreshGeneratorPrototypeAfterParameterBinding(generator);
                }

                result = generator;
                return true;
            }

            if (func.Kind == JsBytecodeFunctionKind.Async)
            {
                result = StartAsyncBytecodeFunction(func, thisValue, args);
                return true;
            }
        }

        result = JsValue.Undefined;
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RestoreInvokeCallerStateOnThrow(int invokedFrameFp, int callerFp)
    {
        var fullStack = Stack.AsSpan();
        var unwindFp = fp;
        var top = StackTop;
        if (top < invokedFrameFp)
            top = invokedFrameFp;

        while (exceptionHandlerCount != 0 && exceptionHandlerStack[exceptionHandlerCount - 1].FrameFp >= invokedFrameFp)
            exceptionHandlerCount--;

        while ((uint)unwindFp < (uint)fullStack.Length && unwindFp >= invokedFrameFp)
        {
            ref readonly var frame = ref Unsafe.As<JsValue, CallFrame>(ref fullStack[unwindFp]);
            if (frame.FrameKind == CallFrameKind.GeneratorFrame &&
                TryGetActiveGeneratorForFrame(unwindFp, out var generator))
            {
                FinalizeGenerator(generator);
                ClearActiveGeneratorForFrame(unwindFp);
            }

            var caller = frame.CallerFp;
            if (caller == unwindFp)
                break;
            if (caller < invokedFrameFp)
                break;
            unwindFp = caller;
        }

        StackTop = invokedFrameFp;
        fp = callerFp;
        if (top > invokedFrameFp)
            fullStack[invokedFrameFp..top].Fill(JsValue.Undefined);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue StartAsyncBytecodeFunction(JsBytecodeFunction func, JsValue thisValue, ReadOnlySpan<JsValue> args)
    {
        var promise = Intrinsics.CreatePromiseObject();
        var asyncDriver = CreateGeneratorObject(func, thisValue, args);
        asyncDriver.AsyncCompletionPromise = promise;

        try
        {
            asyncDriver.PendingResumeMode = GeneratorResumeMode.Next;
            asyncDriver.PendingResumeValue = JsValue.Undefined;
            var stepResult = ExecuteGeneratorFromStart(asyncDriver);

            if (asyncDriver.State == GeneratorState.Completed)
                Intrinsics.ResolvePromiseWithAssimilation(promise, stepResult);
            else
                AttachAsyncAwaitContinuation(asyncDriver, stepResult);
        }
        catch (JsRuntimeException ex)
        {
            RejectAsyncDriverException(promise, ex);
        }

        return promise;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DispatchCrossRealm(JsFunction callee, int receiverReg,
        int argStartReg,
        int argCount,
        bool isConstruct,
        int callerPc,
        ref JsValue registers)
    {
        var args = Stack.AsSpan(fp + HeaderSize + argStartReg, argCount).ToArray();
        if (callee is JsProxyFunction proxy)
        {
            if (isConstruct)
            {
                acc = proxy.ConstructProxy(this, args, callee, callerPc);
            }
            else
            {
                var crossRealmThisValue = receiverReg < 0 ? JsValue.Undefined : Unsafe.Add(ref registers, receiverReg);
                acc = proxy.InvokeProxy(this, crossRealmThisValue, args);
            }

            return;
        }

        if (isConstruct)
        {
            acc = callee.Realm.ConstructWithExplicitNewTarget(callee, args, callee, callerPc);
        }
        else
        {
            var crossRealmThisValue = receiverReg < 0 ? JsValue.Undefined : Unsafe.Add(ref registers, receiverReg);
            acc = callee.Realm.InvokeFunction(callee, crossRealmThisValue, args);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryDispatchVmStackInvocation(
        JsFunction callee,
        int receiverReg,
        int argStartReg,
        int argCount,
        bool isConstruct,
        bool allowTailCall,
        int callerPc,
        ref JsBytecodeFunction currentFunc,
        ref JsValue registers)
    {
        var argOffset = fp + HeaderSize + argStartReg;
        if (!isConstruct && callee is JsBytecodeFunction directBytecodeTarget)
        {
            var thisValue = receiverReg < 0 ? JsValue.Undefined : Unsafe.Add(ref registers, receiverReg);
            return TryDispatchVmDirectBytecodeInvocation(directBytecodeTarget, thisValue, argOffset, argCount,
                allowTailCall, callerPc, ref currentFunc);
        }

        return TryDispatchVmStackInvocationSlow(callee, receiverReg, argOffset, argCount, isConstruct, allowTailCall,
            callerPc, ref currentFunc, ref registers);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryDispatchVmStackInvocationSlow(
        JsFunction callee,
        int receiverReg,
        int argOffset,
        int argCount,
        bool isConstruct,
        bool allowTailCall,
        int callerPc,
        ref JsBytecodeFunction currentFunc,
        ref JsValue registers)
    {
        JsValue thisValue;
        PreparedConstruct preparedConstruct = default;
        var frameKind = CallFrameKind.FunctionFrame;
        if (!isConstruct)
        {
            thisValue = receiverReg < 0 ? JsValue.Undefined : Unsafe.Add(ref registers, receiverReg);
        }
        else
        {
            preparedConstruct = PrepareConstructInvocation(callee, callee);
            thisValue = preparedConstruct.ThisValue;
            frameKind = CallFrameKind.ConstructFrame;
        }

        var prependedArgs = ReadOnlySpan<JsValue>.Empty;
        JsBytecodeFunction? targetBytecode = null;
        if (callee is JsBytecodeFunction)
        {
            targetBytecode = Unsafe.As<JsBytecodeFunction>(callee);
        }
        else
        {
            if (!TryDispatchVmNonBytecodeInvocation(callee, argOffset, argCount, isConstruct, callerPc,
                    ref thisValue, ref preparedConstruct, ref frameKind, ref prependedArgs, ref targetBytecode))
                return false;
        }

        Debug.Assert(targetBytecode is not null);

        thisValue = PrepareBytecodeThisValue(targetBytecode, thisValue, isConstruct);
        var bytecodeArgOffset = argOffset;
        var bytecodeArgCount = argCount;
        if (!prependedArgs.IsEmpty)
            bytecodeArgOffset =
                PreparePrependedVmBytecodeArguments(prependedArgs, argOffset, argCount, out bytecodeArgCount);

        if (!isConstruct && targetBytecode.Kind != JsBytecodeFunctionKind.Normal)
        {
            acc = InvokeVmNonOrdinaryBytecode(targetBytecode, thisValue, bytecodeArgOffset, bytecodeArgCount,
                !prependedArgs.IsEmpty);
            return false;
        }

        if (allowTailCall &&
            !isConstruct &&
            frameKind == CallFrameKind.FunctionFrame &&
            !HasActiveExceptionHandlersForFrame(fp))
            ReplaceCurrentBytecodeFrameFromVmStack(targetBytecode, bytecodeArgOffset, bytecodeArgCount, thisValue);
        else
            EnterBytecodeFrameFromVmStack(targetBytecode, bytecodeArgOffset, bytecodeArgCount, callerPc, thisValue,
                preparedConstruct.NewTarget, frameKind, preparedConstruct.Flags);

        currentFunc = targetBytecode;
        return true;
    }


    private bool TryDispatchVmDirectBytecodeInvocation(
        JsBytecodeFunction targetBytecode,
        JsValue thisValue,
        int argOffset,
        int argCount,
        bool allowTailCall,
        int callerPc,
        ref JsBytecodeFunction currentFunc)
    {
        thisValue = PrepareBytecodeThisValueNotConstruct(targetBytecode, thisValue);

        if (targetBytecode.Kind != JsBytecodeFunctionKind.Normal)
        {
            acc = InvokeVmNonOrdinaryBytecode(targetBytecode, thisValue, argOffset, argCount, false);
            return false;
        }

        if (allowTailCall && !HasActiveExceptionHandlersForFrame(fp))
            ReplaceCurrentBytecodeFrameFromVmStack(targetBytecode, argOffset, argCount, thisValue);
        else
            EnterBytecodeFrameFromVmStack(targetBytecode, argOffset, argCount, callerPc, thisValue, JsValue.Undefined,
                CallFrameKind.FunctionFrame, CallFrameFlag.None);

        currentFunc = targetBytecode;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PrepareBytecodeRegisterWindow(
        Span<JsValue> fullStack,
        int frameFp,
        int bytecodeArgOffset,
        int bytecodeArgCount,
        int registerWindowSize)
    {
        var finalArgOffset = frameFp + HeaderSize;
        if (bytecodeArgOffset != finalArgOffset)
            fullStack.Slice(bytecodeArgOffset, bytecodeArgCount).CopyTo(fullStack[finalArgOffset..]);
        fullStack.Slice(finalArgOffset + bytecodeArgCount, registerWindowSize - bytecodeArgCount)
            .Fill(JsValue.Undefined);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReplaceCurrentBytecodeFrameFromVmStack(
        JsBytecodeFunction targetBytecode,
        int bytecodeArgOffset,
        int bytecodeArgCount,
        JsValue thisValue)
    {
        var fullStack = Stack.AsSpan();
        ref readonly var currentFrame = ref Unsafe.As<JsValue, CallFrame>(ref fullStack[fp]);
        var currentTop = StackTop;
        var newFp = fp;
        var registerWindowSize = Math.Max(targetBytecode.Script.RegisterCount, bytecodeArgCount);
        var newTop = newFp + HeaderSize + registerWindowSize;
        if (newTop > fullStack.Length)
            throw new StackOverflowException();

        PrepareBytecodeRegisterWindow(fullStack, newFp, bytecodeArgOffset, bytecodeArgCount, registerWindowSize);

        Unsafe.As<JsValue, CallFrame>(ref fullStack[newFp]) = new(
            targetBytecode,
            currentFrame.CallerFp,
            bytecodeArgCount,
            currentFrame.CallerPc,
            targetBytecode.BoundParentContext,
            thisValue);
        fullStack[newFp + OffsetExtra0] = JsValue.Undefined;
        acc = JsValue.Undefined;
        if (currentTop > newTop)
            fullStack[newTop..currentTop].Fill(JsValue.Undefined);
        StackTop = newTop;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnterBytecodeFrameFromVmStack(JsBytecodeFunction targetBytecode, int bytecodeArgOffset,
        int bytecodeArgCount,
        int callerPc, JsValue thisValue, JsValue newTarget, CallFrameKind frameKind, CallFrameFlag frameFlags)
    {
        var newFp = StackTop;
        var fullStack = Stack.AsSpan();
        var registerWindowSize = Math.Max(targetBytecode.Script.RegisterCount, bytecodeArgCount);
        if (newFp + HeaderSize + registerWindowSize > fullStack.Length)
            throw new StackOverflowException();

        PrepareBytecodeRegisterWindow(fullStack, newFp, bytecodeArgOffset, bytecodeArgCount, registerWindowSize);
        PushFrame(
            targetBytecode,
            fp,
            callerPc,
            bytecodeArgCount,
            targetBytecode.BoundParentContext,
            thisValue,
            PrepareBytecodeNewTargetValue(targetBytecode, newTarget),
            frameKind,
            frameFlags);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryDispatchVmNonBytecodeInvocation(
        JsFunction callee,
        int argOffset,
        int argCount,
        bool isConstruct,
        int callerPc,
        ref JsValue thisValue,
        ref PreparedConstruct preparedConstruct,
        ref CallFrameKind frameKind,
        ref ReadOnlySpan<JsValue> prependedArgs,
        ref JsBytecodeFunction? targetBytecode)
    {
        ref var acc = ref this.acc;
        if (callee is JsHostFunction jsHostFunction)
            acc = InvokeHostFunctionFromStackWithExitFrame(
                jsHostFunction,
                thisValue,
                callerPc,
                preparedConstruct.NewTarget,
                preparedConstruct.Flags,
                argOffset,
                argCount);
        else if (callee is JsBoundFunction bound)
            return TryHandleVmBoundInvocation(bound, callee, argOffset, argCount, isConstruct, callerPc,
                ref thisValue, ref preparedConstruct, ref frameKind, ref prependedArgs, ref targetBytecode, ref acc);
        else if (callee is JsProxyFunction proxy)
            acc = proxy.DispatchProxyFromStack(
                this,
                thisValue,
                argOffset,
                argCount,
                preparedConstruct.NewTarget,
                callerPc,
                isConstruct);

#if DEBUG
        else
            throw new JsRuntimeException(JsErrorKind.TypeError, "Assume not there is no other type");
#endif

        if (isConstruct)
            acc = CompleteConstructResult(acc, thisValue, preparedConstruct.Flags);
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue InvokeVmNonOrdinaryBytecode(
        JsBytecodeFunction bytecodeTarget,
        JsValue thisValue,
        int argOffset,
        int argCount,
        bool hasPrependedArgs)
    {
        var args = Stack.AsSpan(argOffset, argCount);
        switch (bytecodeTarget.Kind)
        {
            case JsBytecodeFunctionKind.Generator:
            case JsBytecodeFunctionKind.AsyncGenerator:
            {
                var generator = CreateGeneratorObject(bytecodeTarget, thisValue, args);
                if (bytecodeTarget.HasEagerGeneratorParameterBinding)
                {
                    PreflightGeneratorParameterBinding(generator);
                    RefreshGeneratorPrototypeAfterParameterBinding(generator);
                }

                if (hasPrependedArgs)
                    args.Fill(JsValue.Undefined);
                return generator;
            }
            case JsBytecodeFunctionKind.Async:
            {
                var result = StartAsyncBytecodeFunction(bytecodeTarget, thisValue, args);
                if (hasPrependedArgs)
                    args.Fill(JsValue.Undefined);
                return result;
            }
            default:
                throw new UnreachableException();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryHandleVmBoundInvocation(
        JsBoundFunction bound,
        JsObject calleeObject,
        int argOffset,
        int argCount,
        bool isConstruct,
        int callerPc,
        ref JsValue thisValue,
        ref PreparedConstruct preparedConstruct,
        ref CallFrameKind frameKind,
        ref ReadOnlySpan<JsValue> prependedArgs,
        ref JsBytecodeFunction? targetBytecode,
        ref JsValue acc)
    {
        if (!ReferenceEquals(bound.Target.Realm, this))
        {
            acc = isConstruct
                ? bound.ConstructBoundFromStack(this, argOffset, argCount, preparedConstruct.NewTarget, callerPc)
                : bound.InvokeBoundFromStack(this, argOffset, argCount, callerPc);
            if (isConstruct)
                acc = CompleteConstructResult(acc, thisValue, preparedConstruct.Flags);
            return false;
        }

        if (bound.Target is JsBytecodeFunction boundTargetBytecode)
        {
            targetBytecode = boundTargetBytecode;
            prependedArgs = bound.BoundArguments;
            thisValue = bound.BoundThis;
            preparedConstruct = default;
            frameKind = CallFrameKind.FunctionFrame;

            if (isConstruct)
            {
                JsValue rewrittenNewTarget = ReferenceEquals(calleeObject, bound) ? bound.Target : bound;
                var preparedBoundConstruct = PrepareConstructInvocation(bound.Target, rewrittenNewTarget);
                thisValue = preparedBoundConstruct.ThisValue;
                preparedConstruct = preparedBoundConstruct;
                frameKind = CallFrameKind.ConstructFrame;
            }

            return true;
        }

        acc = isConstruct
            ? bound.ConstructBoundFromStack(this, argOffset, argCount, preparedConstruct.NewTarget, callerPc)
            : bound.InvokeBoundFromStack(this, argOffset, argCount, callerPc);
        if (isConstruct)
            acc = CompleteConstructResult(acc, thisValue, preparedConstruct.Flags);
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int PreparePrependedVmBytecodeArguments(
        ReadOnlySpan<JsValue> prependedArgs,
        int argOffset,
        int argCount,
        out int totalArgCount)
    {
        totalArgCount = prependedArgs.Length + argCount;
        var fullStack = Stack.AsSpan();
        if (StackTop + HeaderSize + totalArgCount > fullStack.Length)
            throw new StackOverflowException();

        var mergedOffset = StackTop + HeaderSize;
        var mergedArgs = fullStack.Slice(mergedOffset, totalArgCount);
        prependedArgs.CopyTo(mergedArgs);
        fullStack.Slice(argOffset, argCount).CopyTo(mergedArgs[prependedArgs.Length..]);
        return mergedOffset;
    }

    internal JsValue ConstructWithExplicitNewTarget(
        JsFunction callee,
        ReadOnlySpan<JsValue> args,
        JsValue newTarget,
        int callerPc)
    {
        if (!ReferenceEquals(callee.Realm, this) && callee is not JsProxyFunction)
            return callee.Realm.ConstructWithExplicitNewTarget(callee, args, newTarget, callerPc);

        var prepared = PrepareConstructInvocation(callee, newTarget);
        var result = callee is JsBytecodeFunction targetBytecode
            ? InvokeBytecodeFunction(targetBytecode, prepared.ThisValue, args, prepared.NewTarget,
                CallFrameKind.ConstructFrame, prepared.Flags)
            : callee.InvokeNonBytecodeConstruct(this, prepared.ThisValue, args, prepared.NewTarget, callerPc,
                prepared.Flags);
        return CompleteConstructResult(result, prepared.ThisValue, prepared.Flags);
    }

    internal JsArray CreateArrayFromArgumentWindow(ReadOnlySpan<JsValue> args)
    {
        var argArray = CreateArrayObject();
        for (uint i = 0; i < (uint)args.Length; i++)
            argArray.SetElement(i, args[(int)i]);
        return argArray;
    }

    internal JsArray CreateArrayFromArgumentWindow(int argOffset, int argCount)
    {
        var argArray = CreateArrayObject();
        var args = Stack.AsSpan(argOffset, argCount);
        for (uint i = 0; i < (uint)argCount; i++)
            argArray.SetElement(i, args[(int)i]);
        return argArray;
    }

    internal ReadOnlySpan<JsValue> StackAsSpan(int argOffset, int argCount)
    {
        return Stack.AsSpan(argOffset, argCount);
    }

    internal JsValue InvokeFunctionWithArrayLikeArguments(JsFunction fn, JsValue thisValue, in JsValue argumentsList,
        int callerPc)
    {
        if (TryInvokeStringFromCodePointDenseArrayFastPath(fn, argumentsList, out var fastPathResult))
            return fastPathResult;

        var savedSp = StackTop;
        var argOffset = CopyArrayLikeArgumentsToStackTop(argumentsList, out var argCount);
        try
        {
            return DispatchCallFromStack(fn, thisValue, argOffset, argCount, callerPc);
        }
        finally
        {
            RestoreTemporaryArgumentWindow(savedSp);
        }
    }

    internal JsValue ConstructWithArrayLikeArguments(JsFunction fn, in JsValue argumentsList, JsValue newTarget,
        int callerPc)
    {
        var savedSp = StackTop;
        var argOffset = CopyArrayLikeArgumentsToStackTop(argumentsList, out var argCount);
        try
        {
            var prepared = PrepareConstructInvocation(fn, newTarget);
            return DispatchConstructFromStack(fn, prepared, argOffset, argCount, callerPc);
        }
        finally
        {
            RestoreTemporaryArgumentWindow(savedSp);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CopyPrependedArgumentsToStackTop(ReadOnlySpan<JsValue> prependedArgs, ReadOnlySpan<JsValue> args)
    {
        var argOffset = StackTop;
        var totalCount = prependedArgs.Length + args.Length;
        var fullStack = Stack.AsSpan();
        if (argOffset + totalCount > fullStack.Length)
            throw new StackOverflowException();

        prependedArgs.CopyTo(fullStack[argOffset..]);
        args.CopyTo(fullStack[(argOffset + prependedArgs.Length)..]);
        StackTop = argOffset + totalCount;
        return argOffset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CopyPrependedArgumentsToStackTop(ReadOnlySpan<JsValue> prependedArgs, int argOffset, int argCount)
    {
        var mergedOffset = StackTop;
        var totalCount = prependedArgs.Length + argCount;
        var fullStack = Stack.AsSpan();
        if (mergedOffset + totalCount > fullStack.Length)
            throw new StackOverflowException();

        prependedArgs.CopyTo(fullStack[mergedOffset..]);
        fullStack.Slice(argOffset, argCount).CopyTo(fullStack[(mergedOffset + prependedArgs.Length)..]);
        StackTop = mergedOffset + totalCount;
        return mergedOffset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CopyArrayLikeArgumentsToStackTop(in JsValue value, out int argCount)
    {
        if (!value.TryGetObject(out var obj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "CreateListFromArrayLike requires object");

        if (TryCopyDenseArrayLikeArgumentsToStackTop(obj, out argCount, out var denseArgOffset))
            return denseArgOffset;

        var lengthLong = GetArrayLikeLengthLong(this, obj);
        argCount = lengthLong <= 0
            ? 0
            : lengthLong >= int.MaxValue
                ? int.MaxValue
                : (int)lengthLong;

        var argOffset = StackTop;
        var fullStack = Stack.AsSpan();
        if (argOffset + argCount > fullStack.Length)
            throw new StackOverflowException();

        for (uint i = 0; i < (uint)argCount; i++)
            if (!obj.TryGetElement(i, out fullStack[argOffset + (int)i]))
                fullStack[argOffset + (int)i] = JsValue.Undefined;

        StackTop = argOffset + argCount;
        return argOffset;
    }

    private bool TryInvokeStringFromCodePointDenseArrayFastPath(JsFunction fn, in JsValue argumentsList,
        out JsValue result)
    {
        if (!ReferenceEquals(fn, Intrinsics.StringFromCodePointFunction) ||
            !argumentsList.TryGetObject(out var obj) ||
            obj is not JsArray { Dense: { } denseArray } array ||
            array.IndexedProperties is not null)
        {
            result = default;
            return false;
        }

        var argCount = array.Length >= int.MaxValue ? int.MaxValue : (int)array.Length;
        if (argCount == 0)
        {
            result = JsValue.FromString(string.Empty);
            return true;
        }

        var rented = ArrayPool<char>.Shared.Rent(argCount * 2);
        try
        {
            var length = 0;
            for (var i = 0; i < argCount; i++)
            {
                var value = (uint)i < (uint)denseArray.Length ? denseArray[i] : JsValue.Undefined;
                if (value.IsTheHole)
                    value = JsValue.Undefined;

                var n = value.IsNumber ? value.NumberValue : this.ToNumberSlowPath(value);
                if (double.IsNaN(n) || double.IsInfinity(n) || n != Math.Truncate(n) || n < 0d || n > 0x10FFFF)
                    throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid code point");

                var codePoint = (int)n;
                if (codePoint <= char.MaxValue)
                {
                    rented[length++] = (char)codePoint;
                    continue;
                }

                codePoint -= 0x10000;
                rented[length++] = (char)((codePoint >> 10) + 0xD800);
                rented[length++] = (char)((codePoint & 0x3FF) + 0xDC00);
            }

            result = JsValue.FromString(new string(rented, 0, length));
            return true;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private bool TryCopyDenseArrayLikeArgumentsToStackTop(JsObject obj, out int argCount, out int argOffset)
    {
        if (obj is not JsArray { Dense: { } denseArray } array || array.IndexedProperties is not null)
        {
            argCount = 0;
            argOffset = 0;
            return false;
        }

        argCount = array.Length >= int.MaxValue ? int.MaxValue : (int)array.Length;
        argOffset = StackTop;
        var fullStack = Stack.AsSpan();
        if (argOffset + argCount > fullStack.Length)
            throw new StackOverflowException();

        for (var i = 0; i < argCount; i++)
        {
            if ((uint)i < (uint)denseArray.Length)
            {
                var value = denseArray[i];
                fullStack[argOffset + i] = value.IsTheHole ? JsValue.Undefined : value;
                continue;
            }

            fullStack[argOffset + i] = JsValue.Undefined;
        }

        StackTop = argOffset + argCount;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void RestoreTemporaryArgumentWindow(int savedSp)
    {
        var top = StackTop;
        if (top < savedSp)
            top = savedSp;
        StackTop = savedSp;
        if (top > savedSp)
            Stack.AsSpan(savedSp, top - savedSp).Fill(JsValue.Undefined);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal PreparedConstruct PrepareConstructInvocation(JsFunction callee, JsValue newTarget)
    {
        if (callee is JsBytecodeFunction
            {
                Kind: JsBytecodeFunctionKind.Generator or JsBytecodeFunctionKind.Async
                or JsBytecodeFunctionKind.AsyncGenerator
            })
            ThrowGeneratorNotConstructor();

        if (callee is JsBytecodeFunction { IsArrow: true })
            ThrowNonCallable(true);
        if (!IsConstructableFunction(callee))
            ThrowNonCallable(true);

        if (callee is JsBytecodeFunction { IsDerivedConstructor: true })
            return new(JsValue.TheHole, newTarget,
                CallFrameFlag.IsConstructorCall | CallFrameFlag.IsDerivedConstructorCall);

        var intrinsics = callee.Realm.Intrinsics;
        if (ReferenceEquals(callee, intrinsics.ArrayBufferConstructor))
            return new(JsValue.Undefined, newTarget, CallFrameFlag.IsConstructorCall);

        if (ReferenceEquals(callee, intrinsics.DataViewConstructor))
            return new(JsValue.Undefined, newTarget, CallFrameFlag.IsConstructorCall);

        if (ReferenceEquals(callee, intrinsics.SharedArrayBufferConstructor))
            return new(JsValue.Undefined, newTarget, CallFrameFlag.IsConstructorCall);

        if (ReferenceEquals(callee, intrinsics.PromiseConstructor))
            return new(JsValue.Undefined, newTarget, CallFrameFlag.IsConstructorCall);

        if (ReferenceEquals(callee, intrinsics.TypedArrayConstructor) || (
                callee is JsHostFunction hostFunction &&
                Array.IndexOf(intrinsics.TypedArrayConstructors, hostFunction) >= 0))
            return new(JsValue.Undefined, newTarget, CallFrameFlag.IsConstructorCall);

        return new(CreateConstructReceiver(newTarget, callee), newTarget,
            CallFrameFlag.IsConstructorCall);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static JsValue CompleteConstructResult(JsValue result, JsValue thisValue, CallFrameFlag frameFlags)
    {
        var isConstructor = (frameFlags & CallFrameFlag.IsConstructorCall) != 0;
        if (!isConstructor)
            return result;

        var isDerived = (frameFlags & CallFrameFlag.IsDerivedConstructorCall) != 0;
        if (!isDerived)
            return result.IsObject ? result : thisValue;

        if (result.IsObject)
            return result;

        if (result.IsUndefined)
        {
            if (thisValue.IsTheHole)
                ThrowSuperNotCalled();
            return thisValue;
        }

        if (result.IsTheHole)
            ThrowSuperNotCalled();

        ThrowTypeError("DERIVED_CTOR_RETURN_PRIMITIVE",
            "Derived constructors may only return object or undefined");
        return JsValue.Undefined;
    }

    private JsPlainObject CreateConstructReceiver(JsValue newTarget, JsFunction fallbackCtor)
    {
        JsObject? receiverPrototype = Intrinsics.ObjectPrototype;
        var resolved = false;
        if (newTarget.TryGetObject(out var newTargetObj) && newTargetObj is JsFunction newTargetCtor)
        {
            JsValue newTargetPrototypeValue;
            if (!newTargetCtor.TryGetPropertyAtom(this, IdPrototype, out newTargetPrototypeValue, out _))
                newTargetPrototypeValue = JsValue.Undefined;

            if (newTargetPrototypeValue.TryGetObject(out var newTargetPrototypeObj))
                receiverPrototype = newTargetPrototypeObj;
            else
                receiverPrototype =
                    GetDefaultConstructPrototypeForRealm(Intrinsics.GetFunctionRealm(this, newTargetCtor),
                        fallbackCtor);

            resolved = true;
        }

        if (!resolved &&
            fallbackCtor.TryGetPropertyAtom(this, IdPrototype, out var prototypeValue, out _) &&
            prototypeValue.TryGetObject(out var prototypeObj))
        {
            receiverPrototype = prototypeObj;
            resolved = true;
        }

        if (!resolved)
            receiverPrototype =
                GetDefaultConstructPrototypeForRealm(Intrinsics.GetFunctionRealm(this, fallbackCtor), fallbackCtor);

        return new(this, false)
        {
            Prototype = receiverPrototype
        };
    }

    private static JsObject GetDefaultConstructPrototypeForRealm(JsRealm realm, JsFunction fallbackCtor)
    {
        var intrinsics = realm.Intrinsics;
        var fallbackCtorIntrinsics = fallbackCtor.Realm.Intrinsics;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.ArrayConstructor))
            return intrinsics.ArrayPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.NumberConstructor))
            return intrinsics.NumberPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.BooleanConstructor))
            return intrinsics.BooleanPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.StringConstructor))
            return intrinsics.StringPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.BigIntConstructor))
            return intrinsics.BigIntPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.PromiseConstructor))
            return intrinsics.PromisePrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.ErrorConstructor))
            return intrinsics.ErrorPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.TypeErrorConstructor))
            return intrinsics.TypeErrorPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.ReferenceErrorConstructor))
            return intrinsics.ReferenceErrorPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.RangeErrorConstructor))
            return intrinsics.RangeErrorPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.SyntaxErrorConstructor))
            return intrinsics.SyntaxErrorPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.EvalErrorConstructor))
            return intrinsics.EvalErrorPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.UriErrorConstructor))
            return intrinsics.UriErrorPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.AggregateErrorConstructor))
            return intrinsics.AggregateErrorPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.WeakRefConstructor))
            return intrinsics.WeakRefPrototype;
        if (ReferenceEquals(fallbackCtor, fallbackCtorIntrinsics.FinalizationRegistryConstructor))
            return intrinsics.FinalizationRegistryPrototype;
        return intrinsics.ObjectPrototype;
    }

    public JsArray CreateArray()
    {
        return CreateArrayObject();
    }

    internal JsArray CreateArrayObject()
    {
        var array = new JsArray(this)
        {
            Prototype = Intrinsics.ArrayPrototype
        };
        return array;
    }

    internal JsArray CreateArrayObject(ReadOnlySpan<JsValue> denseElements)
    {
        var array = CreateArrayObject();
        var dense = array.InitializeDenseElementsNoCollision(denseElements.Length);
        denseElements.CopyTo(dense);
        return array;
    }

    internal JsArray CreateArrayObjectWithLength(int length)
    {
        var array = CreateArrayObject();
        var dense = array.InitializeDenseElementsNoCollision(length);
        if (length > 0)
            Array.Fill(dense, JsValue.TheHole);
        return array;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue Add(JsValue a, int b)
    {
        if (a.IsInt32)
        {
            var res = (long)a.Int32Value + b;
            var intRes = (int)res;
            if (intRes == res)
                return JsValue.FromInt32(intRes);
        }

        return new(a.NumberValue + b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue Sub(JsValue a, int b)
    {
        if (a.IsInt32)
        {
            var res = (long)a.Int32Value - b;
            var intRes = (int)res;
            if (intRes == res)
                return JsValue.FromInt32(intRes);
        }

        return new(a.NumberValue - b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue Mul(JsValue a, int b)
    {
        if (a.IsInt32)
        {
            var res = (long)a.Int32Value * b;
            var intRes = (int)res;
            if (intRes == res)
                return JsValue.FromInt32(intRes);
        }

        return new(a.NumberValue * b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue Add(JsValue a, JsValue b)
    {
        if (a.IsInt32 && b.IsInt32)
        {
            var res = (long)a.Int32Value + b.Int32Value;
            var intRes = (int)res;
            if (intRes == res)
                return JsValue.FromInt32(intRes);
        }

        return new(a.NumberValue + b.NumberValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue Sub(JsValue a, JsValue b)
    {
        if (a.IsInt32 && b.IsInt32)
        {
            var res = (long)a.Int32Value - b.Int32Value;
            var intRes = (int)res;
            if (intRes == res)
                return JsValue.FromInt32(intRes);
        }

        return new(a.NumberValue - b.NumberValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue Mul(JsValue a, JsValue b)
    {
        if (a.IsInt32 && b.IsInt32)
        {
            var res = (long)a.Int32Value * b.Int32Value;
            var intRes = (int)res;
            if (intRes == res)
                return JsValue.FromInt32(intRes);
        }

        return new(a.NumberValue * b.NumberValue);
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue HandleArithmeticNonNumberSmiSlowPath(JsRealm realm, JsOpCode op, in JsValue lhs, int imm)
    {
        var leftNumeric = realm.ToNumericSlowPath(lhs);
        if (leftNumeric.IsBigInt)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Cannot mix BigInt and other types, use explicit conversions");

        var a = realm.ToNumberSlowPath(leftNumeric);
        var result = op switch
        {
            JsOpCode.SubSmi => a - imm,
            JsOpCode.MulSmi => a * imm,
            JsOpCode.ModSmi => a % imm,
            JsOpCode.ExpSmi => NumberExponentiate(a, imm),
            _ => throw new NotImplementedException($"Arithmetic Smi opcode {op} not implemented.")
        };
        return new(result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue HandleArithmeticNonNumberSlowPath(JsRealm realm, JsOpCode op, JsValue lhs,
        JsValue rhs)
    {
        if (op == JsOpCode.Add)
        {
            var leftPrim = realm.ToPrimitiveDefaultHintSlowPath(lhs);
            var rightPrim = realm.ToPrimitiveDefaultHintSlowPath(rhs);
            if (leftPrim.IsString || rightPrim.IsString)
                return JsValue.FromString(JsString.Concat(
                    realm.ToJsStringValueSlowPath(leftPrim),
                    realm.ToJsStringValueSlowPath(rightPrim)));

            if (leftPrim.IsBigInt || rightPrim.IsBigInt)
            {
                if (!leftPrim.IsBigInt || !rightPrim.IsBigInt)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Cannot mix BigInt and other types, use explicit conversions");
                return JsValue.FromBigInt(new(leftPrim.AsBigInt().Value + rightPrim.AsBigInt().Value));
            }

            return new(realm.ToNumberSlowPath(leftPrim) + realm.ToNumberSlowPath(rightPrim));
        }

        var leftValue = realm.ToNumericSlowPath(lhs);
        var rightValue = realm.ToNumericSlowPath(rhs);
        if (leftValue.IsBigInt || rightValue.IsBigInt)
        {
            if (!leftValue.IsBigInt || !rightValue.IsBigInt)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Cannot mix BigInt and other types, use explicit conversions");

            var left = leftValue.AsBigInt().Value;
            var right = rightValue.AsBigInt().Value;
            return op switch
            {
                JsOpCode.Sub => JsValue.FromBigInt(new(left - right)),
                JsOpCode.Mul => JsValue.FromBigInt(new(left * right)),
                JsOpCode.Div => right.IsZero
                    ? throw new JsRuntimeException(JsErrorKind.RangeError, "Division by zero")
                    : JsValue.FromBigInt(new(left / right)),
                JsOpCode.Mod => right.IsZero
                    ? throw new JsRuntimeException(JsErrorKind.RangeError, "Division by zero")
                    : JsValue.FromBigInt(new(left % right)),
                JsOpCode.Exp => JsValue.FromBigInt(new(BigIntPow(left, right))),
                _ => throw new NotImplementedException($"Arithmetic opcode {op} not implemented.")
            };
        }

        var a = realm.ToNumberSlowPath(leftValue);
        var b = realm.ToNumberSlowPath(rightValue);
        var result = op switch
        {
            JsOpCode.Sub => a - b,
            JsOpCode.Mul => a * b,
            JsOpCode.Div => a / b,
            JsOpCode.Mod => a % b,
            JsOpCode.Exp => NumberExponentiate(a, b),
            _ => throw new NotImplementedException($"Arithmetic opcode {op} not implemented.")
        };
        return new(result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BigInteger BigIntPow(BigInteger value, BigInteger exponent)
    {
        if (exponent.Sign < 0)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Exponent must be positive");
        if (exponent > int.MaxValue)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Exponent is too large");
        return BigInteger.Pow(value, (int)exponent);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double NumberExponentiate(double @base, double exponent)
    {
        if (exponent == 0d)
            return 1d;

        if (double.IsInfinity(exponent) && Math.Abs(@base) == 1d)
            return double.NaN;

        return Math.Pow(@base, exponent);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue AddSmiSlowPath(JsRealm realm, JsValue lhs, int imm)
    {
        var leftPrim = realm.ToPrimitiveDefaultHintSlowPath(lhs);
        if (leftPrim.IsString)
            return JsValue.FromString(JsString.Concat(
                realm.ToJsStringValueSlowPath(leftPrim),
                imm.ToString(CultureInfo.InvariantCulture)));

        if (leftPrim.IsBigInt)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Cannot mix BigInt and other types, use explicit conversions");
        return new(realm.ToNumberSlowPath(leftPrim) + imm);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue IncrementSlowPath(JsRealm realm, JsValue value, int delta)
    {
        var primitive = realm.ToPrimitiveSlowPath(value, false);
        if (primitive.IsBigInt)
            return IncrementBigIntSlowPath(primitive, delta);
        return new(realm.ToNumberSlowPath(primitive) + delta);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue IncrementBigIntSlowPath(JsValue value, int delta)
    {
        return JsValue.FromBigInt(new(value.AsBigInt().Value + delta));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue BitwiseNotBigIntSlowPath(JsValue value)
    {
        return JsValue.FromBigInt(new(~value.AsBigInt().Value));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue HandleBigIntBitwiseFastSlowPath(JsOpCode op, JsValue lhs, JsValue rhs)
    {
        var left = lhs.AsBigInt().Value;
        var right = rhs.AsBigInt().Value;
        return op switch
        {
            JsOpCode.BitwiseAnd => JsValue.FromBigInt(new(left & right)),
            JsOpCode.BitwiseOr => JsValue.FromBigInt(new(left | right)),
            JsOpCode.BitwiseXor => JsValue.FromBigInt(new(left ^ right)),
            JsOpCode.ShiftLeft => JsValue.FromBigInt(new(ShiftBigInt(left, right, true))),
            JsOpCode.ShiftRight => JsValue.FromBigInt(new(ShiftBigInt(left, right, false))),
            JsOpCode.ShiftRightLogical => throw new JsRuntimeException(JsErrorKind.TypeError,
                "BigInts have no unsigned right shift, use >> instead"),
            _ => throw new NotImplementedException($"Bitwise opcode {op} not implemented.")
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsObject ToObjectForPropertyAccessSlowPath(JsRealm realm, in JsValue receiver)
    {
        if (receiver.IsNullOrUndefined)
            ThrowTypeError("PROPERTY_READ_ON_NULLISH", "Cannot read properties of null or undefined");
        return realm.BoxPrimitiveForPropertyAccess(receiver);
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue HandleComparisonSlowPath(JsRealm realm, JsOpCode op, in JsValue lhs, in JsValue rhs)
    {
        var result = op switch
        {
            JsOpCode.TestLessThan => CompareRelational(realm, op, lhs, rhs),
            JsOpCode.TestGreaterThan => CompareRelational(realm, op, lhs, rhs),
            JsOpCode.TestLessThanOrEqual => CompareRelational(realm, op, lhs, rhs),
            JsOpCode.TestGreaterThanOrEqual => CompareRelational(realm, op, lhs, rhs),
            _ => throw new NotImplementedException($"Comparison opcode {op} not implemented.")
        };
        return result ? JsValue.True : JsValue.False;

        static bool CompareRelational(JsRealm realm, JsOpCode comparisonOp, JsValue left, JsValue right)
        {
            var relation = comparisonOp switch
            {
                JsOpCode.TestLessThan => AbstractRelationalComparison(realm, left, right, true),
                JsOpCode.TestGreaterThan => AbstractRelationalComparison(realm, right, left, false),
                JsOpCode.TestLessThanOrEqual => AbstractRelationalComparison(realm, right, left, false),
                JsOpCode.TestGreaterThanOrEqual => AbstractRelationalComparison(realm, left, right, true),
                _ => null
            };

            return comparisonOp switch
            {
                JsOpCode.TestLessThan => relation == true,
                JsOpCode.TestGreaterThan => relation == true,
                JsOpCode.TestLessThanOrEqual => relation == false,
                JsOpCode.TestGreaterThanOrEqual => relation == false,
                _ => false
            };
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue HandleEqualityCompare(JsRealm realm, JsOpCode op, in JsValue lhs, in JsValue rhs)
    {
        var result = op switch
        {
            JsOpCode.TestEqualStrict => StrictEquals(lhs, rhs),
            JsOpCode.TestEqual => AbstractEquals(realm, lhs, rhs),
            JsOpCode.TestNotEqual => !AbstractEquals(realm, lhs, rhs),
            _ => throw new NotImplementedException($"Equality opcode {op} not implemented.")
        };
        return result ? JsValue.True : JsValue.False;
    }

    internal static bool StrictEquals(in JsValue a, in JsValue b)
    {
        if (a.IsNumber && b.IsNumber)
        {
            var da = a.FastNumberValue;
            var db = b.FastNumberValue;
            return da == db;
        }

        if (a.IsString && b.IsString) return a.AsJsString().Equals(b.AsJsString());
        if (a.IsBigInt && b.IsBigInt) return a.AsBigInt().Equals(b.AsBigInt());
        if (a.IsSymbol && b.IsSymbol) return ReferenceEquals(a.AsSymbol(), b.AsSymbol());
        if (a.IsObject && b.IsObject) return ReferenceEquals(a.AsObject(), b.AsObject());
        if (a.IsBool && b.IsBool) return a.IsTrue == b.IsTrue;
        if (a.IsNull && b.IsNull) return true;
        if (a.IsUndefined && b.IsUndefined) return true;
        if (a.IsTheHole && b.IsTheHole) return true;
        return false;
    }

    internal static bool AbstractEquals(JsRealm realm, in JsValue a, in JsValue b)
    {
        var x = a;
        var y = b;

        while (true)
        {
            if (StrictEquals(x, y))
                return true;

            if ((x.IsNull && y.IsUndefined) || (x.IsUndefined && y.IsNull))
                return true;

            if (x.IsNumber && y.IsString)
            {
                y = new(realm.ToNumberSlowPath(y));
                continue;
            }

            if (x.IsString && y.IsNumber)
            {
                x = new(realm.ToNumberSlowPath(x));
                continue;
            }

            if (x.IsBigInt && y.IsString)
                return Intrinsics.TryParseBigIntString(y.AsString(), out var parsedY) && x.AsBigInt().Equals(parsedY);

            if (x.IsString && y.IsBigInt)
                return Intrinsics.TryParseBigIntString(x.AsString(), out var parsedX) && parsedX.Equals(y.AsBigInt());

            if (x.IsBigInt && y.IsNumber)
                return BigIntEqualsNumber(x.AsBigInt().Value, y.NumberValue);

            if (x.IsNumber && y.IsBigInt)
                return BigIntEqualsNumber(y.AsBigInt().Value, x.NumberValue);

            if (x.IsBool)
            {
                x = new(x.IsTrue ? 1d : 0d);
                continue;
            }

            if (y.IsBool)
            {
                y = new(y.IsTrue ? 1d : 0d);
                continue;
            }

            if ((x.IsString || x.IsNumber || x.IsBigInt || x.IsSymbol) && y.IsObject)
            {
                y = realm.ToPrimitiveDefaultHintSlowPath(y);
                continue;
            }

            if (x.IsObject && (y.IsString || y.IsNumber || y.IsBigInt || y.IsSymbol))
            {
                x = realm.ToPrimitiveDefaultHintSlowPath(x);
                continue;
            }

            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue HandleComparisonSmiSlowPath(JsRealm realm, JsOpCode op, in JsValue lhs, double imm)
    {
        var rhs = new JsValue(imm);
        var result = op switch
        {
            JsOpCode.TestLessThanSmi => AbstractRelationalComparison(realm, lhs, rhs, true) == true,
            JsOpCode.TestGreaterThanSmi => AbstractRelationalComparison(realm, rhs, lhs, false) == true,
            JsOpCode.TestLessThanOrEqualSmi => AbstractRelationalComparison(realm, rhs, lhs, false) ==
                                               false,
            JsOpCode.TestGreaterThanOrEqualSmi => AbstractRelationalComparison(realm, lhs, rhs, true) ==
                                                  false,
            _ => throw new NotImplementedException($"Comparison opcode {op} not implemented.")
        };
        return result ? JsValue.True : JsValue.False;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool? AbstractRelationalComparison(JsRealm realm, JsValue x, JsValue y, bool leftFirst)
    {
        JsValue px;
        JsValue py;
        if (leftFirst)
        {
            px = x.IsObject ? realm.ToPrimitiveSlowPath(x, false) : x;
            py = y.IsObject ? realm.ToPrimitiveSlowPath(y, false) : y;
        }
        else
        {
            py = y.IsObject ? realm.ToPrimitiveSlowPath(y, false) : y;
            px = x.IsObject ? realm.ToPrimitiveSlowPath(x, false) : x;
        }

        if (px.IsString && py.IsString)
            return JsString.CompareOrdinal(px.AsJsString(), py.AsJsString()) < 0;

        if (px.IsBigInt && py.IsBigInt)
            return px.AsBigInt().Value < py.AsBigInt().Value;

        if (px.IsBigInt && py.IsString)
        {
            if (!Intrinsics.TryParseBigIntString(py.AsString(), out var parsedY))
                return null;
            return px.AsBigInt().Value < parsedY.Value;
        }

        if (px.IsString && py.IsBigInt)
        {
            if (!Intrinsics.TryParseBigIntString(px.AsString(), out var parsedX))
                return null;
            return parsedX.Value < py.AsBigInt().Value;
        }

        if (px.IsBool)
            px = new(px.IsTrue ? 1d : 0d);
        if (py.IsBool)
            py = new(py.IsTrue ? 1d : 0d);

        if (px.IsBigInt && py.IsNumber)
            return CompareBigIntAndNumber(px.AsBigInt().Value, py.NumberValue);

        if (px.IsNumber && py.IsBigInt)
        {
            var relation = CompareBigIntAndNumber(py.AsBigInt().Value, px.NumberValue);
            return relation.HasValue
                ? !relation.Value && !BigIntEqualsNumber(py.AsBigInt().Value, px.NumberValue)
                : null;
        }

        var nx = realm.ToNumberSlowPath(px);
        var ny = realm.ToNumberSlowPath(py);
        if (double.IsNaN(nx) || double.IsNaN(ny))
            return null;
        return nx < ny;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue HandleBitwiseSlowPath(JsRealm realm, JsOpCode op, JsValue lhs, JsValue rhs)
    {
        var leftNumeric = realm.ToNumericSlowPath(lhs);
        var rightNumeric = realm.ToNumericSlowPath(rhs);
        if (leftNumeric.IsBigInt || rightNumeric.IsBigInt)
        {
            if (!leftNumeric.IsBigInt || !rightNumeric.IsBigInt)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Cannot mix BigInt and other types, use explicit conversions");

            var left = leftNumeric.AsBigInt().Value;
            var right = rightNumeric.AsBigInt().Value;
            return op switch
            {
                JsOpCode.BitwiseAnd => JsValue.FromBigInt(new(left & right)),
                JsOpCode.BitwiseOr => JsValue.FromBigInt(new(left | right)),
                JsOpCode.BitwiseXor => JsValue.FromBigInt(new(left ^ right)),
                JsOpCode.ShiftLeft => JsValue.FromBigInt(new(ShiftBigInt(left, right, true))),
                JsOpCode.ShiftRight => JsValue.FromBigInt(new(ShiftBigInt(left, right, false))),
                JsOpCode.ShiftRightLogical => throw new JsRuntimeException(JsErrorKind.TypeError,
                    "BigInts have no unsigned right shift, use >> instead"),
                _ => throw new NotImplementedException($"Bitwise opcode {op} not implemented.")
            };
        }

        var a = ToInt32SlowPath(realm, leftNumeric);
        var b = ToInt32SlowPath(realm, rightNumeric);
        var shift = b & 0x1F;
        return op switch
        {
            JsOpCode.BitwiseAnd => JsValue.FromInt32(a & b),
            JsOpCode.BitwiseOr => JsValue.FromInt32(a | b),
            JsOpCode.BitwiseXor => JsValue.FromInt32(a ^ b),
            JsOpCode.ShiftLeft => JsValue.FromInt32(a << shift),
            JsOpCode.ShiftRight => JsValue.FromInt32(a >> shift),
            JsOpCode.ShiftRightLogical => ShiftRightLogicalResult(ToUInt32SlowPath(realm, leftNumeric), shift),
            _ => throw new NotImplementedException($"Bitwise opcode {op} not implemented.")
        };

        static JsValue ShiftRightLogicalResult(uint left, int shiftAmount)
        {
            var result = left >> shiftAmount;
            return result <= int.MaxValue ? JsValue.FromInt32((int)result) : new((double)result);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BigInteger ShiftBigInt(BigInteger value, BigInteger shiftCount, bool leftShift)
    {
        if (shiftCount.Sign < 0)
        {
            var magnitude = BigInteger.Negate(shiftCount);
            var adjustedCount = (int)BigInteger.Min(magnitude, new(int.MaxValue));
            return leftShift ? value >> adjustedCount : value << adjustedCount;
        }

        if (shiftCount > int.MaxValue)
            throw new JsRuntimeException(JsErrorKind.RangeError, "BigInt shift count is too large");

        var count = (int)shiftCount;
        return leftShift ? value << count : value >> count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ToInt32SlowPath(JsRealm realm, in JsValue value)
    {
        var number = realm.ToNumberSlowPath(value);
        if (number == 0d || double.IsNaN(number) || double.IsInfinity(number))
            return 0;

        var intPart = Math.Truncate(number);
        var two32 = 4294967296d;
        var mod = intPart % two32;
        if (mod < 0d)
            mod += two32;
        if (mod >= 2147483648d)
            mod -= two32;
        return (int)mod;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ToUInt32SlowPath(JsRealm realm, in JsValue value)
    {
        var number = realm.ToNumberSlowPath(value);
        if (number == 0d || double.IsNaN(number) || double.IsInfinity(number))
            return 0;

        var intPart = Math.Truncate(number);
        var two32 = 4294967296d;
        var mod = intPart % two32;
        if (mod < 0d)
            mod += two32;
        return (uint)mod;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InstanceOfSlowPath(JsRealm realm, in JsValue candidate)
    {
        var ctor = realm.acc;
        if (!ctor.TryGetObject(out var ctorObj))
            ThrowTypeError("INSTANCEOF_RHS_NOT_CALLABLE", "Right-hand side of 'instanceof' is not callable");

        if (ctorObj.TryGetPropertyAtom(realm, IdSymbolHasInstance, out var hasInstanceMethod, out _) &&
            !hasInstanceMethod.IsUndefined && !hasInstanceMethod.IsNull)
        {
            JsFunction? hasInstanceFn = null;
            if (hasInstanceMethod.TryGetObject(out var hasInstanceObj) && hasInstanceObj is JsFunction okojoFn)
                hasInstanceFn = okojoFn;
            if (hasInstanceFn is null)
                ThrowTypeError("INSTANCEOF_HASINSTANCE_NOT_CALLABLE", "Symbol.hasInstance is not callable");

            var arg = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in candidate), 1);
            var result = realm.InvokeFunction(hasInstanceFn, ctor, arg);
            realm.acc = ToBoolean(result) ? JsValue.True : JsValue.False;
            return;
        }

        if (ctorObj is not JsFunction)
            ThrowTypeError("INSTANCEOF_RHS_NOT_CALLABLE", "Right-hand side of 'instanceof' is not callable");

        if (!ctorObj.TryGetPropertyAtom(realm, IdPrototype, out var prototypeValue, out _))
            ThrowTypeError("INSTANCEOF_BAD_PROTOTYPE", "Function has non-object prototype in instanceof check");
        if (!prototypeValue.TryGetObject(out var prototypeObj))
            ThrowTypeError("INSTANCEOF_BAD_PROTOTYPE", "Function has non-object prototype in instanceof check");

        if (!candidate.TryGetObject(out var candidateObj))
        {
            realm.acc = JsValue.False;
            return;
        }

        for (var current = candidateObj.Prototype; current is not null; current = current.Prototype)
            if (ReferenceEquals(current, prototypeObj))
            {
                realm.acc = JsValue.True;
                return;
            }

        realm.acc = JsValue.False;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InstanceOfSlowPath(JsRealm realm, in JsValue candidate, in JsValue ctor)
    {
        if (!ctor.TryGetObject(out var ctorObj))
            ThrowTypeError("INSTANCEOF_RHS_NOT_CALLABLE", "Right-hand side of 'instanceof' is not callable");

        if (ctorObj.TryGetPropertyAtom(realm, IdSymbolHasInstance, out var hasInstanceMethod, out _) &&
            !hasInstanceMethod.IsUndefined && !hasInstanceMethod.IsNull)
        {
            JsFunction? hasInstanceFn = null;
            if (hasInstanceMethod.TryGetObject(out var hasInstanceObj) && hasInstanceObj is JsFunction okojoFn)
                hasInstanceFn = okojoFn;
            if (hasInstanceFn is null)
                ThrowTypeError("INSTANCEOF_HASINSTANCE_NOT_CALLABLE", "Symbol.hasInstance is not callable");

            var arg = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in candidate), 1);
            var result = realm.InvokeFunction(hasInstanceFn, ctor, arg);
            return ToBoolean(result);
        }

        if (ctorObj is not JsFunction)
            ThrowTypeError("INSTANCEOF_RHS_NOT_CALLABLE", "Right-hand side of 'instanceof' is not callable");

        if (!ctorObj.TryGetPropertyAtom(realm, IdPrototype, out var prototypeValue, out _))
            ThrowTypeError("INSTANCEOF_BAD_PROTOTYPE", "Function has non-object prototype in instanceof check");
        if (!prototypeValue.TryGetObject(out var prototypeObj))
            ThrowTypeError("INSTANCEOF_BAD_PROTOTYPE", "Function has non-object prototype in instanceof check");

        if (!candidate.TryGetObject(out var candidateObj))
            return false;

        for (var current = candidateObj.Prototype; current is not null; current = current.Prototype)
            if (ReferenceEquals(current, prototypeObj))
                return true;

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void HandleSwitchOnGeneratorState(JsScript script, ref int pc, int fp)
    {
        pc++; // gen_reg (reserved, currently frame-active generator lookup)
        int tableStart = script.Bytecode[pc++];
        int tableLength = script.Bytecode[pc++];

        var generator = ResolveGeneratorForSwitch(fp);
        if (generator is null || !generator.HasContinuation)
            return;

        var suspendId = generator.SuspendId;
        if ((uint)suspendId >= (uint)tableLength)
            ThrowTypeError("GENERATOR_INVALID_SUSPEND_ID",
                "Invalid generator suspend id for SwitchOnGeneratorState");

        pc = GetGeneratorSwitchTargetPc(script, tableStart, suspendId);
        generator.HasContinuation = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsGeneratorObject? ResolveGeneratorForSwitch(int fp)
    {
        return TryGetActiveGeneratorForFrame(fp, out var activeGenerator) ? activeGenerator : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetGeneratorSwitchTargetPc(JsScript script, int tableStart, int suspendId)
    {
        var idx = tableStart + suspendId;
        var typedTargets = script.GeneratorSwitchTargets;
        if (typedTargets is null)
            ThrowTypeError("GENERATOR_SWITCH_TABLE_MISSING", "Generator state table is missing");
        if ((uint)idx >= (uint)typedTargets.Length)
            ThrowTypeError("GENERATOR_SWITCH_TABLE_OOB", "Generator state table index out of bounds");
        var targetPc = typedTargets[idx];
        if ((uint)targetPc >= (uint)script.Bytecode.Length)
            ThrowTypeError("GENERATOR_SWITCH_TABLE_INVALID", "Generator state table target is invalid");
        return targetPc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleSwitchOnSmi(JsScript script, ref int pc,
        in JsValue acc)
    {
        var bytecode = script.Bytecode;
        int tableStart = bytecode[pc++];
        int tableLength = bytecode[pc++];
        if (!acc.IsInt32)
            return;

        var smi = acc.Int32Value;
        if ((uint)smi >= (uint)tableLength)
            return;

        var idx = tableStart + smi;
        var targets = script.SwitchOnSmiTargets;
        if (targets is null)
            ThrowTypeError("SWITCH_ON_SMI_TABLE_MISSING", "SwitchOnSmi target table is missing");
        if ((uint)idx >= (uint)targets.Length)
            ThrowTypeError("SWITCH_ON_SMI_TABLE_OOB", "SwitchOnSmi table index out of bounds");

        var targetPc = targets[idx];
        if ((uint)targetPc >= (uint)script.Bytecode.Length)
            ThrowTypeError("SWITCH_ON_SMI_TABLE_INVALID", "SwitchOnSmi target is invalid");
        pc = targetPc;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private GeneratorDispatchResult HandleResumeGenerator(
        ref byte b,
        Span<JsValue> fullStack,
        ref JsValue registers,
        int stopAtCallerFp,
        ref int fp,
        ref int pc,
        ref JsValue acc)
    {
        var startPc = pc;
        var bytecode = MemoryMarshal.CreateReadOnlySpan(ref b, pc + 100);
        int generatorRegOperand = bytecode[pc++];
        int firstRegOperand = bytecode[pc++];
        int regCountOperand = bytecode[pc++];

        if (!TryGetActiveGeneratorForFrame(fp, out var generator))
            throw new InvalidOperationException("Missing active generator frame for resume.");
        ValidateResumeGeneratorOperands(generator, generatorRegOperand, firstRegOperand, regCountOperand,
            ref registers);
        // Use resume operand register directly for delegate iterator lookup (yield* path).
        // If operand resolves to the active generator object itself, treat it as generator-state register.
        if (generatorRegOperand != 0xFF &&
            Unsafe.Add(ref registers, generatorRegOperand).TryGetObject(out var operandDelegateObj) &&
            !ReferenceEquals(operandDelegateObj, generator))
            generator.ActiveDelegateIterator = operandDelegateObj;

        if ((Agent.ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.ResumeGenerator) != 0)
            Agent.ExecutionCheckPolicy.EmitBoundaryCheckpoint(this, fullStack, fp,
                ExecutionCheckpointKind.ResumeGenerator, startPc);

        if (generator.PendingResumeMode != GeneratorResumeMode.Next)
            return HandleResumeGeneratorSlow(generator, fullStack, stopAtCallerFp, ref fp, ref pc, ref acc);

        acc = generator.PendingResumeValue;
        if (!generator.Function.UsesResumeModeDispatch)
        {
            generator.PendingResumeMode = GeneratorResumeMode.Next;
            generator.PendingResumeValue = JsValue.Undefined;
        }

        return GeneratorDispatchResult.Continue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private GeneratorDispatchResult HandleSuspendGenerator(
        ref byte b,
        Span<JsValue> fullStack,
        ref JsValue registers,
        int stopAtCallerFp,
        ref int fp,
        ref int pc,
        ref JsValue acc)
    {
        var startPc = pc;
        var bytecode = MemoryMarshal.CreateReadOnlySpan(ref b, pc + 100);
        int delegateIteratorReg = bytecode[pc++];
        int firstReg = bytecode[pc++];
        int regCount = bytecode[pc++];
        int suspendId = bytecode[pc++];
        var isPrestartSuspend = delegateIteratorReg == 0xFD;
        var isAwaitSuspend = delegateIteratorReg == 0xFE;

        if (!TryGetActiveGeneratorForFrame(fp, out var generator))
            throw new InvalidOperationException("Missing active generator frame for suspension.");

        var snapshotCount = regCount;
        var core = generator.Core;
        var snapshot = core.RegisterSnapshotBuffer;
        for (var i = 0; i < snapshotCount; i++)
            snapshot[i] = Unsafe.Add(ref registers, firstReg + i);

        core.ResumePc = pc;
        core.ResumeContext = GetCurrentContext(fullStack);
        core.ResumeThisValue = fullStack[fp + OffsetThisValue];
        core.ResumeFirstRegister = firstReg;
        core.ResumeRegisterCount = snapshotCount;
        core.SuspendId = suspendId;
        core.ResumeExceptionHandlers = CaptureExceptionHandlersForFrame(fp);
        core.HasContinuation = true;
        generator.LastSuspendWasAwait = isAwaitSuspend;
        if (delegateIteratorReg != 0xFF &&
            delegateIteratorReg != 0xFE &&
            Unsafe.Add(ref registers, delegateIteratorReg).TryGetObject(out var delegateObj) &&
            !ReferenceEquals(delegateObj, generator))
            core.ActiveDelegateIterator = delegateObj;
        else
            core.ActiveDelegateIterator = null;

        core.State = isPrestartSuspend ? GeneratorState.SuspendedStart : GeneratorState.SuspendedYield;
        ClearActiveGeneratorForFrame(fp);
        if (isPrestartSuspend)
        {
            acc = JsValue.Undefined;
        }
        else if (core.IsAsyncDriver || (generator.IsAsyncGenerator && isAwaitSuspend))
        {
            // Async function await suspension keeps ACC as the normalized await promise.
        }
        else if (core.ActiveDelegateIterator is not null)
        {
            // `yield*` must hand through the inner iterator result object directly.
        }
        else if (generator.FastForOfStepMode)
        {
            generator.FastForOfStepDone = false;
        }
        else
        {
            acc = CreateIteratorResultObject(acc, false);
        }

        if ((Agent.ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.SuspendGenerator) != 0)
            Agent.ExecutionCheckPolicy.EmitBoundaryCheckpoint(this, fullStack, fp,
                ExecutionCheckpointKind.SuspendGenerator, startPc);

        return PopCurrentFrameAndRestoreCaller(fullStack, stopAtCallerFp, ref fp, ref pc);
    }

    private GeneratorDispatchResult HandleResumeGeneratorSlow(
        JsGeneratorObject generator,
        Span<JsValue> fullStack,
        int stopAtCallerFp,
        ref int fp,
        ref int pc,
        ref JsValue acc)
    {
        var resumeMode = generator.PendingResumeMode;
        var useResumeModeDispatch = generator.Function.UsesResumeModeDispatch;
        if ((resumeMode == GeneratorResumeMode.Return || resumeMode == GeneratorResumeMode.Throw) &&
            generator.HasActiveDelegateIterator &&
            generator.ActiveDelegateIterator is { } delegateIteratorObj)
        {
            if (generator.IsAsyncGenerator)
            {
                if (resumeMode == GeneratorResumeMode.Return)
                {
                    var asyncReturnResult =
                        InvokeIteratorReturnForAsyncYieldDelegate(delegateIteratorObj, generator.PendingResumeValue,
                            out var hasReturnMethod);
                    if (!hasReturnMethod)
                    {
                        generator.ActiveDelegateIterator = null;
                        ClearDelegateIteratorRegisterInContinuationSnapshot(generator);
                        return SuspendAsyncGeneratorOnAwaitedReturnValue(
                            generator,
                            asyncReturnResult,
                            fullStack,
                            stopAtCallerFp,
                            ref fp,
                            ref pc,
                            ref acc);
                    }

                    return SuspendAsyncGeneratorOnYieldDelegateAwait(
                        generator,
                        asyncReturnResult,
                        resumeMode,
                        fullStack,
                        stopAtCallerFp,
                        ref fp,
                        ref pc,
                        ref acc);
                }

                var asyncThrowResult =
                    InvokeIteratorThrowForAsyncYieldDelegate(delegateIteratorObj, generator.PendingResumeValue);
                return SuspendAsyncGeneratorOnYieldDelegateAwait(
                    generator,
                    asyncThrowResult,
                    resumeMode,
                    fullStack,
                    stopAtCallerFp,
                    ref fp,
                    ref pc,
                    ref acc);
            }

            JsValue abruptResult;
            YieldDelegateAbruptKind abruptKind;
            if (resumeMode == GeneratorResumeMode.Return)
                abruptResult = InvokeIteratorReturnForYieldDelegate(delegateIteratorObj, generator.PendingResumeValue,
                    out abruptKind);
            else
                abruptResult = InvokeIteratorThrowForYieldDelegate(delegateIteratorObj, generator.PendingResumeValue,
                    out abruptKind);

            switch (abruptKind)
            {
                case YieldDelegateAbruptKind.Yield:
                    acc = abruptResult;
                    generator.State = GeneratorState.SuspendedYield;
                    generator.HasContinuation = true;
                    ClearActiveGeneratorForFrame(fp);
                    generator.PendingResumeMode = GeneratorResumeMode.Next;
                    generator.PendingResumeValue = JsValue.Undefined;
                    return PopCurrentFrameAndRestoreCaller(fullStack, stopAtCallerFp, ref fp, ref pc);
                case YieldDelegateAbruptKind.ContinueNext:
                    generator.ActiveDelegateIterator = null;
                    acc = abruptResult;
                    generator.PendingResumeMode = GeneratorResumeMode.Next;
                    generator.PendingResumeValue = JsValue.Undefined;
                    return GeneratorDispatchResult.Continue;
                case YieldDelegateAbruptKind.ContinueReturn:
                    generator.ActiveDelegateIterator = null;
                    acc = abruptResult;
                    generator.PendingResumeMode = GeneratorResumeMode.Return;
                    generator.PendingResumeValue = JsValue.Undefined;
                    return GeneratorDispatchResult.Continue;
                default:
                    throw new InvalidOperationException("Unknown yield* abrupt delegate result kind.");
            }
        }

        if (!useResumeModeDispatch && resumeMode == GeneratorResumeMode.Throw)
        {
            var thrownValue = generator.PendingResumeValue;
            FinalizeGenerator(generator);
            ClearActiveGeneratorForFrame(fp);
            var popResult = PopCurrentFrameAndRestoreCaller(fullStack, stopAtCallerFp, ref fp, ref pc);
            if (popResult == GeneratorDispatchResult.ReturnFromRun)
                return popResult;
            throw new InvalidOperationException($"Generator throw: {thrownValue}");
        }

        if (!useResumeModeDispatch && resumeMode == GeneratorResumeMode.Return)
        {
            var returnValue = generator.PendingResumeValue;
            FinalizeGenerator(generator);
            ClearActiveGeneratorForFrame(fp);
            acc = CreateIteratorResultObject(returnValue, true);
            return PopCurrentFrameAndRestoreCaller(fullStack, stopAtCallerFp, ref fp, ref pc);
        }

        acc = generator.PendingResumeValue;
        if (!useResumeModeDispatch)
        {
            generator.PendingResumeMode = GeneratorResumeMode.Next;
            generator.PendingResumeValue = JsValue.Undefined;
        }

        return GeneratorDispatchResult.Continue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private GeneratorDispatchResult SuspendAsyncGeneratorOnYieldDelegateAwait(
        JsGeneratorObject generator,
        in JsValue abruptResult,
        GeneratorResumeMode originalMode,
        Span<JsValue> fullStack,
        int stopAtCallerFp,
        ref int fp,
        ref int pc,
        ref JsValue acc)
    {
        var promise = Intrinsics.PromiseResolveValue(abruptResult);
        promise.IsHandled = true;
        var reaction = JsPromiseObject.Reaction.CreateAsyncGeneratorYieldDelegate(
            new(generator, originalMode));
        if (promise.State == JsPromiseObject.PromiseState.Pending)
            promise.AddReaction(reaction);
        else
            Intrinsics.EnqueuePromiseReactionJob(promise, reaction);

        acc = JsValue.FromObject(promise);
        generator.State = GeneratorState.SuspendedYield;
        generator.HasContinuation = true;
        generator.LastSuspendWasAwait = false;
        generator.Core.HasPendingAsyncYieldDelegateAwait = true;
        ClearActiveGeneratorForFrame(fp);
        generator.PendingResumeMode = GeneratorResumeMode.Next;
        generator.PendingResumeValue = JsValue.Undefined;
        return PopCurrentFrameAndRestoreCaller(fullStack, stopAtCallerFp, ref fp, ref pc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private GeneratorDispatchResult SuspendAsyncGeneratorOnAwaitedReturnValue(
        JsGeneratorObject generator,
        in JsValue returnValue,
        Span<JsValue> fullStack,
        int stopAtCallerFp,
        ref int fp,
        ref int pc,
        ref JsValue acc)
    {
        var promiseValue = Intrinsics.PromiseResolveByConstructor(Intrinsics.PromiseConstructor, returnValue);
        if (!promiseValue.TryGetObject(out var promiseObj) || promiseObj is not JsPromiseObject promise)
            throw new JsRuntimeException(JsErrorKind.InternalError,
                "PromiseResolve(%Promise%, awaitValue) must produce a promise object");

        promise.IsHandled = true;
        var reaction = JsPromiseObject.Reaction.CreateAsyncGeneratorReturn(generator);
        if (promise.State == JsPromiseObject.PromiseState.Pending)
            promise.AddReaction(reaction);
        else
            Intrinsics.EnqueuePromiseReactionJob(promise, reaction);

        acc = JsValue.FromObject(promise);
        generator.State = GeneratorState.SuspendedYield;
        generator.HasContinuation = true;
        generator.LastSuspendWasAwait = false;
        generator.PendingResumeMode = GeneratorResumeMode.Next;
        generator.PendingResumeValue = JsValue.Undefined;
        ClearActiveGeneratorForFrame(fp);
        return PopCurrentFrameAndRestoreCaller(fullStack, stopAtCallerFp, ref fp, ref pc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void PreflightGeneratorParameterBinding(JsGeneratorObject generator)
    {
        _ = ExecuteGeneratorFromStart(generator);
        if (generator.State != GeneratorState.SuspendedStart)
            throw new InvalidOperationException("Generator parameter preflight did not suspend at prestart boundary.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateResumeGeneratorOperands(
        JsGeneratorObject generator,
        int generatorRegOperand,
        int firstRegOperand,
        int regCountOperand,
        ref JsValue registers)
    {
        if (firstRegOperand != generator.ResumeFirstRegister || regCountOperand != generator.ResumeRegisterCount)
            ThrowTypeError("GENERATOR_RESUME_ABI_MISMATCH",
                "ResumeGenerator live-range operands mismatch continuation.");

        if (generatorRegOperand is 0xFF or 0xFE)
            return;
        if (!generator.HasActiveDelegateIterator)
            return;
        if (generator.ActiveDelegateIterator is not { } activeDelegateObj)
            return;
        if (!Unsafe.Add(ref registers, generatorRegOperand).TryGetObject(out var resumedDelegateObj))
            ThrowTypeError("GENERATOR_RESUME_ABI_MISMATCH", "ResumeGenerator delegate iterator register mismatch.");
        if (!ReferenceEquals(activeDelegateObj, resumedDelegateObj))
            ThrowTypeError("GENERATOR_RESUME_ABI_MISMATCH", "ResumeGenerator delegate iterator register mismatch.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private GeneratorDispatchResult PopCurrentFrameAndRestoreCaller(
        Span<JsValue> fullStack,
        int stopAtCallerFp,
        ref int fp,
        ref int pc)
    {
        var top = StackTop;
        StackTop = fp;
        RemoveExceptionHandlersForFrame(fp);
        ref var frame = ref Unsafe.As<JsValue, CallFrame>(ref fullStack[StackTop]);
        fp = frame.CallerFp;
        pc = frame.CallerPc;
        fullStack[StackTop..top].Fill(JsValue.Undefined);

        if (stopAtCallerFp >= 0 && fp == stopAtCallerFp)
            return GeneratorDispatchResult.ReturnFromRun;
        if (fp == 0 && pc == 0)
            return GeneratorDispatchResult.ReturnFromRun;
        return GeneratorDispatchResult.ReloadFrame;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InOperatorSlowPath(JsRealm realm, in JsValue key, in JsValue rhs)
    {
        if (!rhs.TryGetObject(out var target))
            ThrowTypeError("IN_RHS_NOT_OBJECT", "Right-hand side of 'in' should be an object");

        return HasPropertySlowPath(realm, target, NormalizePropertyKey(realm, key));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InOperatorSlowPath(in JsValue key)
    {
        if (!acc.TryGetObject(out var target))
            ThrowTypeError("IN_RHS_NOT_OBJECT", "Right-hand side of 'in' should be an object");

        acc = HasPropertySlowPath(this, target, NormalizePropertyKey(this, key)) ? JsValue.True : JsValue.False;
    }

    internal static bool HasPropertySlowPath(JsRealm realm, JsObject? target, in JsValue key)
    {
        while (target is not null)
        {
            if (target is JsTypedArrayObject typedArray &&
                Intrinsics.TryHasTypedArrayIntegerIndexedElement(realm, typedArray, key, out var typedArrayHas,
                    out var typedArrayHandled))
                if (typedArrayHandled)
                    return typedArrayHas;

            if (target.TryHasPropertyViaTrap(realm, key, out var viaTrap))
                return viaTrap;

            if (target.HasOwnPropertyKey(realm, key))
                return true;

            target = target.Prototype;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue LoadKeyedPropertySlowPath(JsRealm realm, JsObject obj, in JsValue key)
    {
        var normalizedKey = NormalizePropertyKey(realm, key);
        if (obj is JsTypedArrayObject typedArray &&
            Intrinsics.TryGetTypedArrayIntegerIndexedElement(realm, typedArray, normalizedKey, out var typedArrayValue,
                out var typedArrayHandled))
            if (typedArrayHandled)
                return typedArrayValue;

        if (normalizedKey.IsSymbol)
        {
            var atom = normalizedKey.AsSymbol().Atom;
            _ = obj.TryGetPropertyAtom(realm, atom, out var value, out _);
            return value;
        }

        if (normalizedKey.IsString)
        {
            var text = normalizedKey.AsString();
            if (TryGetArrayIndexFromCanonicalString(text, out var idx))
            {
                _ = obj.TryGetElement(idx, out var element);
                return element;
            }

            var atom = realm.Atoms.InternNoCheck(text);
            _ = obj.TryGetPropertyAtom(realm, atom, out var value, out _);
            return value;
        }

        if (normalizedKey.IsNumber)
        {
            if (TryGetArrayIndexFromNumber(normalizedKey.NumberValue, out var idx))
            {
                if (obj.TryGetElement(idx, out var element))
                    return element;

                return JsValue.Undefined;
            }

            var atom = realm.Atoms.InternNoCheck(JsValue.NumberToJsString(normalizedKey.NumberValue));
            _ = obj.TryGetPropertyAtom(realm, atom, out var value, out _);
            return value;
        }

        var fallbackText = realm.ToJsStringSlowPath(normalizedKey);
        if (TryGetArrayIndexFromCanonicalString(fallbackText, out var fallbackIndex))
        {
            if (obj.TryGetElement(fallbackIndex, out var element))
                return element;
            return JsValue.Undefined;
        }

        var fallbackAtom = realm.Atoms.InternNoCheck(fallbackText);
        _ = obj.TryGetPropertyAtom(realm, fallbackAtom, out var fallbackValue, out _);
        return fallbackValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StoreKeyedPropertySlowPath(JsRealm realm, JsObject obj, in JsValue key,
        JsValue value, bool strict)
    {
        var normalizedKey = NormalizePropertyKey(realm, key);
        if (obj is JsTypedArrayObject typedArray &&
            Intrinsics.TryGetCanonicalNumericIndexString(realm, normalizedKey, out var typedArrayIndex))
        {
            _ = Intrinsics.SetCanonicalNumericIndexOnTypedArrayForSet(typedArray, typedArrayIndex, value);
            return;
        }

        if (normalizedKey.IsSymbol)
        {
            var atom = normalizedKey.AsSymbol().Atom;
            if (!obj.TrySetPropertyAtom(realm, atom, value, out _) && strict)
                ThrowTypeError("ASSIGN_READONLY", "Cannot assign to read only property");
            return;
        }

        if (normalizedKey.IsString)
        {
            var text = normalizedKey.AsString();
            if (TryGetArrayIndexFromCanonicalString(text, out var idx))
            {
                if (!obj.TrySetElement(idx, value) && strict)
                    ThrowTypeError("ASSIGN_READONLY", "Cannot assign to read only property");
                return;
            }

            var atom = realm.Atoms.InternNoCheck(text);
            if (!obj.TrySetPropertyAtom(realm, atom, value, out _) && strict)
                ThrowTypeError("ASSIGN_READONLY", "Cannot assign to read only property");
            return;
        }

        if (normalizedKey.IsNumber)
        {
            if (TryGetArrayIndexFromNumber(normalizedKey.NumberValue, out var idx))
            {
                if (!obj.TrySetElement(idx, value) && strict)
                    ThrowTypeError("ASSIGN_READONLY", "Cannot assign to read only property");
                return;
            }

            var atom = realm.Atoms.InternNoCheck(JsValue.NumberToJsString(normalizedKey.NumberValue));
            if (!obj.TrySetPropertyAtom(realm, atom, value, out _) && strict)
                ThrowTypeError("ASSIGN_READONLY", "Cannot assign to read only property");
            return;
        }

        var fallbackText = realm.ToJsStringSlowPath(normalizedKey);
        if (TryGetArrayIndexFromCanonicalString(fallbackText, out var fallbackIndex))
        {
            if (!obj.TrySetElement(fallbackIndex, value) && strict)
                ThrowTypeError("ASSIGN_READONLY", "Cannot assign to read only property");
            return;
        }

        var fallbackAtom = realm.Atoms.InternNoCheck(fallbackText);
        if (!obj.TrySetPropertyAtom(realm, fallbackAtom, value, out _) && strict)
            ThrowTypeError("ASSIGN_READONLY", "Cannot assign to read only property");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool ToBoolean(in JsValue v)
    {
        if ((v.U & ~1ul) == (JsValue.BoxHdr | ((ulong)Tag.JsTagBool << JsValue.TagShift))) return (v.U & 1ul) != 0;
        return ToBooleanSlowPath(v);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool ToBooleanSlowPath(JsValue v)
        {
            if (v.IsNullOrUndefined) return false;
            if (v.IsNumber)
            {
                var d = v.FastNumberValue;
                return d != 0 && !double.IsNaN(d);
            }

            if (v.IsString) return v.AsString().Length > 0;
            if (v.IsBigInt) return !v.AsBigInt().Value.IsZero;
            return true;
        }
    }

    private static bool BigIntEqualsNumber(BigInteger bigint, double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number) || number != Math.Truncate(number))
            return false;
        return bigint == new BigInteger(number);
    }

    private static bool? CompareBigIntAndNumber(BigInteger bigint, double number)
    {
        if (double.IsNaN(number))
            return null;
        if (double.IsPositiveInfinity(number))
            return true;
        if (double.IsNegativeInfinity(number))
            return false;

        if (number == Math.Truncate(number))
            return bigint < new BigInteger(number);

        if (bigint.IsZero)
            return 0d < number;

        var bigintAsNumber = (double)bigint;
        if (double.IsPositiveInfinity(bigintAsNumber))
            return false;
        if (double.IsNegativeInfinity(bigintAsNumber))
            return true;
        return bigintAsNumber < number;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetArrayIndexFromNumber(double n, out uint index)
    {
        if (n >= 0 && n < uint.MaxValue && n == Math.Truncate(n))
        {
            index = (uint)n;
            return true;
        }

        index = 0;
        return false;
    }


    private JsBytecodeFunction BindClosureIfNeeded(JsBytecodeFunction template)
    {
        var currentContext = GetCurrentContext();
        var closure = template.CloneForClosure(this);
        closure.BoundParentContext = currentContext;
        JsBytecodeFunction.DerivedSuperCallState? derivedSuperCallState = null;
        if (CurrentCallFrame.Function is JsBytecodeFunction currentBytecodeFunction)
        {
            derivedSuperCallState = currentBytecodeFunction.BoundDerivedSuperCallState;
            if (derivedSuperCallState is null &&
                CurrentCallFrame.FrameKind == CallFrameKind.ConstructFrame &&
                currentBytecodeFunction.IsDerivedConstructor)
            {
                JsContext? derivedThisContext = null;
                var derivedThisSlot = -1;
                if (currentContext is not null &&
                    currentBytecodeFunction.DerivedThisContextSlot >= 0 &&
                    (uint)currentBytecodeFunction.DerivedThisContextSlot < (uint)currentContext.Slots.Length)
                {
                    derivedThisContext = currentContext;
                    derivedThisSlot = currentBytecodeFunction.DerivedThisContextSlot;
                }

                derivedSuperCallState = new(
                    fp,
                    currentBytecodeFunction,
                    Stack[fp + OffsetExtra0],
                    derivedThisContext,
                    derivedThisSlot);
            }
        }

        if (template.IsArrow)
        {
            if (template.LexicalThisContextSlot < 0) closure.BoundThisValue = CurrentCallFrame.ThisValue;

            if (CurrentCallFrame.Function is JsBytecodeFunction currentBytecodeArrowSource)
                closure.BoundNewTargetValue =
                    ResolveClosureLexicalNewTarget(currentBytecodeArrowSource, Stack[fp + OffsetExtra0]);
            else
                closure.BoundNewTargetValue = Stack[fp + OffsetExtra0];

            closure.BoundDerivedSuperCallState = derivedSuperCallState;
        }

        return closure;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue ThrowIfTheHole(JsValue value)
    {
        if (value.IsTheHole) ThrowHole();

        return value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ThrowHole()
    {
        ThrowReferenceError("TDZ_READ_BEFORE_INIT");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ThrowSuperNotCalled()
    {
        ThrowReferenceError("SUPER_NOT_CALLED",
            "Must call super constructor in derived class before accessing 'this' or returning.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ThrowNonCallable(bool isConstruct)
    {
        if (isConstruct)
            ThrowTypeError("NOT_CONSTRUCTOR", "constructor is not a function");
        ThrowTypeError("NOT_CALLABLE", "Not a function");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ThrowGeneratorNotConstructor()
    {
        ThrowTypeError("GENERATOR_NOT_CONSTRUCTOR", "generator function is not a constructor");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsConstructableFunction(JsFunction fn)
    {
        return fn.IsConstructor;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue InvokeIteratorReturnForYieldDelegate(JsObject iterator, in JsValue value,
        out YieldDelegateAbruptKind kind)
    {
        if (!iterator.TryGetPropertyAtom(this, IdReturn, out var returnMethod, out _) ||
            returnMethod.IsUndefined || returnMethod.IsNull)
        {
            kind = YieldDelegateAbruptKind.ContinueReturn;
            return value;
        }

        if (returnMethod.TryGetObject(out var fnObj) && fnObj is JsFunction fn)
        {
            var arg = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1);
            var result = InvokeFunction(fn, iterator, arg);
            return NormalizeYieldDelegateAbruptResult(result, YieldDelegateAbruptKind.ContinueReturn, out kind);
        }

        kind = default;
        ThrowTypeError("ITERATOR_RETURN_NOT_FUNCTION", "iterator.return is not a function");
        return default;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue InvokeIteratorThrowForYieldDelegate(JsObject iterator, in JsValue value,
        out YieldDelegateAbruptKind kind)
    {
        if (!iterator.TryGetPropertyAtom(this, IdThrow, out var throwMethod, out _) ||
            throwMethod.IsUndefined || throwMethod.IsNull)
        {
            IteratorCloseForYieldDelegateThrow(iterator);
            kind = default;
            ThrowTypeError("ITERATOR_THROW_MISSING", "iterator.throw is not present");
            return default;
        }

        if (!throwMethod.TryGetObject(out var fnObj) || fnObj is not JsFunction fn)
            throw TypeError("ITERATOR_THROW_NOT_FUNCTION", "iterator.throw is not a function");

        var arg = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1);
        var result = InvokeFunction(fn, iterator, arg);
        return NormalizeYieldDelegateAbruptResult(result, YieldDelegateAbruptKind.ContinueNext, out kind);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue InvokeIteratorReturnForAsyncYieldDelegate(JsObject iterator, in JsValue value, out bool hasMethod)
    {
        if (!iterator.TryGetPropertyAtom(this, IdReturn, out var returnMethod, out _) ||
            returnMethod.IsUndefined || returnMethod.IsNull)
        {
            hasMethod = false;
            return value;
        }

        if (returnMethod.TryGetObject(out var fnObj) && fnObj is JsFunction fn)
        {
            hasMethod = true;
            var arg = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1);
            var result = InvokeFunction(fn, iterator, arg);
            if (!result.TryGetObject(out _))
                ThrowTypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator result is not an object");
            return result;
        }

        hasMethod = false;
        ThrowTypeError("ITERATOR_RETURN_NOT_FUNCTION", "iterator.return is not a function");
        return default;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue InvokeIteratorThrowForAsyncYieldDelegate(JsObject iterator, in JsValue value)
    {
        if (!iterator.TryGetPropertyAtom(this, IdThrow, out var throwMethod, out _) ||
            throwMethod.IsUndefined || throwMethod.IsNull)
        {
            IteratorCloseForYieldDelegateThrow(iterator);
            ThrowTypeError("ITERATOR_THROW_MISSING", "iterator.throw is not present");
            return default;
        }

        if (!throwMethod.TryGetObject(out var fnObj) || fnObj is not JsFunction fn)
            throw TypeError("ITERATOR_THROW_NOT_FUNCTION", "iterator.throw is not a function");

        var arg = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1);
        var result = InvokeFunction(fn, iterator, arg);
        if (!result.TryGetObject(out _))
            ThrowTypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator result is not an object");
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue NormalizeIteratorResult(in JsValue result, out bool completed)
    {
        if (!result.TryGetObject(out var resultObj))
            ThrowTypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator result is not an object");

        _ = resultObj.TryGetPropertyAtom(this, IdDone, out var done, out _);
        completed = ToBoolean(done);
        if (!resultObj.TryGetPropertyAtom(this, IdValue, out var value, out _))
            value = JsValue.Undefined;
        return CreateIteratorResultObject(value, completed);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue NormalizeYieldDelegateAbruptResult(in JsValue result, YieldDelegateAbruptKind completedKind,
        out YieldDelegateAbruptKind kind)
    {
        if (!result.TryGetObject(out var resultObj))
            ThrowTypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator result is not an object");

        _ = resultObj.TryGetPropertyAtom(this, IdDone, out var done, out _);
        if (!ToBoolean(done))
        {
            kind = YieldDelegateAbruptKind.Yield;
            return result;
        }

        kind = completedKind;
        if (!resultObj.TryGetPropertyAtom(this, IdValue, out var value, out _))
            value = JsValue.Undefined;
        return value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void IteratorCloseForYieldDelegateThrow(JsObject iterator)
    {
        if (!iterator.TryGetPropertyAtom(this, IdReturn, out var returnMethod, out _) ||
            returnMethod.IsUndefined || returnMethod.IsNull)
            return;

        if (!returnMethod.TryGetObject(out var fnObj) || fnObj is not JsFunction)
            ThrowTypeError("ITERATOR_RETURN_NOT_FUNCTION", "iterator.return is not a function");

        var fn = (JsFunction)fnObj;
        var result = InvokeFunction(fn, iterator, ReadOnlySpan<JsValue>.Empty);
        if (!result.TryGetObject(out _))
            ThrowTypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator result is not an object");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue ForOfStepValue(in JsValue iterable)
    {
        if (!iterable.TryGetObject(out var iterableObj))
            throw TypeError("FOROF_NOT_ITERABLE", "for-of value is not iterable");

        if (iterableObj is JsGeneratorObject generator &&
            TryGetGeneratorNextMethodObject(iterableObj, out var nextObj) &&
            ReferenceEquals(nextObj, Intrinsics.GeneratorNextFunction))
        {
            var value = ResumeGeneratorForOfNextStep(generator, out var done);
            return done ? JsValue.TheHole : value;
        }

        if (!iterableObj.TryGetPropertyAtom(this, IdNext, out var nextMethod, out _))
            throw TypeError("ITERATOR_NEXT_NOT_FUNCTION", "iterator.next is not a function");
        if (!nextMethod.TryGetObject(out var nextFnObj) || nextFnObj is not JsFunction nextFn)
            throw TypeError("ITERATOR_NEXT_NOT_FUNCTION", "iterator.next is not a function");

        var result = InvokeFunction(nextFn, iterableObj, ReadOnlySpan<JsValue>.Empty);
        if (!result.TryGetObject(out var resultObj))
            throw TypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator result is not an object");

        _ = resultObj.TryGetPropertyAtom(this, IdDone, out var doneValue, out _);
        if (ToBoolean(doneValue))
            return JsValue.TheHole;

        return resultObj.TryGetPropertyAtom(this, IdValue, out var valueOut, out _)
            ? valueOut
            : JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue RuntimeDestructureArrayAssignment(in JsValue source, ReadOnlySpan<JsValue> elementFlags)
    {
        const int flagElision = 1;
        const int flagRest = 2;

        if (!this.TryToObject(source, out var iterableObj))
            throw TypeError("DESTRUCTURE_ASSIGN_NOT_ITERABLE", "Destructuring assignment value is not iterable");

        JsValue iteratorMethod;
        if (iterableObj is JsGeneratorObject { IsAsyncGenerator: false })
            iteratorMethod = JsValue.FromObject(Intrinsics.IteratorSelfFunction);
        else if (!iterableObj.TryGetPropertyAtom(this, IdSymbolIterator, out iteratorMethod, out _))
            throw TypeError("DESTRUCTURE_ASSIGN_NOT_ITERABLE", "Destructuring assignment value is not iterable");

        if (!iteratorMethod.TryGetObject(out var iteratorMethodObj) || iteratorMethodObj is not JsFunction iteratorFn)
            throw TypeError("DESTRUCTURE_ASSIGN_NOT_ITERABLE", "Destructuring assignment value is not iterable");

        var iteratorValue = InvokeFunction(iteratorFn, iterableObj, ReadOnlySpan<JsValue>.Empty);
        if (!iteratorValue.TryGetObject(out var iteratorObj))
            throw TypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator is not an object");

        var result = CreateArrayObject();
        var dense = result.InitializeDenseElementsNoCollision(elementFlags.Length);
        var done = false;

        for (var i = 0; i < elementFlags.Length; i++)
        {
            var flags = elementFlags[i].Int32Value;
            if ((flags & flagRest) != 0)
            {
                var restArray = CreateArrayObject();
                uint restIndex = 0;
                while (!done)
                {
                    var restValue = DestructureArrayStepValue(iteratorObj, out done);
                    if (restValue.IsTheHole)
                        break;

                    restArray.SetElement(restIndex++, restValue);
                }

                dense[i] = JsValue.FromObject(restArray);
                done = true;
                continue;
            }

            var elementValue = DestructureArrayStepValue(iteratorObj, out done);
            if (elementValue.IsTheHole)
                elementValue = JsValue.Undefined;

            if ((flags & flagElision) != 0)
            {
                dense[i] = JsValue.Undefined;
                continue;
            }

            dense[i] = elementValue;
        }

        if (!done)
            IteratorCloseForDestructuring(iteratorObj);

        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue RuntimeDestructureArrayAssignmentMemberTargets(in JsValue source, ReadOnlySpan<JsValue> targetSpecs)
    {
        const int specWidth = 4;
        const int flagHasTarget = 1;
        const int flagComputed = 2;
        const int flagReceiverIsThunk = 4;
        const int flagKeyIsThunk = 8;
        const int flagSetterThunk = 16;
        const int flagElision = 32;
        const int flagRest = 64;

        if (targetSpecs.Length % specWidth != 0)
            ThrowTypeError("DESTRUCTURE_ASSIGN_TARGET_SPEC",
                "DestructureArrayAssignmentMemberTargets requires 4-value target specs");

        if (!this.TryToObject(source, out var iterableObj))
            throw TypeError("DESTRUCTURE_ASSIGN_NOT_ITERABLE", "Destructuring assignment value is not iterable");

        JsValue iteratorMethod;
        if (iterableObj is JsGeneratorObject { IsAsyncGenerator: false })
            iteratorMethod = JsValue.FromObject(Intrinsics.IteratorSelfFunction);
        else if (!iterableObj.TryGetPropertyAtom(this, IdSymbolIterator, out iteratorMethod, out _))
            throw TypeError("DESTRUCTURE_ASSIGN_NOT_ITERABLE", "Destructuring assignment value is not iterable");

        if (!iteratorMethod.TryGetObject(out var iteratorMethodObj) || iteratorMethodObj is not JsFunction iteratorFn)
            throw TypeError("DESTRUCTURE_ASSIGN_NOT_ITERABLE", "Destructuring assignment value is not iterable");

        var iteratorValue = InvokeFunction(iteratorFn, iterableObj, ReadOnlySpan<JsValue>.Empty);
        if (!iteratorValue.TryGetObject(out var iteratorObj))
            throw TypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator is not an object");

        var done = false;
        for (var i = 0; i < targetSpecs.Length; i += specWidth)
        {
            var receiverThunk = targetSpecs[i];
            var keySpec = targetSpecs[i + 1];
            var flags = targetSpecs[i + 2].Int32Value;
            var defaultThunk = targetSpecs[i + 3];

            var targetValue = JsValue.Undefined;
            var keyValue = JsValue.Undefined;
            var hasTarget = (flags & flagHasTarget) != 0;
            var isComputed = (flags & flagComputed) != 0;
            var receiverIsThunk = (flags & flagReceiverIsThunk) != 0;
            var keyIsThunk = (flags & flagKeyIsThunk) != 0;
            var setterThunkTarget = (flags & flagSetterThunk) != 0;
            var isElision = (flags & flagElision) != 0;
            var isRest = (flags & flagRest) != 0;
            if (hasTarget && !setterThunkTarget)
                try
                {
                    if (receiverIsThunk)
                    {
                        if (!receiverThunk.TryGetObject(out var receiverThunkObj))
                            ThrowTypeError("DESTRUCTURE_ASSIGN_TARGET_THUNK",
                                "Destructuring assignment target thunk is not callable");
                        var receiverThunkFn = receiverThunkObj as JsFunction;
                        if (receiverThunkFn is null)
                            ThrowTypeError("DESTRUCTURE_ASSIGN_TARGET_THUNK",
                                "Destructuring assignment target thunk is not callable");
                        targetValue = InvokeFunction(receiverThunkFn, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
                    }
                    else
                    {
                        targetValue = receiverThunk;
                    }

                    if (isComputed)
                    {
                        if (keyIsThunk)
                        {
                            if (!keySpec.TryGetObject(out var keyThunkObj))
                                ThrowTypeError("DESTRUCTURE_ASSIGN_TARGET_KEY_THUNK",
                                    "Destructuring assignment target key thunk is not callable");
                            var keyThunkFn = keyThunkObj as JsFunction;
                            if (keyThunkFn is null)
                                ThrowTypeError("DESTRUCTURE_ASSIGN_TARGET_KEY_THUNK",
                                    "Destructuring assignment target key thunk is not callable");
                            keyValue = InvokeFunction(keyThunkFn, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
                        }
                        else
                        {
                            keyValue = keySpec;
                        }
                    }
                    else
                    {
                        keyValue = keySpec;
                    }
                }
                catch
                {
                    BestEffortIteratorCloseOnThrow(iteratorObj);
                    throw;
                }

            JsValue elementValue;
            if (isRest)
            {
                var restArray = CreateArrayObject();
                uint restIndex = 0;
                while (!done)
                {
                    var restValue = DestructureArrayStepValue(iteratorObj, out done);
                    if (restValue.IsTheHole)
                        break;
                    restArray.SetElement(restIndex++, restValue);
                }

                elementValue = JsValue.FromObject(restArray);
            }
            else
            {
                elementValue = DestructureArrayStepValue(iteratorObj, out done);
                if (elementValue.IsTheHole)
                    elementValue = JsValue.Undefined;
            }

            if (elementValue.IsUndefined &&
                defaultThunk.TryGetObject(out var thunkObj) &&
                thunkObj is JsFunction thunkFn)
                try
                {
                    elementValue = InvokeFunction(thunkFn, JsValue.Undefined, ReadOnlySpan<JsValue>.Empty);
                }
                catch
                {
                    if (!done)
                        BestEffortIteratorCloseOnThrow(iteratorObj);
                    throw;
                }

            if (isElision || !hasTarget)
                continue;

            try
            {
                if (setterThunkTarget)
                {
                    if (!receiverThunk.TryGetObject(out var setterObj))
                        ThrowTypeError("DESTRUCTURE_ASSIGN_TARGET_THUNK",
                            "Destructuring assignment target thunk is not callable");
                    if (setterObj is not JsFunction)
                        ThrowTypeError("DESTRUCTURE_ASSIGN_TARGET_THUNK",
                            "Destructuring assignment target thunk is not callable");
                    var setterFn = (JsFunction)setterObj;
                    _ = InvokeFunction(setterFn, JsValue.Undefined, new[] { elementValue });
                }
                else
                {
                    var strict = CurrentCallFrame.Function is JsBytecodeFunction bytecodeFunction &&
                                 bytecodeFunction.IsStrict;
                    StoreDestructuredMemberAssignmentTarget(targetValue, keyValue, isComputed, elementValue, strict);
                }
            }
            catch
            {
                if (!done)
                    BestEffortIteratorCloseOnThrow(iteratorObj);
                throw;
            }
        }

        if (!done)
            IteratorCloseForDestructuring(iteratorObj);

        return source;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal JsValue DestructureArrayStepValue(JsObject iteratorObj, out bool done)
    {
        if (iteratorObj is JsGeneratorObject generator &&
            TryGetGeneratorNextMethodObject(iteratorObj, out var nextObj) &&
            ReferenceEquals(nextObj, Intrinsics.GeneratorNextFunction))
        {
            var value = ResumeGeneratorForOfNextStep(generator, out done);
            return done ? JsValue.TheHole : value;
        }

        if (!iteratorObj.TryGetPropertyAtom(this, IdNext, out var nextMethod, out _) ||
            !nextMethod.TryGetObject(out var nextFnObj) || nextFnObj is not JsFunction nextFn)
            throw TypeError("ITERATOR_NEXT_NOT_FUNCTION", "iterator.next is not a function");

        var result = InvokeFunction(nextFn, iteratorObj, ReadOnlySpan<JsValue>.Empty);
        if (!result.TryGetObject(out var resultObj))
            throw TypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator result is not an object");

        _ = resultObj.TryGetPropertyAtom(this, IdDone, out var doneValue, out _);
        if (ToBoolean(doneValue))
        {
            done = true;
            return JsValue.TheHole;
        }

        done = false;
        return resultObj.TryGetPropertyAtom(this, IdValue, out var valueOut, out _)
            ? valueOut
            : JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void IteratorCloseForDestructuring(JsObject iteratorObj)
    {
        if (!iteratorObj.TryGetPropertyAtom(this, IdReturn, out var returnMethod, out _) ||
            returnMethod.IsUndefined || returnMethod.IsNull)
            return;

        if (!returnMethod.TryGetObject(out var returnObj) || returnObj is not JsFunction returnFn)
            throw TypeError("ITERATOR_RETURN_NOT_FUNCTION", "iterator.return is not a function");

        var result = InvokeFunction(returnFn, iteratorObj, ReadOnlySpan<JsValue>.Empty);
        if (!result.TryGetObject(out _))
            throw TypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator result is not an object");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void BestEffortIteratorCloseOnThrow(JsObject iteratorObj)
    {
        try
        {
            _ = iteratorObj.TryGetPropertyAtom(this, IdReturn, out var returnMethod, out _);
            if (returnMethod.IsUndefined || returnMethod.IsNull)
                return;
            if (!returnMethod.TryGetObject(out var returnObj) || returnObj is not JsFunction returnFn)
                return;

            _ = InvokeFunction(returnFn, iteratorObj, ReadOnlySpan<JsValue>.Empty);
        }
        catch
        {
            // Preserve the original abrupt completion.
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StoreDestructuredMemberAssignmentTarget(in JsValue targetValue, in JsValue keyValue, bool isComputed,
        JsValue value, bool strict)
    {
        if (targetValue.IsNullOrUndefined)
            ThrowTypeError("PROPERTY_READ_ON_NULLISH", "Cannot read properties of null or undefined");
        var obj = targetValue.TryGetObject(out var existingObj)
            ? existingObj
            : ToObjectForPropertyAccessSlowPath(this, targetValue);

        if (!isComputed)
        {
            if (TryResolveRuntimePropertyKey(this, keyValue, out var index, out var atom))
            {
                if (!obj.TrySetElement(index, value) && strict)
                    ThrowTypeError("ASSIGN_READONLY", "Cannot assign to read only property");
            }
            else
            {
                if (!obj.TrySetPropertyAtom(this, atom, value, out _) && strict)
                    ThrowTypeError("ASSIGN_READONLY", "Cannot assign to read only property");
            }

            return;
        }

        var primitiveKey = keyValue;
        if (primitiveKey.IsObject)
            primitiveKey = this.ToPrimitiveSlowPath(primitiveKey, true);

        if (primitiveKey.IsSymbol || primitiveKey.IsString || primitiveKey.IsNumber)
        {
            StoreKeyedPropertySlowPath(this, obj, primitiveKey, value, strict);
            return;
        }

        StoreKeyedPropertySlowPath(this, obj, this.ToJsStringSlowPath(primitiveKey), value, strict);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue ForInEnumerate(in JsValue value)
    {
        if (!this.TryToObject(value, out var obj))
            return new JsForInEnumeratorObject(this, Array.Empty<string>());

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var keys = new List<string>(8);
        var owners = new List<JsObject>(8);
        for (var cursor = obj; cursor is not null; cursor = cursor.Prototype)
        {
            var beforeCount = keys.Count;
            cursor.CollectForInEnumerableStringAtomKeys(this, visited, keys);
            for (var i = beforeCount; i < keys.Count; i++)
                owners.Add(cursor);
        }

        return new JsForInEnumeratorObject(this, keys.ToArray(), owners.ToArray());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue ForInStepKey(in JsValue enumeratorValue)
    {
        if (!enumeratorValue.TryGetObject(out var obj))
            ThrowTypeError("FORIN_ENUMERATOR_INVALID", "for-in enumerator is invalid");
        if (obj is not JsForInEnumeratorObject)
            ThrowTypeError("FORIN_ENUMERATOR_INVALID", "for-in enumerator is invalid");
        var enumerator = (JsForInEnumeratorObject)obj;

        if (enumerator.TryNextKey(this, out var key))
            return key;
        return JsValue.TheHole;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue ForInNext(in JsValue enumeratorValue)
    {
        if (!enumeratorValue.TryGetObject(out var obj))
            ThrowTypeError("FORIN_ENUMERATOR_INVALID", "for-in enumerator is invalid");
        if (obj is not JsForInEnumeratorObject)
            ThrowTypeError("FORIN_ENUMERATOR_INVALID", "for-in enumerator is invalid");
        var enumeratorObj = (JsForInEnumeratorObject)obj;

        if (enumeratorObj.TryPeekKey(this, out var key))
            return key;
        return JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ForInStep(in JsValue enumeratorValue)
    {
        if (!enumeratorValue.TryGetObject(out var obj))
            ThrowTypeError("FORIN_ENUMERATOR_INVALID", "for-in enumerator is invalid");
        if (obj is not JsForInEnumeratorObject)
            ThrowTypeError("FORIN_ENUMERATOR_INVALID", "for-in enumerator is invalid");
        var enumeratorObj = (JsForInEnumeratorObject)obj;

        enumeratorObj.Step();
    }

    internal bool IsForInOwnEnumerableStringKey(JsObject owner, string key)
    {
        var keyValue = JsValue.FromString(key);
        if (owner.TryGetOwnEnumerableDescriptorViaTrap(this, keyValue, out var hasTrapDescriptor,
                out var trapEnumerable))
            return hasTrapDescriptor && trapEnumerable;

        if (TryGetArrayIndexFromCanonicalString(key, out var index))
        {
            if (owner is JsTypedArrayObject typedArray)
                return index < typedArray.Length;
            if (owner.HasOwnElement(index))
                return true;
        }

        var atom = Atoms.InternNoCheck(key);
        if (owner.TryGetOwnNamedPropertyDescriptorAtom(this, atom, out var descriptor))
            return descriptor.Enumerable;

        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue DeleteKeyedPropertyForRuntime(in JsValue targetValue, in JsValue key)
    {
        if (!targetValue.TryGetObject(out var targetObj))
            targetObj = ToObjectForPropertyAccessSlowPath(this, targetValue);

        if (targetObj is JsTypedArrayObject typedArray &&
            Intrinsics.TryDeleteTypedArrayIntegerIndexedElement(this, typedArray, key, out var typedArrayDeleted,
                out var typedArrayHandled))
            if (typedArrayHandled)
                return typedArrayDeleted ? JsValue.True : JsValue.False;

        if (key.IsSymbol)
            return targetObj.DeletePropertyAtom(this, key.AsSymbol().Atom) ? JsValue.True : JsValue.False;

        if (key.IsNumber && TryGetArrayIndexFromNumber(key.NumberValue, out var nIdx))
            return targetObj.DeleteElement(nIdx) ? JsValue.True : JsValue.False;
        if (key.IsString && TryGetArrayIndexFromCanonicalString(key.AsString(), out var sIdx))
            return targetObj.DeleteElement(sIdx) ? JsValue.True : JsValue.False;

        var text = key.IsString ? key.AsString() : this.ToJsStringSlowPath(key);
        if (TryGetArrayIndexFromCanonicalString(text, out var idx))
            return targetObj.DeleteElement(idx) ? JsValue.True : JsValue.False;
        var atom = Atoms.InternNoCheck(text);
        return targetObj.DeletePropertyAtom(this, atom) ? JsValue.True : JsValue.False;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsValue NormalizeSloppyThisValue(in JsValue thisValue)
    {
        if (thisValue.IsUndefined || thisValue.IsNull)
            return GlobalObject;
        if (thisValue.IsObject)
            return thisValue;
        return this.BoxPrimitive(thisValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetGeneratorNextMethodObject(JsObject iterator, out JsObject method)
    {
        method = null!;
        if (!iterator.TryGetPropertyAtom(this, IdNext, out var nextMethod, out _))
            return false;
        if (!nextMethod.TryGetObject(out var methodObj))
            return false;
        method = methodObj;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ThrowTypeError(string code, string message)
    {
        throw new JsRuntimeException(JsErrorKind.TypeError, message, code);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsRuntimeException TypeError(string code, string message)
    {
        return new(JsErrorKind.TypeError, message, code);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsRuntimeException TypeErrorInRealm(JsRealm realm, string code, string message)
    {
        return new(JsErrorKind.TypeError, message, code, errorRealm: realm);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ThrowReferenceError(string code)
    {
        throw new JsRuntimeException(JsErrorKind.ReferenceError, string.Empty, code);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ThrowReferenceError(string code, string message)
    {
        throw new JsRuntimeException(JsErrorKind.ReferenceError, message, code);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ResolveLazyRuntimeExceptionMessage(JsRuntimeException ex, JsScript script, int opcodePc)
    {
        if (ex.DetailCode == "TDZ_READ_BEFORE_INIT")
        {
            if (TryGetScriptDebugNameByPc(script, opcodePc, script.TdzReadDebugPcs, script.TdzReadDebugNameIndices,
                    out var name))
            {
                ex.ResolveMessageIfMissing($"Cannot access '{name}' before initialization");
                return;
            }

            ex.ResolveMessageIfMissing("Cannot access 'anonymous' before initialization");
        }
    }

    internal StackFrameInfo GetCurrentFrameInfo(Span<JsValue> fullStack, int framePointer, int programCounter)
    {
        if ((uint)framePointer >= (uint)fullStack.Length)
            throw new ArgumentOutOfRangeException(nameof(framePointer));

        ref readonly var frame = ref Unsafe.As<JsValue, CallFrame>(ref fullStack[framePointer]);
        return CreateStackFrameInfo(fullStack, framePointer, frame, programCounter);
    }

    internal string? GetCurrentSourcePath(Span<JsValue> fullStack, int framePointer)
    {
        if ((uint)framePointer >= (uint)fullStack.Length)
            throw new ArgumentOutOfRangeException(nameof(framePointer));

        ref readonly var frame = ref Unsafe.As<JsValue, CallFrame>(ref fullStack[framePointer]);
        return frame.Value0.Obj is JsBytecodeFunction bytecodeFunction ? bytecodeFunction.Script.SourcePath : null;
    }

    internal IReadOnlyList<StackFrameInfo> CaptureStackTraceSnapshot(Span<JsValue> fullStack, int throwFp, int throwPc)
    {
        var frames = new List<StackFrameInfo>(8);
        var fpCursor = throwFp;
        var pcCursor = throwPc;

        for (var depth = 0; depth < 1024; depth++)
        {
            if ((uint)fpCursor >= (uint)fullStack.Length)
                break;

            ref readonly var frame = ref Unsafe.As<JsValue, CallFrame>(ref fullStack[fpCursor]);
            frames.Add(CreateStackFrameInfo(fullStack, fpCursor, frame, pcCursor));

            var callerFp = frame.CallerFp;
            var callerPc = frame.CallerPc;
            if (callerFp == fpCursor)
                break;

            fpCursor = callerFp;
            pcCursor = callerPc;

            if (fpCursor == 0 && pcCursor == 0)
            {
                if ((uint)fpCursor < (uint)fullStack.Length)
                {
                    ref readonly var root = ref Unsafe.As<JsValue, CallFrame>(ref fullStack[fpCursor]);
                    var rootFunction = root.Value0.Obj as JsFunction;
                    var rootSourcePath = root.Value0.Obj is JsBytecodeFunction rootBytecodeFunction
                        ? rootBytecodeFunction.Script.SourcePath
                        : null;
                    frames.Add(new(
                        rootFunction?.Name ?? "<script>",
                        0,
                        root.FrameKind,
                        root.Flags,
                        false,
                        GeneratorState.SuspendedStart,
                        -1,
                        false,
                        0,
                        0,
                        rootSourcePath));
                }

                break;
            }
        }

        return frames;
    }

    private StackFrameInfo CreateStackFrameInfo(
        Span<JsValue> fullStack,
        int framePointer,
        in CallFrame frame,
        int programCounter)
    {
        var fn = frame.Value0.Obj as JsFunction;
        var name = fn?.Name ?? "<anonymous>";
        var script = fn is JsBytecodeFunction bytecodeFunction ? bytecodeFunction.Script : null;
        var sourcePath = script?.SourcePath;
        var sourceLine = 0;
        var sourceColumn = 0;
        var hasSourceLocation = script is not null &&
                                TryGetSourceLocation(script, programCounter, out sourceLine, out sourceColumn);
        if (hasSourceLocation &&
            sourcePath is { Length: > 0 } &&
            Agent.Engine.SourceMapRegistry is { } sourceMaps &&
            sourceMaps.TryMapToOriginal(sourcePath, sourceLine, sourceColumn, out var mappedLocation))
        {
            sourcePath = mappedLocation.SourcePath;
            sourceLine = mappedLocation.Line;
            sourceColumn = mappedLocation.Column;
        }

        if (TryGetActiveGeneratorForFrame(framePointer, out var generator))
            return new(
                name,
                programCounter,
                frame.FrameKind,
                frame.Flags,
                true,
                generator.State,
                generator.SuspendId,
                hasSourceLocation,
                hasSourceLocation ? sourceLine : 0,
                hasSourceLocation ? sourceColumn : 0,
                sourcePath);

        return new(
            name,
            programCounter,
            frame.FrameKind,
            frame.Flags,
            false,
            GeneratorState.SuspendedStart,
            -1,
            hasSourceLocation,
            hasSourceLocation ? sourceLine : 0,
            hasSourceLocation ? sourceColumn : 0,
            sourcePath);
    }

    private void CaptureExceptionStackIfMissing(JsRuntimeException ex, Span<JsValue> fullStack, int throwFp,
        int throwPc)
    {
        if (ex.StackFrames.Count != 0)
            return;

        ex.SetStackFramesIfMissing(CaptureStackTraceSnapshot(fullStack, throwFp, throwPc));
        if (Agent.IsCaughtExceptionHookEnabled &&
            Unsafe.As<JsValue, CallFrame>(ref fullStack[throwFp]).Function is JsBytecodeFunction currentFunc)
        {
            ref var bytecode = ref MemoryMarshal.GetArrayDataReference(currentFunc.Script.Bytecode);
            ref var checkpointPc = ref Unsafe.Add(ref bytecode, throwPc);
            EmitExecutionBoundaryCheckpoint(fullStack, throwFp, ExecutionCheckpointKind.CaughtException, ref bytecode,
                ref checkpointPc);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryGetSourceLocation(JsScript script, int opcodePc, out int line, out int column)
    {
        return JsScriptDebugInfo.TryGetSourceLocation(script, opcodePc, out line, out column);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ThrowJsValue(in JsValue value)
    {
        throw new JsRuntimeException(JsErrorKind.InternalError, $"Throw: {FormatThrownValueSummary(value)}",
            "JS_THROW_VALUE", value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string FormatThrownValueSummary(in JsValue value)
    {
        if (!value.TryGetObject(out var obj))
            return JsValueDebugString.FormatValue(value);

        if (!TryFormatErrorLikeThrownValue(obj, out var errorSummary))
            return JsValueDebugString.FormatValue(value);

        return errorSummary;
    }

    private static bool TryFormatErrorLikeThrownValue(JsObject target, out string summary)
    {
        var realm = target.Realm;
        if (!IsErrorLikeObject(target, realm))
        {
            summary = string.Empty;
            return false;
        }

        var name = "Error";
        if (target.TryGetPropertyAtom(realm, IdName, out var nameValue, out _) && !nameValue.IsUndefined)
            name = realm.ToJsStringSlowPath(nameValue);

        string? message = null;
        if (target.TryGetPropertyAtom(realm, IdMessage, out var messageValue, out _) && !messageValue.IsUndefined)
            message = realm.ToJsStringSlowPath(messageValue);

        summary = string.IsNullOrEmpty(message) ? name : $"{name}: {message}";
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsErrorLikeObject(JsObject obj, JsRealm realm)
    {
        if (ReferenceEquals(obj, realm.ErrorPrototype) ||
            ReferenceEquals(obj, realm.TypeErrorPrototype) ||
            ReferenceEquals(obj, realm.ReferenceErrorPrototype) ||
            ReferenceEquals(obj, realm.RangeErrorPrototype) ||
            ReferenceEquals(obj, realm.SyntaxErrorPrototype) ||
            ReferenceEquals(obj, realm.EvalErrorPrototype) ||
            ReferenceEquals(obj, realm.UriErrorPrototype) ||
            ReferenceEquals(obj, realm.AggregateErrorPrototype))
            return false;

        for (var cursor = obj.Prototype; cursor is not null; cursor = cursor.Prototype)
            if (ReferenceEquals(cursor, realm.ErrorPrototype))
                return true;

        return false;
    }

    internal JsValue CreateErrorObjectFromException(JsRuntimeException ex)
    {
        string name;
        var errorRealm = ex.ErrorRealm ?? this;
        JsObject prototype;
        switch (ex.Kind)
        {
            case JsErrorKind.TypeError:
                name = "TypeError";
                prototype = errorRealm.Intrinsics.TypeErrorPrototype;
                break;
            case JsErrorKind.ReferenceError:
                name = "ReferenceError";
                prototype = errorRealm.Intrinsics.ReferenceErrorPrototype;
                break;
            case JsErrorKind.RangeError:
                name = "RangeError";
                prototype = errorRealm.Intrinsics.RangeErrorPrototype;
                break;
            case JsErrorKind.SyntaxError:
                name = "SyntaxError";
                prototype = errorRealm.Intrinsics.SyntaxErrorPrototype;
                break;
            default:
                name = "Error";
                prototype = errorRealm.Intrinsics.ErrorPrototype;
                break;
        }

        var nativeException = ex.InnerException;
        while (nativeException?.InnerException is not null)
            nativeException = nativeException.InnerException;

        var error = new JsNativeErrorObject(errorRealm, nativeException ?? ex, false)
        {
            Prototype = prototype
        };
        error.DefineDataPropertyAtom(errorRealm, IdName, name, JsShapePropertyFlags.Open);
        error.DefineDataPropertyAtom(errorRealm, IdMessage, ex.Message,
            JsShapePropertyFlags.Open);
        error.DefineDataPropertyAtom(errorRealm, IdStack, FormatErrorStack(name, ex),
            JsShapePropertyFlags.Open);
        return error;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string FormatErrorStack(string errorName, JsRuntimeException ex)
    {
        var header = string.IsNullOrEmpty(ex.Message) ? errorName : $"{errorName}: {ex.Message}";
        var trace = ex.FormatOkojoStackTrace();
        if (string.IsNullOrEmpty(trace))
            return header;
        return $"{header}{Environment.NewLine}{trace}";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeThrowConstAssignError(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        var suffix = string.Empty;
        if (argCount > 0)
        {
            var arg = Unsafe.Add(ref registers, argRegStart);
            if (arg.IsString)
                suffix = $" '{arg.AsString()}'";
        }
        else if (TryGetScriptDebugNameByPc(script, opcodePc, script.RuntimeCallDebugPcs,
                     script.RuntimeCallDebugNameIndices, out var debugName))
        {
            suffix = $" '{debugName}'";
        }

        throw new JsRuntimeException(JsErrorKind.TypeError,
            $"Assignment to constant variable{suffix}.",
            "CONST_ASSIGN");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeGeneratorGetResumeMode(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        HandleIntrinsicGeneratorGetResumeMode(realm, script, opcodePc, ref registers, fp, argRegStart, argCount,
            ref acc);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeGeneratorClearResumeState(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        HandleIntrinsicGeneratorClearResumeState(realm, script, opcodePc, ref registers, fp, argRegStart, argCount,
            ref acc);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleIntrinsicClassGetPrototypeAndSetConstructor(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("CLASS_INTRINSIC_ARGC", "ClassGetPrototypeAndSetConstructor requires one argument");

        var ctorValue = Unsafe.Add(ref registers, argRegStart);
        if (!ctorValue.TryGetObject(out var ctorObj))
            ThrowTypeError("CLASS_INTRINSIC_CTOR", "Class constructor intrinsic requires object constructor");

        JsValue prototypeValue;
        if (!ctorObj.TryGetPropertyAtom(realm, IdPrototype, out prototypeValue, out _))
            prototypeValue = JsValue.Undefined;

        if (!prototypeValue.TryGetObject(out var prototypeObj))
            ThrowTypeError("CLASS_INTRINSIC_PROTO", "Class constructor prototype must be object");

        prototypeObj.DefineDataPropertyAtom(
            realm,
            IdConstructor,
            ctorValue,
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);

        acc = prototypeValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeGeneratorThrowValue(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        ThrowJsValue(acc);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeDeleteKeyedProperty(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 2)
            ThrowTypeError("DELETE_KEYED_ARGC", "DeleteKeyedProperty requires two arguments");
        var target = Unsafe.Add(ref registers, argRegStart);
        var key = Unsafe.Add(ref registers, argRegStart + 1);
        acc = realm.DeleteKeyedPropertyForRuntime(target, key);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeDeleteKeyedPropertyStrict(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 2)
            ThrowTypeError("DELETE_KEYED_STRICT_ARGC", "DeleteKeyedPropertyStrict requires two arguments");
        var target = Unsafe.Add(ref registers, argRegStart);
        var key = Unsafe.Add(ref registers, argRegStart + 1);
        var deleted = realm.DeleteKeyedPropertyForRuntime(target, key);
        if (!deleted.IsTrue)
            ThrowTypeError("DELETE_KEYED_STRICT_FAILED", "Cannot delete property in strict mode");
        acc = JsValue.True;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeNormalizePropertyKey(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("NORMALIZE_KEY_ARGC", "NormalizePropertyKey requires one argument");

        var value = Unsafe.Add(ref registers, argRegStart);
        acc = NormalizePropertyKey(realm, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static JsValue NormalizePropertyKey(JsRealm realm, in JsValue key)
    {
        if (key.IsSymbol || key.IsString || key.IsNumber)
            return key;

        var primitive = realm.ToPrimitiveSlowPath(key, true);
        if (primitive.IsSymbol || primitive.IsString || primitive.IsNumber)
            return primitive;

        // ToPropertyKey: for non-Symbol primitives, apply ToString.
        return realm.ToJsStringSlowPath(primitive);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeRequireObjectCoercible(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("REQUIRE_OBJ_COERCIBLE_ARGC", "RequireObjectCoercible requires one argument");

        var value = Unsafe.Add(ref registers, argRegStart);
        if (value.IsNullOrUndefined)
            ThrowTypeError("PROPERTY_READ_ON_NULLISH", "Cannot read properties of null or undefined");
        acc = value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeThrowParameterInitializerTdz(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        ThrowReferenceError("TDZ_READ_BEFORE_INIT");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeLoadKeyedFromSuper(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 2)
            ThrowTypeError("SUPER_KEYED_GET_ARGC", "LoadKeyedFromSuper requires two arguments");

        var thisValue = Unsafe.Add(ref registers, argRegStart);
        var keyValue = Unsafe.Add(ref registers, argRegStart + 1);
        if (thisValue.IsTheHole)
            ThrowSuperNotCalled();
        if (!thisValue.TryGetObject(out var receiver))
            ThrowTypeError("SUPER_RECEIVER", "super receiver must be object");

        var superBase = realm.RequireObjectSuperBaseForFrame(fp);
        if (TryResolveRuntimePropertyKey(realm, keyValue, out var index, out var atom))
        {
            if (superBase.TryGetElementWithReceiver(realm, receiver, index, out var indexedValue))
                acc = indexedValue;
            else
                acc = JsValue.Undefined;
        }
        else if (superBase.TryGetPropertyAtomWithReceiver(realm, receiver, atom, out var value, out _))
        {
            acc = value;
        }
        else
        {
            acc = JsValue.Undefined;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeGetCurrentFunctionSuperBase(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 0)
            ThrowTypeError("SUPER_BASE_ARGC", "GetCurrentFunctionSuperBase expects zero arguments");
        acc = realm.ResolveSuperBaseValueForFrame(fp);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeGetObjectPrototypeForSuper(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 0)
            ThrowTypeError("SUPER_HOME_PROTO_ARGC", "GetObjectPrototypeForSuper expects zero arguments");

        if (!acc.TryGetObject(out var homeObject))
            ThrowTypeError("SUPER_BASE", "super base is not available");

        acc = homeObject.Prototype is null
            ? JsValue.Null
            : JsValue.FromObject(homeObject.Prototype);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeThrowDeleteSuperPropertyReference(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 0)
            ThrowTypeError("DELETE_SUPER_ARGC", "ThrowDeleteSuperPropertyReference expects zero arguments");
        ThrowReferenceError("DELETE_SUPER", "Cannot delete a super property");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeCreateRegExpLiteral(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 2)
            ThrowTypeError("REGEXP_LITERAL_ARGC", "CreateRegExpLiteral requires pattern and flags");

        var pattern = realm.ToJsStringSlowPath(Unsafe.Add(ref registers, argRegStart));
        var flags = realm.ToJsStringSlowPath(Unsafe.Add(ref registers, argRegStart + 1));
        acc = realm.CreateRegExpObject(pattern, flags);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeGetTemplateObject(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("TEMPLATE_OBJECT_ARGC", "GetTemplateObject requires one argument");

        var siteIndexValue = Unsafe.Add(ref registers, argRegStart);
        if (!siteIndexValue.IsInt32)
            ThrowTypeError("TEMPLATE_OBJECT_SITE_INDEX", "Template site index must be integer");
        var siteIndex = siteIndexValue.Int32Value;
        if ((uint)siteIndex >= (uint)script.ObjectConstants.Length)
            ThrowTypeError("TEMPLATE_OBJECT_SITE_INDEX", "Invalid template site index");
        var siteObject = script.ObjectConstants[siteIndex];
        if (siteObject is not JsTemplateSiteDescriptor)
            ThrowTypeError("TEMPLATE_OBJECT_SITE_INDEX", "Invalid template site index");
        var site = (JsTemplateSiteDescriptor)siteObject;

        acc = site.GetOrCreate(realm);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeForInEnumerate(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("FORIN_ENUMERATE_ARGC", "ForInEnumerate requires one argument");
        acc = realm.ForInEnumerate(Unsafe.Add(ref registers, argRegStart));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeForInStepKey(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("FORIN_STEP_KEY_ARGC", "ForInStepKey requires one argument");
        acc = realm.ForInStepKey(Unsafe.Add(ref registers, argRegStart));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDefineClassMethod(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 3)
            ThrowTypeError("CLASS_METHOD_ARGC", "DefineClassMethod requires three arguments");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        var keyValue = Unsafe.Add(ref registers, argRegStart + 1);
        var methodValue = Unsafe.Add(ref registers, argRegStart + 2);

        if (!targetValue.TryGetObject(out var target))
            ThrowTypeError("CLASS_METHOD_TARGET", "Class method target must be object");

        AssignFunctionNameFromPropertyKey(realm, methodValue, keyValue);

        if (TryResolveRuntimePropertyKey(realm, keyValue, out var index, out var atom))
        {
            target.DefineElementDescriptor(index, PropertyDescriptor.Data(
                methodValue,
                true,
                false,
                true));
        }
        else
        {
            if (target is JsFunction && atom == IdPrototype)
                ThrowTypeError("CLASS_METHOD_PROTOTYPE", "Cannot redefine class constructor 'prototype'");
            _ = target.DefineOwnDataPropertyExact(realm, atom, methodValue,
                JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);
        }

        acc = JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDefineClassAccessor(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 4)
            ThrowTypeError("CLASS_ACCESSOR_ARGC", "DefineClassAccessor requires four arguments");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        var keyValue = Unsafe.Add(ref registers, argRegStart + 1);
        var getterValue = Unsafe.Add(ref registers, argRegStart + 2);
        var setterValue = Unsafe.Add(ref registers, argRegStart + 3);

        if (!targetValue.TryGetObject(out var target))
            ThrowTypeError("CLASS_ACCESSOR_TARGET", "Class accessor target must be object");

        var getter = CoerceClassAccessorFunction(getterValue, "CLASS_ACCESSOR_GETTER",
            "Class accessor getter must be function or undefined");
        var setter = CoerceClassAccessorFunction(setterValue, "CLASS_ACCESSOR_SETTER",
            "Class accessor setter must be function or undefined");

        if (getter is null && setter is null)
            ThrowTypeError("CLASS_ACCESSOR_EMPTY", "Class accessor requires getter and/or setter");

        if (getter is not null)
            AssignFunctionNameFromPropertyKey(realm, getterValue, keyValue, "get");
        if (setter is not null)
            AssignFunctionNameFromPropertyKey(realm, setterValue, keyValue, "set");

        if (TryResolveRuntimePropertyKey(realm, keyValue, out var index, out var atom))
        {
            var descriptor = BuildMergedIndexedAccessorDescriptor(
                target,
                index,
                getter,
                setter,
                false,
                true);
            target.DefineElementDescriptor(index, descriptor);
        }
        else
        {
            if (target is JsFunction && atom == IdPrototype)
                ThrowTypeError("CLASS_ACCESSOR_PROTOTYPE", "Cannot redefine class constructor 'prototype'");
            target.DefineClassAccessorPropertyAtom(realm, atom, getter, setter);
        }

        acc = JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsFunction? CoerceClassAccessorFunction(in JsValue value, string detailCode, string message)
    {
        if (value.IsUndefined)
            return null;
        if (value.TryGetObject(out var obj) && obj is JsFunction fn)
            return fn;
        ThrowTypeError(detailCode, message);
        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PropertyDescriptor BuildMergedIndexedAccessorDescriptor(
        JsObject target,
        uint index,
        JsFunction? getter,
        JsFunction? setter,
        bool enumerable,
        bool configurable)
    {
        if (target.TryGetOwnElementDescriptor(index, out var existing) && existing.IsAccessor)
        {
            getter ??= existing.Getter;
            setter ??= existing.Setter;
        }

        return getter is not null && setter is not null
            ? PropertyDescriptor.GetterSetterData(getter, setter, enumerable, configurable)
            : getter is not null
                ? PropertyDescriptor.GetterData(getter, enumerable, configurable)
                : PropertyDescriptor.SetterData(setter!, enumerable, configurable);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDefineClassField(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 3)
            ThrowTypeError("CLASS_FIELD_ARGC", "DefineClassField requires three arguments");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        var keyValue = Unsafe.Add(ref registers, argRegStart + 1);
        var value = Unsafe.Add(ref registers, argRegStart + 2);

        if (!targetValue.TryGetObject(out var target))
            ThrowTypeError("CLASS_FIELD_TARGET", "Class field target must be object");

        if (TryResolveRuntimePropertyKey(realm, keyValue, out var index, out var atom))
        {
            DefineClassFieldOrThrow(realm, target, JsValue.FromString(index.ToString(CultureInfo.InvariantCulture)),
                value);
        }
        else
        {
            if (target is JsFunction && atom == IdPrototype)
                ThrowTypeError("CLASS_FIELD_PROTOTYPE", "Cannot define class field with name 'prototype'");
            DefineClassFieldOrThrow(realm, target, keyValue, value);
        }

        acc = JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefineClassFieldOrThrow(JsRealm realm, JsObject target, in JsValue key, in JsValue value)
    {
        if (TryResolveRuntimePropertyKey(realm, key, out var index, out var atom))
        {
            if (target.TryDefineOwnDataPropertyForSet(realm, index, value, out _))
                return;
        }
        else
        {
            if (target.TryDefineOwnDataPropertyForSet(realm, atom, value, out _))
                return;
        }

        var descriptorObject = new JsPlainObject(realm);
        descriptorObject.DefineDataPropertyAtom(realm, IdValue, value, JsShapePropertyFlags.Open);
        descriptorObject.DefineDataPropertyAtom(realm, IdWritable, JsValue.True, JsShapePropertyFlags.Open);
        descriptorObject.DefineDataPropertyAtom(realm, IdEnumerable, JsValue.True, JsShapePropertyFlags.Open);
        descriptorObject.DefineDataPropertyAtom(realm, IdConfigurable, JsValue.True,
            JsShapePropertyFlags.Open);

        const int atomDefineProperty = IdDefineProperty;
        if (!realm.Intrinsics.ObjectConstructor.TryGetPropertyAtom(realm, atomDefineProperty, out var methodValue,
                out _))
            ThrowTypeError("CLASS_FIELD_DEFINE_PROPERTY", "Object.defineProperty is not callable");
        if (!methodValue.TryGetObject(out var methodObj))
            ThrowTypeError("CLASS_FIELD_DEFINE_PROPERTY", "Object.defineProperty is not callable");
        if (methodObj is not JsFunction)
            ThrowTypeError("CLASS_FIELD_DEFINE_PROPERTY", "Object.defineProperty is not callable");
        var methodFn = (JsFunction)methodObj;

        var args = new InlineJsValueArray3
        {
            Item0 = JsValue.FromObject(target),
            Item1 = key,
            Item2 = JsValue.FromObject(descriptorObject)
        };
        _ = realm.InvokeFunction(methodFn, JsValue.FromObject(realm.Intrinsics.ObjectConstructor), args.AsSpan());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeDefineObjectAccessor(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 4)
            ThrowTypeError("OBJ_ACCESSOR_ARGC", "DefineObjectAccessor requires four arguments");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        var keyValue = Unsafe.Add(ref registers, argRegStart + 1);
        var getterValue = Unsafe.Add(ref registers, argRegStart + 2);
        var setterValue = Unsafe.Add(ref registers, argRegStart + 3);

        if (!targetValue.TryGetObject(out var target))
            ThrowTypeError("OBJ_ACCESSOR_TARGET", "Object accessor target must be object");

        var getter = CoerceClassAccessorFunction(getterValue, "OBJ_ACCESSOR_GETTER",
            "Object accessor getter must be function or undefined");
        var setter = CoerceClassAccessorFunction(setterValue, "OBJ_ACCESSOR_SETTER",
            "Object accessor setter must be function or undefined");

        if (getter is null && setter is null)
            ThrowTypeError("OBJ_ACCESSOR_EMPTY", "Object accessor requires getter and/or setter");

        if (getter is not null)
            AssignFunctionNameFromPropertyKey(realm, getterValue, keyValue, "get");
        if (setter is not null)
            AssignFunctionNameFromPropertyKey(realm, setterValue, keyValue, "set");

        if (TryResolveRuntimePropertyKey(realm, keyValue, out var index, out var atom))
        {
            var descriptor = BuildMergedIndexedAccessorDescriptor(
                target,
                index,
                getter,
                setter,
                true,
                true);
            target.DefineElementDescriptor(index, descriptor);
        }
        else
        {
            var flags = DescriptorUtilities.BuildAccessorFlags(
                true,
                true,
                getter is not null,
                setter is not null);
            target.DefineAccessorPropertyAtom(realm, atom, getter, setter, flags);
        }

        acc = JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeCopyDataProperties(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 2)
            ThrowTypeError("COPY_DATA_PROPERTIES_ARGC", "CopyDataProperties requires two arguments");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        var sourceValue = Unsafe.Add(ref registers, argRegStart + 1);
        if (!targetValue.TryGetObject(out var target))
            ThrowTypeError("COPY_DATA_PROPERTIES_TARGET", "CopyDataProperties target must be object");

        realm.Intrinsics.CopyDataPropertiesOntoObject(target, sourceValue);
        acc = targetValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeCopyDataPropertiesExcluding(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount < 2)
            ThrowTypeError("COPY_DATA_PROPERTIES_EXCLUDING_ARGC",
                "CopyDataPropertiesExcluding requires at least two arguments");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        var sourceValue = Unsafe.Add(ref registers, argRegStart + 1);
        if (!targetValue.TryGetObject(out var target))
            ThrowTypeError("COPY_DATA_PROPERTIES_EXCLUDING_TARGET",
                "CopyDataPropertiesExcluding target must be object");

        realm.Intrinsics.CopyDataPropertiesOntoObjectExcluding(target, sourceValue,
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref registers, argRegStart + 2), argCount - 2));
        acc = targetValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeSetFunctionName(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 2)
            ThrowTypeError("SET_FUNCTION_NAME_ARGC", "SetFunctionName requires target and name");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        if (!targetValue.TryGetObject(out var target))
            ThrowTypeError("SET_FUNCTION_NAME_TARGET", "SetFunctionName target must be object");

        var nameValue = Unsafe.Add(ref registers, argRegStart + 1);
        try
        {
            SetFunctionNameProperty(realm, target, nameValue.AsString(), false);
        }
        catch (IndexOutOfRangeException ex)
        {
            throw new InvalidOperationException(
                $"SetFunctionName failed: target={target.GetType().FullName}, name='{nameValue.AsString()}', targetName='{target}'",
                ex);
        }

        acc = targetValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AssignFunctionNameFromPropertyKey(JsRealm realm, in JsValue functionValue,
        in JsValue keyValue,
        string? prefix = null)
    {
        var name = GetFunctionNameFromPropertyKey(realm, keyValue, prefix);
        AssignFunctionNameFromResolvedPropertyKey(realm, functionValue, name);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AssignFunctionNameFromResolvedPropertyKey(
        JsRealm realm,
        in JsValue functionValue,
        string name)
    {
        if (!functionValue.TryGetObject(out var functionObject))
            return;

        SetFunctionNameProperty(realm, functionObject, name, true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetFunctionNameProperty(JsRealm realm, JsObject target, string name, bool overwriteExisting)
    {
        if (!overwriteExisting &&
            target.TryGetPropertyAtom(realm, IdName, out var existingNameValue, out _) &&
            existingNameValue.IsString &&
            existingNameValue.AsString().Length != 0)
            return;

        target.DefineDataPropertyAtom(realm, IdName, JsValue.FromString(name),
            JsShapePropertyFlags.Configurable);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetFunctionNameFromPropertyKey(JsRealm realm, in JsValue keyValue, string? prefix)
    {
        string keyText;
        if (keyValue.IsSymbol)
        {
            var description = keyValue.AsSymbol().Description;
            keyText = string.IsNullOrEmpty(description) ? string.Empty : $"[{description}]";
        }
        else if (keyValue.IsString)
        {
            keyText = keyValue.AsString();
        }
        else
        {
            keyText = realm.ToJsStringSlowPath(keyValue);
        }

        return prefix is null ? keyText : $"{prefix} {keyText}";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeSetFunctionInstanceFieldKey(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 3)
            ThrowTypeError("SET_FUNCTION_INSTANCE_FIELD_KEY_ARGC",
                "SetFunctionInstanceFieldKey requires target, index, and value");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        if (!targetValue.TryGetObject(out var targetObj) || targetObj is not JsBytecodeFunction)
            ThrowTypeError("SET_FUNCTION_INSTANCE_FIELD_KEY_TARGET",
                "SetFunctionInstanceFieldKey target must be bytecode function");
        var target = (JsBytecodeFunction)targetObj;

        var index = Unsafe.Add(ref registers, argRegStart + 1).Int32Value;
        if (index < 0)
            ThrowTypeError("SET_FUNCTION_INSTANCE_FIELD_KEY_INDEX", "instance field key index must be non-negative");

        var value = Unsafe.Add(ref registers, argRegStart + 2);
        var keys = target.PrecomputedInstanceFieldKeys;
        if (keys is null || keys.Length <= index)
        {
            var grown = new JsValue[index + 1];
            if (keys is not null)
                keys.CopyTo(grown, 0);
            target.PrecomputedInstanceFieldKeys = keys = grown;
        }

        keys[index] = value;
        acc = targetValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeLoadCurrentFunctionInstanceFieldKey(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("LOAD_CURRENT_FUNCTION_INSTANCE_FIELD_KEY_ARGC",
                "LoadCurrentFunctionInstanceFieldKey requires index");

        var index = Unsafe.Add(ref registers, argRegStart).Int32Value;
        if (index < 0)
            ThrowTypeError("LOAD_CURRENT_FUNCTION_INSTANCE_FIELD_KEY_INDEX",
                "instance field key index must be non-negative");

        ref readonly var callFrame = ref Unsafe.As<JsValue, CallFrame>(ref realm.Stack[fp]);
        if (callFrame.Function is not JsBytecodeFunction)
            ThrowTypeError("LOAD_CURRENT_FUNCTION_INSTANCE_FIELD_KEY_MISSING",
                "missing precomputed instance field key");

        var function = (JsBytecodeFunction)callFrame.Function;
        var keys = function.PrecomputedInstanceFieldKeys;
        if (keys is null || index >= keys.Length)
            ThrowTypeError("LOAD_CURRENT_FUNCTION_INSTANCE_FIELD_KEY_MISSING",
                "missing precomputed instance field key");

        acc = keys[index];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeSetFunctionPrivateBrandToken(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 2)
            ThrowTypeError("SET_FUNCTION_PRIVATE_BRAND_TOKEN_ARGC",
                "SetFunctionPrivateBrandToken requires target and brand source");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        if (!targetValue.TryGetObject(out var targetObj) || targetObj is not JsBytecodeFunction)
            ThrowTypeError("SET_FUNCTION_PRIVATE_BRAND_TOKEN_TARGET",
                "SetFunctionPrivateBrandToken target must be bytecode function");

        var target = (JsBytecodeFunction)targetObj!;

        var brandSourceValue = Unsafe.Add(ref registers, argRegStart + 1);
        if (!brandSourceValue.TryGetObject(out var brandSource))
            ThrowTypeError("SET_FUNCTION_PRIVATE_BRAND_TOKEN_SOURCE",
                "SetFunctionPrivateBrandToken source must be object");

        target.SetPrivateBrandToken(brandSource is JsBytecodeFunction brandFunction
            ? brandFunction.ResolvePrivateBrandSourceToken()
            : brandSource);
        acc = targetValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeSetFunctionMethodEnvironment(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 3)
            ThrowTypeError("SET_FUNCTION_METHOD_ENV_ARGC",
                "SetFunctionMethodEnvironment requires target, home object, and class lexical value");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        if (!targetValue.TryGetObject(out var targetObj) || targetObj is not JsBytecodeFunction)
            ThrowTypeError("SET_FUNCTION_METHOD_ENV_TARGET",
                "SetFunctionMethodEnvironment target must be bytecode function");

        var target = (JsBytecodeFunction)targetObj!;

        var homeObjectValue = Unsafe.Add(ref registers, argRegStart + 1);
        if (!homeObjectValue.TryGetObject(out var homeObject))
            ThrowTypeError("SET_FUNCTION_METHOD_ENV_HOME",
                "SetFunctionMethodEnvironment home object must be object");

        var parent = target.BoundParentContext;
        var context = new JsContext(parent, 2);
        context.Slots[0] = homeObjectValue;
        context.Slots[1] = Unsafe.Add(ref registers, argRegStart + 2);
        target.BoundParentContext = context;
        acc = targetValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeSetFunctionPrivateMethodValue(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 3)
            ThrowTypeError("SET_FUNCTION_PRIVATE_METHOD_VALUE_ARGC",
                "SetFunctionPrivateMethodValue requires target, index, and value");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        if (!targetValue.TryGetObject(out var targetObj) || targetObj is not JsBytecodeFunction)
            ThrowTypeError("SET_FUNCTION_PRIVATE_METHOD_VALUE_TARGET",
                "SetFunctionPrivateMethodValue target must be bytecode function");

        var target = (JsBytecodeFunction)targetObj!;

        var index = Unsafe.Add(ref registers, argRegStart + 1).Int32Value;
        if (index < 0)
            ThrowTypeError("SET_FUNCTION_PRIVATE_METHOD_VALUE_INDEX",
                "private method cache index must be non-negative");

        var value = Unsafe.Add(ref registers, argRegStart + 2);
        target.StorePrivateMethodValue(index, value);
        acc = targetValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeLoadCurrentFunctionPrivateMethodValue(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 1)
            ThrowTypeError("LOAD_CURRENT_FUNCTION_PRIVATE_METHOD_VALUE_ARGC",
                "LoadCurrentFunctionPrivateMethodValue requires index");

        var index = Unsafe.Add(ref registers, argRegStart).Int32Value;
        if (index < 0)
            ThrowTypeError("LOAD_CURRENT_FUNCTION_PRIVATE_METHOD_VALUE_INDEX",
                "private method cache index must be non-negative");

        ref readonly var callFrame = ref GetCurrentCallFrame(realm.Stack, fp);
        if (callFrame.Function is not JsBytecodeFunction)
            ThrowTypeError("LOAD_CURRENT_FUNCTION_PRIVATE_METHOD_VALUE_MISSING",
                "missing precomputed private method value");

        var function = (JsBytecodeFunction)callFrame.Function!;

        if (!function.TryLoadPrivateMethodValue(index, out acc))
            ThrowTypeError("LOAD_CURRENT_FUNCTION_PRIVATE_METHOD_VALUE_MISSING",
                "missing precomputed private method value");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeSetFunctionPrivateBrandMapping(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 3)
            ThrowTypeError("SET_FUNCTION_PRIVATE_BRAND_MAPPING_ARGC",
                "SetFunctionPrivateBrandMapping requires target, brand id, and source");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        if (!targetValue.TryGetObject(out var targetObj) || targetObj is not JsBytecodeFunction)
            ThrowTypeError("SET_FUNCTION_PRIVATE_BRAND_MAPPING_TARGET",
                "SetFunctionPrivateBrandMapping target must be bytecode function");

        var target = (JsBytecodeFunction)targetObj!;

        var brandId = Unsafe.Add(ref registers, argRegStart + 1).Int32Value;
        if (brandId < 0)
            ThrowTypeError("SET_FUNCTION_PRIVATE_BRAND_MAPPING_BRAND",
                "private brand id must be non-negative");

        var sourceValue = Unsafe.Add(ref registers, argRegStart + 2);
        if (!sourceValue.TryGetObject(out var source))
            ThrowTypeError("SET_FUNCTION_PRIVATE_BRAND_MAPPING_SOURCE",
                "SetFunctionPrivateBrandMapping source must be object");

        target.SetPrivateBrandMapping(brandId, source is JsBytecodeFunction sourceFunction
            ? sourceFunction.ResolvePrivateBrandMappingSource(brandId)
            : source);
        acc = targetValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeSetFunctionPrivateBrandMappingExact(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 3)
            ThrowTypeError("SET_FUNCTION_PRIVATE_BRAND_MAPPING_EXACT_ARGC",
                "SetFunctionPrivateBrandMappingExact requires target, brand id, and source");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        if (!targetValue.TryGetObject(out var targetObj) || targetObj is not JsBytecodeFunction)
            ThrowTypeError("SET_FUNCTION_PRIVATE_BRAND_MAPPING_EXACT_TARGET",
                "SetFunctionPrivateBrandMappingExact target must be bytecode function");

        var target = (JsBytecodeFunction)targetObj!;

        var brandId = Unsafe.Add(ref registers, argRegStart + 1).Int32Value;
        if (brandId < 0)
            ThrowTypeError("SET_FUNCTION_PRIVATE_BRAND_MAPPING_EXACT_BRAND",
                "private brand id must be non-negative");

        var sourceValue = Unsafe.Add(ref registers, argRegStart + 2);
        if (!sourceValue.TryGetObject(out var source))
            ThrowTypeError("SET_FUNCTION_PRIVATE_BRAND_MAPPING_EXACT_SOURCE",
                "SetFunctionPrivateBrandMappingExact source must be object");

        target.SetPrivateBrandMapping(brandId, source);
        acc = targetValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeAppendArraySpread(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 3)
            ThrowTypeError("APPEND_ARRAY_SPREAD_ARGC", "AppendArraySpread requires three arguments");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        var sourceValue = Unsafe.Add(ref registers, argRegStart + 1);
        var nextIndexValue = Unsafe.Add(ref registers, argRegStart + 2);
        if (!targetValue.TryGetObject(out var target))
            ThrowTypeError("APPEND_ARRAY_SPREAD_TARGET", "AppendArraySpread target must be object");

        var nextIndex = realm.ToUint32(nextIndexValue);
        var next = realm.AppendArraySpreadValues(target, sourceValue, nextIndex);
        acc = next <= int.MaxValue ? JsValue.FromInt32((int)next) : new((double)next);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeGetCurrentModuleSetFunctionName(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 0)
            ThrowTypeError("MODULE_BINDING_SETNAME_ARGC", "GetCurrentModuleSetFunctionName expects zero arguments");
        acc = realm.Agent.GetCurrentModuleSetFunctionNameBinding();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleRuntimeGetCurrentModuleImportMeta(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 0)
            ThrowTypeError("MODULE_BINDING_META_ARGC", "GetCurrentModuleImportMeta expects zero arguments");
        acc = realm.Agent.GetCurrentModuleImportMetaBinding(realm, realm.GetCurrentContext());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryResolveRuntimePropertyKey(
        JsRealm realm,
        in JsValue keyValue,
        out uint index,
        out int atom)
    {
        var primitive = keyValue;
        if (primitive.IsObject)
            primitive = realm.ToPrimitiveSlowPath(primitive, true);

        if (primitive.IsSymbol)
        {
            index = 0;
            atom = primitive.AsSymbol().Atom;
            return false;
        }

        if (primitive.IsString)
        {
            var text = primitive.AsString();
            if (TryGetArrayIndexFromCanonicalString(text, out index))
            {
                atom = 0;
                return true;
            }

            atom = realm.Atoms.InternNoCheck(text);
            return false;
        }

        if (primitive.IsNumber)
        {
            if (TryGetArrayIndexFromNumber(primitive.NumberValue, out index))
            {
                atom = 0;
                return true;
            }

            var numberText = JsValue.NumberToJsString(primitive.NumberValue);
            if (TryGetArrayIndexFromCanonicalString(numberText, out index))
            {
                atom = 0;
                return true;
            }

            atom = realm.Atoms.InternNoCheck(numberText);
            return false;
        }

        var fallback = realm.ToJsStringSlowPath(primitive);
        if (TryGetArrayIndexFromCanonicalString(fallback, out index))
        {
            atom = 0;
            return true;
        }

        atom = realm.Atoms.InternNoCheck(fallback);
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeCallWithSpread(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount < 3)
            ThrowTypeError("CALL_SPREAD_ARGC",
                "CallWithSpread requires callee, thisValue, flags, and optional arguments");

        var calleeValue = Unsafe.Add(ref registers, argRegStart);
        var thisValue = Unsafe.Add(ref registers, argRegStart + 1);
        var flagsValue = Unsafe.Add(ref registers, argRegStart + 2);

        if (!calleeValue.TryGetObject(out var calleeObj) || calleeObj is not JsFunction)
            ThrowTypeError("NOT_CALLABLE", "target is not a function");
        var callee = (JsFunction)calleeObj;

        var savedSp = realm.StackTop;
        int spreadArgOffset;
        int spreadArgCount;
        try
        {
            spreadArgOffset = realm.CopySpreadArgumentsToStackTop(flagsValue, argCount == 3
                    ? ReadOnlySpan<JsValue>.Empty
                    : MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref registers, argRegStart + 3), argCount - 3),
                out spreadArgCount);
            acc = realm.DispatchCallFromStack(callee, thisValue, spreadArgOffset, spreadArgCount, 0);
        }
        finally
        {
            realm.RestoreTemporaryArgumentWindow(savedSp);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeConstructWithSpread(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount < 2)
            ThrowTypeError("CONSTRUCT_SPREAD_ARGC",
                "ConstructWithSpread requires callee, flags, and optional arguments");

        var calleeValue = Unsafe.Add(ref registers, argRegStart);
        var flagsValue = Unsafe.Add(ref registers, argRegStart + 1);

        if (!calleeValue.TryGetObject(out var calleeObj) || calleeObj is not JsFunction)
            ThrowTypeError("NOT_CONSTRUCTOR", "constructor is not a function");
        var callee = (JsFunction)calleeObj;

        var savedSp = realm.StackTop;
        int spreadArgOffset;
        int spreadArgCount;
        try
        {
            spreadArgOffset = realm.CopySpreadArgumentsToStackTop(flagsValue, argCount == 2
                    ? ReadOnlySpan<JsValue>.Empty
                    : MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref registers, argRegStart + 2), argCount - 2),
                out spreadArgCount);
            var prepared = realm.PrepareConstructInvocation(callee, callee);
            acc = realm.DispatchConstructFromStack(callee, prepared, spreadArgOffset, spreadArgCount, opcodePc);
        }
        finally
        {
            realm.RestoreTemporaryArgumentWindow(savedSp);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int CopySpreadArgumentsToStackTop(in JsValue flagsValue, ReadOnlySpan<JsValue> args, out int argCount)
    {
        if (flagsValue.Obj is not int[])
            ThrowTypeError("SPREAD_FLAGS_INVALID", "spread flags are invalid");
        var spreadFlags = (int[])flagsValue.Obj!;
        if (spreadFlags.Length != args.Length)
            ThrowTypeError("SPREAD_FLAGS_LENGTH", "spread flags length does not match arguments");

        var argOffset = StackTop;
        argCount = 0;
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (spreadFlags[i] == 0)
                {
                    AppendMaterializedArgumentToStack(args[i], ref argCount);
                    continue;
                }

                AppendSpreadArgumentValuesToStack(args[i], ref argCount);
            }
        }
        catch
        {
            StackTop = argOffset;
            throw;
        }

        return argOffset;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AppendSpreadArgumentValuesToStack(in JsValue value, ref int argCount)
    {
        if (!this.TryToObject(value, out var iterableObj))
            throw TypeError("SPREAD_NOT_ITERABLE", "Spread syntax requires an iterable");

        if (!iterableObj.TryGetPropertyAtom(this, IdSymbolIterator, out var iteratorMethod, out _) ||
            !iteratorMethod.TryGetObject(out var iteratorMethodObj) || iteratorMethodObj is not JsFunction iteratorFn)
            throw TypeError("SPREAD_NOT_ITERABLE", "Spread syntax requires an iterable");

        var iteratorValue = InvokeFunction(iteratorFn, iterableObj, ReadOnlySpan<JsValue>.Empty);
        if (!iteratorValue.TryGetObject(out var iteratorObj))
            throw TypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator is not an object");

        try
        {
            while (true)
            {
                var elementValue = DestructureArrayStepValue(iteratorObj, out var done);
                if (done)
                    break;
                AppendMaterializedArgumentToStack(elementValue.IsTheHole ? JsValue.Undefined : elementValue,
                    ref argCount);
            }
        }
        catch
        {
            BestEffortIteratorCloseOnThrow(iteratorObj);
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendMaterializedArgumentToStack(in JsValue value, ref int argCount)
    {
        if (StackTop >= Stack.Length)
            throw new StackOverflowException();

        Stack[StackTop++] = value;
        argCount++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private uint AppendArraySpreadValues(JsObject target, in JsValue value, uint nextIndex)
    {
        if (!this.TryToObject(value, out var iterableObj))
            throw TypeError("SPREAD_NOT_ITERABLE", "Spread syntax requires an iterable");

        if (!iterableObj.TryGetPropertyAtom(this, IdSymbolIterator, out var iteratorMethod, out _) ||
            !iteratorMethod.TryGetObject(out var iteratorMethodObj) || iteratorMethodObj is not JsFunction iteratorFn)
            throw TypeError("SPREAD_NOT_ITERABLE", "Spread syntax requires an iterable");

        var iteratorValue = InvokeFunction(iteratorFn, iterableObj, ReadOnlySpan<JsValue>.Empty);
        if (!iteratorValue.TryGetObject(out var iteratorObj))
            throw TypeError("ITERATOR_RESULT_NOT_OBJECT", "iterator is not an object");

        try
        {
            while (true)
            {
                var elementValue = DestructureArrayStepValue(iteratorObj, out var done);
                if (done)
                    return nextIndex;

                var spreadValue = elementValue.IsTheHole ? JsValue.Undefined : elementValue;
                FreshArrayOperations.DefineElement(target, nextIndex++, spreadValue);
            }
        }
        catch
        {
            BestEffortIteratorCloseOnThrow(iteratorObj);
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeSuperSet(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 3)
            ThrowTypeError("SUPER_SET_ARGC", "SuperSet requires three arguments");

        var thisValue = Unsafe.Add(ref registers, argRegStart);
        var keyValue = Unsafe.Add(ref registers, argRegStart + 1);
        var value = Unsafe.Add(ref registers, argRegStart + 2);
        if (thisValue.IsTheHole)
            ThrowSuperNotCalled();
        if (!thisValue.TryGetObject(out var receiver))
            ThrowTypeError("SUPER_RECEIVER", "super receiver must be object");

        var superBase = realm.RequireObjectSuperBaseForFrame(fp);
        var succeeded = TryResolveRuntimePropertyKey(realm, keyValue, out var index, out var atom)
            ? superBase.SetElementWithReceiver(realm, receiver, index, value)
            : superBase.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out _);
        if (!succeeded && realm.IsStrictFunctionFrame(fp))
            ThrowTypeError("SUPER_SET_FAILED", "Failed to set super property");
        acc = value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ToPropertyAtomForRuntime(JsRealm realm, in JsValue keyValue)
    {
        if (keyValue.IsString)
        {
            var text = keyValue.AsString();
            if (TryGetArrayIndexFromCanonicalString(text, out _))
                throw new InvalidOperationException(
                    "Array-index string atomization is disabled for runtime property keys.");

            return realm.Atoms.InternNoCheck(text);
        }

        if (keyValue.IsSymbol)
            return keyValue.AsSymbol().Atom;
        var fallback = realm.ToJsStringSlowPath(keyValue);
        if (TryGetArrayIndexFromCanonicalString(fallback, out _))
            throw new InvalidOperationException(
                "Array-index string atomization is disabled for runtime property keys.");

        return realm.Atoms.InternNoCheck(fallback);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryGetArrayIndexFromRuntimePropertyKey(JsRealm realm, in JsValue keyValue,
        out uint index)
    {
        if (keyValue.IsNumber)
            return TryGetArrayIndexFromNumber(keyValue.NumberValue, out index);
        if (keyValue.IsString)
            return TryGetArrayIndexFromCanonicalString(keyValue.AsString(), out index);
        if (keyValue.IsSymbol)
        {
            index = 0;
            return false;
        }

        var text = realm.ToJsStringSlowPath(keyValue);
        return TryGetArrayIndexFromCanonicalString(text, out index);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeInitPrivateField(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 3)
            ThrowTypeError("PRIVATE_INIT_ARGC", "InitPrivateField requires three arguments");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        var keyValue = Unsafe.Add(ref registers, argRegStart + 1);
        var initialValue = Unsafe.Add(ref registers, argRegStart + 2);

        if (!targetValue.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        var atom = ToPropertyAtomForRuntime(realm, keyValue);
        if (target.TryGetOwnPropertySlotInfoAtom(atom, out _))
            ThrowTypeError("PRIVATE_FIELD_REINIT", "Cannot initialize private field twice on the same object");

        target.DefineDataPropertyAtom(realm, atom, initialValue, JsShapePropertyFlags.Writable);
        acc = JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeGetPrivateField(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 2)
            ThrowTypeError("PRIVATE_GET_ARGC", "GetPrivateField requires two arguments");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        var keyValue = Unsafe.Add(ref registers, argRegStart + 1);

        if (!targetValue.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        var atom = ToPropertyAtomForRuntime(realm, keyValue);
        if (!target.TryGetOwnPropertySlotInfoAtom(atom, out var slotInfo))
            throw TypeErrorInRealm(realm, "PRIVATE_FIELD_BRAND",
                "Cannot read private member from an object whose class did not declare it");
        if ((slotInfo.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0)
            ThrowTypeError("PRIVATE_FIELD_KIND", "Private member is not a data field");

        acc = target.GetNamedSlotUnchecked(slotInfo.Slot);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeSetPrivateField(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 3)
            ThrowTypeError("PRIVATE_SET_ARGC", "SetPrivateField requires three arguments");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        var keyValue = Unsafe.Add(ref registers, argRegStart + 1);
        var value = Unsafe.Add(ref registers, argRegStart + 2);

        if (!targetValue.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        var atom = ToPropertyAtomForRuntime(realm, keyValue);
        if (!target.TryGetOwnPropertySlotInfoAtom(atom, out var slotInfo))
            throw TypeErrorInRealm(realm, "PRIVATE_FIELD_BRAND",
                "Cannot write private member to an object whose class did not declare it");
        if ((slotInfo.Flags & (JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter)) != 0)
            ThrowTypeError("PRIVATE_FIELD_KIND", "Private member is not a data field");
        if ((slotInfo.Flags & JsShapePropertyFlags.Writable) == 0)
            ThrowTypeError("PRIVATE_FIELD_READONLY", "Private field is not writable");

        target.SetNamedSlotUnchecked(slotInfo.Slot, value);
        acc = value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleRuntimeHasPrivateField(
        JsRealm realm, JsScript script, int opcodePc, ref JsValue registers, int fp, int argRegStart,
        int argCount, ref JsValue acc)
    {
        if (argCount != 3)
            ThrowTypeError("PRIVATE_HAS_ARGC", "HasPrivateField requires target, brand id, and slot index");

        var targetValue = Unsafe.Add(ref registers, argRegStart);
        if (!targetValue.TryGetObject(out var target))
            ThrowTypeError("PRIVATE_FIELD_TARGET", "Private field target must be object");

        var brandId = Unsafe.Add(ref registers, argRegStart + 1).Int32Value;
        var slotIndex = Unsafe.Add(ref registers, argRegStart + 2).Int32Value;
        if (brandId < 0 || slotIndex < 0)
            ThrowTypeError("PRIVATE_HAS_INDEX", "private field brand id and slot index must be non-negative");

        ref readonly var callFrame = ref GetCurrentCallFrame(realm.Stack, fp);
        if (callFrame.Function is not JsBytecodeFunction)
            ThrowTypeError("PRIVATE_HAS_CONTEXT", "missing private field context");
        var currentFunc = (JsBytecodeFunction)callFrame.Function!;

        acc = realm.TryGetPrivateSlotValue(target, currentFunc, brandId, slotIndex, out _)
            ? JsValue.True
            : JsValue.False;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsContext? GetCurrentContext()
    {
        return CurrentCallFrame.Context;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateDerivedThisContextValue(int framePointer, JsBytecodeFunction function, JsValue thisValue)
    {
        var slot = function.DerivedThisContextSlot;
        if (slot < 0)
            return;

        ref readonly var frame = ref GetCallFrameAt(framePointer);
        var context = frame.Context;
        if (context is null || (uint)slot >= (uint)context.Slots.Length)
            return;

        context.Slots[slot] = thisValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsValue ResolveSuperBaseValueForFrame(int framePointer)
    {
        ref readonly var callFrame = ref Unsafe.As<JsValue, CallFrame>(ref Stack[framePointer]);
        if (callFrame.Function is not JsBytecodeFunction bytecodeFunction)
            throw TypeError("SUPER_BASE", "super base is not available");

        var superBaseSlot = bytecodeFunction.SuperBaseContextSlot;
        if (superBaseSlot >= 0)
        {
            var context = callFrame.Context;
            if (context is null || (uint)superBaseSlot >= (uint)context.Slots.Length)
                ThrowTypeError("SUPER_BASE", "super base is not available");

            return context.Slots[superBaseSlot];
        }

        if (bytecodeFunction.IsClassConstructor)
        {
            if (bytecodeFunction.Prototype is JsFunction superCtor &&
                superCtor.TryGetPropertyAtom(this, IdPrototype, out var superPrototypeValue, out _) &&
                (superPrototypeValue.TryGetObject(out _) || superPrototypeValue.IsNull))
                return superPrototypeValue;

            if (!bytecodeFunction.TryGetPropertyAtom(this, IdPrototype, out var ctorPrototypeValue, out _))
                ThrowTypeError("SUPER_BASE", "super base is not available");

            if (!ctorPrototypeValue.TryGetObject(out var ctorPrototypeObject))
                ThrowTypeError("SUPER_BASE", "super base is not available");

            return ctorPrototypeObject.Prototype is null
                ? JsValue.Null
                : JsValue.FromObject(ctorPrototypeObject.Prototype);
        }

        ThrowTypeError("SUPER_BASE", "super base is not available");
        return JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsObject RequireObjectSuperBaseForFrame(int framePointer)
    {
        var superBaseValue = ResolveSuperBaseValueForFrame(framePointer);
        if (!superBaseValue.TryGetObject(out var superBaseObject))
            ThrowTypeError("SUPER_BASE", "super base is not available");

        return superBaseObject;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsStrictFunctionFrame(int framePointer)
    {
        ref readonly var callFrame = ref Unsafe.As<JsValue, CallFrame>(ref Stack[framePointer]);
        return callFrame.Function is JsBytecodeFunction { IsStrict: true };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short ReadJumpOffset16(ReadOnlySpan<byte> bytecode, int pc)
    {
        ref readonly var src = ref bytecode[pc];
        return Unsafe.ReadUnaligned<short>(ref Unsafe.AsRef(in src));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool EvaluateJumpCondition(JsOpCode op, in JsValue acc)
    {
        return op switch
        {
            JsOpCode.JumpIfTrue => acc.U == JsValue.JsBoolTrueBits,
            JsOpCode.JumpIfFalse => acc.U == JsValue.JsBoolFalseBits,
            JsOpCode.JumpIfToBooleanTrue or JsOpCode.JumpIfToBooleanFalse => ToBoolean(acc) ^
                                                                             (op == JsOpCode.JumpIfToBooleanFalse),
            JsOpCode.JumpIfNull => acc.U == JsValue.JsNullBits,
            JsOpCode.JumpIfUndefined => acc.U == JsValue.JsUndefinedBits,
            JsOpCode.JumpIfNotUndefined => acc.U != JsValue.JsUndefinedBits,
            JsOpCode.JumpIfJsReceiver => acc.U == JsValue.JsObjectBits,
            _ => false
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue TypeOfValue(in JsValue value)
    {
        if (value.IsUndefined)
            return "undefined";
        if (value.IsNull)
            return "object";
        if (value.IsBool)
            return "boolean";
        if (value.IsNumber)
            return "number";
        if (value.IsString)
            return "string";
        if (value.IsSymbol)
            return "symbol";
        if (value.IsBigInt)
            return "bigint";
        if (value.IsObject)
            return value.Obj is JsFunction ? "function" : "object";
        return "object";
    }

    private static ref readonly CallFrame GetCurrentCallFrame(Span<JsValue> fullStack, int framePointer)
    {
        return ref Unsafe.As<JsValue, CallFrame>(ref fullStack[framePointer]);
    }

    internal IReadOnlyList<PausedLocalValue>? CapturePausedLocalValues(Span<JsValue> fullStack, int framePointer,
        int programCounter)
    {
        return CapturePausedLocalValuesCore(fullStack, framePointer, programCounter, out _);
    }

    internal IReadOnlyList<PausedScopeSnapshot>? CapturePausedScopeChain(Span<JsValue> fullStack, int framePointer,
        int programCounter)
    {
        var chain = new List<PausedScopeSnapshot>(8);
        var fpCursor = framePointer;
        var pcCursor = programCounter;
        for (var depth = 0; depth < 1024; depth++)
        {
            if ((uint)fpCursor >= (uint)fullStack.Length)
                break;

            ref readonly var callFrame = ref GetCurrentCallFrame(fullStack, fpCursor);
            var frameInfo = CreateStackFrameInfo(fullStack, fpCursor, callFrame, pcCursor);
            var localInfos = GetVisibleLocalInfos(callFrame, pcCursor);
            var localValues = localInfos is null ? null : CapturePausedLocalValues(fullStack, fpCursor, pcCursor);
            chain.Add(new(fpCursor, frameInfo, localInfos, localValues));

            var callerFp = callFrame.CallerFp;
            var callerPc = callFrame.CallerPc;
            if (callerFp == fpCursor)
                break;

            fpCursor = callerFp;
            pcCursor = callerPc;

            if (fpCursor == 0 && pcCursor == 0)
                break;
        }

        return chain.Count == 0 ? null : chain;
    }

    private IReadOnlyList<PausedLocalValue>? CapturePausedLocalValuesCore(Span<JsValue> fullStack, int framePointer,
        int programCounter, out StackFrameInfo frameInfo)
    {
        ref readonly var callFrame = ref GetCurrentCallFrame(fullStack, framePointer);
        frameInfo = CreateStackFrameInfo(fullStack, framePointer, callFrame, programCounter);
        var localInfos = GetVisibleLocalInfos(callFrame, programCounter);
        if (callFrame.Function is not JsBytecodeFunction bytecodeFunction || localInfos is null)
            return null;

        var values = new List<PausedLocalValue>(localInfos.Count + 8);
        var seenRegisters = new HashSet<int>();
        for (var i = 0; i < localInfos.Count; i++)
        {
            var local = localInfos[i];
            values.Add(new(
                local.Name,
                local.StorageKind,
                local.StorageIndex,
                ReadPausedLocalValue(fullStack, callFrame, framePointer, local),
                local.StartPc,
                local.EndPc,
                local.Flags));

            if (local.StorageKind == JsLocalDebugStorageKind.Register)
                seenRegisters.Add(local.StorageIndex);
        }

        if (JsScriptDebugInfo.GetInstructionOperandRegisters(bytecodeFunction.Script, programCounter) is
            { Count: > 0 } operandRegisters)
            for (var i = 0; i < operandRegisters.Count; i++)
            {
                var register = operandRegisters[i];
                if (!seenRegisters.Add(register))
                    continue;

                values.Add(new(
                    $"$r{register}",
                    JsLocalDebugStorageKind.Register,
                    register,
                    ReadPausedRegisterValue(fullStack, framePointer, register),
                    programCounter,
                    programCounter + 1,
                    JsLocalDebugFlags.None));
            }

        return values;
    }

    private IReadOnlyList<JsLocalDebugInfo>? GetVisibleLocalInfos(in CallFrame callFrame, int programCounter)
    {
        if (callFrame.Function is not JsBytecodeFunction bytecodeFunction)
            return null;

        return JsScriptDebugInfo.GetVisibleLocalInfos(bytecodeFunction.Script, programCounter);
    }

    private static JsValue ReadPausedLocalValue(Span<JsValue> fullStack, in CallFrame callFrame, int framePointer,
        JsLocalDebugInfo local)
    {
        if (local.StorageKind == JsLocalDebugStorageKind.ContextSlot)
        {
            var context = callFrame.Context;
            if (context is null)
                return JsValue.Undefined;
            if ((uint)local.StorageIndex >= (uint)context.Slots.Length)
                return JsValue.Undefined;
            return context.Slots[local.StorageIndex];
        }

        return ReadPausedRegisterValue(fullStack, framePointer, local.StorageIndex);
    }

    private static JsValue ReadPausedRegisterValue(Span<JsValue> fullStack, int framePointer, int registerIndex)
    {
        var stackIndex = framePointer + HeaderSize + registerIndex;
        if ((uint)stackIndex >= (uint)fullStack.Length)
            return JsValue.Undefined;
        return fullStack[stackIndex];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsContext? GetCurrentContext(Span<JsValue> fullStack)
    {
        return Unsafe.As<JsValue, CallFrame>(ref fullStack[fp]).Context;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsContext? GetCurrentContextForFrame(int framePointer)
    {
        var ctxVal = Stack[framePointer + OffsetCurrentContext];
        return ctxVal.Obj as JsContext;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetFrameContext(Span<JsValue> fullStack, int framePointer, JsContext? context)
    {
        Unsafe.AsRef(in fullStack[framePointer + OffsetCurrentContext].Obj) = context;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsContext GetContextAtDepth(Span<JsValue> fullStack, int depth)
    {
        var ctx = GetCurrentContext(fullStack) ?? throw new InvalidOperationException("No current context.");
        for (var i = 0; i < depth; i++)
            ctx = ctx.Parent ?? throw new InvalidOperationException("Missing parent context for requested depth.");

        return ctx;
    }

    internal JsContext GetContextAtDepth(int depth)
    {
        return GetContextAtDepth(Stack.AsSpan(), depth);
    }

    internal int GetExecutionContextDepth()
    {
        return GetExecutionContextsSnapshot().Count;
    }

    internal IReadOnlyList<JsExecutionContext> GetExecutionContextsSnapshot()
    {
        if (executionPhaseDepth == 0 || StackTop == 0)
            return Array.Empty<JsExecutionContext>();

        var fullStack = Stack.AsSpan();
        var frames = new List<JsExecutionContext>(8);
        var fpCursor = fp;
        for (var guard = 0; guard < 1024; guard++)
        {
            if ((uint)fpCursor >= (uint)fullStack.Length)
                break;

            ref readonly var frame = ref Unsafe.As<JsValue, CallFrame>(ref fullStack[fpCursor]);
            frames.Add(new(
                this,
                frame.FrameKind,
                frame.Function.Name ?? "<anonymous>"));

            var callerFp = frame.CallerFp;
            var callerPc = frame.CallerPc;
            if (callerFp == fpCursor)
                break;
            if (callerFp == 0 && callerPc == 0)
                break;

            fpCursor = callerFp;
        }

        return frames;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BeginExecutionPhase()
    {
        executionPhaseDepth++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EndExecutionPhase()
    {
        if (executionPhaseDepth > 0)
            executionPhaseDepth--;
        if (executionPhaseDepth == 0)
            Agent.ClearKeptObjects();
    }

    internal void ReportFinalizationRegistryCleanupError(JsRuntimeException ex)
    {
        FinalizationRegistryCleanupError?.Invoke(ex.ThrownValue ?? CreateErrorObjectFromException(ex));
    }

    internal readonly struct PreparedConstruct
    {
        internal readonly JsValue ThisValue;
        internal readonly JsValue NewTarget;
        internal readonly CallFrameFlag Flags;

        internal PreparedConstruct(JsValue thisValue, JsValue newTarget, CallFrameFlag flags)
        {
            ThisValue = thisValue;
            NewTarget = newTarget;
            Flags = flags;
        }
    }

    private enum GeneratorDispatchResult : byte
    {
        Continue = 0,
        ReloadFrame = 1,
        ReturnFromRun = 2
    }

    private delegate void RuntimeHandler(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc);

    private delegate void IntrinsicHandler(
        JsRealm realm,
        JsScript script,
        int opcodePc,
        ref JsValue registers,
        int fp,
        int argRegStart,
        int argCount,
        ref JsValue acc);

    private enum YieldDelegateAbruptKind : byte
    {
        Yield = 0,
        ContinueNext = 1,
        ContinueReturn = 2
    }
}
