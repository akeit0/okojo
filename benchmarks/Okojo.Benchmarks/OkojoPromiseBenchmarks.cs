using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoPromiseBenchmarks
{
    private JsBytecodeFunction jsFunction = null!;
    private JsRealm jsVm = null!;
    private double sink;
    private string source = string.Empty;

    [Params("p-all", "p-any", "p-allset")] public string Scenario { get; set; } = "p-all";

    [GlobalSetup]
    public void Setup()
    {
        source = ScriptSourceLoader.LoadScenario(Scenario);

        var program = JavaScriptParser.ParseScript(source);
        jsVm = JsRuntime.CreateBuilder().Build().DefaultRealm;
        var compiler = new JsCompiler(jsVm);
        var okojoScript = compiler.Compile(program);
        jsVm.Execute(okojoScript);
        jsFunction = (JsBytecodeFunction)jsVm.Accumulator.AsObject();
    }

    [Benchmark]
    public double Okojo_Execute_Promise_Combinator()
    {
        jsVm.Execute(jsFunction);
        jsVm.PumpJobs();
        var outValue = jsVm.Global["out"];
        sink = outValue.IsInt32 ? outValue.Int32Value : outValue.NumberValue;
        return sink;
    }
}
