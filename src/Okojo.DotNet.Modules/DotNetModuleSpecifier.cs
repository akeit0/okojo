namespace Okojo.DotNet.Modules;

internal enum DotNetModuleImportScheme : byte
{
    Dll = 0,
    NuGet = 1
}

internal readonly record struct DotNetModuleSpecifier(
    DotNetModuleImportScheme Scheme,
    string Target,
    string? ClrPath)
{
    public static bool IsDotNetResolvedId(string resolvedId)
    {
        return resolvedId.StartsWith("dll:", StringComparison.OrdinalIgnoreCase) ||
               resolvedId.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryResolve(string specifier, string? referrer, out string resolvedId)
    {
        if (specifier.StartsWith("dll:", StringComparison.OrdinalIgnoreCase))
        {
            var raw = specifier["dll:".Length..];
            var (target, clrPath) = SplitTargetAndClrPath(raw);
            if (string.IsNullOrWhiteSpace(target))
                throw new InvalidOperationException("dll: imports must specify a path.");

            var fullPath = ResolveDllPath(target, referrer);
            resolvedId = "dll:" + fullPath + (clrPath is null ? string.Empty : "#" + clrPath);
            return true;
        }

        if (specifier.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase))
        {
            var raw = specifier["nuget:".Length..];
            var (target, clrPath) = SplitTargetAndClrPath(raw);
            if (string.IsNullOrWhiteSpace(target))
                throw new InvalidOperationException("nuget: imports must specify a package id and version.");

            resolvedId = "nuget:" + target + (clrPath is null ? string.Empty : "#" + clrPath);
            return true;
        }

        resolvedId = string.Empty;
        return false;
    }

    public static bool TryParseResolvedId(string resolvedId, out DotNetModuleSpecifier specifier)
    {
        if (resolvedId.StartsWith("dll:", StringComparison.OrdinalIgnoreCase))
        {
            var (target, clrPath) = SplitTargetAndClrPath(resolvedId["dll:".Length..]);
            specifier = new(DotNetModuleImportScheme.Dll, target, clrPath);
            return true;
        }

        if (resolvedId.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase))
        {
            var (target, clrPath) = SplitTargetAndClrPath(resolvedId["nuget:".Length..]);
            specifier = new(DotNetModuleImportScheme.NuGet, target, clrPath);
            return true;
        }

        specifier = default;
        return false;
    }

    private static (string Target, string? ClrPath) SplitTargetAndClrPath(string raw)
    {
        var hashIndex = raw.IndexOf('#');
        if (hashIndex < 0)
            return (raw, null);

        var target = raw[..hashIndex];
        var clrPath = hashIndex == raw.Length - 1 ? null : raw[(hashIndex + 1)..];
        return (target, clrPath);
    }

    private static string ResolveDllPath(string target, string? referrer)
    {
        if (Path.IsPathRooted(target))
            return Path.GetFullPath(target);

        if (!string.IsNullOrEmpty(referrer) && !IsDotNetResolvedId(referrer))
        {
            var baseDir = Path.GetDirectoryName(referrer);
            if (!string.IsNullOrEmpty(baseDir))
                return Path.GetFullPath(Path.Combine(baseDir, target));
        }

        return Path.GetFullPath(target);
    }
}
