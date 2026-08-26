namespace Okojo.JavaScript.Compiler;

internal readonly record struct CompilerCollectedReference(
    int ScopeId,
    int NameId,
    int ExcludedBodyScopeId = -1
);
