namespace Okojo.Runtime;

public sealed class JsModuleLoadResult
{
    internal JsModuleLoadResult(JsModuleNamespace moduleNamespace, JsValue completionValue, bool isCompleted)
    {
        Namespace = moduleNamespace;
        CompletionValue = completionValue;
        IsCompleted = isCompleted;
    }

    public string ResolvedId => Namespace.ResolvedId;
    public JsRealm Realm => Namespace.Realm;
    public JsModuleNamespace Namespace { get; }

    public JsObject Object => Namespace.Object;

    // Immediate namespace object for sync modules, or a promise that resolves to the namespace for TLA modules.
    public JsValue CompletionValue { get; }

    public bool IsCompleted { get; }

    public bool TryGetExport(string name, out JsValue value)
    {
        return Namespace.TryGetExport(name, out value);
    }

    public JsValue GetExport(string name)
    {
        return Namespace.GetExport(name);
    }

    public JsFunction GetFunctionExport(string name)
    {
        return Namespace.GetFunctionExport(name);
    }

    public JsValue CallExport(string name, params ReadOnlySpan<JsValue> args)
    {
        return Namespace.CallExport(name, args);
    }

    public async Task<JsModuleNamespace> ToTask(CancellationToken cancellationToken = default)
    {
        _ = await Realm.ToTask(CompletionValue, cancellationToken).ConfigureAwait(false);
        return Namespace;
    }
}
