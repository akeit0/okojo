using System.Diagnostics;
using System.Text;
using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.Text.RegularExpressions;
using BclRegex = System.Text.RegularExpressions.Regex;

// Probe for the dromaeo-object-regexp-modern gap: Okojo's backtracking VM vs
// Jint-style compiled .NET Regex.
//
//   lib  library-level micro-probes over dromaeo patterns/inputs (default)
//   js   run the real scenario through the engine; good dotnet-trace target
//
// Trace recipe:
//   dotnet-trace collect --profile cpu-sampling -o regexp.nettrace -- \
//       dotnet RegExpProbe.dll js --iterations 5

return args.Length > 0 && args[0] == "js" ? RunJs(args)
    : args.Length > 0 && args[0] == "shape" ? RunShapes(args)
    : RunLib(args);

static int RunJs(string[] args)
{
    var iterations = GetOption(args, "--iterations", 3);
    var source =
        """
            var startTest = function () { };
            var test = function (name, fn) { fn(); };
            var endTest = function () { };
            var prep = function (fn) { fn(); };
            """ + LoadScenario("dromaeo-object-regexp-modern");
    using var runtime = JsRuntime.CreateBuilder().Build();
    var realm = runtime.DefaultRealm;
    var script = JsCompiler.Compile(realm, source);
    realm.Execute(script, pumpJobsAfterRun: false);

    var sw = Stopwatch.StartNew();
    for (var i = 0; i < iterations; i++)
        realm.Execute(script, pumpJobsAfterRun: false);
    sw.Stop();
    Console.WriteLine($"js mode: {iterations} iterations in {sw.ElapsedMilliseconds} ms");
    return 0;
}

static int RunShapes(string[] args)
{
    var iterations = GetOption(args, "--iterations", 2);
    var preamble = """
        var startTest = function () { };
        var test = function (name, fn) { fn(); };
        var endTest = function () { };
        var prep = function (fn) { fn(); };
        var ret, tmp, re;
        function generateTestStrings(count) {
            var str = [];
            for (var i = 0; i < 16384; i++)
                str.push(String.fromCharCode((25 * Math.random()) + 97));
            str = str.join("");
            str += str;
            str += str;
            var out = [str];
            for (var j = 1; j < count; j++) {
                var c = String.fromCharCode((25 * Math.random()) + 97);
                out.push(c + str + c);
            }
            return out;
        }
        """;

    (string Name, string Body)[] shapes =
    [
        (
            "SplitEmpty",
            "re = /(?:)/; tmp = generateTestStrings(30); for (let i = 0; i < 30; i++) ret = tmp[i].split(re);"
        ),
        (
            "SplitChar",
            "re = /a/; tmp = generateTestStrings(30); for (let i = 0; i < 30; i++) ret = tmp[i].split(re);"
        ),
        (
            "SplitStar",
            "re = /.*/; tmp = generateTestStrings(100); for (let i = 0; i < 100; i++) ret = tmp[i].split(re);"
        ),
        (
            "MatchLiteralG",
            "re = /aaaaaaaaaa/g; tmp = generateTestStrings(100); for (let i = 0; i < 100; i++) ret = tmp[i].match(re);"
        ),
        (
            "TestLiteralG",
            "re = /aaaaaaaaaa/g; tmp = generateTestStrings(100); for (let i = 0; i < 100; i++) ret = re.test(tmp[i]);"
        ),
        (
            "ReplaceLiteralG",
            "re = /aaaaaaaaaa/g; tmp = generateTestStrings(50); for (let i = 0; i < 50; i++) ret = tmp[i].replace(re, 'asdfasdfasdf');"
        ),
        (
            "VarStarMatch",
            "re = /a.*a/; tmp = generateTestStrings(100); for (let i = 0; i < 100; i++) ret = tmp[i].match(re);"
        ),
        (
            "CaptureGReplace",
            "re = /aa(b)aa/g; tmp = generateTestStrings(50); for (let i = 0; i < 50; i++) ret = tmp[i].replace(re, 'asdf\\\\1asdfasdf');"
        ),
        ("ArrayBuildOnly", "tmp = generateTestStrings(100);"),
        (
            "FillDenseCached",
            "for (var n = 0; n < 30; n++) { var a = []; for (let i = 0; i < 65536; i++) a[i] = 'x'; }"
        ),
        (
            "FillPushCached",
            "for (var n = 0; n < 30; n++) { var a = []; for (let i = 0; i < 65536; i++) a.push('x'); }"
        ),
    ];

    using var runtime = JsRuntime.CreateBuilder().Build();
    foreach (var (name, body) in shapes)
    {
        var realm = runtime.DefaultRealm;
        var script = JsCompiler.Compile(realm, preamble + body);
        realm.Execute(script, pumpJobsAfterRun: false);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
            realm.Execute(script, pumpJobsAfterRun: false);
        sw.Stop();
        Console.WriteLine($"{name, -18} {sw.Elapsed.TotalMilliseconds / iterations, 9:F1} ms/iter");
    }
    return 0;
}

static int RunLib(string[] args)
{
    string? caseFilter = null;
    for (var i = 1; i < args.Length - 1; i++)
        if (args[i] == "--case")
            caseFilter = args[i + 1];
    var strings = GenerateTestStrings(100);

    (string Name, string Pattern, string Flags, int Inputs)[] cases =
    [
        ("LiteralGlobalMatch", "aaaaaaaaaa", "g", 100),
        ("LiteralGlobalTest", "aaaaaaaaaa", "g", 100),
        ("LiteralReplaceEmpty", "aaaaaaaaaa", "g", 50),
        ("VariableStarMatch", "a.*a", "", 100),
        ("CaptureGlobalMatch", "aa(b)aa", "g", 100),
        ("SplitEmpty", "(?:)", "", 30),
        ("SplitChar", "a", "", 30),
        ("SplitStar", ".*", "", 100),
    ];

    foreach (var (name, pattern, flags, inputs) in cases)
    {
        if (caseFilter is not null && !name.StartsWith(caseFilter, StringComparison.Ordinal))
            continue;

        var okojo = RegExp.Compile(pattern, flags);
        var bcl = new BclRegex(
            pattern is "(?:)" ? "" : pattern,
            System.Text.RegularExpressions.RegexOptions.Compiled
                | System.Text.RegularExpressions.RegexOptions.ECMAScript
                | System.Text.RegularExpressions.RegexOptions.CultureInvariant
        );

        double okojoMs = Measure(() => RunOkojoCase(name, okojo, strings, inputs));
        double bclMs = Measure(() => RunBclCase(name, bcl, strings, inputs));
        Console.WriteLine(
            $"{name, -22} ok={okojoMs, 9:F2} ms   bcl={bclMs, 9:F2} ms   ratio={(okojoMs / Math.Max(bclMs, 0.001)), 7:F2}x"
        );
    }
    return 0;
}

static double Measure(Action action)
{
    action();
    var sw = Stopwatch.StartNew();
    action();
    sw.Stop();
    return sw.Elapsed.TotalMilliseconds;
}

static void RunOkojoCase(string name, RegExp re, string[] strings, int inputs)
{
    Span<CaptureRange> captures = stackalloc CaptureRange[Math.Max(re.RequiredCaptureCount, 4)];
    switch (name)
    {
        case "LiteralGlobalMatch":
        case "CaptureGlobalMatch":
            for (var s = 0; s < inputs; s++)
            {
                int lastIndex = 0;
                while (re.TryExec(strings[s], ref lastIndex, captures, out _)) { }
            }
            break;
        case "LiteralGlobalTest":
        case "VariableStarMatch":
            for (var s = 0; s < inputs; s++)
                re.TryMatch(strings[s].AsSpan(), 0, captures, out _);
            break;
        case "LiteralReplaceEmpty":
            for (var s = 0; s < inputs; s++)
                ProbeState.Sink += ReplaceToString(re, strings[s]).Length;
            break;
        case "SplitEmpty":
        case "SplitChar":
        case "SplitStar":
            for (var s = 0; s < inputs; s++)
                ProbeState.Sink += CountSegments(re, strings[s]);
            break;
        default:
            throw new InvalidOperationException($"Unknown case '{name}'.");
    }
}

static string ReplaceToString(RegExp re, string input)
{
    var sb = new StringBuilder();
    Span<CaptureRange> captures = stackalloc CaptureRange[Math.Max(re.RequiredCaptureCount, 4)];
    int lastIndex = 0;
    int copied = 0;
    while (re.TryExec(input.AsSpan(), ref lastIndex, captures, out _))
    {
        var range = captures[0];
        sb.Append(input.AsSpan(copied, range.Index - copied));
        copied = range.Index + range.Length;
    }
    sb.Append(input.AsSpan(copied));
    return sb.ToString();
}

static int CountSegments(RegExp re, string input)
{
    Span<CaptureRange> captures = stackalloc CaptureRange[Math.Max(re.RequiredCaptureCount, 4)];
    int segments = 1;
    int cursor = 0;
    while (cursor <= input.Length)
    {
        if (!re.TryMatchAt(input.AsSpan(), cursor, captures, out var match))
        {
            cursor++;
            continue;
        }
        cursor = match.Length == 0 ? cursor + 1 : match.End;
        segments++;
    }
    return segments;
}

static void RunBclCase(string name, BclRegex re, string[] strings, int inputs)
{
    switch (name)
    {
        case "LiteralGlobalMatch":
        case "CaptureGlobalMatch":
            for (var s = 0; s < inputs; s++)
            {
                var m = re.Match(strings[s]);
                while (m.Success)
                    m = m.NextMatch();
            }
            break;
        case "LiteralGlobalTest":
        case "VariableStarMatch":
            for (var s = 0; s < inputs; s++)
                _ = re.IsMatch(strings[s]);
            break;
        case "LiteralReplaceEmpty":
            for (var s = 0; s < inputs; s++)
                ProbeState.Sink += re.Replace(strings[s], "").Length;
            break;
        case "SplitEmpty":
        case "SplitChar":
        case "SplitStar":
            for (var s = 0; s < inputs; s++)
                ProbeState.Sink += re.Split(strings[s]).Length;
            break;
        default:
            throw new InvalidOperationException($"Unknown case '{name}'.");
    }
}

static string[] GenerateTestStrings(int count)
{
    var rng = Random.Shared;
    var random = new char[16384];
    for (var i = 0; i < random.Length; i++)
        random[i] = (char)((25 * rng.NextDouble()) + 97);
    var baseString = new string(random);
    baseString += baseString;
    baseString += baseString;

    var result = new string[count];
    for (var i = 0; i < count; i++)
    {
        var c = (char)((25 * rng.NextDouble()) + 97);
        result[i] = c + baseString + c;
    }
    return result;
}

static string LoadScenario(string name)
{
    var path = Path.Combine(AppContext.BaseDirectory, "scripts", name + ".js");
    if (!File.Exists(path))
        path = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "benchmarks",
                "Okojo.Benchmarks",
                "scripts",
                name + ".js"
            )
        );
    return File.ReadAllText(path);
}

static int GetOption(string[] args, string name, int fallback)
{
    for (var i = 1; i < args.Length - 1; i++)
        if (args[i] == name && int.TryParse(args[i + 1], out var value))
            return value;
    return fallback;
}

static class ProbeState
{
    internal static int Sink;
}
