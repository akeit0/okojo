using Okojo.DotNet.Modules;
using Okojo.Runtime;

namespace Okojo.Node.Tests;

public class NodeDotNetModuleTests
{
    [Test]
    public void RunMainModule_NuGetImport_Can_Load_Into_ClrNamespace()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "okojo-node-dotnet-modules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var packageDllPath = Path.Combine(tempRoot, "sample.uri", "1.0.0", "lib", "net10.0", "sample.uri.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(packageDllPath)!);
            File.Copy(typeof(Uri).Assembly.Location, packageDllPath);

            using var runtime = NodeRuntime.CreateBuilder()
                .UseModuleSourceLoader(new InMemoryModuleLoader(new(StringComparer.Ordinal)
                {
                    ["/app/main.mjs"] = """
                                        import "nuget:sample.uri@1.0.0";
                                        export default new clr.System.Uri("https://example.com/node").Host;
                                        """
                }))
                .ConfigureRuntime(builder => builder.UseDotNetModuleImports(options => options.GlobalPackagesRoot = tempRoot))
                .Build();

            var result = runtime.RunMainModule("/app/main.mjs");

            Assert.That(result.TryGetObject(out var namespaceObj), Is.True);
            Assert.That(namespaceObj!.TryGetProperty("default", out var value), Is.True);
            Assert.That(value.AsString(), Is.EqualTo("example.com"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
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
