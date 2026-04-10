namespace Okojo.DebugServer.Tests;

public sealed partial class OkojoDebugServerIntegrationTests
{
    [Test]
    public async Task BytecodeCommand_Returns_Current_Disassembly_With_Stop_Marker()
    {
        await using var workspace = new TempWorkspace("""
            console.log("before bytecode");
            debugger;
            console.log("after bytecode");
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1"
        });

        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("debugger-statement"));

        server.SendCommand("bytecode");
        var bytecode = await server.WaitForJsonEventAsync("bytecode", TimeSpan.FromSeconds(10));
        Assert.That(GetString(bytecode, "title"), Does.Contain(Path.GetFileName(workspace.ScriptPath)));
        Assert.That(GetNestedString(bytecode, "sourceLocation", "sourcePath"), Is.EqualTo(workspace.ScriptPath));
        Assert.That(GetNestedInt(bytecode, "sourceLocation", "line"), Is.EqualTo(2));
        Assert.That(GetString(bytecode, "text"), Does.Contain(".code"));
        Assert.That(GetString(bytecode, "text"), Does.Contain("=> "));
        Assert.That(GetString(bytecode, "text"), Does.Contain("Debugger"));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task BytecodeCommand_Resolves_Imported_Module_Stop()
    {
        await using var workspace = new TempModuleWorkspace();
        workspace.WriteFile("entry.mjs", """
            import "./lib/message.mjs";
            console.log("done");
            """);
        workspace.WriteFile("lib/message.mjs", """
            console.log("module loaded");
            debugger;
            export const message = "hello";
            """);

        await using var server = DebugServerProcess.Start(workspace.EntryPath, new[]
        {
            "--cwd", workspace.Root,
            "--module-entry",
            "--check-interval", "1"
        });

        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("debugger-statement"));
        Assert.That(GetNestedString(stopped, "sourceLocation", "sourcePath"),
            Does.EndWith(Path.Combine("lib", "message.mjs")));

        server.SendCommand("bytecode");
        var bytecode = await server.WaitForJsonEventAsync("bytecode", TimeSpan.FromSeconds(10));
        Assert.That(GetString(bytecode, "title"), Does.Contain("message.mjs"));
        Assert.That(GetString(bytecode, "text"), Does.Contain("=> "));
        Assert.That(GetString(bytecode, "text"), Does.Contain("Debugger"));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task BytecodeCommand_Shows_Original_Opcode_For_Breakpoint_Patch()
    {
        await using var workspace = new TempWorkspace("""
            const value = 1;
            console.log(value);
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1",
            "--break", $"{workspace.ScriptPath}:2"
        });

        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("breakpoint"));

        server.SendCommand("bytecode");
        var bytecode = await server.WaitForJsonEventAsync("bytecode", TimeSpan.FromSeconds(10));
        string? text = GetString(bytecode, "text");
        Assert.That(text, Is.Not.Null);
        string? highlightedLine = text!.Split('\n').FirstOrDefault(static line => line.StartsWith("=> "));
        Assert.That(text, Does.Contain("=> "));
        Assert.That(highlightedLine, Is.Not.Null);
        Assert.That(highlightedLine!, Does.Not.Contain("Debugger"));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }
}
