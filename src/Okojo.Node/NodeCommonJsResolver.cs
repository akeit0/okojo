using System.Text.Json;

namespace Okojo.Node;

internal sealed class NodeCommonJsResolver(
    Func<string, string?, string> resolveSpecifier,
    Func<string, string> loadSource)
{
    private static readonly string[] RequirePathExtensions = [".js"];
    private static readonly string[] RequireIndexCandidates = ["index.js"];
    private static readonly string[] MainPathExtensions = [".js", ".cjs", ".mjs"];
    private static readonly string[] MainIndexCandidates = ["index.js", "index.cjs", "index.mjs"];
    private static readonly string[] RequireConditions = ["node", "require", "default"];
    private static readonly string[] ImportPathExtensions = [".js", ".mjs", ".cjs"];
    private static readonly string[] ImportIndexCandidates = ["index.js", "index.mjs", "index.cjs"];
    private static readonly string[] ImportConditions = ["node", "import", "default"];
    private readonly Func<string, string> loadSource = loadSource;
    private readonly Func<string, string?, string> resolveSpecifier = resolveSpecifier;

    public string ResolveRequire(string specifier, string? referrer)
    {
        if (IsInternalImportsSpecifier(specifier))
            return ResolvePackageImports(specifier, referrer, RequirePathExtensions, RequireIndexCandidates,
                       RequireConditions)
                   ?? throw CreateModuleNotFoundException(specifier, referrer);

        if (IsRelativeOrAbsoluteSpecifier(specifier))
        {
            var resolved = resolveSpecifier(specifier, referrer);
            return ResolveAsPath(resolved, RequirePathExtensions, RequireIndexCandidates) ??
                   ResolveRealPathIfPossible(resolved);
        }

        return ResolveAsPackage(specifier, referrer, RequirePathExtensions, RequireIndexCandidates)
               ?? throw CreateModuleNotFoundException(specifier, referrer);
    }

    public string ResolveMain(string specifier, string? referrer)
    {
        if (IsInternalImportsSpecifier(specifier))
            return ResolvePackageImports(specifier, referrer, MainPathExtensions, MainIndexCandidates,
                       RequireConditions)
                   ?? throw CreateModuleNotFoundException(specifier, referrer);

        if (IsRelativeOrAbsoluteSpecifier(specifier))
        {
            var resolved = resolveSpecifier(specifier, referrer);
            return ResolveAsPath(resolved, MainPathExtensions, MainIndexCandidates) ??
                   ResolveRealPathIfPossible(resolved);
        }

        return ResolveAsPackage(specifier, referrer, MainPathExtensions, MainIndexCandidates)
               ?? throw CreateModuleNotFoundException(specifier, referrer);
    }

    public string ResolveImport(string specifier, string? referrer)
    {
        if (IsInternalImportsSpecifier(specifier))
            return ResolvePackageImports(specifier, referrer, ImportPathExtensions, ImportIndexCandidates,
                       ImportConditions)
                   ?? throw CreateModuleNotFoundException(specifier, referrer);

        if (IsRelativeOrAbsoluteSpecifier(specifier))
        {
            var resolved = resolveSpecifier(specifier, referrer);
            return ResolveAsPath(resolved, ImportPathExtensions, ImportIndexCandidates) ??
                   ResolveRealPathIfPossible(resolved);
        }

        return ResolveAsPackage(specifier, referrer, ImportPathExtensions, ImportIndexCandidates, ImportConditions)
               ?? throw CreateModuleNotFoundException(specifier, referrer);
    }

    private string? ResolveAsPackage(
        string specifier,
        string? referrer,
        ReadOnlySpan<string> pathExtensions,
        ReadOnlySpan<string> indexCandidates)
    {
        return ResolveAsPackage(specifier, referrer, pathExtensions, indexCandidates, RequireConditions);
    }

    private string? ResolveAsPackage(
        string specifier,
        string? referrer,
        ReadOnlySpan<string> pathExtensions,
        ReadOnlySpan<string> indexCandidates,
        ReadOnlySpan<string> conditions)
    {
        var (packageName, packageSubpath) = SplitPackageSpecifier(specifier);
        foreach (var nodeModulesDirectory in EnumerateNodeModulesDirectories(referrer))
        {
            var packageRoot = CombinePath(nodeModulesDirectory, packageName);
            var packageJsonPath = CombinePath(packageRoot, "package.json");
            if (TryLoadSource(packageJsonPath, out var packageJsonSource))
            {
                if (TryResolvePackageExports(packageRoot, packageSubpath, packageJsonSource!, pathExtensions,
                        indexCandidates, conditions,
                        out var exportsResolved))
                    return exportsResolved;

                if (HasPackageExports(packageJsonSource!))
                    throw CreatePackagePathNotExportedException(specifier, packageJsonPath);
            }

            var candidate = packageSubpath is null
                ? packageRoot
                : CombinePath(packageRoot, packageSubpath);

            var resolved = ResolveAsPath(candidate, pathExtensions, indexCandidates);
            if (resolved is not null)
                return resolved;
        }

        return null;
    }

    private string? ResolvePackageImports(
        string specifier,
        string? referrer,
        ReadOnlySpan<string> pathExtensions,
        ReadOnlySpan<string> indexCandidates,
        ReadOnlySpan<string> conditions)
    {
        foreach (var (packageRoot, packageJsonPath, packageJsonSource) in EnumerateContainingPackages(referrer))
        {
            if (TryResolvePackageImports(packageRoot, specifier, packageJsonSource, pathExtensions, indexCandidates,
                    conditions,
                    out var resolved))
                return resolved;

            if (HasPackageImports(packageJsonSource))
                throw CreatePackageImportNotDefinedException(specifier, packageJsonPath);
        }

        return null;
    }

    private bool TryResolvePackageExports(
        string packageRoot,
        string? packageSubpath,
        string packageJsonSource,
        ReadOnlySpan<string> pathExtensions,
        ReadOnlySpan<string> indexCandidates,
        ReadOnlySpan<string> conditions,
        out string? resolvedPath)
    {
        resolvedPath = null;

        try
        {
            using var document = JsonDocument.Parse(packageJsonSource);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("exports", out var exportsElement))
                return false;

            var target = ResolveExportsTarget(exportsElement, packageSubpath, conditions);
            if (string.IsNullOrWhiteSpace(target))
                return false;

            var targetPath = CombinePath(packageRoot, target!);
            resolvedPath = ResolveAsPath(targetPath, pathExtensions, indexCandidates);
            return resolvedPath is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool TryResolvePackageImports(
        string packageRoot,
        string specifier,
        string packageJsonSource,
        ReadOnlySpan<string> pathExtensions,
        ReadOnlySpan<string> indexCandidates,
        ReadOnlySpan<string> conditions,
        out string? resolvedPath)
    {
        resolvedPath = null;

        try
        {
            using var document = JsonDocument.Parse(packageJsonSource);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("imports", out var importsElement))
                return false;

            var target = ResolveImportsTarget(importsElement, specifier, conditions);
            if (string.IsNullOrWhiteSpace(target))
                return false;

            var targetPath = CombinePath(packageRoot, target!);
            resolvedPath = ResolveAsPath(targetPath, pathExtensions, indexCandidates);
            return resolvedPath is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasPackageExports(string packageJsonSource)
    {
        try
        {
            using var document = JsonDocument.Parse(packageJsonSource);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("exports", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasPackageImports(string packageJsonSource)
    {
        try
        {
            using var document = JsonDocument.Parse(packageJsonSource);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("imports", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ResolveExportsTarget(JsonElement exportsElement, string? packageSubpath,
        ReadOnlySpan<string> conditions)
    {
        if (exportsElement.ValueKind == JsonValueKind.String)
            return packageSubpath is null ? exportsElement.GetString() : null;

        if (exportsElement.ValueKind != JsonValueKind.Object)
            return null;

        if (LooksLikeConditionMap(exportsElement))
            return packageSubpath is null ? ResolveConditionalTarget(exportsElement, conditions) : null;

        var exportKey = packageSubpath is null ? "." : "./" + packageSubpath.Replace('\\', '/');
        if (!exportsElement.TryGetProperty(exportKey, out var subpathEntry))
            return null;

        return ResolveExportsEntry(subpathEntry, conditions);
    }

    private static string? ResolveImportsTarget(JsonElement importsElement, string specifier,
        ReadOnlySpan<string> conditions)
    {
        if (importsElement.ValueKind != JsonValueKind.Object)
            return null;

        if (!importsElement.TryGetProperty(specifier, out var importEntry))
            return null;

        return ResolveExportsEntry(importEntry, conditions);
    }

    private static string? ResolveExportsEntry(JsonElement entry, ReadOnlySpan<string> conditions)
    {
        return entry.ValueKind switch
        {
            JsonValueKind.String => entry.GetString(),
            JsonValueKind.Array => ResolveTargetArray(entry, conditions),
            JsonValueKind.Object => ResolveConditionalTarget(entry, conditions),
            _ => null
        };
    }

    private static string? ResolveTargetArray(JsonElement entryArray, ReadOnlySpan<string> conditions)
    {
        foreach (var item in entryArray.EnumerateArray())
        {
            var target = ResolveExportsEntry(item, conditions);
            if (!string.IsNullOrWhiteSpace(target))
                return target;
        }

        return null;
    }

    private static string? ResolveConditionalTarget(JsonElement conditionObject, ReadOnlySpan<string> conditions)
    {
        foreach (var property in conditionObject.EnumerateObject())
        {
            if (!conditions.Contains(property.Name, StringComparer.Ordinal))
                continue;

            var target = ResolveExportsEntry(property.Value, conditions);
            if (!string.IsNullOrWhiteSpace(target))
                return target;
        }

        return null;
    }

    private static bool LooksLikeConditionMap(JsonElement exportsObject)
    {
        foreach (var property in exportsObject.EnumerateObject())
            return !property.Name.StartsWith(".", StringComparison.Ordinal);
        return false;
    }

    private string? ResolveAsPath(
        string path,
        ReadOnlySpan<string> pathExtensions,
        ReadOnlySpan<string> indexCandidates)
    {
        if (TryLoadSource(path))
            return ResolveRealPathIfPossible(path);

        foreach (var extension in pathExtensions)
        {
            var candidate = path + extension;
            if (TryLoadSource(candidate))
                return ResolveRealPathIfPossible(candidate);
        }

        return ResolveAsDirectory(path, pathExtensions, indexCandidates);
    }

    private string? ResolveAsDirectory(
        string directoryPath,
        ReadOnlySpan<string> pathExtensions,
        ReadOnlySpan<string> indexCandidates)
    {
        var packageJsonPath = CombinePath(directoryPath, "package.json");
        if (TryLoadSource(packageJsonPath, out var packageJsonSource))
        {
            var mainEntry = TryGetPackageMain(packageJsonSource!);
            if (!string.IsNullOrWhiteSpace(mainEntry))
            {
                var mainPath = CombinePath(directoryPath, mainEntry!);
                var mainResolved = ResolveAsPath(mainPath, pathExtensions, indexCandidates);
                if (mainResolved is not null)
                    return mainResolved;
            }
        }

        foreach (var indexCandidateName in indexCandidates)
        {
            var indexCandidate = CombinePath(directoryPath, indexCandidateName);
            if (TryLoadSource(indexCandidate))
                return ResolveRealPathIfPossible(indexCandidate);
        }

        return null;
    }

    private static (string PackageName, string? PackageSubpath) SplitPackageSpecifier(string specifier)
    {
        if (specifier.StartsWith('@'))
        {
            var firstSlash = specifier.IndexOf('/');
            if (firstSlash < 0)
                return (specifier, null);

            var secondSlash = specifier.IndexOf('/', firstSlash + 1);
            if (secondSlash < 0)
                return (specifier, null);

            return (specifier[..secondSlash], specifier[(secondSlash + 1)..]);
        }

        var slash = specifier.IndexOf('/');
        if (slash < 0)
            return (specifier, null);

        return (specifier[..slash], specifier[(slash + 1)..]);
    }

    private IEnumerable<(string PackageRoot, string PackageJsonPath, string PackageJsonSource)>
        EnumerateContainingPackages(string? referrer)
    {
        var currentDirectory = GetDirectoryName(ResolveRealPathIfPossible(referrer ?? "/"));
        if (string.IsNullOrEmpty(currentDirectory))
            yield break;

        while (true)
        {
            var packageJsonPath = CombinePath(currentDirectory, "package.json");
            if (TryLoadSource(packageJsonPath, out var packageJsonSource))
                yield return (currentDirectory, packageJsonPath, packageJsonSource!);

            if (currentDirectory == "/" || IsDriveRoot(currentDirectory))
                yield break;

            var parentDirectory = GetDirectoryName(currentDirectory);
            if (string.Equals(parentDirectory, currentDirectory, StringComparison.Ordinal))
                yield break;
            currentDirectory = parentDirectory;
        }
    }

    private IEnumerable<string> EnumerateNodeModulesDirectories(string? referrer)
    {
        var currentDirectory = GetDirectoryName(ResolveRealPathIfPossible(referrer ?? "/"));
        if (string.IsNullOrEmpty(currentDirectory))
            currentDirectory = "/";

        while (true)
        {
            yield return CombinePath(currentDirectory, "node_modules");

            if (currentDirectory == "/")
                yield break;

            var parentDirectory = GetDirectoryName(currentDirectory);
            if (string.Equals(parentDirectory, currentDirectory, StringComparison.Ordinal))
                yield break;
            currentDirectory = parentDirectory;
        }
    }

    private bool TryLoadSource(string resolvedId)
    {
        return TryLoadSource(resolvedId, out _);
    }

    private bool TryLoadSource(string resolvedId, out string? source)
    {
        try
        {
            source = loadSource(resolvedId);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException
                                       or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            source = null;
            return false;
        }
    }

    private static string? TryGetPackageMain(string packageJsonSource)
    {
        try
        {
            using var document = JsonDocument.Parse(packageJsonSource);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!document.RootElement.TryGetProperty("main", out var mainElement))
                return null;

            return mainElement.ValueKind == JsonValueKind.String ? mainElement.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CombinePath(string left, string right)
    {
        if (Path.IsPathRooted(right))
            return NormalizePath(right);

        var normalizedLeft = NormalizePath(left).TrimEnd('/');
        var normalizedRight = NormalizePath(right).TrimStart('/');
        if (string.IsNullOrEmpty(normalizedLeft))
            return "/" + normalizedRight;
        if (normalizedLeft == "/")
            return "/" + normalizedRight;
        if (IsDriveRoot(normalizedLeft))
            return normalizedLeft + normalizedRight;
        return NormalizePath(normalizedLeft + "/" + normalizedRight);
    }

    private static bool IsRelativeOrAbsoluteSpecifier(string specifier)
    {
        return specifier.StartsWith("./", StringComparison.Ordinal) ||
               specifier.StartsWith("../", StringComparison.Ordinal) ||
               specifier.StartsWith("/", StringComparison.Ordinal) ||
               Path.IsPathRooted(specifier);
    }

    private static bool IsInternalImportsSpecifier(string specifier)
    {
        return specifier.StartsWith('#') &&
               !string.Equals(specifier, "#", StringComparison.Ordinal) &&
               !specifier.StartsWith("#/", StringComparison.Ordinal);
    }

    private static string GetDirectoryName(string path)
    {
        var normalized = NormalizePath(path).TrimEnd('/');
        if (IsDriveRoot(normalized))
            return normalized;
        var slash = normalized.LastIndexOf('/');
        if (slash < 0)
            return ".";
        if (slash == 0)
            return "/";
        if (slash == 2 && normalized.Length >= 3 && normalized[1] == ':')
            return normalized[..3];
        return normalized[..slash];
    }

    private static string NormalizePath(string path)
    {
        path = path.Replace('\\', '/');
        var rootPrefix = GetRootPrefix(path, out var isAbsolute);
        var remainder = path[rootPrefix.Length..];
        var parts = new List<string>();
        foreach (var part in remainder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count != 0 && parts[^1] != "..")
                    parts.RemoveAt(parts.Count - 1);
                else if (!isAbsolute)
                    parts.Add(part);
                continue;
            }

            parts.Add(part);
        }

        var joined = string.Join("/", parts);
        if (rootPrefix.Length != 0)
        {
            if (joined.Length == 0)
                return rootPrefix;
            return rootPrefix + joined;
        }

        if (isAbsolute)
            return "/" + joined;
        return joined.Length == 0 ? "." : joined;
    }

    private static string ResolveRealPathIfPossible(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            return path;

        var packageRealPath = TryResolveNodeModulesPackageRealPath(path);
        if (!string.Equals(packageRealPath, path, StringComparison.Ordinal))
            return NormalizePath(packageRealPath);

        try
        {
            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                var resolved = fileInfo.ResolveLinkTarget(true)?.FullName ?? fileInfo.FullName;
                return NormalizePath(resolved);
            }

            if (Directory.Exists(path))
            {
                var directoryInfo = new DirectoryInfo(path);
                var resolved = directoryInfo.ResolveLinkTarget(true)?.FullName ?? directoryInfo.FullName;
                return NormalizePath(resolved);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }

        return path;
    }

    private static string TryResolveNodeModulesPackageRealPath(string path)
    {
        var normalized = NormalizePath(path);
        const string marker = "/node_modules/";
        var markerIndex = normalized.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return path;

        var packageStart = markerIndex + marker.Length;
        var packageEnd = normalized.IndexOf('/', packageStart);
        if (packageEnd < 0)
            packageEnd = normalized.Length;

        if (packageStart < normalized.Length && normalized[packageStart] == '@')
        {
            var scopeEnd = normalized.IndexOf('/', packageEnd + 1);
            if (scopeEnd > 0)
                packageEnd = scopeEnd;
        }

        var packagePath = normalized[..packageEnd];
        try
        {
            if (!Directory.Exists(packagePath))
                return path;

            var packageDirectory = new DirectoryInfo(packagePath);
            var resolvedPackageDirectory = packageDirectory.ResolveLinkTarget(true);
            if (resolvedPackageDirectory is null)
                return path;

            var suffix = normalized[packageEnd..];
            return NormalizePath(resolvedPackageDirectory.FullName + suffix);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return path;
        }
    }

    private static string GetRootPrefix(string path, out bool isAbsolute)
    {
        if (path.Length >= 3 &&
            char.IsAsciiLetter(path[0]) &&
            path[1] == ':' &&
            path[2] == '/')
        {
            isAbsolute = true;
            return path[..3];
        }

        if (path.StartsWith("/", StringComparison.Ordinal))
        {
            isAbsolute = true;
            return "/";
        }

        isAbsolute = false;
        return string.Empty;
    }

    private static bool IsDriveRoot(string path)
    {
        return path.Length == 3 &&
               char.IsAsciiLetter(path[0]) &&
               path[1] == ':' &&
               path[2] == '/';
    }

    private static Exception CreateModuleNotFoundException(string specifier, string? referrer)
    {
        return new InvalidOperationException(
            $"Cannot resolve CommonJS module '{specifier}'" +
            (referrer is null ? string.Empty : $" from '{referrer}'."));
    }

    private static Exception CreatePackagePathNotExportedException(string specifier, string packageJsonPath)
    {
        return new InvalidOperationException(
            $"Package path '{specifier}' is not exported by '{packageJsonPath}'.");
    }

    private static Exception CreatePackageImportNotDefinedException(string specifier, string packageJsonPath)
    {
        return new InvalidOperationException(
            $"Package import specifier '{specifier}' is not defined by '{packageJsonPath}'.");
    }
}
