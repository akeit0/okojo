using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public partial class ExecutionCheckTests
{
    [Test]
    public void PeriodicCheckpoint_ToPausedSnapshot_Mirrors_Checkpoint()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.SetCheckInterval(1);
        }));
        var realm = runtime.DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let total = 0;
            total = total + 1;
            """, "paused-periodic.js"));

        realm.Execute(script);

        var checkpoint = debugger.Checkpoints.Find(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Periodic);
        Assert.That(checkpoint.Kind, Is.EqualTo(ExecutionCheckpointKind.Periodic));
        var snapshot = checkpoint.ToPausedSnapshot();

        Assert.That(snapshot.Kind, Is.EqualTo(checkpoint.Kind));
        Assert.That(snapshot.KindLabel, Is.EqualTo(checkpoint.KindLabel));
        Assert.That(snapshot.CurrentFrameInfo.FunctionName, Is.Not.Empty);
        Assert.That(snapshot.StackFrames.Count, Is.GreaterThan(0));
        Assert.That(snapshot.GetDebuggerStopSummary(), Does.Contain(snapshot.CurrentFrameInfo.FunctionName));
    }

    [Test]
    public void DebuggerAndBreakpoint_Checkpoints_Expose_Paused_Snapshots()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableBreakpointHook();
            agent.EnableDebuggerStatementHook();
        }));
        var realm = runtime.DefaultRealm;
        var program = JavaScriptParser.ParseScript("""
                                                   function add(a, b) {
                                                       debugger;
                                                       return a + b;
                                                   }
                                                   globalThis.out = add(1, 2);
                                                   """, "paused-debugger.js");
        var script = new JsCompiler(realm).Compile(program);
        using var breakpoint = runtime.MainAgent.AddBreakpoint("paused-debugger.js", 3);

        realm.Execute(script);

        var debuggerStop =
            debugger.Checkpoints.First(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.DebuggerStatement);
        var breakpointStop =
            debugger.Checkpoints.First(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Breakpoint);

        var debuggerSnapshot = debuggerStop.ToPausedSnapshot();
        var breakpointSnapshot = breakpointStop.ToPausedSnapshot();

        Assert.That(debuggerSnapshot.SourceLocation?.SourcePath, Is.EqualTo("paused-debugger.js"));
        Assert.That(debuggerSnapshot.CurrentFrameInfo.FunctionName, Is.EqualTo("add"));
        Assert.That(breakpointSnapshot.SourceLocation?.SourcePath, Is.EqualTo("paused-debugger.js"));
        Assert.That(breakpointSnapshot.StackFrames.Any(frame => frame.FunctionName == "add"), Is.True);
    }

    [Test]
    public void StepOver_Request_Emits_Step_Checkpoint()
    {
        var debugger = new StepRequestDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableDebuggerStatementHook();
            agent.EnableCallHook();
            agent.EnableReturnHook();
        }));
        var realm = runtime.DefaultRealm;
        debugger.Realm = realm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function add(a, b) {
                return a + b;
            }
            debugger;
            globalThis.out = add(1, 2);
            """, "step-over.js"));

        realm.Execute(script);

        Assert.That(debugger.Checkpoints.Any(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Step),
            Is.True);
        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.Kind == ExecutionCheckpointKind.Step &&
            checkpoint.SourceLocation is { } sourceLocation &&
            sourceLocation.SourcePath == "step-over.js"), Is.True);
    }
}
