using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeEventsBuiltIn(NodeRuntime runtime)
{
    private const int ModuleEventEmitterSlot = 0;

    private const int PrototypeOnSlot = 0;
    private const int PrototypeOnceSlot = 1;
    private const int PrototypeOffSlot = 2;
    private const int PrototypeEmitSlot = 3;
    private const int PrototypeAddListenerSlot = 4;
    private const int PrototypeRemoveListenerSlot = 5;
    private const int PrototypeSetMaxListenersSlot = 6;
    private const int PrototypeGetMaxListenersSlot = 7;
    private const int PrototypeListenersSlot = 8;
    private const int PrototypeListenerCountSlot = 9;

    private readonly ConditionalWeakTable<JsObject, JsUserDataObject<EventEmitterState>> emitterStates = new();

    private int atomAddListener = -1;
    private int atomEmit = -1;
    private int atomEventEmitter = -1;
    private int atomGetMaxListeners = -1;
    private int atomListenerCount = -1;
    private int atomListeners = -1;
    private int atomOff = -1;
    private int atomOn = -1;
    private int atomOnce = -1;
    private int atomRemoveListener = -1;
    private int atomSetMaxListeners = -1;
    private JsHostFunction? constructorFunction;
    private JsPlainObject? moduleObject;
    private StaticNamedPropertyLayout? moduleShape;
    private JsPlainObject? prototypeObject;
    private StaticNamedPropertyLayout? prototypeShape;

    public JsPlainObject GetModule()
    {
        if (moduleObject is not null)
            return moduleObject;

        var realm = runtime.MainRealm;
        var shape = moduleShape ??= CreateModuleShape(realm);
        var module = new JsPlainObject(shape);
        module.SetNamedSlotUnchecked(ModuleEventEmitterSlot, JsValue.FromObject(GetEventEmitterConstructor()));
        moduleObject = module;
        return module;
    }

    public JsHostFunction GetEventEmitterConstructor()
    {
        if (constructorFunction is not null)
            return constructorFunction;

        var realm = runtime.MainRealm;
        var ctor = new JsHostFunction(realm, "EventEmitter", 0, static (in info) =>
        {
            if (!info.IsConstruct)
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    "Class constructor EventEmitter cannot be invoked without 'new'");

            var state = (ConstructorState)((JsHostFunction)info.Function).UserData!;
            if (info.ThisValue.TryGetObject(out var thisObject))
            {
                state.Owner.InitializeEmitterReceiver(info.Realm, thisObject);
                return info.ThisValue;
            }

            return JsValue.FromObject(state.Owner.CreateEmitterInstance(info.Realm));
        }, true)
        {
            UserData = new ConstructorState(this)
        };
        ctor.DefineDataProperty("prototype", JsValue.FromObject(GetPrototypeObject()), JsShapePropertyFlags.Open);
        constructorFunction = ctor;
        return ctor;
    }

    private JsUserDataObject<EventEmitterState> CreateEmitterInstance(JsRealm realm)
    {
        var emitter = new JsUserDataObject<EventEmitterState>(realm, false);
        emitter.UserData = new();
        emitter.Prototype = GetPrototypeObject();
        return emitter;
    }

    internal void InitializeEmitterReceiver(JsRealm realm, JsObject receiver)
    {
        EnsureAtoms(realm);
        if (emitterStates.TryGetValue(receiver, out _)) return;

        var stateBox = new JsUserDataObject<EventEmitterState>(realm, false)
        {
            UserData = new(),
            Prototype = null
        };

        emitterStates.Add(receiver, stateBox);
    }

    internal JsPlainObject GetPrototypeObject()
    {
        if (prototypeObject is not null)
            return prototypeObject;

        var realm = runtime.MainRealm;
        var shape = prototypeShape ??= CreatePrototypeShape(realm);
        var prototype = new JsPlainObject(shape);
        prototype.SetNamedSlotUnchecked(PrototypeOnSlot, JsValue.FromObject(CreateOnFunction(realm)));
        prototype.SetNamedSlotUnchecked(PrototypeOnceSlot, JsValue.FromObject(CreateOnceFunction(realm)));
        prototype.SetNamedSlotUnchecked(PrototypeOffSlot, JsValue.FromObject(CreateOffFunction(realm)));
        prototype.SetNamedSlotUnchecked(PrototypeEmitSlot, JsValue.FromObject(CreateEmitFunction(realm)));
        prototype.SetNamedSlotUnchecked(PrototypeAddListenerSlot, JsValue.FromObject(CreateAddListenerFunction(realm)));
        prototype.SetNamedSlotUnchecked(PrototypeRemoveListenerSlot,
            JsValue.FromObject(CreateRemoveListenerFunction(realm)));
        prototype.SetNamedSlotUnchecked(PrototypeSetMaxListenersSlot,
            JsValue.FromObject(CreateSetMaxListenersFunction(realm)));
        prototype.SetNamedSlotUnchecked(PrototypeGetMaxListenersSlot,
            JsValue.FromObject(CreateGetMaxListenersFunction(realm)));
        prototype.SetNamedSlotUnchecked(PrototypeListenersSlot, JsValue.FromObject(CreateListenersFunction(realm)));
        prototype.SetNamedSlotUnchecked(PrototypeListenerCountSlot,
            JsValue.FromObject(CreateListenerCountFunction(realm)));
        prototypeObject = prototype;
        return prototype;
    }

    private StaticNamedPropertyLayout CreateModuleShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomEventEmitter, JsShapePropertyFlags.Open,
            out var eventEmitterInfo);
        Debug.Assert(eventEmitterInfo.Slot == ModuleEventEmitterSlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreatePrototypeShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomOn, JsShapePropertyFlags.Open, out var onInfo);
        shape = shape.GetOrAddTransition(atomOnce, JsShapePropertyFlags.Open, out var onceInfo);
        shape = shape.GetOrAddTransition(atomOff, JsShapePropertyFlags.Open, out var offInfo);
        shape = shape.GetOrAddTransition(atomEmit, JsShapePropertyFlags.Open, out var emitInfo);
        shape = shape.GetOrAddTransition(atomAddListener, JsShapePropertyFlags.Open, out var addInfo);
        shape = shape.GetOrAddTransition(atomRemoveListener, JsShapePropertyFlags.Open, out var removeInfo);
        shape = shape.GetOrAddTransition(atomSetMaxListeners, JsShapePropertyFlags.Open, out var setMaxListenersInfo);
        shape = shape.GetOrAddTransition(atomGetMaxListeners, JsShapePropertyFlags.Open, out var getMaxListenersInfo);
        shape = shape.GetOrAddTransition(atomListeners, JsShapePropertyFlags.Open, out var listenersInfo);
        shape = shape.GetOrAddTransition(atomListenerCount, JsShapePropertyFlags.Open, out var listenerCountInfo);
        Debug.Assert(onInfo.Slot == PrototypeOnSlot);
        Debug.Assert(onceInfo.Slot == PrototypeOnceSlot);
        Debug.Assert(offInfo.Slot == PrototypeOffSlot);
        Debug.Assert(emitInfo.Slot == PrototypeEmitSlot);
        Debug.Assert(addInfo.Slot == PrototypeAddListenerSlot);
        Debug.Assert(removeInfo.Slot == PrototypeRemoveListenerSlot);
        Debug.Assert(setMaxListenersInfo.Slot == PrototypeSetMaxListenersSlot);
        Debug.Assert(getMaxListenersInfo.Slot == PrototypeGetMaxListenersSlot);
        Debug.Assert(listenersInfo.Slot == PrototypeListenersSlot);
        Debug.Assert(listenerCountInfo.Slot == PrototypeListenerCountSlot);
        return shape;
    }

    private void EnsureAtoms(JsRealm realm)
    {
        atomAddListener = EnsureAtom(realm, atomAddListener, "addListener");
        atomEmit = EnsureAtom(realm, atomEmit, "emit");
        atomEventEmitter = EnsureAtom(realm, atomEventEmitter, "EventEmitter");
        atomOff = EnsureAtom(realm, atomOff, "off");
        atomOn = EnsureAtom(realm, atomOn, "on");
        atomOnce = EnsureAtom(realm, atomOnce, "once");
        atomRemoveListener = EnsureAtom(realm, atomRemoveListener, "removeListener");
        atomSetMaxListeners = EnsureAtom(realm, atomSetMaxListeners, "setMaxListeners");
        atomGetMaxListeners = EnsureAtom(realm, atomGetMaxListeners, "getMaxListeners");
        atomListeners = EnsureAtom(realm, atomListeners, "listeners");
        atomListenerCount = EnsureAtom(realm, atomListenerCount, "listenerCount");
    }

    private static int EnsureAtom(JsRealm realm, int atom, string text)
    {
        return atom >= 0 ? atom : realm.Atoms.InternNoCheck(text);
    }

    private JsHostFunction CreateOnFunction(JsRealm realm)
    {
        return new(realm, "on", 2, (in info) =>
        {
            AddListener(info, false);
            return info.ThisValue;
        }, false);
    }

    private JsHostFunction CreateOnceFunction(JsRealm realm)
    {
        return new(realm, "once", 2, (in info) =>
        {
            AddListener(info, true);
            return info.ThisValue;
        }, false);
    }

    private JsHostFunction CreateAddListenerFunction(JsRealm realm)
    {
        return new(realm, "addListener", 2, (in info) =>
        {
            AddListener(info, false);
            return info.ThisValue;
        }, false);
    }

    private JsHostFunction CreateOffFunction(JsRealm realm)
    {
        return new(realm, "off", 2, (in info) =>
        {
            RemoveListener(info);
            return info.ThisValue;
        }, false);
    }

    private JsHostFunction CreateRemoveListenerFunction(JsRealm realm)
    {
        return new(realm, "removeListener", 2, (in info) =>
        {
            RemoveListener(info);
            return info.ThisValue;
        }, false);
    }

    private JsHostFunction CreateEmitFunction(JsRealm realm)
    {
        return new(realm, "emit", 1, (in info) =>
        {
            var emitter = GetEmitter(info);
            var eventName = GetEventName(info);
            if (!emitter.UserData!.Listeners.TryGetValue(eventName, out var listeners) || listeners.Count == 0)
                return JsValue.False;

            var snapshot = listeners.ToArray();
            var args = info.Arguments.Length <= 1
                ? []
                : info.Arguments[1..].ToArray();
            var invokedAny = false;
            for (var i = 0; i < snapshot.Length; i++)
            {
                var listener = snapshot[i];
                invokedAny = true;
                _ = info.Realm.Call(listener.Callback, info.ThisValue, args);
                if (listener.Once)
                    RemoveListenerEntry(emitter.UserData!.Listeners, eventName, listener.Callback);
            }

            return invokedAny ? JsValue.True : JsValue.False;
        }, false);
    }

    private JsHostFunction CreateSetMaxListenersFunction(JsRealm realm)
    {
        return new(realm, "setMaxListeners", 1, (in info) =>
        {
            var emitter = GetEmitter(info);
            var maxListeners = info.Arguments.Length == 0
                ? 10
                : info.GetArgument(0).IsNumber
                    ? info.GetArgument(0).NumberValue
                    : info.Realm.ToNumber(info.GetArgument(0));

            if (double.IsNaN(maxListeners) || maxListeners < 0)
                throw new JsRuntimeException(JsErrorKind.RangeError, "The value of \"n\" is out of range.");

            emitter.UserData!.MaxListeners = maxListeners;
            return info.ThisValue;
        }, false);
    }

    private JsHostFunction CreateGetMaxListenersFunction(JsRealm realm)
    {
        return new(realm, "getMaxListeners", 0, (in info) =>
        {
            var emitter = GetEmitter(info);
            return new(emitter.UserData!.MaxListeners);
        }, false);
    }

    private JsHostFunction CreateListenersFunction(JsRealm realm)
    {
        return new(realm, "listeners", 1, (in info) =>
        {
            var emitter = GetEmitter(info);
            var eventName = GetEventName(info);
            if (!emitter.UserData!.Listeners.TryGetValue(eventName, out var listeners) || listeners.Count == 0)
                return JsValue.FromObject(new JsArray(info.Realm));

            var result = new JsArray(info.Realm);
            var dense = result.InitializeDenseElementsNoCollision(listeners.Count);
            for (var i = 0; i < listeners.Count; i++)
                dense[i] = JsValue.FromObject(listeners[i].Callback);
            return JsValue.FromObject(result);
        }, false);
    }

    private JsHostFunction CreateListenerCountFunction(JsRealm realm)
    {
        return new(realm, "listenerCount", 1, (in info) =>
        {
            var emitter = GetEmitter(info);
            var eventName = GetEventName(info);
            var count = emitter.UserData!.Listeners.TryGetValue(eventName, out var listeners) ? listeners.Count : 0;
            return JsValue.FromInt32(count);
        }, false);
    }

    private void AddListener(in CallInfo info, bool once)
    {
        var emitter = GetEmitter(info);
        var eventName = GetEventName(info);
        var callbackValue = info.GetArgument(1);
        if (!callbackValue.TryGetObject(out var callbackObject) || callbackObject is not JsFunction callback)
            throw new JsRuntimeException(JsErrorKind.TypeError, "EventEmitter listener must be callable");

        ref var listeners =
            ref CollectionsMarshal.GetValueRefOrAddDefault(emitter.UserData!.Listeners, eventName, out _);
        listeners ??= [];
        listeners.Add(new(callback, once));
    }

    private void RemoveListener(in CallInfo info)
    {
        var emitter = GetEmitter(info);
        var eventName = GetEventName(info);
        var callbackValue = info.GetArgument(1);
        if (!callbackValue.TryGetObject(out var callbackObject) || callbackObject is not JsFunction callback)
            throw new JsRuntimeException(JsErrorKind.TypeError, "EventEmitter listener must be callable");

        RemoveListenerEntry(emitter.UserData!.Listeners, eventName, callback);
    }

    private static void RemoveListenerEntry(
        Dictionary<string, List<ListenerEntry>> listenersByEvent,
        string eventName,
        JsFunction callback)
    {
        if (!listenersByEvent.TryGetValue(eventName, out var listeners) || listeners.Count == 0)
            return;

        for (var i = listeners.Count - 1; i >= 0; i--)
            if (ReferenceEquals(listeners[i].Callback, callback))
                listeners.RemoveAt(i);

        if (listeners.Count == 0)
            listenersByEvent.Remove(eventName);
    }

    private JsUserDataObject<EventEmitterState> GetEmitter(in CallInfo info)
    {
        if (info.ThisValue.TryGetObject(out var thisObj) &&
            thisObj is JsUserDataObject<EventEmitterState> directEmitter &&
            directEmitter.UserData is not null)
            return directEmitter;

        if (info.ThisValue.TryGetObject(out thisObj) &&
            emitterStates.TryGetValue(thisObj, out var emitter) &&
            emitter.UserData is not null)
            return emitter;

        throw new JsRuntimeException(JsErrorKind.TypeError, "EventEmitter method called on incompatible receiver");
    }

    private static string GetEventName(in CallInfo info)
    {
        var eventValue = info.GetArgument(0);
        return eventValue.IsString ? eventValue.AsString() : info.Realm.ToJsStringSlowPath(eventValue);
    }

    internal sealed class EventEmitterState
    {
        public readonly Dictionary<string, List<ListenerEntry>> Listeners = new(StringComparer.Ordinal);
        public double MaxListeners { get; set; } = 10;
    }

    internal readonly record struct ListenerEntry(JsFunction Callback, bool Once);

    private sealed class ConstructorState(NodeEventsBuiltIn owner)
    {
        public NodeEventsBuiltIn Owner { get; } = owner;
    }
}
