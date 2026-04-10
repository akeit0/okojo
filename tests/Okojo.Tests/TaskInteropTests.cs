using Okojo.Runtime;
using Okojo.Runtime.Interop;

namespace Okojo.Tests;

public class TaskInteropTests
{
    [Test]
    public async Task ToValueTask_Uses_Source_Backed_Pending_Promise()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.MainRealm;

        var promise = realm.Eval("""
                                 globalThis.resolvePending = undefined;
                                 new Promise(resolve => {
                                   globalThis.resolvePending = resolve;
                                 });
                                 """);

        var task = realm.ToValueTask<int>(promise);
        Assert.That(task.IsCompleted, Is.False);

        realm.Call(realm.Global["resolvePending"], JsValue.Undefined, JsValue.FromInt32(7));
        realm.PumpJobs();

        var result = await task;
        Assert.That(result, Is.EqualTo(7));
    }

    [Test]
    public async Task ToPumpedValueTask_Advances_Pending_Promise_Without_ToTask()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.MainRealm;

        var promise = realm.Eval("""
                                 new Promise(resolve => {
                                   Promise.resolve().then(() => resolve(9));
                                 });
                                 """);

        var task = realm.ToPumpedValueTask<int>(promise);
        var result = await task;
        Assert.That(result, Is.EqualTo(9));
    }

    [Test]
    public void ToValueTask_Uses_Fault_Object_For_Rejected_Promise()
    {
        using var runtime = JsRuntime.Create();
        var realm = runtime.MainRealm;

        var promise = realm.Eval("Promise.reject('bye')");
        var task = realm.ToValueTask(promise);

        var ex = Assert.ThrowsAsync<PromiseRejectedException>(async () => await task);
        Assert.That(ex!.Reason.AsString(), Is.EqualTo("bye"));
    }
}
