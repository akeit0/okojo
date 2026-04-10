namespace Okojo.Node.Tests;

public class NodeCliIntegrationTests
{
    [Test]
    public async Task OkojoNode_PrintFlag_WithEval_Prints_Result()
    {
        await using var process = NodeCliProcess.Start("-p", "-e", "Math.PI");

        await process.WaitForExitAsync(TimeSpan.FromSeconds(10));

        Assert.That(process.GetStdout().Trim(), Is.EqualTo("3.141592653589793"));
        Assert.That(process.GetStderr(), Is.Empty);
    }

    [Test]
    public async Task OkojoNode_EnvFile_Loads_Values_Into_ProcessEnv()
    {
        var envFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(envFile, "OKOJO_NODE_CLI_TEST=from-env-file");

            await using var process =
                NodeCliProcess.Start("--env-file", envFile, "-p", "-e", "process.env.OKOJO_NODE_CLI_TEST");

            await process.WaitForExitAsync(TimeSpan.FromSeconds(10));

            Assert.That(process.GetStdout().Trim(), Is.EqualTo("'from-env-file'"));
            Assert.That(process.GetStderr(), Is.Empty);
        }
        finally
        {
            File.Delete(envFile);
        }
    }

    [Test]
    public async Task OkojoNode_PrintBytecode_ForEval_Emits_Disassembly_And_Result()
    {
        await using var process = NodeCliProcess.Start("--print-bytecode", "-p", "-e", "1 + 2");

        await process.WaitForExitAsync(TimeSpan.FromSeconds(10));

        var stdout = process.GetStdout();
        Assert.That(stdout, Does.Contain("; okojo-disasm v1"));
        Assert.That(stdout.TrimEnd(), Does.EndWith("3"));
        Assert.That(process.GetStderr(), Is.Empty);
    }

    [Test]
    public async Task OkojoNode_Repl_Pumps_Timers_While_Waiting_For_Input()
    {
        await using var process = NodeCliProcess.Start();

        process.SendCommand("var a = 3;");
        process.SendCommand("setTimeout(() => { console.log(\"3seconds\"); a = 2000; }, 50);");

        var timerId = await process.WaitForStdoutLineAsync(
            static line => line.Trim() == "1",
            TimeSpan.FromSeconds(10));
        Assert.That(timerId.Trim(), Is.EqualTo("1"));

        var callbackLine = await process.WaitForStdoutLineAsync(
            static line => line.Contains("3seconds", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
        Assert.That(callbackLine, Does.Contain("3seconds"));

        process.SendCommand("a");
        var valueLine = await process.WaitForStdoutLineAsync(
            static line => line.Trim() == "2000",
            TimeSpan.FromSeconds(10));
        Assert.That(valueLine.Trim(), Is.EqualTo("2000"));
        Assert.That(process.GetStderr(), Is.Empty);
    }

    [Test]
    public async Task OkojoNode_Repl_Does_Not_Block_On_Promise_Result_With_Timers()
    {
        await using var process = NodeCliProcess.Start();

        process.SendCommand("const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));");
        process.SendCommand("async function init() {");
        process.SendCommand("  console.log(\"now\");");
        process.SendCommand("  await delay(25);");
        process.SendCommand("  console.log(\"after 1 tick\");");
        process.SendCommand("  await delay(25);");
        process.SendCommand("  console.log(\"after 2 ticks\");");
        process.SendCommand("}");

        process.SendCommand("init();");
        process.SendCommand("40 + 2");

        var promptStayedResponsive = await process.WaitForStdoutLineAsync(
            static line => line.Trim() == "42",
            TimeSpan.FromSeconds(5));
        Assert.That(promptStayedResponsive.Trim(), Is.EqualTo("42"));

        var firstLog = await process.WaitForStdoutLineAsync(
            static line => line.Contains("after 1 tick", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.That(firstLog, Does.Contain("after 1 tick"));

        var secondLog = await process.WaitForStdoutLineAsync(
            static line => line.Contains("after 2 ticks", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.That(secondLog, Does.Contain("after 2 ticks"));

        Assert.That(process.GetStdout(), Does.Contain("now"));
        Assert.That(process.GetStderr(), Is.Empty);
    }

    [Test]
    public async Task OkojoNode_Repl_Supports_TopLevelAwait_Expressions()
    {
        await using var process = NodeCliProcess.Start();

        process.SendCommand("await 2");
        var awaitedValue = await process.WaitForStdoutLineAsync(
            static line => line.Trim() == "2",
            TimeSpan.FromSeconds(5));
        Assert.That(awaitedValue.Trim(), Is.EqualTo("2"));

        process.SendCommand(
            "for (let i = 0; i < 3; i++) { await new Promise(resolve => setTimeout(resolve, 25)); console.log(i); }");
        var zero = await process.WaitForStdoutLineAsync(
            static line => line.Trim() == "0",
            TimeSpan.FromSeconds(5));
        var one = await process.WaitForStdoutLineAsync(
            static line => line.Trim() == "1",
            TimeSpan.FromSeconds(5));
        var two = await process.WaitForStdoutLineAsync(
            static line => line.Trim() == "2",
            TimeSpan.FromSeconds(5));
        var result = await process.WaitForStdoutLineAsync(
            static line => line.Trim() == "undefined",
            TimeSpan.FromSeconds(5));

        Assert.That(zero.Trim(), Is.EqualTo("0"));
        Assert.That(one.Trim(), Is.EqualTo("1"));
        Assert.That(two.Trim(), Is.EqualTo("2"));
        Assert.That(result.Trim(), Is.EqualTo("undefined"));
        Assert.That(process.GetStderr(), Is.Empty);
    }

    [Test]
    public async Task OkojoNode_Eval_Supports_NodeTimersPromises_SetTimeout()
    {
        await using var process = NodeCliProcess.Start(
            "-p",
            "-e",
            "await (await import('node:timers/promises')).setTimeout(25, 'ok')");

        await process.WaitForExitAsync(TimeSpan.FromSeconds(10));

        Assert.That(process.GetStdout().Trim(), Is.EqualTo("'ok'"));
        Assert.That(process.GetStderr(), Is.Empty);
    }

    [Test]
    public async Task OkojoNode_Repl_NodeTimersPromises_SetTimeout_Does_Not_Recurse()
    {
        await using var process = NodeCliProcess.Start();

        process.SendCommand("const { setTimeout } = await import('node:timers/promises');");
        process.SendCommand("for (let i = 0; i < 3; i++) { await setTimeout(25); console.log(i); }");

        var zero = await process.WaitForStdoutLineAsync(
            static line => line.Trim() == "0",
            TimeSpan.FromSeconds(5));
        var one = await process.WaitForStdoutLineAsync(
            static line => line.Trim() == "1",
            TimeSpan.FromSeconds(5));
        var two = await process.WaitForStdoutLineAsync(
            static line => line.Trim() == "2",
            TimeSpan.FromSeconds(5));
        var result = await process.WaitForStdoutLineAsync(
            static line => line.Trim() == "undefined",
            TimeSpan.FromSeconds(5));

        Assert.That(zero.Trim(), Is.EqualTo("0"));
        Assert.That(one.Trim(), Is.EqualTo("1"));
        Assert.That(two.Trim(), Is.EqualTo("2"));
        Assert.That(result.Trim(), Is.EqualTo("undefined"));
        Assert.That(process.GetStderr(), Is.Empty);
    }

    [Test]
    public async Task OkojoNode_Eval_Supports_NodeRepl_Start_And_Context()
    {
        var directory = Path.Combine(Path.GetTempPath(), "okojo-node-repl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "main.js");
        try
        {
            await File.WriteAllTextAsync(scriptPath,
                """
                const repl = require('node:repl');
                const r = repl.start('repl> ');
                r.context.m = 'message';
                console.log('ready');
                """);

            await using var process = NodeCliProcess.Start(scriptPath);

            var ready = await process.WaitForStdoutLineAsync(
                static line => line.Contains("ready", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            Assert.That(ready, Does.Contain("ready"));

            process.SendCommand("m");
            var value = await process.WaitForStdoutLineAsync(
                static line => line.Trim() == "'message'",
                TimeSpan.FromSeconds(10));
            Assert.That(value.Trim(), Is.EqualTo("'message'"));

            process.SendCommand(".exit");
            await process.WaitForExitAsync(TimeSpan.FromSeconds(10));
            Assert.That(process.GetStderr(), Is.Empty);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task OkojoNode_Repl_Formats_Uncaught_Errors_Like_Node()
    {
        await using var process = NodeCliProcess.Start();

        process.SendCommand("throw a;");
        process.SendCommand("throw 1;");
        process.SendCommand("function a(v){");
        process.SendCommand("var c =vvvv;");
        process.SendCommand("}");
        process.SendCommand("a(3)");
        process.SendCommand(".exit");

        await process.WaitForExitAsync(TimeSpan.FromSeconds(10));

        var stdout = process.GetStdout();
        var stderr = process.GetStderr();

        Assert.That(stdout, Does.Contain("undefined"));
        Assert.That(stderr, Does.Contain("Uncaught ReferenceError: a is not defined"));
        Assert.That(stderr, Does.Contain("Uncaught 1"));
        Assert.That(stderr, Does.Contain("Uncaught ReferenceError: vvvv is not defined"));
        Assert.That(stderr, Does.Contain("at a (REPL3:2:8)"));
        Assert.That(stderr, Does.Not.Contain("[repl].js"));
        Assert.That(stderr, Does.Not.Contain("InternalError: Throw"));
    }

    [Test]
    public async Task OkojoNode_InspectBrk_Stops_On_Entry_And_Continues()
    {
        await using var process = NodeCliProcess.Start("--inspect-brk", "-p", "-e", "40 + 2");

        var stopped = await process.WaitForStdoutLineAsync(
            static line => line.Contains("Break on start in [eval-1].js:1", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
        Assert.That(stopped, Does.Contain("Break on start in [eval-1].js:1"));

        process.SendCommand("continue");

        var result = await process.WaitForStdoutLineAsync(
            static line => line.TrimEnd().EndsWith("42", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
        Assert.That(result.TrimEnd(), Does.EndWith("42"));

        await process.WaitForExitAsync(TimeSpan.FromSeconds(10));
        Assert.That(process.GetStderr(), Is.Empty);
    }

    [Test]
    public async Task OkojoNode_Inspect_Command_Supports_SetBreakpoint_List_And_Continue()
    {
        var directory = Path.Combine(Path.GetTempPath(), "okojo-node-cli-inspect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "index.js");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "package.json"), """{ "type": "module" }""");
            await File.WriteAllTextAsync(scriptPath, """
                                                     const line1 = 1;
                                                     const line2 = 2;
                                                     const line3 = 3;
                                                     const line4 = 4;
                                                     const line5 = 5;
                                                     const line6 = 6;
                                                     const line7 = 7;
                                                     const line8 = 8;
                                                     const line9 = 9;
                                                     globalThis.answer = 42;
                                                     console.log(globalThis.answer);
                                                     """);

            await using var process = NodeCliProcess.Start("inspect", scriptPath);

            var entryStop = await process.WaitForStdoutLineAsync(
                static line => line.Contains("Break on start in index.js:1", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            Assert.That(entryStop, Does.Contain("Break on start in index.js:1"));

            var entryListing = await process.WaitForStdoutLineAsync(
                static line => line.Contains(">    1 const line1 = 1;", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            Assert.That(entryListing, Does.Contain("const line1 = 1;"));

            process.SendCommand("sb('index.js',10)");

            process.SendCommand("breakpoints");
            var breakpointList = await process.WaitForStdoutLineAsync(
                static line => line.Contains("#0 index.js:10", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            Assert.That(breakpointList, Does.Contain("#0 index.js:10"));

            process.SendCommand("list(5)");
            var listedLine = await process.WaitForStdoutLineAsync(
                static line => line.Contains(">    1 const line1 = 1;", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            Assert.That(listedLine, Does.Contain("const line1 = 1;"));

            process.SendCommand("c");
            await process.WaitForExitAsync(TimeSpan.FromSeconds(10));
            Assert.That(process.GetStdout(), Does.Contain("42"));
            Assert.That(process.GetStderr(), Is.Empty);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task OkojoNode_EnableSourceMaps_Remap_StackTrace_To_Original_Source()
    {
        var fixture = await CreateSourceMapFixtureAsync();
        try
        {
            await using var withoutMaps = NodeCliProcess.Start(fixture.ScriptPath);
            await withoutMaps.WaitForExitAsync(TimeSpan.FromSeconds(10));
            Assert.That(withoutMaps.GetStderr(), Does.Contain("index.js:"));
            Assert.That(withoutMaps.GetStderr(), Does.Not.Contain("app.ts"));

            await using var withMaps = NodeCliProcess.Start("--enable-source-maps", fixture.ScriptPath);
            await withMaps.WaitForExitAsync(TimeSpan.FromSeconds(10));
            Assert.That(withMaps.GetStderr(), Does.Contain("app.ts:"));
            Assert.That(withMaps.GetStderr(), Does.Not.Contain("index.js:"));
        }
        finally
        {
            DeleteFixture(fixture.Directory);
        }
    }

    [Test]
    public async Task OkojoNode_Inspect_WithSourceMaps_Uses_Original_Source_Locations()
    {
        var fixture = await CreateSourceMapFixtureAsync();
        try
        {
            await using var process = NodeCliProcess.Start("inspect", "--enable-source-maps", fixture.ScriptPath);

            var entryStop = await process.WaitForStdoutLineAsync(
                static line => line.Contains("Break on start in app.ts:3", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            Assert.That(entryStop, Does.Contain("Break on start in app.ts:3"));

            var entryListing = await process.WaitForStdoutLineAsync(
                static line => line.Contains(">    3 const title: string = \"probe\";", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            Assert.That(entryListing, Does.Contain("const title: string = \"probe\";"));

            process.SendCommand("sb('app.ts',7)");
            process.SendCommand("breakpoints");
            var breakpointList = await process.WaitForStdoutLineAsync(
                static line => line.Contains("#0 app.ts:7", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            Assert.That(breakpointList, Does.Contain("#0 app.ts:7"));

            process.SendCommand("list(5)");
            var listedLine = await process.WaitForStdoutLineAsync(
                static line => line.Contains("     5   throw new Error(\"boom\");", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            Assert.That(listedLine, Does.Contain("throw new Error(\"boom\");"));
            Assert.That(process.GetStderr(), Is.Empty);
        }
        finally
        {
            DeleteFixture(fixture.Directory);
        }
    }

    private static async Task<SourceMapFixture> CreateSourceMapFixtureAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "okojo-node-cli-sourcemaps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var sourcePath = Path.Combine(directory, "app.ts");
        var scriptPath = Path.Combine(directory, "index.js");
        var mapPath = Path.Combine(directory, "index.js.map");

        await File.WriteAllTextAsync(sourcePath, """
                                                 const pad1 = 1;
                                                 const pad2 = 2;
                                                 const title: string = "probe";
                                                 function boom(): never {
                                                   throw new Error("boom");
                                                 }
                                                 boom();
                                                 """);

        await File.WriteAllTextAsync(scriptPath, """
                                                 const title = "probe";
                                                 function boom() {
                                                   throw new Error("boom");
                                                 }
                                                 boom();
                                                 //# sourceMappingURL=index.js.map
                                                 """);

        await File.WriteAllTextAsync(mapPath, """
                                              {
                                                "version": 3,
                                                "file": "index.js",
                                                "sources": ["app.ts"],
                                                "mappings": "AAEA;AACA;AACA;AACA;AACA"
                                              }
                                              """);

        return new(directory, scriptPath);
    }

    private static void DeleteFixture(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private sealed record SourceMapFixture(string Directory, string ScriptPath);
}
