using Okojo.Runtime;

namespace Okojo.Tests;

public class ModuleTdzBindingKindTests
{
    [Test]
    public void EvaluateModule_LocalLet_ReadBeforeInitialization_Throws()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export const y = x;
                                let x = 1;
                                """
        });

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        Assert.Throws<JsRuntimeException>(() => engine.MainAgent.EvaluateModule(realm, "/mods/main.js"));
    }

    [Test]
    public void EvaluateModule_LocalConst_ReadBeforeInitialization_Throws()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export const y = x;
                                const x = 1;
                                """
        });

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        Assert.Throws<JsRuntimeException>(() => engine.MainAgent.EvaluateModule(realm, "/mods/main.js"));
    }

    [Test]
    public void EvaluateModule_LocalClass_ReadBeforeInitialization_Throws()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export const y = C;
                                class C {}
                                """
        });

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        Assert.Throws<JsRuntimeException>(() => engine.MainAgent.EvaluateModule(realm, "/mods/main.js"));
    }

    [Test]
    public void EvaluateModule_LocalVar_ReadBeforeInitialization_IsUndefined()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export const y = x;
                                var x = 1;
                                """
        });

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var obj), Is.True);
        Assert.That(obj!.TryGetProperty("y", out var y), Is.True);
        Assert.That(y.IsUndefined, Is.True);
    }

    [Test]
    public void EvaluateModule_LocalFunction_ReadBeforeDeclaration_IsCallable()
    {
        var loader = new InMemoryModuleLoader(new(StringComparer.Ordinal)
        {
            ["/mods/main.js"] = """
                                export const y = f();
                                function f() { return 7; }
                                """
        });

        using var engine = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var realm = engine.MainRealm;
        var ns = engine.MainAgent.EvaluateModule(realm, "/mods/main.js");
        Assert.That(ns.TryGetObject(out var obj), Is.True);
        Assert.That(obj!.TryGetProperty("y", out var y), Is.True);
        Assert.That(y.IsInt32, Is.True);
        Assert.That(y.Int32Value, Is.EqualTo(7));
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
