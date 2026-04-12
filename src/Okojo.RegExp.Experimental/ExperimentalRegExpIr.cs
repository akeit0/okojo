namespace Okojo.RegExp.Experimental;

internal enum ExperimentalRegExpIrOpcode : byte
{
    Match,
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
    public ScratchRegExpProgram.ClassNode[] Classes { get; init; } = [];
    public ScratchRegExpProgram.PropertyEscapeNode[] PropertyEscapes { get; init; } = [];
}

internal static class ExperimentalRegExpIrGenerator
{
    public static ExperimentalRegExpIrProgram? TryGenerate(ScratchRegExpProgram treeProgram)
    {
        if (treeProgram.CaptureCount != 0)
            return null;

        var instructions = new List<ExperimentalRegExpIrInstruction>();
        var classes = new List<ScratchRegExpProgram.ClassNode>();
        var propertyEscapes = new List<ScratchRegExpProgram.PropertyEscapeNode>();
        if (!TryEmitNode(treeProgram.Root, treeProgram.Flags, instructions, classes, propertyEscapes))
            return null;

        instructions.Add(new(ExperimentalRegExpIrOpcode.Match, 0));
        return new()
        {
            Instructions = instructions.ToArray(),
            Classes = classes.ToArray(),
            PropertyEscapes = propertyEscapes.ToArray()
        };
    }

    private static bool TryEmitNode(ScratchRegExpProgram.Node node, RegExpRuntimeFlags flags,
        List<ExperimentalRegExpIrInstruction> instructions, List<ScratchRegExpProgram.ClassNode> classes,
        List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes)
    {
        switch (node)
        {
            case ScratchRegExpProgram.EmptyNode:
                return true;
            case ScratchRegExpProgram.SequenceNode sequence:
                for (var i = 0; i < sequence.Terms.Length; i++)
                    if (!TryEmitNode(sequence.Terms[i], flags, instructions, classes, propertyEscapes))
                        return false;
                return true;
            case ScratchRegExpProgram.AlternationNode alternation:
                return TryEmitAlternation(alternation.Alternatives, flags, instructions, classes, propertyEscapes);
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
                return TryEmitQuantifier(quantifier, flags, instructions, classes, propertyEscapes);
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                return TryEmitNode(scoped.Child, flags with
                {
                    IgnoreCase = scoped.IgnoreCase ?? flags.IgnoreCase,
                    Multiline = scoped.Multiline ?? flags.Multiline,
                    DotAll = scoped.DotAll ?? flags.DotAll
                }, instructions, classes, propertyEscapes);
            default:
                return false;
        }
    }

    private static bool TryEmitAlternation(ScratchRegExpProgram.Node[] alternatives, RegExpRuntimeFlags flags,
        List<ExperimentalRegExpIrInstruction> instructions, List<ScratchRegExpProgram.ClassNode> classes,
        List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes)
    {
        if (alternatives.Length == 0)
            return true;
        if (alternatives.Length == 1)
            return TryEmitNode(alternatives[0], flags, instructions, classes, propertyEscapes);

        List<int> endJumps = [];
        for (var i = 0; i < alternatives.Length - 1; i++)
        {
            var splitIndex = AddInstruction(instructions, ExperimentalRegExpIrOpcode.Split);
            var firstTarget = instructions.Count;
            if (!TryEmitNode(alternatives[i], flags, instructions, classes, propertyEscapes))
                return false;

            endJumps.Add(AddInstruction(instructions, ExperimentalRegExpIrOpcode.Jump));
            var secondTarget = instructions.Count;
            PatchInstruction(instructions, splitIndex, firstTarget, secondTarget);
        }

        if (!TryEmitNode(alternatives[^1], flags, instructions, classes, propertyEscapes))
            return false;

        var endTarget = instructions.Count;
        for (var i = 0; i < endJumps.Count; i++)
            PatchInstruction(instructions, endJumps[i], endTarget);

        return true;
    }

    private static bool TryEmitQuantifier(ScratchRegExpProgram.QuantifierNode quantifier, RegExpRuntimeFlags flags,
        List<ExperimentalRegExpIrInstruction> instructions, List<ScratchRegExpProgram.ClassNode> classes,
        List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes)
    {
        if (!TryComputeMinMatchLength(quantifier.Child, out var childMinLength))
            return false;

        for (var i = 0; i < quantifier.Min; i++)
            if (!TryEmitNode(quantifier.Child, flags, instructions, classes, propertyEscapes))
                return false;

        if (quantifier.Max == quantifier.Min)
            return true;

        if (childMinLength == 0)
            return true;

        if (quantifier.Max == int.MaxValue)
            return TryEmitStar(quantifier.Child, quantifier.Greedy, flags, instructions, classes, propertyEscapes);

        var optionalCount = quantifier.Max - quantifier.Min;
        for (var i = 0; i < optionalCount; i++)
            if (!TryEmitOptional(quantifier.Child, quantifier.Greedy, flags, instructions, classes, propertyEscapes))
                return false;

        return true;
    }

    private static bool TryEmitOptional(ScratchRegExpProgram.Node child, bool greedy, RegExpRuntimeFlags flags,
        List<ExperimentalRegExpIrInstruction> instructions, List<ScratchRegExpProgram.ClassNode> classes,
        List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes)
    {
        var splitIndex = AddInstruction(instructions, ExperimentalRegExpIrOpcode.Split);
        var childStart = instructions.Count;
        if (!TryEmitNode(child, flags, instructions, classes, propertyEscapes))
            return false;

        var after = instructions.Count;
        PatchInstruction(instructions, splitIndex,
            greedy ? childStart : after,
            greedy ? after : childStart);
        return true;
    }

    private static bool TryEmitStar(ScratchRegExpProgram.Node child, bool greedy, RegExpRuntimeFlags flags,
        List<ExperimentalRegExpIrInstruction> instructions, List<ScratchRegExpProgram.ClassNode> classes,
        List<ScratchRegExpProgram.PropertyEscapeNode> propertyEscapes)
    {
        var loopHead = instructions.Count;
        var splitIndex = AddInstruction(instructions, ExperimentalRegExpIrOpcode.Split);
        var childStart = instructions.Count;
        if (!TryEmitNode(child, flags, instructions, classes, propertyEscapes))
            return false;

        instructions.Add(new(ExperimentalRegExpIrOpcode.Jump, loopHead));
        var after = instructions.Count;
        PatchInstruction(instructions, splitIndex,
            greedy ? childStart : after,
            greedy ? after : childStart);
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
