using Okojo.Internals;
using System.Buffers;

namespace Okojo.RegExp.Experimental;

internal enum ExperimentalRegExpOpcode : byte
{
    Match,
    ClearCaptures,
    SaveLoopPosition,
    BranchIfLoopUnchanged,
    ScanAnyToEnd,
    ScanDotToEnd,
    ScanAsciiClassToEnd,
    ScanClassToEnd,
    ScanClassToEndIgnoreCase,
    ScanPropertyEscapeToEnd,
    ScanPropertyEscapeToEndIgnoreCase,
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
    ClassAscii,
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
    public ExperimentalRegExpBytecodeProgram?[] LookbehindPrograms { get; init; } = [];
    public ScratchRegExpProgram.Node[] LookbehindNodes { get; init; } = [];
    public RegExpRuntimeFlags[] LookbehindFlags { get; init; } = [];
    public int[] LookbehindMinMatchLengths { get; init; } = [];
    public int[] LookbehindMaxMatchLengths { get; init; } = [];
    public int LoopSlotCount { get; init; }
    public string[] LiteralTexts { get; init; } = [];
    public ExperimentalRegExpCharacterSet[] CharacterSets { get; init; } = [];
    public ExperimentalRegExpPropertyEscape[] PropertyEscapes { get; init; } = [];
}

internal static class ExperimentalRegExpCodeGenerator
{
    public static ExperimentalRegExpBytecodeProgram? TryGenerate(ExperimentalRegExpIrProgram? irProgram)
    {
        if (irProgram is null)
            return null;

        BuildCharacterSets(irProgram, out var characterSets, out var classOperandMap, out var literalSetOperandMap);
        var source = irProgram.Instructions;
        Span<ExperimentalRegExpInstruction> initialInstructionBuffer =
            stackalloc ExperimentalRegExpInstruction[Math.Min(Math.Max(source.Length, 1), 64)];
        var instructions = new PooledArrayBuilder<ExperimentalRegExpInstruction>(initialInstructionBuffer);
        try
        {
            for (var i = 0; i < source.Length; i++)
            {
                if (TryEmitTailScan(source, classOperandMap, characterSets, i, ref instructions,
                        out var consumedInstructions))
                {
                    i += consumedInstructions - 1;
                    continue;
                }

                var instruction = source[i];
                if (instruction.OpCode == ExperimentalRegExpIrOpcode.Class &&
                    characterSets[classOperandMap[instruction.Operand]].AsciiBitmap.HasValue)
                {
                    instructions.Add(new(ExperimentalRegExpOpcode.ClassAscii, classOperandMap[instruction.Operand],
                        instruction.Operand2));
                    continue;
                }

                instructions.Add(MapInstruction(instruction, classOperandMap, literalSetOperandMap));
            }

            var lookbehindPrograms = LowerLookbehindPrograms(irProgram.LookbehindPrograms, irProgram.LookbehindNodes,
                out var lookbehindMinMatchLengths, out var lookbehindMaxMatchLengths);
            return new()
            {
                Instructions = instructions.ToArray(),
                CaptureClearSets = irProgram.CaptureClearSets,
                NamedBackReferenceCaptureSets = irProgram.NamedBackReferenceCaptureSets,
                LookaheadPrograms = LowerLookaheadPrograms(irProgram.LookaheadPrograms),
                LookbehindPrograms = lookbehindPrograms,
                LookbehindNodes = irProgram.LookbehindNodes,
                LookbehindFlags = irProgram.LookbehindFlags,
                LookbehindMinMatchLengths = lookbehindMinMatchLengths,
                LookbehindMaxMatchLengths = lookbehindMaxMatchLengths,
                LoopSlotCount = irProgram.LoopSlotCount,
                LiteralTexts = irProgram.LiteralTexts,
                CharacterSets = characterSets,
                PropertyEscapes = BuildPropertyEscapes(irProgram.PropertyEscapes)
            };
        }
        finally
        {
            instructions.Dispose();
        }
    }

    private static void BuildCharacterSets(ExperimentalRegExpIrProgram irProgram,
        out ExperimentalRegExpCharacterSet[] characterSets, out int[] classOperandMap, out int[] literalSetOperandMap)
    {
        var builder = new List<ExperimentalRegExpCharacterSet>(irProgram.Classes.Length + irProgram.LiteralCodePointSets.Length);
        classOperandMap = new int[irProgram.Classes.Length];
        literalSetOperandMap = new int[irProgram.LiteralCodePointSets.Length];

        for (var i = 0; i < irProgram.Classes.Length; i++)
        {
            var asciiBitmap = ScratchRegExpProgram.TryBuildAsciiClassBitmap(irProgram.Classes[i], out var lowMask,
                out var highMask)
                ? new ExperimentalRegExpAsciiBitmap(lowMask, highMask)
                : default;
            var hasSimpleClass = ScratchRegExpProgram.TryCreateSimpleClass(irProgram.Classes[i], out var simpleClass);
            classOperandMap[i] = builder.Count;
            builder.Add(new()
            {
                SimpleClass = hasSimpleClass ? simpleClass : null,
                ComplexClass = hasSimpleClass ? null : irProgram.Classes[i],
                AsciiBitmap = asciiBitmap
            });
        }

        for (var i = 0; i < irProgram.LiteralCodePointSets.Length; i++)
        {
            literalSetOperandMap[i] = builder.Count;
            builder.Add(new()
            {
                LiteralCodePoints = irProgram.LiteralCodePointSets[i]
            });
        }

        characterSets = builder.ToArray();
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

    private static ExperimentalRegExpBytecodeProgram?[] LowerLookbehindPrograms(
        ExperimentalRegExpIrProgram?[] lookbehindPrograms,
        ScratchRegExpProgram.Node[] lookbehindNodes,
        out int[] lookbehindMinMatchLengths,
        out int[] lookbehindMaxMatchLengths)
    {
        if (lookbehindNodes.Length == 0)
        {
            lookbehindMinMatchLengths = [];
            lookbehindMaxMatchLengths = [];
            return [];
        }

        var lowered = new ExperimentalRegExpBytecodeProgram?[lookbehindNodes.Length];
        lookbehindMinMatchLengths = new int[lookbehindNodes.Length];
        lookbehindMaxMatchLengths = new int[lookbehindNodes.Length];
        Array.Fill(lookbehindMinMatchLengths, -1);
        Array.Fill(lookbehindMaxMatchLengths, -1);
        for (var i = 0; i < lookbehindNodes.Length; i++)
        {
            var node = lookbehindNodes[i];
            if (ScratchRegExpProgram.TryGetNodeMinMatchLength(node, out var minMatchLength))
                lookbehindMinMatchLengths[i] = minMatchLength;
            if (ScratchRegExpProgram.TryGetNodeMaxMatchLength(node, out var maxMatchLength))
                lookbehindMaxMatchLengths[i] = maxMatchLength;

            lowered[i] = TryGenerate(lookbehindPrograms[i]);
        }

        return lowered;
    }

    private static ExperimentalRegExpPropertyEscape[] BuildPropertyEscapes(
        ScratchRegExpProgram.PropertyEscapeNode[] propertyEscapes)
    {
        if (propertyEscapes.Length == 0)
            return [];

        var lowered = new ExperimentalRegExpPropertyEscape[propertyEscapes.Length];
        for (var i = 0; i < propertyEscapes.Length; i++)
            lowered[i] = new(propertyEscapes[i].Kind, propertyEscapes[i].Negated, propertyEscapes[i].Categories,
                propertyEscapes[i].PropertyValue);
        return lowered;
    }

    private static bool TryEmitTailScan(ExperimentalRegExpIrInstruction[] source, int[] classOperandMap,
        ExperimentalRegExpCharacterSet[] characterSets, int index,
        ref PooledArrayBuilder<ExperimentalRegExpInstruction> instructions,
        out int consumedInstructions)
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
            !HasSafeTailScanSuffix(source, index + 3))
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
                var classOperand = classOperandMap[child.Operand];
                instructions.Add(new(characterSets[classOperand].AsciiBitmap.HasValue
                    ? ExperimentalRegExpOpcode.ScanAsciiClassToEnd
                    : ExperimentalRegExpOpcode.ScanClassToEnd, classOperand));
                consumedInstructions = 3;
                return true;
            case ExperimentalRegExpIrOpcode.ClassIgnoreCase:
                instructions.Add(new(ExperimentalRegExpOpcode.ScanClassToEndIgnoreCase, classOperandMap[child.Operand]));
                consumedInstructions = 3;
                return true;
            case ExperimentalRegExpIrOpcode.PropertyEscape:
                instructions.Add(new(ExperimentalRegExpOpcode.ScanPropertyEscapeToEnd, child.Operand));
                consumedInstructions = 3;
                return true;
            case ExperimentalRegExpIrOpcode.PropertyEscapeIgnoreCase:
                instructions.Add(new(ExperimentalRegExpOpcode.ScanPropertyEscapeToEndIgnoreCase, child.Operand));
                consumedInstructions = 3;
                return true;
            default:
                return false;
        }
    }

    private static bool HasSafeTailScanSuffix(ExperimentalRegExpIrInstruction[] source, int startIndex)
    {
        if ((uint)startIndex >= (uint)source.Length)
            return false;

        if (source[startIndex].OpCode == ExperimentalRegExpIrOpcode.Match)
            return true;

        return startIndex + 1 < source.Length &&
               (source[startIndex].OpCode is ExperimentalRegExpIrOpcode.AssertEnd or
                   ExperimentalRegExpIrOpcode.AssertEndMultiline) &&
               source[startIndex + 1].OpCode == ExperimentalRegExpIrOpcode.Match;
    }

    private static ExperimentalRegExpInstruction MapInstruction(ExperimentalRegExpIrInstruction instruction,
        int[] classOperandMap, int[] literalSetOperandMap)
    {
        var opcode = instruction.OpCode switch
        {
            ExperimentalRegExpIrOpcode.Match => ExperimentalRegExpOpcode.Match,
            ExperimentalRegExpIrOpcode.ClearCaptures => ExperimentalRegExpOpcode.ClearCaptures,
            ExperimentalRegExpIrOpcode.SaveLoopPosition => ExperimentalRegExpOpcode.SaveLoopPosition,
            ExperimentalRegExpIrOpcode.BranchIfLoopUnchanged => ExperimentalRegExpOpcode.BranchIfLoopUnchanged,
            ExperimentalRegExpIrOpcode.SaveStart => ExperimentalRegExpOpcode.SaveStart,
            ExperimentalRegExpIrOpcode.SaveEnd => ExperimentalRegExpOpcode.SaveEnd,
            ExperimentalRegExpIrOpcode.BackReference => ExperimentalRegExpOpcode.BackReference,
            ExperimentalRegExpIrOpcode.BackReferenceIgnoreCase => ExperimentalRegExpOpcode.BackReferenceIgnoreCase,
            ExperimentalRegExpIrOpcode.NamedBackReference => ExperimentalRegExpOpcode.NamedBackReference,
            ExperimentalRegExpIrOpcode.NamedBackReferenceIgnoreCase => ExperimentalRegExpOpcode.NamedBackReferenceIgnoreCase,
            ExperimentalRegExpIrOpcode.AssertLookahead => ExperimentalRegExpOpcode.AssertLookahead,
            ExperimentalRegExpIrOpcode.AssertNotLookahead => ExperimentalRegExpOpcode.AssertNotLookahead,
            ExperimentalRegExpIrOpcode.AssertLookbehind => ExperimentalRegExpOpcode.AssertLookbehind,
            ExperimentalRegExpIrOpcode.AssertNotLookbehind => ExperimentalRegExpOpcode.AssertNotLookbehind,
            ExperimentalRegExpIrOpcode.LiteralText => ExperimentalRegExpOpcode.LiteralText,
            ExperimentalRegExpIrOpcode.LiteralSet => ExperimentalRegExpOpcode.LiteralSet,
            ExperimentalRegExpIrOpcode.LiteralSetIgnoreCase => ExperimentalRegExpOpcode.LiteralSetIgnoreCase,
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
            _ => throw new InvalidOperationException($"Unknown regex IR opcode: {instruction.OpCode}")
        };

        var operand = instruction.OpCode switch
        {
            ExperimentalRegExpIrOpcode.Class or ExperimentalRegExpIrOpcode.ClassIgnoreCase => classOperandMap[instruction.Operand],
            ExperimentalRegExpIrOpcode.LiteralSet or ExperimentalRegExpIrOpcode.LiteralSetIgnoreCase => literalSetOperandMap[instruction.Operand],
            _ => instruction.Operand
        };

        return new(opcode, operand, instruction.Operand2);
    }
}

internal static class ExperimentalRegExpVm
{
    public static bool TryMatch(ScratchRegExpProgram treeProgram, ExperimentalRegExpBytecodeProgram program, string input,
        int startIndex, RegExpRuntimeFlags flags, ExperimentalRegExpCaptureState? captureState, out int endIndex)
    {
        return TryMatch(treeProgram, program, input, startIndex, flags, captureState, input.Length, out endIndex);
    }

    public static bool TryMatch(ScratchRegExpProgram treeProgram, ExperimentalRegExpBytecodeProgram program, string input,
        int startIndex, RegExpRuntimeFlags flags, ExperimentalRegExpCaptureState? captureState, int endLimit,
        out int endIndex)
    {
        var stack = new ExperimentalBacktrackStack();
        using var loopState = program.LoopSlotCount == 0 ? null : new ExperimentalRegExpLoopState(program.LoopSlotCount);
        try
        {
            loopState?.Reset();
            return TryMatch(treeProgram, program, input, startIndex, flags, captureState, endLimit, loopState, ref stack,
                out endIndex);
        }
        finally
        {
            stack.Dispose();
        }
    }

    private static bool TryMatch(ScratchRegExpProgram treeProgram, ExperimentalRegExpBytecodeProgram program, string input,
        int startIndex, RegExpRuntimeFlags flags, ExperimentalRegExpCaptureState? captureState, int endLimit,
        ExperimentalRegExpLoopState? loopState, ref ExperimentalBacktrackStack stack, out int endIndex)
    {
        var currentPos = startIndex;
        var instructions = program.Instructions;
        var instructionIndex = 0;
        while (true)
        {
            if ((uint)instructionIndex >= (uint)instructions.Length)
            {
                if (TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                case ExperimentalRegExpOpcode.SaveLoopPosition:
                    loopState?.Save(instruction.Operand, currentPos);
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.BranchIfLoopUnchanged:
                    if (loopState is not null && loopState.IsUnchanged(instruction.Operand, currentPos))
                    {
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.ScanAnyToEnd:
                    currentPos = endLimit;
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.ScanDotToEnd:
                    currentPos = ScratchRegExpMatcher.ScanDotToEndForVm(input, currentPos, flags.Unicode, endLimit);
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.ScanAsciiClassToEnd:
                    var asciiScanCharacterSet = program.CharacterSets[instruction.Operand];
                    currentPos = ScratchRegExpMatcher.ScanAsciiClassToEndForVm(input, currentPos,
                        asciiScanCharacterSet.AsciiBitmap.Low, asciiScanCharacterSet.AsciiBitmap.High, endLimit);
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.ScanClassToEnd:
                case ExperimentalRegExpOpcode.ScanClassToEndIgnoreCase:
                    var scanCharacterSet = program.CharacterSets[instruction.Operand];
                    currentPos = ScratchRegExpMatcher.ScanCharacterSetToEndForVm(input, currentPos,
                        scanCharacterSet, flags with
                        {
                            IgnoreCase = instruction.OpCode == ExperimentalRegExpOpcode.ScanClassToEndIgnoreCase
                        }, endLimit);
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.ScanPropertyEscapeToEnd:
                case ExperimentalRegExpOpcode.ScanPropertyEscapeToEndIgnoreCase:
                    currentPos = ScratchRegExpMatcher.ScanPropertyEscapeToEndForVm(input, currentPos,
                        program.PropertyEscapes[instruction.Operand], flags with
                        {
                            IgnoreCase = instruction.OpCode ==
                                         ExperimentalRegExpOpcode.ScanPropertyEscapeToEndIgnoreCase
                        }, endLimit);
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
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                    var lookaheadMatched = TryMatch(treeProgram, program.LookaheadPrograms[instruction.Operand], input,
                        currentPos, flags, captureState, endLimit, out _);
                    if (instruction.OpCode == ExperimentalRegExpOpcode.AssertLookahead)
                    {
                        if (!lookaheadMatched)
                        {
                            captureState?.Restore(checkpoint);
                            if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                            if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
                            {
                                endIndex = default;
                                return false;
                            }

                            continue;
                        }
                    }

                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.AssertLookbehind:
                case ExperimentalRegExpOpcode.AssertNotLookbehind:
                    checkpoint = captureState?.Checkpoint ?? 0;
                    var lookbehindProgram = program.LookbehindPrograms[instruction.Operand];
                    var lookbehindMatched = lookbehindProgram is not null
                        ? ScratchRegExpMatcher.TryMatchLookbehindForwardProgramForVm(treeProgram, lookbehindProgram,
                            input, currentPos, program.LookbehindFlags[instruction.Operand],
                            program.LookbehindMinMatchLengths[instruction.Operand],
                            program.LookbehindMaxMatchLengths[instruction.Operand], captureState)
                        : ScratchRegExpMatcher.TryMatchLookbehindForVm(treeProgram,
                            program.LookbehindNodes[instruction.Operand], input, currentPos,
                            program.LookbehindFlags[instruction.Operand],
                            program.LookbehindMinMatchLengths[instruction.Operand],
                            program.LookbehindMaxMatchLengths[instruction.Operand], captureState);
                    if (instruction.OpCode == ExperimentalRegExpOpcode.AssertLookbehind)
                    {
                        if (!lookbehindMatched)
                        {
                            captureState?.Restore(checkpoint);
                            if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                        if (lookbehindMatched)
                        {
                            if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    currentPos += literalText.Length;
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.LiteralSet:
                case ExperimentalRegExpOpcode.LiteralSetIgnoreCase:
                    var literalSet = program.CharacterSets[instruction.Operand];
                    if (!ScratchRegExpMatcher.TryMatchLiteralSetForVm(input, currentPos,
                            literalSet.LiteralCodePoints,
                            instruction.OpCode == ExperimentalRegExpOpcode.LiteralSetIgnoreCase,
                            flags.Unicode, endLimit, out var literalSetNextPos))
                    {
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    currentPos = literalSetNextPos;
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.Char:
                case ExperimentalRegExpOpcode.CharIgnoreCase:
                    if (!ScratchRegExpMatcher.TryReadCodePointForVm(input, currentPos, flags.Unicode, endLimit,
                            out var nextPos,
                            out var codePoint) ||
                        !ScratchRegExpMatcher.CodePointEqualsForVm(codePoint, instruction.Operand,
                            instruction.OpCode == ExperimentalRegExpOpcode.CharIgnoreCase))
                    {
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                    if (!ScratchRegExpMatcher.TryReadCodePointForVm(input, currentPos, flags.Unicode, endLimit,
                            out nextPos,
                            out codePoint) ||
                        ScratchRegExpMatcher.IsLineTerminatorForVm(codePoint))
                    {
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                    if (!ScratchRegExpMatcher.TryReadCodePointForVm(input, currentPos, flags.Unicode, endLimit,
                            out nextPos,
                            out _))
                    {
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
                        {
                            endIndex = default;
                            return false;
                        }

                        continue;
                    }

                    currentPos = nextPos;
                    instructionIndex++;
                    break;
                case ExperimentalRegExpOpcode.ClassAscii:
                    var asciiCharacterSet = program.CharacterSets[instruction.Operand];
                    if (!ScratchRegExpMatcher.TryMatchAsciiClassForVm(input, currentPos,
                            asciiCharacterSet.AsciiBitmap.Low, asciiCharacterSet.AsciiBitmap.High,
                            endLimit, out nextPos))
                    {
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                    var classCharacterSet = program.CharacterSets[instruction.Operand];
                    if (!ScratchRegExpMatcher.TryMatchCharacterSetForVm(input, currentPos, classCharacterSet,
                            flags with
                            {
                                IgnoreCase = instruction.OpCode == ExperimentalRegExpOpcode.ClassIgnoreCase
                            }, endLimit, out nextPos))
                    {
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                            }, endLimit, out nextPos))
                    {
                        if (!TryBacktrack(captureState, loopState, ref stack, out instructionIndex, out currentPos))
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
                    stack.Push(instruction.Operand2, currentPos, captureState?.Checkpoint ?? 0, loopState?.Checkpoint ?? 0);
                    instructionIndex = instruction.Operand;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown regex bytecode opcode: {instruction.OpCode}");
            }
        }
    }

    private static bool TryBacktrack(ExperimentalRegExpCaptureState? captureState, ExperimentalRegExpLoopState? loopState,
        ref ExperimentalBacktrackStack stack, out int instructionIndex, out int currentPos)
    {
        if (!stack.TryPop(out instructionIndex, out currentPos, out var captureCheckpoint, out var loopCheckpoint))
            return false;

        captureState?.Restore(captureCheckpoint);
        loopState?.Restore(loopCheckpoint);
        return true;
    }

    private ref struct ExperimentalBacktrackStack
    {
        private int[]? values;
        private int count;

        public void Push(int instructionIndex, int inputPosition, int captureCheckpoint, int loopCheckpoint)
        {
            EnsureCapacity(count + 4);
            values![count++] = instructionIndex;
            values[count++] = inputPosition;
            values[count++] = captureCheckpoint;
            values[count++] = loopCheckpoint;
        }

        public bool TryPop(out int instructionIndex, out int inputPosition, out int captureCheckpoint,
            out int loopCheckpoint)
        {
            if (count == 0)
            {
                instructionIndex = default;
                inputPosition = default;
                captureCheckpoint = default;
                loopCheckpoint = default;
                return false;
            }

            loopCheckpoint = values![--count];
            captureCheckpoint = values[--count];
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

internal sealed class ExperimentalRegExpLoopState : IDisposable
{
    private ExperimentalLoopLogEntry[] logEntries;
    private int logCount;

    public ExperimentalRegExpLoopState(int slotCount)
    {
        Positions = ArrayPool<int>.Shared.Rent(Math.Max(1, slotCount));
        logEntries = ArrayPool<ExperimentalLoopLogEntry>.Shared.Rent(Math.Max(8, slotCount));
        SlotCount = slotCount;
        Reset();
    }

    public int SlotCount { get; }
    public int[] Positions { get; }
    public int Checkpoint => logCount;

    public void Reset()
    {
        Array.Fill(Positions, -1, 0, SlotCount);
        logCount = 0;
    }

    public void Save(int slot, int position)
    {
        EnsureLogCapacity(logCount + 1);
        logEntries[logCount++] = new(slot, Positions[slot]);
        Positions[slot] = position;
    }

    public bool IsUnchanged(int slot, int currentPos)
    {
        return Positions[slot] == currentPos;
    }

    public void Restore(int checkpoint)
    {
        while (logCount > checkpoint)
        {
            var entry = logEntries[--logCount];
            Positions[entry.Slot] = entry.Position;
        }
    }

    public void Dispose()
    {
        ArrayPool<int>.Shared.Return(Positions);
        ArrayPool<ExperimentalLoopLogEntry>.Shared.Return(logEntries);
    }

    private void EnsureLogCapacity(int requiredCount)
    {
        if (logEntries.Length >= requiredCount)
            return;

        var next = ArrayPool<ExperimentalLoopLogEntry>.Shared.Rent(requiredCount * 2);
        Array.Copy(logEntries, next, logCount);
        ArrayPool<ExperimentalLoopLogEntry>.Shared.Return(logEntries);
        logEntries = next;
    }

    private readonly record struct ExperimentalLoopLogEntry(int Slot, int Position);
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
