namespace Okojo.JavaScript.Execution;

/// <summary>
/// An intrinsic Promise owned by a host operation. Use only on the owning agent's sequence.
/// Resolution adopts thenables and is locked by the first resolve or reject call, even while
/// adoption is pending. Reactions run as Promise jobs, without consulting mutable then/species.
/// </summary>
public sealed class JsPromiseCapability
{
    private readonly JsRealm _realm;
    private readonly JsPromiseObject _promise;
    private bool _resolved;

    internal JsPromiseCapability(JsRealm realm)
    {
        _realm = realm;
        _promise = realm.Intrinsics.CreatePromiseObject();
    }

    public JsValue Promise => JsValue.FromObject(_promise);

    public void Resolve(in JsValue value)
    {
        _realm.EnsureCompatibleValue(value, nameof(value));
        if (_resolved)
            return;
        _resolved = true;
        _realm.Intrinsics.ResolvePromiseWithAssimilation(_promise, value);
    }

    public void Reject(in JsValue reason)
    {
        _realm.EnsureCompatibleValue(reason, nameof(reason));
        if (_resolved)
            return;
        _resolved = true;
        _realm.Intrinsics.RejectPromise(_promise, reason);
    }

    /// <summary>Suppresses the engine's unhandled-rejection report for this promise only.</summary>
    public void MarkHandled() => _promise.IsHandled = true;

    /// <summary>
    /// Registers host reactions without creating a derived Promise. Host callbacks must not
    /// throw; they are responsible for settling their own operation's result.
    /// </summary>
    public void Observe(Action<JsValue> onFulfilled, Action<JsValue> onRejected)
    {
        ArgumentNullException.ThrowIfNull(onFulfilled);
        ArgumentNullException.ThrowIfNull(onRejected);
        _realm.Intrinsics.PromiseThenNoCapability(
            _promise,
            new JsHostFunction(
                _realm,
                (in CallInfo info) =>
                {
                    onFulfilled(info.GetArgument(0));
                    return JsValue.Undefined;
                },
                string.Empty,
                1
            ),
            new JsHostFunction(
                _realm,
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
