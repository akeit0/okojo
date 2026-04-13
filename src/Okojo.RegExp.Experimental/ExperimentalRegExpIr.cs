using System.Text;

namespace Okojo.RegExp.Experimental;

internal enum ExperimentalRegExpIrOpcode : byte
{
    Match,
    ClearCaptures,
    SaveLoopPosition,
    BranchIfLoopUnchanged,
    SaveStart,
    SaveEnd,
    BackReference,
    BackReferenceIgnoreCase,
    NamedBackReference,
    NamedBackReferenceIgnoreCase,
    AssertLookahead,
    AssertNotLookahead,
    AssertLookbehind,
    AssertNotLookbehind,
    LiteralText,
    LiteralSet,
    LiteralSetIgnoreCase,
    Char,
    CharIgnoreCase,
    Dot,
    Any,
    AssertStart,
    AssertStartMultiline,
    AssertEnd,
    AssertEndMultiline,
    AssertWordBoundary,
    AssertWordBoundaryIgnoreCase,
    AssertNotWordBoundary,
    AssertNotWordBoundaryIgnoreCase,
    Class,
    ClassIgnoreCase,
    PropertyEscape,
    PropertyEscapeIgnoreCase,
    Jump,
    Split
}

internal readonly record struct ExperimentalRegExpIrInstruction(ExperimentalRegExpIrOpcode OpCode, int Operand,
    int Operand2 = 0);

internal sealed class ExperimentalRegExpIrProgram
{
    public required ExperimentalRegExpIrInstruction[] Instructions { get; init; }
    public int[][] CaptureClearSets { get; init; } = [];
    public int[][] NamedBackReferenceCaptureSets { get; init; } = [];
    public ExperimentalRegExpIrProgram[] LookaheadPrograms { get; init; } = [];
    public ScratchRegExpProgram.Node[] LookbehindNodes { get; init; } = [];
    public RegExpRuntimeFlags[] LookbehindFlags { get; init; } = [];
    public int LoopSlotCount { get; init; }
    public string[] LiteralTexts { get; init; } = [];
    public int[][] LiteralCodePointSets { get; init; } = [];
    public ScratchRegExpProgram.ClassNode[] Classes { get; init; } = [];
    public ScratchRegExpProgram.PropertyEscapeNode[] PropertyEscapes { get; init; } = [];
}

internal static class ExperimentalRegExpIrGenerator
{
    public static ExperimentalRegExpIrProgram? TryGenerate(ScratchRegExpProgram treeProgram)
    {
        return TryGenerate(treeProgram.Root, treeProgram.Flags, treeProgram.NodeCaptureIndices,
            treeProgram.NamedCaptureIndexes);
    }

    internal static ExperimentalRegExpIrProgram? TryGenerate(ScratchRegExpProgram.Node root, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, Dictionary<string, List<int>> namedCaptureIndexes)
    {
        var instructions = new List<ExperimentalRegExpIrInstruction>();
        var captureClearSets = new List<int[]>();
        var namedBackReferenceCaptureSets = new List<int[]>();
        var lookaheadPrograms = new List<ExperimentalRegExpIrProgram>();
        var lookbehindNodes = new List<ScratchRegExpProgram.Node>();
        var lookbehindFlags = new List<RegExpRuntimeFlags>();
        var loopSlots = new List<bool>();
        var literalTexts = new List<string>();
        var literalCodePointSets = new List<int[]>();
        var classes = new List<ScratchRegExpProgram.ClassNode>();
        var propertyEscapes = new List<ScratchRegExpProgram.PropertyEscapeNode>();
        if (!TryEmitNode(root, flags, nodeCaptureIndices, instructions, captureClearSets, namedBackReferenceCaptureSets,
                lookaheadPrograms, lookbehindNodes, lookbehindFlags, loopSlots, literalTexts, literalCodePointSets,
                classes, propertyEscapes, namedCaptureIndexes))
            return null;

        instructions.Add(new(ExperimentalRegExpIrOpcode.Match, 0));
        return new()
        {
            Instructions = instructions.ToArray(),
            CaptureClearSets = captureClearSets.ToArray(),
            NamedBackReferenceCaptureSets = namedBackReferenceCaptureSets.ToArray(),
            LookaheadPrograms = lookaheadPrograms.ToArray(),
            LookbehindNodes = lookbehindNodes.ToArray(),
            LookbehindFlags = lookbehindFlags.ToArray(),
            LoopSlotCount = loopSlots.Count,
            LiteralTexts = literalTexts.ToArray(),
            LiteralCodePointSets = literalCodePointSets.ToArray(),
            Classes = classes.ToArray(),
            PropertyEscapes = propertyEscapes.ToArray()
        };
    }

    private static bool TryEmitNode(ScratchRegExpProgram.Node node, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets,
        List<int[]> namedBackReferenceCaptureSets, List<ExperimentalRegExpIrProgram> lookaheadPrograms,
        List<ScratchRegExpProgram.Node> lookbehindNodes, List<RegExpRuntimeFlags> lookbehindFlags,
        List<bool> loopSlots,
        List<string> literalTexts, List<int[]> literalCodePointSets,
        List<ScratchRegExpProgram.ClassNode> classes, List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        switch (node)
        {
            case ScratchRegExpProgram.EmptyNode:
                return true;
            case ScratchRegExpProgram.SequenceNode sequence:
                return TryEmitSequence(sequence, flags, nodeCaptureIndices, instructions, captureClearSets,
                    namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes, lookbehindFlags, loopSlots,
                    literalTexts, literalCodePointSets, classes, propertyEscapes, namedCaptureIndexes);
            case ScratchRegExpProgram.AlternationNode alternation:
                return TryEmitAlternation(alternation.Alternatives, flags, nodeCaptureIndices, instructions,
                    captureClearSets, namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes,
                    lookbehindFlags, loopSlots, literalTexts, literalCodePointSets, classes, propertyEscapes,
                    namedCaptureIndexes);
            case ScratchRegExpProgram.CaptureNode capture:
                instructions.Add(new(ExperimentalRegExpIrOpcode.SaveStart, capture.Index));
                if (!TryEmitNode(capture.Child, flags, nodeCaptureIndices, instructions, captureClearSets,
                        namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes, lookbehindFlags,
                        loopSlots, literalTexts, literalCodePointSets, classes, propertyEscapes, namedCaptureIndexes))
                    return false;
                instructions.Add(new(ExperimentalRegExpIrOpcode.SaveEnd, capture.Index));
                return true;
            case ScratchRegExpProgram.BackReferenceNode backReference:
                instructions.Add(new(flags.IgnoreCase
                    ? ExperimentalRegExpIrOpcode.BackReferenceIgnoreCase
                    : ExperimentalRegExpIrOpcode.BackReference, backReference.Index));
                return true;
            case ScratchRegExpProgram.NamedBackReferenceNode namedBackReference:
                if (!namedCaptureIndexes.TryGetValue(namedBackReference.Name, out var captureIndexes))
                    return false;
                namedBackReferenceCaptureSets.Add(captureIndexes.ToArray());
                instructions.Add(new(flags.IgnoreCase
                        ? ExperimentalRegExpIrOpcode.NamedBackReferenceIgnoreCase
                        : ExperimentalRegExpIrOpcode.NamedBackReference,
                    namedBackReferenceCaptureSets.Count - 1));
                return true;
            case ScratchRegExpProgram.LookaheadNode lookahead:
            {
                var lookaheadProgram = TryGenerate(lookahead.Child, flags, nodeCaptureIndices, namedCaptureIndexes);
                if (lookaheadProgram is null)
                    return false;

                lookaheadPrograms.Add(lookaheadProgram);
                instructions.Add(new(lookahead.Positive
                    ? ExperimentalRegExpIrOpcode.AssertLookahead
                    : ExperimentalRegExpIrOpcode.AssertNotLookahead, lookaheadPrograms.Count - 1));
                return true;
            }
            case ScratchRegExpProgram.LookbehindNode lookbehind:
                lookbehindNodes.Add(lookbehind.Child);
                lookbehindFlags.Add(flags);
                instructions.Add(new(lookbehind.Positive
                    ? ExperimentalRegExpIrOpcode.AssertLookbehind
                    : ExperimentalRegExpIrOpcode.AssertNotLookbehind, lookbehindNodes.Count - 1));
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
                    ? (flags.IgnoreCase
                        ? ExperimentalRegExpIrOpcode.AssertWordBoundaryIgnoreCase
                        : ExperimentalRegExpIrOpcode.AssertWordBoundary)
                    : (flags.IgnoreCase
                        ? ExperimentalRegExpIrOpcode.AssertNotWordBoundaryIgnoreCase
                        : ExperimentalRegExpIrOpcode.AssertNotWordBoundary), 0));
                return true;
            case ScratchRegExpProgram.ClassNode cls:
                if (ScratchRegExpProgram.TryGetSingleLiteralClassCodePoint(cls, out var classCodePoint))
                {
                    instructions.Add(new(flags.IgnoreCase
                        ? ExperimentalRegExpIrOpcode.CharIgnoreCase
                        : ExperimentalRegExpIrOpcode.Char, classCodePoint));
                    return true;
                }

                if (ScratchRegExpProgram.TryGetSmallLiteralClassCodePoints(cls, out var classCodePoints))
                {
                    literalCodePointSets.Add(classCodePoints);
                    instructions.Add(new(flags.IgnoreCase
                        ? ExperimentalRegExpIrOpcode.LiteralSetIgnoreCase
                        : ExperimentalRegExpIrOpcode.LiteralSet, literalCodePointSets.Count - 1));
                    return true;
                }

                classes.Add(cls);
                instructions.Add(new(flags.IgnoreCase
                    ? ExperimentalRegExpIrOpcode.ClassIgnoreCase
                    : ExperimentalRegExpIrOpcode.Class, classes.Count - 1));
                return true;
            case ScratchRegExpProgram.PropertyEscapeNode propertyEscape:
                propertyEscapes.Add(propertyEscape);
                instructions.Add(new(flags.IgnoreCase
                    ? ExperimentalRegExpIrOpcode.PropertyEscapeIgnoreCase
                    : ExperimentalRegExpIrOpcode.PropertyEscape, propertyEscapes.Count - 1));
                return true;
            case ScratchRegExpProgram.QuantifierNode quantifier:
                return TryEmitQuantifier(quantifier, flags, nodeCaptureIndices, instructions, captureClearSets,
                    literalTexts, namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes, lookbehindFlags,
                    loopSlots, literalCodePointSets, classes, propertyEscapes, namedCaptureIndexes);
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                return TryEmitNode(scoped.Child, flags with
                {
                    IgnoreCase = scoped.IgnoreCase ?? flags.IgnoreCase,
                    Multiline = scoped.Multiline ?? flags.Multiline,
                    DotAll = scoped.DotAll ?? flags.DotAll
                }, nodeCaptureIndices, instructions, captureClearSets, namedBackReferenceCaptureSets,
                    lookaheadPrograms, lookbehindNodes, lookbehindFlags, loopSlots, literalTexts, literalCodePointSets,
                    classes,
                    propertyEscapes, namedCaptureIndexes);
            default:
                return false;
        }
    }

    private static bool TryEmitSequence(ScratchRegExpProgram.SequenceNode sequence, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets,
        List<int[]> namedBackReferenceCaptureSets, List<ExperimentalRegExpIrProgram> lookaheadPrograms,
        List<ScratchRegExpProgram.Node> lookbehindNodes, List<RegExpRuntimeFlags> lookbehindFlags,
        List<bool> loopSlots,
        List<string> literalTexts, List<int[]> literalCodePointSets,
        List<ScratchRegExpProgram.ClassNode> classes, List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes,
        Dictionary<string, List<int>> namedCaptureIndexes)
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

            if (!TryEmitNode(sequence.Terms[i], flags, nodeCaptureIndices, instructions, captureClearSets,
                    namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes, lookbehindFlags, loopSlots,
                    literalTexts, literalCodePointSets, classes, propertyEscapes, namedCaptureIndexes))
                return false;

            i++;
        }

        return true;
    }

    private static bool TryEmitAlternation(ScratchRegExpProgram.Node[] alternatives, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets,
        List<int[]> namedBackReferenceCaptureSets, List<ExperimentalRegExpIrProgram> lookaheadPrograms,
        List<ScratchRegExpProgram.Node> lookbehindNodes, List<RegExpRuntimeFlags> lookbehindFlags,
        List<bool> loopSlots,
        List<string> literalTexts, List<int[]> literalCodePointSets,
        List<ScratchRegExpProgram.ClassNode> classes, List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        if (alternatives.Length == 0)
            return true;
        if (alternatives.Length == 1)
            return TryEmitNode(alternatives[0], flags, nodeCaptureIndices, instructions, captureClearSets,
                namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes, lookbehindFlags, loopSlots,
                literalTexts, literalCodePointSets, classes, propertyEscapes, namedCaptureIndexes);

        List<int> endJumps = [];
        for (var i = 0; i < alternatives.Length - 1; i++)
        {
            var splitIndex = AddInstruction(instructions, ExperimentalRegExpIrOpcode.Split);
            var firstTarget = instructions.Count;
            if (!TryEmitNode(alternatives[i], flags, nodeCaptureIndices, instructions, captureClearSets,
                    namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes, lookbehindFlags, loopSlots,
                    literalTexts, literalCodePointSets, classes, propertyEscapes, namedCaptureIndexes))
                return false;

            endJumps.Add(AddInstruction(instructions, ExperimentalRegExpIrOpcode.Jump));
            var secondTarget = instructions.Count;
            PatchInstruction(instructions, splitIndex, firstTarget, secondTarget);
        }

        if (!TryEmitNode(alternatives[^1], flags, nodeCaptureIndices, instructions, captureClearSets,
                namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes, lookbehindFlags, loopSlots,
                literalTexts, literalCodePointSets, classes, propertyEscapes, namedCaptureIndexes))
            return false;

        var endTarget = instructions.Count;
        for (var i = 0; i < endJumps.Count; i++)
            PatchInstruction(instructions, endJumps[i], endTarget);

        return true;
    }

    private static bool TryEmitQuantifier(ScratchRegExpProgram.QuantifierNode quantifier, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets, List<string> literalTexts,
        List<int[]> namedBackReferenceCaptureSets, List<ExperimentalRegExpIrProgram> lookaheadPrograms,
        List<ScratchRegExpProgram.Node> lookbehindNodes, List<RegExpRuntimeFlags> lookbehindFlags,
        List<bool> loopSlots, List<int[]> literalCodePointSets,
        List<ScratchRegExpProgram.ClassNode> classes,
        List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        if (!ScratchRegExpProgram.TryGetNodeMinMatchLength(quantifier.Child, out var childMinLength))
            return false;

        var captureClearSet = GetCaptureClearSet(nodeCaptureIndices, quantifier.Child);
        for (var i = 0; i < quantifier.Min; i++)
            if (!TryEmitQuantifiedChild(quantifier.Child, flags, nodeCaptureIndices, captureClearSet, instructions,
                    captureClearSets, namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes,
                    lookbehindFlags, loopSlots, literalCodePointSets, literalTexts, classes, propertyEscapes,
                    namedCaptureIndexes))
                return false;

        if (quantifier.Max == quantifier.Min)
            return true;

        if (childMinLength == 0)
        {
            if (IsAlwaysZeroWidth(quantifier.Child))
                return true;

            if (quantifier.Max == int.MaxValue)
                return TryEmitProgressSensitiveStar(quantifier.Child, quantifier.Greedy, flags, nodeCaptureIndices,
                    captureClearSet, instructions, captureClearSets, namedBackReferenceCaptureSets, lookaheadPrograms,
                    lookbehindNodes, lookbehindFlags, loopSlots, literalCodePointSets, literalTexts, classes,
                    propertyEscapes,
                    namedCaptureIndexes);

            return TryEmitProgressSensitiveOptionals(quantifier.Child, quantifier.Max - quantifier.Min,
                quantifier.Greedy, flags, nodeCaptureIndices, captureClearSet, instructions, captureClearSets,
                namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes, lookbehindFlags, loopSlots,
                literalCodePointSets, literalTexts, classes, propertyEscapes, namedCaptureIndexes);
        }

        if (quantifier.Max == int.MaxValue)
            return TryEmitStar(quantifier.Child, quantifier.Greedy, flags, nodeCaptureIndices, captureClearSet,
                instructions, captureClearSets, namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes,
                lookbehindFlags, loopSlots, literalCodePointSets, literalTexts, classes, propertyEscapes,
                namedCaptureIndexes);

        var optionalCount = quantifier.Max - quantifier.Min;
        for (var i = 0; i < optionalCount; i++)
            if (!TryEmitOptional(quantifier.Child, quantifier.Greedy, flags, nodeCaptureIndices, captureClearSet,
                    instructions, captureClearSets, namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes,
                    lookbehindFlags, loopSlots, literalCodePointSets, literalTexts, classes, propertyEscapes,
                    namedCaptureIndexes))
                return false;

        return true;
    }

    private static bool TryEmitOptional(ScratchRegExpProgram.Node child, bool greedy, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, int[] captureClearSet,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets,
        List<int[]> namedBackReferenceCaptureSets, List<ExperimentalRegExpIrProgram> lookaheadPrograms,
        List<ScratchRegExpProgram.Node> lookbehindNodes, List<RegExpRuntimeFlags> lookbehindFlags,
        List<bool> loopSlots, List<int[]> literalCodePointSets,
        List<string> literalTexts,
        List<ScratchRegExpProgram.ClassNode> classes, List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        var splitIndex = AddInstruction(instructions, ExperimentalRegExpIrOpcode.Split);
        var childStart = instructions.Count;
        if (!TryEmitQuantifiedChild(child, flags, nodeCaptureIndices, captureClearSet, instructions, captureClearSets,
                namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes, lookbehindFlags, loopSlots,
                literalCodePointSets, literalTexts, classes, propertyEscapes, namedCaptureIndexes))
            return false;

        var after = instructions.Count;
        PatchInstruction(instructions, splitIndex,
            greedy ? childStart : after,
            greedy ? after : childStart);
        return true;
    }

    private static bool TryEmitStar(ScratchRegExpProgram.Node child, bool greedy, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, int[] captureClearSet,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets,
        List<int[]> namedBackReferenceCaptureSets, List<ExperimentalRegExpIrProgram> lookaheadPrograms,
        List<ScratchRegExpProgram.Node> lookbehindNodes, List<RegExpRuntimeFlags> lookbehindFlags,
        List<bool> loopSlots, List<int[]> literalCodePointSets,
        List<string> literalTexts,
        List<ScratchRegExpProgram.ClassNode> classes, List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        var loopHead = instructions.Count;
        var splitIndex = AddInstruction(instructions, ExperimentalRegExpIrOpcode.Split);
        var childStart = instructions.Count;
        if (!TryEmitQuantifiedChild(child, flags, nodeCaptureIndices, captureClearSet, instructions, captureClearSets,
                namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes, lookbehindFlags, loopSlots,
                literalCodePointSets, literalTexts, classes, propertyEscapes, namedCaptureIndexes))
            return false;

        instructions.Add(new(ExperimentalRegExpIrOpcode.Jump, loopHead));
        var after = instructions.Count;
        PatchInstruction(instructions, splitIndex,
            greedy ? childStart : after,
            greedy ? after : childStart);
        return true;
    }

    private static bool TryEmitProgressSensitiveOptionals(ScratchRegExpProgram.Node child, int optionalCount, bool greedy,
        RegExpRuntimeFlags flags, Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, int[] captureClearSet,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets,
        List<int[]> namedBackReferenceCaptureSets, List<ExperimentalRegExpIrProgram> lookaheadPrograms,
        List<ScratchRegExpProgram.Node> lookbehindNodes, List<RegExpRuntimeFlags> lookbehindFlags,
        List<bool> loopSlots, List<int[]> literalCodePointSets, List<string> literalTexts,
        List<ScratchRegExpProgram.ClassNode> classes,
        List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        if (optionalCount <= 0)
            return true;

        List<int> progressBranches = [];
        List<int> skipSplits = [];
        for (var i = 0; i < optionalCount; i++)
        {
            var loopSlot = AllocateLoopSlot(loopSlots);
            instructions.Add(new(ExperimentalRegExpIrOpcode.SaveLoopPosition, loopSlot));
            var splitIndex = AddInstruction(instructions, ExperimentalRegExpIrOpcode.Split);
            var childStart = instructions.Count;
            if (!TryEmitQuantifiedChild(child, flags, nodeCaptureIndices, captureClearSet, instructions, captureClearSets,
                    namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes, lookbehindFlags, loopSlots,
                    literalCodePointSets, literalTexts, classes, propertyEscapes, namedCaptureIndexes))
                return false;

            progressBranches.Add(AddInstruction(instructions, ExperimentalRegExpIrOpcode.BranchIfLoopUnchanged));
            skipSplits.Add(splitIndex);
            PatchInstruction(instructions, splitIndex,
                greedy ? childStart : 0,
                greedy ? 0 : childStart);
        }

        var afterAll = instructions.Count;
        for (var i = 0; i < progressBranches.Count; i++)
            PatchInstruction(instructions, progressBranches[i], instructions[progressBranches[i]].Operand, afterAll);
        for (var i = 0; i < skipSplits.Count; i++)
        {
            var split = instructions[skipSplits[i]];
            PatchInstruction(instructions, skipSplits[i],
                greedy ? split.Operand : afterAll,
                greedy ? afterAll : split.Operand2);
        }

        return true;
    }

    private static bool TryEmitProgressSensitiveStar(ScratchRegExpProgram.Node child, bool greedy,
        RegExpRuntimeFlags flags, Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, int[] captureClearSet,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets,
        List<int[]> namedBackReferenceCaptureSets, List<ExperimentalRegExpIrProgram> lookaheadPrograms,
        List<ScratchRegExpProgram.Node> lookbehindNodes, List<RegExpRuntimeFlags> lookbehindFlags,
        List<bool> loopSlots, List<int[]> literalCodePointSets, List<string> literalTexts,
        List<ScratchRegExpProgram.ClassNode> classes,
        List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        var loopHead = instructions.Count;
        var loopSlot = AllocateLoopSlot(loopSlots);
        instructions.Add(new(ExperimentalRegExpIrOpcode.SaveLoopPosition, loopSlot));
        var splitIndex = AddInstruction(instructions, ExperimentalRegExpIrOpcode.Split);
        var childStart = instructions.Count;
        if (!TryEmitQuantifiedChild(child, flags, nodeCaptureIndices, captureClearSet, instructions, captureClearSets,
                namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes, lookbehindFlags, loopSlots,
                literalCodePointSets, literalTexts, classes, propertyEscapes, namedCaptureIndexes))
            return false;

        var progressBranch = AddInstruction(instructions, ExperimentalRegExpIrOpcode.BranchIfLoopUnchanged);
        instructions.Add(new(ExperimentalRegExpIrOpcode.Jump, loopHead));
        var after = instructions.Count;
        PatchInstruction(instructions, splitIndex,
            greedy ? childStart : after,
            greedy ? after : childStart);
        PatchInstruction(instructions, progressBranch, loopSlot, after);
        return true;
    }

    private static bool TryEmitQuantifiedChild(ScratchRegExpProgram.Node child, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, int[] captureClearSet,
        List<ExperimentalRegExpIrInstruction> instructions, List<int[]> captureClearSets,
        List<int[]> namedBackReferenceCaptureSets, List<ExperimentalRegExpIrProgram> lookaheadPrograms,
        List<ScratchRegExpProgram.Node> lookbehindNodes, List<RegExpRuntimeFlags> lookbehindFlags,
        List<bool> loopSlots, List<int[]> literalCodePointSets,
        List<string> literalTexts,
        List<ScratchRegExpProgram.ClassNode> classes, List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        if (captureClearSet.Length != 0)
        {
            captureClearSets.Add(captureClearSet);
            instructions.Add(new(ExperimentalRegExpIrOpcode.ClearCaptures, captureClearSets.Count - 1));
        }

        return TryEmitNode(child, flags, nodeCaptureIndices, instructions, captureClearSets,
            namedBackReferenceCaptureSets, lookaheadPrograms, lookbehindNodes, lookbehindFlags, loopSlots,
            literalTexts, literalCodePointSets, classes, propertyEscapes, namedCaptureIndexes);
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

    private static int AllocateLoopSlot(List<bool> loopSlots)
    {
        loopSlots.Add(true);
        return loopSlots.Count - 1;
    }

    private static bool IsAlwaysZeroWidth(ScratchRegExpProgram.Node node)
    {
        switch (node)
        {
            case ScratchRegExpProgram.EmptyNode:
            case ScratchRegExpProgram.AnchorNode:
            case ScratchRegExpProgram.BoundaryNode:
            case ScratchRegExpProgram.LookaheadNode:
            case ScratchRegExpProgram.LookbehindNode:
                return true;
            case ScratchRegExpProgram.CaptureNode capture:
                return IsAlwaysZeroWidth(capture.Child);
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                return IsAlwaysZeroWidth(scoped.Child);
            case ScratchRegExpProgram.SequenceNode sequence:
                for (var i = 0; i < sequence.Terms.Length; i++)
                    if (!IsAlwaysZeroWidth(sequence.Terms[i]))
                        return false;
                return true;
            case ScratchRegExpProgram.AlternationNode alternation:
                for (var i = 0; i < alternation.Alternatives.Length; i++)
                    if (!IsAlwaysZeroWidth(alternation.Alternatives[i]))
                        return false;
                return true;
            case ScratchRegExpProgram.QuantifierNode quantifier:
                return quantifier.Max == 0 || IsAlwaysZeroWidth(quantifier.Child);
            default:
                return false;
        }
    }

    private static void PatchInstruction(List<ExperimentalRegExpIrInstruction> instructions, int index, int operand,
        int operand2 = 0)
    {
        instructions[index] = new(instructions[index].OpCode, operand, operand2);
    }

}
