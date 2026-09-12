using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.DotNet.Modules;

internal sealed class DotNetModuleSourceLoader(
    IModuleSourceLoader inner,
    DotNetModuleImportBridge bridge
) : IModuleSourceLoader
{
    public string ResolveSpecifier(string specifier, string? referrer)
    {
        if (DotNetModuleSpecifier.TryResolve(specifier, referrer, out var resolvedId))
            return resolvedId;

        return inner.ResolveSpecifier(specifier, referrer);
    }

    public string LoadSource(string resolvedId)
    {
        return DotNetModuleSpecifier.IsDotNetResolvedId(resolvedId)
            ? bridge.CreateModuleSource(resolvedId)
            : inner.LoadSource(resolvedId);
    }
}
