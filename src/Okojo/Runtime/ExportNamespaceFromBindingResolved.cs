namespace Okojo.Runtime;

internal sealed class ExportNamespaceFromBindingResolved(
    string resolvedDependencyId,
    string exportedName,
    string? importType = null)
{
    public string ResolvedDependencyId { get; } = resolvedDependencyId;
    public string ExportedName { get; } = exportedName;
    public string? ImportType { get; } = importType;
}
