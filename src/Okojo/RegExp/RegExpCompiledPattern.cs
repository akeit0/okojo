namespace Okojo.RegExp;

internal sealed record RegExpCompiledPattern(
    string Pattern,
    string Flags,
    string ExecutionPattern,
    string[] NamedGroupNames,
    RegExpRuntimeFlags ParsedFlags)
{
    /// <summary>
    ///     Engine-private compiled state attached by the <see cref="RegExpEngine"/>
    ///     implementation that produced this pattern. Not part of the stable API surface.
    /// </summary>
    internal object? EngineState { get; init; }
}
