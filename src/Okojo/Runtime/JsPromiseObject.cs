namespace Okojo.Runtime;

public sealed class JsPromiseObject : JsObject
{
    private List<Reaction>? reactions;

    internal JsPromiseObject(JsRealm realm) : base(realm)
    {
        Prototype = realm.Intrinsics.ObjectPrototype;
    }

    internal PromiseState State { get; private set; } = PromiseState.Pending;

    internal JsValue Result { get; private set; } = JsValue.Undefined;

    public bool IsPending => State == PromiseState.Pending;
    public bool IsFulfilled => State == PromiseState.Fulfilled;
    public bool IsRejected => State == PromiseState.Rejected;
    public JsValue SettledResult => Result;

    internal bool IsHandled { get; set; }

    internal void AddReaction(Reaction reaction)
    {
        (reactions ??= new()).Add(reaction);
    }

    internal List<Reaction>? ConsumeReactions()
    {
        var list = reactions;
        reactions = null;
        return list;
    }

    internal bool TrySettle(PromiseState state, in JsValue result)
    {
        if (State != PromiseState.Pending)
            return false;
        State = state;
        Result = result;
        return true;
    }

    internal sealed class PromiseCapability
    {
        public readonly JsObject Promise;
        public readonly JsFunction? Reject;
        public readonly JsFunction? Resolve;

        public PromiseCapability(JsPromiseObject promise)
        {
            Promise = promise;
            Resolve = null;
            Reject = null;
        }

        public PromiseCapability(JsPromiseObject promise, JsFunction resolve, JsFunction reject)
        {
            Promise = promise;
            Resolve = resolve;
            Reject = reject;
        }

        public PromiseCapability(JsObject promise, JsFunction resolve, JsFunction reject)
        {
            Promise = promise;
            Resolve = resolve;
            Reject = reject;
        }
    }

    internal enum PromiseState : byte
    {
        Pending = 0,
        Fulfilled = 1,
        Rejected = 2
    }

    internal sealed class Reaction
    {
        internal readonly object? Data;

        public ReactionKind Kind;
        public JsValue OnFulfilled;
        public JsValue OnRejected;
        internal JsPromiseObject? SourcePromise;

        public Reaction(JsValue onFulfilled, JsValue onRejected, PromiseCapability capability)
        {
            Kind = ReactionKind.UserHandlers;
            OnFulfilled = onFulfilled;
            OnRejected = onRejected;
            Data = capability;
        }

        public Reaction(JsPromiseObject targetPromise)
        {
            Kind = ReactionKind.AssimilateToPromise;
            OnFulfilled = JsValue.Undefined;
            OnRejected = JsValue.Undefined;
            Data = targetPromise;
        }

        public Reaction(JsGeneratorObject asyncGenerator)
        {
            Kind = ReactionKind.ResumeAsyncDriver;
            OnFulfilled = JsValue.Undefined;
            OnRejected = JsValue.Undefined;
            Data = asyncGenerator;
        }

        public Reaction(JsValue onFulfilled, JsValue onRejected)
        {
            Kind = ReactionKind.InvokeHandlersOnly;
            OnFulfilled = onFulfilled;
            OnRejected = onRejected;
            Data = null;
        }

        private Reaction(ReactionKind kind, object? data)
        {
            Kind = kind;
            OnFulfilled = JsValue.Undefined;
            OnRejected = JsValue.Undefined;
            Data = data;
        }

        public PromiseCapability? Capability => Kind == ReactionKind.UserHandlers ? (PromiseCapability?)Data : null;

        public JsPromiseObject? TargetPromise =>
            Kind == ReactionKind.AssimilateToPromise ? (JsPromiseObject?)Data : null;

        public JsGeneratorObject? AsyncGenerator =>
            Kind == ReactionKind.ResumeAsyncDriver ? (JsGeneratorObject?)Data : null;

        public JsGeneratorObject? AsyncGeneratorReturnTarget =>
            Kind == ReactionKind.ResumeAsyncGeneratorReturn ? (JsGeneratorObject?)Data : null;

        public AsyncGeneratorYieldDelegateAwaitState? AsyncGeneratorYieldDelegateAwaitState =>
            Kind == ReactionKind.ResumeAsyncGeneratorYieldDelegate
                ? (AsyncGeneratorYieldDelegateAwaitState?)Data
                : null;

        public Intrinsics.AsyncFromSyncIteratorResolution? AsyncFromSyncIteratorResolution =>
            Kind == ReactionKind.CompleteAsyncFromSyncIteratorResult
                ? (Intrinsics.AsyncFromSyncIteratorResolution?)Data
                : null;

        public AsyncGeneratorYieldValueResolution? AsyncGeneratorYieldValueResolution =>
            Kind == ReactionKind.AwaitAsyncGeneratorYieldValue ? (AsyncGeneratorYieldValueResolution?)Data : null;

        public static Reaction CreateAsyncGeneratorReturn(JsGeneratorObject generator)
        {
            return new(ReactionKind.ResumeAsyncGeneratorReturn, generator);
        }

        public static Reaction CreateAsyncGeneratorYieldDelegate(
            AsyncGeneratorYieldDelegateAwaitState state)
        {
            return new(ReactionKind.ResumeAsyncGeneratorYieldDelegate, state);
        }

        public static Reaction CreateAsyncFromSyncIteratorResult(Intrinsics.AsyncFromSyncIteratorResolution resolution)
        {
            return new(ReactionKind.CompleteAsyncFromSyncIteratorResult, resolution);
        }

        public static Reaction CreateAsyncGeneratorYieldValue(AsyncGeneratorYieldValueResolution resolution)
        {
            return new(ReactionKind.AwaitAsyncGeneratorYieldValue, resolution);
        }

        internal enum ReactionKind : byte
        {
            UserHandlers = 0,
            AssimilateToPromise = 1,
            ResumeAsyncDriver = 2,
            InvokeHandlersOnly = 3,
            ResumeAsyncGeneratorReturn = 4,
            ResumeAsyncGeneratorYieldDelegate = 5,
            CompleteAsyncFromSyncIteratorResult = 6,
            AwaitAsyncGeneratorYieldValue = 7
        }
    }
}
