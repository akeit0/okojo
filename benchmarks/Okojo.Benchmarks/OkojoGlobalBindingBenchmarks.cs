using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoGlobalBindingBenchmarks
{
    private const string MathLoadFunctionSource = """
                                                  (() => {
                                                      let value = Math;
                                                      for (let i = 0; i < 1000000; i++) {
                                                          value = Math;
                                                      }
                                                      return value;
                                                  })()
                                                  """;

    private const string LoadFunctionSource = """
                                              (() => {
                                                  let sum = 0;
                                                  for (let i = 0; i < 1000000; i++) {
                                                      sum += shared;
                                                  }
                                                  return sum;
                                              })()
                                              """;

    private const string StoreFunctionSource = """
                                               (() => {
                                                   for (let i = 0; i < 1000000; i++) {
                                                       shared = i;
                                                   }
                                                   return shared;
                                               })()
                                               """;

    private JsRealm globalLoadRealm = null!;

    private JsScript globalLoadScript = null!;
    private JsRealm globalStoreRealm = null!;
    private JsScript globalStoreScript = null!;
    private JsRealm lexicalLoadRealm = null!;
    private JsScript lexicalLoadScript = null!;
    private JsRealm lexicalStoreRealm = null!;
    private JsScript lexicalStoreScript = null!;

    [GlobalSetup]
    public void Setup()
    {
        (globalLoadRealm, globalLoadScript) = CreateScript(string.Empty, MathLoadFunctionSource);
        (lexicalLoadRealm, lexicalLoadScript) = CreateScript("let shared = 1;", LoadFunctionSource);
        (globalStoreRealm, globalStoreScript) = CreateScript("var shared = 0;", StoreFunctionSource);
        (lexicalStoreRealm, lexicalStoreScript) = CreateScript("let shared = 0;", StoreFunctionSource);
    }

    [Benchmark(Baseline = true)]
    public JsObject MathGlobalLoad()
    {
        globalLoadRealm.Execute(globalLoadScript);
        return globalLoadRealm.Accumulator.AsObject();
    }

    [Benchmark]
    public int GlobalLexicalLoad()
    {
        lexicalLoadRealm.Execute(lexicalLoadScript);
        return lexicalLoadRealm.Accumulator.Int32Value;
    }

    [Benchmark]
    public int GlobalObjectStore()
    {
        globalStoreRealm.Execute(globalStoreScript);
        return globalStoreRealm.Accumulator.Int32Value;
    }

    [Benchmark]
    public int GlobalLexicalStore()
    {
        lexicalStoreRealm.Execute(lexicalStoreScript);
        return lexicalStoreRealm.Accumulator.Int32Value;
    }

    private static (JsRealm Realm, JsScript Script) CreateScript(string preludeSource, string bodySource)
    {
        var realm = JsRuntime.CreateBuilder().Build().DefaultRealm;

        if (!string.IsNullOrEmpty(preludeSource))
        {
            var preludeScript = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript(preludeSource));
            realm.Execute(preludeScript);
        }

        var bodyScript = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript(bodySource));
        return (realm, bodyScript);
    }
}
