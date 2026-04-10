namespace Okojo.Runtime;

internal static class WorkerHandleFactory
{
    public static JsPlainObject CreateHandle(
        JsRealm ownerRealm,
        WorkerHostBinding binding,
        OkojoWorkerHandleAtoms atoms,
        Action<int> removeHandleByAgentId)
    {
        var runtimeData = new WorkerHandleRuntimeData
        {
            Binding = binding,
            RemoveHandleByAgentId = removeHandleByAgentId
        };

        var handle = new JsPlainObject(ownerRealm);
        handle.DefineDataPropertyAtom(ownerRealm, atoms.OnMessage, JsValue.Undefined, JsShapePropertyFlags.Writable);
        handle.DefineDataPropertyAtom(ownerRealm, atoms.OnMessageError, JsValue.Undefined,
            JsShapePropertyFlags.Writable);

        var postMessageFn = new JsHostFunction(ownerRealm, static (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            var data = (WorkerHandleRuntimeData)callee.UserData!;
            var payload = args.Length != 0
                ? realm.Engine.Options.HostServices.MessageSerializer.SerializeOutgoing(realm, args[0])
                : null;
            realm.Agent.PostMessage(data.Binding.Agent, payload,
                realm.Engine.Options.HostServices.WorkerMessageQueueKey);
            return JsValue.Undefined;
        }, "postMessage", 1)
        {
            UserData = runtimeData
        };

        var evalFn = new JsHostFunction(ownerRealm, static (in info) =>
        {
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            var data = (WorkerHandleRuntimeData)callee.UserData!;
            var source = args.Length != 0 && args[0].IsString ? args[0].AsString() : string.Empty;
            return data.Binding.Eval(source);
        }, "eval", 1)
        {
            UserData = runtimeData
        };

        var loadModuleFn = new JsHostFunction(ownerRealm, static (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            if (args.Length == 0 || !args[0].IsString)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "loadModule specifier must be a string",
                    "WORKER_MODULE_SPECIFIER_TYPE");

            var data = (WorkerHandleRuntimeData)callee.UserData!;
            return data.Binding.LoadModule(realm, args[0].AsString());
        }, "loadModule", 1)
        {
            UserData = runtimeData
        };

        var pumpFn = new JsHostFunction(ownerRealm, static (in info) =>
        {
            var realm = info.Realm;
            var callee = (JsHostFunction)info.Function;
            var data = (WorkerHandleRuntimeData)callee.UserData!;
            data.Binding.Pump(realm);
            return JsValue.Undefined;
        }, "pump", 0)
        {
            UserData = runtimeData
        };

        var terminateFn = new JsHostFunction(ownerRealm, static (in info) =>
        {
            var thisValue = info.ThisValue;
            var callee = (JsHostFunction)info.Function;
            var data = (WorkerHandleRuntimeData)callee.UserData!;
            data.Binding.Terminate();
            if (thisValue.TryGetObject(out var handleObj) && handleObj is JsPlainObject)
                data.RemoveHandleByAgentId(data.Binding.Agent.Id);
            return JsValue.Undefined;
        }, "terminate", 0)
        {
            UserData = runtimeData
        };

        handle.DefineDataPropertyAtom(ownerRealm, atoms.PostMessage, JsValue.FromObject(postMessageFn),
            JsShapePropertyFlags.Open);
        handle.DefineDataPropertyAtom(ownerRealm, atoms.Eval, JsValue.FromObject(evalFn), JsShapePropertyFlags.Open);
        handle.DefineDataPropertyAtom(ownerRealm, atoms.LoadModule, JsValue.FromObject(loadModuleFn),
            JsShapePropertyFlags.Open);
        handle.DefineDataPropertyAtom(ownerRealm, atoms.Pump, JsValue.FromObject(pumpFn), JsShapePropertyFlags.Open);
        handle.DefineDataPropertyAtom(ownerRealm, atoms.Terminate, JsValue.FromObject(terminateFn),
            JsShapePropertyFlags.Open);

        return handle;
    }

    private sealed class WorkerHandleRuntimeData
    {
        public required WorkerHostBinding Binding;
        public required Action<int> RemoveHandleByAgentId;
    }

    internal readonly record struct OkojoWorkerHandleAtoms(
        int OnMessage,
        int OnMessageError,
        int PostMessage,
        int Eval,
        int LoadModule,
        int Pump,
        int Terminate);
}
