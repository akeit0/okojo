using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public partial class ExecutionCheckTests
{
    [Test]
    public void DebuggerStatement_Exposes_Named_Locals_And_ContextSlots()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableDebuggerStatementHook();
        }));
        var realm = runtime.DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function outer() {
                let captured = 1;
                function inner() {
                    return captured;
                }
                let x = 1;
                {
                    let x = 2;
                    debugger;
                }
                return inner();
            }
            outer();
            """, "locals-debug.js"));

        realm.Execute(script);

        var checkpoint = debugger.Checkpoints.First(checkpoint =>
            checkpoint.Kind == ExecutionCheckpointKind.DebuggerStatement &&
            checkpoint.SourcePath == "locals-debug.js");
        var locals = checkpoint.Locals;
        var localValues = checkpoint.LocalValues;
        var scopeChain = checkpoint.ScopeChain;

        Assert.That(locals, Is.Not.Null);
        Assert.That(localValues, Is.Not.Null);
        Assert.That(scopeChain, Is.Not.Null);
        Assert.That(checkpoint.TryGetLocalValue("captured", out var capturedValue), Is.True);
        Assert.That(locals!.Any(local =>
            local.Name == "captured" &&
            local.StorageKind == JsLocalDebugStorageKind.ContextSlot &&
            local.IsLiveAt(checkpoint.ProgramCounter) &&
            local.StartPc <= checkpoint.ProgramCounter &&
            checkpoint.ProgramCounter < local.EndPc), Is.True);
        Assert.That(locals.Count(local => local.Name == "x"), Is.EqualTo(1));
        Assert.That(locals.Any(local =>
            local.Name == "x" &&
            local.StorageKind == JsLocalDebugStorageKind.Register &&
            local.IsLiveAt(checkpoint.ProgramCounter)), Is.True);
        Assert.That(localValues!.Any(local =>
            local.Name == "captured" &&
            local.StorageKind == JsLocalDebugStorageKind.ContextSlot &&
            local.Value.IsInt32 &&
            local.Value.Int32Value == 1), Is.True);
        Assert.That(localValues.Any(local =>
            local.Name == "x" &&
            local.StorageKind == JsLocalDebugStorageKind.Register &&
            local.Value.IsInt32 &&
            local.Value.Int32Value == 2), Is.True);
        Assert.That(capturedValue.Value.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void DebuggerStatement_Uses_Innermost_Local_Range_For_Shadowed_Name()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableDebuggerStatementHook();
        }));
        var realm = runtime.DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function outer() {
                let x = 1;
                {
                    let x = 2;
                    debugger;
                }
                return x;
            }
            outer();
            """, "locals-shadow.js"));

        realm.Execute(script);

        var checkpoint = debugger.Checkpoints.First(checkpoint =>
            checkpoint.Kind == ExecutionCheckpointKind.DebuggerStatement &&
            checkpoint.SourcePath == "locals-shadow.js");
        var locals = checkpoint.Locals;
        var localValues = checkpoint.LocalValues;
        var scopeChain = checkpoint.ScopeChain;

        Assert.That(locals, Is.Not.Null);
        Assert.That(localValues, Is.Not.Null);
        Assert.That(scopeChain, Is.Not.Null);
        var xLocals = locals!.Where(local => local.Name == "x").ToArray();
        Assert.That(xLocals, Has.Length.EqualTo(1));
        Assert.That(xLocals[0].StorageKind, Is.EqualTo(JsLocalDebugStorageKind.Register));
        Assert.That(xLocals[0].StartPc, Is.LessThanOrEqualTo(checkpoint.ProgramCounter));
        Assert.That(checkpoint.ProgramCounter, Is.LessThan(xLocals[0].EndPc));
        Assert.That(localValues!.Single(local => local.Name == "x").Value.Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void DebuggerStatement_Resolves_Outer_Captured_Local_Via_Scope_Chain()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder => builder.UseAgent(agent =>
        {
            agent.DebuggerSession = debugger;
            agent.EnableDebuggerStatementHook();
        }));
        var realm = runtime.DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            function outer() {
                let captured = 1;
                function inner() {
                    debugger;
                }
                inner();
            }
            outer();
            """, "locals-chain.js"));

        realm.Execute(script);

        var checkpoint = debugger.Checkpoints.First(checkpoint =>
            checkpoint.Kind == ExecutionCheckpointKind.DebuggerStatement &&
            checkpoint.SourcePath == "locals-chain.js");

        Assert.That(checkpoint.ScopeChain, Is.Not.Null);
        Assert.That(checkpoint.ScopeChain!.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(checkpoint.TryGetLocalValue("captured", out var capturedValue), Is.True);
        Assert.That(capturedValue.Value.IsInt32, Is.True);
        Assert.That(capturedValue.Value.Int32Value, Is.EqualTo(1));
        Assert.That(checkpoint.ScopeChain.Skip(1).Any(scope => scope.TryGetLocalValue("captured", out _)), Is.True);
    }
}
