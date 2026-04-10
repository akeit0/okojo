namespace Okojo.Objects;

internal sealed class JsWeakRefObject : JsObject
{
    private WeakReference<JsObject>? objectTarget;
    private Symbol? symbolTarget;

    internal JsWeakRefObject(JsRealm realm, JsObject target, JsObject prototype) : base(realm)
    {
        Prototype = prototype;
        objectTarget = new(target);
        realm.Agent.AddToKeptObjects(target);
        realm.Agent.TrackWeakRef(this);
    }

    internal JsWeakRefObject(JsRealm realm, Symbol target, JsObject prototype) : base(realm)
    {
        Prototype = prototype;
        symbolTarget = target;
        realm.Agent.AddToKeptObjects(target);
        realm.Agent.TrackWeakRef(this);
    }

    internal bool MatchesTarget(in JsValue target)
    {
        if (target.TryGetObject(out var obj))
            return objectTarget is not null && objectTarget.TryGetTarget(out var current) &&
                   ReferenceEquals(current, obj);
        return target.IsSymbol && symbolTarget is not null && ReferenceEquals(symbolTarget, target.AsSymbol());
    }

    internal void ClearTarget()
    {
        objectTarget = null;
        symbolTarget = null;
    }

    internal JsValue Deref()
    {
        if (symbolTarget is not null)
        {
            Realm.Agent.AddToKeptObjects(symbolTarget);
            return JsValue.FromSymbol(symbolTarget);
        }

        if (objectTarget is not null && objectTarget.TryGetTarget(out var obj))
        {
            Realm.Agent.AddToKeptObjects(obj);
            return obj;
        }

        return JsValue.Undefined;
    }
}
