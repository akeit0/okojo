namespace Okojo.RegExp;

public sealed class RegExpEngine : IRegExpEngine
{
    public static RegExpEngine Default { get; } = new();

    public RegExpCompiledPattern Compile(string pattern, string flags)
    {
        var parsedFlags = ScratchRegExpProgram.ParseFlags(flags);
        var canonicalFlags = ScratchRegExpProgram.CanonicalizeFlags(parsedFlags);
        var program = ScratchRegExpProgram.Parse(pattern, parsedFlags);
        var compiledProgram = CompiledProgram.Create(program);
        return new(pattern, canonicalFlags, pattern, program.NamedGroupNames, parsedFlags)
        {
            EngineState = compiledProgram
        };
    }

    public RegExpMatchResult? Exec(RegExpCompiledPattern compiled, string input, int startIndex)
    {
        if (compiled.EngineState is not CompiledProgram program)
            throw new ArgumentException("Compiled pattern was not created by RegExpEngine.", nameof(compiled));

        return ScratchRegExpMatcher.Exec(program, input, startIndex);
    }
}
