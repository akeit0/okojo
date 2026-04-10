using System.Runtime.CompilerServices;
using Okojo.Bytecode;

namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    internal JsValue ResumeGeneratorObject(JsGeneratorObject generator, GeneratorResumeMode mode, JsValue input)
    {
        if (generator.State == GeneratorState.Executing)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Generator is already executing");

        if (generator.State == GeneratorState.Completed)
        {
            if (mode == GeneratorResumeMode.Throw)
                ThrowGeneratorThrownValue(input);
            return CreateIteratorResultObject(
                mode == GeneratorResumeMode.Return ? input : JsValue.Undefined,
                true);
        }

        if (generator.State == GeneratorState.SuspendedStart)
        {
            if (generator.HasContinuation)
            {
                if (mode == GeneratorResumeMode.Return)
                {
                    FinalizeGenerator(generator);
                    return CreateIteratorResultObject(input, true);
                }

                if (mode == GeneratorResumeMode.Throw)
                {
                    FinalizeGenerator(generator);
                    ThrowGeneratorThrownValue(input);
                }

                generator.PendingResumeMode = GeneratorResumeMode.Next;
                generator.PendingResumeValue = JsValue.Undefined;
                return ExecuteGeneratorFromContinuation(generator);
            }

            if (mode == GeneratorResumeMode.Return)
            {
                FinalizeGenerator(generator);
                return CreateIteratorResultObject(input, true);
            }

            if (mode == GeneratorResumeMode.Throw)
            {
                FinalizeGenerator(generator);
                ThrowGeneratorThrownValue(input);
            }

            generator.PendingResumeMode = GeneratorResumeMode.Next;
            generator.PendingResumeValue = JsValue.Undefined; // first next(value) argument is ignored by spec
            return ExecuteGeneratorFromStart(generator);
        }

        if (mode == GeneratorResumeMode.Return)
        {
            generator.PendingResumeMode = GeneratorResumeMode.Return;
            generator.PendingResumeValue = input;
            return ExecuteGeneratorFromContinuation(generator);
        }

        generator.PendingResumeMode = mode;
        generator.PendingResumeValue = input;
        return ExecuteGeneratorFromContinuation(generator);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal JsValue ResumeGeneratorForOfNextStep(JsGeneratorObject generator, out bool done)
    {
        if (generator.State == GeneratorState.Executing)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Generator is already executing");

        if (generator.State == GeneratorState.Completed)
        {
            done = true;
            return JsValue.Undefined;
        }

        generator.FastForOfStepMode = true;
        try
        {
            if (generator.State == GeneratorState.SuspendedStart)
            {
                generator.PendingResumeMode = GeneratorResumeMode.Next;
                generator.PendingResumeValue = JsValue.Undefined; // first next(value) arg is ignored by spec
                var value = ExecuteGeneratorFromStart(generator);
                done = generator.FastForOfStepDone;
                return value;
            }

            generator.PendingResumeMode = GeneratorResumeMode.Next;
            generator.PendingResumeValue = JsValue.Undefined;
            var resumedValue = ExecuteGeneratorFromContinuation(generator);
            done = generator.FastForOfStepDone;
            return resumedValue;
        }
        finally
        {
            generator.FastForOfStepMode = false;
            generator.FastForOfStepDone = false;
        }
    }

    internal JsValue ExecuteGeneratorFromStart(JsGeneratorObject generator)
    {
        ThrowIfManagedRunDepthExceeded();

        var callerFp = fp;
        var newFp = StackTop;
        var fullStack = Stack.AsSpan();
        var registerCount = generator.Function.Script.RegisterCount;
        if (newFp + HeaderSize + registerCount > fullStack.Length)
            throw new StackOverflowException();

        var copyArgCount = Math.Min(generator.StartArgumentCount, registerCount);
        for (var i = 0; i < copyArgCount; i++)
            fullStack[newFp + HeaderSize + i] = generator.RegisterSnapshotBuffer[i];
        generator.Core.RestoreOverflowStartArguments(fullStack[(newFp + HeaderSize)..], registerCount,
            generator.StartArgumentCount);

        generator.State = GeneratorState.Executing;
        PushFrame(generator.Function, callerFp, 0, generator.StartArgumentCount, generator.Function.BoundParentContext,
            generator.ThisValue, JsValue.Undefined, CallFrameKind.GeneratorFrame);
        var frameFp = fp;
        SetActiveGeneratorForFrame(frameFp, generator);
        if ((Agent.ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.ResumeGenerator) != 0)
            Agent.ExecutionCheckPolicy.EmitBoundaryCheckpoint(this, fullStack, frameFp,
                ExecutionCheckpointKind.ResumeGenerator, 0);

        try
        {
            Run(callerFp);
            return acc;
        }
        catch
        {
            ClearActiveGeneratorForFrame(frameFp);
            FinalizeGenerator(generator);
            RestoreInvokeCallerStateOnThrow(newFp, callerFp);
            throw;
        }
    }

    internal JsValue ExecuteGeneratorFromContinuation(JsGeneratorObject generator)
    {
        if (!generator.HasContinuation)
            throw new InvalidOperationException("Generator continuation is missing.");
        ThrowIfManagedRunDepthExceeded();
        var callerFp = fp;
        var newFp = StackTop;
        var fullStack = Stack.AsSpan();
        var registerCount = generator.Function.Script.RegisterCount;
        if (newFp + HeaderSize + registerCount > fullStack.Length)
            throw new StackOverflowException();

        PushFrame(generator.Function, callerFp, 0, generator.StartArgumentCount, generator.ResumeContext,
            generator.ResumeThisValue,
            JsValue.Undefined, CallFrameKind.GeneratorFrame);
        RestoreExceptionHandlersForFrame(fp, generator.ResumeExceptionHandlers);
        var registers = fullStack.Slice(fp + HeaderSize);
        generator.Core.RestoreOverflowStartArguments(fullStack[(fp + HeaderSize)..], registerCount,
            generator.StartArgumentCount);
        var restoreFirstReg = generator.ResumeFirstRegister;
        var restoreRegCount = generator.ResumeRegisterCount;
        if (TryGetResumeOperandRange(generator.Function.Script, generator.ResumePc, out var opFirstReg,
                out var opRegCount))
        {
            restoreFirstReg = opFirstReg;
            restoreRegCount = opRegCount;
        }

        var copyCount = Math.Min(registerCount - restoreFirstReg, restoreRegCount);
        for (var i = 0; i < copyCount; i++)
            registers[restoreFirstReg + i] = generator.RegisterSnapshotBuffer![i];

        var frameFp = fp;
        SetActiveGeneratorForFrame(frameFp, generator);
        generator.State = GeneratorState.Executing;
        var startPc = StartsWithSwitchOnGeneratorState(generator.Function.Script) ? 0 : generator.ResumePc;
        if ((Agent.ExecutionCheckpointHookBits & (int)ExecutionCheckpointHooks.ResumeGenerator) != 0)
            Agent.ExecutionCheckPolicy.EmitBoundaryCheckpoint(this, fullStack, frameFp,
                ExecutionCheckpointKind.ResumeGenerator, startPc);

        try
        {
            Run(callerFp, startPc);
            return acc;
        }
        catch
        {
            ClearActiveGeneratorForFrame(frameFp);
            FinalizeGenerator(generator);
            RestoreInvokeCallerStateOnThrow(newFp, callerFp);
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void StartOrResumeAsyncDriver(JsGeneratorObject generator, GeneratorResumeMode mode, JsValue value)
    {
        var completionPromise = generator.AsyncCompletionPromise;
        if (completionPromise is null || completionPromise.State != JsPromiseObject.PromiseState.Pending)
            return;

        try
        {
            var stepResult = RunAsyncDriverStep(generator, mode, value);

            if (generator.State == GeneratorState.Completed)
            {
                this.ResolvePromiseWithAssimilation(completionPromise, stepResult);
                return;
            }

            AttachAsyncAwaitContinuation(generator, stepResult);
        }
        catch (JsRuntimeException ex)
        {
            RejectAsyncDriverException(completionPromise, ex);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private JsValue RunAsyncDriverStep(JsGeneratorObject generator, GeneratorResumeMode mode, JsValue value)
    {
        if (generator.State == GeneratorState.SuspendedStart)
        {
            // Async function initial entry ignores external value, like generator first next().
            generator.PendingResumeMode = GeneratorResumeMode.Next;
            generator.PendingResumeValue = JsValue.Undefined;
            return ExecuteGeneratorFromStart(generator);
        }

        generator.PendingResumeMode = mode;
        generator.PendingResumeValue = value;
        return ExecuteGeneratorFromContinuation(generator);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void AttachAsyncAwaitContinuation(JsGeneratorObject generator, JsValue stepResult)
    {
        var awaitedPromiseValue = this.PromiseResolveByConstructor(PromiseConstructor, stepResult);
        if (!awaitedPromiseValue.TryGetObject(out var awaitedPromiseObj) ||
            awaitedPromiseObj is not JsPromiseObject awaitedPromise)
            throw new JsRuntimeException(JsErrorKind.InternalError,
                "PromiseResolve(%Promise%, awaitValue) must produce a promise object");
        Intrinsics.PromiseThenResumeAsync(awaitedPromise, generator);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetResumeOperandRange(JsScript script, int resumePc, out int firstReg, out int regCount)
    {
        firstReg = 0;
        regCount = 0;
        var code = script.Bytecode;
        if ((uint)resumePc >= (uint)code.Length)
            return false;
        if ((JsOpCode)code[resumePc] != JsOpCode.ResumeGenerator)
            return false;
        if ((uint)(resumePc + 3) >= (uint)code.Length)
            return false;

        firstReg = code[resumePc + 2];
        regCount = code[resumePc + 3];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetResumeGeneratorOperand(JsScript script, int resumePc, out int generatorReg)
    {
        generatorReg = 0xFF;
        var code = script.Bytecode;
        if ((uint)resumePc >= (uint)code.Length)
            return false;
        if ((JsOpCode)code[resumePc] != JsOpCode.ResumeGenerator)
            return false;
        if ((uint)(resumePc + 1) >= (uint)code.Length)
            return false;

        generatorReg = code[resumePc + 1];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ClearDelegateIteratorRegisterInContinuationSnapshot(JsGeneratorObject generator)
    {
        if (!TryGetResumeGeneratorOperand(generator.Function.Script, generator.ResumePc, out var generatorReg))
            return;
        if (generatorReg is 0xFF or 0xFE or 0xFD)
            return;

        var restoreFirstReg = generator.ResumeFirstRegister;
        var restoreRegCount = generator.ResumeRegisterCount;
        if (TryGetResumeOperandRange(generator.Function.Script, generator.ResumePc, out var opFirstReg,
                out var opRegCount))
        {
            restoreFirstReg = opFirstReg;
            restoreRegCount = opRegCount;
        }

        if ((uint)generatorReg < (uint)restoreFirstReg)
            return;

        var snapshotIndex = generatorReg - restoreFirstReg;
        if ((uint)snapshotIndex >= (uint)restoreRegCount)
            return;
        if ((uint)snapshotIndex >= (uint)generator.RegisterSnapshotBuffer.Length)
            return;

        generator.RegisterSnapshotBuffer[snapshotIndex] = JsValue.Undefined;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool StartsWithSwitchOnGeneratorState(JsScript script)
    {
        var code = script.Bytecode;
        return code.Length != 0 && (JsOpCode)code[0] == JsOpCode.SwitchOnGeneratorState;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsGeneratorObject CreateGeneratorObject(JsBytecodeFunction function, JsValue thisValue,
        ReadOnlySpan<JsValue> args)
    {
        var core = RentGeneratorCore(function.Script.RegisterCount);
        var generator = new JsGeneratorObject(this, function, thisValue, args, core)
        {
            Prototype = ResolveGeneratorPrototype(function),
            IsAsyncGenerator = function.Kind == JsBytecodeFunctionKind.AsyncGenerator
        };
        return generator;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private JsObject ResolveGeneratorPrototype(JsBytecodeFunction function)
    {
        var intrinsic = function.Kind == JsBytecodeFunctionKind.AsyncGenerator
            ? (JsObject)AsyncGeneratorObjectPrototype
            : GeneratorObjectPrototypeForFunctions;

        if (!function.TryGetPropertyAtom(this, IdPrototype, out var value, out _) ||
            !value.TryGetObject(out var obj))
            return intrinsic;

        return obj;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void RefreshGeneratorPrototypeAfterParameterBinding(JsGeneratorObject generator)
    {
        generator.Prototype = ResolveGeneratorPrototype(generator.Function);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsPlainObject CreateIteratorResultObject(JsValue value, bool done)
    {
        var result = new JsPlainObject(IteratorResultObjectShape)
        {
            Prototype = ObjectPrototype
        };
        result.SetNamedSlotUnchecked(IteratorResultValueSlot, value);
        result.SetNamedSlotUnchecked(IteratorResultDoneSlot, done ? JsValue.True : JsValue.False);
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowGeneratorThrownValue(in JsValue value)
    {
        ThrowJsValue(value);
    }
}
