using System.Buffers;

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
    PropertyEscape,
    Jump,
    Split
}

internal readonly record struct ExperimentalRegExpInstruction(ExperimentalRegExpOpcode OpCode, int Operand,
    int Operand2 = 0);

internal sealed class ExperimentalRegExpBytecodeProgram
{
    public required ExperimentalRegExpInstruction[] Instructions { get; init; }
    public ScratchRegExpProgram.ClassNode[] Classes { get; init; } = [];
    public ScratchRegExpProgram.PropertyEscapeNode[] PropertyEscapes { get; init; } = [];
}

internal static class ExperimentalRegExpCodeGenerator
{
    public static ExperimentalRegExpBytecodeProgram? TryGenerate(ExperimentalRegExpIrProgram? irProgram)
    {
        if (irProgram is null)
            return null;

        var source = irProgram.Instructions;
        var instructions = new ExperimentalRegExpInstruction[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            var instruction = source[i];
            instructions[i] = new(MapOpcode(instruction.OpCode), instruction.Operand, instruction.Operand2);
        }

        return new()
        {
            Instructions = instructions,
            Classes = irProgram.Classes,
            PropertyEscapes = irProgram.PropertyEscapes
        };
    }

    private static ExperimentalRegExpOpcode MapOpcode(ExperimentalRegExpIrOpcode opcode)
    {
        return opcode switch
        {
            ExperimentalRegExpIrOpcode.Match => ExperimentalRegExpOpcode.Match,
            ExperimentalRegExpIrOpcode.Char => ExperimentalRegExpOpcode.Char,
            ExperimentalRegExpIrOpcode.CharIgnoreCase => ExperimentalRegExpOpcode.CharIgnoreCase,
            ExperimentalRegExpIrOpcode.Dot => ExperimentalRegExpOpcode.Dot,
            ExperimentalRegExpIrOpcode.Any => ExperimentalRegExpOpcode.Any,
            ExperimentalRegExpIrOpcode.AssertStart => ExperimentalRegExpOpcode.AssertStart,
            ExperimentalRegExpIrOpcode.AssertStartMultiline => ExperimentalRegExpOpcode.AssertStartMultiline,
            ExperimentalRegExpIrOpcode.AssertEnd => ExperimentalRegExpOpcode.AssertEnd,
            ExperimentalRegExpIrOpcode.AssertEndMultiline => ExperimentalRegExpOpcode.AssertEndMultiline,
            ExperimentalRegExpIrOpcode.AssertWordBoundary => ExperimentalRegExpOpcode.AssertWordBoundary,
            ExperimentalRegExpIrOpcode.AssertNotWordBoundary => ExperimentalRegExpOpcode.AssertNotWordBoundary,
            ExperimentalRegExpIrOpcode.Class => ExperimentalRegExpOpcode.Class,
            ExperimentalRegExpIrOpcode.PropertyEscape => ExperimentalRegExpOpcode.PropertyEscape,
            ExperimentalRegExpIrOpcode.Jump => ExperimentalRegExpOpcode.Jump,
            ExperimentalRegExpIrOpcode.Split => ExperimentalRegExpOpcode.Split,
            _ => throw new InvalidOperationException($"Unknown regex IR opcode: {opcode}")
        };
    }
}

internal static class ExperimentalRegExpVm
{
    public static bool TryMatch(ExperimentalRegExpBytecodeProgram program, string input, int startIndex,
        RegExpRuntimeFlags flags, out int endIndex)
    {
        var stack = new ExperimentalBacktrackStack();
        try
        {
            return TryMatch(program, input, startIndex, flags, ref stack, out endIndex);
        }
        finally
        {
            stack.Dispose();
        }
    }

    private static bool TryMatch(ExperimentalRegExpBytecodeProgram program, string input, int startIndex,
        RegExpRuntimeFlags flags, ref ExperimentalBacktrackStack stack, out int endIndex)
    {
        var currentPos = startIndex;
        var instructions = program.Instructions;
        var instructionIndex = 0;
        while (true)
        {
            if ((uint)instructionIndex >= (uint)instructions.Length)
            {
                if (stack.TryPop(out instructionIndex, out currentPos))
                    continue;

                endIndex = default;
                return false;
            }

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
                        if (!stack.TryPop(out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    currentPos = nextPos;
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.Dot:
                    if (!ScratchRegExpMatcher.TryReadCodePointForVm(input, currentPos, flags.Unicode, out nextPos,
                            out codePoint) ||
                        ScratchRegExpMatcher.IsLineTerminatorForVm(codePoint))
                    {
                        if (!stack.TryPop(out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    currentPos = nextPos;
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.Any:
                    if (!ScratchRegExpMatcher.TryReadCodePointForVm(input, currentPos, flags.Unicode, out nextPos,
                            out _))
                    {
                        if (!stack.TryPop(out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    currentPos = nextPos;
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.AssertStart:
                    if (!ScratchRegExpMatcher.IsStartAnchorSatisfiedForVm(input, currentPos, multiline: false))
                    {
                        if (!stack.TryPop(out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.AssertStartMultiline:
                    if (!ScratchRegExpMatcher.IsStartAnchorSatisfiedForVm(input, currentPos, multiline: true))
                    {
                        if (!stack.TryPop(out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.AssertEnd:
                    if (!ScratchRegExpMatcher.IsEndAnchorSatisfiedForVm(input, currentPos, multiline: false))
                    {
                        if (!stack.TryPop(out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.AssertEndMultiline:
                    if (!ScratchRegExpMatcher.IsEndAnchorSatisfiedForVm(input, currentPos, multiline: true))
                    {
                        if (!stack.TryPop(out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.AssertWordBoundary:
                    if (!ScratchRegExpMatcher.IsWordBoundaryForVm(input, currentPos, flags.Unicode, flags.IgnoreCase))
                    {
                        if (!stack.TryPop(out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.AssertNotWordBoundary:
                    if (ScratchRegExpMatcher.IsWordBoundaryForVm(input, currentPos, flags.Unicode, flags.IgnoreCase))
                    {
                        if (!stack.TryPop(out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.Class:
                    if (!ScratchRegExpMatcher.TryMatchClassForVm(input, currentPos, program.Classes[instruction.Operand],
                            flags, out nextPos))
                    {
                        if (!stack.TryPop(out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    currentPos = nextPos;
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.PropertyEscape:
                    if (!ScratchRegExpMatcher.TryMatchPropertyEscapeForVm(input, currentPos,
                            program.PropertyEscapes[instruction.Operand], flags, out nextPos))
                    {
                        if (!stack.TryPop(out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    currentPos = nextPos;
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.Jump:
                    instructionIndex = instruction.Operand;
                    break;
                case ExperimentalRegExpOpcode.Split:
                    stack.Push(instruction.Operand2, currentPos);
                    instructionIndex = instruction.Operand;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown regex bytecode opcode: {instruction.OpCode}");
            }
        }
    }

    private ref struct ExperimentalBacktrackStack
    {
        private int[]? values;
        private int count;

        public void Push(int instructionIndex, int inputPosition)
        {
            EnsureCapacity(count + 2);
            values![count++] = instructionIndex;
            values[count++] = inputPosition;
        }

        public bool TryPop(out int instructionIndex, out int inputPosition)
        {
            if (count == 0)
            {
                instructionIndex = default;
                inputPosition = default;
                return false;
            }

            inputPosition = values![--count];
            instructionIndex = values[--count];
            return true;
        }

        public void Dispose()
        {
            if (values is null)
                return;

            ArrayPool<int>.Shared.Return(values);
            values = null;
            count = 0;
        }

        private void EnsureCapacity(int requiredCapacity)
        {
            if (values is not null && values.Length >= requiredCapacity)
                return;

            var next = ArrayPool<int>.Shared.Rent(requiredCapacity <= 8 ? 8 : requiredCapacity * 2);
            if (values is not null && count != 0)
                Array.Copy(values, next, count);

            if (values is not null)
                ArrayPool<int>.Shared.Return(values);

            values = next;
        }
    }
}
