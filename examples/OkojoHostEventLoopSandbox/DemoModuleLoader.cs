using Okojo.Runtime;

namespace OkojoHostEventLoopSandbox;

internal sealed class DemoModuleLoader(SandboxAssets assets) : IModuleSourceLoader
{
    public string ResolveSpecifier(string specifier, string? referrer)
    {
        if (specifier.StartsWith("/", StringComparison.Ordinal))
            return specifier;

        if (referrer is null)
            return "/" + specifier.TrimStart('/');

        var slash = referrer.LastIndexOf('/');
        var baseDir = slash >= 0 ? referrer[..(slash + 1)] : "/";
        if (specifier.StartsWith("./", StringComparison.Ordinal))
            return baseDir + specifier[2..];

        return baseDir + specifier;
    }

    public string LoadSource(string resolvedSpecifier)
    {
        return assets.ReadScript(resolvedSpecifier);
    }
}
