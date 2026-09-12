namespace Okojo.JavaScript.Compiler;

internal readonly record struct CompilerCollectedBinding(
    int ScopeId,
    CompilerCollectedBindingKind Kind,
    string Name,
    int NameId = -1,
    bool IsConst = false,
    int Position = 0,
    bool IsSynthetic = false
);
