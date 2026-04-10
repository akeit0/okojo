namespace Okojo.Runtime;

internal sealed class ExportNamespaceFromBindingResolved(string resolvedDependencyId, string exportedName)
{
    public string ResolvedDependencyId { get; } = resolvedDependencyId;
    public string ExportedName { get; } = exportedName;
}
