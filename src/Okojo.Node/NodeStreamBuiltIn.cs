using System.Diagnostics;
using System.Runtime.CompilerServices;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeStreamBuiltIn(NodeRuntime runtime, NodeEventsBuiltIn eventsBuiltIn)
{
    private const int ModuleReadableSlot = 0;
    private const int ModuleWritableSlot = 1;
    private const int ModuleDuplexSlot = 2;
    private const int ModuleTransformSlot = 3;
    private const int ModulePassThroughSlot = 4;
    private const int ModulePipelineSlot = 5;

    private const int InstanceReadableSlot = 0;
    private const int InstanceWritableSlot = 1;
    private const int InstanceWritableEndedSlot = 2;
    private const int InstanceDestroyedSlot = 3;

    private const int PrototypeWriteSlot = 0;
    private const int PrototypeEndSlot = 1;
    private const int PrototypePipeSlot = 2;
    private const int PrototypeDestroySlot = 3;

    private readonly ConditionalWeakTable<JsObject, StreamState> states = [];
    private int atomDestroy = -1;
    private int atomDestroyed = -1;
    private int atomDuplex = -1;
    private int atomEnd = -1;
    private int atomPassThrough = -1;
    private int atomPipe = -1;
    private int atomPipeline = -1;

    private int atomReadable = -1;
    private int atomReadableState = -1;
    private int atomTransform = -1;
    private int atomWritable = -1;
    private int atomWritableEnded = -1;
    private int atomWritableState = -1;
    private int atomWrite = -1;
    private JsHostFunction? duplexCtor;
    private StaticNamedPropertyLayout? instanceShape;
    private JsPlainObject? moduleObject;
    private StaticNamedPropertyLayout? moduleShape;
    private JsHostFunction? passThroughCtor;
    private StaticNamedPropertyLayout? prototypeShape;
    private JsHostFunction? readableCtor;
    private JsPlainObject? streamPrototype;
    private JsHostFunction? transformCtor;
    private JsHostFunction? writableCtor;

    public JsPlainObject GetModule()
    {
        if (moduleObject is not null)
            return moduleObject;

        var realm = runtime.MainRealm;
        var shape = moduleShape ??= CreateModuleShape(realm);
        var module = new JsPlainObject(shape);
        module.SetNamedSlotUnchecked(ModuleReadableSlot, JsValue.FromObject(GetReadableConstructor()));
        module.SetNamedSlotUnchecked(ModuleWritableSlot, JsValue.FromObject(GetWritableConstructor()));
        module.SetNamedSlotUnchecked(ModuleDuplexSlot, JsValue.FromObject(GetDuplexConstructor()));
        module.SetNamedSlotUnchecked(ModuleTransformSlot, JsValue.FromObject(GetTransformConstructor()));
        module.SetNamedSlotUnchecked(ModulePassThroughSlot, JsValue.FromObject(GetPassThroughConstructor()));
        module.SetNamedSlotUnchecked(ModulePipelineSlot, JsValue.FromObject(CreatePipelineFunction(realm)));
        moduleObject = module;
        return module;
    }

    private JsHostFunction GetReadableConstructor()
    {
        return readableCtor ??= CreateConstructor("Readable", true, false);
    }

    private JsHostFunction GetWritableConstructor()
    {
        return writableCtor ??= CreateConstructor("Writable", false, true);
    }

    private JsHostFunction GetDuplexConstructor()
    {
        return duplexCtor ??= CreateConstructor("Duplex", true, true);
    }

    private JsHostFunction GetTransformConstructor()
    {
        return transformCtor ??= CreateConstructor("Transform", true, true);
    }

    private JsHostFunction GetPassThroughConstructor()
    {
        return passThroughCtor ??= CreateConstructor("PassThrough", true, true);
    }

    private JsHostFunction CreateConstructor(string name, bool readable, bool writable)
    {
        var realm = runtime.MainRealm;
        var ctor = new JsHostFunction(realm, name, 0, static (in info) =>
        {
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Class constructor cannot be invoked without 'new'");

            var state = (ConstructorState)((JsHostFunction)info.Function).UserData!;
            return JsValue.FromObject(state.Owner.CreateStreamInstance(info.Realm, state.Readable, state.Writable));
        })
        {
            UserData = new ConstructorState(this, name, readable, writable)
        };
        ctor.DefineDataProperty("prototype", JsValue.FromObject(GetStreamPrototype()), JsShapePropertyFlags.Open);
        return ctor;
    }

    private JsUserDataObject<NodeEventsBuiltIn.EventEmitterState> CreateStreamInstance(
        JsRealm realm,
        bool readable,
        bool writable)
    {
        var shape = instanceShape ??= CreateInstanceShape(realm);
        var instance = new JsUserDataObject<NodeEventsBuiltIn.EventEmitterState>(shape, false);
        instance.UserData = new();
        instance.Prototype = GetStreamPrototype();
        var state = new StreamState(readable, writable);
        states.Add(instance, state);
        instance.SetNamedSlotUnchecked(InstanceReadableSlot, readable ? JsValue.True : JsValue.False);
        instance.SetNamedSlotUnchecked(InstanceWritableSlot, writable ? JsValue.True : JsValue.False);
        instance.SetNamedSlotUnchecked(InstanceWritableEndedSlot, JsValue.False);
        instance.SetNamedSlotUnchecked(InstanceDestroyedSlot, JsValue.False);
        return instance;
    }

    private JsPlainObject GetStreamPrototype()
    {
        if (streamPrototype is not null)
            return streamPrototype;

        var realm = runtime.MainRealm;
        var shape = prototypeShape ??= CreatePrototypeShape(realm);
        var prototype = new JsPlainObject(shape);
        prototype.Prototype = eventsBuiltIn.GetPrototypeObject();
        prototype.SetNamedSlotUnchecked(PrototypeWriteSlot, JsValue.FromObject(CreateWriteFunction(realm)));
        prototype.SetNamedSlotUnchecked(PrototypeEndSlot, JsValue.FromObject(CreateEndFunction(realm)));
        prototype.SetNamedSlotUnchecked(PrototypePipeSlot, JsValue.FromObject(CreatePipeFunction(realm)));
        prototype.SetNamedSlotUnchecked(PrototypeDestroySlot, JsValue.FromObject(CreateDestroyFunction(realm)));
        streamPrototype = prototype;
        return prototype;
    }

    private StaticNamedPropertyLayout CreateModuleShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomReadable, JsShapePropertyFlags.Open, out var readableInfo);
        shape = shape.GetOrAddTransition(atomWritable, JsShapePropertyFlags.Open, out var writableInfo);
        shape = shape.GetOrAddTransition(atomDuplex, JsShapePropertyFlags.Open, out var duplexInfo);
        shape = shape.GetOrAddTransition(atomTransform, JsShapePropertyFlags.Open, out var transformInfo);
        shape = shape.GetOrAddTransition(atomPassThrough, JsShapePropertyFlags.Open, out var passThroughInfo);
        shape = shape.GetOrAddTransition(atomPipeline, JsShapePropertyFlags.Open, out var pipelineInfo);
        Debug.Assert(readableInfo.Slot == ModuleReadableSlot);
        Debug.Assert(writableInfo.Slot == ModuleWritableSlot);
        Debug.Assert(duplexInfo.Slot == ModuleDuplexSlot);
        Debug.Assert(transformInfo.Slot == ModuleTransformSlot);
        Debug.Assert(passThroughInfo.Slot == ModulePassThroughSlot);
        Debug.Assert(pipelineInfo.Slot == ModulePipelineSlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreateInstanceShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomReadableState, JsShapePropertyFlags.Open,
            out var readableInfo);
        shape = shape.GetOrAddTransition(atomWritableState, JsShapePropertyFlags.Open, out var writableInfo);
        shape = shape.GetOrAddTransition(atomWritableEnded, JsShapePropertyFlags.Open, out var endedInfo);
        shape = shape.GetOrAddTransition(atomDestroyed, JsShapePropertyFlags.Open, out var destroyedInfo);
        Debug.Assert(readableInfo.Slot == InstanceReadableSlot);
        Debug.Assert(writableInfo.Slot == InstanceWritableSlot);
        Debug.Assert(endedInfo.Slot == InstanceWritableEndedSlot);
        Debug.Assert(destroyedInfo.Slot == InstanceDestroyedSlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreatePrototypeShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomWrite, JsShapePropertyFlags.Open, out var writeInfo);
        shape = shape.GetOrAddTransition(atomEnd, JsShapePropertyFlags.Open, out var endInfo);
        shape = shape.GetOrAddTransition(atomPipe, JsShapePropertyFlags.Open, out var pipeInfo);
        shape = shape.GetOrAddTransition(atomDestroy, JsShapePropertyFlags.Open, out var destroyInfo);
        Debug.Assert(writeInfo.Slot == PrototypeWriteSlot);
        Debug.Assert(endInfo.Slot == PrototypeEndSlot);
        Debug.Assert(pipeInfo.Slot == PrototypePipeSlot);
        Debug.Assert(destroyInfo.Slot == PrototypeDestroySlot);
        return shape;
    }

    private void EnsureAtoms(JsRealm realm)
    {
        atomReadable = EnsureAtom(realm, atomReadable, "Readable");
        atomWritable = EnsureAtom(realm, atomWritable, "Writable");
        atomDuplex = EnsureAtom(realm, atomDuplex, "Duplex");
        atomTransform = EnsureAtom(realm, atomTransform, "Transform");
        atomPassThrough = EnsureAtom(realm, atomPassThrough, "PassThrough");
        atomPipeline = EnsureAtom(realm, atomPipeline, "pipeline");
        atomWrite = EnsureAtom(realm, atomWrite, "write");
        atomEnd = EnsureAtom(realm, atomEnd, "end");
        atomPipe = EnsureAtom(realm, atomPipe, "pipe");
        atomDestroy = EnsureAtom(realm, atomDestroy, "destroy");
        atomReadableState = EnsureAtom(realm, atomReadableState, "readable");
        atomWritableState = EnsureAtom(realm, atomWritableState, "writable");
        atomWritableEnded = EnsureAtom(realm, atomWritableEnded, "writableEnded");
        atomDestroyed = EnsureAtom(realm, atomDestroyed, "destroyed");
    }

    private static int EnsureAtom(JsRealm realm, int atom, string text)
    {
        return atom >= 0 ? atom : realm.Atoms.InternNoCheck(text);
    }

    private JsHostFunction CreateWriteFunction(JsRealm realm)
    {
        return new(realm, "write", 1, (in info) =>
        {
            var stream = GetStreamObject(info);
            var state = states.GetValue(stream,
                static _ => throw new InvalidOperationException("Stream state missing."));
            if (!state.Writable || state.WritableEnded || state.Destroyed)
                return JsValue.False;

            var chunk = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
            Emit(stream, "data", [chunk]);
            return JsValue.True;
        }, false);
    }

    private JsHostFunction CreateEndFunction(JsRealm realm)
    {
        return new(realm, "end", 1, (in info) =>
        {
            var stream = GetStreamObject(info);
            var state = states.GetValue(stream,
                static _ => throw new InvalidOperationException("Stream state missing."));
            if (!state.Destroyed && info.Arguments.Length != 0)
            {
                var chunk = info.Arguments[0];
                Emit(stream, "data", [chunk]);
            }

            state.WritableEnded = true;
            stream.SetNamedSlotUnchecked(InstanceWritableEndedSlot, JsValue.True);
            Emit(stream, "end", []);
            Emit(stream, "finish", []);
            return info.ThisValue;
        }, false);
    }

    private JsHostFunction CreatePipeFunction(JsRealm realm)
    {
        return new(realm, "pipe", 1, (in info) =>
        {
            var source = GetStreamObject(info);
            var destValue = info.GetArgument(0);
            if (!destValue.TryGetObject(out var destObject))
                throw new JsRuntimeException(JsErrorKind.TypeError, "pipe destination must be an object");

            var destWrite = GetCallable(destObject, "write");
            var destEnd = GetCallable(destObject, "end");

            EmitBridge(source, "data", realm, destObject, destWrite, false);
            EmitBridge(source, "end", realm, destObject, destEnd, true);
            return destValue;
        }, false);
    }

    private JsHostFunction CreateDestroyFunction(JsRealm realm)
    {
        return new(realm, "destroy", 0, (in info) =>
        {
            var stream = GetStreamObject(info);
            var state = states.GetValue(stream,
                static _ => throw new InvalidOperationException("Stream state missing."));
            if (!state.Destroyed)
            {
                state.Destroyed = true;
                stream.SetNamedSlotUnchecked(InstanceDestroyedSlot, JsValue.True);
                Emit(stream, "close", []);
            }

            return info.ThisValue;
        }, false);
    }

    private JsHostFunction CreatePipelineFunction(JsRealm realm)
    {
        return new(realm, "pipeline", 0, (in info) =>
        {
            if (info.Arguments.Length < 2)
                throw new JsRuntimeException(JsErrorKind.TypeError, "pipeline requires at least two streams");

            var callbackIndex = -1;
            JsFunction? callback = null;
            if (info.Arguments[^1].TryGetObject(out var callbackObject) && callbackObject is JsFunction callbackFn)
            {
                callbackIndex = info.Arguments.Length - 1;
                callback = callbackFn;
            }

            var streamCount = callbackIndex >= 0 ? callbackIndex : info.Arguments.Length;
            for (var i = 0; i < streamCount - 1; i++)
            {
                if (!info.Arguments[i].TryGetObject(out var sourceObj))
                    throw new JsRuntimeException(JsErrorKind.TypeError, "pipeline source must be an object");
                var pipe = GetCallable(sourceObj, "pipe");
                _ = realm.Call(pipe, info.Arguments[i], info.Arguments[i + 1]);
            }

            if (callback is not null)
                _ = realm.Call(callback, JsValue.Undefined, JsValue.Undefined);

            return info.Arguments[streamCount - 1];
        }, false);
    }

    private void EmitBridge(
        JsObject source,
        string eventName,
        JsRealm realm,
        JsObject destObject,
        JsFunction targetFn,
        bool endOnTrigger)
    {
        var emitterPrototype = eventsBuiltIn.GetPrototypeObject();
        if (!emitterPrototype.TryGetProperty(eventName == "data" ? "on" : "once", out var hookValue) ||
            !hookValue.TryGetObject(out var hookObject) ||
            hookObject is not JsFunction hook)
            throw new InvalidOperationException("EventEmitter hook missing.");

        var bridge = new JsHostFunction(realm, endOnTrigger ? "pipelineEnd" : "pipelineWrite", 1,
            static (in bridgeInfo) =>
            {
                var data = (PipelineBridgeState)((JsHostFunction)bridgeInfo.Function).UserData!;
                if (data.EndOnTrigger)
                    _ = data.Realm.Call(data.Target, JsValue.FromObject(data.Destination));
                else
                    _ = data.Realm.Call(data.Target, JsValue.FromObject(data.Destination),
                        bridgeInfo.Arguments.Length == 0 ? [] : [bridgeInfo.Arguments[0]]);
                return JsValue.Undefined;
            }, false)
        {
            UserData = new PipelineBridgeState(realm, destObject, targetFn, endOnTrigger)
        };

        _ = realm.Call(hook, JsValue.FromObject(source), JsValue.FromString(eventName), JsValue.FromObject(bridge));
    }

    private static JsFunction GetCallable(JsObject obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value) ||
            !value.TryGetObject(out var fnObject) ||
            fnObject is not JsFunction fn)
            throw new JsRuntimeException(JsErrorKind.TypeError, $"Expected callable property '{propertyName}'");

        return fn;
    }

    private static JsUserDataObject<NodeEventsBuiltIn.EventEmitterState> GetStreamObject(in CallInfo info)
    {
        if (info.ThisValue.TryGetObject(out var thisObj) &&
            thisObj is JsUserDataObject<NodeEventsBuiltIn.EventEmitterState> stream &&
            stream.UserData is not null)
            return stream;

        throw new JsRuntimeException(JsErrorKind.TypeError, "Stream method called on incompatible receiver");
    }

    private static void Emit(JsUserDataObject<NodeEventsBuiltIn.EventEmitterState> stream, string eventName,
        ReadOnlySpan<JsValue> args)
    {
        if (!stream.TryGetProperty("emit", out var emitValue) ||
            !emitValue.TryGetObject(out var emitObject) ||
            emitObject is not JsFunction emit)
            throw new InvalidOperationException("EventEmitter.emit missing on stream object.");

        var callArgs = new JsValue[args.Length + 1];
        callArgs[0] = JsValue.FromString(eventName);
        for (var i = 0; i < args.Length; i++)
            callArgs[i + 1] = args[i];
        _ = stream.Realm.Call(emit, JsValue.FromObject(stream), callArgs);
    }

    private sealed class StreamState(bool readable, bool writable)
    {
        public bool Readable { get; } = readable;
        public bool Writable { get; } = writable;
        public bool WritableEnded { get; set; }
        public bool Destroyed { get; set; }
    }

    private sealed class ConstructorState(NodeStreamBuiltIn owner, string name, bool readable, bool writable)
    {
        public NodeStreamBuiltIn Owner { get; } = owner;
        public string Name { get; } = name;
        public bool Readable { get; } = readable;
        public bool Writable { get; } = writable;
    }

    private sealed class PipelineBridgeState(JsRealm realm, JsObject destination, JsFunction target, bool endOnTrigger)
    {
        public JsRealm Realm { get; } = realm;
        public JsObject Destination { get; } = destination;
        public JsFunction Target { get; } = target;
        public bool EndOnTrigger { get; } = endOnTrigger;
    }
}
