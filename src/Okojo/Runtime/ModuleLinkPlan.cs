using Okojo.Compiler;

namespace Okojo.Runtime;

internal sealed class ModuleLinkPlan(
    ModuleExecutionPlan executionPlan,
    IReadOnlyList<ResolvedModuleDependency> requestedDependencies,
    IReadOnlyList<string> importDependencyResolvedIds,
    IReadOnlyList<JsResolvedImportBinding> resolvedImportBindings,
    IReadOnlyList<ExportFromBindingResolved> exportFromBindings,
    IReadOnlyList<ExportNamespaceFromBindingResolved> exportNamespaceFromBindings,
    IReadOnlyList<string> exportStarResolvedIds)
{
    public ModuleExecutionPlan ExecutionPlan { get; } = executionPlan;
    public IReadOnlyList<ResolvedModuleDependency> RequestedDependencies { get; } = requestedDependencies;
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

internal sealed class ResolvedModuleDependency(string resolvedId, string? importType = null)
{
    public string ResolvedId { get; } = resolvedId;
    public string? ImportType { get; } = importType;
}
