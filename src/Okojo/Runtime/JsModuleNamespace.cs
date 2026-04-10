namespace Okojo.Runtime;

public sealed class JsModuleNamespace
{
    internal JsModuleNamespace(JsRealm realm, string resolvedId, JsObject namespaceObject)
    {
        Realm = realm;
        ResolvedId = resolvedId;
        Object = namespaceObject;
    }

    public string ResolvedId { get; }
    public JsRealm Realm { get; }

    public JsObject Object { get; }

    public bool TryGetExport(string name, out JsValue value)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Object.TryGetProperty(name, out value);
    }

    public JsValue GetExport(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!Object.TryGetProperty(name, out var value))
            throw new InvalidOperationException($"Module '{ResolvedId}' does not export '{name}'.");
        return value;
    }

    public JsFunction GetFunctionExport(string name)
    {
        var value = GetExport(name);
        if (!value.TryGetObject(out var obj) || obj is not JsFunction function)
            throw new InvalidOperationException($"Module '{ResolvedId}' export '{name}' is not a function.");
        return function;
    }

    public JsValue CallExport(string name, params ReadOnlySpan<JsValue> args)
    {
        var function = GetFunctionExport(name);
        return Realm.Call(function, JsValue.Undefined, args);
    }
}
