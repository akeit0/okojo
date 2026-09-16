namespace Okojo.JavaScript.Execution;

public sealed partial class JsRealm
{
    /// <summary>
    /// Creates an intrinsic pending Promise for a host API, independent of global Promise,
    /// Promise.prototype.then and species. The capability is an owner-sequence host handle;
    /// only its Promise value is exposed to script.
    /// </summary>
    public JsPromiseCapability CreatePromiseCapability() => new(this);

    /// <summary>
    /// Attaches host reactions to an existing native Promise, without reading then/species or
    /// creating a derived Promise. Reactions run as Promise jobs on the owning agent's sequence.
    /// Host callbacks must not throw; they own settlement of any dependent operation.
    /// </summary>
    public void ObservePromise(
        in JsValue promise,
        Action<JsValue> onFulfilled,
        Action<JsValue> onRejected
    )
    {
        EnsureCompatibleValue(promise, nameof(promise));
        ArgumentNullException.ThrowIfNull(onFulfilled);
        ArgumentNullException.ThrowIfNull(onRejected);
        if (!promise.TryGetObject(out var value) || value is not JsPromiseObject nativePromise)
            throw new ArgumentException(
                "Expected a native Promise. Convert callback results with CreateResolvedPromise first.",
                nameof(promise)
            );
        Intrinsics.PromiseThenNoCapability(
            nativePromise,
            new JsHostFunction(
                this,
                (in CallInfo info) =>
                {
                    onFulfilled(info.GetArgument(0));
                    return JsValue.Undefined;
                },
                string.Empty,
                1
            ),
            new JsHostFunction(
                this,
                (in CallInfo info) =>
                {
                    onRejected(info.GetArgument(0));
                    return JsValue.Undefined;
                },
                string.Empty,
                1
            )
        );
    }
}
