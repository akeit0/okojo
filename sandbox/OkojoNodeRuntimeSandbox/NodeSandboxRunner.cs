using Okojo.Node;
using Okojo;

namespace OkojoNodeRuntimeSandbox;

internal sealed class NodeSandboxRunner(string baseDirectory)
{
    private readonly string fixturesRoot = Path.Combine(baseDirectory, "fixtures");

    public void RunAll()
    {
        Console.WriteLine("OkojoNodeRuntimeSandbox");
        Console.WriteLine();

        RunScenario(
            "CommonJS app",
            Path.Combine(fixturesRoot, "cjs-app"),
            "main.js");

        Console.WriteLine();

        RunScenario(
            "ESM app",
            Path.Combine(fixturesRoot, "esm-app"),
            "main.mjs");

        Console.WriteLine();

        RunScenarioWithPump(
            "nextTick + events app",
            Path.Combine(fixturesRoot, "tick-events-app"),
            "main.js",
            "status");

        Console.WriteLine();

        RunScenario(
            "stream app",
            Path.Combine(fixturesRoot, "stream-app"),
            "main.js");

        Console.WriteLine();

        RunScenario(
            "json require repro",
            Path.Combine(fixturesRoot, "json-require-repro"),
            "main.js");

        Console.WriteLine();

        RunScenario(
            "chalk getter repro",
            Path.Combine(fixturesRoot, "chalk-getter-repro"),
            "main.mjs");

        Console.WriteLine();

        RunScenarioWithCapturedTerminal(
            "stdout + tty app",
            Path.Combine(fixturesRoot, "tty-app"),
            "main.js");
    }

    private static IDisposable PushCurrentDirectory(string directory)
    {
        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = directory;
        return new RestoreCurrentDirectory(previous);
    }

    private void RunScenario(string name, string appRoot, string entryFile)
    {
        Console.WriteLine($"[{name}]");
        Console.WriteLine($"appRoot: {appRoot}");

        using var _ = PushCurrentDirectory(appRoot);
        using var runtime = OkojoNodeRuntime.CreateBuilder()
            .ConfigureTerminal(static options =>
            {
                options.StdoutIsTty = true;
                options.StderrIsTty = true;
                options.StdoutColumns = 120;
                options.StdoutRows = 40;
                options.StderrColumns = 120;
                options.StderrRows = 40;
            })
            .Build();

        var result = runtime.RunMainModule(Path.Combine(appRoot, entryFile));
        Console.WriteLine($"result: {FormatResult(result)}");
    }

    private void RunScenarioWithPump(string name, string appRoot, string entryFile, string globalKey)
    {
        Console.WriteLine($"[{name}]");
        Console.WriteLine($"appRoot: {appRoot}");

        using var _ = PushCurrentDirectory(appRoot);
        using var runtime = OkojoNodeRuntime.CreateBuilder()
            .ConfigureTerminal(static options =>
            {
                options.StdoutIsTty = true;
                options.StderrIsTty = true;
                options.StdoutColumns = 120;
                options.StdoutRows = 40;
                options.StderrColumns = 120;
                options.StderrRows = 40;
            })
            .Build();

        var result = runtime.RunMainModule(Path.Combine(appRoot, entryFile));
        runtime.MainRealm.PumpJobs();
        Console.WriteLine($"result: {FormatResult(result)}");
        Console.WriteLine($"after pump: {FormatResult(runtime.MainRealm.Global[globalKey])}");
    }

    private void RunScenarioWithCapturedTerminal(string name, string appRoot, string entryFile)
    {
        Console.WriteLine($"[{name}]");
        Console.WriteLine($"appRoot: {appRoot}");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        using var _ = PushCurrentDirectory(appRoot);
        using var runtime = OkojoNodeRuntime.CreateBuilder()
            .ConfigureTerminal(options =>
            {
                options.Stdout = stdout;
                options.Stderr = stderr;
                options.StdoutIsTty = true;
                options.StderrIsTty = true;
                options.StdoutColumns = 120;
                options.StdoutRows = 40;
                options.StderrColumns = 120;
                options.StderrRows = 40;
            })
            .Build();

        var result = runtime.RunMainModule(Path.Combine(appRoot, entryFile));
        Console.WriteLine($"result: {FormatResult(result)}");
        Console.WriteLine($"captured stdout: {EscapeControl(stdout.ToString())}");
        Console.WriteLine($"captured stderr: {EscapeControl(stderr.ToString())}");
    }

    private static string FormatResult(JsValue value)
    {
        if (value.TryGetObject(out var obj) &&
            obj.TryGetProperty("default", out var defaultValue) &&
            !defaultValue.IsUndefined)
        {
            return defaultValue.ToString();
        }

        return value.ToString();
    }

    private static string EscapeControl(string text)
    {
        return text
            .Replace("\u001b", "\\u001b", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private sealed class RestoreCurrentDirectory(string previous) : IDisposable
    {
        public void Dispose()
        {
            Environment.CurrentDirectory = previous;
        }
    }
}
