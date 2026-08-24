using Okojo.JavaScript;
using Okojo.JavaScript.Compiler.Experimental;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Parsing;

namespace Okojo.Compiler.Tests;

public sealed class DirectFlatDefaultFlipTests
{
    private const string DecoratorSource = """
        class Dec {
          static decorate(target) { return target; }
        }
        @Dec.decorate
        class Decorated {}
        'survived';
        """;

    [Test]
    public void FlatParser_RejectsDecoratorSyntax()
    {
        Assert.Throws<JsParseException>(() => FlatJavaScriptParser.ParseScript(DecoratorSource));
    }

    [Test]
    public void UseDirectFlatCompilers_ExecutesScriptsThroughDirectFlatPath()
    {
        using var runtime = JsRuntime.Create(builder =>
            builder.UseAgent(options => options.UseDirectFlatCompilers())
        );
        var realm = runtime.DefaultRealm;

        Assert.That(
            realm.Evaluate("[1, 2, 3].map(v => v * 2).join()").AsString(),
            Is.EqualTo("2,4,6")
        );
    }

    [Test]
    public void UseDirectFlatCompilers_FallsBackToProductionWhenFlatParseThrows()
    {
        using var runtime = JsRuntime.Create(builder =>
            builder.UseAgent(options => options.UseDirectFlatCompilers())
        );
        var realm = runtime.DefaultRealm;

        Assert.That(realm.Evaluate(DecoratorSource).AsString(), Is.EqualTo("survived"));
    }

    [Test]
    public void WithoutRegistration_ScriptsKeepProductionCompiler()
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
    public void UseDirectFlatCompilers_RejectedValueCloseMatchesProductionBehavior()
    {
        using var runtime = JsRuntime.Create(builder =>
            builder.UseAgent(options => options.UseDirectFlatCompilers())
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
