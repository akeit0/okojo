using System.Text.Json;

namespace Okojo.DebugServer.Tests;

public sealed partial class OkojoDebugServerIntegrationTests
{
    [Test]
    public async Task BreakpointUpdate_Publishes_Relocated_Line_Info()
    {
        await using var workspace = new TempWorkspace("""
            function run() {
            }

            console.log("after");
            run();
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--stop-entry",
            "--check-interval", "1",
            "--break", $"{workspace.ScriptPath}:2"
        });

        var added = await server.WaitForJsonEventAsync("breakpoint-added", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(added, "requestedLine"), Is.EqualTo(2));
        Assert.That(added.TryGetProperty("verified", out var addedVerified) && addedVerified.ValueKind == JsonValueKind.False, Is.True);

        _ = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        server.SendCommand("continue");

        var updated = await server.WaitForJsonEventAsync("breakpoint-updated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(updated, "requestedLine"), Is.EqualTo(2));
        Assert.That(updated.TryGetProperty("verified", out var updatedVerified) && updatedVerified.GetBoolean(), Is.True);
        Assert.That(GetInt(updated, "resolvedLine"), Is.EqualTo(4));
        Assert.That(GetInt(updated, "programCounter"), Is.GreaterThanOrEqualTo(0));
    }
}
