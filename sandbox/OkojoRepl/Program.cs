using System.Diagnostics;
using ConsoleAppFramework;
using Okojo;
using Okojo.Compiler;
using Okojo.Diagnostics;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Reflection;
using Okojo.RegExp;
using Okojo.Runtime;
using OkojoRepl;
using PrettyPrompt;
using PrettyPrompt.Highlighting;

var cli = CliArgumentParser.Parse(args);
if (cli is null)
    return;

var vm = JsRuntime.Create(options =>
{
    options.AllowClrAccess()
        .AddClrAssembly(typeof(Console).Assembly)
        .AddClrAssembly(typeof(int).Assembly)
        .AddClrAssembly(typeof(Enumerable).Assembly).UseRegExpEngine(RegExpEngine.Default);
}).DefaultRealm;
InstallConsole(vm);

var topLevelLexicalNames = new HashSet<string>(StringComparer.Ordinal);
var topLevelConstNames = new HashSet<string>(StringComparer.Ordinal);
var compileContext = new JsCompilerContext
{
    IsRepl = true,
    ReplTopLevelLexicalNames = topLevelLexicalNames,
    ReplTopLevelConstNames = topLevelConstNames
};

if (cli.Expressions.Count != 0)
{
    foreach (var expr in cli.Expressions)
        try
        {
            await ExecuteAndPrintAsync(vm, compileContext, topLevelLexicalNames, topLevelConstNames, expr,
                cli.StrictMode);
        }
        catch (JsRuntimeException runtimeException)
        {
            Console.WriteLine($"{runtimeException.Kind}: {runtimeException.Message}");
            Console.WriteLine(runtimeException.FormatOkojoStackTrace());
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            Environment.ExitCode = 1;
        }

    return;
}

Console.WriteLine("Okojo REPL");
Console.WriteLine("Type :help for commands.");
Console.WriteLine("Shift+Enter for multi-line input.");
Console.WriteLine(cli.StrictMode switch
{
    ReplStrictMode.Strict => "Mode: strict",
    ReplStrictMode.Sloppy => "Mode: sloppy",
    _ => "Mode: auto"
});
Console.WriteLine(cli.NoSuggestions ? "Suggestions: off" : "Suggestions: on");

var keyBindings = new KeyBindings();
var promptConfiguration = CreatePromptConfiguration(keyBindings);
var prompt = new Prompt(
    callbacks: new OkojoReplPromptCallbacks(keyBindings, !cli.NoSuggestions),
    configuration: promptConfiguration);

while (true)
{
    var response = await prompt.ReadLineAsync();
    if (!response.IsSuccess)
        break;

    var line = response.Text;
    if (line is null)
        break;
    if (string.IsNullOrWhiteSpace(line))
        continue;

    var trimmed = line.Trim();
    if (trimmed is ":q" or ":quit" or ":exit")
        break;
    if (trimmed is ":help")
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  :help                 Show help");
        Console.WriteLine("  -e, --eval <code>     Batch eval (CLI option)");
        Console.WriteLine("  --strict              Force strict mode (CLI option)");
        Console.WriteLine("  --no-strict           Force sloppy mode (CLI option)");
        Console.WriteLine("  --no-suggestions      Disable completion suggestions");
        Console.WriteLine("  :q, :quit, :exit      Exit");
        continue;
    }

    try
    {
        await ExecuteAndPrintAsync(vm, compileContext, topLevelLexicalNames, topLevelConstNames, line, cli.StrictMode);
    }
    catch (JsRuntimeException runtimeException)
    {
        Console.WriteLine($"{runtimeException.Kind}: {runtimeException.Message}");
        Console.WriteLine(runtimeException.FormatOkojoStackTrace());
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{ex.GetType().Name}: {ex.Message}");
    }
}

static async Task ExecuteAndPrintAsync(
    JsRealm vm,
    JsCompilerContext compileContext,
    HashSet<string> topLevelLexicalNames,
    HashSet<string> topLevelConstNames,
    string source,
    int strictMode)
{
    var adjustedSource = ApplyStrictMode(source, strictMode);
    var program = JavaScriptParser.ParseScript(adjustedSource);
    ValidateReplTopLevelLexicalRedeclaration(program, topLevelLexicalNames);

    var compiler = new JsCompiler(vm, compileContext);
    var script = compiler.Compile(program);
    vm.Execute(script);
    RegisterTopLevelLexicalDeclarations(program, topLevelLexicalNames, topLevelConstNames);

    var result = await AwaitIfPromiseAsync(vm, vm.Accumulator);
    if (!result.IsUndefined)
        Console.WriteLine(new ReplFormatter(vm, 2).Format(result));
}

static string ApplyStrictMode(string source, int strictMode)
{
    return strictMode switch
    {
        ReplStrictMode.Strict => "'use strict';\n" + source,
        ReplStrictMode.Sloppy => "void 0;\n" + source,
        _ => source
    };
}

static async Task<JsValue> AwaitIfPromiseAsync(JsRealm vm, JsValue value, int timeoutMs = 30000)
{
    if (!value.TryGetObject(out var obj) || obj is not JsPromiseObject promise)
        return value;

    var sw = Stopwatch.StartNew();
    while (promise.IsPending)
    {
        vm.PumpJobs();
        if (!promise.IsPending)
            break;
        if (sw.ElapsedMilliseconds > timeoutMs)
            throw new TimeoutException("Timed out waiting for Promise settlement.");
        await Task.Delay(1);
    }

    if (promise.IsRejected)
        throw new InvalidOperationException($"UnhandledPromiseRejection: {promise.SettledResult}");

    return promise.SettledResult;
}

static void InstallConsole(JsRealm vm)
{
    var console = new JsPlainObject(vm);
    var log = new JsHostFunction(vm, static (in info) =>
    {
        var realm = info.Realm;
        var args = info.Arguments;
        if (args.Length == 0)
        {
            Console.WriteLine();
            return JsValue.Undefined;
        }

        var parts = new string[args.Length];
        for (var i = 0; i < args.Length; i++)
            parts[i] = new ReplFormatter(realm).Format(args[i]);
        Console.WriteLine(string.Join(" ", parts));
        return JsValue.Undefined;
    }, "log", 1);

    console.SetProperty("log", JsValue.FromObject(log));
    vm.Global["console"] = JsValue.FromObject(console);
}

static PromptConfiguration CreatePromptConfiguration(KeyBindings keyBindings)
{
    return new(
        keyBindings,
        new FormattedString("> "));
}

static void ValidateReplTopLevelLexicalRedeclaration(JsProgram program, HashSet<string> existingLexicalNames)
{
    foreach (var name in EnumerateTopLevelLexicalNames(program))
        if (existingLexicalNames.Contains(name))
            throw new InvalidOperationException($"SyntaxError: Identifier '{name}' has already been declared");
}

static void RegisterTopLevelLexicalDeclarations(
    JsProgram program,
    HashSet<string> lexicalNames,
    HashSet<string> constNames)
{
    foreach (var stmt in program.Statements)
    {
        if (stmt is not JsVariableDeclarationStatement decl)
            continue;
        if (decl.Kind is not (JsVariableDeclarationKind.Let or JsVariableDeclarationKind.Const))
            continue;

        foreach (var d in decl.Declarators)
        {
            lexicalNames.Add(d.Name);
            if (decl.Kind == JsVariableDeclarationKind.Const)
                constNames.Add(d.Name);
        }
    }
}

static IEnumerable<string> EnumerateTopLevelLexicalNames(JsProgram program)
{
    foreach (var stmt in program.Statements)
    {
        if (stmt is not JsVariableDeclarationStatement decl)
            continue;
        if (decl.Kind is not (JsVariableDeclarationKind.Let or JsVariableDeclarationKind.Const))
            continue;

        foreach (var d in decl.Declarators)
            yield return d.Name;
    }
}

internal sealed record CliOptions(IReadOnlyList<string> Expressions, int StrictMode, bool NoSuggestions)
{
    internal static CliOptions Default { get; } = new(Array.Empty<string>(), ReplStrictMode.Auto, false);
}

internal sealed class CliArgumentParser
{
    private CliOptions? parsed;

    internal static CliOptions? Parse(string[] args)
    {
        if (args.Length == 0)
            return CliOptions.Default;

        var parser = new CliArgumentParser();
        ConsoleApp.Run(args, parser.ParseCore);
        return parser.parsed;
    }

    /// <param name="eval">-e, Evaluate code and exit. Can be repeated.</param>
    /// <param name="strict">Force strict mode for input.</param>
    /// <param name="noStrict">Force sloppy mode for input.</param>
    /// <param name="noSuggestions">Disable PrettyPrompt suggestions and completion popups.</param>
    public void ParseCore(string[]? eval = null, bool strict = false, bool noStrict = false, bool noSuggestions = false)
    {
        if (strict && noStrict)
            throw new InvalidOperationException("Cannot combine --strict and --no-strict");

        parsed = new(
            eval ?? Array.Empty<string>(),
            strict ? ReplStrictMode.Strict : noStrict ? ReplStrictMode.Sloppy : ReplStrictMode.Auto,
            noSuggestions);
    }
}

internal static class ReplStrictMode
{
    internal const int Auto = 0;
    internal const int Strict = 1;
    internal const int Sloppy = 2;
}
