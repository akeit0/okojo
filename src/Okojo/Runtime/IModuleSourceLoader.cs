namespace Okojo.Runtime;

public interface IModuleSourceLoader
{
    string ResolveSpecifier(string specifier, string? referrer);
    string LoadSource(string resolvedId);
}
