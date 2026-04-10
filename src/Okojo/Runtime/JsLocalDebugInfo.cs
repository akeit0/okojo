namespace Okojo.Runtime;

[Flags]
public enum JsLocalDebugFlags : byte
{
    None = 0,
    Lexical = 1 << 0,
    Var = 1 << 1,
    Parameter = 1 << 2,
    Const = 1 << 3,
    CapturedByChild = 1 << 4,
    ImmutableFunctionName = 1 << 5
}

public enum JsLocalDebugStorageKind : byte
{
    Register = 0,
    ContextSlot = 1
}

public readonly record struct JsLocalDebugInfo(
    string Name,
    JsLocalDebugStorageKind StorageKind,
    int StorageIndex,
    int StartPc,
    int EndPc,
    JsLocalDebugFlags Flags)
{
    public bool IsLiveAt(int pc)
    {
        return pc >= StartPc && pc < EndPc;
    }
}
