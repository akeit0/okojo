using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoCompileBenchmarks
{
    private JsAst program = null!;
    private JsRuntime sharedRuntime = null!;
    private JsRealm sharedRealm = null!;
    private string source = string.Empty;

    [Params("pc-id", "pc-param", "pc-dstr", "pc-dynimp")]
    public string Scenario { get; set; } = "pc-id";

    [GlobalSetup]
    public void Setup()
    {
        source = ScriptSourceLoader.LoadScenario(Scenario);
        program = JavaScriptParser.ParseScript(source);
        sharedRuntime = JsRuntime.CreateBuilder().Build();
        sharedRealm = sharedRuntime.DefaultRealm;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        program.Dispose();
        sharedRuntime.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int Okojo_Parse_Only()
    {
        using var ast = JavaScriptParser.ParseScript(source);
        return ast.ChildRange(ast[ast.Root].Arg0, ast[ast.Root].Arg1).Length;
    }

    [Benchmark]
    public int Okojo_Compile_Preparsed()
    {
        var script = new JsScriptCompiler(sharedRealm).Compile(program, null);
        return script.Bytecode.Length;
    }

    [Benchmark]
    public int Okojo_Parse_And_Compile()
    {
        using var ast = JavaScriptParser.ParseScript(source);
        var script = new JsScriptCompiler(sharedRealm).Compile(ast, null);
        return script.Bytecode.Length;
    }
}
