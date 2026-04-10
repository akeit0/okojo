using Okojo.Runtime;

namespace Okojo.Tests;

public class ImportTextModuleTests
{
    [Test]
    public void EvaluateModule_TextImport_DefaultBinding_UsesRawSource()
    {
        const string textSource = "hello from text module";
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                import value from './message.txt' with { type: 'text' };
                                export default value;
                                """,
            ["/mods/message.txt"] = textSource
        });

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var obj), Is.True);
        Assert.That(obj!.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo(textSource));
    }

    [Test]
    public void EvaluateModule_TextImport_NamespaceBinding_ExposesDefaultOnly()
    {
        const string textSource = "namespaced text";
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                import * as ns from './message.txt' with { type: 'text' };
                                export const count = Object.getOwnPropertyNames(ns).length;
                                export default ns.default;
                                """,
            ["/mods/message.txt"] = textSource
        });

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var obj), Is.True);
        Assert.That(obj!.TryGetProperty("count", out var count), Is.True);
        Assert.That(count.Int32Value, Is.EqualTo(1));
        Assert.That(obj.TryGetProperty("default", out var value), Is.True);
        Assert.That(value.AsString(), Is.EqualTo(textSource));
    }

    [Test]
    public void EvaluateModule_TextImport_SupportsSelfImport_And_SkipsJavaScriptParsing()
    {
        const string selfSource = """
                                  import value from './self.js' with { type: 'text' };
                                  export default typeof value;
                                  """;
        const string invalidJavaScriptSource = "not { valid javascript";
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/self.js"] = selfSource,
            ["/mods/main.js"] = """
                                import value from './invalid.js' with { type: 'text' };
                                export default value;
                                """,
            ["/mods/invalid.js"] = invalidJavaScriptSource
        });

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;

        var selfNs = engine.MainAgent.EvaluateModule(realm, "/mods/self.js");
        Assert.That(selfNs.TryGetObject(out var selfObj), Is.True);
        Assert.That(selfObj!.TryGetProperty("default", out var selfValue), Is.True);
        Assert.That(selfValue.AsString(), Is.EqualTo("string"));

        var mainNs = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(mainNs.TryGetObject(out var mainObj), Is.True);
        Assert.That(mainObj!.TryGetProperty("default", out var mainValue), Is.True);
        Assert.That(mainValue.AsString(), Is.EqualTo(invalidJavaScriptSource));
    }

    private sealed class InMemoryModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
    {
        private readonly Dictionary<string, string> modules = modules;

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
