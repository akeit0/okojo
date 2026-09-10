using System.Text.Json;

namespace Okojo.DebugServer.Tests;

public sealed partial class OkojoDebugServerIntegrationTests
{
    private static async Task<JsonElement> HostRequestAsync(
        DebugServerProcess server,
        int id,
        string command,
        object? arguments = null,
        bool success = true
    )
    {
        server.SendCommand(
            JsonSerializer.Serialize(
                new
                {
                    id,
                    command,
                    arguments = arguments ?? new { },
                }
            )
        );
        var response = await server.WaitForJsonEventAsync("response", TimeSpan.FromSeconds(10));
        Assert.That(GetInt(response, "requestId"), Is.EqualTo(id));
        Assert.That(
            response.GetProperty("success").GetBoolean(),
            Is.EqualTo(success),
            response.ToString()
        );
        return response;
    }

    [Test]
    public async Task Protocol_Inspects_Cycles_And_Arrays_Without_Invoking_Getters()
    {
        await using var workspace = new TempWorkspace(
            """
            let getterCalls = 0;
            const object = { value: 42, items: [10, 20, 30], get danger() { getterCalls++; throw new Error("getter invoked"); } };
            object.self = object;
            debugger;
            console.log(getterCalls);
            """
        );
        await using var server = DebugServerProcess.Start(
            workspace.ScriptPath,
            ["--check-interval", "1024"]
        );
        await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        var evaluated = await HostRequestAsync(
            server,
            1,
            "evaluate",
            new { expression = "object" }
        );
        int reference = evaluated.GetProperty("body").GetProperty("variablesReference").GetInt32();
        var children = await HostRequestAsync(
            server,
            2,
            "variables",
            new { variablesReference = reference }
        );
        var values = children
            .GetProperty("body")
            .GetProperty("variables")
            .EnumerateArray()
            .ToArray();
        Assert.That(
            GetString(values.Single(item => GetString(item, "name") == "danger"), "value"),
            Is.EqualTo("<accessor>")
        );
        Assert.That(
            GetInt(values.Single(item => GetString(item, "name") == "self"), "variablesReference"),
            Is.EqualTo(reference)
        );
        int array = values
            .Single(item => GetString(item, "name") == "items")
            .GetProperty("variablesReference")
            .GetInt32();
        var page = await HostRequestAsync(
            server,
            3,
            "variables",
            new
            {
                variablesReference = array,
                filter = "indexed",
                start = 1,
                count = 1,
            }
        );
        var items = page.GetProperty("body").GetProperty("variables").EnumerateArray().ToArray();
        Assert.That(items, Has.Length.EqualTo(1));
        Assert.That(GetString(items[0], "name"), Is.EqualTo("1"));
        Assert.That(GetString(items[0], "value"), Is.EqualTo("Number(20)"));
        await HostRequestAsync(
            server,
            4,
            "evaluate",
            new { expression = "object.danger" },
            success: false
        );
        var calls = await HostRequestAsync(
            server,
            5,
            "evaluate",
            new { expression = "getterCalls" }
        );
        Assert.That(GetNestedString(calls, "body", "result"), Is.EqualTo("Number(0)"));
        server.SendCommand("quit");
        await server.WaitForExitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Protocol_Uses_Selected_Frame_Not_Dynamic_Caller_Scope()
    {
        await using var workspace = new TempWorkspace(
            """
            function inner(value) {
              debugger;
              return value;
            }
            function caller(value) {
              const callerOnly = 99;
              return inner(value + 1) + callerOnly;
            }
            caller(7);
            """
        );
        await using var server = DebugServerProcess.Start(
            workspace.ScriptPath,
            ["--check-interval", "1024"]
        );
        await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        var inner = await HostRequestAsync(
            server,
            1,
            "evaluate",
            new { frameId = 1, expression = "value" }
        );
        var caller = await HostRequestAsync(
            server,
            2,
            "evaluate",
            new { frameId = 2, expression = "value" }
        );
        Assert.That(GetNestedString(inner, "body", "result"), Is.EqualTo("Number(8)"));
        Assert.That(GetNestedString(caller, "body", "result"), Is.EqualTo("Number(7)"));
        await HostRequestAsync(
            server,
            3,
            "evaluate",
            new { frameId = 1, expression = "callerOnly" },
            success: false
        );
        var scopes = await HostRequestAsync(server, 4, "scopes", new { frameId = 1 });
        var names = scopes
            .GetProperty("body")
            .GetProperty("scopes")
            .EnumerateArray()
            .Select(scope => GetString(scope, "name"));
        Assert.That(names, Is.EqualTo(new[] { "Locals", "Global" }));
        server.SendCommand("quit");
        await server.WaitForExitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Protocol_Expires_References_After_Resume_And_Steps_With_Default_Interval()
    {
        await using var workspace = new TempWorkspace(
            "const object = { value: 1 };\ndebugger;\nobject.value++;\nconsole.log(object.value);\n"
        );
        await using var server = DebugServerProcess.Start(
            workspace.ScriptPath,
            ["--check-interval", "1024"]
        );
        await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        var evaluated = await HostRequestAsync(
            server,
            1,
            "evaluate",
            new { expression = "object" }
        );
        int reference = evaluated.GetProperty("body").GetProperty("variablesReference").GetInt32();
        await HostRequestAsync(server, 2, "resume", new { mode = "step", granularity = "line" });
        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("step"));
        Assert.That(GetNestedInt(stopped, "sourceLocation", "line"), Is.EqualTo(3));
        await HostRequestAsync(
            server,
            3,
            "variables",
            new { variablesReference = reference },
            success: false
        );
        await HostRequestAsync(server, 4, "resume", new { mode = "continue" });
        await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Protocol_Breakpoint_Replacement_Supports_Spaces_And_Unicode()
    {
        await using var workspace = new TempWorkspace(
            "let value = 0;\nvalue++;\nvalue++;\nconsole.log(value);\n"
        );
        string script = Path.Combine(workspace.Root, "entry 日本語 with spaces.js");
        File.Move(workspace.ScriptPath, script);
        await using var server = DebugServerProcess.Start(
            script,
            ["--check-interval", "1024", "--stop-entry"]
        );
        await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        await HostRequestAsync(
            server,
            1,
            "setBreakpoints",
            new { sourcePath = script, breakpoints = new[] { new { id = 101, line = 2 } } }
        );
        await HostRequestAsync(
            server,
            2,
            "setBreakpoints",
            new { sourcePath = script, breakpoints = new[] { new { id = 102, line = 3 } } }
        );
        await HostRequestAsync(server, 3, "resume", new { mode = "continue" });
        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("breakpoint"));
        Assert.That(GetNestedInt(stopped, "sourceLocation", "line"), Is.EqualTo(3));
        await HostRequestAsync(
            server,
            4,
            "setBreakpoints",
            new { sourcePath = script, breakpoints = Array.Empty<object>() }
        );
        await HostRequestAsync(server, 5, "resume", new { mode = "continue" });
        await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Protocol_Pause_And_Quit_Interrupt_Catch_Protected_Infinite_Loop()
    {
        await using var workspace = new TempWorkspace(
            "try { while (true) {} } catch (error) { while (true) {} }\n"
        );
        await using var server = DebugServerProcess.Start(
            workspace.ScriptPath,
            ["--check-interval", "1024", "--stop-entry"]
        );
        await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        await HostRequestAsync(server, 1, "resume", new { mode = "continue" });
        await HostRequestAsync(server, 2, "pause");
        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("pause"));
        server.SendCommand("quit");
        await server.WaitForExitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Protocol_All_Exception_Filter_Stops_Before_Catch()
    {
        await using var workspace = new TempWorkspace(
            "try { throw new Error('expected'); } catch (error) { console.log('handled'); }\n"
        );
        await using var server = DebugServerProcess.Start(
            workspace.ScriptPath,
            ["--check-interval", "1024", "--stop-entry"]
        );
        await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        await HostRequestAsync(
            server,
            1,
            "setExceptionBreakpoints",
            new { filters = new[] { "all" } }
        );
        await HostRequestAsync(server, 2, "resume", new { mode = "continue" });
        var stopped = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(stopped, "kind"), Is.EqualTo("caught-exception"));
        await HostRequestAsync(
            server,
            3,
            "setExceptionBreakpoints",
            new { filters = Array.Empty<string>() }
        );
        await HostRequestAsync(server, 4, "resume", new { mode = "continue" });
        await server.WaitForJsonEventAsync("terminated", TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Protocol_Instruction_Step_Reloads_Interval_After_Periodic_Pause()
    {
        await using var workspace = new TempWorkspace("while (true) {}\n");
        await using var server = DebugServerProcess.Start(
            workspace.ScriptPath,
            ["--check-interval", "1024", "--stop-entry"]
        );
        await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        await HostRequestAsync(server, 1, "resume", new { mode = "continue" });
        await HostRequestAsync(server, 2, "pause");
        var before = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        await HostRequestAsync(
            server,
            3,
            "resume",
            new { mode = "step", granularity = "instruction" }
        );
        var after = await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        Assert.That(GetString(after, "kind"), Is.EqualTo("step"));
        // A one-opcode back edge can keep the same PC: the instruction counter
        // must distinguish iterations, and the old 1024 interval must not win.
        Assert.That(
            after.GetProperty("executedInstructions").GetUInt64(),
            Is.EqualTo(before.GetProperty("executedInstructions").GetUInt64() + 1)
        );
        server.SendCommand("quit");
        await server.WaitForExitAsync(TimeSpan.FromSeconds(10));
    }

    [TestCase("object.value = 2")]
    [TestCase("object.method()")]
    [TestCase("object[value]")]
    [TestCase("object.value + 1")]
    public async Task Protocol_Rejects_Expressions_That_Can_Execute_Code(string expression)
    {
        await using var workspace = new TempWorkspace("const object = { value: 1 };\ndebugger;\n");
        await using var server = DebugServerProcess.Start(
            workspace.ScriptPath,
            ["--check-interval", "1024"]
        );
        await server.WaitForJsonEventAsync("stopped", TimeSpan.FromSeconds(10));
        await HostRequestAsync(server, 1, "evaluate", new { expression }, success: false);
        var value = await HostRequestAsync(
            server,
            2,
            "evaluate",
            new { expression = "object.value" }
        );
        Assert.That(GetNestedString(value, "body", "result"), Is.EqualTo("Number(1)"));
        server.SendCommand("quit");
        await server.WaitForExitAsync(TimeSpan.FromSeconds(10));
    }
}
