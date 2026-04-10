namespace Okojo.Objects;

internal sealed class JsIteratorTakeObject : JsObject
{
    private readonly JsObject iterator;
    private readonly JsValue nextMethod;
    private bool closed;
    private bool executing;
    private long remaining;

    internal JsIteratorTakeObject(JsRealm realm, JsObject iterator, JsValue nextMethod, long remaining) : base(realm)
    {
        this.iterator = iterator;
        this.nextMethod = nextMethod;
        this.remaining = remaining;
        Prototype = realm.IteratorPrototype;

        var nextFn = new JsHostFunction(realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsIteratorTakeObject take)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.take result next called on incompatible receiver");

            return take.Next();
        }, "next", 0);

        var returnFn = new JsHostFunction(realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsIteratorTakeObject take)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.take result return called on incompatible receiver");

            return take.Return();
        }, "return", 0);

        DefineDataPropertyAtom(realm, IdNext, JsValue.FromObject(nextFn), JsShapePropertyFlags.Open);
        DefineDataPropertyAtom(realm, IdReturn, JsValue.FromObject(returnFn), JsShapePropertyFlags.Open);
    }

    internal JsValue Next()
    {
        if (executing)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator helper is already executing");
        if (closed)
            return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm, JsValue.Undefined,
                true));

        executing = true;
        try
        {
            if (remaining == 0)
            {
                closed = true;
                CloseUnderlying();
                return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm,
                    JsValue.Undefined,
                    true));
            }

            if (remaining != long.MaxValue)
                remaining--;

            var step = Intrinsics.CallIteratorHelperMethod(Realm, nextMethod, iterator,
                "Iterator next must be callable");
            if (!step.TryGetObject(out var stepObj))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result must be an object");

            stepObj.TryGetPropertyAtom(Realm, IdDone, out var doneValue, out _);
            if (JsIteratorHelperOperations.ToBoolean(doneValue))
            {
                closed = true;
                return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm,
                    JsValue.Undefined,
                    true));
            }

            stepObj.TryGetPropertyAtom(Realm, IdValue, out var value, out _);
            return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm, value, false));
        }
        finally
        {
            executing = false;
        }
    }

    internal JsValue Return()
    {
        if (executing)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator helper is already executing");
        if (closed)
            return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm, JsValue.Undefined,
                true));

        executing = true;
        try
        {
            closed = true;
            CloseUnderlying();
            return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm, JsValue.Undefined,
                true));
        }
        finally
        {
            executing = false;
        }
    }

    private void CloseUnderlying()
    {
        if (iterator.TryGetPropertyAtom(Realm, IdReturn, out var returnValue, out _) &&
            !returnValue.IsUndefined && !returnValue.IsNull)
            _ = Intrinsics.CallIteratorHelperMethod(Realm, returnValue, iterator, "Iterator return must be callable");
    }
}
