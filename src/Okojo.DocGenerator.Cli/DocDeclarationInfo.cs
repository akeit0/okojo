namespace Okojo.DocGenerator.Cli;

internal sealed class DocDeclarationInfo
{
    public static readonly DocDeclarationInfo Empty = new();

    public string? FileName { get; init; }
    public string? Namespace { get; init; }
}
