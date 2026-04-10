namespace Okojo.SourceMaps;

public sealed class SourceMapModuleSourceLoader(IModuleSourceLoader inner, SourceMapScriptLoader sourceMapLoader)
    : IModuleSourceLoader
{
    public SourceMapModuleSourceLoader(IModuleSourceLoader inner, SourceMapRegistry registry)
        : this(inner, new SourceMapScriptLoader(registry))
    {
    }

    public string ResolveSpecifier(string specifier, string? referrer)
    {
        return inner.ResolveSpecifier(specifier, referrer);
    }

    public string LoadSource(string resolvedId)
    {
        var source = inner.LoadSource(resolvedId);
        sourceMapLoader.TryRegister(resolvedId, source);
        return source;
    }
}
