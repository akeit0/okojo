using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Jint;
using Jint.Native.Function;
using Okojo.Benchmarks;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Declared)]
[Config(typeof(ConfigWithCustomEnvVars))]
public class VsJintBenchmarks
{
    private Engine jint = null!;
    private Function jintFunction = null!;

    private JsBytecodeFunction jsFunction = null!;
    private JsRealm jsVm = null!;

    private double sink;

    // in scripts/*.js
    private string source = string.Empty;

    //scripts/*.js
    [Params("for-loop-sum", "pure-function-call", "many-object")] // "indexing", "lexical-block"//"loop","generator",

    // [Params("nop", "arith", "loop", "object", "many-object", "function-call", "closure-heavy", "with-eval-heavy",
    //     "math-call")]
    public string Scenario { get; set; } = "indexing";

    [GlobalSetup]
    public void Setup()
    {
        source = ScriptSourceLoader.LoadScenario(Scenario);

        jint = new(cfg => cfg.ExperimentalFeatures = ExperimentalFeature.All);
        jintFunction = (Function)jint.Evaluate(source);
        ;
        var program = JavaScriptParser.ParseScript(source);
        jsVm = JsRuntime.CreateBuilder().Build().DefaultRealm;
        var compiler = new JsCompiler(jsVm);
        var okojoScript = compiler.Compile(program);
        jsVm.Execute(okojoScript);
        jsFunction = (JsBytecodeFunction)jsVm.Accumulator.AsObject();
    }

    // [Benchmark(Baseline = true)]
    // public double Managed_Execute_Function()
    // {
    //     var result = mFucntion.Invoke(new JsFunctionContext(_managedVm), JsValue.Undefined,
    //         ReadOnlySpan<JsValue>.Empty);
    //     _sink = result.IsFloat64 ? result.Float64Value : double.NaN;
    //     return _sink;
    // }
    [Benchmark]
    [BenchmarkCategory("Okojo")]
    public double Okojo_Execute_Function()
    {
        jsVm.Execute(jsFunction);
        return sink;
    }

    // [Benchmark(Baseline = true)]
    // public double Managed_Compile_And_Execute()
    // {
    //     ;
    //     var script = ManagedScriptCompiler.CompileSource(_source);
    //     var result = _managedVm.Execute(script);
    //     _sink = result.IsFloat64 ? result.Float64Value : double.NaN;
    //     return _sink;
    // }
    [BenchmarkCategory("Jint")]

    [Benchmark(Baseline = true)]
    public double Jint_Execute_Function()
    {
        var value = jintFunction.Call().AsNumber();
        sink = value;
        return sink;
    }

    private class ConfigWithCustomEnvVars : ManualConfig
    {
        public ConfigWithCustomEnvVars()
        {
            AddJob(Job.ShortRun
                    .WithEnvironmentVariables(new EnvironmentVariable("DOTNET_TieredPGO", "0"))
                )
                //.
                //AddJob(Job.ShortRun.WithRuntime(NativeAotRuntime.Net90))
                ;
        }
    }
    // [Benchmark]
    // public double Jint_Execute_ReusedEngine()
    // {
    //     var jintV=_jint.Evaluate(_source);
    //     var f =(Function)jintV;
    //     ;
    //     var value = f.Call()AsNumber();
    //     _sink = value;
    //     return _sink;
    // }
}
