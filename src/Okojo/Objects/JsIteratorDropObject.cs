namespace Okojo.Objects;

internal sealed class JsIteratorDropObject : JsObject
{
    private readonly JsObject iterator;
    private readonly JsValue nextMethod;
    private bool closed;
    private bool executing;
    private long remaining;

    internal JsIteratorDropObject(JsRealm realm, JsObject iterator, JsValue nextMethod, long remaining) : base(realm)
    {
        this.iterator = iterator;
        this.nextMethod = nextMethod;
        this.remaining = remaining;
        Prototype = realm.Intrinsics.IteratorPrototype;

        var nextFn = new JsHostFunction(realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsIteratorDropObject drop)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.drop result next called on incompatible receiver");

            return drop.Next();
        }, "next", 0);

        var returnFn = new JsHostFunction(realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsIteratorDropObject drop)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.drop result return called on incompatible receiver");

            return drop.Return();
        }, "return", 0);

        DefineDataPropertyAtom(realm, IdNext, JsValue.FromObject(nextFn), JsShapePropertyFlags.Open);
        DefineDataPropertyAtom(realm, IdReturn, JsValue.FromObject(returnFn), JsShapePropertyFlags.Open);
    }

    internal JsValue Next()
    {
        if (executing)
            throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator helper is already executing");
        if (closed)
            return JsValue.FromObject(CreateIteratorResultObject(JsValue.Undefined, true));

        executing = true;
        try
        {
            while (remaining > 0)
            {
                var step = InvokeNext();
                _ = step.TryGetPropertyAtom(Realm, IdDone, out var dropDoneValue, out _);
                if (dropDoneValue.ToBoolean())
                {
                    closed = true;
                    return JsValue.FromObject(CreateIteratorResultObject(JsValue.Undefined, true));
                }

                remaining--;
            }

            var nextStep = InvokeNext();
            _ = nextStep.TryGetPropertyAtom(Realm, IdDone, out var doneValue, out _);
            if (doneValue.ToBoolean())
            {
                closed = true;
                return JsValue.FromObject(CreateIteratorResultObject(JsValue.Undefined, true));
            }

            _ = nextStep.TryGetPropertyAtom(Realm, IdValue, out var value, out _);
            return JsValue.FromObject(CreateIteratorResultObject(value, false));
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
            return JsValue.FromObject(CreateIteratorResultObject(JsValue.Undefined, true));

        executing = true;
        try
        {
            closed = true;
            if (iterator.TryGetPropertyAtom(Realm, IdReturn, out var returnValue, out _) &&
                !returnValue.IsUndefined && !returnValue.IsNull)
                _ = Intrinsics.CallIteratorHelperMethod(Realm, returnValue, iterator,
                    "Iterator return must be callable");

            return JsValue.FromObject(CreateIteratorResultObject(JsValue.Undefined, true));
        }
        finally
        {
            executing = false;
        }
    }

    private JsObject InvokeNext()
    {
        var step = Intrinsics.CallIteratorHelperMethod(Realm, nextMethod, iterator, "Iterator next must be callable");
        if (!step.TryGetObject(out var stepObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result must be an object");
        return stepObj;
    }

    private JsPlainObject CreateIteratorResultObject(JsValue value, bool done)
    {
        return Realm.CreateIteratorResultObject(value, done);
    }
}
