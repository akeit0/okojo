using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;
using OkojoJsValue = Okojo.JsValue;

namespace Okojo.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoAsyncGeneratorBenchmarks
{
    private JsBytecodeFunction jsFunction = null!;
    private JsRealm jsVm = null!;
    private double sink;
    private string source = string.Empty;

    [Params("ag-retq", "ag-retq-core")] public string Scenario { get; set; } = "ag-retq";

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
    public double Okojo_Execute_AsyncGenerator_ReturnQueue()
    {
        jsVm.Execute(jsFunction);
        jsVm.PumpJobs();
        var outValue = jsVm.Global["out"];
        sink = ReadBenchmarkOutput(outValue);
        return sink;
    }

    private static double ReadBenchmarkOutput(OkojoJsValue outValue)
    {
        if (outValue.IsInt32)
            return outValue.Int32Value;
        if (outValue.IsNumber)
            return outValue.NumberValue;
        if (outValue.TryGetObject(out var obj) && obj is JsArray array)
            return SumSettledPromiseResults(array);
        return 0;
    }

    private static double SumSettledPromiseResults(JsArray promises)
    {
        double sum = 0;
        for (uint i = 0; i < promises.Length; i++)
        {
            if (!promises.TryGetElement(i, out var promiseValue) ||
                !promiseValue.TryGetObject(out var promiseObj) ||
                promiseObj is not JsPromiseObject promise ||
                !promise.IsFulfilled)
                continue;

            var settled = promise.SettledResult;
            if (settled.TryGetObject(out var resultObj))
            {
                if (resultObj.TryGetProperty("value", out var value))
                {
                    if (value.IsInt32)
                        sum += value.Int32Value;
                    else if (value.IsNumber)
                        sum += value.NumberValue;
                }

                if (resultObj.TryGetProperty("done", out var done) && done.IsTrue)
                    sum += 1;
            }
        }

        return sum;
    }
}
