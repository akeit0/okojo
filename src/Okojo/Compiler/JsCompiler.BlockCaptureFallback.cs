using Okojo.Parsing;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private void MarkBlockLexicalsCapturedByNestedFunctionFallback(JsBlockStatement block)
    {
        if (!block.BodyMayCreateNestedFunction)
            return;
        if (!nestedBlockLexicals.TryGetValue(block.Position, out var bindings) || bindings.Count == 0)
            return;

        for (var i = 0; i < bindings.Count; i++)
        {
            forcedAliasContextSlotSymbolIds.Add(bindings[i].InternalSymbolId);
            MarkCapturedByChildBinding(bindings[i].InternalSymbolId);
        }
    }
}
