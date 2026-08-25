using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Parsing;

namespace Okojo.Compiler.Tests;

public sealed class DirectFlatDefaultFlipTests
{
    [Test]
    public void UseModuleCompiler_ExecutesScriptsThroughCanonicalPath()
    {
        using var runtime = JsRuntime.Create(builder =>
            builder.UseAgent(options => options.UseModuleCompiler())
        );
        var realm = runtime.DefaultRealm;

        Assert.That(
            realm.Evaluate("[1, 2, 3].map(v => v * 2).join()").AsString(),
            Is.EqualTo("2,4,6")
        );
    }

    [Test]
    public void WithoutRegistration_ScriptsRunThroughDirectFlat()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;

        realm.Execute(
            """
            globalThis.__prodProbe = [];
            const source = {
              [Symbol.iterator]() {
                return {
                  next() { return { value: Promise.reject('reject'), done: false }; },
                  return() { __prodProbe.push('ret'); return {}; }
                };
              }
            };
            async function run() { for await (let _ of source); }
            run().catch(() => {});
            """,
            pumpJobsAfterRun: false
        );
        realm.Agent.RunPromiseJobs();

        Assert.That(realm.Evaluate("__prodProbe.join()").AsString(), Is.EqualTo("ret"));
    }

    [Test]
    public void UseModuleCompiler_RejectedValueCloseMatchesProductionBehavior()
    {
        using var runtime = JsRuntime.Create(builder =>
            builder.UseAgent(options => options.UseModuleCompiler())
        );
        var realm = runtime.DefaultRealm;

        realm.Execute(
            """
            globalThis.__flatSeamProbe = [];
            const source = {
              [Symbol.iterator]() {
                return {
                  next() { return { value: Promise.reject('reject'), done: false }; },
                  return() { __flatSeamProbe.push('ret'); return {}; }
                };
              }
            };
            async function run() { for await (let _ of source); }
            run().catch(() => {});
            """,
            pumpJobsAfterRun: false
        );
        realm.Agent.RunPromiseJobs();

        Assert.That(realm.Evaluate("__flatSeamProbe.length").FastNumberValue, Is.EqualTo(1));
    }
}
