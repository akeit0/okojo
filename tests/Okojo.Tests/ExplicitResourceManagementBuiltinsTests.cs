using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ExplicitResourceManagementBuiltinsTests
{
    [Test]
    public void SuppressedError_Constructor_Installs_Expected_Error_Surface()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;

        realm.Evaluate("""
            globalThis.__ermChecks = (() => {
              const err = new SuppressedError(1, 2, "msg");
              return [
                Object.getPrototypeOf(SuppressedError) === Error,
                Object.getPrototypeOf(SuppressedError.prototype) === Error.prototype,
                err.message === "msg",
                err.error === 1,
                err.suppressed === 2,
                Object.prototype.propertyIsEnumerable.call(err, "error") === false,
                Object.prototype.propertyIsEnumerable.call(err, "suppressed") === false,
                err instanceof SuppressedError
              ];
            })();
            """);

        var checks = realm.Global["__ermChecks"].AsObject() as JsArray;
        Assert.That(checks, Is.Not.Null);
        for (uint i = 0; i < checks!.Length; i++)
        {
            Assert.That(checks.TryGetElement(i, out var value), Is.True);
            Assert.That(value.IsTrue, Is.True, $"SuppressedError check {i} failed.");
        }
    }

    [Test]
    public void DisposableStack_Move_Transfers_Resources_And_Disposes_In_Reverse_Order()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;

        var result = realm.Evaluate("""
            class MyDisposableStack extends DisposableStack {}
            const order = [];
            const stack1 = new MyDisposableStack();
            stack1.use({
              [Symbol.dispose]() {
                order.push("resource");
              }
            });
            stack1.defer(() => order.push("defer"));
            const stack2 = stack1.move();
            stack2.dispose();
            stack1.disposed === true &&
            stack2.disposed === true &&
            stack2 instanceof DisposableStack &&
            !(stack2 instanceof MyDisposableStack) &&
            order.join(",") === "defer,resource";
            """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void DisposableStack_Dispose_Chains_Errors_With_SuppressedError()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;

        realm.Evaluate("""
            globalThis.__ermSuppressedChecks = (() => {
              const first = new Error("first");
              const second = new Error("second");
              const stack = new DisposableStack();
              stack.defer(() => { throw first; });
              stack.defer(() => { throw second; });
              try {
                stack.dispose();
                return [false];
              } catch (e) {
                return [
                  e instanceof SuppressedError,
                  e.error === first,
                  e.suppressed === second
                ];
              }
            })();
            """);

        var checks = realm.Global["__ermSuppressedChecks"].AsObject() as JsArray;
        Assert.That(checks, Is.Not.Null);
        for (uint i = 0; i < checks!.Length; i++)
        {
            Assert.That(checks.TryGetElement(i, out var value), Is.True);
            Assert.That(value.IsTrue, Is.True, $"SuppressedError disposal check {i} failed.");
        }
    }

    [Test]
    public async Task AsyncDisposableStack_Uses_Sync_Fallback_And_Returns_Promise()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;

        var result = await realm.EvaluateInAsyncFunctionScope<bool>("""
            class MyAsyncDisposableStack extends AsyncDisposableStack {}
            const order = [];
            const stack1 = new MyAsyncDisposableStack();
            stack1.use({
              get [Symbol.asyncDispose]() {
                order.push("Symbol.asyncDispose");
                return undefined;
              },
              get [Symbol.dispose]() {
                order.push("Symbol.dispose");
                return () => {
                  order.push("disposed");
                };
              }
            });
            const promise = stack1.disposeAsync();
            await promise;

            const stack2 = new MyAsyncDisposableStack();
            stack2.defer(async () => order.push("moved"));
            const moved = stack2.move();
            await moved.disposeAsync();

            return Object.getPrototypeOf(promise) === Promise.prototype &&
              stack1.disposed === true &&
              moved.disposed === true &&
              moved instanceof AsyncDisposableStack &&
              !(moved instanceof MyAsyncDisposableStack) &&
              order.join(",") === "Symbol.asyncDispose,Symbol.dispose,disposed,moved";
            """);

        Assert.That(result, Is.True);
    }
}
