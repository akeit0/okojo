using System.Text;

namespace Okojo.RegExp.Experimental;

internal enum ExperimentalRegExpIrOpcode : byte
{
    Match,
    ClearCaptures,
    SaveStart,
    SaveEnd,
    LiteralText,
    Char,
    CharIgnoreCase,
    Dot,
    Any,
    AssertStart,
    AssertStartMultiline,
    AssertEnd,
    AssertEndMultiline,
    AssertWordBoundary,
    AssertNotWordBoundary,
    Class,
    PropertyEscape,
    Jump,
    Split
}

internal readonly record struct ExperimentalRegExpIrInstruction(ExperimentalRegExpIrOpcode OpCode, int Operand,
    int Operand2 = 0);

internal sealed class ExperimentalRegExpIrProgram
{
    public required ExperimentalRegExpIrInstruction[] Instructions { get; init; }
    public int[][] CaptureClearSets { get; init; } = [];
    public string[] LiteralTexts { get; init; } = [];
    public ScratchRegExpProgram.ClassNode[] Classes { get; init; } = [];
    public ScratchRegExpProgram.PropertyEscapeNode[] PropertyEscapes { get; init; } = [];
}

internal static class ExperimentalRegExpIrGenerator
{
    public static ExperimentalRegExpIrProgram? TryGenerate(ScratchRegExpProgram treeProgram)
    {
        var instructions = new List<ExperimentalRegExpIrInstruction>();
        var captureClearSets = new List<int[]>();
        var literalTexts = new List<string>();
        var classes = new List<ScratchRegExpProgram.ClassNode>();
        var propertyEscapes = new List<ScratchRegExpProgram.PropertyEscapeNode>();
        if (!TryEmitNode(treeProgram.Root, treeProgram.Flags, treeProgram.NodeCaptureIndices, instructions, captureClearSets,
                literalTexts, classes, propertyEscapes))
            return null;

        instructions.Add(new(ExperimentalRegExpIrOpcode.Match, 0));
        return new()
        {
            Instructions = instructions.ToArray(),
            CaptureClearSets = captureClearSets.ToArray(),
            LiteralTexts = literalTexts.ToArray(),
            Classes = classes.ToArray(),
            PropertyEscapes = propertyEscapes.ToArray()
        };
    }

    private static bool TryEmitNode(ScratchRegExpProgram.Node node, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets, List<string> literalTexts,
        List<ScratchRegExpProgram.ClassNode> classes, List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes)
    {
        switch (node)
        {
            case ScratchRegExpProgram.EmptyNode:
                return true;
            case ScratchRegExpProgram.SequenceNode sequence:
                return TryEmitSequence(sequence, flags, nodeCaptureIndices, instructions, captureClearSets, literalTexts,
                    classes, propertyEscapes);
            case ScratchRegExpProgram.AlternationNode alternation:
                return TryEmitAlternation(alternation.Alternatives, flags, nodeCaptureIndices, instructions, captureClearSets,
                    literalTexts, classes,
                    propertyEscapes);
            case ScratchRegExpProgram.CaptureNode capture:
                instructions.Add(new(ExperimentalRegExpIrOpcode.SaveStart, capture.Index));
                if (!TryEmitNode(capture.Child, flags, nodeCaptureIndices, instructions, captureClearSets, literalTexts,
                        classes, propertyEscapes))
                    return false;
                instructions.Add(new(ExperimentalRegExpIrOpcode.SaveEnd, capture.Index));
                return true;
            case ScratchRegExpProgram.LiteralNode literal:
                instructions.Add(new(flags.IgnoreCase
                    ? ExperimentalRegExpIrOpcode.CharIgnoreCase
                    : ExperimentalRegExpIrOpcode.Char, literal.CodePoint));
                return true;
            case ScratchRegExpProgram.DotNode:
                instructions.Add(new(flags.DotAll ? ExperimentalRegExpIrOpcode.Any : ExperimentalRegExpIrOpcode.Dot, 0));
                return true;
            case ScratchRegExpProgram.AnchorNode anchor:
                instructions.Add(new(anchor.Start
                    ? (flags.Multiline ? ExperimentalRegExpIrOpcode.AssertStartMultiline : ExperimentalRegExpIrOpcode.AssertStart)
                    : (flags.Multiline ? ExperimentalRegExpIrOpcode.AssertEndMultiline : ExperimentalRegExpIrOpcode.AssertEnd), 0));
                return true;
            case ScratchRegExpProgram.BoundaryNode boundary:
                instructions.Add(new(boundary.Positive
                    ? ExperimentalRegExpIrOpcode.AssertWordBoundary
                    : ExperimentalRegExpIrOpcode.AssertNotWordBoundary, 0));
                return true;
            case ScratchRegExpProgram.ClassNode cls:
                classes.Add(cls);
                instructions.Add(new(ExperimentalRegExpIrOpcode.Class, classes.Count - 1));
                return true;
            case ScratchRegExpProgram.PropertyEscapeNode propertyEscape:
                propertyEscapes.Add(propertyEscape);
                instructions.Add(new(ExperimentalRegExpIrOpcode.PropertyEscape, propertyEscapes.Count - 1));
                return true;
            case ScratchRegExpProgram.QuantifierNode quantifier:
                return TryEmitQuantifier(quantifier, flags, nodeCaptureIndices, instructions, captureClearSets, literalTexts,
                    classes, propertyEscapes);
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                return TryEmitNode(scoped.Child, flags with
                {
                    IgnoreCase = scoped.IgnoreCase ?? flags.IgnoreCase,
                    Multiline = scoped.Multiline ?? flags.Multiline,
                    DotAll = scoped.DotAll ?? flags.DotAll
                }, nodeCaptureIndices, instructions, captureClearSets, literalTexts, classes, propertyEscapes);
            default:
                return false;
        }
    }

    private static bool TryEmitSequence(ScratchRegExpProgram.SequenceNode sequence, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets, List<string> literalTexts,
        List<ScratchRegExpProgram.ClassNode> classes, List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes)
    {
        for (var i = 0; i < sequence.Terms.Length;)
        {
            if (!flags.IgnoreCase && sequence.Terms[i] is ScratchRegExpProgram.LiteralNode)
            {
                var runEnd = i;
                while (runEnd < sequence.Terms.Length && sequence.Terms[runEnd] is ScratchRegExpProgram.LiteralNode)
                    runEnd++;

                if (runEnd - i > 1)
                {
                    if (!TryBuildLiteralText(sequence.Terms, i, runEnd, out var literalText))
                        return false;

                    literalTexts.Add(literalText);
                    instructions.Add(new(ExperimentalRegExpIrOpcode.LiteralText, literalTexts.Count - 1));
                    i = runEnd;
                    continue;
                }
            }

            if (!TryEmitNode(sequence.Terms[i], flags, nodeCaptureIndices, instructions, captureClearSets, literalTexts,
                    classes, propertyEscapes))
                return false;

            i++;
        }

        return true;
    }

    private static bool TryEmitAlternation(ScratchRegExpProgram.Node[] alternatives, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets, List<string> literalTexts,
        List<ScratchRegExpProgram.ClassNode> classes, List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes)
    {
        if (alternatives.Length == 0)
            return true;
        if (alternatives.Length == 1)
            return TryEmitNode(alternatives[0], flags, nodeCaptureIndices, instructions, captureClearSets, literalTexts,
                classes, propertyEscapes);

        List<int> endJumps = [];
        for (var i = 0; i < alternatives.Length - 1; i++)
        {
            var splitIndex = AddInstruction(instructions, ExperimentalRegExpIrOpcode.Split);
            var firstTarget = instructions.Count;
            if (!TryEmitNode(alternatives[i], flags, nodeCaptureIndices, instructions, captureClearSets, literalTexts,
                    classes, propertyEscapes))
                return false;

            endJumps.Add(AddInstruction(instructions, ExperimentalRegExpIrOpcode.Jump));
            var secondTarget = instructions.Count;
            PatchInstruction(instructions, splitIndex, firstTarget, secondTarget);
        }

        if (!TryEmitNode(alternatives[^1], flags, nodeCaptureIndices, instructions, captureClearSets, literalTexts,
                classes, propertyEscapes))
            return false;

        var endTarget = instructions.Count;
        for (var i = 0; i < endJumps.Count; i++)
            PatchInstruction(instructions, endJumps[i], endTarget);

        return true;
    }

    private static bool TryEmitQuantifier(ScratchRegExpProgram.QuantifierNode quantifier, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets, List<string> literalTexts,
        List<ScratchRegExpProgram.ClassNode> classes, List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes)
    {
        if (!TryComputeMinMatchLength(quantifier.Child, out var childMinLength))
            return false;

        var captureClearSet = GetCaptureClearSet(nodeCaptureIndices, quantifier.Child);
        for (var i = 0; i < quantifier.Min; i++)
            if (!TryEmitQuantifiedChild(quantifier.Child, flags, nodeCaptureIndices, captureClearSet, instructions,
                    captureClearSets, literalTexts, classes, propertyEscapes))
                return false;

        if (quantifier.Max == quantifier.Min)
            return true;

        if (childMinLength == 0)
            return false;

        if (quantifier.Max == int.MaxValue)
            return TryEmitStar(quantifier.Child, quantifier.Greedy, flags, nodeCaptureIndices, captureClearSet,
                instructions, captureClearSets, literalTexts, classes, propertyEscapes);

        var optionalCount = quantifier.Max - quantifier.Min;
        for (var i = 0; i < optionalCount; i++)
            if (!TryEmitOptional(quantifier.Child, quantifier.Greedy, flags, nodeCaptureIndices, captureClearSet,
                    instructions, captureClearSets, literalTexts, classes, propertyEscapes))
                return false;

        return true;
    }

    private static bool TryEmitOptional(ScratchRegExpProgram.Node child, bool greedy, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, int[] captureClearSet,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets, List<string> literalTexts,
        List<ScratchRegExpProgram.ClassNode> classes, List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes)
    {
        var splitIndex = AddInstruction(instructions, ExperimentalRegExpIrOpcode.Split);
        var childStart = instructions.Count;
        if (!TryEmitQuantifiedChild(child, flags, nodeCaptureIndices, captureClearSet, instructions, captureClearSets,
                literalTexts, classes, propertyEscapes))
            return false;

        var after = instructions.Count;
        PatchInstruction(instructions, splitIndex,
            greedy ? childStart : after,
            greedy ? after : childStart);
        return true;
    }

    private static bool TryEmitStar(ScratchRegExpProgram.Node child, bool greedy, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, int[] captureClearSet,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets, List<string> literalTexts,
        List<ScratchRegExpProgram.ClassNode> classes, List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes)
    {
        var loopHead = instructions.Count;
        var splitIndex = AddInstruction(instructions, ExperimentalRegExpIrOpcode.Split);
        var childStart = instructions.Count;
        if (!TryEmitQuantifiedChild(child, flags, nodeCaptureIndices, captureClearSet, instructions, captureClearSets,
                literalTexts, classes, propertyEscapes))
            return false;

        instructions.Add(new(ExperimentalRegExpIrOpcode.Jump, loopHead));
        var after = instructions.Count;
        PatchInstruction(instructions, splitIndex,
            greedy ? childStart : after,
            greedy ? after : childStart);
        return true;
    }

    private static bool TryEmitQuantifiedChild(ScratchRegExpProgram.Node child, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, int[] captureClearSet,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets, List<string> literalTexts,
        List<ScratchRegExpProgram.ClassNode> classes, List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes)
    {
        if (captureClearSet.Length != 0)
        {
            captureClearSets.Add(captureClearSet);
            instructions.Add(new(ExperimentalRegExpIrOpcode.ClearCaptures, captureClearSets.Count - 1));
        }

        return TryEmitNode(child, flags, nodeCaptureIndices, instructions, captureClearSets, literalTexts, classes,
            propertyEscapes);
    }

    private static int[] GetCaptureClearSet(Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices,
        ScratchRegExpProgram.Node node)
    {
        return nodeCaptureIndices.TryGetValue(node, out var indices) ? indices : [];
    }

    private static bool TryBuildLiteralText(ScratchRegExpProgram.Node[] terms, int start, int end, out string text)
    {
        var builder = new StringBuilder();
        for (var i = start; i < end; i++)
        {
            if (terms[i] is not ScratchRegExpProgram.LiteralNode literal ||
                !Rune.TryCreate(literal.CodePoint, out var rune))
            {
                text = string.Empty;
                return false;
            }

            builder.Append(rune.ToString());
        }

        text = builder.ToString();
        return true;
    }

    private static int AddInstruction(List<ExperimentalRegExpIrInstruction> instructions, ExperimentalRegExpIrOpcode opcode)
    {
        instructions.Add(new(opcode, 0));
        return instructions.Count - 1;
    }

    private static void PatchInstruction(List<ExperimentalRegExpIrInstruction> instructions, int index, int operand,
        int operand2 = 0)
    {
        instructions[index] = new(instructions[index].OpCode, operand, operand2);
    }

    private static bool TryComputeMinMatchLength(ScratchRegExpProgram.Node node, out int minLength)
    {
        switch (node)
        {
            case ScratchRegExpProgram.EmptyNode:
            case ScratchRegExpProgram.AnchorNode:
            case ScratchRegExpProgram.BoundaryNode:
                minLength = 0;
                return true;
            case ScratchRegExpProgram.LiteralNode:
            case ScratchRegExpProgram.DotNode:
            case ScratchRegExpProgram.ClassNode:
            case ScratchRegExpProgram.PropertyEscapeNode:
                minLength = 1;
                return true;
            case ScratchRegExpProgram.CaptureNode capture:
                return TryComputeMinMatchLength(capture.Child, out minLength);
            case ScratchRegExpProgram.SequenceNode sequence:
            {
                long total = 0;
                for (var i = 0; i < sequence.Terms.Length; i++)
                {
                    if (!TryComputeMinMatchLength(sequence.Terms[i], out var termLength))
                    {
                        minLength = default;
                        return false;
                    }

                    total += termLength;
                    if (total > int.MaxValue)
                    {
                        minLength = int.MaxValue;
                        return true;
                    }
                }

                minLength = (int)total;
                return true;
            }
            case ScratchRegExpProgram.AlternationNode alternation:
            {
                var best = int.MaxValue;
                for (var i = 0; i < alternation.Alternatives.Length; i++)
                {
                    if (!TryComputeMinMatchLength(alternation.Alternatives[i], out var alternativeLength))
                    {
                        minLength = default;
                        return false;
                    }

                    if (alternativeLength < best)
                        best = alternativeLength;
                }

                minLength = best == int.MaxValue ? 0 : best;
                return true;
            }
            case ScratchRegExpProgram.QuantifierNode quantifier:
            {
                if (!TryComputeMinMatchLength(quantifier.Child, out var childLength))
                {
                    minLength = default;
                    return false;
                }

                var total = (long)childLength * quantifier.Min;
                minLength = total > int.MaxValue ? int.MaxValue : (int)total;
                return true;
            }
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                return TryComputeMinMatchLength(scoped.Child, out minLength);
            default:
                minLength = default;
                return false;
        }
    }
}
