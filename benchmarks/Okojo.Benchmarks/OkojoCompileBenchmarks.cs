using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoCompileBenchmarks
{
    private JsProgram program = null!;
    private JsRealm sharedRealm = null!;
    private string source = string.Empty;

    [Params("pc-id", "pc-param", "pc-dstr", "pc-dynimp")]
    public string Scenario { get; set; } = "pc-id";

    [GlobalSetup]
    public void Setup()
    {
        source = ScriptSourceLoader.LoadScenario(Scenario);
        program = JavaScriptParser.ParseScript(source);
        sharedRealm = JsRuntime.CreateBuilder().Build().DefaultRealm;
    }

    [Benchmark(Baseline = true)]
    public int Okojo_Parse_Only()
    {
        var program = JavaScriptParser.ParseScript(source);
        return program.Statements.Count;
    }

    [Benchmark]
    public int Okojo_Compile_Preparsed()
    {
        var script = JsCompiler.Compile(sharedRealm, program);
        return script.Bytecode.Length;
    }

    [Benchmark]
    public int Okojo_Parse_And_Compile()
    {
        var program = JavaScriptParser.ParseScript(source);
        var script = JsCompiler.Compile(sharedRealm, program);
        return script.Bytecode.Length;
    }
}
