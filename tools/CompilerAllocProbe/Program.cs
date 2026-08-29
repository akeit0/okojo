using System.Diagnostics;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

var sourcePath = GetSourcePath();
var source = sourcePath is null
    ? """
        function makeCounters(n) {
          const counters = [];
          for (let i = 0; i < n; i++) {
            counters.push(function () { return i * 2; });
          }
          return counters;
        }
        const fns = makeCounters(12);
        let sum = 0;
        for (const fn of fns) sum += fn();
        function outer() {
          var x = 1;
          function mid() {
            function inner() { x += 5; return x; }
            return inner();
          }
          return mid() + x;
        }
        outer();
        sum;
        """
    : File.ReadAllText(Path.GetFullPath(sourcePath));

var warmupCount = GetIntOption("--warmup", 100);
var sampleCount = GetIntOption("--samples", 200);
var runtimeBatchSize = GetIntOption("--runtime-batch", 25);

if (args.Contains("--strict", StringComparer.OrdinalIgnoreCase))
    source = "\"use strict\";" + Environment.NewLine + source;

WarmCompiler(source, warmupCount, runtimeBatchSize);

// Phase attribution over many samples.
long parseBytes = 0;
long collectBytes = 0;
long planBytes = 0;
long parseTimestampTicks = 0;
long collectTimestampTicks = 0;
long planTimestampTicks = 0;

PrepareMeasurement();
for (var s = 0; s < sampleCount; s++)
{
    var astStart = GC.GetAllocatedBytesForCurrentThread();
    var timestampStart = Stopwatch.GetTimestamp();
    using var ast = JavaScriptParser.ParseScript(source);
    parseTimestampTicks += Stopwatch.GetTimestamp() - timestampStart;
    parseBytes += GC.GetAllocatedBytesForCurrentThread() - astStart;
}

PrepareMeasurement();
for (var s = 0; s < sampleCount; s++)
{
    using var ast = JavaScriptParser.ParseScript(source);
    var collectStart = GC.GetAllocatedBytesForCurrentThread();
    var timestampStart = Stopwatch.GetTimestamp();
    using var collected = CompilerBindingCollector.Collect(ast);
    collectTimestampTicks += Stopwatch.GetTimestamp() - timestampStart;
    collectBytes += GC.GetAllocatedBytesForCurrentThread() - collectStart;
}

PrepareMeasurement();
for (var s = 0; s < sampleCount; s++)
{
    using var ast = JavaScriptParser.ParseScript(source);
    using var collected = CompilerBindingCollector.Collect(ast);
    var planStart = GC.GetAllocatedBytesForCurrentThread();
    var timestampStart = Stopwatch.GetTimestamp();
    using var plan = CompilerStoragePlanner.Plan(collected, ast);
    planTimestampTicks += Stopwatch.GetTimestamp() - timestampStart;
    planBytes += GC.GetAllocatedBytesForCurrentThread() - planStart;
}

PrepareMeasurement();
var compile = MeasureCompile(source, sampleCount, runtimeBatchSize);

Console.WriteLine($"warmup={warmupCount} samples={sampleCount} runtime-batch={runtimeBatchSize}");
WritePhase("parse", parseBytes, parseTimestampTicks);
WritePhase("collect", collectBytes, collectTimestampTicks);
WritePhase("plan", planBytes, planTimestampTicks);
WritePhase("compile(full)", compile.AllocatedBytes, compile.TimestampTicks);
Console.WriteLine(
    $"compile-output: {compile.ScriptUnits / (double)sampleCount:F2} units/op "
        + $"{compile.PeakRetainedScriptUnits} max-retained-units"
);
Console.WriteLine(
    $"process-peak-working-set: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024d / 1024d:F1} MiB"
);

void WritePhase(string name, long allocatedBytes, long timestampTicks)
{
    var kilobytesPerOperation = allocatedBytes / (double)sampleCount / 1024;
    var microsecondsPerOperation = timestampTicks * 1_000_000d / Stopwatch.Frequency / sampleCount;
    Console.WriteLine(
        $"{name, -14}: {kilobytesPerOperation, 8:F2} KB/op {microsecondsPerOperation, 9:F2} us/op"
    );
}

int GetIntOption(string option, int fallback)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
            continue;
        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var value) && value > 0)
            return value;
        throw new ArgumentException($"{option} requires a positive integer.");
    }
    return fallback;
}

string? GetSourcePath()
{
    for (var i = 0; i < args.Length; i++)
    {
        if (
            string.Equals(args[i], "--warmup", StringComparison.OrdinalIgnoreCase)
            || string.Equals(args[i], "--samples", StringComparison.OrdinalIgnoreCase)
            || string.Equals(args[i], "--runtime-batch", StringComparison.OrdinalIgnoreCase)
        )
        {
            i++;
            continue;
        }
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            return args[i];
    }
    return null;
}

static void PrepareMeasurement()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

static void WarmCompiler(string source, int warmupCount, int runtimeBatchSize)
{
    var remaining = warmupCount;
    while (remaining > 0)
    {
        var count = Math.Min(remaining, runtimeBatchSize);
        using (var runtime = JsRuntime.CreateBuilder().Build())
        {
            var realm = runtime.DefaultRealm;
            for (var i = 0; i < count; i++)
            {
                using var ast = JavaScriptParser.ParseScript(source);
                _ = new JsScriptCompiler(realm).Compile(ast, null);
            }
        }

        PrepareMeasurement();
        remaining -= count;
    }
}

static CompileMeasurement MeasureCompile(string source, int sampleCount, int runtimeBatchSize)
{
    long allocatedBytes = 0;
    long timestampTicks = 0;
    long scriptUnits = 0;
    var peakRetainedScriptUnits = 0;
    var remaining = sampleCount;

    while (remaining > 0)
    {
        var count = Math.Min(remaining, runtimeBatchSize);
        var retainedScriptUnits = 0;
        using (var runtime = JsRuntime.CreateBuilder().Build())
        {
            var realm = runtime.DefaultRealm;

            // Seed the realm-owned compile pools without including cold pool rent in
            // the measured batch. Runtime creation and collection are likewise outside
            // the timed/allocation intervals.
            using (var seedAst = JavaScriptParser.ParseScript(source))
            {
                var seed = new JsScriptCompiler(realm).Compile(seedAst, null);
                retainedScriptUnits += CountScriptUnits(seed);
            }

            for (var i = 0; i < count; i++)
            {
                using var ast = JavaScriptParser.ParseScript(source);
                var allocationStart = GC.GetAllocatedBytesForCurrentThread();
                var timestampStart = Stopwatch.GetTimestamp();
                var script = new JsScriptCompiler(realm).Compile(ast, null);
                timestampTicks += Stopwatch.GetTimestamp() - timestampStart;
                allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocationStart;

                var units = CountScriptUnits(script);
                scriptUnits += units;
                retainedScriptUnits += units;
            }
        }

        peakRetainedScriptUnits = Math.Max(peakRetainedScriptUnits, retainedScriptUnits);
        PrepareMeasurement();
        remaining -= count;
    }

    return new(allocatedBytes, timestampTicks, scriptUnits, peakRetainedScriptUnits);
}

static int CountScriptUnits(JsScript script)
{
    var scripts = new HashSet<JsScript>(ReferenceEqualityComparer.Instance);
    AddScriptTree(script, scripts);
    return scripts.Count;
}

static void AddScriptTree(JsScript script, HashSet<JsScript> scripts)
{
    if (!scripts.Add(script))
        return;

    for (var i = 0; i < script.ObjectConstants.Length; i++)
        if (script.ObjectConstants[i] is JsBytecodeFunction function)
            AddScriptTree(function.Script, scripts);
}

readonly record struct CompileMeasurement(
    long AllocatedBytes,
    long TimestampTicks,
    long ScriptUnits,
    int PeakRetainedScriptUnits
);
