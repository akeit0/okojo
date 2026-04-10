using Microsoft.Extensions.Time.Testing;
using Okojo.Reflection;
using Okojo.Runtime;

namespace Okojo.Tests;

public class RuntimeApiRefineTests
{
    [Test]
    public void Create_FromOptions_UsesHostConfiguration()
    {
        var timeProvider = new FakeTimeProvider();
        var moduleLoader = new InlineModuleLoader();

        var options = new JsRuntimeOptions();
        options.Host.UseTimeProvider(timeProvider);
        options.Host.UseModuleSourceLoader(moduleLoader);

        using var engine = JsRuntime.Create(options);

        Assert.That(engine.TimeProvider, Is.SameAs(timeProvider));
        Assert.That(engine.ModuleSourceLoader, Is.SameAs(moduleLoader));
    }

    [Test]
    public void Builder_UseHost_ComposesHostConfiguration()
    {
        var timeProvider = new FakeTimeProvider();
        var workerLoader = new InlineWorkerScriptLoader();

        using var engine = JsRuntime.CreateBuilder()
            .UseHost(host =>
            {
                host.UseTimeProvider(timeProvider);
                host.UseWorkerScriptSourceLoader(workerLoader);
            })
            .Build();

        Assert.That(engine.TimeProvider, Is.SameAs(timeProvider));
        Assert.That(engine.WorkerScriptSourceLoader, Is.SameAs(workerLoader));
    }

    [Test]
    public void Builder_UseCore_ComposesCoreConfiguration()
    {
        using var engine = JsRuntime.CreateBuilder()
            .UseCore(core =>
            {
                core.AllowClrAccess();
                core.AddClrAssembly(typeof(object).Assembly);
            })
            .Build();

        Assert.That(engine.IsClrAccessEnabled, Is.True);
        Assert.That(engine.Options.Core.ClrAssemblies, Does.Contain(typeof(object).Assembly));
    }

    [Test]
    public void Builder_UseAgent_And_UseRealm_ComposesAgentAndRealmConfiguration()
    {
        using var engine = JsRuntime.CreateBuilder()
            .UseAgent(agent => { agent.HostDefined = "agent-meta"; })
            .UseRealm(realm =>
            {
                realm.HostDefined = "realm-meta";
                realm.Initialize = realmInstance => realmInstance.Global["booted"] = JsValue.FromString("yes");
            })
            .Build();

        Assert.That(engine.MainAgent.HostDefined, Is.EqualTo("agent-meta"));
        Assert.That(engine.MainRealm.HostDefined, Is.EqualTo("realm-meta"));
        Assert.That(engine.MainRealm.Global["booted"].AsString(), Is.EqualTo("yes"));
    }

    [Test]
    public void Builder_UseGlobals_Installs_GlobalValues_And_HostFunctions()
    {
        using var engine = JsRuntime.CreateBuilder()
            .UseGlobals(globals => globals
                .Value("answer", JsValue.FromInt32(42))
                .Function("sum", 2, static (in info) =>
                {
                    var left = info.GetArgumentOrDefault(0, JsValue.FromInt32(0)).Int32Value;
                    var right = info.GetArgumentOrDefault(1, JsValue.FromInt32(0)).Int32Value;
                    return JsValue.FromInt32(left + right);
                }))
            .Build();

        var result = engine.MainRealm.Eval("answer + sum(5, 7)");
        Assert.That(result.Int32Value, Is.EqualTo(54));
    }

    [Test]
    public void Realm_InstallGlobals_Installs_GlobalValues_And_HostFunctions()
    {
        using var engine = JsRuntime.Create();

        engine.MainRealm.InstallGlobals(globals => globals
            .Value("label", JsValue.FromString("ok"))
            .Function("twice", 1, static (in info) =>
            {
                var value = info.GetArgumentOrDefault(0, JsValue.FromInt32(0)).Int32Value;
                return JsValue.FromInt32(value * 2);
            }));

        var result = engine.MainRealm.Eval("`${label}:${twice(9)}`");
        Assert.That(result.AsString(), Is.EqualTo("ok:18"));
    }

    [Test]
    public void CreateWorkerAgent_AcceptsAgentAndRealmOptions()
    {
        using var engine = JsRuntime.Create();

        var worker = engine.CreateWorkerAgent(agent =>
        {
            agent.HostDefined = "worker-agent-meta";
            agent.Realm.HostDefined = "worker-realm-meta";
            agent.Realm.Initialize = realm => realm.Global["booted"] = JsValue.FromString("worker");
        });

        Assert.That(worker.HostDefined, Is.EqualTo("worker-agent-meta"));
        Assert.That(worker.MainRealm.HostDefined, Is.EqualTo("worker-realm-meta"));
        Assert.That(worker.MainRealm.Global["booted"].AsString(), Is.EqualTo("worker"));
    }

    private sealed class InlineModuleLoader : IModuleSourceLoader
    {
        public string ResolveSpecifier(string specifier, string? referrer)
        {
            return specifier;
        }

        public string LoadSource(string resolvedId)
        {
            return "export const value = 1;";
        }
    }

    private sealed class InlineWorkerScriptLoader : IWorkerScriptSourceLoader
    {
        public string LoadScript(string path, string? referrer = null)
        {
            return "globalThis.loaded = true;";
        }
    }
}
