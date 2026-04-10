namespace Okojo.Objects;

internal sealed class JsIteratorMapObject : JsObject
{
    private readonly JsObject iterator;
    private readonly JsFunction mapper;
    private readonly JsValue nextMethod;
    private bool closed;
    private long counter;
    private bool executing;

    internal JsIteratorMapObject(JsRealm realm, JsObject iterator, JsValue nextMethod, JsFunction mapper) :
        base(realm)
    {
        this.iterator = iterator;
        this.nextMethod = nextMethod;
        this.mapper = mapper;
        Prototype = realm.IteratorPrototype;

        var nextFn = new JsHostFunction(realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsIteratorMapObject map)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.map result next called on incompatible receiver");

            return map.Next();
        }, "next", 0);

        var returnFn = new JsHostFunction(realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsIteratorMapObject map)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.map result return called on incompatible receiver");

            return map.Return();
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
            var step = Intrinsics.CallIteratorHelperMethod(Realm, nextMethod, iterator,
                "Iterator next must be callable");
            if (!step.TryGetObject(out var stepObj))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result must be an object");

            stepObj.TryGetPropertyAtom(Realm, IdDone, out var doneValue, out _);
            if (JsIteratorHelperOperations.ToBoolean(doneValue))
            {
                closed = true;
                return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm,
                    JsValue.Undefined, true));
            }

            stepObj.TryGetPropertyAtom(Realm, IdValue, out var value, out _);
            JsValue mapped;
            var mapperArgs = new[] { value, new JsValue(counter) };
            try
            {
                mapped = Realm.InvokeFunction(mapper, JsValue.Undefined, mapperArgs);
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

            return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm, mapped,
                false));
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
