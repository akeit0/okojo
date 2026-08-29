using System.Diagnostics;
using System.Runtime.CompilerServices;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
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
        + $"{compile.PeakProducedScriptUnits} max-produced-units/batch "
        + $"{compile.PeakLiveRegisteredScriptUnitsAfterGc} max-live-registered-units-after-gc"
);
Console.WriteLine(
    $"compile-array-payload: {compile.OutputPayload.TotalBytes / (double)sampleCount / 1024:F2} KB/op "
        + $"(bytecode {compile.OutputPayload.BytecodeBytes / (double)sampleCount / 1024:F2}, "
        + $"constants {compile.OutputPayload.ConstantBytes / (double)sampleCount / 1024:F2}, "
        + $"feedback {compile.OutputPayload.FeedbackBytes / (double)sampleCount / 1024:F2}, "
        + $"debug {compile.OutputPayload.DebugBytes / (double)sampleCount / 1024:F2}, "
        + $"metadata {compile.OutputPayload.MetadataBytes / (double)sampleCount / 1024:F2})"
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
    var outputPayload = new ScriptOutputPayload();
    var peakProducedScriptUnits = 0;
    var peakLiveRegisteredScriptUnitsAfterGc = 0;
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
                retainedScriptUnits += CompileAndCountUnits(realm, seedAst);
            }

            for (var i = 0; i < count; i++)
            {
                using var ast = JavaScriptParser.ParseScript(source);
                var operation = MeasureCompileOperation(realm, ast);
                timestampTicks += operation.TimestampTicks;
                allocatedBytes += operation.AllocatedBytes;
                scriptUnits += operation.OutputPayload.ScriptUnits;
                outputPayload += operation.OutputPayload;
                retainedScriptUnits += operation.OutputPayload.ScriptUnits;
            }

            PrepareMeasurement();
            peakLiveRegisteredScriptUnitsAfterGc = Math.Max(
                peakLiveRegisteredScriptUnitsAfterGc,
                realm.Agent.ScriptDebugRegistry.GetAllRegisteredScripts().Count
            );
        }

        peakProducedScriptUnits = Math.Max(peakProducedScriptUnits, retainedScriptUnits);
        PrepareMeasurement();
        remaining -= count;
    }

    return new(
        allocatedBytes,
        timestampTicks,
        scriptUnits,
        outputPayload,
        peakProducedScriptUnits,
        peakLiveRegisteredScriptUnitsAfterGc
    );
}

[MethodImpl(MethodImplOptions.NoInlining)]
static int CompileAndCountUnits(Okojo.JavaScript.Execution.JsRealm realm, JsAst ast)
{
    var script = new JsScriptCompiler(realm).Compile(ast, null);
    return MeasureScriptOutputPayload(script).ScriptUnits;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static CompileOperationMeasurement MeasureCompileOperation(
    Okojo.JavaScript.Execution.JsRealm realm,
    JsAst ast
)
{
    var allocationStart = GC.GetAllocatedBytesForCurrentThread();
    var timestampStart = Stopwatch.GetTimestamp();
    var script = new JsScriptCompiler(realm).Compile(ast, null);
    var timestampTicks = Stopwatch.GetTimestamp() - timestampStart;
    var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
    return new(allocatedBytes, timestampTicks, MeasureScriptOutputPayload(script));
}

static ScriptOutputPayload MeasureScriptOutputPayload(JsScript script)
{
    var scripts = new HashSet<JsScript>(ReferenceEqualityComparer.Instance);
    var payload = new ScriptOutputPayload();
    AddScriptTree(script, scripts, ref payload);
    return payload;
}

static void AddScriptTree(
    JsScript script,
    HashSet<JsScript> scripts,
    ref ScriptOutputPayload payload
)
{
    if (!scripts.Add(script))
        return;

    payload.ScriptUnits++;
    payload.BytecodeBytes += script.Bytecode.Length;
    payload.ConstantBytes +=
        script.NumericConstants.Length * sizeof(ulong)
        + script.ObjectConstants.Length * IntPtr.Size
        + script.AtomizedStringConstants.Length * sizeof(int);
    payload.FeedbackBytes +=
        (script.NamedPropertyIcEntries?.Length ?? 0) * Unsafe.SizeOf<OkojoNamedPropertyIcEntry>()
        + (script.PrototypeNamedPropertyIcEntries?.Length ?? 0)
            * Unsafe.SizeOf<OkojoPrototypeNamedPropertyIcEntry>()
        + (script.GlobalBindingIcEntries?.Length ?? 0) * Unsafe.SizeOf<GlobalBindingIcEntry>();
    payload.DebugBytes +=
        (script.DebugNames?.Length ?? 0) * IntPtr.Size
        + (
            (script.CallSiteDebugPcs?.Length ?? 0)
            + (script.CallSiteDebugNameIndices?.Length ?? 0)
            + (script.RuntimeCallDebugPcs?.Length ?? 0)
            + (script.RuntimeCallDebugNameIndices?.Length ?? 0)
            + (script.TdzReadDebugPcs?.Length ?? 0)
            + (script.TdzReadDebugNameIndices?.Length ?? 0)
            + (script.DebugPcOffsets?.Length ?? 0)
            + (script.DebugSourceOffsets?.Length ?? 0)
            + (script.PrivateFieldDebugNameIndices?.Length ?? 0)
        ) * sizeof(int)
        + (script.PrivateFieldDebugKeys?.Length ?? 0) * sizeof(long)
        + (script.LocalDebugInfos?.Length ?? 0) * Unsafe.SizeOf<JsLocalDebugInfo>();
    payload.MetadataBytes +=
        (
            (script.GeneratorSwitchTargets?.Length ?? 0)
            + (script.SwitchOnSmiTargets?.Length ?? 0)
            + (script.TopLevelLexicalAtoms?.Length ?? 0)
            + (script.TopLevelLexicalSlots?.Length ?? 0)
        ) * sizeof(int)
        + (script.TopLevelLexicalConstFlags?.Length ?? 0) * sizeof(bool);

    for (var i = 0; i < script.ObjectConstants.Length; i++)
        if (script.ObjectConstants[i] is JsBytecodeFunction function)
            AddScriptTree(function.Script, scripts, ref payload);
}

readonly record struct CompileMeasurement(
    long AllocatedBytes,
    long TimestampTicks,
    long ScriptUnits,
    ScriptOutputPayload OutputPayload,
    int PeakProducedScriptUnits,
    int PeakLiveRegisteredScriptUnitsAfterGc
);

readonly record struct CompileOperationMeasurement(
    long AllocatedBytes,
    long TimestampTicks,
    ScriptOutputPayload OutputPayload
);

record struct ScriptOutputPayload(
    int ScriptUnits = 0,
    long BytecodeBytes = 0,
    long ConstantBytes = 0,
    long FeedbackBytes = 0,
    long DebugBytes = 0,
    long MetadataBytes = 0
)
{
    public readonly long TotalBytes =>
        BytecodeBytes + ConstantBytes + FeedbackBytes + DebugBytes + MetadataBytes;

    public static ScriptOutputPayload operator +(
        ScriptOutputPayload left,
        ScriptOutputPayload right
    )
    {
        left.ScriptUnits += right.ScriptUnits;
        left.BytecodeBytes += right.BytecodeBytes;
        left.ConstantBytes += right.ConstantBytes;
        left.FeedbackBytes += right.FeedbackBytes;
        left.DebugBytes += right.DebugBytes;
        left.MetadataBytes += right.MetadataBytes;
        return left;
    }
}
