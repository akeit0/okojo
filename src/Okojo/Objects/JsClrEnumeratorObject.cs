using Okojo.Runtime.Interop;

namespace Okojo.Objects;

internal sealed class JsClrEnumeratorObject : JsObject
{
    private readonly HostEnumeratorAdapter enumerator;
    private bool completed;

    internal JsClrEnumeratorObject(JsRealm realm, JsHostObject host)
        : base(realm)
    {
        Prototype = realm.Intrinsics.IteratorPrototype;
        enumerator = CreateEnumerator(host);

        var nextFn = new JsHostFunction(realm, static (in info) =>
        {
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsClrEnumeratorObject iterator)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "CLR Enumerator next called on incompatible receiver");

            return JsValue.FromObject(iterator.Next());
        }, "next", 0);

        var returnFn = new JsHostFunction(realm, static (in info) =>
        {
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsClrEnumeratorObject iterator)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "CLR Enumerator return called on incompatible receiver");

            return JsValue.FromObject(iterator.Return(info.GetArgumentOrDefault(0, JsValue.Undefined)));
        }, "return", 1);

        DefineDataPropertyAtom(realm, IdNext, JsValue.FromObject(nextFn), JsShapePropertyFlags.Open);
        DefineDataPropertyAtom(realm, IdReturn, JsValue.FromObject(returnFn), JsShapePropertyFlags.Open);
        DefineDataPropertyAtom(realm, IdSymbolIterator, JsValue.FromObject(realm.Intrinsics.IteratorSelfFunction),
            JsShapePropertyFlags.Open);
    }

    private static HostEnumeratorAdapter CreateEnumerator(JsHostObject host)
    {
        var createEnumerator = host.Descriptor.Enumerator
                          ?? throw new InvalidOperationException("Host type does not expose GetEnumerator.");
        return createEnumerator(host.Data);
    }

    internal JsPlainObject Next()
    {
        if (completed)
            return Realm.CreateIteratorResultObject(JsValue.Undefined, true);

        if (!enumerator.MoveNext())
        {
            Close();
            return Realm.CreateIteratorResultObject(JsValue.Undefined, true);
        }

        var current = enumerator.GetCurrentAsJsValue(Realm);
        return Realm.CreateIteratorResultObject(current, false);
    }

    internal JsPlainObject Return(in JsValue value)
    {
        Close();
        return Realm.CreateIteratorResultObject(value, true);
    }

    private void Close()
    {
        if (completed)
            return;

        completed = true;
        enumerator.Dispose();
    }
}
