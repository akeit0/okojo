using Acornima.Ast;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Jint;
using Okojo.Benchmarks;
using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

/// <summary>
///     Port of Jint.Benchmark/EngineComparisonBenchmark.cs (jint commit
///     34525701f1b4) restricted to the two engines under investigation and
///     adapted to this repository's embedding API. Separates the two useful
///     lanes: parse+compile creates a fresh prepared artifact per operation,
///     while execution reuses one prepared artifact per engine.
///
///     Their published numbers pinned Okojo as an older NuGet package; this
///     run measures the current vm-opt tree against latest Jint (4.x).
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
[HideColumns("Error", "Gen0", "Gen1", "Gen2")]
[Config(typeof(ShortRunConfig))]
public class JintSuiteComparisonBenchmarks
{
    private const string DromaeoHelpers = """
        var startTest = function () { };
        var test = function (name, fn) { fn(); };
        var endTest = function () { };
        var prep = function (fn) { fn(); };
        """;

    private static readonly string[] ScriptKeys =
    [
        "array-stress",
        "evaluation-modern",
        "json-parse-modern",
        "linq-js",
        "minimal",
        "stopwatch-modern",
        "dromaeo-3d-cube-modern",
        "dromaeo-core-eval-modern",
        "dromaeo-object-array-modern",
        "dromaeo-object-regexp-modern",
        "dromaeo-object-string-modern",
        "dromaeo-string-base64-modern",
    ];

    private Engine jintEngine = null!;
    private Prepared<Script> jintPreparedScript;
    private JsRuntime okojoRuntime = null!;
    private JsRealm okojoRealm = null!;
    private JsScript okojoScript = null!;
    private string source = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        source = LoadStrictSource(FileName);

        // Warm host plumbing before setting up the prepared execution lanes.
        using var warmRuntime = JsRuntime.CreateBuilder().Build();
        _ = JsCompiler.Compile(warmRuntime.DefaultRealm, source);
        _ = Engine.PrepareScript(source);

        okojoRuntime = JsRuntime.CreateBuilder().Build();
        okojoRealm = okojoRuntime.DefaultRealm;
        okojoScript = JsCompiler.Compile(okojoRealm, source);
        okojoRealm.Execute(okojoScript, pumpJobsAfterRun: false);

        jintEngine = new Engine(static options => options.Strict());
        jintPreparedScript = Engine.PrepareScript(source);
        jintEngine.Execute(jintPreparedScript);
    }

    [GlobalCleanup]
    public void Cleanup() => okojoRuntime.Dispose();

    public IEnumerable<string> FileNames() => ScriptKeys;

    [ParamsSource(nameof(FileNames))]
    public string FileName { get; set; } = "minimal";

    [Benchmark]
    [BenchmarkCategory("ParseCompile", "Jint")]
    public void Jint_ParseCompile()
    {
        GC.KeepAlive(Engine.PrepareScript(source));
    }

    [Benchmark]
    [BenchmarkCategory("ParseCompile", "Okojo")]
    public void Okojo_ParseCompile()
    {
        GC.KeepAlive(JsCompiler.Compile(okojoRealm, source));
    }

    [Benchmark]
    [BenchmarkCategory("Execution", "Jint")]
    public void Jint_Execute()
    {
        jintEngine.Execute(jintPreparedScript);
    }

    [Benchmark]
    [BenchmarkCategory("Execution", "Okojo")]
    public void Okojo_Execute()
    {
        okojoRealm.Execute(okojoScript, pumpJobsAfterRun: false);
    }

    private static string LoadStrictSource(string key)
    {
        var script = ScriptSourceLoader.LoadScenario(key);
        if (key.Contains("dromaeo"))
            script = DromaeoHelpers + Environment.NewLine + script;

        return "\"use strict\";" + Environment.NewLine + script;
    }

    private sealed class ShortRunConfig : ManualConfig
    {
        public ShortRunConfig()
        {
            // Set OKOJO_BENCH_QUICK=1 for fast dev-iteration verification runs:
            // one invocation per benchmark, no pilot/warmup. BDN command-line
            // job arguments cannot be used for this because they are additive
            // when a [Config] defines jobs (they would run both jobs).
            if (Environment.GetEnvironmentVariable("OKOJO_BENCH_QUICK") == "1")
                AddJob(Job.Dry.WithInvocationCount(1).WithUnrollFactor(1));
            else
                AddJob(Job.ShortRun.WithWarmupCount(3).WithIterationCount(5));
            WithSummaryStyle(
                BenchmarkDotNet.Reports.SummaryStyle.Default.WithMaxParameterColumnWidth(40)
            );
        }
    }
}
