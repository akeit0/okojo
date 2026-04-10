using System.Buffers;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private int HideNonParameterLocalsForInitializerScope(out KeyValuePair<int, int>[]? hiddenLocals)
    {
        hiddenLocals = null;
        if (!functionHasParameterExpressions || locals.Count == 0)
            return 0;

        hiddenLocals = ArrayPool<KeyValuePair<int, int>>.Shared.Rent(locals.Count);
        var hiddenCount = 0;
        foreach (var entry in locals)
        {
            if (IsParameterLocalBinding(entry.Key) ||
                IsImmutableFunctionNameBinding(entry.Key) ||
                entry.Key == SyntheticArgumentsSymbolId)
                continue;

            hiddenLocals[hiddenCount++] = entry;
        }

        for (var i = 0; i < hiddenCount; i++) locals.Remove(hiddenLocals[i].Key);

        return hiddenCount;
    }

    private void RestoreHiddenLocalsAfterInitializerScope(KeyValuePair<int, int>[]? hiddenLocals, int hiddenCount)
    {
        if (hiddenLocals is null)
            return;

        try
        {
            for (var i = 0; i < hiddenCount; i++)
            {
                var entry = hiddenLocals[i];
                locals[entry.Key] = entry.Value;
                hiddenLocals[i] = default;
            }
        }
        finally
        {
            ArrayPool<KeyValuePair<int, int>>.Shared.Return(hiddenLocals);
        }
    }
}
