using System.Buffers;

namespace Okojo.RegExp.Experimental;

internal enum ExperimentalRegExpOpcode : byte
{
    Match,
    ClearCaptures,
    ScanAnyToEnd,
    ScanDotToEnd,
    ScanClassToEnd,
    ScanClassToEndIgnoreCase,
    SaveStart,
    SaveEnd,
    BackReference,
    BackReferenceIgnoreCase,
    NamedBackReference,
    NamedBackReferenceIgnoreCase,
    AssertLookahead,
    AssertNotLookahead,
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

internal readonly record struct ExperimentalRegExpInstruction(ExperimentalRegExpOpcode OpCode, int Operand,
    int Operand2 = 0);

internal sealed class ExperimentalRegExpBytecodeProgram
{
    public required ExperimentalRegExpInstruction[] Instructions { get; init; }
    public int[][] CaptureClearSets { get; init; } = [];
    public int[][] NamedBackReferenceCaptureSets { get; init; } = [];
    public ExperimentalRegExpBytecodeProgram[] LookaheadPrograms { get; init; } = [];
    public string[] LiteralTexts { get; init; } = [];
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
        var instructions = new List<ExperimentalRegExpInstruction>(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            if (TryEmitTailScan(source, i, instructions, out var consumedInstructions))
            {
                i += consumedInstructions - 1;
                continue;
            }

            var instruction = source[i];
            instructions.Add(new(MapOpcode(instruction.OpCode), instruction.Operand, instruction.Operand2));
        }

        return new()
        {
            Instructions = instructions.ToArray(),
            CaptureClearSets = irProgram.CaptureClearSets,
            NamedBackReferenceCaptureSets = irProgram.NamedBackReferenceCaptureSets,
            LookaheadPrograms = LowerLookaheadPrograms(irProgram.LookaheadPrograms),
            LiteralTexts = irProgram.LiteralTexts,
            Classes = irProgram.Classes,
            PropertyEscapes = irProgram.PropertyEscapes
        };
    }

    private static ExperimentalRegExpBytecodeProgram[] LowerLookaheadPrograms(ExperimentalRegExpIrProgram[] lookaheadPrograms)
    {
        if (lookaheadPrograms.Length == 0)
            return [];

        var lowered = new ExperimentalRegExpBytecodeProgram[lookaheadPrograms.Length];
        for (var i = 0; i < lookaheadPrograms.Length; i++)
            lowered[i] = TryGenerate(lookaheadPrograms[i])!;
        return lowered;
    }

    private static bool TryEmitTailScan(ExperimentalRegExpIrInstruction[] source, int index,
        List<ExperimentalRegExpInstruction> instructions, out int consumedInstructions)
    {
        consumedInstructions = 0;
        if (index + 3 >= source.Length)
            return false;

        var split = source[index];
        if (split.OpCode != ExperimentalRegExpIrOpcode.Split ||
            split.Operand != index + 1 ||
            split.Operand2 != index + 3 ||
            source[index + 2].OpCode != ExperimentalRegExpIrOpcode.Jump ||
            source[index + 2].Operand != index ||
            source[index + 3].OpCode != ExperimentalRegExpIrOpcode.Match)
            return false;

        var child = source[index + 1];
        switch (child.OpCode)
        {
            case ExperimentalRegExpIrOpcode.Any:
                instructions.Add(new(ExperimentalRegExpOpcode.ScanAnyToEnd, 0));
                consumedInstructions = 3;
                return true;
            case ExperimentalRegExpIrOpcode.Dot:
                instructions.Add(new(ExperimentalRegExpOpcode.ScanDotToEnd, 0));
                consumedInstructions = 3;
                return true;
            case ExperimentalRegExpIrOpcode.Class:
                instructions.Add(new(ExperimentalRegExpOpcode.ScanClassToEnd, child.Operand));
                consumedInstructions = 3;
                return true;
            case ExperimentalRegExpIrOpcode.ClassIgnoreCase:
                instructions.Add(new(ExperimentalRegExpOpcode.ScanClassToEndIgnoreCase, child.Operand));
                consumedInstructions = 3;
                return true;
            default:
                return false;
        }
    }

    private static ExperimentalRegExpOpcode MapOpcode(ExperimentalRegExpIrOpcode opcode)
    {
        return opcode switch
        {
            ExperimentalRegExpIrOpcode.Match => ExperimentalRegExpOpcode.Match,
            ExperimentalRegExpIrOpcode.ClearCaptures => ExperimentalRegExpOpcode.ClearCaptures,
            ExperimentalRegExpIrOpcode.SaveStart => ExperimentalRegExpOpcode.SaveStart,
            ExperimentalRegExpIrOpcode.SaveEnd => ExperimentalRegExpOpcode.SaveEnd,
            ExperimentalRegExpIrOpcode.BackReference => ExperimentalRegExpOpcode.BackReference,
            ExperimentalRegExpIrOpcode.BackReferenceIgnoreCase => ExperimentalRegExpOpcode.BackReferenceIgnoreCase,
            ExperimentalRegExpIrOpcode.NamedBackReference => ExperimentalRegExpOpcode.NamedBackReference,
            ExperimentalRegExpIrOpcode.NamedBackReferenceIgnoreCase => ExperimentalRegExpOpcode.NamedBackReferenceIgnoreCase,
            ExperimentalRegExpIrOpcode.AssertLookahead => ExperimentalRegExpOpcode.AssertLookahead,
            ExperimentalRegExpIrOpcode.AssertNotLookahead => ExperimentalRegExpOpcode.AssertNotLookahead,
            ExperimentalRegExpIrOpcode.LiteralText => ExperimentalRegExpOpcode.LiteralText,
            ExperimentalRegExpIrOpcode.Char => ExperimentalRegExpOpcode.Char,
            ExperimentalRegExpIrOpcode.CharIgnoreCase => ExperimentalRegExpOpcode.CharIgnoreCase,
            ExperimentalRegExpIrOpcode.Dot => ExperimentalRegExpOpcode.Dot,
            ExperimentalRegExpIrOpcode.Any => ExperimentalRegExpOpcode.Any,
            ExperimentalRegExpIrOpcode.AssertStart => ExperimentalRegExpOpcode.AssertStart,
            ExperimentalRegExpIrOpcode.AssertStartMultiline => ExperimentalRegExpOpcode.AssertStartMultiline,
            ExperimentalRegExpIrOpcode.AssertEnd => ExperimentalRegExpOpcode.AssertEnd,
            ExperimentalRegExpIrOpcode.AssertEndMultiline => ExperimentalRegExpOpcode.AssertEndMultiline,
            ExperimentalRegExpIrOpcode.AssertWordBoundary => ExperimentalRegExpOpcode.AssertWordBoundary,
            ExperimentalRegExpIrOpcode.AssertWordBoundaryIgnoreCase => ExperimentalRegExpOpcode.AssertWordBoundaryIgnoreCase,
            ExperimentalRegExpIrOpcode.AssertNotWordBoundary => ExperimentalRegExpOpcode.AssertNotWordBoundary,
            ExperimentalRegExpIrOpcode.AssertNotWordBoundaryIgnoreCase => ExperimentalRegExpOpcode.AssertNotWordBoundaryIgnoreCase,
            ExperimentalRegExpIrOpcode.Class => ExperimentalRegExpOpcode.Class,
            ExperimentalRegExpIrOpcode.ClassIgnoreCase => ExperimentalRegExpOpcode.ClassIgnoreCase,
            ExperimentalRegExpIrOpcode.PropertyEscape => ExperimentalRegExpOpcode.PropertyEscape,
            ExperimentalRegExpIrOpcode.PropertyEscapeIgnoreCase => ExperimentalRegExpOpcode.PropertyEscapeIgnoreCase,
            ExperimentalRegExpIrOpcode.Jump => ExperimentalRegExpOpcode.Jump,
            ExperimentalRegExpIrOpcode.Split => ExperimentalRegExpOpcode.Split,
            _ => throw new InvalidOperationException($"Unknown regex IR opcode: {opcode}")
        };
    }
}

internal static class ExperimentalRegExpVm
{
    public static bool TryMatch(ExperimentalRegExpBytecodeProgram program, string input, int startIndex,
        RegExpRuntimeFlags flags, ExperimentalRegExpCaptureState? captureState, out int endIndex)
    {
        var stack = new ExperimentalBacktrackStack();
        try
        {
            return TryMatch(program, input, startIndex, flags, captureState, ref stack, out endIndex);
        }
        finally
        {
            stack.Dispose();
        }
    }

    private static bool TryMatch(ExperimentalRegExpBytecodeProgram program, string input, int startIndex,
        RegExpRuntimeFlags flags, ExperimentalRegExpCaptureState? captureState, ref ExperimentalBacktrackStack stack,
        out int endIndex)
    {
        var currentPos = startIndex;
        var instructions = program.Instructions;
        var instructionIndex = 0;
        while (true)
        {
            if ((uint)instructionIndex >= (uint)instructions.Length)
            {
                if (TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
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
                case ExperimentalRegExpOpcode.ClearCaptures:
                    if (captureState is not null)
                    {
                        var clearSet = program.CaptureClearSets[instruction.Operand];
                        for (var i = 0; i < clearSet.Length; i++)
                            captureState.Clear(clearSet[i]);
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.ScanAnyToEnd:
                    currentPos = input.Length;
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.ScanDotToEnd:
                    currentPos = ScratchRegExpMatcher.ScanDotToEndForVm(input, currentPos, flags.Unicode);
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.ScanClassToEnd:
                case ExperimentalRegExpOpcode.ScanClassToEndIgnoreCase:
                    currentPos = ScratchRegExpMatcher.ScanClassToEndForVm(input, currentPos,
                        program.Classes[instruction.Operand], flags with
                        {
                            IgnoreCase = instruction.OpCode == ExperimentalRegExpOpcode.ScanClassToEndIgnoreCase
                        });
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.SaveStart:
                    captureState?.SaveStart(instruction.Operand, currentPos);
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.SaveEnd:
                    captureState?.SaveEnd(instruction.Operand, currentPos);
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.BackReference:
                case ExperimentalRegExpOpcode.BackReferenceIgnoreCase:
                    if (!ScratchRegExpMatcher.TryMatchBackReferenceForVm(input, currentPos, instruction.Operand, flags,
                            instruction.OpCode == ExperimentalRegExpOpcode.BackReferenceIgnoreCase,
                            captureState, out currentPos))
                    {
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.NamedBackReference:
                case ExperimentalRegExpOpcode.NamedBackReferenceIgnoreCase:
                    if (!ScratchRegExpMatcher.TryMatchNamedBackReferenceForVm(input, currentPos,
                            program.NamedBackReferenceCaptureSets[instruction.Operand], flags,
                            instruction.OpCode == ExperimentalRegExpOpcode.NamedBackReferenceIgnoreCase, captureState,
                            out currentPos))
                    {
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.AssertLookahead:
                case ExperimentalRegExpOpcode.AssertNotLookahead:
                    var checkpoint = captureState?.Checkpoint ?? 0;
                    var lookaheadMatched = TryMatch(program.LookaheadPrograms[instruction.Operand], input, currentPos,
                        flags, captureState, out _);
                    if (instruction.OpCode == ExperimentalRegExpOpcode.AssertLookahead)
                    {
                        if (!lookaheadMatched)
                        {
                            captureState?.Restore(checkpoint);
                            if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
                            {
                                endIndex = default;
                                return false;
                            }

                            continue;
                        }
                    }
                    else
                    {
                        captureState?.Restore(checkpoint);
                        if (lookaheadMatched)
                        {
                            if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
                            {
                                endIndex = default;
                                return false;
                            }

                            continue;
                        }
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.LiteralText:
                    var literalText = program.LiteralTexts[instruction.Operand];
                    if (currentPos > input.Length ||
                        !input.AsSpan(currentPos).StartsWith(literalText.AsSpan(), StringComparison.Ordinal))
                    {
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    currentPos += literalText.Length;
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.Char:
                case ExperimentalRegExpOpcode.CharIgnoreCase:
                    if (!ScratchRegExpMatcher.TryReadCodePointForVm(input, currentPos, flags.Unicode, out var nextPos,
                            out var codePoint) ||
                        !ScratchRegExpMatcher.CodePointEqualsForVm(codePoint, instruction.Operand,
                            instruction.OpCode == ExperimentalRegExpOpcode.CharIgnoreCase))
                    {
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.AssertWordBoundary:
                case ExperimentalRegExpOpcode.AssertWordBoundaryIgnoreCase:
                    if (!ScratchRegExpMatcher.IsWordBoundaryForVm(input, currentPos, flags.Unicode,
                            instruction.OpCode == ExperimentalRegExpOpcode.AssertWordBoundaryIgnoreCase))
                    {
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.AssertNotWordBoundary:
                case ExperimentalRegExpOpcode.AssertNotWordBoundaryIgnoreCase:
                    if (ScratchRegExpMatcher.IsWordBoundaryForVm(input, currentPos, flags.Unicode,
                            instruction.OpCode == ExperimentalRegExpOpcode.AssertNotWordBoundaryIgnoreCase))
                    {
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.Class:
                case ExperimentalRegExpOpcode.ClassIgnoreCase:
                    if (!ScratchRegExpMatcher.TryMatchClassForVm(input, currentPos, program.Classes[instruction.Operand],
                            flags with
                            {
                                IgnoreCase = instruction.OpCode == ExperimentalRegExpOpcode.ClassIgnoreCase
                            }, out nextPos))
                    {
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
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
                case ExperimentalRegExpOpcode.PropertyEscapeIgnoreCase:
                    if (!ScratchRegExpMatcher.TryMatchPropertyEscapeForVm(input, currentPos,
                            program.PropertyEscapes[instruction.Operand], flags with
                            {
                                IgnoreCase = instruction.OpCode == ExperimentalRegExpOpcode.PropertyEscapeIgnoreCase
                            }, out nextPos))
                    {
                        if (!TryBacktrack(captureState, ref stack, out instructionIndex, out currentPos))
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
                    stack.Push(instruction.Operand2, currentPos, captureState?.Checkpoint ?? 0);
                    instructionIndex = instruction.Operand;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown regex bytecode opcode: {instruction.OpCode}");
            }
        }
    }

    private static bool TryBacktrack(ExperimentalRegExpCaptureState? captureState, ref ExperimentalBacktrackStack stack,
        out int instructionIndex, out int currentPos)
    {
        if (!stack.TryPop(out instructionIndex, out currentPos, out var captureCheckpoint))
            return false;

        captureState?.Restore(captureCheckpoint);
        return true;
    }

    private ref struct ExperimentalBacktrackStack
    {
        private int[]? values;
        private int count;

        public void Push(int instructionIndex, int inputPosition, int captureCheckpoint)
        {
            EnsureCapacity(count + 3);
            values![count++] = instructionIndex;
            values[count++] = inputPosition;
            values[count++] = captureCheckpoint;
        }

        public bool TryPop(out int instructionIndex, out int inputPosition, out int captureCheckpoint)
        {
            if (count == 0)
            {
                instructionIndex = default;
                inputPosition = default;
                captureCheckpoint = default;
                return false;
            }

            captureCheckpoint = values![--count];
            inputPosition = values[--count];
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

internal sealed class ExperimentalRegExpCaptureState : IDisposable
{
    private ExperimentalCaptureLogEntry[] logEntries;
    private int logCount;

    public ExperimentalRegExpCaptureState(int captureCount)
    {
        Starts = ArrayPool<int>.Shared.Rent(captureCount + 1);
        Ends = ArrayPool<int>.Shared.Rent(captureCount + 1);
        Matched = ArrayPool<bool>.Shared.Rent(captureCount + 1);
        logEntries = ArrayPool<ExperimentalCaptureLogEntry>.Shared.Rent(Math.Max(8, captureCount + 1));
        CaptureCount = captureCount;
        Reset();
    }

    public int CaptureCount { get; }
    public int[] Starts { get; }
    public int[] Ends { get; }
    public bool[] Matched { get; }
    public int Checkpoint => logCount;

    public void Reset()
    {
        Array.Clear(Starts, 0, CaptureCount + 1);
        Array.Clear(Ends, 0, CaptureCount + 1);
        Array.Clear(Matched, 0, CaptureCount + 1);
        logCount = 0;
    }

    public void SaveStart(int index, int position)
    {
        Log(index);
        Starts[index] = position;
        Matched[index] = true;
    }

    public void SaveEnd(int index, int position)
    {
        Log(index);
        Ends[index] = position;
        Matched[index] = true;
    }

    public void Clear(int index)
    {
        Log(index);
        Starts[index] = 0;
        Ends[index] = 0;
        Matched[index] = false;
    }

    public void Restore(int checkpoint)
    {
        while (logCount > checkpoint)
        {
            var entry = logEntries[--logCount];
            Starts[entry.Index] = entry.Start;
            Ends[entry.Index] = entry.End;
            Matched[entry.Index] = entry.Matched;
        }
    }

    public void Dispose()
    {
        ArrayPool<int>.Shared.Return(Starts);
        ArrayPool<int>.Shared.Return(Ends);
        ArrayPool<bool>.Shared.Return(Matched);
        ArrayPool<ExperimentalCaptureLogEntry>.Shared.Return(logEntries);
    }

    private void Log(int index)
    {
        EnsureLogCapacity(logCount + 1);
        logEntries[logCount++] = new(index, Starts[index], Ends[index], Matched[index]);
    }

    private void EnsureLogCapacity(int requiredCount)
    {
        if (logEntries.Length >= requiredCount)
            return;

        var next = ArrayPool<ExperimentalCaptureLogEntry>.Shared.Rent(requiredCount * 2);
        Array.Copy(logEntries, next, logCount);
        ArrayPool<ExperimentalCaptureLogEntry>.Shared.Return(logEntries);
        logEntries = next;
    }

    private readonly record struct ExperimentalCaptureLogEntry(int Index, int Start, int End, bool Matched);
}
