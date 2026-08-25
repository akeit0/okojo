using System.Diagnostics;
using System.Runtime.InteropServices;
using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: VmLoopProbe <case> [iterations] [warmup]");
    return 1;
}

var caseName = args[0];
var iterations =
    args.Length > 1 && int.TryParse(args[1], out var parsedIterations) ? parsedIterations : 200;
var warmup =
    args.Length > 2 && int.TryParse(args[2], out var parsedWarmup)
        ? parsedWarmup
        : Math.Max(400, iterations * 2);

var source = ResolveCaseSource(caseName);

Console.WriteLine($"[env] runtime={RuntimeInformation.FrameworkDescription}");
Console.WriteLine(
    $"[env] tieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "<default>"}"
);
Console.WriteLine(
    $"[env] tieredPGO={Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "<default>"}"
);
Console.WriteLine(
    $"[env] jitDisasm={(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_JitDisasm")) ? "<off>" : "on")}"
);
Console.WriteLine($"[env] case={caseName} iterations={iterations} warmup={warmup}");

using var runtime = JsRuntime.CreateBuilder().Build();
var realm = runtime.DefaultRealm;
var script = realm.CompileScript(source);

realm.Execute(script, pumpJobsAfterRun: false);

var function = realm.Accumulator.AsObject() as JsBytecodeFunction;
Console.WriteLine(function is null ? "[mode] script" : "[mode] function");

for (var i = 0; i < warmup; i++)
    RunOnce();

var samples = new double[iterations];
var stopwatch = new Stopwatch();
for (var i = 0; i < iterations; i++)
{
    stopwatch.Restart();
    RunOnce();
    stopwatch.Stop();
    samples[i] = stopwatch.Elapsed.TotalNanoseconds;
}

Array.Sort(samples);
var totalMs = samples.Sum() / 1_000_000.0;
var meanNs = samples.Average();
var minNs = samples[0];
var medianNs = samples[samples.Length / 2];
var maxNs = samples[^1];

Console.WriteLine(
    $"[result] case={caseName} mode={(function is null ? "script" : "function")} runs={iterations} mean_ns={meanNs:F1} median_ns={medianNs:F1} min_ns={minNs:F1} max_ns={maxNs:F1} total_ms={totalMs:F2}"
);
return 0;

void RunOnce()
{
    if (function is not null)
        realm.Execute(function, pumpJobsAfterRun: false);
    else
        realm.Execute(script, pumpJobsAfterRun: false);
}

static string ResolveCaseSource(string caseName)
{
    if (string.IsNullOrWhiteSpace(caseName))
        throw new ArgumentException("Case must be non-empty.", nameof(caseName));

    var fileName = caseName + ".js";
    var baseDir = AppContext.BaseDirectory;

    var probeCasesPath = Path.Combine(baseDir, "cases", fileName);
    if (File.Exists(probeCasesPath))
        return File.ReadAllText(probeCasesPath);

    var benchmarkScriptsPath = Path.GetFullPath(
        Path.Combine(
            baseDir,
            "..",
            "..",
            "..",
            "..",
            "..",
            "benchmarks",
            "Okojo.Benchmarks",
            "scripts",
            fileName
        )
    );
    if (File.Exists(benchmarkScriptsPath))
        return File.ReadAllText(benchmarkScriptsPath);

    throw new FileNotFoundException(
        $"VM loop probe case not found for '{caseName}'.",
        probeCasesPath
    );
}
