using Microsoft.Extensions.Time.Testing;
using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class TimerTests
{
    [Test]
    public void SetTimeout_Fires_After_Delay()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   setTimeout(function () { globalThis.out = 42; }, 100);
                                                                   0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(0));

        fakeTime.Advance(TimeSpan.FromMilliseconds(99));
        realm.PumpJobs();
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(0));

        fakeTime.Advance(TimeSpan.FromMilliseconds(1));
        realm.PumpJobs();
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(42));
    }

    [Test]
    public void ClearTimeout_Cancels_Timer()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   const id = setTimeout(function () { globalThis.out = 99; }, 50);
                                                                   clearTimeout(id);
                                                                   0;
                                                                   """));

        realm.Execute(script);
        fakeTime.Advance(TimeSpan.FromMilliseconds(100));
        realm.PumpJobs();

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(0));
    }

    [Test]
    public void AsyncAwait_With_SetTimeout_Promise_Resolves()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = 0;
                                                                   async function sleepValue() {
                                                                       return await new Promise(function (resolve) {
                                                                           setTimeout(resolve, 100, 7);
                                                                       });
                                                                   }
                                                                   sleepValue().then(function (v) { globalThis.out = v; });
                                                                   0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(0));

        fakeTime.Advance(TimeSpan.FromMilliseconds(100));
        realm.PumpJobs();

        Assert.That(realm.Global["out"].Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void SetTimeout_Forwards_All_Callback_Arguments()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   globalThis.out = "";
                                                                   setTimeout(function (a, b, c) {
                                                                       globalThis.out = String(a) + "|" + String(b) + "|" + String(c);
                                                                   }, 10, "A", 2, true);
                                                                   0;
                                                                   """));

        realm.Execute(script);
        fakeTime.Advance(TimeSpan.FromMilliseconds(10));
        realm.PumpJobs();

        Assert.That(realm.Global["out"].AsString(), Is.EqualTo("A|2|true"));
    }

    [Test]
    public void SetTimeout_Callback_Runtime_Error_Escapes_As_JsRuntimeException()
    {
        var fakeTime = new FakeTimeProvider();
        var realm = JsRuntime.CreateBuilder().UseTimeProvider(fakeTime).UseWebRuntimeGlobals().Build().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                            setTimeout(function () {
                                                                                var value = null;
                                                                                value.missing();
                                                                            }, 10);
                                                                            0;
                                                                            """));

        realm.Execute(script);
        fakeTime.Advance(TimeSpan.FromMilliseconds(10));

        var exception = Assert.Throws<JsRuntimeException>(() => realm.PumpJobs());
        Assert.That(exception!.Message, Does.Contain("Cannot read properties of null or undefined"));
    }
}
