using System.Runtime.CompilerServices;
using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;

namespace Okojo.WebPlatform.Internal;

internal static class WebWorkerObjectFactory
{
    private const int HandleSlot = 0;
    private static readonly ConditionalWeakTable<JsRealm, CachedWorkerApi> CacheByRealm = new();

    public static CachedWorkerApi For(JsRealm realm)
    {
        return CacheByRealm.GetValue(realm, static realmValue => new(realmValue));
    }

    private static JsPlainObject GetBackingHandle(in JsValue thisValue, CachedWorkerApi workerApi)
    {
        if (
            !thisValue.TryGetObject(out var wrapper)
            || wrapper is not JsPlainObject wrapperObject
            || !workerApi.BackingHandles.TryGetValue(wrapperObject, out var handle)
        )
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Worker method called on an incompatible receiver",
                "WORKER_THIS_INVALID"
            );

        return handle;
    }

    private static JsValue CallBackingMethod(
        JsRealm realm,
        JsPlainObject handle,
        string methodName,
        ReadOnlySpan<JsValue> args
    )
    {
        if (
            !handle.TryGetProperty(methodName, out var methodValue)
            || !methodValue.TryGetObject(out var methodObject)
            || methodObject is not JsFunction method
        )
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Worker backing handle is missing a required method",
                "WORKER_BACKING_METHOD_MISSING"
            );

        return realm.Call(method, JsValue.FromObject(handle), args);
    }

    private static JsValue GetBackingProperty(JsPlainObject handle, string propertyName)
    {
        return handle.TryGetProperty(propertyName, out var value) ? value : JsValue.Undefined;
    }

    private static void SetBackingProperty(
        JsPlainObject handle,
        string propertyName,
        in JsValue value
    )
    {
        handle.SetProperty(propertyName, value);
    }

    internal sealed class CachedWorkerApi
    {
        public CachedWorkerApi(JsRealm realm)
        {
            BackingHandles = new();
            PrototypeObject = CreatePrototypeObject(realm);
        }

        public ConditionalWeakTable<JsPlainObject, JsPlainObject> BackingHandles { get; }
        public JsPlainObject PrototypeObject { get; }

        public JsPlainObject CreateWorkerObject(JsRealm realm, JsPlainObject workerHandle)
        {
            var wrapper = new JsPlainObject(realm);
            if (!wrapper.TrySetPrototype(PrototypeObject))
                throw new InvalidOperationException(
                    "Worker object prototype could not be assigned."
                );
            BackingHandles.Add(wrapper, workerHandle);
            return wrapper;
        }

        private JsPlainObject CreatePrototypeObject(JsRealm realm)
        {
            var prototype = new JsPlainObject(realm);

            prototype.DefineDataProperty(
                "postMessage",
                JsValue.FromObject(
                    new JsHostFunction(
                        realm,
                        static (in info) =>
                        {
                            var thisHandle = GetBackingHandle(
                                info.ThisValue,
                                (CachedWorkerApi)((JsHostFunction)info.Function).UserData!
                            );
                            return CallBackingMethod(
                                info.Realm,
                                thisHandle,
                                "postMessage",
                                info.Arguments
                            );
                        },
                        "postMessage",
                        1
                    )
                    {
                        UserData = this,
                    }
                ),
                JsShapePropertyFlags.Configurable
            );

            prototype.DefineDataProperty(
                "terminate",
                JsValue.FromObject(
                    new JsHostFunction(
                        realm,
                        static (in info) =>
                        {
                            var thisHandle = GetBackingHandle(
                                info.ThisValue,
                                (CachedWorkerApi)((JsHostFunction)info.Function).UserData!
                            );
                            return CallBackingMethod(
                                info.Realm,
                                thisHandle,
                                "terminate",
                                ReadOnlySpan<JsValue>.Empty
                            );
                        },
                        "terminate",
                        0
                    )
                    {
                        UserData = this,
                    }
                ),
                JsShapePropertyFlags.Configurable
            );

            var onMessageGetter = new JsHostFunction(
                realm,
                static (in info) =>
                {
                    var thisHandle = GetBackingHandle(
                        info.ThisValue,
                        (CachedWorkerApi)((JsHostFunction)info.Function).UserData!
                    );
                    return GetBackingProperty(thisHandle, "onmessage");
                },
                "get onmessage",
                0
            )
            {
                UserData = this,
            };
            var onMessageSetter = new JsHostFunction(
                realm,
                static (in info) =>
                {
                    var thisHandle = GetBackingHandle(
                        info.ThisValue,
                        (CachedWorkerApi)((JsHostFunction)info.Function).UserData!
                    );
                    var value = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
                    SetBackingProperty(thisHandle, "onmessage", value);
                    return JsValue.Undefined;
                },
                "set onmessage",
                1
            )
            {
                UserData = this,
            };
            prototype.DefineAccessorProperty(
                "onmessage",
                onMessageGetter,
                onMessageSetter,
                JsShapePropertyFlags.HasGetter
                    | JsShapePropertyFlags.HasSetter
                    | JsShapePropertyFlags.Configurable
            );

            var onMessageErrorGetter = new JsHostFunction(
                realm,
                static (in info) =>
                {
                    var thisHandle = GetBackingHandle(
                        info.ThisValue,
                        (CachedWorkerApi)((JsHostFunction)info.Function).UserData!
                    );
                    return GetBackingProperty(thisHandle, "onmessageerror");
                },
                "get onmessageerror",
                0
            )
            {
                UserData = this,
            };
            var onMessageErrorSetter = new JsHostFunction(
                realm,
                static (in info) =>
                {
                    var thisHandle = GetBackingHandle(
                        info.ThisValue,
                        (CachedWorkerApi)((JsHostFunction)info.Function).UserData!
                    );
                    var value = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
                    SetBackingProperty(thisHandle, "onmessageerror", value);
                    return JsValue.Undefined;
                },
                "set onmessageerror",
                1
            )
            {
                UserData = this,
            };
            prototype.DefineAccessorProperty(
                "onmessageerror",
                onMessageErrorGetter,
                onMessageErrorSetter,
                JsShapePropertyFlags.HasGetter
                    | JsShapePropertyFlags.HasSetter
                    | JsShapePropertyFlags.Configurable
            );

            prototype.DefineDataProperty(
                realm.ToStringTagSymbol,
                JsValue.FromString("Worker"),
                JsShapePropertyFlags.Configurable
            );

            return prototype;
        }
    }
}
