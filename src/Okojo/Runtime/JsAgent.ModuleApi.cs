namespace Okojo.Runtime;

public sealed partial class JsAgent
{
    public JsAgentModuleApi Modules => field ??= new(this);

    internal JsModuleLoadResult LoadModuleResult(JsRealm realm, string specifier, string? referrer = null)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var resolvedId = ResolveModuleSpecifierOrThrow(specifier, referrer);
        var value = EvaluateModule(realm, specifier, referrer, false);
        if (!value.TryGetObject(out var ns))
            throw new InvalidOperationException("Module namespace object was not returned.");

        var moduleNamespace = new JsModuleNamespace(realm, resolvedId, ns);
        var isCompleted = !TryGetPendingModuleEvaluationPromise(specifier, referrer, out var pendingPromise);
        var completionValue = isCompleted
            ? value
            : CreateModuleCompletionValue(realm, pendingPromise, value);
        return new(moduleNamespace, completionValue, isCompleted);
    }

    private static JsValue CreateModuleCompletionValue(JsRealm realm, JsPromiseObject pendingPromise,
        JsValue namespaceValue)
    {
        var capability = realm.CreatePromiseCapability(realm.PromiseConstructor);
        var onFulfilled = new JsHostFunction(realm, (in info) =>
        {
            info.Realm.ResolvePromiseCapability(capability, namespaceValue);
            return JsValue.Undefined;
        }, string.Empty, 0);
        var onRejected = new JsHostFunction(realm, (in info) =>
        {
            var reason = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
            info.Realm.RejectPromiseCapability(capability, reason);
            return JsValue.Undefined;
        }, string.Empty, 1);
        _ = realm.PromiseThen(
            pendingPromise,
            JsValue.FromObject(onFulfilled),
            JsValue.FromObject(onRejected));
        return JsValue.FromObject(capability.Promise);
    }

    public sealed class JsAgentModuleApi
    {
        public enum ModuleStateKind
        {
            Uninitialized = 0,
            Instantiating = 1,
            Evaluating = 2,
            Evaluated = 3,
            Failed = 4
        }

        private readonly JsAgent agent;

        internal JsAgentModuleApi(JsAgent agent)
        {
            this.agent = agent;
        }

        public string Resolve(string specifier, string? referrer = null)
        {
            return agent.ResolveModuleSpecifierOrThrow(specifier, referrer);
        }

        public JsValue Evaluate(string specifier, string? referrer = null)
        {
            return agent.EvaluateModule(agent.MainRealm, specifier, referrer);
        }

        public JsValue Evaluate(JsRealm realm, string specifier, string? referrer = null)
        {
            return agent.EvaluateModule(realm, specifier, referrer);
        }

        public JsObject EvaluateNamespace(string specifier, string? referrer = null)
        {
            var value = Evaluate(specifier, referrer);
            if (!value.TryGetObject(out var ns))
                throw new InvalidOperationException("Module namespace object was not returned.");
            return ns;
        }

        public JsObject EvaluateNamespace(JsRealm realm, string specifier, string? referrer = null)
        {
            var value = Evaluate(realm, specifier, referrer);
            if (!value.TryGetObject(out var ns))
                throw new InvalidOperationException("Module namespace object was not returned.");
            return ns;
        }

        public bool TryGetCachedNamespace(string resolvedId, out JsValue namespaceValue)
        {
            return agent.TryGetCachedModuleNamespaceByResolvedId(resolvedId, out namespaceValue);
        }

        public ModuleStateSnapshot GetState(string resolvedId, bool includeError = false)
        {
            return agent.GetModuleStateSnapshotByResolvedId(resolvedId, includeError);
        }

        public bool Invalidate(string resolvedId)
        {
            return agent.InvalidateModuleByResolvedId(resolvedId);
        }

        public void Clear()
        {
            agent.ClearModuleCaches();
        }

        // Current architecture does parse/cache during link entry; full instantiate-only link API will
        // be separated when module linker/runtime lifecycle split is complete.
        public string Link(string specifier, string? referrer = null)
        {
            return agent.LinkModule(agent.MainRealm, specifier, referrer).ResolvedId;
        }

        public string Link(JsRealm realm, string specifier, string? referrer = null)
        {
            return agent.LinkModule(realm, specifier, referrer).ResolvedId;
        }

        public readonly record struct ModuleStateSnapshot(
            string ResolvedId,
            bool Exists,
            ModuleStateKind State,
            bool HasLinkPlan,
            bool HasSourceCache,
            ModuleErrorSnapshot? LastError);

        public readonly record struct ModuleErrorSnapshot(
            string? DetailCode,
            string Message,
            string ExceptionType,
            string? InnerExceptionType);
    }
}
