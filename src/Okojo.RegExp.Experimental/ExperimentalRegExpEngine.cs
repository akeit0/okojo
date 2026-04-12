namespace Okojo.RegExp.Experimental;

public sealed class ExperimentalRegExpEngine : IRegExpEngine
{
    public static ExperimentalRegExpEngine Default { get; } = new();

    public RegExpCompiledPattern Compile(string pattern, string flags)
    {
        var parsedFlags = ScratchRegExpProgram.ParseFlags(flags);
        var canonicalFlags = ScratchRegExpProgram.CanonicalizeFlags(parsedFlags);
        var program = ScratchRegExpProgram.Parse(pattern, parsedFlags);
        var compiledProgram = ExperimentalCompiledProgram.Create(program);
        return new(pattern, canonicalFlags, pattern, program.NamedGroupNames, parsedFlags)
        {
            EngineState = compiledProgram
        };
    }

    public RegExpMatchResult? Exec(RegExpCompiledPattern compiled, string input, int startIndex)
    {
        if (compiled.EngineState is not ExperimentalCompiledProgram program)
            throw new ArgumentException("Compiled pattern was not created by ExperimentalRegExpEngine.", nameof(compiled));

        return ScratchRegExpMatcher.Exec(program, input, startIndex);
    }
}
