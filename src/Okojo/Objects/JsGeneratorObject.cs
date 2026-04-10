namespace Okojo.Objects;

public enum GeneratorState : byte
{
    SuspendedStart = 0,
    SuspendedYield = 1,
    Executing = 2,
    Completed = 3
}

public enum GeneratorResumeMode : byte
{
    Next = 0,
    Return = 1,
    Throw = 2
}

[Flags]
public enum GeneratorFlag : byte
{
    None = 0,
    HasContinuation = 1 << 0,
    FastForOfStepMode = 1 << 1,
    FastForOfStepDone = 1 << 2,
    HasActiveDelegateIterator = 1 << 3,
    IsAsyncDriver = 1 << 4,
    IsAsyncGenerator = 1 << 5,
    AsyncGeneratorRequestActive = 1 << 6,
    LastSuspendWasAwait = 1 << 7
}

public sealed class JsGeneratorObject : JsObject
{
    internal JsGeneratorObject(
        JsRealm realm,
        JsBytecodeFunction function,
        JsValue thisValue,
        ReadOnlySpan<JsValue> startArguments,
        GeneratorObjectCore core)
        : base(realm)
    {
        Function = function;
        ThisValue = thisValue;
        StartArgumentCount = (ushort)startArguments.Length;
        Core = core;
        core.CaptureStartArguments(startArguments, function.Script.RegisterCount);
    }

    public JsBytecodeFunction Function { get; }
    public JsValue ThisValue { get; }
    public ushort StartArgumentCount { get; }
    internal GeneratorObjectCore Core { get; set; }

    public GeneratorState State
    {
        get => Core.State;
        set => Core.State = value;
    }

    public GeneratorResumeMode PendingResumeMode
    {
        get => Core.PendingResumeMode;
        set => Core.PendingResumeMode = value;
    }

    public JsValue PendingResumeValue
    {
        get => Core.PendingResumeValue;
        set => Core.PendingResumeValue = value;
    }

    public JsValue[] RegisterSnapshotBuffer
    {
        get => Core.RegisterSnapshotBuffer;
        set => Core.RegisterSnapshotBuffer = value;
    }

    public int ResumePc
    {
        get => Core.ResumePc;
        set => Core.ResumePc = value;
    }

    public JsContext? ResumeContext
    {
        get => Core.ResumeContext;
        set => Core.ResumeContext = value;
    }

    public JsValue ResumeThisValue
    {
        get => Core.ResumeThisValue;
        set => Core.ResumeThisValue = value;
    }

    public int ResumeFirstRegister
    {
        get => Core.ResumeFirstRegister;
        set => Core.ResumeFirstRegister = value;
    }

    public int ResumeRegisterCount
    {
        get => Core.ResumeRegisterCount;
        set => Core.ResumeRegisterCount = value;
    }

    public int SuspendId
    {
        get => Core.SuspendId;
        set => Core.SuspendId = value;
    }

    public bool HasContinuation
    {
        get => (Core.Flags & GeneratorFlag.HasContinuation) != 0;
        set
        {
            if (value)
                Core.Flags |= GeneratorFlag.HasContinuation;
            else
                Core.Flags &= ~GeneratorFlag.HasContinuation;
        }
    }

    public bool FastForOfStepMode
    {
        get => (Core.Flags & GeneratorFlag.FastForOfStepMode) != 0;
        set
        {
            if (value)
                Core.Flags |= GeneratorFlag.FastForOfStepMode;
            else
                Core.Flags &= ~GeneratorFlag.FastForOfStepMode;
        }
    }

    public bool FastForOfStepDone
    {
        get => (Core.Flags & GeneratorFlag.FastForOfStepDone) != 0;
        set
        {
            if (value)
                Core.Flags |= GeneratorFlag.FastForOfStepDone;
            else
                Core.Flags &= ~GeneratorFlag.FastForOfStepDone;
        }
    }

    public bool HasActiveDelegateIterator => (Core.Flags & GeneratorFlag.HasActiveDelegateIterator) != 0;

    public JsObject? ActiveDelegateIterator
    {
        get => Core.ActiveDelegateIterator;
        set => Core.ActiveDelegateIterator = value;
    }

    public bool IsAsyncDriver => (Core.Flags & GeneratorFlag.IsAsyncDriver) != 0;

    public bool IsAsyncGenerator
    {
        get => (Core.Flags & GeneratorFlag.IsAsyncGenerator) != 0;
        set
        {
            if (value)
                Core.Flags |= GeneratorFlag.IsAsyncGenerator;
            else
                Core.Flags &= ~GeneratorFlag.IsAsyncGenerator;
        }
    }

    public bool AsyncGeneratorRequestActive
    {
        get => (Core.Flags & GeneratorFlag.AsyncGeneratorRequestActive) != 0;
        set
        {
            if (value)
                Core.Flags |= GeneratorFlag.AsyncGeneratorRequestActive;
            else
                Core.Flags &= ~GeneratorFlag.AsyncGeneratorRequestActive;
        }
    }

    public bool LastSuspendWasAwait
    {
        get => (Core.Flags & GeneratorFlag.LastSuspendWasAwait) != 0;
        set
        {
            if (value)
                Core.Flags |= GeneratorFlag.LastSuspendWasAwait;
            else
                Core.Flags &= ~GeneratorFlag.LastSuspendWasAwait;
        }
    }

    public JsPromiseObject? AsyncCompletionPromise
    {
        get => Core.AsyncCompletionPromise;
        set => Core.AsyncCompletionPromise = value;
    }

    internal GeneratorObjectCore.SuspendedExceptionHandler[]? ResumeExceptionHandlers
    {
        get => Core.ResumeExceptionHandlers;
        set => Core.ResumeExceptionHandlers = value;
    }
}
