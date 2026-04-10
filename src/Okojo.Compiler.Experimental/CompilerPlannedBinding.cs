namespace Okojo.Compiler.Experimental;

internal readonly record struct CompilerPlannedBinding(
    int ScopeId,
    string Name,
    int NameId,
    CompilerCollectedBindingKind Kind,
    CompilerPlannedStorageKind StorageKind,
    int StorageIndex,
    bool IsCaptured,
    bool IsConst,
    int Position);
