using Microsoft.Extensions.Time.Testing;
using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public partial class ExecutionCheckTests
{
    [Test]
    public void ReceiverLoadFollowedByStore_PreservesPeriodicCheckpointPositions()
    {
        var debugger = new RecordingDebugger();
        using var runtime = JsRuntime.Create(builder =>
            builder.UseAgent(agent =>
            {
                agent.DebuggerSession = debugger;
                agent.SetCheckInterval(3);
            })
        );
        var realm = runtime.DefaultRealm;
        var code = new JsFunctionCode(
            [
                (byte)JsOpCode.LdaZero,
                (byte)JsOpCode.LdaThis,
                (byte)JsOpCode.Star,
                0,
                (byte)JsOpCode.LdaThis,
                (byte)JsOpCode.Star,
                0,
                (byte)JsOpCode.LdaThis,
                (byte)JsOpCode.Star,
                0,
                (byte)JsOpCode.LdaSmi,
                42,
                (byte)JsOpCode.Return,
            ],
            [],
            [],
            1,
            []
        );
        realm.Execute(new JsCompilationUnit(new JsFunctionDescriptor(code)).Link(realm));

        Assert.That(
            debugger.Checkpoints.Select(checkpoint => checkpoint.ProgramCounter),
            Is.EqualTo(new[] { 2, 7, 12 })
        );
    }

    [Test]
    public void HasChecks_Tracks_Combined_Runtime_Settings()
    {
        using var runtime = JsRuntime.Create();
        var agent = runtime.DefaultRealm.Agent;
        var policy = agent.ExecutionCheckPolicy;
        using var cancellation = new CancellationTokenSource();
        Assert.That(policy.HasChecks, Is.False);
        var configured = new ExecutionCheckPolicy(
            new JsAgentOptions { DebuggerSession = new RecordingDebugger() },
            TimeProvider.System
        );
        Assert.That(configured.HasChecks, Is.True);

        agent.AttachDebugger(new RecordingDebugger());
        agent.SetExecutionCancellationToken(cancellation.Token);
        agent.DetachDebugger();
        Assert.That(policy.HasChecks, Is.True);
        agent.ClearExecutionCancellationToken();
        Assert.That(policy.HasChecks, Is.False);

        agent.SetExecutionTimeout(TimeSpan.FromSeconds(1));
        Assert.That(agent.ResetExecutionTimeout(), Is.True);
        Assert.That(policy.HasChecks, Is.True);
        agent.ClearExecutionTimeout();
        Assert.That(policy.HasChecks, Is.False);

        agent.SetMaxInstructions(100);
        agent.SetCheckInterval(10);
        agent.SetMaxInstructions(ulong.MaxValue);
        Assert.That(policy.HasChecks, Is.True);
        agent.SetCheckInterval(ulong.MaxValue);
        Assert.That(policy.HasChecks, Is.False);
        agent.AddConstraint(new RecordingConstraint());
        Assert.That(policy.HasChecks, Is.True);
    }

    [Test]
    public void ExecutionConstraint_Fires_On_Checkpoint()
    {
        var constraint = new ThrowingConstraint();
        var runtime = JsRuntime.Create(builder =>
            builder.UseAgent(agent =>
            {
                agent.SetCheckInterval(1);
                agent.AddConstraint(constraint);
            })
        );
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let total = 0;
                total = total + 1;
                """
            )
        );

        Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(constraint.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void MaxInstructions_Exceeded_Throws_RangeError()
    {
        var runtime = JsRuntime.Create(builder =>
            builder.UseAgent(agent =>
            {
                agent.SetCheckInterval(1);
                agent.SetMaxInstructions(1);
            })
        );
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let total = 0;
                total = total + 1;
                total = total + 1;
                """
            )
        );

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.RangeError));
        Assert.That(ex.DetailCode, Is.EqualTo("EXECUTION_LIMIT_EXCEEDED"));
        Assert.That(ex.StackFrames.Count, Is.GreaterThan(0));
        Assert.That(ex.FormatOkojoStackTrace(), Is.Not.Empty);
    }

    [Test]
    public void ExecutionTimeout_Exceeded_Throws_RangeError()
    {
        var fakeTime = new FakeTimeProvider();
        var runtime = JsRuntime.Create(builder =>
            builder
                .UseTimeProvider(fakeTime)
                .UseAgent(agent =>
                {
                    agent.SetCheckInterval(1);
                    agent.SetExecutionTimeout(TimeSpan.FromMilliseconds(1));
                })
        );
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let total = 0;
                total = total + 1;
                total = total + 1;
                """
            )
        );

        fakeTime.Advance(TimeSpan.FromMilliseconds(10));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.RangeError));
        Assert.That(ex.DetailCode, Is.EqualTo("EXECUTION_TIMEOUT_EXCEEDED"));
        Assert.That(ex.StackFrames.Count, Is.GreaterThan(0));
        Assert.That(ex.FormatOkojoStackTrace(), Is.Not.Empty);
    }

    [Test]
    public void Agent_ResetExecutionTimeout_Restarts_Current_Timeout_Window()
    {
        var fakeTime = new FakeTimeProvider();
        var runtime = JsRuntime.Create(builder =>
            builder.UseTimeProvider(fakeTime).UseAgent(agent => agent.SetCheckInterval(1))
        );
        var agent = runtime.MainAgent;
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let total = 0;
                total = total + 1;
                total = total + 1;
                """
            )
        );

        agent.SetExecutionTimeout(TimeSpan.FromMilliseconds(1));
        fakeTime.Advance(TimeSpan.FromMilliseconds(10));
        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.DetailCode, Is.EqualTo("EXECUTION_TIMEOUT_EXCEEDED"));

        Assert.That(agent.ResetExecutionTimeout(), Is.True);
        Assert.DoesNotThrow(() => realm.Execute(script));
    }

    [Test]
    public void Agent_ResetExecutedInstructions_Clears_Cumulative_Limit_State()
    {
        var runtime = JsRuntime.Create(builder =>
            builder.UseAgent(agent => agent.SetCheckInterval(1))
        );
        var agent = runtime.MainAgent;
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let total = 0;
                total = total + 1;
                total = total + 1;
                """
            )
        );
        var recorder = new RecordingConstraint();
        agent.AddConstraint(recorder);

        realm.Execute(script);
        Assert.That(recorder.LastCheckpoint, Is.Not.Null);

        agent.SetMaxInstructions(recorder.LastCheckpoint!.Value.ExecutedInstructions);
        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.DetailCode, Is.EqualTo("EXECUTION_LIMIT_EXCEEDED"));

        agent.ResetExecutedInstructions();
        Assert.DoesNotThrow(() => realm.Execute(script));
    }

    [Test]
    public void PeriodicCheckpoint_Provides_Source_Location()
    {
        var debugger = new RecordingDebugger();
        var runtime = JsRuntime.Create(builder =>
            builder.UseAgent(agent =>
            {
                agent.DebuggerSession = debugger;
                agent.SetCheckInterval(1);
            })
        );
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let total = 0;
                total = total + 1;
                """,
                "periodic-stop.js"
            )
        );

        realm.Execute(script);

        Assert.That(
            debugger.Checkpoints.Any(checkpoint =>
                checkpoint.Kind == ExecutionCheckpointKind.Periodic
                && checkpoint.SourceLocation is { } sourceLocation
                && sourceLocation.SourcePath == "periodic-stop.js"
                && sourceLocation.Line > 0
                && sourceLocation.Column > 0
            ),
            Is.True
        );
    }

    [Test]
    public void ExecutionCancellationToken_Exceeded_Throws_RangeError()
    {
        using var cancellationSource = new CancellationTokenSource();
        var runtime = JsRuntime.Create(builder =>
            builder.UseAgent(agent =>
            {
                agent.SetCheckInterval(1);
                agent.SetExecutionCancellationToken(cancellationSource.Token);
            })
        );
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let total = 0;
                total = total + 1;
                total = total + 1;
                """
            )
        );

        cancellationSource.Cancel();

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.RangeError));
        Assert.That(ex.DetailCode, Is.EqualTo("EXECUTION_CANCELED"));
    }

    [Test]
    public void Agent_ClearExecutionCancellationToken_Removes_Cancel_Stop()
    {
        using var cancellationSource = new CancellationTokenSource();
        var runtime = JsRuntime.Create(builder =>
            builder.UseAgent(agent => agent.SetCheckInterval(1))
        );
        var agent = runtime.MainAgent;
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                let total = 0;
                total = total + 1;
                total = total + 1;
                """
            )
        );

        agent.SetExecutionCancellationToken(cancellationSource.Token);
        cancellationSource.Cancel();
        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.DetailCode, Is.EqualTo("EXECUTION_CANCELED"));

        agent.ClearExecutionCancellationToken();
        Assert.DoesNotThrow(() => realm.Execute(script));
    }
}
