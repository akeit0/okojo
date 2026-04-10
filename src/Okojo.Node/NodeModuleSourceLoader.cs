using System.Text;
using System.Text.RegularExpressions;
using Okojo.Runtime;
using Okojo.SourceMaps;

namespace Okojo.Node;

internal sealed partial class NodeModuleSourceLoader(
    IModuleSourceLoader inner,
    SourceMapRegistry? sourceMapRegistry = null)
    : IModuleSourceLoader
{
    private const string NodeHostImportSymbolAccessor = "globalThis[Symbol.for(\"node.host.import\")]";
    private readonly NodeModuleFormatResolver formatResolver = new(inner.LoadSource);
    private readonly NodeCommonJsResolver resolver = new(inner.ResolveSpecifier, inner.LoadSource);

    private readonly NodeSourceMapLoader? sourceMapLoader =
        sourceMapRegistry is null ? null : new NodeSourceMapLoader(sourceMapRegistry);

    public string ResolveSpecifier(string specifier, string? referrer)
    {
        if (NodeBuiltInModuleSource.IsBuiltInSpecifier(specifier))
            return NodeBuiltInModuleSource.Canonicalize(specifier);

        return resolver.ResolveImport(specifier, referrer);
    }

    public string LoadSource(string resolvedId)
    {
        if (NodeBuiltInModuleSource.IsBuiltInSpecifier(resolvedId))
            return NodeBuiltInModuleSource.GetModuleSource(NodeBuiltInModuleSource.Canonicalize(resolvedId));

        if (formatResolver.DetermineFormat(resolvedId) == NodeModuleFormat.CommonJs)
            return CreateCommonJsImportModuleSource(
                resolvedId,
                DiscoverCommonJsExportNames(resolvedId));

        var source = inner.LoadSource(resolvedId);
        TryRegisterSourceMap(resolvedId, source);
        return source;
    }

    public string LoadRawSource(string resolvedId)
    {
        if (NodeBuiltInModuleSource.IsBuiltInSpecifier(resolvedId))
            return NodeBuiltInModuleSource.GetModuleSource(NodeBuiltInModuleSource.Canonicalize(resolvedId));

        var source = inner.LoadSource(resolvedId);
        TryRegisterSourceMap(resolvedId, source);
        return source;
    }

    public NodeModuleFormat DetermineFormat(string resolvedId)
    {
        return formatResolver.DetermineFormat(resolvedId);
    }

    private static string CreateCommonJsImportModuleSource(string resolvedId, IReadOnlyList<string> exportNames)
    {
        var escapedResolvedId = EscapeJavaScriptStringLiteral(resolvedId);
        var builder = new StringBuilder();
        builder.Append("const nodeCjsDefault = ")
            .Append(NodeHostImportSymbolAccessor)
            .Append("(\"")
            .Append(escapedResolvedId)
            .AppendLine("\");");
        builder.AppendLine("export default nodeCjsDefault;");

        for (var i = 0; i < exportNames.Count; i++)
        {
            var exportName = exportNames[i];
            if (!IsValidExportIdentifier(exportName))
                continue;

            builder.Append("export const ")
                .Append(exportName)
                .Append(" = nodeCjsDefault.")
                .Append(exportName)
                .AppendLine(";");
        }

        return builder.ToString();
    }

    private static string EscapeJavaScriptStringLiteral(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private IReadOnlyList<string> DiscoverCommonJsExportNames(string resolvedId)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        DiscoverCommonJsExportNamesCore(resolvedId, names, new(StringComparer.Ordinal));
        return names.Count == 0 ? Array.Empty<string>() : names.ToArray();
    }

    private void DiscoverCommonJsExportNamesCore(string resolvedId, HashSet<string> names, HashSet<string> visited)
    {
        if (!visited.Add(resolvedId))
            return;

        string source;
        try
        {
            source = LoadRawSource(resolvedId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException
                                       or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (Match match in NamedExportAssignmentRegex().Matches(source))
            names.Add(match.Groups[1].Value);

        if (names.Count != 0)
            return;

        foreach (Match match in RequireAssignmentRegex().Matches(source))
        {
            var requiredSpecifier = match.Groups[1].Value;
            if (!requiredSpecifier.StartsWith("./", StringComparison.Ordinal) &&
                !requiredSpecifier.StartsWith("../", StringComparison.Ordinal))
                continue;

            var requiredResolvedId = resolver.ResolveRequire(requiredSpecifier, resolvedId);
            DiscoverCommonJsExportNamesCore(requiredResolvedId, names, visited);
            if (names.Count != 0)
                return;
        }
    }

    private static bool IsValidExportIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || string.Equals(name, "default", StringComparison.Ordinal))
            return false;

        if (!(char.IsLetter(name[0]) || name[0] == '_' || name[0] == '$'))
            return false;

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '$'))
                return false;
        }

        return true;
    }

    internal void TryRegisterSourceMap(string resolvedId, string source)
    {
        if (NodeBuiltInModuleSource.IsBuiltInSpecifier(resolvedId))
            return;

        sourceMapLoader?.TryRegister(resolvedId, source);
    }

    [GeneratedRegex(
        @"(?:^|[;\r\n\s])(?:exports|module\.exports)\.([A-Za-z_$][A-Za-z0-9_$]*)\s*=",
        RegexOptions.CultureInvariant)]
    private static partial Regex NamedExportAssignmentRegex();

    [GeneratedRegex(
        @"module\.exports\s*=\s*require\(\s*['""]([^'""]+)['""]\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex RequireAssignmentRegex();
}
