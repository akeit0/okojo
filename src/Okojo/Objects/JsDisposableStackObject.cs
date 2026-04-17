namespace Okojo.Objects;

internal enum DisposableStackState : byte
{
    Pending = 0,
    Disposed = 1
}

internal enum DisposableResourceHint : byte
{
    SyncDispose = 0,
    AsyncDispose = 1
}

internal readonly record struct DisposableResource(
    JsValue Value,
    DisposableResourceHint Hint,
    JsValue DisposeMethod);

internal sealed class JsDisposableStackObject : JsObject
{
    private List<DisposableResource> resources = [];

    internal JsDisposableStackObject(JsRealm realm, bool isAsync, JsObject prototype) : base(realm)
    {
        IsAsync = isAsync;
        Prototype = prototype;
    }

    internal bool IsAsync { get; }

    internal DisposableStackState State { get; set; }

    internal bool IsDisposed => State == DisposableStackState.Disposed;

    internal void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new JsRuntimeException(JsErrorKind.ReferenceError,
                IsAsync
                    ? "AsyncDisposableStack is already disposed"
                    : "DisposableStack is already disposed");
        }
    }

    internal void AddResource(in DisposableResource resource)
    {
        resources.Add(resource);
    }

    internal List<DisposableResource> DetachResources()
    {
        var detached = resources;
        resources = [];
        return detached;
    }

    internal void ReplaceResources(List<DisposableResource> movedResources)
    {
        resources = movedResources;
    }
}
