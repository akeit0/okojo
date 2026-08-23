namespace Okojo.JavaScript.Compiler.Experimental;

internal readonly record struct CapturedBindingAccess(
    int Slot,
    int Depth,
    bool IsConst = false,
    bool IsImmutableFunctionName = false,
    bool IsModuleVariable = false
);
