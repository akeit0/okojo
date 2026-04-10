namespace Okojo.Parsing;

internal sealed partial class JsParser
{
    private int nestedFunctionTrackingDepth;
    private ulong nestedFunctionTrackingMask;

    private JsStatement ParseStatementTrackingNestedFunctionSyntax(out bool sawNestedFunctionSyntax)
    {
        var marker = BeginNestedFunctionSyntaxTracking();
        try
        {
            return ParseStatement();
        }
        finally
        {
            sawNestedFunctionSyntax = EndNestedFunctionSyntaxTracking(marker);
        }
    }

    private int BeginNestedFunctionSyntaxTracking()
    {
        var depth = nestedFunctionTrackingDepth;
        nestedFunctionTrackingDepth = depth + 1;
        if (depth < sizeof(ulong) * 8)
            nestedFunctionTrackingMask &= ~(1UL << depth);
        return depth;
    }

    private bool EndNestedFunctionSyntaxTracking(int depth)
    {
        var sawNestedFunctionSyntax = depth >= sizeof(ulong) * 8
            ? nestedFunctionTrackingMask != 0
            : (nestedFunctionTrackingMask & (1UL << depth)) != 0;

        if (depth < sizeof(ulong) * 8)
            nestedFunctionTrackingMask &= ~(1UL << depth);
        nestedFunctionTrackingDepth = depth;
        if (sawNestedFunctionSyntax && depth > 0 && depth - 1 < sizeof(ulong) * 8)
            nestedFunctionTrackingMask |= 1UL << (depth - 1);

        return sawNestedFunctionSyntax;
    }

    private void MarkNestedFunctionSyntaxSeen()
    {
        if (nestedFunctionTrackingDepth == 0)
            return;

        var bitIndex = nestedFunctionTrackingDepth - 1;
        if (bitIndex < sizeof(ulong) * 8)
            nestedFunctionTrackingMask |= 1UL << bitIndex;
    }
}
