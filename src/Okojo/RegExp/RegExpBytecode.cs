using Okojo.Internals;
using System.Buffers;

namespace Okojo.RegExp;

internal enum RegExpOpcode : byte
{
    Match,
    ClearCaptures,
    SaveLoopPosition,
    BranchIfLoopUnchanged,
    SetLoopCounter,
    BranchIfLoopCounterZero,
    DecrementLoopCounter,
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

internal readonly record struct RegExpInstruction(RegExpOpcode OpCode, int Operand,
    int Operand2 = 0);

internal sealed class RegExpBytecodeProgram
{
    public required RegExpInstruction[] Instructions { get; init; }
    public int[][] CaptureClearSets { get; init; } = [];
    public int[][] NamedBackReferenceCaptureSets { get; init; } = [];
    public RegExpBytecodeProgram[] LookaheadPrograms { get; init; } = [];
    public RegExpBytecodeProgram?[] LookbehindPrograms { get; init; } = [];
    public ScratchRegExpProgram.Node[] LookbehindNodes { get; init; } = [];
    public RegExpRuntimeFlags[] LookbehindFlags { get; init; } = [];
    public int[] LookbehindMinMatchLengths { get; init; } = [];
    public int[] LookbehindMaxMatchLengths { get; init; } = [];
    public int LoopSlotCount { get; init; }
    public string[] LiteralTexts { get; init; } = [];
    public RegExpCharacterSet[] CharacterSets { get; init; } = [];
    public RegExpPropertyEscape[] PropertyEscapes { get; init; } = [];
}

internal static class RegExpCodeGenerator
{
    public static RegExpBytecodeProgram? TryGenerate(RegExpIrProgram? irProgram)
    {
        if (irProgram is null)
            return null;

        BuildCharacterSets(irProgram, out var characterSets, out var classOperandMap, out var literalSetOperandMap);
        var source = irProgram.Instructions;
        Span<RegExpInstruction> initialInstructionBuffer =
            stackalloc RegExpInstruction[Math.Min(Math.Max(source.Length, 1), 64)];
        var instructions = new PooledArrayBuilder<RegExpInstruction>(initialInstructionBuffer);
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
                if (instruction.OpCode == RegExpIrOpcode.Class &&
                    characterSets[classOperandMap[instruction.Operand]].AsciiBitmap.HasValue)
                {
                    instructions.Add(new(RegExpOpcode.ClassAscii, classOperandMap[instruction.Operand],
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

    private static void BuildCharacterSets(RegExpIrProgram irProgram,
        out RegExpCharacterSet[] characterSets, out int[] classOperandMap, out int[] literalSetOperandMap)
    {
        var builder = new List<RegExpCharacterSet>(irProgram.Classes.Length + irProgram.LiteralCodePointSets.Length);
        classOperandMap = new int[irProgram.Classes.Length];
        literalSetOperandMap = new int[irProgram.LiteralCodePointSets.Length];

        for (var i = 0; i < irProgram.Classes.Length; i++)
        {
            var asciiBitmap = ScratchRegExpProgram.TryBuildAsciiClassBitmap(irProgram.Classes[i], out var lowMask,
                out var highMask)
                ? new RegExpAsciiBitmap(lowMask, highMask)
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

    private static RegExpBytecodeProgram[] LowerLookaheadPrograms(RegExpIrProgram[] lookaheadPrograms)
    {
        if (lookaheadPrograms.Length == 0)
            return [];

        var lowered = new RegExpBytecodeProgram[lookaheadPrograms.Length];
        for (var i = 0; i < lookaheadPrograms.Length; i++)
            lowered[i] = TryGenerate(lookaheadPrograms[i])!;
        return lowered;
    }

    private static RegExpBytecodeProgram?[] LowerLookbehindPrograms(
        RegExpIrProgram?[] lookbehindPrograms,
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

        var lowered = new RegExpBytecodeProgram?[lookbehindNodes.Length];
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

    private static RegExpPropertyEscape[] BuildPropertyEscapes(
        ScratchRegExpProgram.PropertyEscapeNode[] propertyEscapes)
    {
        if (propertyEscapes.Length == 0)
            return [];

        var lowered = new RegExpPropertyEscape[propertyEscapes.Length];
        for (var i = 0; i < propertyEscapes.Length; i++)
            lowered[i] = new(propertyEscapes[i].Kind, propertyEscapes[i].Negated, propertyEscapes[i].Categories,
                propertyEscapes[i].PropertyValue);
        return lowered;
    }

    private static bool TryEmitTailScan(RegExpIrInstruction[] source, int[] classOperandMap,
        RegExpCharacterSet[] characterSets, int index,
        ref PooledArrayBuilder<RegExpInstruction> instructions,
        out int consumedInstructions)
    {
        consumedInstructions = 0;
        if (index + 3 >= source.Length)
            return false;

        var split = source[index];
        if (split.OpCode != RegExpIrOpcode.Split ||
            split.Operand != index + 1 ||
            split.Operand2 != index + 3 ||
            source[index + 2].OpCode != RegExpIrOpcode.Jump ||
            source[index + 2].Operand != index ||
            !HasSafeTailScanSuffix(source, index + 3))
            return false;

        var child = source[index + 1];
        switch (child.OpCode)
        {
            case RegExpIrOpcode.Any:
                instructions.Add(new(RegExpOpcode.ScanAnyToEnd, 0));
                consumedInstructions = 3;
                return true;
            case RegExpIrOpcode.Dot:
                instructions.Add(new(RegExpOpcode.ScanDotToEnd, 0));
                consumedInstructions = 3;
                return true;
            case RegExpIrOpcode.Class:
                var classOperand = classOperandMap[child.Operand];
                instructions.Add(new(characterSets[classOperand].AsciiBitmap.HasValue
                    ? RegExpOpcode.ScanAsciiClassToEnd
                    : RegExpOpcode.ScanClassToEnd, classOperand));
                consumedInstructions = 3;
                return true;
            case RegExpIrOpcode.ClassIgnoreCase:
                instructions.Add(new(RegExpOpcode.ScanClassToEndIgnoreCase, classOperandMap[child.Operand]));
                consumedInstructions = 3;
                return true;
            case RegExpIrOpcode.PropertyEscape:
                instructions.Add(new(RegExpOpcode.ScanPropertyEscapeToEnd, child.Operand));
                consumedInstructions = 3;
                return true;
            case RegExpIrOpcode.PropertyEscapeIgnoreCase:
                instructions.Add(new(RegExpOpcode.ScanPropertyEscapeToEndIgnoreCase, child.Operand));
                consumedInstructions = 3;
                return true;
            default:
                return false;
        }
    }

    private static bool HasSafeTailScanSuffix(RegExpIrInstruction[] source, int startIndex)
    {
        if ((uint)startIndex >= (uint)source.Length)
            return false;

        if (source[startIndex].OpCode == RegExpIrOpcode.Match)
            return true;

        return startIndex + 1 < source.Length &&
               (source[startIndex].OpCode is RegExpIrOpcode.AssertEnd or
                   RegExpIrOpcode.AssertEndMultiline) &&
               source[startIndex + 1].OpCode == RegExpIrOpcode.Match;
    }

    private static RegExpInstruction MapInstruction(RegExpIrInstruction instruction,
        int[] classOperandMap, int[] literalSetOperandMap)
    {
        var opcode = instruction.OpCode switch
        {
            RegExpIrOpcode.Match => RegExpOpcode.Match,
            RegExpIrOpcode.ClearCaptures => RegExpOpcode.ClearCaptures,
            RegExpIrOpcode.SaveLoopPosition => RegExpOpcode.SaveLoopPosition,
            RegExpIrOpcode.BranchIfLoopUnchanged => RegExpOpcode.BranchIfLoopUnchanged,
            RegExpIrOpcode.SetLoopCounter => RegExpOpcode.SetLoopCounter,
            RegExpIrOpcode.BranchIfLoopCounterZero => RegExpOpcode.BranchIfLoopCounterZero,
            RegExpIrOpcode.DecrementLoopCounter => RegExpOpcode.DecrementLoopCounter,
            RegExpIrOpcode.SaveStart => RegExpOpcode.SaveStart,
            RegExpIrOpcode.SaveEnd => RegExpOpcode.SaveEnd,
            RegExpIrOpcode.BackReference => RegExpOpcode.BackReference,
            RegExpIrOpcode.BackReferenceIgnoreCase => RegExpOpcode.BackReferenceIgnoreCase,
            RegExpIrOpcode.NamedBackReference => RegExpOpcode.NamedBackReference,
            RegExpIrOpcode.NamedBackReferenceIgnoreCase => RegExpOpcode.NamedBackReferenceIgnoreCase,
            RegExpIrOpcode.AssertLookahead => RegExpOpcode.AssertLookahead,
            RegExpIrOpcode.AssertNotLookahead => RegExpOpcode.AssertNotLookahead,
            RegExpIrOpcode.AssertLookbehind => RegExpOpcode.AssertLookbehind,
            RegExpIrOpcode.AssertNotLookbehind => RegExpOpcode.AssertNotLookbehind,
            RegExpIrOpcode.LiteralText => RegExpOpcode.LiteralText,
            RegExpIrOpcode.LiteralSet => RegExpOpcode.LiteralSet,
            RegExpIrOpcode.LiteralSetIgnoreCase => RegExpOpcode.LiteralSetIgnoreCase,
            RegExpIrOpcode.Char => RegExpOpcode.Char,
            RegExpIrOpcode.CharIgnoreCase => RegExpOpcode.CharIgnoreCase,
            RegExpIrOpcode.Dot => RegExpOpcode.Dot,
            RegExpIrOpcode.Any => RegExpOpcode.Any,
            RegExpIrOpcode.AssertStart => RegExpOpcode.AssertStart,
            RegExpIrOpcode.AssertStartMultiline => RegExpOpcode.AssertStartMultiline,
            RegExpIrOpcode.AssertEnd => RegExpOpcode.AssertEnd,
            RegExpIrOpcode.AssertEndMultiline => RegExpOpcode.AssertEndMultiline,
            RegExpIrOpcode.AssertWordBoundary => RegExpOpcode.AssertWordBoundary,
            RegExpIrOpcode.AssertWordBoundaryIgnoreCase => RegExpOpcode.AssertWordBoundaryIgnoreCase,
            RegExpIrOpcode.AssertNotWordBoundary => RegExpOpcode.AssertNotWordBoundary,
            RegExpIrOpcode.AssertNotWordBoundaryIgnoreCase => RegExpOpcode.AssertNotWordBoundaryIgnoreCase,
            RegExpIrOpcode.Class => RegExpOpcode.Class,
            RegExpIrOpcode.ClassIgnoreCase => RegExpOpcode.ClassIgnoreCase,
            RegExpIrOpcode.PropertyEscape => RegExpOpcode.PropertyEscape,
            RegExpIrOpcode.PropertyEscapeIgnoreCase => RegExpOpcode.PropertyEscapeIgnoreCase,
            RegExpIrOpcode.Jump => RegExpOpcode.Jump,
            RegExpIrOpcode.Split => RegExpOpcode.Split,
            _ => throw new InvalidOperationException($"Unknown regex IR opcode: {instruction.OpCode}")
        };

        var operand = instruction.OpCode switch
        {
            RegExpIrOpcode.Class or RegExpIrOpcode.ClassIgnoreCase => classOperandMap[instruction.Operand],
            RegExpIrOpcode.LiteralSet or RegExpIrOpcode.LiteralSetIgnoreCase => literalSetOperandMap[instruction.Operand],
            _ => instruction.Operand
        };

        return new(opcode, operand, instruction.Operand2);
    }
}

internal static class RegExpVm
{
    public static bool TryMatch(CompiledProgram compiledProgram, RegExpBytecodeProgram program,
        string input, int startIndex, RegExpRuntimeFlags flags, RegExpCaptureState? captureState,
        out int endIndex)
    {
        return TryMatch(compiledProgram, program, input, startIndex, flags, captureState, input.Length, out endIndex);
    }

    public static bool TryMatch(CompiledProgram compiledProgram, RegExpBytecodeProgram program,
        string input, int startIndex, RegExpRuntimeFlags flags, RegExpCaptureState? captureState,
        int endLimit, out int endIndex)
    {
        var stack = new BacktrackStack();
        using var loopState = program.LoopSlotCount == 0 ? null : new RegExpLoopState(program.LoopSlotCount);
        try
        {
            loopState?.Reset();
            return TryMatch(compiledProgram, program, input, startIndex, flags, captureState, endLimit, loopState,
                ref stack, out endIndex);
        }
        finally
        {
            stack.Dispose();
        }
    }

    private static bool TryMatch(CompiledProgram compiledProgram, RegExpBytecodeProgram program,
        string input, int startIndex, RegExpRuntimeFlags flags, RegExpCaptureState? captureState,
        int endLimit, RegExpLoopState? loopState, ref BacktrackStack stack, out int endIndex)
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
                case RegExpOpcode.Match:
                    endIndex = currentPos;
                    return true;
                case RegExpOpcode.ClearCaptures:
                    if (captureState is not null)
                    {
                        var clearSet = program.CaptureClearSets[instruction.Operand];
                        for (var i = 0; i < clearSet.Length; i++)
                            captureState.Clear(clearSet[i]);
                    }

                    instructionIndex++;
                    break;
                case RegExpOpcode.SaveLoopPosition:
                    loopState?.Save(instruction.Operand, currentPos);
                    instructionIndex++;
                    break;
                case RegExpOpcode.BranchIfLoopUnchanged:
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
                case RegExpOpcode.SetLoopCounter:
                    loopState?.SetCounter(instruction.Operand, instruction.Operand2);
                    instructionIndex++;
                    break;
                case RegExpOpcode.BranchIfLoopCounterZero:
                    if (loopState is not null && loopState.IsCounterZero(instruction.Operand))
                    {
                        instructionIndex = instruction.Operand2;
                        break;
                    }

                    instructionIndex++;
                    break;
                case RegExpOpcode.DecrementLoopCounter:
                    loopState?.DecrementCounter(instruction.Operand);
                    instructionIndex++;
                    break;
                case RegExpOpcode.ScanAnyToEnd:
                    currentPos = endLimit;
                    instructionIndex++;
                    break;
                case RegExpOpcode.ScanDotToEnd:
                    currentPos = ScratchRegExpMatcher.ScanDotToEndForVm(input, currentPos, flags.Unicode, endLimit);
                    instructionIndex++;
                    break;
                case RegExpOpcode.ScanAsciiClassToEnd:
                    var asciiScanCharacterSet = program.CharacterSets[instruction.Operand];
                    currentPos = ScratchRegExpMatcher.ScanAsciiClassToEndForVm(input, currentPos,
                        asciiScanCharacterSet.AsciiBitmap.Low, asciiScanCharacterSet.AsciiBitmap.High, endLimit);
                    instructionIndex++;
                    break;
                case RegExpOpcode.ScanClassToEnd:
                case RegExpOpcode.ScanClassToEndIgnoreCase:
                    var scanCharacterSet = program.CharacterSets[instruction.Operand];
                    currentPos = ScratchRegExpMatcher.ScanCharacterSetToEndForVm(input, currentPos,
                        scanCharacterSet, flags with
                        {
                            IgnoreCase = instruction.OpCode == RegExpOpcode.ScanClassToEndIgnoreCase
                        }, endLimit);
                    instructionIndex++;
                    break;
                case RegExpOpcode.ScanPropertyEscapeToEnd:
                case RegExpOpcode.ScanPropertyEscapeToEndIgnoreCase:
                    currentPos = ScratchRegExpMatcher.ScanPropertyEscapeToEndForVm(input, currentPos,
                        program.PropertyEscapes[instruction.Operand], flags with
                        {
                            IgnoreCase = instruction.OpCode ==
                                         RegExpOpcode.ScanPropertyEscapeToEndIgnoreCase
                        }, endLimit);
                    instructionIndex++;
                    break;
                case RegExpOpcode.SaveStart:
                    captureState?.SaveStart(instruction.Operand, currentPos);
                    instructionIndex++;
                    break;
                case RegExpOpcode.SaveEnd:
                    captureState?.SaveEnd(instruction.Operand, currentPos);
                    instructionIndex++;
                    break;
                case RegExpOpcode.BackReference:
                case RegExpOpcode.BackReferenceIgnoreCase:
                    if (!ScratchRegExpMatcher.TryMatchBackReferenceForVm(input, currentPos, instruction.Operand, flags,
                            instruction.OpCode == RegExpOpcode.BackReferenceIgnoreCase,
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
                case RegExpOpcode.NamedBackReference:
                case RegExpOpcode.NamedBackReferenceIgnoreCase:
                    if (!ScratchRegExpMatcher.TryMatchNamedBackReferenceForVm(input, currentPos,
                            program.NamedBackReferenceCaptureSets[instruction.Operand], flags,
                            instruction.OpCode == RegExpOpcode.NamedBackReferenceIgnoreCase, captureState,
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
                case RegExpOpcode.AssertLookahead:
                case RegExpOpcode.AssertNotLookahead:
                    var checkpoint = captureState?.Checkpoint ?? 0;
                    var lookaheadMatched = TryMatch(compiledProgram, program.LookaheadPrograms[instruction.Operand], input,
                        currentPos, flags, captureState, endLimit, out _);
                    if (instruction.OpCode == RegExpOpcode.AssertLookahead)
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
                case RegExpOpcode.AssertLookbehind:
                case RegExpOpcode.AssertNotLookbehind:
                    checkpoint = captureState?.Checkpoint ?? 0;
                    var lookbehindProgram = program.LookbehindPrograms[instruction.Operand];
                    var lookbehindMatched = lookbehindProgram is not null
                        ? ScratchRegExpMatcher.TryMatchLookbehindForwardProgramForVm(compiledProgram, lookbehindProgram,
                            input, currentPos, program.LookbehindFlags[instruction.Operand],
                            program.LookbehindMinMatchLengths[instruction.Operand],
                            program.LookbehindMaxMatchLengths[instruction.Operand], captureState)
                        : ScratchRegExpMatcher.TryMatchLookbehindForVm(compiledProgram,
                            program.LookbehindNodes[instruction.Operand], input, currentPos,
                            program.LookbehindFlags[instruction.Operand],
                            program.LookbehindMinMatchLengths[instruction.Operand],
                            program.LookbehindMaxMatchLengths[instruction.Operand], captureState);
                    if (instruction.OpCode == RegExpOpcode.AssertLookbehind)
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
                case RegExpOpcode.LiteralText:
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
                case RegExpOpcode.LiteralSet:
                case RegExpOpcode.LiteralSetIgnoreCase:
                    var literalSet = program.CharacterSets[instruction.Operand];
                    if (!ScratchRegExpMatcher.TryMatchLiteralSetForVm(input, currentPos,
                            literalSet.LiteralCodePoints,
                            instruction.OpCode == RegExpOpcode.LiteralSetIgnoreCase,
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
                case RegExpOpcode.Char:
                case RegExpOpcode.CharIgnoreCase:
                    if (!ScratchRegExpMatcher.TryReadCodePointForVm(input, currentPos, flags.Unicode, endLimit,
                            out var nextPos,
                            out var codePoint) ||
                        !ScratchRegExpMatcher.CodePointEqualsForVm(codePoint, instruction.Operand,
                            instruction.OpCode == RegExpOpcode.CharIgnoreCase))
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
                case RegExpOpcode.Dot:
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
                case RegExpOpcode.Any:
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
                case RegExpOpcode.ClassAscii:
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
                case RegExpOpcode.AssertStart:
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
                case RegExpOpcode.AssertStartMultiline:
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
                case RegExpOpcode.AssertEnd:
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
                case RegExpOpcode.AssertEndMultiline:
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
                case RegExpOpcode.AssertWordBoundary:
                case RegExpOpcode.AssertWordBoundaryIgnoreCase:
                    if (!ScratchRegExpMatcher.IsWordBoundaryForVm(input, currentPos, flags.Unicode,
                            instruction.OpCode == RegExpOpcode.AssertWordBoundaryIgnoreCase))
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
                case RegExpOpcode.AssertNotWordBoundary:
                case RegExpOpcode.AssertNotWordBoundaryIgnoreCase:
                    if (ScratchRegExpMatcher.IsWordBoundaryForVm(input, currentPos, flags.Unicode,
                            instruction.OpCode == RegExpOpcode.AssertNotWordBoundaryIgnoreCase))
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
                case RegExpOpcode.Class:
                case RegExpOpcode.ClassIgnoreCase:
                    var classCharacterSet = program.CharacterSets[instruction.Operand];
                    if (!ScratchRegExpMatcher.TryMatchCharacterSetForVm(input, currentPos, classCharacterSet,
                            flags with
                            {
                                IgnoreCase = instruction.OpCode == RegExpOpcode.ClassIgnoreCase
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
                case RegExpOpcode.PropertyEscape:
                case RegExpOpcode.PropertyEscapeIgnoreCase:
                    if (!ScratchRegExpMatcher.TryMatchPropertyEscapeForVm(input, currentPos,
                            program.PropertyEscapes[instruction.Operand], flags with
                            {
                                IgnoreCase = instruction.OpCode == RegExpOpcode.PropertyEscapeIgnoreCase
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
                case RegExpOpcode.Jump:
                    instructionIndex = instruction.Operand;
                    break;
                case RegExpOpcode.Split:
                    stack.Push(instruction.Operand2, currentPos, captureState?.Checkpoint ?? 0, loopState?.Checkpoint ?? 0);
                    instructionIndex = instruction.Operand;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown regex bytecode opcode: {instruction.OpCode}");
            }
        }
    }

    private static bool TryBacktrack(RegExpCaptureState? captureState, RegExpLoopState? loopState,
        ref BacktrackStack stack, out int instructionIndex, out int currentPos)
    {
        if (!stack.TryPop(out instructionIndex, out currentPos, out var captureCheckpoint, out var loopCheckpoint))
            return false;

        captureState?.Restore(captureCheckpoint);
        loopState?.Restore(loopCheckpoint);
        return true;
    }

    private ref struct BacktrackStack
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

internal sealed class RegExpLoopState : IDisposable
{
    private LoopLogEntry[] logEntries;
    private int logCount;

    public RegExpLoopState(int slotCount)
    {
        Positions = ArrayPool<int>.Shared.Rent(Math.Max(1, slotCount));
        Counters = ArrayPool<int>.Shared.Rent(Math.Max(1, slotCount));
        logEntries = ArrayPool<LoopLogEntry>.Shared.Rent(Math.Max(8, slotCount));
        SlotCount = slotCount;
        Reset();
    }

    public int SlotCount { get; }
    public int[] Positions { get; }
    public int[] Counters { get; }
    public int Checkpoint => logCount;

    public void Reset()
    {
        Array.Fill(Positions, -1, 0, SlotCount);
        Array.Clear(Counters, 0, SlotCount);
        logCount = 0;
    }

    public void Save(int slot, int position)
    {
        EnsureLogCapacity(logCount + 1);
        logEntries[logCount++] = new(slot, Positions[slot], LoopValueKind.Position);
        Positions[slot] = position;
    }

    public bool IsUnchanged(int slot, int currentPos)
    {
        return Positions[slot] == currentPos;
    }

    public void SetCounter(int slot, int value)
    {
        EnsureLogCapacity(logCount + 1);
        logEntries[logCount++] = new(slot, Counters[slot], LoopValueKind.Counter);
        Counters[slot] = value;
    }

    public bool IsCounterZero(int slot)
    {
        return Counters[slot] == 0;
    }

    public void DecrementCounter(int slot)
    {
        EnsureLogCapacity(logCount + 1);
        logEntries[logCount++] = new(slot, Counters[slot], LoopValueKind.Counter);
        Counters[slot]--;
    }

    public void Restore(int checkpoint)
    {
        while (logCount > checkpoint)
        {
            var entry = logEntries[--logCount];
            if (entry.Kind == LoopValueKind.Position)
                Positions[entry.Slot] = entry.PreviousValue;
            else
                Counters[entry.Slot] = entry.PreviousValue;
        }
    }

    public void Dispose()
    {
        ArrayPool<int>.Shared.Return(Positions);
        ArrayPool<int>.Shared.Return(Counters);
        ArrayPool<LoopLogEntry>.Shared.Return(logEntries);
    }

    private void EnsureLogCapacity(int requiredCount)
    {
        if (logEntries.Length >= requiredCount)
            return;

        var next = ArrayPool<LoopLogEntry>.Shared.Rent(requiredCount * 2);
        Array.Copy(logEntries, next, logCount);
        ArrayPool<LoopLogEntry>.Shared.Return(logEntries);
        logEntries = next;
    }

    private enum LoopValueKind : byte
    {
        Position,
        Counter
    }

    private readonly record struct LoopLogEntry(int Slot, int PreviousValue, LoopValueKind Kind);
}

internal sealed class RegExpCaptureState : IDisposable
{
    private CaptureLogEntry[] logEntries;
    private int logCount;

    public RegExpCaptureState(int captureCount)
    {
        Starts = ArrayPool<int>.Shared.Rent(captureCount + 1);
        Ends = ArrayPool<int>.Shared.Rent(captureCount + 1);
        Matched = ArrayPool<bool>.Shared.Rent(captureCount + 1);
        logEntries = ArrayPool<CaptureLogEntry>.Shared.Rent(Math.Max(8, captureCount + 1));
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
        ArrayPool<CaptureLogEntry>.Shared.Return(logEntries);
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

        var next = ArrayPool<CaptureLogEntry>.Shared.Rent(requiredCount * 2);
        Array.Copy(logEntries, next, logCount);
        ArrayPool<CaptureLogEntry>.Shared.Return(logEntries);
        logEntries = next;
    }

    private readonly record struct CaptureLogEntry(int Index, int Start, int End, bool Matched);
}
