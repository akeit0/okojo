using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Jint;
using Okojo.Benchmarks;
using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

/// <summary>
///     Port of Jint.Benchmark/EngineComparisonBenchmark.cs (jint commit
///     34525701f1b4) restricted to the two engines under investigation and
///     adapted to this repository's embedding API. Mirrors their lanes:
///     fresh strict engine per operation, strict source prepared by parsing
///     once in GlobalSetup.
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

    private static readonly Dictionary<string, string> StrictSources = new();

    [GlobalSetup]
    public void Setup()
    {
        foreach (var key in ScriptKeys)
        {
            var script = ScriptSourceLoader.LoadScenario(key);
            if (key.Contains("dromaeo"))
                script = DromaeoHelpers + Environment.NewLine + script;

            var strict = "\"use strict\";" + Environment.NewLine + script;
            StrictSources[key] = strict;
        }

        // Warm compile paths once so per-op lanes measure execution, not
        // first-run JIT of host plumbing.
        using var warmRuntime = JsRuntime.CreateBuilder().Build();
        _ = JsCompiler.Compile(warmRuntime.DefaultRealm, StrictSources["minimal"]);
        _ = new Engine(static o => o.Strict()).Execute(StrictSources["minimal"]);
    }

    public IEnumerable<string> FileNames() => ScriptKeys;

    [ParamsSource(nameof(FileNames))]
    public string FileName { get; set; } = "minimal";

    [Benchmark]
    [BenchmarkCategory("Jint")]
    public void Jint_Strict()
    {
        var engine = new Engine(static options => options.Strict());
        engine.Execute(StrictSources[FileName]);
    }

    [Benchmark]
    [BenchmarkCategory("Okojo")]
    public void Okojo_Strict()
    {
        using var runtime = JsRuntime.CreateBuilder().Build();
        var script = JsCompiler.Compile(runtime.DefaultRealm, StrictSources[FileName]);
        runtime.DefaultRealm.Execute(script);
    }

    private sealed class ShortRunConfig : ManualConfig
    {
        public ShortRunConfig()
        {
            AddJob(Job.ShortRun.WithWarmupCount(3).WithIterationCount(5));
            WithSummaryStyle(
                BenchmarkDotNet.Reports.SummaryStyle.Default.WithMaxParameterColumnWidth(40)
            );
        }
    }
}
