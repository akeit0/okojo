namespace Okojo.RegExp.Experimental;

internal enum ExperimentalRegExpOpcode : byte
{
    Match,
    Char,
    CharIgnoreCase
}

internal readonly record struct ExperimentalRegExpInstruction(ExperimentalRegExpOpcode OpCode, int Operand);

internal sealed class ExperimentalRegExpBytecodeProgram
{
    public required ExperimentalRegExpInstruction[] Instructions { get; init; }
}

internal static class ExperimentalRegExpCodeGenerator
{
    public static ExperimentalRegExpBytecodeProgram? TryGenerate(ScratchRegExpProgram treeProgram)
    {
        if (!treeProgram.HasExactLiteralPattern)
            return null;

        var exactLiteral = treeProgram.ExactLiteralCodePoints;
        if (exactLiteral.Length == 0)
            return null;

        var instructions = new ExperimentalRegExpInstruction[exactLiteral.Length + 1];
        var charOpcode = treeProgram.Flags.IgnoreCase
            ? ExperimentalRegExpOpcode.CharIgnoreCase
            : ExperimentalRegExpOpcode.Char;
        for (var i = 0; i < exactLiteral.Length; i++)
            instructions[i] = new(charOpcode, exactLiteral[i]);
        instructions[^1] = new(ExperimentalRegExpOpcode.Match, 0);
        return new()
        {
            Instructions = instructions
        };
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
                default:
                    throw new InvalidOperationException($"Unknown regex bytecode opcode: {instruction.OpCode}");
            }
        }

        endIndex = default;
        return false;
    }
}
