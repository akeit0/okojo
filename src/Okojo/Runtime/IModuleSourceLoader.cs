namespace Okojo.Runtime;

public interface IModuleSourceLoader
{
    string ResolveSpecifier(string specifier, string? referrer);
    ModuleLoadResult LoadModule(string resolvedId) => ModuleLoadResult.SourceText(LoadSource(resolvedId));
    string LoadSource(string resolvedId);
}
