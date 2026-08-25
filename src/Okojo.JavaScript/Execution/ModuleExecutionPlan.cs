namespace Okojo.JavaScript.Execution;

internal sealed class ModuleExecutionPlan(
    IReadOnlyDictionary<string, string> exportLocalByName,
    IReadOnlySet<string> preinitializedLocalExportNames,
    IReadOnlySet<string> defaultNameEligibleLocalNames,
    bool requiresTopLevelAwait,
    bool hasTopLevelUsingLike,
    bool hasTopLevelAwaitUsingLike
)
{
    public IReadOnlyDictionary<string, string> ExportLocalByName { get; } = exportLocalByName;
    public IReadOnlySet<string> PreinitializedLocalExportNames { get; } =
        preinitializedLocalExportNames;
    public IReadOnlySet<string> DefaultNameEligibleLocalNames { get; } =
        defaultNameEligibleLocalNames;
    public bool RequiresTopLevelAwait { get; } = requiresTopLevelAwait;
    public bool HasTopLevelUsingLike { get; } = hasTopLevelUsingLike;
    public bool HasTopLevelAwaitUsingLike { get; } = hasTopLevelAwaitUsingLike;
}
