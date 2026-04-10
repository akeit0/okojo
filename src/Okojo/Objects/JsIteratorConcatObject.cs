namespace Okojo.Objects;

internal sealed class JsIteratorConcatObject : JsObject
{
    private readonly List<Intrinsics.IteratorConcatItem> items;
    private bool closed;
    private JsObject? currentIterator;
    private JsFunction? currentNextMethod;
    private bool executing;
    private int itemIndex;

    internal JsIteratorConcatObject(JsRealm realm, List<Intrinsics.IteratorConcatItem> items) : base(realm)
    {
        this.items = items;
        Prototype = realm.IteratorPrototype;

        var nextFn = new JsHostFunction(realm, static (in info) =>
        {
            var innerRealm = info.Realm;
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsIteratorConcatObject concat)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.concat result next called on incompatible receiver");
            return concat.Next();
        }, "next", 0);
        var returnFn = new JsHostFunction(realm, static (in info) =>
        {
            var innerRealm = info.Realm;
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var thisObj) || thisObj is not JsIteratorConcatObject concat)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Iterator.concat result return called on incompatible receiver");
            return concat.Return();
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
            while (true)
            {
                if (currentIterator is null)
                {
                    if (itemIndex >= items.Count)
                        return JsValue.FromObject(CreateIteratorResultObject(JsValue.Undefined, true));

                    var item = items[itemIndex++];
                    var iteratorValue = Realm.InvokeFunction(item.IteratorMethod, JsValue.FromObject(item.Iterable),
                        ReadOnlySpan<JsValue>.Empty);
                    if (!iteratorValue.TryGetObject(out var iteratorObj))
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "Iterator.concat iterator method must return an object");
                    if (!iteratorObj.TryGetPropertyAtom(Realm, IdNext, out var nextValue, out _) ||
                        !nextValue.TryGetObject(out var nextObj) || nextObj is not JsFunction nextFn)
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "Iterator.concat iterator next is not callable");

                    currentIterator = iteratorObj;
                    currentNextMethod = nextFn;
                }

                var step = Realm.InvokeFunction(currentNextMethod!, JsValue.FromObject(currentIterator),
                    ReadOnlySpan<JsValue>.Empty);
                if (!step.TryGetObject(out var stepObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Iterator result must be an object");

                _ = stepObj.TryGetPropertyAtom(Realm, IdDone, out var doneValue, out _);
                if (doneValue.ToBoolean())
                {
                    currentIterator = null;
                    currentNextMethod = null;
                    continue;
                }

                _ = stepObj.TryGetPropertyAtom(Realm, IdValue, out var value, out _);
                return JsValue.FromObject(CreateIteratorResultObject(value, false));
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
            return JsValue.FromObject(CreateIteratorResultObject(JsValue.Undefined, true));

        executing = true;
        try
        {
            closed = true;
            if (currentIterator is not null &&
                currentIterator.TryGetPropertyAtom(Realm, IdReturn, out var returnValue, out _) &&
                !returnValue.IsUndefined && !returnValue.IsNull)
            {
                if (!returnValue.TryGetObject(out var returnObj) || returnObj is not JsFunction returnFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "Iterator return must be callable");

                _ = Realm.InvokeFunction(returnFn, JsValue.FromObject(currentIterator), ReadOnlySpan<JsValue>.Empty);
            }

            currentIterator = null;
            currentNextMethod = null;
            itemIndex = items.Count;
            return JsValue.FromObject(CreateIteratorResultObject(JsValue.Undefined, true));
        }
        finally
        {
            executing = false;
        }
    }

    private JsPlainObject CreateIteratorResultObject(JsValue value, bool done)
    {
        return Realm.CreateIteratorResultObject(value, done);
    }
}
