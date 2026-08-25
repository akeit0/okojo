using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Okojo.Benchmarks;
using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

public static class VmLoopDispatchJobs
{
    public static readonly Job DynamicPgoOn = Job
        .Default.WithId("DynamicPgo-On")
        .WithWarmupCount(3)
        .WithIterationCount(10)
        .WithEnvironmentVariable("DOTNET_TieredPGO", "1");

    public static readonly Job DynamicPgoOff = Job
        .Default.WithId("DynamicPgo-Off")
        .WithWarmupCount(3)
        .WithIterationCount(10)
        .WithEnvironmentVariable("DOTNET_TieredPGO", "0");

    public static readonly Job TieredOff = Job
        .Default.WithId("Tiered-Off")
        .WithWarmupCount(3)
        .WithIterationCount(10)
        .WithEnvironmentVariable("DOTNET_TieredCompilation", "0")
        .WithEnvironmentVariable("DOTNET_TieredPGO", "0");

    public static IEnumerable<Job> Values => [DynamicPgoOn, DynamicPgoOff, TieredOff];
}

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Declared)]
public class VmLoopDispatchBenchmarks
{
    private JsBytecodeFunction jsFunction = null!;

    private JsRealm jsVm = null!;

    private string source = string.Empty;

    // in scripts/*.js
    [Params("smi-sum-loop", "for-loop-sum", "named-get", "arith", "pure-function-call")]
    public string Scenario { get; set; } = "smi-sum-loop";

    [ParamsSource(nameof(JobValues))]
    public Job VmJob { get; set; } = VmLoopDispatchJobs.DynamicPgoOn;

    public static IEnumerable<Job> JobValues => VmLoopDispatchJobs.Values;

    [GlobalSetup]
    public void Setup()
    {
        source = ScriptSourceLoader.LoadScenario(Scenario);
        var program = FlatJavaScriptParser.ParseScript(source);
        jsVm = JsRuntime.CreateBuilder().Build().DefaultRealm;
        var okojoScript = JsCompiler.Compile(jsVm, program);
        jsVm.Execute(okojoScript);
        jsFunction = (JsBytecodeFunction)jsVm.Accumulator.AsObject()!;
    }

    [Benchmark]
    public void Okojo_Execute_VmLoop()
    {
        jsVm.Execute(jsFunction);
    }
}
