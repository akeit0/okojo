namespace Okojo.Runtime;

public readonly record struct PausedScopeSnapshot(
    int FramePointer,
    StackFrameInfo FrameInfo,
    IReadOnlyList<JsLocalDebugInfo>? Locals,
    IReadOnlyList<PausedLocalValue>? LocalValues)
{
    public bool TryGetLocalValue(string name, out PausedLocalValue value)
    {
        var locals = LocalValues;
        if (locals is not null)
            for (var i = 0; i < locals.Count; i++)
                if (string.Equals(locals[i].Name, name, StringComparison.Ordinal))
                {
                    value = locals[i];
                    return true;
                }

        value = default;
        return false;
    }
}
