namespace Okojo.Runtime;

internal sealed class JsResolvedImportBinding(
    string localName,
    ModuleImportBindingKind kind,
    string resolvedDependencyId,
    string importedName,
    int position,
    string? importType = null)
{
    public string LocalName { get; } = localName;
    public ModuleImportBindingKind Kind { get; } = kind;
    public string ResolvedDependencyId { get; } = resolvedDependencyId;
    public string ImportedName { get; } = importedName;
    public int Position { get; } = position;
    public string? ImportType { get; } = importType;
}
