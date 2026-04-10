using System.Runtime.CompilerServices;
using Okojo.Node;
using Okojo.Objects;
using Okojo;

var escapeOutput = args.Any(static arg => string.Equals(arg, "--escape-output", StringComparison.Ordinal));
var forwardedArgs = args
    .Where(static arg => !string.Equals(arg, "--escape-output", StringComparison.Ordinal))
    .ToArray();
var appRoot = ResolveAppRoot();
var entryPath = Path.Combine(appRoot, "main.mjs");

if (forwardedArgs.Length == 0)
{
    Console.WriteLine("OkojoCliAppSandbox");
    Console.WriteLine($"appRoot: {appRoot}");
    Console.WriteLine($"entry: {entryPath}");
    Console.WriteLine($"outputMode: {(escapeOutput ? "ai-escaped" : "human")}");
    Console.WriteLine();

    RunScenario("greet", ["greet", "--name", "okojo", "--times", "2"], verbose: true);
    RunScenario("inspect", ["inspect", "alpha", "beta", "--upper", "--repeat", "2"], verbose: true);
    RunScenario("report", ["report", "--config", "app.config.json"], verbose: true);
    RunScenario("explain", ["explain", "--doc", "project.explain.json", "--width", "68"], verbose: true);
}
else
{
    var exitCode = RunScenario(forwardedArgs[0], forwardedArgs, verbose: false);
    Environment.ExitCode = exitCode;
}

int RunScenario(string name, string[] argv, bool verbose)
{
    var stdout = new StringWriter();
    var stderr = new StringWriter();

    if (verbose)
        Console.WriteLine($"[{name}] argv: {string.Join(" ", argv)}");

    var previous = Environment.CurrentDirectory;
    Environment.CurrentDirectory = appRoot;
    Exception? failure = null;
    try
    {
        try
        {
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

            var result = runtime.RunMainModule(entryPath, argv);
            runtime.MainRealm.PumpJobs();

            if (verbose)
            {
                Console.WriteLine($"result: {result}");
                if (TryGetDefaultExport(result, out var defaultExport))
                    Console.WriteLine($"default: {defaultExport}");
            }
        }
        catch (Exception ex)
        {
            failure = ex;
            if (verbose)
            {
                Console.WriteLine($"exception: {ex.GetType().Name}");
                Console.WriteLine(ex);
                if (ex is Okojo.Runtime.JsRuntimeException jsEx)
                {
                    Console.WriteLine($"detailCode: {jsEx.DetailCode}");
                    if (jsEx.ThrownValue is { } thrownValue)
                        Console.WriteLine($"thrown: {thrownValue}");
                    var okojoStack = jsEx.FormatOkojoStackTrace();
                    if (!string.IsNullOrEmpty(okojoStack))
                    {
                        Console.WriteLine("okojoStack>");
                        Console.WriteLine(okojoStack);
                    }
                }
            }
        }
    }
    finally
    {
        Environment.CurrentDirectory = previous;
    }

    if (stdout.GetStringBuilder().Length != 0)
    {
        if (verbose)
            Console.WriteLine("stdout>");
        var text = stdout.ToString();
        Console.Write(escapeOutput ? EscapeControl(text) : text);
        if (!text.EndsWith('\n') && !escapeOutput)
            Console.WriteLine();
        if (escapeOutput)
            Console.WriteLine();
    }

    if (stderr.GetStringBuilder().Length != 0)
    {
        if (verbose)
            Console.WriteLine("stderr>");
        var text = stderr.ToString();
        Console.Write(escapeOutput ? EscapeControl(text) : text);
        if (!text.EndsWith('\n') && !escapeOutput)
            Console.WriteLine();
        if (escapeOutput)
            Console.WriteLine();
    }

    if (failure is not null)
    {
        if (!verbose)
        {
            Console.Error.WriteLine(failure);
            if (failure is Okojo.Runtime.JsRuntimeException jsEx)
            {
                var okojoStack = jsEx.FormatOkojoStackTrace();
                if (!string.IsNullOrEmpty(okojoStack))
                    Console.Error.WriteLine(okojoStack);
            }
        }

        if (verbose)
            Console.WriteLine();

        return 1;
    }

    if (verbose)
        Console.WriteLine();

    return 0;
}

static bool TryGetDefaultExport(JsValue moduleNamespace, out JsValue value)
{
    value = default;
    if (!moduleNamespace.TryGetObject(out var namespaceObject))
        return false;

    if (namespaceObject is not JsObject jsObject)
        return false;

    return jsObject.TryGetProperty("default", out value);
}

static string EscapeControl(string text)
{
    return text
        .Replace("\u001b", "\\u001b", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}

static string ResolveAppRoot([CallerFilePath] string callerFilePath = "")
{
    if (string.IsNullOrEmpty(callerFilePath))
        throw new InvalidOperationException("Caller file path is required.");

    return Path.Combine(Path.GetDirectoryName(callerFilePath)!, "app");
}
