using Okojo.Compiler;

namespace Okojo.Runtime;

internal sealed class ModuleLinkPlan(
    ModuleExecutionPlan executionPlan,
    IReadOnlyList<string> requestedDependencyResolvedIds,
    IReadOnlyList<string> importDependencyResolvedIds,
    IReadOnlyList<JsResolvedImportBinding> resolvedImportBindings,
    IReadOnlyList<ExportFromBindingResolved> exportFromBindings,
    IReadOnlyList<ExportNamespaceFromBindingResolved> exportNamespaceFromBindings,
    IReadOnlyList<string> exportStarResolvedIds)
{
    public ModuleExecutionPlan ExecutionPlan { get; } = executionPlan;
    public IReadOnlyList<string> RequestedDependencyResolvedIds { get; } = requestedDependencyResolvedIds;
    public IReadOnlyList<string> ImportDependencyResolvedIds { get; } = importDependencyResolvedIds;
    public IReadOnlyList<JsResolvedImportBinding> ResolvedImportBindings { get; } = resolvedImportBindings;
    public IReadOnlyList<ExportFromBindingResolved> ExportFromBindings { get; } = exportFromBindings;

    public IReadOnlyList<ExportNamespaceFromBindingResolved> ExportNamespaceFromBindings { get; } =
        exportNamespaceFromBindings;

    public IReadOnlyList<string> ExportStarResolvedIds { get; } = exportStarResolvedIds;

    public Dictionary<string, ModuleImportBinding> BuildCompilerImportBindingMap()
    {
        var map = new Dictionary<string, ModuleImportBinding>(ResolvedImportBindings.Count, StringComparer.Ordinal);
        for (var i = 0; i < ResolvedImportBindings.Count; i++)
        {
            var binding = ResolvedImportBindings[i];
            map[binding.LocalName] = new(
                binding.ResolvedDependencyId,
                binding.ImportedName,
                binding.Kind == ModuleImportBindingKind.Namespace);
        }

        return map;
    }
}
