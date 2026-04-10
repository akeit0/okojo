using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

internal sealed class GeneratorObjectCore
{
    internal AsyncGeneratorRequest? ActiveAsyncRequest;
    public JsObject? ActiveDelegateIteratorObject;
    public JsPromiseObject? AsyncCompletionPromiseObject;
    internal Queue<AsyncGeneratorRequest>? AsyncRequestQueue;

    internal GeneratorFlag Flags;
    internal bool HasPendingAsyncYieldDelegateAwait;
    public JsValue[]? OverflowStartArguments;
    public GeneratorResumeMode PendingResumeMode;
    public JsValue PendingResumeValue = JsValue.Undefined;
    public JsValue[] RegisterSnapshotBuffer = Array.Empty<JsValue>();
    public JsContext? ResumeContext;
    internal SuspendedExceptionHandler[]? ResumeExceptionHandlers;
    public int ResumeFirstRegister;
    public int ResumePc;
    public int ResumeRegisterCount;
    public JsValue ResumeThisValue = JsValue.Undefined;
    public GeneratorState State;
    public int SuspendId = -1;

    public bool HasContinuation
    {
        get => (Flags & GeneratorFlag.HasContinuation) != 0;
        set
        {
            if (value)
                Flags |= GeneratorFlag.HasContinuation;
            else
                Flags &= ~GeneratorFlag.HasContinuation;
        }
    }

    public bool HasActiveDelegateIterator => (Flags & GeneratorFlag.HasActiveDelegateIterator) != 0;

    public JsObject? ActiveDelegateIterator
    {
        get => HasActiveDelegateIterator ? ActiveDelegateIteratorObject : null;
        set
        {
            if (value != null)
            {
                ActiveDelegateIteratorObject = value;
                Flags |= GeneratorFlag.HasActiveDelegateIterator;
            }
            else
            {
                ActiveDelegateIteratorObject = null;
                Flags &= ~GeneratorFlag.HasActiveDelegateIterator;
            }
        }
    }

    public bool IsAsyncDriver => (Flags & GeneratorFlag.IsAsyncDriver) != 0;

    public JsPromiseObject? AsyncCompletionPromise
    {
        get => IsAsyncDriver ? AsyncCompletionPromiseObject : null;
        set
        {
            if (value != null)
            {
                AsyncCompletionPromiseObject = value;
                Flags |= GeneratorFlag.IsAsyncDriver;
            }
            else
            {
                AsyncCompletionPromiseObject = null;
                Flags &= ~GeneratorFlag.IsAsyncDriver;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CaptureStartArguments(ReadOnlySpan<JsValue> startArguments, int registerCount)
    {
        var copiedCount = Math.Min(startArguments.Length, registerCount);
        if (copiedCount != 0)
            startArguments[..copiedCount].CopyTo(RegisterSnapshotBuffer);

        OverflowStartArguments = startArguments.Length > registerCount
            ? startArguments[registerCount..].ToArray()
            : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RestoreOverflowStartArguments(Span<JsValue> frameSlots, int registerCount, int startArgumentCount)
    {
        if (OverflowStartArguments is null || startArgumentCount <= registerCount)
            return;

        OverflowStartArguments.CopyTo(frameSlots[registerCount..]);
    }

    public void ResetForReuse()
    {
        State = GeneratorState.SuspendedStart;
    }

    public void SetCompletedDetached()
    {
        Flags = GeneratorFlag.None;
        State = GeneratorState.Completed;
        PendingResumeMode = GeneratorResumeMode.Next;
        PendingResumeValue = JsValue.Undefined;
        ResumePc = 0;
        ResumeContext = null;
        ResumeThisValue = JsValue.Undefined;
        OverflowStartArguments = null;
        ResumeFirstRegister = 0;
        ResumeRegisterCount = 0;
        SuspendId = -1;
        ActiveDelegateIteratorObject = null;
        AsyncCompletionPromiseObject = null;
        ResumeExceptionHandlers = null;
        AsyncRequestQueue = null;
        ActiveAsyncRequest = null;
        HasPendingAsyncYieldDelegateAwait = false;
    }

    internal readonly struct SuspendedExceptionHandler(int catchPc, int savedSpOffset)
    {
        public readonly int CatchPc = catchPc;
        public readonly int SavedSpOffset = savedSpOffset;
    }

    internal sealed class AsyncGeneratorRequest
    {
        public bool CompletedAfterAwait;
        public GeneratorResumeMode Mode;
        public JsPromiseObject Promise = null!;
        public bool ReturnValueAwaited;
        public JsValue Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            Mode = GeneratorResumeMode.Next;
            Value = JsValue.Undefined;
            Promise = null!;
            ReturnValueAwaited = false;
            CompletedAfterAwait = false;
        }
    }
}
