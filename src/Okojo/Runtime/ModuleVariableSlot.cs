namespace Okojo.Runtime;

internal sealed class ModuleVariableSlot(
    ModuleVariableSlotKind kind,
    string? resolvedDependencyId = null,
    string? importedName = null,
    bool isReadOnly = false)
{
    public ModuleVariableSlotKind Kind { get; } = kind;
    public string? ResolvedDependencyId { get; } = resolvedDependencyId;
    public string? ImportedName { get; } = importedName;
    public bool IsReadOnly { get; } = isReadOnly;
    public JsValue LocalValue { get; set; } = JsValue.TheHole;
}
