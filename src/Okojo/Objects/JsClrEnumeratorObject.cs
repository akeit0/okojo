using System.Collections;

namespace Okojo.Objects;

internal sealed class JsClrEnumeratorObject : JsObject
{
    private readonly IEnumerator enumerator;
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

        DefineDataPropertyAtom(realm, IdNext, JsValue.FromObject(nextFn), JsShapePropertyFlags.Open);
        DefineDataPropertyAtom(realm, IdSymbolIterator, JsValue.FromObject(realm.Intrinsics.IteratorSelfFunction),
            JsShapePropertyFlags.Open);
    }

    private static IEnumerator CreateEnumerator(JsHostObject host)
    {
        var descriptor = host.Descriptor.Enumerator
                         ?? throw new InvalidOperationException("Host type does not expose GetEnumerator.");
        return descriptor.GetEnumerator(host.Data);
    }

    internal JsPlainObject Next()
    {
        if (completed)
            return Realm.CreateIteratorResultObject(JsValue.Undefined, true);

        if (!enumerator.MoveNext())
        {
            completed = true;
            return Realm.CreateIteratorResultObject(JsValue.Undefined, true);
        }

        var current = HostValueConverter.ConvertToJsValue(Realm, enumerator.Current);
        return Realm.CreateIteratorResultObject(current, false);
    }
}
