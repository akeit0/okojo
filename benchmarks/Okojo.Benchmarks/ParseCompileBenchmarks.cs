using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Okojo.Benchmarks;
using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

[MemoryDiagnoser]
[ShortRunJob]
[WarmupCount(6)]
[IterationCount(10)]
[Orderer(SummaryOrderPolicy.Declared)]
public class ParseCompileBenchmarks
{
    private JsRealm realm = null!;

    // in this file
    private string source = string.Empty;

    [Params("Micro", "Closures", "Classes", "Patterns", "AsyncGen")]
    public string Scenario { get; set; } = "Micro";

    [GlobalSetup]
    public void Setup()
    {
        source = Corpus.ScenarioSource(Scenario);
        realm = JsRuntime.CreateBuilder().Build().DefaultRealm;
    }

    [Benchmark(Baseline = true)]
    public JsScript Okojo_Compile()
    {
        return realm.CompileScript(source);
    }

    [Benchmark]
    public int Flat_Parse()
    {
        using var ast = FlatJavaScriptParser.ParseScript(source);
        return ast.Count;
    }

    [Benchmark]
    public JsScript Flat_Compile()
    {
        using var ast = FlatJavaScriptParser.ParseScript(source);
        return new JsScriptCompiler(realm).Compile(ast, null);
    }
}

internal static class Corpus
{
    public static string ScenarioSource(string scenario) =>
        scenario switch
        {
            "Micro" => Micro,
            "Closures" => Closures,
            "Classes" => Classes,
            "Patterns" => Patterns,
            "AsyncGen" => AsyncGen,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

    private const string Micro = """
        const scale = 2;
        function add(a, b) { return a * scale + b; }
        let total = 0;
        for (let i = 0; i < 100; i++) {
          if (i % 3 === 0) total += add(i, i / 2);
          else total -= i;
        }
        total;
        """;

    private const string Closures = """
        function makeCounters(n) {
          const counters = [];
          for (let i = 0; i < n; i++) {
            counters.push(function () { return i * 2; });
          }
          return counters;
        }
        const fns = makeCounters(12);
        let sum = 0;
        for (const fn of fns) sum += fn();
        function outer() {
          var x = 1;
          function mid() {
            function inner() { x += 5; return x; }
            return inner();
          }
          return mid() + x;
        }
        outer();
        sum;
        """;

    private const string Classes = """
        class Base {
          secret = 7;
          static origin = 'base';
          constructor(id) { this.id = id; }
          get id2x() { return this.id * 2; }
          describe() { return `${Base.origin}:${this.id}:${this.secret}`; }
        }
        class Derived extends Base {
          values;
          constructor(id, values) { super(id); this.values = values; }
          total() { return this.values.reduce((a, b) => a + b, this.id2x); }
        }
        const items = [];
        for (let i = 0; i < 8; i++) items.push(new Derived(i, [i, i + 1, i * 2]));
        let acc = '';
        for (const d of items) acc += d.describe() + '=' + d.total() + ';';
        acc.length;
        """;

    private const string Patterns = """
        const config = {
          server: { host: 'h', ports: [80, 443] },
          flags: { debug: true, verbose: false },
          tags: ['a', ...['b', 'c']],
        };
        const {
          server: { host, ports: [http, ...restPorts] },
          flags: { debug = false },
          tags: [first, ...others],
        } = config;
        function draw({ x = 0, y = 0 }, ...extras) {
          return [x, y, extras.length];
        }
        const [a1 = 9, , a3] = [1, 2, 3];
        const { p1, ...restObj } = { p1: 1, q: 2, r: 3 };
        const tpl = `host=${host} first=${first} rest=${restPorts.length}`;
        const swapped = ([a1, a3] = [a3, a1]);
        draw({ y: 4 }, 1, 2, 3);
        tpl.length + a1 + a3 + p1 + Object.keys(restObj).length + swapped.length;
        """;

    private const string AsyncGen = """
        async function produce(values) {
          const out = [];
          for await (const v of values) out.push(v * 2);
          return out;
        }
        async function* gen(n) {
          for (let i = 0; i < n; i++) yield await i;
        }
        async function consume() {
          let last;
          for await (const v of gen(6)) last = v;
          const [first] = await Promise.all([produce([1, 2])]);
          return last + first.length;
        }
        consume().then((v) => v);
        """;
}
