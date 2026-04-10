namespace Okojo.Compiler.Experimental;

internal readonly record struct CompilerCollectedReference(
    int ScopeId,
    string Name,
    int Position = 0);
