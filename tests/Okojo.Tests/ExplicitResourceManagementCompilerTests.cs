using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ExplicitResourceManagementCompilerTests
{
    [Test]
    public void Using_Block_Disposes_On_Normal_Exit()
    {
        using var runtime = JsRuntime.Create();
        var result = runtime.DefaultRealm.EvaluateInFunctionScope("""
            const order = [];
            {
              using resource = {
                [Symbol.dispose]() {
                  order.push("dispose");
                }
              };
              order.push("body");
            }
            return order.join(",") === "body,dispose";
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Using_Return_Disposes_Before_Function_Leaves()
    {
        using var runtime = JsRuntime.Create();
        var result = runtime.DefaultRealm.EvaluateInFunctionScope("""
            const order = [];
            function run() {
              using resource = {
                [Symbol.dispose]() {
                  order.push("dispose");
                }
              };
              return 42;
            }
            return run() === 42 && order.join(",") === "dispose";
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Using_Throw_Path_Creates_SuppressedError()
    {
        using var runtime = JsRuntime.Create();
        var result = runtime.DefaultRealm.EvaluateInFunctionScope("""
            const bodyError = new Error("body");
            const disposeError = new Error("dispose");
            try {
              using resource = {
                [Symbol.dispose]() {
                  throw disposeError;
                }
              };
              throw bodyError;
            } catch (e) {
              return e instanceof SuppressedError &&
                e.error === disposeError &&
                e.suppressed === bodyError;
            }
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public async Task AwaitUsing_Awaits_Disposal_On_Exit()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var result = await realm.EvaluateInAsyncFunctionScope<JsValue>("""
            const order = [];
            await using resource = {
              [Symbol.asyncDispose]() {
                order.push("dispose-start");
                return Promise.resolve().then(() => order.push("dispose-end"));
              }
            };
            order.push("body");
            return order;
            """);

        var array = result.AsObject() as JsArray;
        Assert.That(array, Is.Not.Null);
        Assert.That(array!.TryGetElement(0, out var first), Is.True);
        Assert.That(array.TryGetElement(1, out var second), Is.True);
        Assert.That(array.TryGetElement(2, out var third), Is.True);
        Assert.That(first.AsString(), Is.EqualTo("body"));
        Assert.That(second.AsString(), Is.EqualTo("dispose-start"));
        Assert.That(third.AsString(), Is.EqualTo("dispose-end"));
    }

    [Test]
    public async Task AwaitUsing_Unevaluated_Path_Does_Not_Insert_Await_Boundary()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;

        _ = realm.Evaluate("""
            globalThis.isRunningInSameMicrotask = true;
            globalThis.wasRunningInSameMicrotask = false;
            globalThis.pending = (async function f() {
              outer: {
                if (true) break outer;
                await using _ = null;
              }
              globalThis.wasRunningInSameMicrotask = globalThis.isRunningInSameMicrotask;
            })();
            globalThis.isRunningInSameMicrotask = false;
            """);

        await realm.ToPumpedValueTask(realm.Global["pending"]);
        Assert.That(realm.Global["wasRunningInSameMicrotask"].IsTrue, Is.True);
    }

    [Test]
    public void Using_ForOf_Head_Disposes_Previous_Iteration_Before_Advancing()
    {
        using var runtime = JsRuntime.Create();
        var result = runtime.DefaultRealm.Evaluate("""
            const states = [];
            const resources = [0, 1].map(index => ({
              disposed: false,
              [Symbol.dispose]() {
                this.disposed = true;
              }
            }));

            for (using resource of resources) {
              states.push(resources.map(item => item.disposed).join(","));
            }

            states.push(resources.map(item => item.disposed).join(","));
            states.join("|") === "false,false|true,false|true,true";
            """);

        Assert.That(result.IsTrue, Is.True);
    }
}
