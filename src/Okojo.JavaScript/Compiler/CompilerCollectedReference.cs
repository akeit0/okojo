namespace Okojo.JavaScript.Compiler;

internal readonly record struct CompilerCollectedReference(
    int ScopeId,
    string Name,
    int Position = 0,
    int ExcludedBodyScopeId = -1
);
