using System.Runtime.CompilerServices;
using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.Tests;

public class RealmModuleTests
{
    private const string Root = "https://example.test/main.js";

    [Test]
    public void SameUrlUsesIndependentLoadersGlobalsAndNamespaces()
    {
        var firstLoader = new Loader(
            new()
            {
                [Root] =
                    "export { value } from './dep.js'; export const url = import.meta.url; globalThis.runs = (globalThis.runs || 0) + 1;",
                ["https://example.test/dep.js"] = "export const value = globalThis.seed + 1;",
            }
        );
        var secondLoader = new Loader(
            new()
            {
                [Root] = firstLoader.Sources[Root],
                ["https://example.test/dep.js"] = "export const value = globalThis.seed + 2;",
            }
        );
        using var runtime = JsRuntime.CreateBuilder().UseModuleSourceLoader(firstLoader).Build();
        var first = runtime.MainRealm;
        var second = runtime.CreateRealm(options => options.ModuleSourceLoader = secondLoader);
        first.Global["seed"] = 10;
        second.Global["seed"] = 20;
        first.Global["ns"] = first.Import(Root);
        second.Global["ns"] = second.Import(Root);
        first.Global["other"] = second.Global["ns"];
        first.Global["again"] = first.Import(Root);
        Assert.That(
            first.Evaluate("ns.value === 11 && ns !== other && ns === again && runs === 1").IsTrue,
            Is.True
        );
        Assert.That(
            second
                .Evaluate(
                    "ns.value === 22 && ns.url === 'https://example.test/main.js' && runs === 1"
                )
                .IsTrue,
            Is.True
        );
        Assert.That(firstLoader.Loads, Is.EqualTo(2));
        Assert.That(secondLoader.Loads, Is.EqualTo(2));
    }

    [Test]
    public void PendingDynamicImportsSettleInTheirOwnRealm()
    {
        var loader = new Loader(
            new()
            {
                [Root] =
                    "await new Promise(resolve => { globalThis.resume = resolve; }); export const value = globalThis.seed;",
            }
        );
        using var runtime = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var first = runtime.MainRealm;
        var second = runtime.CreateRealm();
        first.Execute(
            "globalThis.seed = 1; import('https://example.test/main.js').then(ns => { globalThis.result = ns.value; });"
        );
        second.Execute(
            "globalThis.seed = 2; import('https://example.test/main.js').then(ns => { globalThis.result = ns.value; });"
        );
        second.Execute("resume();");
        Assert.That(second.Global["result"].NumberValue, Is.EqualTo(2));
        Assert.That(first.Evaluate("typeof result === 'undefined'").IsTrue, Is.True);
        first.Execute("resume();");
        Assert.That(first.Global["result"].NumberValue, Is.EqualTo(1));
    }

    [Test]
    public void JsonAndTextModulesUseRealmLoaderAndNamespaceIdentity()
    {
        using var runtime = JsRuntime.Create();
        var first = runtime.CreateRealm(options => options.ModuleSourceLoader = DataLoader("one"));
        var second = runtime.CreateRealm(options => options.ModuleSourceLoader = DataLoader("two"));
        const string source =
            "Promise.all([import('https://example.test/data.json', {with:{type:'json'}}), import('https://example.test/data.txt', {with:{type:'text'}})]).then(values => { globalThis.json = values[0]; globalThis.text = values[1]; });";
        first.Execute(source);
        second.Execute(source);
        Assert.That(
            first.Evaluate("json.default.value === 'one' && text.default === 'one'").IsTrue,
            Is.True
        );
        Assert.That(
            second.Evaluate("json.default.value === 'two' && text.default === 'two'").IsTrue,
            Is.True
        );
        first.Global["other"] = second.Global["json"];
        Assert.That(first.Evaluate("json !== other").IsTrue, Is.True);
    }

    [Test]
    public void InvalidationAndDiagnosticsAddressOnlySelectedRealm()
    {
        var loader = new Loader(new() { [Root] = "export const value = 1;" });
        using var runtime = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var child = runtime.CreateRealm();
        var mainNamespace = runtime.MainRealm.Import(Root);
        var childNamespace = child.Import(Root);
        var modules = runtime.MainAgent.Modules;
        Assert.That(modules.GetState(child, Root).Exists, Is.True);
        Assert.That(modules.Invalidate(child, Root), Is.True);
        Assert.That(modules.GetState(child, Root).Exists, Is.False);
        Assert.That(modules.TryGetCachedNamespace(Root, out var cached), Is.True);
        Assert.That(cached, Is.EqualTo(mainNamespace));
        loader.Sources[Root] = "export const value = 2;";
        child.Global["ns"] = child.Import(Root);
        child.Global["old"] = childNamespace;
        Assert.That(
            child.Evaluate("ns !== old && ns.value === 2 && old.value === 1").IsTrue,
            Is.True
        );
        modules.Clear(child);
        Assert.That(modules.GetState(Root).Exists, Is.True);
        using var foreign = JsRuntime.Create();
        Assert.Throws<ArgumentException>(() => modules.Evaluate(foreign.MainRealm, Root));
    }

    [Test]
    public void RetainedModuleFunctionCanImportAfterRealmRelease()
    {
        var loader = new Loader(
            new()
            {
                [Root] = "export const read = async () => (await import('./dep.js')).value;",
                ["https://example.test/dep.js"] = "export const value = 42;",
            }
        );
        using var runtime = JsRuntime.CreateBuilder().UseModuleSourceLoader(loader).Build();
        var child = runtime.CreateRealm();
        runtime.MainRealm.Global["saved"] = child.Import(Root);
        runtime.ReleaseRealm(child);
        runtime.MainRealm.Execute("saved.read().then(value => { globalThis.result = value; });");
        Assert.That(runtime.MainRealm.Global["result"].NumberValue, Is.EqualTo(42));
    }

    [Test]
    [NonParallelizable]
    public void ReleasedRealmWithModulesIsCollectible()
    {
        using var runtime = JsRuntime.Create();
        var weak = LoadAndRelease(runtime);
        for (var attempt = 0; attempt < 5 && weak.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.That(weak.IsAlive, Is.False);
        GC.KeepAlive(runtime);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndRelease(JsRuntime runtime)
    {
        var child = runtime.CreateRealm(options =>
            options.ModuleSourceLoader = new Loader(
                new() { [Root] = "export const value = {}; export const read = () => value;" }
            )
        );
        child.Import(Root);
        runtime.ReleaseRealm(child);
        return new WeakReference(child);
    }

    private static Loader DataLoader(string value) =>
        new(
            new()
            {
                ["https://example.test/data.json"] = "{\"value\":\"" + value + "\"}",
                ["https://example.test/data.txt"] = value,
            }
        );

    private sealed class Loader(Dictionary<string, string> sources) : IModuleSourceLoader
    {
        internal Dictionary<string, string> Sources { get; } = sources;
        internal int Loads { get; private set; }

        public string ResolveSpecifier(string specifier, string? referrer) =>
            new Uri(new Uri(referrer ?? Root), specifier).AbsoluteUri;

        public string LoadSource(string resolvedId)
        {
            Loads++;
            return Sources[resolvedId];
        }
    }
}
