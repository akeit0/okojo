namespace Okojo.DebugServer.Tests;

public sealed partial class OkojoDebugServerIntegrationTests
{
    [Test]
    public async Task Step_Stops_On_Next_Caller_Line()
    {
        await using var workspace = new TempWorkspace("""
            function helper() {
              console.log("inside helper");
            }

            function outer() {
              debugger;
              helper();
              console.log("after step");
            }

            outer();
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1"
        });

        var entry = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(entry, "kind"), Is.EqualTo("debugger-statement"));
        Assert.That(GetNestedInt(entry, "sourceLocation", "line"), Is.EqualTo(6));

        server.SendCommand("step");
        var stepped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stepped, "kind"), Is.EqualTo("step"));
        Assert.That(GetNestedString(stepped, "currentFrame", "functionName"), Is.EqualTo("outer"));
        Assert.That(GetNestedInt(stepped, "sourceLocation", "line"), Is.EqualTo(7));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task StepIn_Enters_Callee_Line()
    {
        await using var workspace = new TempWorkspace("""
            function helper() {
              console.log("inside helper");
            }

            function outer() {
              debugger;
              helper();
              console.log("after step in");
            }

            outer();
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1"
        });

        _ = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));

        server.SendCommand("step");
        var atCall = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetNestedInt(atCall, "sourceLocation", "line"), Is.EqualTo(7));

        server.SendCommand("stepin");
        var stepped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stepped, "kind"), Is.EqualTo("step"));
        Assert.That(GetNestedString(stepped, "currentFrame", "functionName"), Is.EqualTo("helper"));
        Assert.That(GetNestedInt(stepped, "sourceLocation", "line"), Is.EqualTo(2));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task StepOver_Call_Stops_On_Following_Caller_Line()
    {
        await using var workspace = new TempWorkspace("""
            function helper() {
              console.log("inside helper");
            }

            function outer() {
              debugger;
              helper();
              console.log("after step over");
            }

            outer();
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1"
        });

        _ = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));

        server.SendCommand("step");
        var atCall = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetNestedInt(atCall, "sourceLocation", "line"), Is.EqualTo(7));

        server.SendCommand("step");
        var stepped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stepped, "kind"), Is.EqualTo("step"));
        Assert.That(GetNestedString(stepped, "currentFrame", "functionName"), Is.EqualTo("outer"));
        Assert.That(GetNestedInt(stepped, "sourceLocation", "line"), Is.EqualTo(8));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task StepOut_Returns_To_Caller_Line()
    {
        await using var workspace = new TempWorkspace("""
            function helper() {
              debugger;
              console.log("inside helper");
            }

            function outer() {
              helper();
              console.log("after step out");
            }

            outer();
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1"
        });

        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("debugger-statement"));
        Assert.That(GetNestedString(stopped, "currentFrame", "functionName"), Is.EqualTo("helper"));

        server.SendCommand("stepout");
        var stepped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stepped, "kind"), Is.EqualTo("step"));
        Assert.That(GetNestedString(stepped, "currentFrame", "functionName"), Is.EqualTo("outer"));
        Assert.That(GetNestedInt(stepped, "sourceLocation", "line"), Is.EqualTo(8));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task Instruction_Granularity_Steps_Within_Same_Source_Line()
    {
        await using var workspace = new TempWorkspace("""
            function outer() {
              debugger; const left = 1, right = 2;
              console.log(left + right);
            }

            outer();
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1"
        });

        _ = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));

        server.SendCommand("stepmode instruction");
        var optionUpdated = await server.WaitForJsonEventAsync("option-updated", TimeSpan.FromSeconds(10));
        Assert.That(GetString(optionUpdated, "name"), Is.EqualTo("stepGranularity"));
        Assert.That(GetString(optionUpdated, "value"), Is.EqualTo("Instruction"));

        server.SendCommand("step");
        var stepped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stepped, "kind"), Is.EqualTo("step"));
        Assert.That(GetNestedInt(stepped, "sourceLocation", "line"), Is.EqualTo(2));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task StepOver_Still_Stops_On_Explicit_DebuggerStatement()
    {
        await using var workspace = new TempWorkspace("""
            function helper() {
              debugger;
              return 1;
            }

            function outer() {
              debugger;
              helper();
              console.log("after step over");
            }

            outer();
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1"
        });

        _ = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));

        server.SendCommand("step");
        var atCall = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(atCall, "kind"), Is.EqualTo("step"));
        Assert.That(GetNestedInt(atCall, "sourceLocation", "line"), Is.EqualTo(8));

        server.SendCommand("step");
        var debuggerStop = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(debuggerStop, "kind"), Is.EqualTo("debugger-statement"));
        Assert.That(GetNestedString(debuggerStop, "currentFrame", "functionName"), Is.EqualTo("helper"));
        Assert.That(GetNestedInt(debuggerStop, "sourceLocation", "line"), Is.EqualTo(2));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task LineStep_Ignores_Unmapped_Instructions()
    {
        await using var workspace = new TempWorkspace("""
            function outer() {
              debugger;
              const left = 1;
              const right = 2;
              console.log(left + right);
            }

            outer();
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1"
        });

        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("debugger-statement"));
        Assert.That(GetNestedInt(stopped, "sourceLocation", "line"), Is.EqualTo(2));

        server.SendCommand("step");
        var stepped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stepped, "kind"), Is.EqualTo("step"));
        Assert.That(GetNestedInt(stepped, "sourceLocation", "line"), Is.EqualTo(3));

        server.SendCommand("step");
        var next = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(next, "kind"), Is.EqualTo("step"));
        Assert.That(GetNestedInt(next, "sourceLocation", "line"), Is.EqualTo(4));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task LineStep_Does_Not_Fall_Back_To_Line_One_Inside_Template_Literal_Evaluation()
    {
        await using var workspace = new TempWorkspace("""
            function outer() {
              const message = "answer";
              debugger;
              console.log(`entry: ${message}`);
              return message;
            }

            outer();
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1"
        });

        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("debugger-statement"));
        Assert.That(GetNestedInt(stopped, "sourceLocation", "line"), Is.EqualTo(3));

        server.SendCommand("step");
        var templateLine = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(templateLine, "kind"), Is.EqualTo("step"));
        Assert.That(GetNestedInt(templateLine, "sourceLocation", "line"), Is.EqualTo(4));

        server.SendCommand("step");
        var afterTemplate = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(afterTemplate, "kind"), Is.EqualTo("step"));
        Assert.That(GetNestedInt(afterTemplate, "sourceLocation", "line"), Is.EqualTo(5));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task LineStep_From_Breakpoint_Skips_Adjacent_Same_Line_Stop()
    {
        await using var workspace = new TempWorkspace("""
            const message = "answer";
            debugger;
            console.log(`entry: ${message}`);
            console.log("after");
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1"
        });

        var debuggerStop = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(debuggerStop, "kind"), Is.EqualTo("debugger-statement"));
        Assert.That(GetNestedInt(debuggerStop, "sourceLocation", "line"), Is.EqualTo(2));

        server.SendCommand($"break {workspace.ScriptPath}:3");
        _ = await server.WaitForJsonEventAsync("breakpoint-added", TimeSpan.FromSeconds(10));

        server.SendCommand("continue");
        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("breakpoint"));
        Assert.That(GetNestedInt(stopped, "sourceLocation", "line"), Is.EqualTo(3));

        server.SendCommand("step");
        var stepped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stepped, "kind"), Is.EqualTo("step"));
        Assert.That(GetNestedInt(stepped, "sourceLocation", "line"), Is.EqualTo(4));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }
}
