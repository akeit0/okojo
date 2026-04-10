namespace Okojo.Runtime;

internal sealed class ExportFromBindingResolved(
    string resolvedDependencyId,
    string importedName,
    string exportedName,
    int position,
    string? importType = null)
{
    public string ResolvedDependencyId { get; } = resolvedDependencyId;
    public string ImportedName { get; } = importedName;
    public string ExportedName { get; } = exportedName;
    public int Position { get; } = position;
    public string? ImportType { get; } = importType;
}
