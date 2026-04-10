using System.Text.Json;

namespace Okojo.DebugServer.Tests;

public sealed partial class OkojoDebugServerIntegrationTests
{
    [Test]
    public async Task Evaluate_Resolves_Paused_Local_Property_Path()
    {
        await using var workspace = new TempWorkspace("""
            const obj = { value: 42 };
            debugger;
            console.log(obj.value);
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1",
        });

        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("debugger-statement"));

        server.SendCommand("evaluate 1 1 \"obj.value\"");
        JsonElement evaluate = await server.WaitForJsonEventAsync("evaluate", TimeSpan.FromSeconds(10));
        Assert.That(evaluate.TryGetProperty("success", out var success) && success.GetBoolean(), Is.True);
        Assert.That(GetString(evaluate, "result"), Is.EqualTo("Number(42)"));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }
}
