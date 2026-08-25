namespace Okojo.JavaScript.Compiler.Experimental;

internal readonly record struct CompilerCollectedScope(
    int ScopeId,
    int ParentScopeId,
    CompilerCollectedScopeKind Kind,
    int Position = 0,
    bool IsArrow = false
);
