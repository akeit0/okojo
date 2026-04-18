using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Jint;
using Jint.Native.Function;
using Okojo.Benchmarks;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoAwaitBenchmarks
{
    private Engine jint = null!;
    private Function jintFunction = null!;
    private JsBytecodeFunction jsFunction = null!;
    private JsRealm jsVm = null!;
    private double sink;
    private string source = string.Empty;

    [Params("await-async")] //"await-immediate", "await-suspended",
    public string Scenario { get; set; } = "await-immediate";

    [GlobalSetup]
    public void Setup()
    {
        source = ScriptSourceLoader.LoadScenario(Scenario);

        var program = JavaScriptParser.ParseScript(source);
        jsVm = JsRuntime.CreateBuilder().Build().DefaultRealm;
        var okojoScript = JsCompiler.Compile(jsVm, program);
        jsVm.Execute(okojoScript);
        jsFunction = (JsBytecodeFunction)jsVm.Accumulator.AsObject();

        jint = new(cfg => cfg.ExperimentalFeatures = ExperimentalFeature.All);
        jintFunction = (Function)jint.Evaluate(source);
    }

    [Benchmark]
    public double Okojo_Execute_Await_Function()
    {
        jsVm.Execute(jsFunction);
        var outValue = jsVm.Global["out"];
        sink = outValue.IsInt32 ? outValue.Int32Value : outValue.NumberValue;
        return sink;
    }

    [Benchmark(Baseline = true)]
    public double Jint_Execute_Function()
    {
        sink = jintFunction.Call().AsNumber();
        return sink;
    }
}
