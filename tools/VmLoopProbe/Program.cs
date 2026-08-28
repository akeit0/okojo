using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

if (args.Length == 1 && string.Equals(args[0], "--inspect-run", StringComparison.OrdinalIgnoreCase))
    return InspectRun();

if (args.Length < 1)
{
    Console.Error.WriteLine(
        "usage: VmLoopProbe <case> [iterations] [warmup] [--hold] [--strict] [--profile-opcodes] [--phase <name>]"
    );
    Console.Error.WriteLine("       VmLoopProbe --inspect-run");
    return 1;
}

var caseName = args[0];
var numericArgs = args.Skip(1)
    .Where(static arg => !arg.StartsWith("--", StringComparison.Ordinal))
    .ToArray();
var iterations =
    numericArgs.Length > 0 && int.TryParse(numericArgs[0], out var parsedIterations)
        ? parsedIterations
        : 200;
var warmup =
    numericArgs.Length > 1 && int.TryParse(numericArgs[1], out var parsedWarmup)
        ? parsedWarmup
        : Math.Max(400, iterations * 2);
var phase = GetOption(args, "--phase");
var strict = args.Contains("--strict", StringComparer.OrdinalIgnoreCase);
var hold = args.Contains("--hold", StringComparer.OrdinalIgnoreCase);
var profileOpcodes = args.Contains("--profile-opcodes", StringComparer.OrdinalIgnoreCase);

var source = ResolveCaseSource(caseName);
if (strict)
    source = "\"use strict\";" + Environment.NewLine + source;

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
Console.WriteLine(
    $"[env] case={caseName} strict={strict} iterations={iterations} warmup={warmup} phase={phase ?? "execute"} profileOpcodes={profileOpcodes}"
);

if (phase is not null)
{
    if (profileOpcodes)
    {
        Console.Error.WriteLine(
            "--profile-opcodes is supported only for the normal execute probe."
        );
        return 1;
    }

    return RunPhaseProbe(source, phase, iterations, warmup);
}

using var runtime = JsRuntime.CreateBuilder().Build();
var realm = runtime.DefaultRealm;
var script = realm.CompileScript(source);

realm.Execute(script, pumpJobsAfterRun: false);

var function = realm.Accumulator.AsObject() as JsBytecodeFunction;
Console.WriteLine(function is null ? "[mode] script" : "[mode] function");

for (var i = 0; i < warmup; i++)
    RunOnce();

if (hold)
{
    Console.WriteLine($"[hold] pid={Environment.ProcessId} warmed=true");
    Console.Out.Flush();
    Console.ReadLine();
    if (profileOpcodes && !WriteOpcodeProfile())
        return 2;
    return 0;
}

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
if (profileOpcodes && !WriteOpcodeProfile())
    return 2;
return 0;

void RunOnce()
{
    if (function is not null)
        realm.Execute(function, pumpJobsAfterRun: false);
    else
        realm.Execute(script, pumpJobsAfterRun: false);
}

static bool WriteOpcodeProfile()
{
    var report = JsRealm.GetVmOpcodeProfileReport();
    if (report is null)
    {
        Console.Error.WriteLine(
            "[profile] unavailable; rebuild with -p:OkojoVmProfile=true before using --profile-opcodes."
        );
        return false;
    }

    Console.Write(report);
    return true;
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

static string? GetOption(string[] arguments, string name)
{
    for (var i = 0; i < arguments.Length; i++)
        if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase))
            return i + 1 < arguments.Length && !arguments[i + 1].StartsWith("--")
                ? arguments[i + 1]
                : throw new ArgumentException($"Missing value for {name}.");

    return null;
}

static int InspectRun()
{
    var method = typeof(JsRealm).GetMethod("Run", BindingFlags.Instance | BindingFlags.NonPublic);
    if (method is null)
    {
        Console.Error.WriteLine("Run method was not found.");
        return 1;
    }

    var body = method.GetMethodBody();
    if (body is null)
    {
        Console.Error.WriteLine("Run method has no method body.");
        return 1;
    }

    var locals = body.LocalVariables;
    Console.WriteLine(
        $"[run] method={method.DeclaringType!.FullName}.{method.Name} il_bytes={body.GetILAsByteArray()?.Length ?? 0} max_stack={body.MaxStackSize} init_locals={body.InitLocals} locals={locals.Count}"
    );
    foreach (
        var group in locals.GroupBy(static local =>
            local.LocalType.FullName ?? local.LocalType.Name
        )
    )
        Console.WriteLine($"[run-type] type={group.Key} count={group.Count()}");
    foreach (var local in locals)
        Console.WriteLine($"[run-local] index={local.LocalIndex} type={local.LocalType}");
    PrintRunSourceLocals(method, locals);

    return 0;
}

static void PrintRunSourceLocals(MethodInfo method, IList<LocalVariableInfo> locals)
{
    var assemblyPath = method.Module.Assembly.Location;
    var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
    if (!File.Exists(pdbPath))
    {
        Console.WriteLine("[run-source] pdb=missing");
        return;
    }

    using var pdbStream = File.OpenRead(pdbPath);
    using var pdbProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
    var pdbReader = pdbProvider.GetMetadataReader();
    var methodHandle = MetadataTokens.MethodDefinitionHandle(method.MetadataToken);
    foreach (var scopeHandle in pdbReader.GetLocalScopes(methodHandle))
    {
        var scope = pdbReader.GetLocalScope(scopeHandle);
        foreach (var localHandle in scope.GetLocalVariables())
        {
            var local = pdbReader.GetLocalVariable(localHandle);
            var name = pdbReader.GetString(local.Name);
            var type = local.Index < locals.Count ? locals[local.Index].LocalType : null;
            Console.WriteLine(
                $"[run-source-local] scope={scope.StartOffset}:{scope.Length} index={local.Index} type={type} name={name}"
            );
        }
    }
}

static int RunPhaseProbe(string source, string phase, int iterations, int warmup)
{
    if (iterations <= 0 || warmup < 0)
        throw new ArgumentOutOfRangeException(
            nameof(iterations),
            "Iterations must be positive and warmup must not be negative."
        );

    if (phase is not ("parse" or "compile" or "parse-compile" or "execute" or "all"))
        throw new ArgumentException(
            "Phase must be parse, compile, parse-compile, execute, or all.",
            nameof(phase)
        );

    using var runtime = JsRuntime.CreateBuilder().Build();
    var realm = runtime.DefaultRealm;

    if (phase is "parse" or "all")
        Measure(
            "parse",
            () =>
            {
                using var ast = JavaScriptParser.ParseScript(source);
                GC.KeepAlive(ast);
            },
            iterations,
            warmup
        );

    if (phase is "compile" or "all")
    {
        using var ast = JavaScriptParser.ParseScript(source);
        Measure(
            "compile",
            () => GC.KeepAlive(new JsScriptCompiler(realm).Compile(ast, null)),
            iterations,
            warmup
        );
    }

    if (phase is "parse-compile" or "all")
        Measure(
            "parse+compile",
            () =>
            {
                using var ast = JavaScriptParser.ParseScript(source);
                GC.KeepAlive(new JsScriptCompiler(realm).Compile(ast, null));
            },
            iterations,
            warmup
        );

    if (phase is "execute" or "all")
    {
        var script = realm.CompileScript(source);
        Measure(
            "execute",
            () => realm.Execute(script, pumpJobsAfterRun: false),
            iterations,
            warmup
        );
    }

    return 0;

    static void Measure(string name, Action action, int iterations, int warmup)
    {
        for (var i = 0; i < warmup; i++)
            action();

        var times = new double[iterations];
        var allocations = new long[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            action();
            times[i] = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
            allocations[i] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }

        Array.Sort(times);
        Array.Sort(allocations);
        Console.WriteLine(
            $"[result] phase={name} runs={iterations} mean_ns={times.Average():F1} "
                + $"median_ns={times[iterations / 2]:F1} min_ns={times[0]:F1} max_ns={times[^1]:F1} "
                + $"mean_alloc_bytes={allocations.Average():F1} "
                + $"median_alloc_bytes={allocations[iterations / 2]}"
        );
    }
}
