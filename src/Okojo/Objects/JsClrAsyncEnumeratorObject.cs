using Okojo.Runtime.Interop;

namespace Okojo.Objects;

internal sealed class JsClrAsyncEnumeratorObject : JsObject
{
    private readonly HostAsyncEnumeratorAdapter enumerator;
    private bool completed;

    internal JsClrAsyncEnumeratorObject(JsRealm realm, JsHostObject host)
        : base(realm)
    {
        Prototype = realm.AsyncIteratorPrototype;
        enumerator = CreateEnumerator(host);

        var nextFn = new JsHostFunction(realm, static (in info) =>
        {
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsClrAsyncEnumeratorObject iterator)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "CLR async enumerator next called on incompatible receiver");

            return iterator.Next();
        }, "next", 0);

        var returnFn = new JsHostFunction(realm, static (in info) =>
        {
            if (!info.ThisValue.TryGetObject(out var thisObj) || thisObj is not JsClrAsyncEnumeratorObject iterator)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "CLR async enumerator return called on incompatible receiver");

            return iterator.Return(info.GetArgumentOrDefault(0, JsValue.Undefined));
        }, "return", 1);

        DefineDataPropertyAtom(realm, IdNext, JsValue.FromObject(nextFn), JsShapePropertyFlags.Open);
        DefineDataPropertyAtom(realm, IdReturn, JsValue.FromObject(returnFn), JsShapePropertyFlags.Open);
        DefineDataPropertyAtom(realm, IdSymbolAsyncIterator,
            JsValue.FromObject(realm.Intrinsics.AsyncIteratorSelfFunction), JsShapePropertyFlags.Open);
    }

    private static HostAsyncEnumeratorAdapter CreateEnumerator(JsHostObject host)
    {
        var createEnumerator = host.Descriptor.AsyncEnumerator
                          ?? throw new InvalidOperationException("Host type does not expose GetAsyncEnumerator.");
        return createEnumerator(host.Data);
    }

    private JsValue Next()
    {
        try
        {
            return Realm.WrapTask(NextCore());
        }
        catch (Exception ex)
        {
            return Reject(ex);
        }
    }

    private JsValue Return(in JsValue value)
    {
        try
        {
            return Realm.WrapTask(ReturnCore(value));
        }
        catch (Exception ex)
        {
            return Reject(ex);
        }
    }

    private async ValueTask<JsPlainObject> NextCore()
    {
        if (completed)
            return Realm.CreateIteratorResultObject(JsValue.Undefined, true);

        if (!await enumerator.MoveNextAsync())
        {
            await CloseAsync();
            return Realm.CreateIteratorResultObject(JsValue.Undefined, true);
        }

        var current = enumerator.GetCurrentAsJsValue(Realm);
        return Realm.CreateIteratorResultObject(current, false);
    }

    private async ValueTask<JsPlainObject> ReturnCore(JsValue value)
    {
        await CloseAsync();
        return Realm.CreateIteratorResultObject(value, true);
    }

    private async ValueTask CloseAsync()
    {
        if (completed)
            return;

        completed = true;
        await enumerator.DisposeAsync();
    }

    private JsValue Reject(Exception ex)
    {
        var promise = Realm.CreatePromiseObject();
        var runtime = ex as JsRuntimeException ?? JsRealm.WrapUnexpectedRuntimeException(ex);
        var thrown = runtime.ThrownValue ?? Realm.CreateErrorObjectFromException(runtime);
        Realm.RejectPromise(promise, thrown);
        return JsValue.FromObject(promise);
    }
}
