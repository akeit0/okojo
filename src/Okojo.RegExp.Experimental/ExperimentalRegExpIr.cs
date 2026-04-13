using Okojo.Internals;
using Okojo.Parsing;
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
    private const int MaxUnrolledQuantifierCount = 4096;

    public static ExperimentalRegExpIrProgram? TryGenerate(ScratchRegExpProgram treeProgram)
    {
        return TryGenerate(treeProgram.Root, treeProgram.Flags, treeProgram.NodeCaptureIndices,
            treeProgram.NamedCaptureIndexes);
    }

    internal static ExperimentalRegExpIrProgram? TryGenerate(ScratchRegExpProgram.Node root, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, Dictionary<string, List<int>> namedCaptureIndexes)
    {
        using var state = new IrBuildState();
        if (!TryEmitNode(root, flags, nodeCaptureIndices, state, namedCaptureIndexes))
            return null;

        state.AddInstruction(ExperimentalRegExpIrOpcode.Match);
        return state.BuildProgram();
    }

    private sealed class IrBuildState : IDisposable
    {
        public ScratchPooledList<ExperimentalRegExpIrInstruction> Instructions { get; } = new();
        public ScratchPooledList<int[]> CaptureClearSets { get; } = new();
        public ScratchPooledList<int[]> NamedBackReferenceCaptureSets { get; } = new();
        public ScratchPooledList<ExperimentalRegExpIrProgram> LookaheadPrograms { get; } = new();
        public ScratchPooledList<ScratchRegExpProgram.Node> LookbehindNodes { get; } = new();
        public ScratchPooledList<RegExpRuntimeFlags> LookbehindFlags { get; } = new();
        public ScratchPooledList<string> LiteralTexts { get; } = new();
        public ScratchPooledList<int[]> LiteralCodePointSets { get; } = new();
        public ScratchPooledList<ScratchRegExpProgram.ClassNode> Classes { get; } = new();
        public ScratchPooledList<ScratchRegExpProgram.PropertyEscapeNode> PropertyEscapes { get; } = new();

        public int LoopSlotCount { get; private set; }

        public int AddInstruction(ExperimentalRegExpIrOpcode opcode, int operand = 0, int operand2 = 0)
        {
            Instructions.Add(new(opcode, operand, operand2));
            return Instructions.Count - 1;
        }

        public void PatchInstruction(int index, int operand, int operand2 = 0)
        {
            Instructions[index] = new(Instructions[index].OpCode, operand, operand2);
        }

        public int AddCaptureClearSet(int[] captureClearSet)
        {
            CaptureClearSets.Add(captureClearSet);
            return CaptureClearSets.Count - 1;
        }

        public int AddNamedBackReferenceCaptureSet(int[] captureIndexes)
        {
            NamedBackReferenceCaptureSets.Add(captureIndexes);
            return NamedBackReferenceCaptureSets.Count - 1;
        }

        public int AddLookaheadProgram(ExperimentalRegExpIrProgram lookaheadProgram)
        {
            LookaheadPrograms.Add(lookaheadProgram);
            return LookaheadPrograms.Count - 1;
        }

        public int AddLookbehind(ScratchRegExpProgram.Node lookbehindNode, RegExpRuntimeFlags flags)
        {
            LookbehindNodes.Add(lookbehindNode);
            LookbehindFlags.Add(flags);
            return LookbehindNodes.Count - 1;
        }

        public int AddLiteralText(string literalText)
        {
            LiteralTexts.Add(literalText);
            return LiteralTexts.Count - 1;
        }

        public int AddLiteralCodePointSet(int[] literalCodePoints)
        {
            LiteralCodePointSets.Add(literalCodePoints);
            return LiteralCodePointSets.Count - 1;
        }

        public int AddClass(ScratchRegExpProgram.ClassNode cls)
        {
            Classes.Add(cls);
            return Classes.Count - 1;
        }

        public int AddPropertyEscape(ScratchRegExpProgram.PropertyEscapeNode propertyEscape)
        {
            PropertyEscapes.Add(propertyEscape);
            return PropertyEscapes.Count - 1;
        }

        public int AllocateLoopSlot()
        {
            return LoopSlotCount++;
        }

        public ExperimentalRegExpIrProgram BuildProgram()
        {
            return new()
            {
                Instructions = Instructions.ToArray(),
                CaptureClearSets = CaptureClearSets.ToArray(),
                NamedBackReferenceCaptureSets = NamedBackReferenceCaptureSets.ToArray(),
                LookaheadPrograms = LookaheadPrograms.ToArray(),
                LookbehindNodes = LookbehindNodes.ToArray(),
                LookbehindFlags = LookbehindFlags.ToArray(),
                LoopSlotCount = LoopSlotCount,
                LiteralTexts = LiteralTexts.ToArray(),
                LiteralCodePointSets = LiteralCodePointSets.ToArray(),
                Classes = Classes.ToArray(),
                PropertyEscapes = PropertyEscapes.ToArray()
            };
        }

        public void Dispose()
        {
            Instructions.Dispose();
            CaptureClearSets.Dispose();
            NamedBackReferenceCaptureSets.Dispose();
            LookaheadPrograms.Dispose();
            LookbehindNodes.Dispose();
            LookbehindFlags.Dispose();
            LiteralTexts.Dispose();
            LiteralCodePointSets.Dispose();
            Classes.Dispose();
            PropertyEscapes.Dispose();
        }
    }

    private static bool TryEmitNode(ScratchRegExpProgram.Node node, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, IrBuildState state,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        switch (node)
        {
            case ScratchRegExpProgram.EmptyNode:
                return true;
            case ScratchRegExpProgram.SequenceNode sequence:
                return TryEmitSequence(sequence, flags, nodeCaptureIndices, state, namedCaptureIndexes);
            case ScratchRegExpProgram.AlternationNode alternation:
                return TryEmitAlternation(alternation.Alternatives, flags, nodeCaptureIndices, state,
                    namedCaptureIndexes);
            case ScratchRegExpProgram.CaptureNode capture:
                state.AddInstruction(ExperimentalRegExpIrOpcode.SaveStart, capture.Index);
                if (!TryEmitNode(capture.Child, flags, nodeCaptureIndices, state, namedCaptureIndexes))
                    return false;
                state.AddInstruction(ExperimentalRegExpIrOpcode.SaveEnd, capture.Index);
                return true;
            case ScratchRegExpProgram.BackReferenceNode backReference:
                state.AddInstruction(flags.IgnoreCase
                    ? ExperimentalRegExpIrOpcode.BackReferenceIgnoreCase
                    : ExperimentalRegExpIrOpcode.BackReference, backReference.Index);
                return true;
            case ScratchRegExpProgram.NamedBackReferenceNode namedBackReference:
                if (!namedCaptureIndexes.TryGetValue(namedBackReference.Name, out var captureIndexes))
                    return false;

                state.AddInstruction(flags.IgnoreCase
                        ? ExperimentalRegExpIrOpcode.NamedBackReferenceIgnoreCase
                        : ExperimentalRegExpIrOpcode.NamedBackReference,
                    state.AddNamedBackReferenceCaptureSet(captureIndexes.ToArray()));
                return true;
            case ScratchRegExpProgram.LookaheadNode lookahead:
            {
                var lookaheadProgram = TryGenerate(lookahead.Child, flags, nodeCaptureIndices, namedCaptureIndexes);
                if (lookaheadProgram is null)
                    return false;

                state.AddInstruction(lookahead.Positive
                        ? ExperimentalRegExpIrOpcode.AssertLookahead
                        : ExperimentalRegExpIrOpcode.AssertNotLookahead,
                    state.AddLookaheadProgram(lookaheadProgram));
                return true;
            }
            case ScratchRegExpProgram.LookbehindNode lookbehind:
                state.AddInstruction(lookbehind.Positive
                        ? ExperimentalRegExpIrOpcode.AssertLookbehind
                        : ExperimentalRegExpIrOpcode.AssertNotLookbehind,
                    state.AddLookbehind(lookbehind.Child, flags));
                return true;
            case ScratchRegExpProgram.LiteralNode literal:
                state.AddInstruction(flags.IgnoreCase
                    ? ExperimentalRegExpIrOpcode.CharIgnoreCase
                    : ExperimentalRegExpIrOpcode.Char, literal.CodePoint);
                return true;
            case ScratchRegExpProgram.DotNode:
                state.AddInstruction(flags.DotAll ? ExperimentalRegExpIrOpcode.Any : ExperimentalRegExpIrOpcode.Dot);
                return true;
            case ScratchRegExpProgram.AnchorNode anchor:
                state.AddInstruction(anchor.Start
                    ? (flags.Multiline ? ExperimentalRegExpIrOpcode.AssertStartMultiline : ExperimentalRegExpIrOpcode.AssertStart)
                    : (flags.Multiline ? ExperimentalRegExpIrOpcode.AssertEndMultiline : ExperimentalRegExpIrOpcode.AssertEnd));
                return true;
            case ScratchRegExpProgram.BoundaryNode boundary:
                state.AddInstruction(boundary.Positive
                    ? (flags.IgnoreCase
                        ? ExperimentalRegExpIrOpcode.AssertWordBoundaryIgnoreCase
                        : ExperimentalRegExpIrOpcode.AssertWordBoundary)
                    : (flags.IgnoreCase
                        ? ExperimentalRegExpIrOpcode.AssertNotWordBoundaryIgnoreCase
                        : ExperimentalRegExpIrOpcode.AssertNotWordBoundary));
                return true;
            case ScratchRegExpProgram.ClassNode cls:
                if (ScratchRegExpProgram.TryGetSingleLiteralClassCodePoint(cls, out var classCodePoint))
                {
                    state.AddInstruction(flags.IgnoreCase
                        ? ExperimentalRegExpIrOpcode.CharIgnoreCase
                        : ExperimentalRegExpIrOpcode.Char, classCodePoint);
                    return true;
                }

                if (ScratchRegExpProgram.TryGetSmallLiteralClassCodePoints(cls, out var classCodePoints))
                {
                    state.AddInstruction(flags.IgnoreCase
                            ? ExperimentalRegExpIrOpcode.LiteralSetIgnoreCase
                            : ExperimentalRegExpIrOpcode.LiteralSet,
                        state.AddLiteralCodePointSet(classCodePoints));
                    return true;
                }

                state.AddInstruction(flags.IgnoreCase
                        ? ExperimentalRegExpIrOpcode.ClassIgnoreCase
                        : ExperimentalRegExpIrOpcode.Class,
                    state.AddClass(cls));
                return true;
            case ScratchRegExpProgram.PropertyEscapeNode propertyEscape:
                state.AddInstruction(flags.IgnoreCase
                        ? ExperimentalRegExpIrOpcode.PropertyEscapeIgnoreCase
                        : ExperimentalRegExpIrOpcode.PropertyEscape,
                    state.AddPropertyEscape(propertyEscape));
                return true;
            case ScratchRegExpProgram.QuantifierNode quantifier:
                return TryEmitQuantifier(quantifier, flags, nodeCaptureIndices, state, namedCaptureIndexes);
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                return TryEmitNode(scoped.Child, flags with
                {
                    IgnoreCase = scoped.IgnoreCase ?? flags.IgnoreCase,
                    Multiline = scoped.Multiline ?? flags.Multiline,
                    DotAll = scoped.DotAll ?? flags.DotAll
                }, nodeCaptureIndices, state, namedCaptureIndexes);
            default:
                return false;
        }
    }

    private static bool TryEmitSequence(ScratchRegExpProgram.SequenceNode sequence, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, IrBuildState state,
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

                    state.AddInstruction(ExperimentalRegExpIrOpcode.LiteralText, state.AddLiteralText(literalText));
                    i = runEnd;
                    continue;
                }
            }

            if (!TryEmitNode(sequence.Terms[i], flags, nodeCaptureIndices, state, namedCaptureIndexes))
                return false;

            i++;
        }

        return true;
    }

    private static bool TryEmitAlternation(ScratchRegExpProgram.Node[] alternatives, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, IrBuildState state,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        if (alternatives.Length == 0)
            return true;
        if (alternatives.Length == 1)
            return TryEmitNode(alternatives[0], flags, nodeCaptureIndices, state, namedCaptureIndexes);

        Span<int> initialEndJumps = stackalloc int[Math.Min(alternatives.Length - 1, 8)];
        using var endJumps = new PooledArrayBuilder<int>(initialEndJumps);
        for (var i = 0; i < alternatives.Length - 1; i++)
        {
            var splitIndex = state.AddInstruction(ExperimentalRegExpIrOpcode.Split);
            var firstTarget = state.Instructions.Count;
            if (!TryEmitNode(alternatives[i], flags, nodeCaptureIndices, state, namedCaptureIndexes))
                return false;

            endJumps.Add(state.AddInstruction(ExperimentalRegExpIrOpcode.Jump));
            var secondTarget = state.Instructions.Count;
            state.PatchInstruction(splitIndex, firstTarget, secondTarget);
        }

        if (!TryEmitNode(alternatives[^1], flags, nodeCaptureIndices, state, namedCaptureIndexes))
            return false;

        var endTarget = state.Instructions.Count;
        var endJumpSpan = endJumps.AsSpan();
        for (var i = 0; i < endJumpSpan.Length; i++)
            state.PatchInstruction(endJumpSpan[i], endTarget);

        return true;
    }

    private static bool TryEmitQuantifier(ScratchRegExpProgram.QuantifierNode quantifier, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, IrBuildState state,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        if (!CanUnrollQuantifier(quantifier))
            return false;

        if (!ScratchRegExpProgram.TryGetNodeMinMatchLength(quantifier.Child, out var childMinLength))
            return false;

        var captureClearSet = GetCaptureClearSet(nodeCaptureIndices, quantifier.Child);
        for (var i = 0; i < quantifier.Min; i++)
            if (!TryEmitQuantifiedChild(quantifier.Child, flags, nodeCaptureIndices, captureClearSet, state,
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
                    captureClearSet, state, namedCaptureIndexes);

            return TryEmitProgressSensitiveOptionals(quantifier.Child, quantifier.Max - quantifier.Min,
                quantifier.Greedy, flags, nodeCaptureIndices, captureClearSet, state, namedCaptureIndexes);
        }

        if (quantifier.Max == int.MaxValue)
            return TryEmitStar(quantifier.Child, quantifier.Greedy, flags, nodeCaptureIndices, captureClearSet,
                state, namedCaptureIndexes);

        var optionalCount = quantifier.Max - quantifier.Min;
        for (var i = 0; i < optionalCount; i++)
            if (!TryEmitOptional(quantifier.Child, quantifier.Greedy, flags, nodeCaptureIndices, captureClearSet,
                    state, namedCaptureIndexes))
                return false;

        return true;
    }

    private static bool CanUnrollQuantifier(ScratchRegExpProgram.QuantifierNode quantifier)
    {
        if (quantifier.Min > MaxUnrolledQuantifierCount)
            return false;

        if (quantifier.Max == int.MaxValue)
            return true;

        return quantifier.Max - quantifier.Min <= MaxUnrolledQuantifierCount;
    }

    private static bool TryEmitOptional(ScratchRegExpProgram.Node child, bool greedy, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, int[] captureClearSet, IrBuildState state,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        var splitIndex = state.AddInstruction(ExperimentalRegExpIrOpcode.Split);
        var childStart = state.Instructions.Count;
        if (!TryEmitQuantifiedChild(child, flags, nodeCaptureIndices, captureClearSet, state, namedCaptureIndexes))
            return false;

        var after = state.Instructions.Count;
        state.PatchInstruction(splitIndex,
            greedy ? childStart : after,
            greedy ? after : childStart);
        return true;
    }

    private static bool TryEmitStar(ScratchRegExpProgram.Node child, bool greedy, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, int[] captureClearSet, IrBuildState state,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        var loopHead = state.Instructions.Count;
        var splitIndex = state.AddInstruction(ExperimentalRegExpIrOpcode.Split);
        var childStart = state.Instructions.Count;
        if (!TryEmitQuantifiedChild(child, flags, nodeCaptureIndices, captureClearSet, state, namedCaptureIndexes))
            return false;

        state.AddInstruction(ExperimentalRegExpIrOpcode.Jump, loopHead);
        var after = state.Instructions.Count;
        state.PatchInstruction(splitIndex,
            greedy ? childStart : after,
            greedy ? after : childStart);
        return true;
    }

    private static bool TryEmitProgressSensitiveOptionals(ScratchRegExpProgram.Node child, int optionalCount, bool greedy,
        RegExpRuntimeFlags flags, Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, int[] captureClearSet,
        IrBuildState state, Dictionary<string, List<int>> namedCaptureIndexes)
    {
        if (optionalCount <= 0)
            return true;

        Span<int> initialProgressBranches = stackalloc int[Math.Min(optionalCount, 8)];
        Span<int> initialSkipSplits = stackalloc int[Math.Min(optionalCount, 8)];
        using var progressBranches = new PooledArrayBuilder<int>(initialProgressBranches);
        using var skipSplits = new PooledArrayBuilder<int>(initialSkipSplits);
        for (var i = 0; i < optionalCount; i++)
        {
            var loopSlot = state.AllocateLoopSlot();
            state.AddInstruction(ExperimentalRegExpIrOpcode.SaveLoopPosition, loopSlot);
            var splitIndex = state.AddInstruction(ExperimentalRegExpIrOpcode.Split);
            var childStart = state.Instructions.Count;
            if (!TryEmitQuantifiedChild(child, flags, nodeCaptureIndices, captureClearSet, state, namedCaptureIndexes))
                return false;

            progressBranches.Add(state.AddInstruction(ExperimentalRegExpIrOpcode.BranchIfLoopUnchanged));
            skipSplits.Add(splitIndex);
            state.PatchInstruction(splitIndex,
                greedy ? childStart : 0,
                greedy ? 0 : childStart);
        }

        var afterAll = state.Instructions.Count;
        var progressBranchSpan = progressBranches.AsSpan();
        for (var i = 0; i < progressBranchSpan.Length; i++)
        {
            var progressBranchIndex = progressBranchSpan[i];
            state.PatchInstruction(progressBranchIndex, state.Instructions[progressBranchIndex].Operand, afterAll);
        }

        var skipSplitSpan = skipSplits.AsSpan();
        for (var i = 0; i < skipSplitSpan.Length; i++)
        {
            var splitIndex = skipSplitSpan[i];
            var split = state.Instructions[splitIndex];
            state.PatchInstruction(splitIndex,
                greedy ? split.Operand : afterAll,
                greedy ? afterAll : split.Operand2);
        }

        return true;
    }

    private static bool TryEmitProgressSensitiveStar(ScratchRegExpProgram.Node child, bool greedy,
        RegExpRuntimeFlags flags, Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, int[] captureClearSet,
        IrBuildState state, Dictionary<string, List<int>> namedCaptureIndexes)
    {
        var loopHead = state.Instructions.Count;
        var loopSlot = state.AllocateLoopSlot();
        state.AddInstruction(ExperimentalRegExpIrOpcode.SaveLoopPosition, loopSlot);
        var splitIndex = state.AddInstruction(ExperimentalRegExpIrOpcode.Split);
        var childStart = state.Instructions.Count;
        if (!TryEmitQuantifiedChild(child, flags, nodeCaptureIndices, captureClearSet, state, namedCaptureIndexes))
            return false;

        var progressBranch = state.AddInstruction(ExperimentalRegExpIrOpcode.BranchIfLoopUnchanged);
        state.AddInstruction(ExperimentalRegExpIrOpcode.Jump, loopHead);
        var after = state.Instructions.Count;
        state.PatchInstruction(splitIndex,
            greedy ? childStart : after,
            greedy ? after : childStart);
        state.PatchInstruction(progressBranch, loopSlot, after);
        return true;
    }

    private static bool TryEmitQuantifiedChild(ScratchRegExpProgram.Node child, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, int[] captureClearSet, IrBuildState state,
        Dictionary<string, List<int>> namedCaptureIndexes)
    {
        if (captureClearSet.Length != 0)
            state.AddInstruction(ExperimentalRegExpIrOpcode.ClearCaptures, state.AddCaptureClearSet(captureClearSet));

        return TryEmitNode(child, flags, nodeCaptureIndices, state, namedCaptureIndexes);
    }

    private static int[] GetCaptureClearSet(Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices,
        ScratchRegExpProgram.Node node)
    {
        return nodeCaptureIndices.TryGetValue(node, out var indices) ? indices : [];
    }

    private static bool TryBuildLiteralText(ScratchRegExpProgram.Node[] terms, int start, int end, out string text)
    {
        Span<char> initialBuffer = stackalloc char[Math.Min(Math.Max((end - start) * 2, 8), 128)];
        using var builder = new PooledCharBuilder(initialBuffer);
        for (var i = start; i < end; i++)
        {
            if (terms[i] is not ScratchRegExpProgram.LiteralNode literal ||
                !Rune.TryCreate(literal.CodePoint, out var rune))
            {
                text = string.Empty;
                return false;
            }

            builder.AppendRune(rune);
        }

        text = builder.ToString();
        return true;
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
}
