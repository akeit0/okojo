using Okojo.Hosting;
using Okojo.Runtime;

namespace Okojo.DebugServer.Tests;

public sealed partial class OkojoDebugServerIntegrationTests
{
    [Test]
    public async Task StopOnEntry_Pauses_Until_Continue()
    {
        await using var workspace = new TempWorkspace("""
            console.log("before entry");
            console.log("after entry");
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--stop-entry",
            "--check-interval", "1"
        });

        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("entry"));
        Assert.That(GetString(stopped, "summary"), Does.Contain("entry"));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task DebuggerStatement_Pauses_Execution()
    {
        await using var workspace = new TempWorkspace("""
            console.log("before debugger");
            debugger;
            console.log("after debugger");
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1",
        });

        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("debugger-statement"));
        Assert.That(GetString(stopped, "summary"), Does.Contain("debugger"));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task SourceBreakpoint_Pauses_Execution()
    {
        await using var workspace = new TempWorkspace("""
            console.log("before breakpoint");
            const value = 1;
            console.log(value);
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1",
            "--break", $"{workspace.ScriptPath}:3"
        });

        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("breakpoint"));
        Assert.That(GetString(stopped, "summary"), Does.Contain(Path.GetFileName(workspace.ScriptPath)));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task SourceBreakpoint_Is_CaseInsensitive_On_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows-only source path case behavior.");
            return;
        }

        await using var workspace = new TempWorkspace("""
            const value = 1;
            debugger;
            console.log(value);
            """);

        string lowerPath = char.ToLowerInvariant(workspace.ScriptPath[0]) + workspace.ScriptPath[1..];
        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1",
            "--break", $"{lowerPath}:2"
        });

        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("breakpoint"));
        Assert.That(GetNestedInt(stopped, "sourceLocation", "line"), Is.EqualTo(2));

        server.SendCommand("continue");
        var debuggerStop = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(debuggerStop, "kind"), Is.EqualTo("debugger-statement"));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task Breakpoint_On_Debugger_Line_Stops_Once_And_Then_Continues()
    {
        await using var workspace = new TempWorkspace("""
            console.log("before");
            debugger;
            console.log("after");
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1",
            "--break", $"{workspace.ScriptPath}:2"
        });

        var first = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(first, "kind"), Is.EqualTo("breakpoint"));
        int? firstLine = GetNestedInt(first, "sourceLocation", "line");
        Assert.That(firstLine, Is.GreaterThan(0));

        server.SendCommand("continue");
        var second = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(second, "kind"), Is.EqualTo("debugger-statement"));
        Assert.That(GetNestedInt(second, "sourceLocation", "line"), Is.EqualTo(firstLine));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task ToggleDebuggerOption_Disables_Next_Debugger_Stop()
    {
        await using var workspace = new TempWorkspace("""
            debugger;
            console.log("between");
            debugger;
            console.log("after");
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1"
        });

        var first = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(first, "kind"), Is.EqualTo("debugger-statement"));

        server.SendCommand("toggle debugger");
        server.SendCommand("continue");

        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task ModuleEntry_Breakpoint_Pauses_On_Requested_Line()
    {
        await using var workspace = new TempModuleWorkspace();
        workspace.WriteFile("entry.mjs", """
            import { multiply } from './lib/math.mjs';

            function run() {
              const product = multiply(6, 7);
              return product;
            }

            run();
            """);
        workspace.WriteFile("lib/math.mjs", """
            export function multiply(left, right) {
              return left * right;
            }
            """);

        await using var server = DebugServerProcess.Start(workspace.EntryPath, new[]
        {
            "--cwd", workspace.Root,
            "--module-entry",
            "--check-interval", "1",
            "--break", $"{workspace.EntryPath}:8"
        });

        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("breakpoint"));
        Assert.That(GetNestedInt(stopped, "sourceLocation", "line"), Is.EqualTo(8));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task ModuleNestedFunction_Breakpoint_Pauses_On_Requested_Line()
    {
        await using var workspace = new TempModuleWorkspace();
        workspace.WriteFile("entry.mjs", """
            import { multiply } from './lib/math.mjs';
            import { formatMessage } from './lib/message.mjs';

            function run() {
              const product = multiply(6, 7);
              return formatMessage('answer', product);
            }

            run();
            """);
        workspace.WriteFile("lib/math.mjs", """
            export function multiply(left, right) {
              const product = left * right;
              return product;
            }
            """);
        workspace.WriteFile("lib/message.mjs", """
            export function formatMessage(label, value) {
              const prefix = 'result';
              return `${prefix} ${label}: ${value}`;
            }
            """);

        await using var server = DebugServerProcess.Start(workspace.EntryPath, new[]
        {
            "--cwd", workspace.Root,
            "--module-entry",
            "--check-interval", "1",
            "--stop-entry",
            "--break", $"{Path.Combine(workspace.Root, "lib", "math.mjs")}:2"
        });

        var entry = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(entry, "kind"), Is.EqualTo("entry"));

        server.SendCommand("continue");
        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("breakpoint"));
        Assert.That(GetNestedString(stopped, "sourceLocation", "sourcePath"), Does.EndWith("math.mjs"));
        Assert.That(GetNestedInt(stopped, "sourceLocation", "line"), Is.EqualTo(2));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public async Task Breakpoint_On_NonExecutable_Line_Relocates_To_Next_Executable_Line()
    {
        await using var workspace = new TempWorkspace("""
            function run() {
            }

            console.log('after');
            run();
            """);

        await using var server = DebugServerProcess.Start(workspace.ScriptPath, new[]
        {
            "--cwd", workspace.Root,
            "--check-interval", "1",
            "--break", $"{workspace.ScriptPath}:2"
        });

        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("breakpoint"));
        Assert.That(GetNestedInt(stopped, "sourceLocation", "line"), Is.EqualTo(4));

        server.SendCommand("continue");
        var terminated = await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(terminated, "exitCode"), Is.EqualTo(0));
    }

    [Test]
    public void ModuleEntry_Registers_Source_Line_Mapping_For_Nested_Functions()
    {
        var workspace = new TempModuleWorkspace();
        try
        {
            workspace.WriteFile("entry.mjs", """
                import { multiply } from './lib/math.mjs';

                function run() {
                  const product = multiply(6, 7);
                  return product;
                }

                run();
                """);
            workspace.WriteFile("lib/math.mjs", """
                export function multiply(left, right) {
                  return left * right;
                }
                """);

            using var runtime = JsRuntime.Create(builder => builder.UseThreadPoolHosting());
            _ = runtime.LoadModule(workspace.EntryPath);

            var scripts = runtime.MainAgent.ScriptDebugRegistry.GetRegisteredScripts(workspace.EntryPath);
            Assert.That(scripts.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(scripts.Any(static script => HasAnyPcForLine(script, 4)), Is.True);
            var rootScript = scripts.First(static script => HasAnyPcForLine(script, 8));
            Assert.That(HasAnyPcForLine(rootScript, 3), Is.False);
            Assert.That(TryGetFirstPcForLine(rootScript, 8, out _), Is.True);
        }
        finally
        {
            workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static bool HasAnyPcForLine(Okojo.Bytecode.JsScript script, int line)
    {
        for (int pc = 0; pc < script.Bytecode.Length; pc++)
        {
            if (script.TryGetSourceLocationAtPc(pc, out int currentLine, out _) && currentLine == line)
                return true;
        }

        return false;
    }

    private static bool TryGetFirstPcForLine(Okojo.Bytecode.JsScript script, int line, out int pc)
    {
        for (pc = 0; pc < script.Bytecode.Length; pc++)
        {
            if (script.TryGetSourceLocationAtPc(pc, out int currentLine, out _) && currentLine == line)
                return true;
        }

        pc = -1;
        return false;
    }
}
