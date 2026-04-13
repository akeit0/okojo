using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Okojo.RegExp;
using Okojo.RegExp.Experimental;

namespace Okojo.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[WarmupCount(4)]
[IterationCount(4)]
[Orderer(SummaryOrderPolicy.Declared)]
public class RegExpOneShotPropertyBenchmarks
{
    private sealed record ScenarioData(string Pattern, string Flags, string Input);

    private static readonly IReadOnlyDictionary<string, ScenarioData> ScenarioMap =
        new Dictionary<string, ScenarioData>(StringComparer.Ordinal)
        {
            ["property-whole-input"] = new(@"^\p{Uppercase_Letter}+$", "u",
                Repeat("ABCDEFGHIJKLMNOPQRSTUVWXYZ", 32)),
            ["string-property-whole-input"] = new(@"^\p{RGI_Emoji}+$", "v",
                Repeat("1\uFE0F\u20E3", 64))
        };

    private readonly IRegExpEngine currentEngine = RegExpEngine.Default;
    private readonly IRegExpEngine experimentalEngine = ExperimentalRegExpEngine.Default;
    private ScenarioData scenario = null!;

    [Params("property-whole-input", "string-property-whole-input")]
    public string Scenario { get; set; } = "property-whole-input";

    [GlobalSetup]
    public void Setup()
    {
        scenario = ScenarioMap[Scenario];
    }

    [Benchmark(Baseline = true)]
    public int Current_Compile_And_FirstExec()
    {
        return CompileAndExec(currentEngine, scenario);
    }

    [Benchmark]
    public int Experimental_Compile_And_FirstExec()
    {
        return CompileAndExec(experimentalEngine, scenario);
    }

    private static int CompileAndExec(IRegExpEngine engine, ScenarioData scenario)
    {
        var compiled = engine.Compile(scenario.Pattern, scenario.Flags);
        var match = engine.Exec(compiled, scenario.Input, 0);
        return match?.Length ?? -1;
    }

    private static string Repeat(string value, int count)
    {
        var builder = new System.Text.StringBuilder(value.Length * count);
        for (var i = 0; i < count; i++)
            builder.Append(value);
        return builder.ToString();
    }
}
