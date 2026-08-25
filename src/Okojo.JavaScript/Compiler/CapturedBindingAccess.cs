namespace Okojo.JavaScript.Compiler;

internal readonly record struct CapturedBindingAccess(
    int Slot,
    int Depth,
    bool IsConst = false,
    bool IsImmutableFunctionName = false,
    bool IsModuleVariable = false,
    bool NeedsTdzWriteCheck = false
);
