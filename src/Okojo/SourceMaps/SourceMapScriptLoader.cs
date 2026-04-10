using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Okojo.SourceMaps;

public sealed partial class SourceMapScriptLoader(SourceMapRegistry registry)
{
    public void TryRegister(string generatedSourcePath, string sourceText)
    {
        if (string.IsNullOrEmpty(generatedSourcePath) || string.IsNullOrEmpty(sourceText))
            return;

        generatedSourcePath = Path.GetFullPath(generatedSourcePath);
        if (registry.TryGetDocument(generatedSourcePath, out _))
            return;

        if (!TryGetSourceMappingUrl(sourceText, out var sourceMappingUrl))
            return;

        try
        {
            string sourceMapJson;
            string? sourceMapPath = null;
            if (sourceMappingUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryDecodeInlineDataUrl(sourceMappingUrl, out sourceMapJson))
                    return;
            }
            else
            {
                sourceMapPath = ResolveSourceMapPath(generatedSourcePath, sourceMappingUrl);
                if (!File.Exists(sourceMapPath))
                    return;

                sourceMapJson = File.ReadAllText(sourceMapPath);
            }

            registry.Register(SourceMapParser.Parse(sourceMapJson, generatedSourcePath, sourceMapPath));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (FormatException)
        {
        }
        catch (JsonException)
        {
        }
    }

    public static bool TryGetSourceMappingUrl(string sourceText, out string sourceMappingUrl)
    {
        sourceMappingUrl = string.Empty;
        var matches = SourceMappingUrlRegex().Matches(sourceText);
        if (matches.Count == 0)
            return false;

        sourceMappingUrl = matches[^1].Groups["url"].Value.Trim();
        return sourceMappingUrl.Length != 0;
    }

    public static string ResolveSourceMapPath(string generatedSourcePath, string sourceMappingUrl)
    {
        if (Uri.TryCreate(sourceMappingUrl, UriKind.Absolute, out var absoluteUri) && absoluteUri.IsFile)
            return Path.GetFullPath(absoluteUri.LocalPath);

        var basePath = Path.GetDirectoryName(generatedSourcePath)!;
        return Path.GetFullPath(sourceMappingUrl, basePath);
    }

    public static bool TryDecodeInlineDataUrl(string sourceMappingUrl, out string sourceMapJson)
    {
        sourceMapJson = string.Empty;
        var commaIndex = sourceMappingUrl.IndexOf(',');
        if (commaIndex < 0)
            return false;

        var metadata = sourceMappingUrl[..commaIndex];
        var payload = sourceMappingUrl[(commaIndex + 1)..];
        if (!metadata.Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            sourceMapJson = Uri.UnescapeDataString(payload);
            return true;
        }

        try
        {
            sourceMapJson = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    [GeneratedRegex(
        @"(?://[@#]\s*sourceMappingURL=(?<url>\S+)|/\*[#@]\s*sourceMappingURL=(?<url>[^*\r\n]+)\*/)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SourceMappingUrlRegex();
}
