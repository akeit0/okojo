using System.Diagnostics;
using System.Runtime.CompilerServices;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.WebPlatform.Internal;

internal static class WebWorkerObjectFactory
{
    private const int HandleSlot = 0;
    private static readonly ConditionalWeakTable<JsRealm, CachedWorkerApi> CacheByRealm = new();

    public static CachedWorkerApi For(JsRealm realm)
    {
        return CacheByRealm.GetValue(realm, static realmValue => new(realmValue));
    }

    private static JsPlainObject GetBackingHandle(JsRealm realm, in JsValue thisValue, int handleAtom)
    {
        if (!thisValue.TryGetObject(out var wrapper) ||
            !wrapper.TryGetPropertyAtom(realm, handleAtom, out var handleValue, out _) ||
            !handleValue.TryGetObject(out var handleObject) ||
            handleObject is not JsPlainObject handle)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Worker method called on an incompatible receiver",
                "WORKER_THIS_INVALID");

        return handle;
    }

    private static JsValue CallBackingMethod(
        JsRealm realm,
        JsPlainObject handle,
        int methodAtom,
        ReadOnlySpan<JsValue> args)
    {
        if (!handle.TryGetPropertyAtom(realm, methodAtom, out var methodValue, out _) ||
            !methodValue.TryGetObject(out var methodObject) ||
            methodObject is not JsFunction method)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Worker backing handle is missing a required method",
                "WORKER_BACKING_METHOD_MISSING");

        return realm.Call(method, JsValue.FromObject(handle), args);
    }

    private static JsValue GetBackingProperty(JsRealm realm, JsPlainObject handle, int atom)
    {
        return handle.TryGetPropertyAtom(realm, atom, out var value, out _) ? value : JsValue.Undefined;
    }

    private static void SetBackingProperty(JsRealm realm, JsPlainObject handle, int atom, in JsValue value)
    {
        handle.SetPropertyAtom(realm, atom, value, out _);
    }

    internal sealed class CachedWorkerApi
    {
        public CachedWorkerApi(JsRealm realm)
        {
            HandleAtom = realm.Atoms.InternSymbolString("Okojo.Web.Worker.handle");

            var shape = realm.EmptyShape;
            shape = shape.GetOrAddTransition(HandleAtom, JsShapePropertyFlags.None, out var handleInfo);
            Debug.Assert(handleInfo.Slot == HandleSlot);
            InstanceShape = shape;

            PrototypeObject = CreatePrototypeObject(realm);
        }

        public int HandleAtom { get; }
        public StaticNamedPropertyLayout InstanceShape { get; }
        public JsPlainObject PrototypeObject { get; }

        public JsPlainObject CreateWorkerObject(JsRealm realm, JsPlainObject workerHandle)
        {
            var wrapper = new JsPlainObject(InstanceShape, false)
            {
                Prototype = PrototypeObject
            };
            wrapper.SetNamedSlotUnchecked(HandleSlot, JsValue.FromObject(workerHandle));
            return wrapper;
        }

        private JsPlainObject CreatePrototypeObject(JsRealm realm)
        {
            var prototype = new JsPlainObject(realm, false)
            {
                Prototype = realm.ObjectPrototype
            };

            prototype.DefineDataPropertyAtom(realm, AtomTable.IdPostMessage,
                JsValue.FromObject(new JsHostFunction(realm, static (in info) =>
                {
                    var thisHandle = GetBackingHandle(info.Realm, info.ThisValue,
                        ((CachedWorkerApi)((JsHostFunction)info.Function).UserData!).HandleAtom);
                    return CallBackingMethod(info.Realm, thisHandle, AtomTable.IdPostMessage, info.Arguments);
                }, "postMessage", 1)
                {
                    UserData = this
                }), JsShapePropertyFlags.Configurable);

            prototype.DefineDataPropertyAtom(realm, AtomTable.IdTerminate,
                JsValue.FromObject(new JsHostFunction(realm, static (in info) =>
                {
                    var thisHandle = GetBackingHandle(info.Realm, info.ThisValue,
                        ((CachedWorkerApi)((JsHostFunction)info.Function).UserData!).HandleAtom);
                    return CallBackingMethod(info.Realm, thisHandle, AtomTable.IdTerminate,
                        ReadOnlySpan<JsValue>.Empty);
                }, "terminate", 0)
                {
                    UserData = this
                }), JsShapePropertyFlags.Configurable);

            var onMessageGetter = new JsHostFunction(realm, static (in info) =>
            {
                var thisHandle = GetBackingHandle(info.Realm, info.ThisValue,
                    ((CachedWorkerApi)((JsHostFunction)info.Function).UserData!).HandleAtom);
                return GetBackingProperty(info.Realm, thisHandle, AtomTable.IdOnmessage);
            }, "get onmessage", 0)
            {
                UserData = this
            };
            var onMessageSetter = new JsHostFunction(realm, static (in info) =>
            {
                var thisHandle = GetBackingHandle(info.Realm, info.ThisValue,
                    ((CachedWorkerApi)((JsHostFunction)info.Function).UserData!).HandleAtom);
                var value = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
                SetBackingProperty(info.Realm, thisHandle, AtomTable.IdOnmessage, value);
                return JsValue.Undefined;
            }, "set onmessage", 1)
            {
                UserData = this
            };
            prototype.DefineAccessorPropertyAtom(realm, AtomTable.IdOnmessage, onMessageGetter, onMessageSetter,
                JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter | JsShapePropertyFlags.Configurable);

            var onMessageErrorGetter = new JsHostFunction(realm, static (in info) =>
            {
                var thisHandle = GetBackingHandle(info.Realm, info.ThisValue,
                    ((CachedWorkerApi)((JsHostFunction)info.Function).UserData!).HandleAtom);
                return GetBackingProperty(info.Realm, thisHandle, AtomTable.IdOnmessageerror);
            }, "get onmessageerror", 0)
            {
                UserData = this
            };
            var onMessageErrorSetter = new JsHostFunction(realm, static (in info) =>
            {
                var thisHandle = GetBackingHandle(info.Realm, info.ThisValue,
                    ((CachedWorkerApi)((JsHostFunction)info.Function).UserData!).HandleAtom);
                var value = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
                SetBackingProperty(info.Realm, thisHandle, AtomTable.IdOnmessageerror, value);
                return JsValue.Undefined;
            }, "set onmessageerror", 1)
            {
                UserData = this
            };
            prototype.DefineAccessorPropertyAtom(realm, AtomTable.IdOnmessageerror, onMessageErrorGetter,
                onMessageErrorSetter,
                JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.HasSetter | JsShapePropertyFlags.Configurable);

            prototype.DefineDataPropertyAtom(realm, AtomTable.IdSymbolToStringTag, JsValue.FromString("Worker"),
                JsShapePropertyFlags.Configurable);

            return prototype;
        }
    }
}
