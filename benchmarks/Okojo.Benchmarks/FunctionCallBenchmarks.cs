using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Okojo.Benchmarks;
using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

[MemoryDiagnoser]
[ShortRunJob]
[WarmupCount(8)]
[IterationCount(8)]
[Orderer(SummaryOrderPolicy.Declared)]
public class FunctionCallBenchmarks
{
    private JsBytecodeFunction jsFunction = null!;

    private JsRealm jsVm = null!;

    // in scripts/*.js
    private string source = string.Empty;

    //scripts/*.js
    [Params("arrow-function-call", "pure-function-call", "math-call")]
    public string Scenario { get; set; } = "arrow-function-call";

    [GlobalSetup]
    public void Setup()
    {
        source = ScriptSourceLoader.LoadScenario(Scenario);
        var program = JavaScriptParser.ParseScript(source);
        jsVm = JsRuntime.CreateBuilder().Build().DefaultRealm;
        var okojoScript = JsCompiler.Compile(jsVm, program);
        jsVm.Execute(okojoScript);
        jsFunction = (JsBytecodeFunction)jsVm.Accumulator.AsObject();
    }

    [Benchmark]
    public void Okojo_Execute_Function()
    {
        jsVm.Execute(jsFunction);
    }
}
