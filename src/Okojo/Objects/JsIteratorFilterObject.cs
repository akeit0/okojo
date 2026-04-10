namespace Okojo.Objects;

internal sealed class JsIteratorFilterObject : JsObject
{
    private readonly JsObject iterator;
    private readonly JsValue nextMethod;
    private readonly JsFunction predicate;
    private bool closed;
    private long counter;
    private bool executing;

    internal JsIteratorFilterObject(JsRealm realm, JsObject iterator, JsValue nextMethod, JsFunction predicate) :
        base(realm)
    {
        this.iterator = iterator;
        this.nextMethod = nextMethod;
        this.predicate = predicate;
        Prototype = realm.IteratorPrototype;

        var nextFn = new JsHostFunction(realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsIteratorFilterObject filter)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.filter result next called on incompatible receiver");

            return filter.Next();
        }, "next", 0);

        var returnFn = new JsHostFunction(realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsIteratorFilterObject filter)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.filter result return called on incompatible receiver");

            return filter.Return();
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
            while (true)
            {
                var step = InvokeNext();
                step.TryGetPropertyAtom(Realm, IdDone, out var doneValue, out _);
                if (JsIteratorHelperOperations.ToBoolean(doneValue))
                {
                    closed = true;
                    return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm,
                        JsValue.Undefined, true));
                }

                step.TryGetPropertyAtom(Realm, IdValue, out var value, out _);

                JsValue predicateResult;
                var predicateArgs = new[] { value, new JsValue(counter) };
                try
                {
                    predicateResult = Realm.InvokeFunction(predicate, JsValue.Undefined, predicateArgs);
                }
                catch
                {
                    BestEffortClose();
                    throw;
                }
                finally
                {
                    counter++;
                }

                if (JsIteratorHelperOperations.ToBoolean(predicateResult))
                    return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm, value,
                        false));
            }
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
            if (iterator.TryGetPropertyAtom(Realm, IdReturn, out var returnValue, out _) &&
                !returnValue.IsUndefined && !returnValue.IsNull)
                _ = Intrinsics.CallIteratorHelperMethod(Realm, returnValue, iterator,
                    "Iterator return must be callable");

            return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm, JsValue.Undefined,
                true));
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

    private void BestEffortClose()
    {
        try
        {
            if (iterator.TryGetPropertyAtom(Realm, IdReturn, out var returnValue, out _) &&
                !returnValue.IsUndefined && !returnValue.IsNull)
                _ = Intrinsics.CallIteratorHelperMethod(Realm, returnValue, iterator,
                    "Iterator return must be callable");
        }
        catch
        {
            // Preserve the original abrupt completion.
        }
    }
}
