namespace Okojo.Objects;

internal sealed class JsWrappedIteratorObject : JsObject
{
    private readonly JsObject iterator;
    private readonly JsValue nextMethod;
    private bool closed;

    internal JsWrappedIteratorObject(JsRealm realm, JsObject iterator, JsValue nextMethod) : base(realm)
    {
        this.iterator = iterator;
        this.nextMethod = nextMethod;
        Prototype = realm.IteratorWrapPrototype;

        var nextFn = new JsHostFunction(realm, static (in info) =>
        {
            var innerRealm = info.Realm;
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsWrappedIteratorObject wrapped)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Wrapped Iterator next called on incompatible receiver");
            return wrapped.Next();
        }, "next", 0);

        var returnFn = new JsHostFunction(realm, static (in info) =>
        {
            var innerRealm = info.Realm;
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsWrappedIteratorObject wrapped)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Wrapped Iterator return called on incompatible receiver");
            return wrapped.Return();
        }, "return", 0);

        DefineDataPropertyAtom(realm, IdNext, JsValue.FromObject(nextFn), JsShapePropertyFlags.Open);
        DefineDataPropertyAtom(realm, IdReturn, JsValue.FromObject(returnFn), JsShapePropertyFlags.Open);
    }

    internal JsValue Next()
    {
        if (closed)
            return CreateDoneResult();

        if (!nextMethod.TryGetObject(out var nextObj) || nextObj is not JsFunction nextFn)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator next must be callable");

        var result = Realm.InvokeFunction(nextFn, JsValue.FromObject(iterator), ReadOnlySpan<JsValue>.Empty);
        if (!result.TryGetObject(out _))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result must be an object");
        return result;
    }

    internal JsValue Return()
    {
        if (closed)
            return CreateDoneResult();

        closed = true;
        if (iterator.TryGetPropertyAtom(Realm, IdReturn, out var returnValue, out _) &&
            !returnValue.IsUndefined && !returnValue.IsNull)
        {
            if (!returnValue.TryGetObject(out var returnObj) || returnObj is not JsFunction returnFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator return must be callable");
            return Realm.InvokeFunction(returnFn, JsValue.FromObject(iterator), ReadOnlySpan<JsValue>.Empty);
        }

        return CreateDoneResult();
    }

    private JsValue CreateDoneResult()
    {
        var result = new JsPlainObject(Realm);
        result.DefineDataPropertyAtom(Realm, IdValue, JsValue.Undefined, JsShapePropertyFlags.Open);
        result.DefineDataPropertyAtom(Realm, IdDone, JsValue.True, JsShapePropertyFlags.Open);
        return JsValue.FromObject(result);
    }
}
