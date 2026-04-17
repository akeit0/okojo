using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeConfiguredModuleSourceLoader(
    NodeModuleSourceLoader nodeLoader,
    IModuleSourceLoader topLevelLoader)
    : IModuleSourceLoader
{
    public string ResolveSpecifier(string specifier, string? referrer)
    {
        return topLevelLoader.ResolveSpecifier(specifier, referrer);
    }

    public string LoadSource(string resolvedId)
    {
        return topLevelLoader.LoadSource(resolvedId);
    }

    public string LoadRawSource(string resolvedId)
    {
        return nodeLoader.LoadRawSource(resolvedId);
    }

    public NodeModuleFormat DetermineFormat(string resolvedId)
    {
        return nodeLoader.DetermineFormat(resolvedId);
    }

    public void TryRegisterSourceMap(string resolvedId, string source)
    {
        nodeLoader.TryRegisterSourceMap(resolvedId, source);
    }
}
