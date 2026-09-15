namespace Okojo.JavaScript.Execution;

// A realm owns the complete module map. Keeping the map off the agent's strong registry
// lets a released realm and its modules be collected together, including cyclic imports.
internal sealed class RealmModuleCache(IModuleSourceLoader loader)
{
    internal IModuleSourceLoader Loader { get; } = loader;
    internal ModuleGraph Graph { get; } = new(loader);
    internal ModuleLinker Linker { get; } = new(() => loader);
    internal Dictionary<string, string> Sources { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, JsModuleNamespaceObject> Json { get; } =
        new(StringComparer.Ordinal);
    internal Dictionary<string, JsModuleNamespaceObject> Text { get; } =
        new(StringComparer.Ordinal);

    internal void Clear()
    {
        Sources.Clear();
        Graph.Clear();
        Json.Clear();
        Text.Clear();
    }
}
