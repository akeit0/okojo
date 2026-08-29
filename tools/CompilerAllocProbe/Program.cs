using System.Diagnostics;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
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

if (args.Contains("--strict", StringComparer.OrdinalIgnoreCase))
    source = "\"use strict\";" + Environment.NewLine + source;

using var runtime = JsRuntime.CreateBuilder().Build();
var realm = runtime.DefaultRealm;

for (var i = 0; i < warmupCount; i++)
{
    using var ast = JavaScriptParser.ParseScript(source);
    _ = new JsScriptCompiler(realm).Compile(ast, null);
}

// Phase attribution over many samples.
long parseBytes = 0;
long collectBytes = 0;
long planBytes = 0;
long compileRestBytes = 0;
long parseTimestampTicks = 0;
long collectTimestampTicks = 0;
long planTimestampTicks = 0;
long compileRestTimestampTicks = 0;

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
for (var s = 0; s < sampleCount; s++)
{
    using var ast = JavaScriptParser.ParseScript(source);
    var restStart = GC.GetAllocatedBytesForCurrentThread();
    var timestampStart = Stopwatch.GetTimestamp();
    _ = new JsScriptCompiler(realm).Compile(ast, null);
    compileRestTimestampTicks += Stopwatch.GetTimestamp() - timestampStart;
    compileRestBytes += GC.GetAllocatedBytesForCurrentThread() - restStart;
}

Console.WriteLine($"warmup={warmupCount} samples={sampleCount}");
WritePhase("parse", parseBytes, parseTimestampTicks);
WritePhase("collect", collectBytes, collectTimestampTicks);
WritePhase("plan", planBytes, planTimestampTicks);
WritePhase("compile(full)", compileRestBytes, compileRestTimestampTicks);

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
