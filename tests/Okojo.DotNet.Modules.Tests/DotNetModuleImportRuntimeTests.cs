using Okojo.Runtime;

namespace Okojo.DotNet.Modules.Tests;

public class DotNetModuleImportRuntimeTests
{
    [Test]
    public void EvaluateModule_DllImport_Can_Construct_Clr_Type()
    {
        var uriAssemblyPath = typeof(Uri).Assembly.Location;
        var escapedPath = EscapeJavaScriptStringLiteral(uriAssemblyPath);
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = $$"""
                                 import Uri from "dll:{{escapedPath}}#System.Uri";
                                 const value = new Uri("https://example.com/path");
                                 export default value.Host;
                                 """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseDotNetModuleImports(loader)
            .Build();
        var module = engine.MainRealm.LoadModule("/mods/main.js");

        Assert.That(module.Object.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("example.com"));
    }

    [Test]
    public void EvaluateModule_NuGetImport_Uses_Configured_GlobalPackagesRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "okojo-dotnet-modules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var packageDllPath = Path.Combine(tempRoot, "sample.uri", "1.0.0", "lib", "net10.0", "sample.uri.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(packageDllPath)!);
            File.Copy(typeof(Uri).Assembly.Location, packageDllPath);

            var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
            {
                ["/mods/main.js"] = """
                                    import Uri from "nuget:sample.uri@1.0.0#System.Uri";
                                    export default new Uri("https://example.com/nuget").AbsolutePath;
                                    """
            });

            using var engine = JsRuntime.CreateBuilder()
                .UseDotNetModuleImports(loader, options => options.GlobalPackagesRoot = tempRoot)
                .Build();
            var module = engine.MainRealm.LoadModule("/mods/main.js");

            Assert.That(module.Object.TryGetProperty("default", out var value), Is.True);
            Assert.That(value.AsString(), Is.EqualTo("/nuget"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void EvaluateModule_DllImport_WithoutClrPath_Loads_Assembly_Into_GlobalClrNamespace()
    {
        var uriAssemblyPath = typeof(Uri).Assembly.Location;
        var escapedPath = EscapeJavaScriptStringLiteral(uriAssemblyPath);
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = $$"""
                                 import "dll:{{escapedPath}}";
                                 export default new clr.System.Uri("https://example.com/from-clr").AbsolutePath;
                                 """
        });

        using var engine = JsRuntime.CreateBuilder()
            .UseDotNetModuleImports(loader)
            .Build();
        var module = engine.MainRealm.LoadModule("/mods/main.js");

        Assert.That(module.Object.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo("/from-clr"));
    }

    private static string EscapeJavaScriptStringLiteral(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private sealed class InMemoryModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
    {
        public string ResolveSpecifier(string specifier, string? referrer)
        {
            if (specifier.StartsWith("/", StringComparison.Ordinal))
                return specifier;

            if (referrer is null)
                return "/" + specifier.TrimStart('/');

            var slash = referrer.LastIndexOf('/');
            var baseDir = slash >= 0 ? referrer[..(slash + 1)] : "/";
            if (specifier.StartsWith("./", StringComparison.Ordinal))
                return baseDir + specifier[2..];

            return baseDir + specifier;
        }

        public string LoadSource(string resolvedId)
        {
            if (!modules.TryGetValue(resolvedId, out var source))
                throw new InvalidOperationException("Module not found: " + resolvedId);
            return source;
        }
    }
}
