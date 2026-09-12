namespace Okojo.JavaScript.Compiler;

public sealed record ModuleImportBinding(
    string ResolvedDependencyId,
    string ImportedName,
    bool IsNamespace
);

public readonly record struct ModuleVariableBinding(sbyte CellIndex, byte Depth, bool IsReadOnly);
