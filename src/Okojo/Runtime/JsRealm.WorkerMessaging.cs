using System.Diagnostics;

namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    private const int MessageEventDataSlot = 0;
    private readonly Dictionary<int, JsPlainObject> workerHandlesByAgentId = new();
    private StaticNamedPropertyLayout? messageEventShape;
    private string? currentWorkerScriptResolvedId;
    private bool workerMessageDispatchHookInstalled;
    private bool workerMessagingGlobalsInstalled;

    internal void EnsureWorkerMessageDispatchHook()
    {
        if (workerMessageDispatchHookInstalled)
            return;

        workerMessageDispatchHookInstalled = true;
        Agent.MessageReceived += (sender, payload) => DispatchMessageEvent(sender, payload);
    }

    internal void InstallWorkerMessagingGlobals(bool includePostMessage)
    {
        EnsureWorkerMessageDispatchHook();

        if (workerMessagingGlobalsInstalled)
            return;
        workerMessagingGlobalsInstalled = true;

        Global["onmessage"] = JsValue.Undefined;
        Global["onmessageerror"] = JsValue.Undefined;

        if (includePostMessage)
        {
            // Worker-side postMessage: sends to parent/main agent.
            Global["postMessage"] = JsValue.FromObject(new JsHostFunction(this, static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                var target = realm.Agent.ParentAgent;
                if (target is null)
                    throw new JsRuntimeException(JsErrorKind.TypeError,
                        "postMessage target is not available for this realm", "POSTMESSAGE_TARGET_UNAVAILABLE");

                var payload = args.Length != 0
                    ? realm.Engine.Options.HostServices.MessageSerializer.SerializeOutgoing(realm, args[0])
                    : null;
                realm.Agent.PostMessage(target, payload, realm.Engine.Options.HostServices.WorkerMessageQueueKey);
                return JsValue.Undefined;
            }, "postMessage", 1));

            Global["importScripts"] = JsValue.FromObject(new JsHostFunction(this, static (in info) =>
            {
                var realm = info.Realm;
                foreach (var arg in info.Arguments)
                {
                    var specifier = arg.IsString ? arg.AsString() : arg.ToString();
                    var referrer = realm.GetCurrentWorkerScriptResolvedIdOrNull()
                                   ?? realm.GetCurrentModuleResolvedIdOrNull();
                    var resolved = realm.Engine.ResolveWorkerScript(specifier, referrer);
                    var source = realm.Engine.LoadResolvedWorkerScript(resolved);
                    realm.ExecuteWorkerScript(source, resolved);
                }

                return JsValue.Undefined;
            }, "importScripts", 1));
        }
    }

    internal string? GetCurrentWorkerScriptResolvedIdOrNull()
    {
        return currentWorkerScriptResolvedId;
    }

    private void DispatchMessageEvent(JsAgent sender, object? payload)
    {
        // Worker realm accepts messages only from its parent agent.
        if (Agent.ParentAgent is not null && !ReferenceEquals(sender, Agent.ParentAgent))
            return;

        if (TryDispatchToWorkerHandle(sender, payload))
            return;

        DispatchGlobalMessageEvent(payload, false);
    }

    private bool TryDispatchToWorkerHandle(JsAgent sender, object? payload)
    {
        if (!workerHandlesByAgentId.TryGetValue(sender.Id, out var workerHandle))
            return false;

        var payloadValue = JsValue.Undefined;
        try
        {
            payloadValue = Engine.Options.HostServices.MessageSerializer.DeserializeIncoming(this, payload);
        }
        catch (Exception)
        {
            return DispatchWorkerHandleMessageEvent(workerHandle, payload, true);
        }

        if (!workerHandle.TryGetPropertyAtom(this, IdOnmessage, out var handler, out _))
            return false;
        if (!handler.TryGetObject(out var handlerObj) || handlerObj is not JsFunction fn)
            return false;

        var evt = CreateMessageEvent(payloadValue);
        Span<JsValue> args = [JsValue.FromObject(evt)];
        try
        {
            _ = InvokeFunction(fn, JsValue.FromObject(workerHandle), args);
        }
        catch (JsRuntimeException)
        {
            // Async message handler errors are host-observable only via existing error hooks.
        }

        return true;
    }

    private bool DispatchWorkerHandleMessageEvent(JsPlainObject workerHandle, object? payload, bool isError)
    {
        var handlerAtom = isError ? IdOnmessageerror : IdOnmessage;
        if (!workerHandle.TryGetPropertyAtom(this, handlerAtom, out var handler, out _))
            return false;
        if (!handler.TryGetObject(out var handlerObj) || handlerObj is not JsFunction fn)
            return false;

        var dataValue = JsValue.Undefined;
        try
        {
            dataValue = Engine.Options.HostServices.MessageSerializer.DeserializeIncoming(this, payload);
        }
        catch (Exception)
        {
            dataValue = JsValue.Undefined;
        }

        var evt = CreateMessageEvent(dataValue);
        Span<JsValue> args = [JsValue.FromObject(evt)];
        try
        {
            _ = InvokeFunction(fn, JsValue.FromObject(workerHandle), args);
        }
        catch (JsRuntimeException)
        {
            // Async message handler errors are host-observable only via existing error hooks.
        }

        return true;
    }

    private void DispatchGlobalMessageEvent(object? payload, bool isError)
    {
        if (!isError)
        {
            JsValue dataValue;
            try
            {
                dataValue = Engine.Options.HostServices.MessageSerializer.DeserializeIncoming(this, payload);
            }
            catch (Exception)
            {
                DispatchGlobalMessageEvent(payload, true);
                return;
            }

            if (!GlobalObject.TryGetPropertyAtom(this, IdOnmessage, out var onMessageHandler, out _))
                return;
            if (!onMessageHandler.TryGetObject(out var onMessageObj) || onMessageObj is not JsFunction onMessageFn)
                return;

            var msgEvt = CreateMessageEvent(dataValue);
            Span<JsValue> msgArgs = [JsValue.FromObject(msgEvt)];
            try
            {
                _ = InvokeFunction(onMessageFn, JsValue.FromObject(GlobalObject), msgArgs);
            }
            catch (JsRuntimeException)
            {
                // Async message handler errors are host-observable only via existing error hooks.
            }

            return;
        }

        if (!GlobalObject.TryGetPropertyAtom(this, IdOnmessageerror, out var handler, out _))
            return;
        if (!handler.TryGetObject(out var handlerObj) || handlerObj is not JsFunction fn)
            return;

        var evt = CreateMessageEvent(JsValue.Undefined);
        Span<JsValue> args = [JsValue.FromObject(evt)];
        try
        {
            _ = InvokeFunction(fn, JsValue.FromObject(GlobalObject), args);
        }
        catch (JsRuntimeException)
        {
            // Async message handler errors are host-observable only via existing error hooks.
        }
    }

    internal JsPlainObject CreateWorkerHandleObject(string? scriptEntry, WorkerScriptType scriptType)
    {
        var workerBinding = Engine.Options.HostServices.WorkerHost.CreateWorker(
            this,
            scriptEntry,
            GetCurrentWorkerScriptResolvedIdOrNull() ?? GetCurrentModuleResolvedIdOrNull(),
            scriptType);
        var handle = WorkerHandleFactory.CreateHandle(this, workerBinding,
            new(
                IdOnmessage,
                IdOnmessageerror,
                IdPostMessage,
                IdEval,
                IdLoadModule,
                IdPump,
                IdTerminate),
            RemoveWorkerHandleByAgentId);

        workerHandlesByAgentId[workerBinding.Agent.Id] = handle;

        return handle;
    }

    private void RemoveWorkerHandleByAgentId(int agentId)
    {
        workerHandlesByAgentId.Remove(agentId);
    }

    private JsPlainObject CreateMessageEvent(in JsValue dataValue)
    {
        var shape = messageEventShape ??= CreateMessageEventShape();
        var evt = new JsPlainObject(shape);
        evt.SetNamedSlotUnchecked(MessageEventDataSlot, dataValue);
        return evt;
    }

    private StaticNamedPropertyLayout CreateMessageEventShape()
    {
        var shape = EmptyShape.GetOrAddTransition(IdData, JsShapePropertyFlags.Open, out var dataInfo);
        Debug.Assert(dataInfo.Slot == MessageEventDataSlot);
        return shape;
    }

    private JsValue BridgeIntoThisRealm(in JsValue sourceValue, Dictionary<JsObject, JsValue>? visited = null)
    {
        if (sourceValue.IsUndefined || sourceValue.IsNull || sourceValue.IsBool || sourceValue.IsInt32 ||
            sourceValue.IsFloat64 || sourceValue.IsString || sourceValue.IsSymbol)
            return sourceValue;

        if (!sourceValue.TryGetObject(out var sourceObj))
            return sourceValue;

        var sourceRealm = sourceObj.Realm;
        if (ReferenceEquals(sourceRealm, this))
            return sourceValue;

        visited ??= new(ReferenceEqualityComparer.Instance);
        if (visited.TryGetValue(sourceObj, out var existing))
            return existing;

        if (sourceObj is JsFunction sourceFn)
        {
            var proxy = new JsHostFunction(this, static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                var callee = (JsHostFunction)info.Function;
                var data = (CrossRealmFunctionProxyData)callee.UserData!;
                var forwarded = args.Length == 0 ? [] : new JsValue[args.Length];
                for (var i = 0; i < args.Length; i++)
                    forwarded[i] = data.SourceRealm.BridgeIntoThisRealm(args[i]);

                var result = data.SourceRealm.InvokeFunction(data.SourceFunction, JsValue.Undefined, forwarded);
                return realm.BridgeIntoThisRealm(result);
            }, sourceFn.Name ?? string.Empty, sourceFn.Length)
            {
                UserData = new CrossRealmFunctionProxyData(sourceRealm, sourceFn)
            };

            var bridgedFn = JsValue.FromObject(proxy);
            visited[sourceObj] = bridgedFn;
            return bridgedFn;
        }

        if (sourceObj is JsArray sourceArray)
        {
            var targetArray = CreateArrayObject();
            var bridged = JsValue.FromObject(targetArray);
            visited[sourceObj] = bridged;
            for (uint i = 0; i < sourceArray.Length; i++)
            {
                if (!sourceArray.TryGetElement(i, out var item))
                    continue;
                targetArray.SetElement(i, BridgeIntoThisRealm(item, visited));
            }

            return bridged;
        }

        var targetObj = new JsPlainObject(this);
        var bridgedObj = JsValue.FromObject(targetObj);
        visited[sourceObj] = bridgedObj;
        var namedAtoms = RentScratchList<int>();
        try
        {
            sourceObj.CollectOwnNamedPropertyAtoms(sourceRealm, namedAtoms, false);
            foreach (var sourceAtom in namedAtoms)
            {
                if (sourceAtom < 0)
                    continue;
                if (!sourceObj.TryGetPropertyAtom(sourceRealm, sourceAtom, out var value, out _))
                    continue;

                var key = sourceRealm.Atoms.AtomToString(sourceAtom);
                if (TryGetArrayIndexFromCanonicalString(key, out var idx))
                {
                    targetObj.SetElement(idx, BridgeIntoThisRealm(value, visited));
                }
                else
                {
                    var targetAtom = Atoms.InternNoCheck(key);
                    targetObj.DefineDataPropertyAtom(this, targetAtom, BridgeIntoThisRealm(value, visited),
                        JsShapePropertyFlags.Open);
                }
            }
        }
        finally
        {
            ReturnScratchList(namedAtoms);
        }

        return bridgedObj;
    }

    internal JsValue BridgeFromOtherRealm(in JsValue sourceValue)
    {
        return BridgeIntoThisRealm(sourceValue);
    }

    private sealed class CrossRealmFunctionProxyData(JsRealm sourceRealm, JsFunction sourceFunction)
    {
        public JsRealm SourceRealm { get; } = sourceRealm;
        public JsFunction SourceFunction { get; } = sourceFunction;
    }
}
