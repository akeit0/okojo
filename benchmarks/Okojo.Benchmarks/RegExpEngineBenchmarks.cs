using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Okojo.Objects;
using Okojo.RegExp;
using Okojo.RegExp.Experimental;
using Okojo.Runtime;

namespace Okojo.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[WarmupCount(4)]
[IterationCount(4)]
[Orderer(SummaryOrderPolicy.Declared)]
public class RegExpEngineBenchmarks
{
    private sealed record RegExpScenario(
        string Name,
        string Pattern,
        string Flags,
        string Input,
        int StartIndex = 0);

    private static readonly IReadOnlyDictionary<string, RegExpScenario> ScenarioMap =
        new Dictionary<string, RegExpScenario>(StringComparer.Ordinal)
        {
            ["literal-scan"] = new("literal-scan", "a+", "", "baaaaaaaaaaaaaaaaa"),
            ["ascii-word-scan"] = new("ascii-word-scan", @"\w+", "", "zzzzzzzzzzzzzzzzzzaaaaaaaaaaaaaa_0099!"),
            ["first-set-class-scan"] = new("first-set-class-scan", @"[A-Z]foo", "", "zzzzzzzzzzzzzzzzzzQfoo"),
            ["property-whole-input"] = new("property-whole-input", @"^\p{Uppercase_Letter}+$", "u",
                Repeat("ABCDEFGHIJKLMNOPQRSTUVWXYZ", 32)),
            ["string-property-whole-input"] = new("string-property-whole-input", @"^\p{RGI_Emoji}+$", "v",
                Repeat("1\uFE0F\u20E3", 64)),
            ["named-capture"] = new("named-capture", @"(?<name>a)(b)?", "g", "zabz", 1),
            ["unicode-casefold"] = new("unicode-casefold", @"[\u0390]", "ui", "\u1fd3"),
            ["unicode-class-set-casefold"] = new("unicode-class-set-casefold", @"[\u0390x]", "ui", "\u1fd3"),
            ["lookahead-backref"] = new("lookahead-backref", @"(.*?)a(?!(a+)b\2c)\2(.*)", "", "baaabaac"),
            ["global-empty"] = new("global-empty", @"a*", "g", string.Empty)
        };

    private readonly IRegExpEngine currentEngine = RegExpEngine.Default;
    private readonly IRegExpEngine experimentalEngine = ExperimentalRegExpEngine.Default;
    private RegExpScenario scenario = null!;
    private RegExpCompiledPattern currentCompiled = null!;
    private RegExpCompiledPattern experimentalCompiled = null!;
    private JsRuntime currentRuntime = null!;
    private JsRuntime experimentalRuntime = null!;
    private JsBytecodeFunction currentJsFunction = null!;
    private JsBytecodeFunction experimentalJsFunction = null!;
    private JsRealm currentRealm = null!;
    private JsRealm experimentalRealm = null!;
    private int sink;

    [Params("literal-scan", "ascii-word-scan", "first-set-class-scan", "property-whole-input",
        "string-property-whole-input", "unicode-casefold", "unicode-class-set-casefold", "lookahead-backref")]
    public string Scenario { get; set; } = "literal-scan";

    [GlobalSetup]
    public void Setup()
    {
        scenario = ScenarioMap[Scenario];
        currentCompiled = currentEngine.Compile(scenario.Pattern, scenario.Flags);
        experimentalCompiled = experimentalEngine.Compile(scenario.Pattern, scenario.Flags);

        currentRuntime = JsRuntime.CreateBuilder()
            .UseRegExpEngine(RegExpEngine.Default)
            .Build();
        experimentalRuntime = JsRuntime.CreateBuilder()
            .UseRegExpEngine(ExperimentalRegExpEngine.Default)
            .Build();
        currentRealm = currentRuntime.DefaultRealm;
        experimentalRealm = experimentalRuntime.DefaultRealm;

        currentJsFunction = CompileRealmFunction(currentRealm, scenario);
        experimentalJsFunction = CompileRealmFunction(experimentalRealm, scenario);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        currentRuntime.Dispose();
        experimentalRuntime.Dispose();
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

    [Benchmark]
    public int Current_ReusedExec()
    {
        return ExecCompiled(currentEngine, currentCompiled, scenario);
    }

    [Benchmark]
    public int Experimental_ReusedExec()
    {
        return ExecCompiled(experimentalEngine, experimentalCompiled, scenario);
    }

    [Benchmark]
    public int Current_JsRuntimePath()
    {
        currentRealm.Execute(currentJsFunction);
        sink = currentRealm.Accumulator.IsNumber ? currentRealm.Accumulator.Int32Value : -1;
        return sink;
    }

    [Benchmark]
    public int Experimental_JsRuntimePath()
    {
        experimentalRealm.Execute(experimentalJsFunction);
        sink = experimentalRealm.Accumulator.IsNumber ? experimentalRealm.Accumulator.Int32Value : -1;
        return sink;
    }

    private static JsBytecodeFunction CompileRealmFunction(JsRealm realm, RegExpScenario scenario)
    {
        var patternLiteral = ToJsStringLiteral(scenario.Pattern);
        var flagsLiteral = ToJsStringLiteral(scenario.Flags);
        var inputLiteral = ToJsStringLiteral(scenario.Input);
        realm.Eval($$"""
                    (() => {
                        const re = new RegExp({{patternLiteral}}, {{flagsLiteral}});
                        const input = {{inputLiteral}};
                        const startIndex = {{scenario.StartIndex}};
                        return function() {
                            re.lastIndex = 0;
                            const match = re.exec(input.slice(startIndex));
                            re.lastIndex = 0;
                            return match === null ? -1 : match[0].length;
                        };
                    })()
                    """);
        return (JsBytecodeFunction)realm.Accumulator.AsObject();
    }

    private static int CompileAndExec(IRegExpEngine engine, RegExpScenario scenario)
    {
        var compiled = engine.Compile(scenario.Pattern, scenario.Flags);
        return ExecCompiled(engine, compiled, scenario);
    }

    private static int ExecCompiled(IRegExpEngine engine, RegExpCompiledPattern compiled, RegExpScenario scenario)
    {
        var match = engine.Exec(compiled, scenario.Input, scenario.StartIndex);
        return match?.Length ?? -1;
    }

    private static string ToJsStringLiteral(string value)
    {
        return $$"""
                 "{{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n")}}"
                 """;
    }

    private static string Repeat(string value, int count)
    {
        var builder = new System.Text.StringBuilder(value.Length * count);
        for (var i = 0; i < count; i++)
            builder.Append(value);
        return builder.ToString();
    }
}
