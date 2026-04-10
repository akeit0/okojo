namespace Okojo.Runtime;

internal sealed class ModuleDiagnostic(
    string code,
    string message,
    string resolvedId,
    int position,
    int line,
    int column)
{
    public string Code { get; } = code;
    public string Message { get; } = message;
    public string ResolvedId { get; } = resolvedId;
    public int Position { get; } = position;
    public int Line { get; } = line;
    public int Column { get; } = column;
}
