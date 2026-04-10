using System.Text.Json;

namespace Okojo.Node;

internal enum NodeModuleFormat
{
    CommonJs = 0,
    EsModule = 1
}

internal sealed class NodeModuleFormatResolver(Func<string, string> loadSource)
{
    private readonly Func<string, string> loadSource = loadSource;

    public NodeModuleFormat DetermineFormat(string resolvedId)
    {
        var normalized = NormalizePath(resolvedId);

        if (normalized.EndsWith(".mjs", StringComparison.Ordinal))
            return NodeModuleFormat.EsModule;

        if (normalized.EndsWith(".cjs", StringComparison.Ordinal))
            return NodeModuleFormat.CommonJs;

        if (normalized.EndsWith(".js", StringComparison.Ordinal))
        {
            var packageType = TryGetNearestPackageType(normalized);
            if (string.Equals(packageType, "module", StringComparison.Ordinal))
                return NodeModuleFormat.EsModule;
        }

        return NodeModuleFormat.CommonJs;
    }

    private string? TryGetNearestPackageType(string moduleResolvedId)
    {
        var currentDirectory = GetDirectoryName(moduleResolvedId);
        while (true)
        {
            var packageJsonPath = CombinePath(currentDirectory, "package.json");
            if (TryLoadSource(packageJsonPath, out var packageJson))
                return TryGetPackageType(packageJson!);

            if (currentDirectory == "/")
                return null;

            currentDirectory = GetDirectoryName(currentDirectory);
        }
    }

    private bool TryLoadSource(string resolvedId, out string? source)
    {
        try
        {
            source = loadSource(resolvedId);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException
                                       or DirectoryNotFoundException)
        {
            source = null;
            return false;
        }
    }

    private static string? TryGetPackageType(string packageJsonSource)
    {
        try
        {
            using var document = JsonDocument.Parse(packageJsonSource);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!document.RootElement.TryGetProperty("type", out var typeElement))
                return null;

            return typeElement.ValueKind == JsonValueKind.String ? typeElement.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string CombinePath(string left, string right)
    {
        var normalizedLeft = NormalizePath(left).TrimEnd('/');
        var normalizedRight = NormalizePath(right).TrimStart('/');
        if (string.IsNullOrEmpty(normalizedLeft) || normalizedLeft == "/")
            return "/" + normalizedRight;
        return normalizedLeft + "/" + normalizedRight;
    }

    private static string GetDirectoryName(string path)
    {
        var normalized = NormalizePath(path).TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        if (slash <= 0)
            return "/";
        return normalized[..slash];
    }
}
