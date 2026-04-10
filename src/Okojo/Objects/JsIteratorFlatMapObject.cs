namespace Okojo.Objects;

internal sealed class JsIteratorFlatMapObject : JsObject
{
    private readonly JsFunction mapper;
    private readonly JsObject outerIterator;
    private readonly JsValue outerNextMethod;
    private bool closed;
    private long counter;
    private bool executing;
    private JsObject? innerIterator;
    private JsValue innerNextMethod;

    internal JsIteratorFlatMapObject(JsRealm realm, JsObject outerIterator, JsValue outerNextMethod,
        JsFunction mapper) : base(realm)
    {
        this.outerIterator = outerIterator;
        this.outerNextMethod = outerNextMethod;
        this.mapper = mapper;
        innerNextMethod = JsValue.Undefined;
        Prototype = realm.IteratorPrototype;

        var nextFn = new JsHostFunction(realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsIteratorFlatMapObject flatMap)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.flatMap result next called on incompatible receiver");

            return flatMap.Next();
        }, "next", 0);

        var returnFn = new JsHostFunction(realm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsIteratorFlatMapObject flatMap)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.flatMap result return called on incompatible receiver");

            return flatMap.Return();
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
                if (innerIterator is not null)
                {
                    var innerStep = Intrinsics.CallIteratorHelperMethod(Realm, innerNextMethod, innerIterator,
                        "Iterator next must be callable");
                    if (!innerStep.TryGetObject(out var innerStepObj))
                        throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result must be an object");

                    innerStepObj.TryGetPropertyAtom(Realm, IdDone, out var innerDoneValue, out _);
                    if (JsIteratorHelperOperations.ToBoolean(innerDoneValue))
                    {
                        innerIterator = null;
                        innerNextMethod = JsValue.Undefined;
                        continue;
                    }

                    innerStepObj.TryGetPropertyAtom(Realm, IdValue, out var innerValue, out _);
                    return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm, innerValue,
                        false));
                }

                var outerStep = Intrinsics.CallIteratorHelperMethod(Realm, outerNextMethod, outerIterator,
                    "Iterator next must be callable");
                if (!outerStep.TryGetObject(out var outerStepObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator result must be an object");

                outerStepObj.TryGetPropertyAtom(Realm, IdDone, out var outerDoneValue, out _);
                if (JsIteratorHelperOperations.ToBoolean(outerDoneValue))
                {
                    closed = true;
                    return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm,
                        JsValue.Undefined, true));
                }

                outerStepObj.TryGetPropertyAtom(Realm, IdValue, out var outerValue, out _);
                JsValue mapped;
                var mapperArgs = new[] { outerValue, new JsValue(counter) };
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

                InitializeInnerIterator(mapped);
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
            if (innerIterator is not null &&
                innerIterator.TryGetPropertyAtom(Realm, IdReturn, out var innerReturnValue, out _) &&
                !innerReturnValue.IsUndefined && !innerReturnValue.IsNull)
                _ = Intrinsics.CallIteratorHelperMethod(Realm, innerReturnValue, innerIterator,
                    "Iterator return must be callable");

            innerIterator = null;
            innerNextMethod = JsValue.Undefined;

            if (outerIterator.TryGetPropertyAtom(Realm, IdReturn, out var outerReturnValue, out _) &&
                !outerReturnValue.IsUndefined && !outerReturnValue.IsNull)
                _ = Intrinsics.CallIteratorHelperMethod(Realm, outerReturnValue, outerIterator,
                    "Iterator return must be callable");

            return JsValue.FromObject(JsIteratorHelperOperations.CreateIteratorResultObject(Realm, JsValue.Undefined,
                true));
        }
        finally
        {
            executing = false;
        }
    }

    private void InitializeInnerIterator(JsValue mapped)
    {
        if (!mapped.TryGetObject(out var mappedObj))
            throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator.flatMap mapper result must be object");

        if (mappedObj.TryGetPropertyAtom(Realm, IdSymbolIterator, out var iteratorMethodValue, out _))
            if (!iteratorMethodValue.IsUndefined && !iteratorMethodValue.IsNull)
            {
                if (!iteratorMethodValue.TryGetObject(out var iteratorMethodObj) ||
                    iteratorMethodObj is not JsFunction iteratorMethod)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Iterator.flatMap mapper result is not iterable");

                var iteratorValue = Realm.InvokeFunction(iteratorMethod, JsValue.FromObject(mappedObj),
                    ReadOnlySpan<JsValue>.Empty);
                if (!iteratorValue.TryGetObject(out var iteratorObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Iterator method must return an object");
                innerIterator = iteratorObj;
                if (!iteratorObj.TryGetPropertyAtom(Realm, IdNext, out innerNextMethod, out _))
                    innerNextMethod = JsValue.Undefined;
                return;
            }

        innerIterator = mappedObj;
        if (!mappedObj.TryGetPropertyAtom(Realm, IdNext, out innerNextMethod, out _))
            innerNextMethod = JsValue.Undefined;
    }

    private void BestEffortClose()
    {
        try
        {
            if (innerIterator is not null &&
                innerIterator.TryGetPropertyAtom(Realm, IdReturn, out var innerReturnValue, out _) &&
                !innerReturnValue.IsUndefined && !innerReturnValue.IsNull)
                _ = Intrinsics.CallIteratorHelperMethod(Realm, innerReturnValue, innerIterator,
                    "Iterator return must be callable");

            if (outerIterator.TryGetPropertyAtom(Realm, IdReturn, out var outerReturnValue, out _) &&
                !outerReturnValue.IsUndefined && !outerReturnValue.IsNull)
                _ = Intrinsics.CallIteratorHelperMethod(Realm, outerReturnValue, outerIterator,
                    "Iterator return must be callable");
        }
        catch
        {
            // Preserve original abrupt completion.
        }
    }
}
