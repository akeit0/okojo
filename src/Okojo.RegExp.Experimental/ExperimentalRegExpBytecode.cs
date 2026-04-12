namespace Okojo.RegExp.Experimental;

internal enum ExperimentalRegExpOpcode : byte
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
    PropertyEscape
}

internal readonly record struct ExperimentalRegExpInstruction(ExperimentalRegExpOpcode OpCode, int Operand);

internal sealed class ExperimentalRegExpBytecodeProgram
{
    public required ExperimentalRegExpInstruction[] Instructions { get; init; }
    public ScratchRegExpProgram.ClassNode[] Classes { get; init; } = [];
    public ScratchRegExpProgram.PropertyEscapeNode[] PropertyEscapes { get; init; } = [];
}

internal static class ExperimentalRegExpCodeGenerator
{
    public static ExperimentalRegExpBytecodeProgram? TryGenerate(ScratchRegExpProgram treeProgram)
    {
        if (treeProgram.CaptureCount != 0)
            return null;

        var instructions = new List<ExperimentalRegExpInstruction>();
        var classes = new List<ScratchRegExpProgram.ClassNode>();
        var propertyEscapes = new List<ScratchRegExpProgram.PropertyEscapeNode>();
        if (!TryEmitNode(treeProgram.Root, treeProgram.Flags, instructions, classes, propertyEscapes))
            return null;

        instructions.Add(new(ExperimentalRegExpOpcode.Match, 0));
        return new()
        {
            Instructions = instructions.ToArray(),
            Classes = classes.ToArray(),
            PropertyEscapes = propertyEscapes.ToArray()
        };
    }

    private static bool TryEmitNode(ScratchRegExpProgram.Node node, RegExpRuntimeFlags flags,
        List<ExperimentalRegExpInstruction> instructions, List<ScratchRegExpProgram.ClassNode> classes,
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
            case ScratchRegExpProgram.LiteralNode literal:
                instructions.Add(new(flags.IgnoreCase
                    ? ExperimentalRegExpOpcode.CharIgnoreCase
                    : ExperimentalRegExpOpcode.Char, literal.CodePoint));
                return true;
            case ScratchRegExpProgram.DotNode:
                instructions.Add(new(flags.DotAll ? ExperimentalRegExpOpcode.Any : ExperimentalRegExpOpcode.Dot, 0));
                return true;
            case ScratchRegExpProgram.AnchorNode anchor:
                instructions.Add(new(anchor.Start
                    ? (flags.Multiline ? ExperimentalRegExpOpcode.AssertStartMultiline : ExperimentalRegExpOpcode.AssertStart)
                    : (flags.Multiline ? ExperimentalRegExpOpcode.AssertEndMultiline : ExperimentalRegExpOpcode.AssertEnd), 0));
                return true;
            case ScratchRegExpProgram.BoundaryNode boundary:
                instructions.Add(new(boundary.Positive
                    ? ExperimentalRegExpOpcode.AssertWordBoundary
                    : ExperimentalRegExpOpcode.AssertNotWordBoundary, 0));
                return true;
            case ScratchRegExpProgram.ClassNode cls:
                classes.Add(cls);
                instructions.Add(new(ExperimentalRegExpOpcode.Class, classes.Count - 1));
                return true;
            case ScratchRegExpProgram.PropertyEscapeNode propertyEscape:
                propertyEscapes.Add(propertyEscape);
                instructions.Add(new(ExperimentalRegExpOpcode.PropertyEscape, propertyEscapes.Count - 1));
                return true;
            default:
                return false;
        }
    }
}

internal static class ExperimentalRegExpVm
{
    public static bool TryMatch(ExperimentalRegExpBytecodeProgram program, string input, int startIndex,
        RegExpRuntimeFlags flags, out int endIndex)
    {
        var currentPos = startIndex;
        var instructions = program.Instructions;
        for (var instructionIndex = 0; instructionIndex < instructions.Length; instructionIndex++)
        {
            var instruction = instructions[instructionIndex];
            switch (instruction.OpCode)
            {
                case ExperimentalRegExpOpcode.Match:
                    endIndex = currentPos;
                    return true;
                case ExperimentalRegExpOpcode.Char:
                case ExperimentalRegExpOpcode.CharIgnoreCase:
                    if (!ScratchRegExpMatcher.TryReadCodePointForVm(input, currentPos, flags.Unicode, out var nextPos,
                            out var codePoint) ||
                        !ScratchRegExpMatcher.CodePointEqualsForVm(codePoint, instruction.Operand,
                            instruction.OpCode == ExperimentalRegExpOpcode.CharIgnoreCase))
                    {
                        endIndex = default;
                        return false;
                    }

                    currentPos = nextPos;
                    break;
                case ExperimentalRegExpOpcode.Dot:
                    if (!ScratchRegExpMatcher.TryReadCodePointForVm(input, currentPos, flags.Unicode, out nextPos,
                            out codePoint) ||
                        ScratchRegExpMatcher.IsLineTerminatorForVm(codePoint))
                    {
                        endIndex = default;
                        return false;
                    }

                    currentPos = nextPos;
                    break;
                case ExperimentalRegExpOpcode.Any:
                    if (!ScratchRegExpMatcher.TryReadCodePointForVm(input, currentPos, flags.Unicode, out nextPos,
                            out _))
                    {
                        endIndex = default;
                        return false;
                    }

                    currentPos = nextPos;
                    break;
                case ExperimentalRegExpOpcode.AssertStart:
                    if (!ScratchRegExpMatcher.IsStartAnchorSatisfiedForVm(input, currentPos, multiline: false))
                    {
                        endIndex = default;
                        return false;
                    }

                    break;
                case ExperimentalRegExpOpcode.AssertStartMultiline:
                    if (!ScratchRegExpMatcher.IsStartAnchorSatisfiedForVm(input, currentPos, multiline: true))
                    {
                        endIndex = default;
                        return false;
                    }

                    break;
                case ExperimentalRegExpOpcode.AssertEnd:
                    if (!ScratchRegExpMatcher.IsEndAnchorSatisfiedForVm(input, currentPos, multiline: false))
                    {
                        endIndex = default;
                        return false;
                    }

                    break;
                case ExperimentalRegExpOpcode.AssertEndMultiline:
                    if (!ScratchRegExpMatcher.IsEndAnchorSatisfiedForVm(input, currentPos, multiline: true))
                    {
                        endIndex = default;
                        return false;
                    }

                    break;
                case ExperimentalRegExpOpcode.AssertWordBoundary:
                    if (!ScratchRegExpMatcher.IsWordBoundaryForVm(input, currentPos, flags.Unicode, flags.IgnoreCase))
                    {
                        endIndex = default;
                        return false;
                    }

                    break;
                case ExperimentalRegExpOpcode.AssertNotWordBoundary:
                    if (ScratchRegExpMatcher.IsWordBoundaryForVm(input, currentPos, flags.Unicode, flags.IgnoreCase))
                    {
                        endIndex = default;
                        return false;
                    }

                    break;
                case ExperimentalRegExpOpcode.Class:
                    if (!ScratchRegExpMatcher.TryMatchClassForVm(input, currentPos, program.Classes[instruction.Operand],
                            flags, out nextPos))
                    {
                        endIndex = default;
                        return false;
                    }

                    currentPos = nextPos;
                    break;
                case ExperimentalRegExpOpcode.PropertyEscape:
                    if (!ScratchRegExpMatcher.TryMatchPropertyEscapeForVm(input, currentPos,
                            program.PropertyEscapes[instruction.Operand], flags, out nextPos))
                    {
                        endIndex = default;
                        return false;
                    }

                    currentPos = nextPos;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown regex bytecode opcode: {instruction.OpCode}");
            }
        }

        endIndex = default;
        return false;
    }
}
