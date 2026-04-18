using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public partial class ExecutionCheckTests
{
    [Test]
    public void LineBreakpoint_Hits_And_Rearms_On_Next_Execute()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableBreakpointHook();
        }));
        var realm = runtime.DefaultRealm;
        var program = JavaScriptParser.ParseScript(
            "function add(a, b) {\n" +
            "  return a + b;\n" +
            "}\n" +
            "globalThis.out = add(1, 2);\n",
            "breakpoint.js");
        var script = JsCompiler.Compile(realm, program);

        using var breakpoint = runtime.MainAgent.AddBreakpoint("breakpoint.js", 2);

        realm.Execute(script);
        var addFunction = script.ObjectConstants.OfType<JsBytecodeFunction>()
            .Single(function => function.Name == "add");
        Assert.That(runtime.MainAgent.IsRegisteredScript(addFunction.Script), Is.True);
        Assert.That(runtime.MainAgent.GetRegisteredScripts("breakpoint.js"), Does.Contain(script));
        Assert.That(runtime.MainAgent.GetRegisteredScripts("breakpoint.js"), Does.Contain(addFunction.Script));
        realm.Execute(script);

        Assert.That(debugger.Checkpoints.Count(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Breakpoint),
            Is.EqualTo(2));
        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.Kind == ExecutionCheckpointKind.Breakpoint &&
            checkpoint.SourceLocation is { } sourceLocation &&
            sourceLocation.SourcePath == "breakpoint.js" &&
            sourceLocation.Line == 2), Is.True);

        debugger.Checkpoints.Clear();
        breakpoint.Dispose();
        realm.Execute(script);

        Assert.That(debugger.Checkpoints.Count(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Breakpoint),
            Is.EqualTo(0));
    }

    [Test]
    public void ScriptBreakpoint_Hits_Exact_Pc_And_Rearms_On_Next_Execute()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableBreakpointHook();
        }));
        var realm = runtime.DefaultRealm;
        var program = JavaScriptParser.ParseScript("""
                                                   function add(a, b) {
                                                       return a + b;
                                                   }
                                                   globalThis.out = add(1, 2);
                                                   """, "pc-breakpoint.js");
        var script = JsCompiler.Compile(realm, program);
        var addFunction = script.ObjectConstants.OfType<JsBytecodeFunction>()
            .Single(function => function.Name == "add");

        using var breakpoint = runtime.MainAgent.AddBreakpoint(addFunction.Script, 2);

        Assert.That(breakpoint.ProgramCounter, Is.EqualTo(2));

        realm.Execute(script);
        realm.Execute(script);

        Assert.That(debugger.Checkpoints.Count(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Breakpoint),
            Is.EqualTo(2));
        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.Kind == ExecutionCheckpointKind.Breakpoint &&
            checkpoint.SourceLocation is { } sourceLocation &&
            sourceLocation.SourcePath == "pc-breakpoint.js" &&
            sourceLocation.Line > 0), Is.True);
    }

    [Test]
    public void CompiledScript_Binds_Agent_And_Cached_Breakpoint_Scripts()
    {
        var runtime = JsRuntime.Create();
        var realm = runtime.DefaultRealm;
        var program = JavaScriptParser.ParseScript("""
                                                   function add(a, b) {
                                                       return a + b;
                                                   }
                                                   globalThis.out = add(1, 2);
                                                   """, "bind-check.js");
        var script = JsCompiler.Compile(realm, program);
        var addFunction = script.ObjectConstants.OfType<JsBytecodeFunction>()
            .Single(function => function.Name == "add");
        var debugRegistry = runtime.MainAgent.ScriptDebugRegistry;
        Assert.That(debugRegistry.IsRegisteredScript(script), Is.True);
        Assert.That(debugRegistry.IsRegisteredScript(addFunction.Script), Is.True);
        Assert.That(debugRegistry.GetRegisteredScripts("bind-check.js"), Does.Contain(script));
        Assert.That(debugRegistry.GetRegisteredScripts("bind-check.js"), Does.Contain(addFunction.Script));
    }

    [Test]
    public void BreakpointHook_Can_Be_Toggled_At_Runtime()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.DisableBreakpointHook();
        }));
        var realm = runtime.DefaultRealm;
        var program = JavaScriptParser.ParseScript("""
                                                   function add(a, b) {
                                                       return a + b;
                                                   }
                                                   globalThis.out = add(1, 2);
                                                   """, "breakpoint-toggle.js");
        var script = JsCompiler.Compile(realm, program);

        using var breakpoint = runtime.MainAgent.AddBreakpoint("breakpoint-toggle.js", 2);

        realm.Execute(script);

        Assert.That(debugger.Checkpoints.Count(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Breakpoint),
            Is.EqualTo(0));

        runtime.MainAgent.EnableBreakpointHook();
        realm.Execute(script);

        Assert.That(debugger.Checkpoints.Count(checkpoint => checkpoint.Kind == ExecutionCheckpointKind.Breakpoint),
            Is.EqualTo(1));
        Assert.That(debugger.Checkpoints.Any(checkpoint =>
            checkpoint.Kind == ExecutionCheckpointKind.Breakpoint &&
            checkpoint.SourceLocation is { } sourceLocation &&
            sourceLocation.SourcePath == "breakpoint-toggle.js" &&
            sourceLocation.Line == 2), Is.True);
    }
}
