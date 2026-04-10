using System.Text;
using Microsoft.CodeAnalysis;

namespace Okojo.DocGenerator.Cli;

internal static class DeclarationFileNameHelper
{
    public static string GetFileName(INamedTypeSymbol symbol)
    {
        return NormalizeRelativePath(GetDefaultBasePath(symbol));
    }

    public static string GetFileName(INamedTypeSymbol symbol, string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return NormalizeRelativePath(configuredPath);
        return GetFileName(symbol);
    }

    private static string GetDefaultBasePath(INamedTypeSymbol symbol)
    {
        var fullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal);

        var sb = new StringBuilder(fullName.Length);
        foreach (var ch in fullName)
            sb.Append(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_');
        return sb.ToString();
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('/', '\\').Trim();
        while (normalized.StartsWith("\\", StringComparison.Ordinal))
            normalized = normalized[1..];
        normalized = normalized.Replace("..", "__", StringComparison.Ordinal);
        if (normalized.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^5];
        else if (normalized.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^3];
        return normalized + ".d.ts";
    }
}
