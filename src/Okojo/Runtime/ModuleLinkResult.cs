namespace Okojo.Runtime;

internal sealed class ModuleLinkResult(ModuleLinkPlan plan, IReadOnlyList<ModuleDiagnostic> diagnostics)
{
    public ModuleLinkPlan Plan { get; } = plan;
    public IReadOnlyList<ModuleDiagnostic> Diagnostics { get; } = diagnostics;
}
