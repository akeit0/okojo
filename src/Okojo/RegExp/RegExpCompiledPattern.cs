namespace Okojo.RegExp;

public sealed record RegExpCompiledPattern(
    string Pattern,
    string Flags,
    string ExecutionPattern,
    string[] NamedGroupNames,
    RegExpRuntimeFlags ParsedFlags)
{
    /// <summary>
    ///     Engine-private compiled state attached by the <see cref="IRegExpEngine"/>
    ///     implementation that produced this pattern. Not part of the stable API surface.
    /// </summary>
    public object? EngineState { get; init; }
}
