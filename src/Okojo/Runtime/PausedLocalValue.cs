namespace Okojo.Runtime;

public readonly record struct PausedLocalValue(
    string Name,
    JsLocalDebugStorageKind StorageKind,
    int StorageIndex,
    JsValue Value,
    int StartPc,
    int EndPc,
    JsLocalDebugFlags Flags)
{
    public bool IsLiveAt(int pc)
    {
        return pc >= StartPc && pc < EndPc;
    }
}
