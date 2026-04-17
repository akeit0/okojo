namespace Okojo.Runtime;

internal sealed class ModuleExecutionPlan(
    IReadOnlyList<ModuleExecutionOp> operations,
    IReadOnlyDictionary<string, string> exportLocalByName,
    IReadOnlySet<string> preinitializedLocalExportNames,
    bool requiresTopLevelAwait,
    bool hasTopLevelUsingLike,
    bool hasTopLevelAwaitUsingLike)
{
    public IReadOnlyList<ModuleExecutionOp> Operations { get; } = operations;
    public IReadOnlyDictionary<string, string> ExportLocalByName { get; } = exportLocalByName;
    public IReadOnlySet<string> PreinitializedLocalExportNames { get; } = preinitializedLocalExportNames;
    public bool RequiresTopLevelAwait { get; } = requiresTopLevelAwait;
    public bool HasTopLevelUsingLike { get; } = hasTopLevelUsingLike;
    public bool HasTopLevelAwaitUsingLike { get; } = hasTopLevelAwaitUsingLike;
}
