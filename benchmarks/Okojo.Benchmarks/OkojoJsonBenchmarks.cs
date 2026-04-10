using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace Okojo.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoJsonObjectBenchmarks : OkojoNamedPropertyBenchmarkBase
{
    private string json = string.Empty;
    private JsonDocumentOptions options;

    [Params(8, 24, 32)] public int PropertyCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        InitializeRealm();
        json = BuildJsonObject(PropertyCount);
        options = default;
    }

    [Benchmark(Baseline = true)]
    public JsonDocument JsonDocument_ParseOnly()
    {
        return JsonDocument.Parse(json, options);
    }

    [Benchmark]
    public JsValue Okojo_ConvertJsonObject()
    {
        using var doc = JsonDocument.Parse(json, options);
        return Realm.Intrinsics.ConvertJsonElementForBenchmark(doc.RootElement);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoJsonDuplicateKeyBenchmarks : OkojoNamedPropertyBenchmarkBase
{
    private string json = string.Empty;
    private JsonDocumentOptions options;

    [Params(8, 24, 32)] public int PropertyCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        InitializeRealm();
        json = BuildJsonObjectWithDuplicateTail(PropertyCount);
        options = default;
    }

    [Benchmark(Baseline = true)]
    public JsonDocument JsonDocument_ParseOnly()
    {
        return JsonDocument.Parse(json, options);
    }

    [Benchmark]
    public JsValue Okojo_ConvertJsonObject_WithDuplicateNamedKey()
    {
        using var doc = JsonDocument.Parse(json, options);
        return Realm.Intrinsics.ConvertJsonElementForBenchmark(doc.RootElement);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoNestedJsonBenchmarks : OkojoNamedPropertyBenchmarkBase
{
    private string json = string.Empty;
    private JsonDocumentOptions options;

    [Params(8, 16)] public int Width { get; set; }

    [Params(2, 3)] public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        InitializeRealm();
        json = BuildNestedJsonPayload(Width, Depth);
        options = default;
    }

    [Benchmark(Baseline = true)]
    public JsonDocument JsonDocument_ParseOnly()
    {
        return JsonDocument.Parse(json, options);
    }

    [Benchmark]
    public JsValue Okojo_ConvertNestedJson()
    {
        using var doc = JsonDocument.Parse(json, options);
        return Realm.Intrinsics.ConvertJsonElementForBenchmark(doc.RootElement);
    }
}
