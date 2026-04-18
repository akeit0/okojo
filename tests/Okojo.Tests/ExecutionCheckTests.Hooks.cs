using Microsoft.Extensions.Time.Testing;
using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public partial class ExecutionCheckTests
{
    [Test]
    public void FunctionCall_And_Return_Emit_Boundary_Checkpoints()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableCallHook();
            agent.EnableReturnHook();
        }));
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function add(a, b) {
                return a + b;
            }
            globalThis.out = add(1, 2);
            """));

        realm.Execute(script);

        Assert.That(debugger.CallCount, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void PumpJobs_Emits_Boundary_Checkpoint()
    {
        var debugger = new RecordingDebugger();
        var fakeTime = new FakeTimeProvider();
        var runtime = JsRuntime.Create(builder => builder
            .UseTimeProvider(fakeTime)
            .UseWebRuntimeGlobals()
            .UseAgent(agent =>
            {
                agent.DebuggerSession = debugger;
                agent.EnablePumpHook();
            }));
        var realm = runtime.DefaultRealm;
        _ = realm.Eval("""
                       setTimeout(function () {
                           globalThis.pumped = true;
                       }, 1);
                       """);

        fakeTime.Advance(TimeSpan.FromMilliseconds(1));
        realm.PumpJobs();

        Assert.That(debugger.CallCount, Is.GreaterThan(0));
        Assert.That(debugger.Checkpoints.Any(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Pump),
            Is.True);
    }

    [Test]
    public void Call_And_Return_Hooks_Can_Be_Toggled_At_Runtime()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.DisableCallHook();
            agent.DisableReturnHook();
        }));
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function add(a, b) {
                return a + b;
            }
            globalThis.out = add(1, 2);
            """));

        realm.Execute(script);
        debugger.Checkpoints.Clear();
        runtime.MainAgent.EnableCallHook();
        runtime.MainAgent.EnableReturnHook();

        realm.Execute(script);
        Assert.That(debugger.Checkpoints.Any(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Call),
            Is.True);
        Assert.That(debugger.Checkpoints.Any(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Return),
            Is.True);
    }

    [Test]
    public void Call_And_Return_Hooks_Can_Be_Toggled_During_Debugger_Stop()
    {
        var debugger = new ToggleHookDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.DisableCallHook();
            agent.DisableReturnHook();
        }));
        debugger.Agent = runtime.MainAgent;
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function add(a, b) {
                return a + b;
            }
            debugger;
            globalThis.first = add(1, 2);
            debugger;
            globalThis.second = add(3, 4);
            """));

        realm.Execute(script);

        Assert.That(debugger.DebuggerStops, Is.EqualTo(2));
        Assert.That(debugger.Checkpoints.Count(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Call),
            Is.EqualTo(1));
        Assert.That(debugger.Checkpoints.Count(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Return),
            Is.EqualTo(1));
        Assert.That(
            debugger.Checkpoints.Count(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.DebuggerStatement),
            Is.EqualTo(2));
    }

    [Test]
    public void CaughtException_Hook_Emits_On_First_Exception_Capture_Before_Catch_Routing()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableCaughtExceptionHook();
        }));
        var realm = runtime.DefaultRealm;

        _ = realm.Eval("""
                       function run() {
                           let local = 1;
                           try {
                               local = 2;
                               throw new Error("boom");
                           } catch (error) {
                               globalThis.caughtMessage = error.message;
                               return local;
                           }
                       }

                       globalThis.result = run();
                       """);

        var checkpoint = debugger.Checkpoints.Single(item => item.Kind == ExecutionCheckpointKind.CaughtException);
        Assert.That(checkpoint.KindLabel, Is.EqualTo("caught-exception"));
        Assert.That(checkpoint.StackFrames.Any(frame => frame.FunctionName == "run"), Is.True);
        Assert.That(checkpoint.TryGetLocalValue("local", out var localValue), Is.True);
        Assert.That(localValue.Value.IsInt32, Is.True);
        Assert.That(localValue.Value.Int32Value, Is.EqualTo(2));
        Assert.That(realm.Eval("globalThis.caughtMessage").AsString(), Is.EqualTo("boom"));
        var result = realm.Eval("globalThis.result");
        Assert.That(result.IsInt32, Is.True);
        Assert.That(result.Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void CaughtException_Hook_Also_Emits_For_Uncaught_Exception_On_First_Capture()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableCaughtExceptionHook();
        }));
        var realm = runtime.DefaultRealm;

        Assert.Throws<JsRuntimeException>(() => realm.Eval("""
                                                           function fail() {
                                                               let marker = 7;
                                                               throw new Error("boom");
                                                           }

                                                           fail();
                                                           """));

        var checkpoint = debugger.Checkpoints.Single(item => item.Kind == ExecutionCheckpointKind.CaughtException);
        Assert.That(checkpoint.StackFrames.Any(frame => frame.FunctionName == "fail"), Is.True);
        Assert.That(checkpoint.TryGetLocalValue("marker", out var markerValue), Is.True);
        Assert.That(markerValue.Value.IsInt32, Is.True);
        Assert.That(markerValue.Value.Int32Value, Is.EqualTo(7));
    }
}
