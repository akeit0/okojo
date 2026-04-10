namespace Okojo.RegExp;

public sealed record RegExpCompiledPattern(
    string Pattern,
    string Flags,
    string ExecutionPattern,
    string[] NamedGroupNames,
    RegExpRuntimeFlags ParsedFlags)
{
    internal object? EngineState { get; init; }
}
