using System.Reflection;
using System.Runtime.Loader;
using Okojo.Runtime;

namespace Okojo.DotNet.Modules;

internal sealed class DotNetModuleImportBridge(DotNetModuleImportOptions options)
{
    private readonly Dictionary<string, Assembly> assemblyCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object assemblyGate = new();

    internal const string GlobalBridgeFunctionName = "__okojoDotNetModule__";

    public string CreateModuleSource(string resolvedId)
    {
        var escapedResolvedId = EscapeJavaScriptStringLiteral(resolvedId);
        return $$"""
                 const value = globalThis.{{GlobalBridgeFunctionName}}("{{escapedResolvedId}}");
                 export default value;
                 """;
    }

    public JsValue Import(JsRealm realm, string resolvedId)
    {
        if (!DotNetModuleSpecifier.TryParseResolvedId(resolvedId, out var specifier))
        {
            throw new JsRuntimeException(JsErrorKind.TypeError,
                $"Unsupported .NET module specifier '{resolvedId}'.",
                "DOTNET_MODULE_SPECIFIER");
        }

        var assembly = specifier.Scheme switch
        {
            DotNetModuleImportScheme.Dll => LoadAssembly(specifier.Target),
            DotNetModuleImportScheme.NuGet => LoadAssembly(ResolveNuGetAssemblyPath(specifier.Target)),
            _ => throw new JsRuntimeException(JsErrorKind.TypeError,
                $"Unsupported .NET module scheme '{specifier.Scheme}'.",
                "DOTNET_MODULE_SCHEME")
        };

        realm.AddClrAssembly(assembly);
        return string.IsNullOrWhiteSpace(specifier.ClrPath)
            ? JsValue.FromObject(realm.GetClrNamespace())
            : ResolveClrPath(realm, specifier.ClrPath, resolvedId);
    }

    private string ResolveNuGetAssemblyPath(string packageReference)
    {
        var atIndex = packageReference.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == packageReference.Length - 1)
        {
            throw new InvalidOperationException(
                "nuget: imports must include an explicit version, for example 'nuget:Package.Id@1.2.3#Namespace.Type'.");
        }

        var packageId = packageReference[..atIndex];
        var version = packageReference[(atIndex + 1)..];
        var root = options.GlobalPackagesRoot ?? DotNetFileBasedAppCacheLayout.Detect().GlobalPackagesRoot;
        var packageDir = Path.Combine(root, packageId.ToLowerInvariant(), version);
        if (!Directory.Exists(packageDir))
            throw new FileNotFoundException($"NuGet package cache entry not found for '{packageId}@{version}'.", packageDir);

        var libDir = Path.Combine(packageDir, "lib");
        if (!Directory.Exists(libDir))
            throw new FileNotFoundException($"Package '{packageId}@{version}' does not contain a lib folder.", libDir);

        var tfmDirs = Directory.GetDirectories(libDir);
        Array.Sort(tfmDirs, static (left, right) =>
            string.Compare(Path.GetFileName(left), Path.GetFileName(right), StringComparison.OrdinalIgnoreCase));

        foreach (var preferredTfm in options.PreferredTargetFrameworks)
        {
            for (var i = 0; i < tfmDirs.Length; i++)
            {
                var tfm = Path.GetFileName(tfmDirs[i]);
                if (!IsPreferredTfm(tfm, preferredTfm))
                    continue;

                var assemblyPath = TrySelectAssemblyPath(tfmDirs[i], packageId);
                if (assemblyPath is not null)
                    return assemblyPath;
            }
        }

        for (var i = 0; i < tfmDirs.Length; i++)
        {
            var assemblyPath = TrySelectAssemblyPath(tfmDirs[i], packageId);
            if (assemblyPath is not null)
                return assemblyPath;
        }

        throw new FileNotFoundException($"No loadable assembly was found for package '{packageId}@{version}'.", packageDir);
    }

    private static bool IsPreferredTfm(string tfm, string preferredTfm)
    {
        return string.Equals(tfm, preferredTfm, StringComparison.OrdinalIgnoreCase) ||
               tfm.StartsWith(preferredTfm + "-", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TrySelectAssemblyPath(string directory, string packageId)
    {
        var dlls = Directory.GetFiles(directory, "*.dll");
        if (dlls.Length == 0)
            return null;

        var exactName = packageId + ".dll";
        for (var i = 0; i < dlls.Length; i++)
        {
            if (string.Equals(Path.GetFileName(dlls[i]), exactName, StringComparison.OrdinalIgnoreCase))
                return dlls[i];
        }

        var lastSegment = packageId.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (!string.IsNullOrEmpty(lastSegment))
        {
            var lastSegmentName = lastSegment + ".dll";
            for (var i = 0; i < dlls.Length; i++)
            {
                if (string.Equals(Path.GetFileName(dlls[i]), lastSegmentName, StringComparison.OrdinalIgnoreCase))
                    return dlls[i];
            }
        }

        return dlls.Length == 1 ? dlls[0] : dlls[0];
    }

    private Assembly LoadAssembly(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        lock (assemblyGate)
        {
            if (assemblyCache.TryGetValue(fullPath, out var cached))
                return cached;

            foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
            {
                if (!string.IsNullOrEmpty(assembly.Location) &&
                    string.Equals(Path.GetFullPath(assembly.Location), fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    assemblyCache[fullPath] = assembly;
                    return assembly;
                }
            }

            var targetName = AssemblyName.GetAssemblyName(fullPath);
            foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
            {
                if (string.Equals(assembly.FullName, targetName.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    assemblyCache[fullPath] = assembly;
                    return assembly;
                }
            }

            var loaded = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
            assemblyCache[fullPath] = loaded;
            return loaded;
        }
    }

    private static JsValue ResolveClrPath(JsRealm realm, string clrPath, string resolvedId)
    {
        if (realm.TryGetClrValue(clrPath, out var value))
            return value;

        throw new JsRuntimeException(JsErrorKind.TypeError,
            $"CLR path '{clrPath}' was not found after loading '{resolvedId}'.",
            "DOTNET_MODULE_CLR_PATH_NOT_FOUND");
    }

    private static string EscapeJavaScriptStringLiteral(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
