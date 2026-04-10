namespace Okojo.Runtime;

public sealed class FileModuleSourceLoader : IModuleSourceLoader
{
    public string ResolveSpecifier(string specifier, string? referrer)
    {
        var resolved = ResolveBasePath(specifier, referrer);
        if (TryResolveExistingModulePath(resolved, out var existing))
            return existing;

        return resolved;
    }

    public string LoadSource(string resolvedId)
    {
        return File.ReadAllText(resolvedId);
    }

    private static string ResolveBasePath(string specifier, string? referrer)
    {
        if (Path.IsPathRooted(specifier))
            return Path.GetFullPath(specifier);

        if (!string.IsNullOrEmpty(referrer))
        {
            var baseDir = Path.GetDirectoryName(referrer);
            if (!string.IsNullOrEmpty(baseDir))
                return Path.GetFullPath(Path.Combine(baseDir, specifier));
        }

        return Path.GetFullPath(specifier);
    }

    private static bool TryResolveExistingModulePath(string resolvedPath, out string existing)
    {
        if (File.Exists(resolvedPath))
        {
            existing = Path.GetFullPath(resolvedPath);
            return true;
        }

        if (!Path.HasExtension(resolvedPath))
        {
            var jsCandidate = resolvedPath + ".js";
            if (File.Exists(jsCandidate))
            {
                existing = Path.GetFullPath(jsCandidate);
                return true;
            }

            var mjsCandidate = resolvedPath + ".mjs";
            if (File.Exists(mjsCandidate))
            {
                existing = Path.GetFullPath(mjsCandidate);
                return true;
            }
        }

        if (Directory.Exists(resolvedPath))
        {
            var indexJs = Path.Combine(resolvedPath, "index.js");
            if (File.Exists(indexJs))
            {
                existing = Path.GetFullPath(indexJs);
                return true;
            }

            var indexMjs = Path.Combine(resolvedPath, "index.mjs");
            if (File.Exists(indexMjs))
            {
                existing = Path.GetFullPath(indexMjs);
                return true;
            }
        }

        existing = resolvedPath;
        return false;
    }
}
