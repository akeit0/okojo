using System.Diagnostics;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Parsing;

var source =
    args.Length == 0
        ? """
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
            """
        : File.ReadAllText(Path.GetFullPath(args[0]));

if (args.Contains("--strict", StringComparer.OrdinalIgnoreCase))
    source = "\"use strict\";" + Environment.NewLine + source;

var realm = JsRuntime.CreateBuilder().Build().DefaultRealm;

for (var i = 0; i < 500; i++)
{
    using var ast = JavaScriptParser.ParseScript(source);
    _ = new JsScriptCompiler(realm).Compile(ast, null);
}

const int Samples = 200;

// Phase attribution over many samples.
long parseBytes = 0;
long collectBytes = 0;
long planBytes = 0;
long compileRestBytes = 0;

// Internal stage APIs via IVT: replicate Compile(JsAst) pipeline manually.
var compiler = new JsScriptCompiler(realm);

// Warm the manual pipeline once to stabilize lazy state.
{
    using var ast = JavaScriptParser.ParseScript(source);
    _ = compiler.Compile(ast, null);
}

for (var s = 0; s < Samples; s++)
{
    var before = GC.GetTotalAllocatedBytes(precise: true);

    var astStart = before;
    using var ast = JavaScriptParser.ParseScript(source);
    parseBytes += GC.GetTotalAllocatedBytes(precise: true) - astStart;

    var collectStart = GC.GetTotalAllocatedBytes(precise: true);
    using var collected = CompilerBindingCollector.Collect(ast);
    collectBytes += GC.GetTotalAllocatedBytes(precise: true) - collectStart;

    var planStart = GC.GetTotalAllocatedBytes(precise: true);
    using var plan = CompilerStoragePlanner.Plan(collected, ast);
    planBytes += GC.GetTotalAllocatedBytes(precise: true) - planStart;

    var restStart = GC.GetTotalAllocatedBytes(precise: true);
    _ = compiler.Compile(ast, null);
    compileRestBytes += GC.GetTotalAllocatedBytes(precise: true) - restStart;
}

Console.WriteLine($"samples={Samples}");
Console.WriteLine($"parse            : {parseBytes / (double)Samples / 1024:F2} KB/op");
Console.WriteLine($"collect          : {collectBytes / (double)Samples / 1024:F2} KB/op");
Console.WriteLine($"plan             : {planBytes / (double)Samples / 1024:F2} KB/op");
Console.WriteLine($"compile(full dup): {compileRestBytes / (double)Samples / 1024:F2} KB/op");
