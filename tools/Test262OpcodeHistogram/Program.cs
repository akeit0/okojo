// R7: test262-wide opcode histogram.
//
// Compiles every .js file under the test262 root in-process and aggregates
// opcode frequencies, adjacent-pair (bigram) frequencies, and coverage vs the
// JsOpCode enum. Compilation-only: runtime behavior is irrelevant, so
// negative tests and harness-less files are fine as long as they parse.
//
// Usage:
//   dotnet Test262OpcodeHistogram.dll [root] [--category name] [--top N] [--out file]

using Okojo.JavaScript;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

var root = "test262/test";
var category = "";
var top = 40;
var outFile = "";

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--category" when i + 1 < args.Length:
            category = args[++i];
            break;
        case "--top" when i + 1 < args.Length:
            top = int.Parse(args[++i]);
            break;
        case "--out" when i + 1 < args.Length:
            outFile = args[++i];
            break;
        default:
            root = args[i];
            break;
    }
}

if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"Root not found: {root}");
    return 1;
}

IEnumerable<string> files = Directory.EnumerateFiles(root, "*.js", SearchOption.AllDirectories);
if (!string.IsNullOrEmpty(category))
{
    var prefix = Path.Combine(root, category) + Path.DirectorySeparatorChar;
    files = files.Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}

var opCounts = new Dictionary<JsOpCode, long>();
var bigramCounts = new Dictionary<(JsOpCode Prev, JsOpCode Cur), long>();
var units = 0L;
var instructions = 0L;

long parsed = 0,
    compiled = 0,
    moduleSkipped = 0,
    negativeSyntaxSkipped = 0,
    compileUnsupported = 0,
    otherErrors = 0;
var firstUnexpectedErrors = new List<string>();

using var runtime = JsRuntime.CreateBuilder().Build();
var realm = runtime.DefaultRealm;

var allFiles = files.OrderBy(static f => f, StringComparer.OrdinalIgnoreCase).ToList();
Console.WriteLine($"files discovered: {allFiles.Count}");

var nextReport = 2000;

foreach (var file in allFiles)
{
    string text;
    try
    {
        text = File.ReadAllText(file);
    }
    catch
    {
        otherErrors++;
        continue;
    }

    if (
        text.Contains("flags: --module", StringComparison.OrdinalIgnoreCase)
        || text.Contains("flags: --module,", StringComparison.OrdinalIgnoreCase)
    )
    {
        moduleSkipped++;
        continue;
    }

    try
    {
        var program = JavaScriptParser.ParseScript(text);
        var script = JsCompiler.Compile(realm, program);
        compiled++;
        foreach (var (op, cur) in WalkUnits(script))
        {
            if (cur is { } current)
            {
                opCounts[current] = opCounts.GetValueOrDefault(current) + 1;
                instructions++;
            }

            if (op is { } prev && cur is { } current2)
            {
                var key = (prev, current2);
                bigramCounts[key] = bigramCounts.GetValueOrDefault(key) + 1;
            }
        }
    }
    catch (Exception ex) when (ex.Message.Contains("Line", StringComparison.Ordinal))
    {
        // Parse errors carry line info; test262 syntax-negative files land here.
        if (text.Contains("negative:", StringComparison.OrdinalIgnoreCase))
            negativeSyntaxSkipped++;
        else
            RecordUnexpected(file, ex);
    }
    catch (Exception ex)
    {
        if (text.Contains("negative:", StringComparison.OrdinalIgnoreCase))
            negativeSyntaxSkipped++;
        else
            RecordUnexpected(file, ex);
    }

    parsed++;
    if (parsed >= nextReport)
    {
        Console.WriteLine($"  processed {parsed}/{allFiles.Count} (compiled={compiled})");
        nextReport += 2000;
    }
}

void RecordUnexpected(string file, Exception ex)
{
    compileUnsupported++;
    if (firstUnexpectedErrors.Count < 15)
        firstUnexpectedErrors.Add($"{file}: {ex.GetType().Name}: {Truncate(ex.Message)}");

    static string Truncate(string s) => s.Length <= 90 ? s : s[..90] + "...";
}

IEnumerable<(JsOpCode? Prev, JsOpCode? Cur)> WalkUnits(JsScript rootScript)
{
    var stack = new Stack<JsScript>();
    var seen = new HashSet<JsScript>();
    stack.Push(rootScript);

    while (stack.Count > 0)
    {
        var script = stack.Pop();
        if (!seen.Add(script))
            continue;

        units++;
        var prev = default(JsOpCode?);
        var pc = 0;
        var code = script.Bytecode;
        while (pc < code.Length)
        {
            if (
                !BytecodeInfo.TryDecodeInstructionHeader(
                    code,
                    pc,
                    out var op,
                    out _,
                    out _,
                    out var operandBytes,
                    out var length
                )
            )
                break;

            yield return (prev, op);
            prev = op;
            pc += length;
        }

        foreach (var obj in script.ObjectConstants)
            if (obj is JsBytecodeFunction fn)
                stack.Push(fn.Script);
    }
}

Console.WriteLine();
Console.WriteLine("== coverage ==");
Console.WriteLine(
    $"files={allFiles.Count} parsedOk={parsed} compiled={compiled} "
        + $"moduleSkipped={moduleSkipped} negativeSyntaxSkipped={negativeSyntaxSkipped} "
        + $"compileUnsupported={compileUnsupported} otherErrors={otherErrors}"
);
Console.WriteLine($"units={units} instructions={instructions}");

Console.WriteLine();
Console.WriteLine($"== opcode frequencies (top {top}) ==");
foreach (var (op, count) in opCounts.OrderByDescending(kv => kv.Value).Take(top))
    Console.WriteLine($"{count, 9}  {count / (double)instructions, 7:P2}  {op}");

var definedCount = Enum.GetValues<JsOpCode>().Length;
var dead = Enum.GetValues<JsOpCode>()
    .Where(op => !opCounts.ContainsKey(op))
    .Select(op => op.ToString())
    .ToList();
Console.WriteLine();
Console.WriteLine(
    $"distinct emitted: {opCounts.Count}/{definedCount}   dead ({dead.Count}): {string.Join(", ", dead)}"
);

Console.WriteLine();
Console.WriteLine($"== bigrams (top {top}) ==");
foreach (var ((prev, cur), count) in bigramCounts.OrderByDescending(kv => kv.Value).Take(top))
    Console.WriteLine($"{count, 9}  {prev} -> {cur}");

Console.WriteLine();
Console.WriteLine(
    $"back-edge ops: Jump={opCounts.GetValueOrDefault(JsOpCode.Jump)} JumpLoop={opCounts.GetValueOrDefault(JsOpCode.JumpLoop)}"
);

if (firstUnexpectedErrors.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("== sample unexpected compile errors ==");
    foreach (var line in firstUnexpectedErrors)
        Console.WriteLine("  " + line);
}

if (!string.IsNullOrEmpty(outFile))
{
    using var w = new StreamWriter(outFile);
    w.WriteLine("opcode,count,share");
    foreach (var (op, count) in opCounts.OrderByDescending(kv => kv.Value))
        w.WriteLine($"{op},{count},{count / (double)instructions:P4}");
    w.WriteLine();
    w.WriteLine("bigram,count");
    foreach (var ((p, c), count) in bigramCounts.OrderByDescending(kv => kv.Value))
        w.WriteLine($"{p}->{c},{count}");
}
return 0;
