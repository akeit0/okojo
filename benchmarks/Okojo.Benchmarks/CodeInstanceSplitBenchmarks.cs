using BenchmarkDotNet.Attributes;
using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;

namespace Okojo.Benchmarks;

// Different operations, not competing implementations. Compare each workload
// against its own prior run; do not interpret ratios between these methods.
[MemoryDiagnoser]
public class CodeInstanceSplitBenchmarks
{
    internal const string Source = "function hot(o) { return o.value + 1; } hot;";
    private JsRuntime runtime = null!;
    private JsRealm realm = null!;
    private JsCompilationUnit unit = null!;
    private JsBytecodeFunction hot = null!;
    private JsValue[] arguments = null!;

    [GlobalSetup]
    public void Setup()
    {
        runtime = JsRuntime.Create();
        realm = runtime.DefaultRealm;
        unit = JsCompiler.CompileUnit(Source, "split-benchmark.js");
        realm.Execute(unit.Link(realm));
        hot = (JsBytecodeFunction)realm.Accumulator.AsObject();
        arguments = [realm.Evaluate("({ value: 41 })")];
        for (var i = 0; i < 100; i++)
            hot.Call(realm, JsValue.Undefined, arguments);
    }

    [GlobalCleanup]
    public void Cleanup() => runtime.Dispose();

    [Benchmark]
    public JsCompilationUnit CompilePortable() => JsCompiler.CompileUnit(Source);

    [Benchmark]
    public JsScript CompileAndLink() => JsCompiler.Compile(realm, Source);

    [Benchmark]
    public JsScript LinkCached() => unit.Link(realm);

    [Benchmark]
    public JsBytecodeFunction CreateClosure() => hot.Script.CreateClosure();

    [Benchmark]
    public JsValue CallWarm() => hot.Call(realm, JsValue.Undefined, arguments);
}

// Iteration setup is deliberately separate from the timed link. BenchmarkDotNet
// runs a single invocation per iteration with iteration setup, avoiding a warm
// CWT hit being mislabeled as cold linking. Interpret timing with that constraint.
[MemoryDiagnoser]
public class CodeInstanceSplitColdLinkBenchmarks
{
    private JsCompilationUnit unit = null!;
    private JsRuntime runtime = null!;

    [GlobalSetup]
    public void SetupCode() => unit = JsCompiler.CompileUnit(CodeInstanceSplitBenchmarks.Source);

    [IterationSetup]
    public void SetupRealm() => runtime = JsRuntime.Create();

    [IterationCleanup]
    public void CleanupRealm() => runtime.Dispose();

    [Benchmark]
    public JsScript LinkCold() => unit.Link(runtime.DefaultRealm);
}
