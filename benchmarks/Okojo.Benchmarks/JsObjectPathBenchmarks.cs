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
public class JsObjectPathBenchmarks
{
    private Engine jint = null!;

    private Function jintFunction = null!;
    private JsBytecodeFunction jsFunction = null!;
    private JsRealm jsVm = null!;
    private double sink;
    private string source = string.Empty;

    [Params(
        //"indexing",
        // "index-descriptor",
        "private-field-hot",
        "private-accessor-hot")]
    public string Scenario { get; set; } = "indexing";

    [GlobalSetup]
    public void Setup()
    {
        source = ScriptSourceLoader.LoadScenario(Scenario);
        jint = new();
        jintFunction = (Function)jint.Evaluate(source);
        var program = JavaScriptParser.ParseScript(source);
        jsVm = JsRuntime.CreateBuilder().Build().DefaultRealm;
        var compiler = new JsCompiler(jsVm);
        var okojoScript = compiler.Compile(program);
        jsFunction = new(jsVm, okojoScript);
    }

    public double Okojo_Execute_Function()
    {
        jsVm.Execute(jsFunction);
        return sink;
    }

    [Benchmark]
    public double Jint_Execute_Function()
    {
        var value = jintFunction.Call().AsNumber();
        sink = value;
        return sink;
    }
}
