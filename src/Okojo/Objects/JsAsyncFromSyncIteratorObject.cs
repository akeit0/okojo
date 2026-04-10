using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Okojo.Objects;

internal sealed class JsAsyncFromSyncIteratorObject : JsObject
{
    private readonly JsObject iterator;
    private JsValue cachedNextMethod;
    private bool hasCachedNextMethod;

    internal JsAsyncFromSyncIteratorObject(JsRealm realm, JsObject iterator) : base(realm)
    {
        this.iterator = iterator;
        cachedNextMethod = JsValue.Undefined;
        Prototype = realm.AsyncIteratorPrototype;

        var nextFn = new JsHostFunction(realm, static (in info) =>
        {
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsAsyncFromSyncIteratorObject wrapped)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Async-from-sync iterator next called on incompatible receiver");
            var args = info.Arguments;
            return args.Length != 0 ? wrapped.Next(args[0], true) : wrapped.Next(JsValue.Undefined, false);
        }, "next", 1);

        var returnFn = new JsHostFunction(realm, static (in info) =>
        {
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsAsyncFromSyncIteratorObject wrapped)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Async-from-sync iterator return called on incompatible receiver");
            var args = info.Arguments;
            return args.Length != 0 ? wrapped.Return(args[0], true) : wrapped.Return(JsValue.Undefined, false);
        }, "return", 1);

        var throwFn = new JsHostFunction(realm, static (in info) =>
        {
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsAsyncFromSyncIteratorObject wrapped)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Async-from-sync iterator throw called on incompatible receiver");
            var args = info.Arguments;
            return args.Length != 0 ? wrapped.Throw(args[0], true) : wrapped.Throw(JsValue.Undefined, false);
        }, "throw", 1);

        DefineDataPropertyAtom(realm, IdNext, JsValue.FromObject(nextFn), JsShapePropertyFlags.Open);
        DefineDataPropertyAtom(realm, IdReturn, JsValue.FromObject(returnFn), JsShapePropertyFlags.Open);
        DefineDataPropertyAtom(realm, IdThrow, JsValue.FromObject(throwFn), JsShapePropertyFlags.Open);
    }

    private JsValue Next(in JsValue value, bool hasValue)
    {
        try
        {
            if (!hasCachedNextMethod)
            {
                if (!iterator.TryGetPropertyAtom(Realm, IdNext, out cachedNextMethod, out _))
                    cachedNextMethod = JsValue.Undefined;
                hasCachedNextMethod = true;
            }

            if (!cachedNextMethod.TryGetObject(out var nextObj) || nextObj is not JsFunction nextFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "iterator.next is not a function");

            var result = hasValue
                ? Realm.InvokeFunction(nextFn, JsValue.FromObject(iterator),
                    MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1))
                : Realm.InvokeFunction(nextFn, JsValue.FromObject(iterator), ReadOnlySpan<JsValue>.Empty);
            if (!result.TryGetObject(out var resultObj))
                throw new JsRuntimeException(JsErrorKind.TypeError, "iterator result is not an object");
            _ = resultObj.TryGetPropertyAtom(Realm, IdDone, out var doneValue, out _);
            var done = JsIteratorHelperOperations.ToBoolean(doneValue);
            if (!resultObj.TryGetPropertyAtom(Realm, IdValue, out var resultValue, out _))
                resultValue = JsValue.Undefined;
            return Realm.CreateAsyncFromSyncIteratorResultPromise(resultValue, done, iterator, true);
        }
        catch (JsRuntimeException ex)
        {
            return Reject(ex);
        }
    }

    private JsValue Return(in JsValue value, bool hasValue)
    {
        try
        {
            if (!iterator.TryGetPropertyAtom(Realm, IdReturn, out var returnValue, out _) ||
                returnValue.IsUndefined || returnValue.IsNull)
                return Realm.CreateAsyncFromSyncIteratorResultPromise(value, true);

            if (!returnValue.TryGetObject(out var returnObj) || returnObj is not JsFunction returnFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "iterator.return is not a function");

            var result = hasValue
                ? Realm.InvokeFunction(returnFn, JsValue.FromObject(iterator),
                    MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1))
                : Realm.InvokeFunction(returnFn, JsValue.FromObject(iterator), ReadOnlySpan<JsValue>.Empty);
            if (!result.TryGetObject(out var resultObj))
                throw new JsRuntimeException(JsErrorKind.TypeError, "iterator result is not an object");
            return Realm.CreateAsyncFromSyncIteratorResultPromise(resultObj);
        }
        catch (JsRuntimeException ex)
        {
            return Reject(ex);
        }
    }

    private JsValue Throw(in JsValue value, bool hasValue)
    {
        try
        {
            if (!iterator.TryGetPropertyAtom(Realm, IdThrow, out var throwValue, out _) ||
                throwValue.IsUndefined || throwValue.IsNull)
            {
                Realm.IteratorCloseForYieldDelegateThrow(iterator);
                throw new JsRuntimeException(JsErrorKind.TypeError, "iterator.throw is not present");
            }

            if (!throwValue.TryGetObject(out var throwObj) || throwObj is not JsFunction throwFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "iterator.throw is not a function");

            var result = hasValue
                ? Realm.InvokeFunction(throwFn, JsValue.FromObject(iterator),
                    MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1))
                : Realm.InvokeFunction(throwFn, JsValue.FromObject(iterator), ReadOnlySpan<JsValue>.Empty);
            if (!result.TryGetObject(out var resultObj))
                throw new JsRuntimeException(JsErrorKind.TypeError, "iterator result is not an object");
            return Realm.CreateAsyncFromSyncIteratorResultPromise(resultObj, iterator, true);
        }
        catch (JsRuntimeException ex)
        {
            return Reject(ex);
        }
    }

    private JsValue Reject(JsRuntimeException ex)
    {
        var promise = Realm.CreatePromiseObject();
        Realm.RejectPromise(promise, ex.ThrownValue ?? Realm.CreateErrorObjectFromException(ex));
        return JsValue.FromObject(promise);
    }
}
