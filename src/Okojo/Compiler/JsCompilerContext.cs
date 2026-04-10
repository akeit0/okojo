namespace Okojo.Compiler;

public sealed record ModuleImportBinding(string ResolvedDependencyId, string ImportedName, bool IsNamespace);

public readonly record struct ModuleVariableBinding(sbyte CellIndex, byte Depth, bool IsReadOnly);

public sealed class JsCompilerContext
{
    public bool IsRepl { get; init; }
    public bool IsIndirectEval { get; init; }
    public bool IsStrictIndirectEval { get; init; }
    public ISet<string>? ReplTopLevelLexicalNames { get; init; }
    public ISet<string>? ReplTopLevelConstNames { get; init; }
}
