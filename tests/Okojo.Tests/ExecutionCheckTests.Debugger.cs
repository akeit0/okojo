using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public partial class ExecutionCheckTests
{
    [Test]
    public void DebuggerStatement_Invokes_Attached_Debugger()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent => { agent.DebuggerSession = debugger; }));
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            debugger;
            """));

        realm.Execute(script);

        Assert.That(debugger.CallCount, Is.GreaterThan(0));
    }

    [Test]
    public void DebuggerStatement_Provides_StackTrace()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent => { agent.DebuggerSession = debugger; }));
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function inner() {
                debugger;
            }
            inner();
            """));

        realm.Execute(script);

        Assert.That(debugger.Checkpoints.Any(checkpoint => checkpoint.CurrentFrameInfo.FunctionName == "inner"),
            Is.True);
        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.GetDebuggerStopSummary().Contains("inner")), Is.True);
        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.StackFrames.Any(frame => frame.FunctionName == "inner")), Is.True);
    }

    [Test]
    public void DebuggerStatement_Provides_Source_Position()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableDebuggerStatementHook();
        }));
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function inner() {
                debugger;
            }
            inner();
            """));

        realm.Execute(script);

        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.Kind == ExecutionCheckpointKind.DebuggerStatement &&
            checkpoint.CurrentFrameInfo.FunctionName == "inner" &&
            checkpoint.CurrentFrameInfo.HasSourceLocation &&
            checkpoint.CurrentFrameInfo.SourceLine > 0 &&
            checkpoint.CurrentFrameInfo.SourceColumn > 0), Is.True);
    }

    [Test]
    public void DebuggerStatement_Provides_Source_Path()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableDebuggerStatementHook();
        }));
        var realm = runtime.DefaultRealm;
        var program = JavaScriptParser.ParseScript("""
                                                   function inner() {
                                                       debugger;
                                                   }
                                                   inner();
                                                   """, "debug-stop.js");
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.Kind == ExecutionCheckpointKind.DebuggerStatement &&
            checkpoint.SourcePath == "debug-stop.js" &&
            checkpoint.CurrentFrameInfo.FunctionName == "inner"), Is.True);
        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.Kind == ExecutionCheckpointKind.DebuggerStatement &&
            checkpoint.SourceLocation is { } sourceLocation &&
            sourceLocation.SourcePath == "debug-stop.js" &&
            sourceLocation.Line > 0 &&
            sourceLocation.Column > 0), Is.True);
        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.GetDebuggerStopSummary().Contains("debug-stop.js")), Is.True);
    }

    [Test]
    public void DebuggerStatement_Provides_Source_Path_And_Caller_Stack()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableDebuggerStatementHook();
        }));
        var realm = runtime.DefaultRealm;
        var program = JavaScriptParser.ParseScript("""
                                                   function outer() {
                                                       return inner();
                                                   }
                                                   function inner() {
                                                       debugger;
                                                   }
                                                   outer();
                                                   """, "debug-stop-stack.js");
        var script = JsCompiler.Compile(realm, program);

        realm.Execute(script);

        var checkpoint = debugger.Checkpoints.LastOrDefault(checkpoint =>
            checkpoint.Kind == ExecutionCheckpointKind.DebuggerStatement &&
            checkpoint.SourcePath == "debug-stop-stack.js");

        Assert.That(checkpoint.Kind, Is.EqualTo(ExecutionCheckpointKind.DebuggerStatement));
        Assert.That(checkpoint.CurrentFrameInfo.FunctionName, Is.EqualTo("inner"));
        Assert.That(checkpoint.StackFrames.Any(frame => frame.SourcePath == "debug-stop-stack.js"), Is.True);
        Assert.That(checkpoint.StackFrames.Any(frame => frame.FunctionName == "inner"), Is.True);
        Assert.That(checkpoint.StackFrames.Any(frame => frame.FunctionName == "outer"), Is.True);
        Assert.That(checkpoint.GetDebuggerStopSummary().Contains("debug-stop-stack.js"), Is.True);
    }

    [Test]
    public void Generator_Suspend_And_Resume_Emit_Boundary_Checkpoints()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableSuspendGeneratorHook();
            agent.EnableResumeGeneratorHook();
        }));
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function* gen() {
                yield 1;
                return 2;
            }
            const it = gen();
            it.next();
            it.next();
            """, "generator-stop.js"));

        realm.Execute(script);

        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.CurrentFrameInfo.FunctionName == "gen"), Is.True);
        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.GetDebuggerStopSummary().Contains("gen")), Is.True);
        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.Kind == ExecutionCheckpointKind.SuspendGenerator &&
            checkpoint.StackFrames.Any(frame => frame.FunctionName == "gen") &&
            checkpoint.SourceLocation is { } suspendLocation &&
            suspendLocation.SourcePath == "generator-stop.js" &&
            suspendLocation.Line > 0 &&
            suspendLocation.Column > 0), Is.True);
        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.Kind == ExecutionCheckpointKind.ResumeGenerator &&
            checkpoint.StackFrames.Any(frame => frame.FunctionName == "gen") &&
            checkpoint.SourceLocation is { } resumeLocation &&
            resumeLocation.SourcePath == "generator-stop.js" &&
            resumeLocation.Line > 0 &&
            resumeLocation.Column > 0), Is.True);
    }
}
