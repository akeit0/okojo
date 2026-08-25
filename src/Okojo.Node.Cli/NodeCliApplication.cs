using System.Diagnostics;
using System.Reflection;
using Okojo.Diagnostics;
using Okojo.Hosting;
using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;
using Okojo.Repl;

namespace Okojo.Node.Cli;

internal static class NodeCliApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        NodeCliOptions cli;
        try
        {
            cli = NodeCliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (cli.ShowHelp)
        {
            WriteHelp();
            return 0;
        }

        if (cli.IsInspectEnabled && cli.ScriptPath is null && cli.Expressions.Count == 0)
        {
            Console.Error.WriteLine(
                "inspect mode currently requires a script path or -e/--eval input."
            );
            return 1;
        }

        try
        {
            if (cli.EnvFilePath is not null)
                NodeCliEnvironmentFileLoader.LoadIntoProcess(cli.EnvFilePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var hostLoop = new ManualHostEventLoop(TimeProvider.System);
        using var runtime = NodeCliRuntimeFactory.CreateRuntime(hostLoop, cli.EnableSourceMaps);
        using var debugger = NodeCliDebuggerHost.TryCreate(runtime, cli);

        try
        {
            if (cli.ScriptPath is not null)
                RunScript(runtime, hostLoop, cli, debugger);
            else if (cli.Expressions.Count != 0)
                await RunEvalAsync(runtime, hostLoop, cli, debugger);
            else
                await RunReplAsync(runtime, hostLoop, cli.StrictMode, cli.PrintBytecode);

            debugger?.PublishTerminated(0);
            return 0;
        }
        catch (JsRuntimeException runtimeException)
        {
            if (debugger is not null)
            {
                debugger.PublishError(runtimeException);
                debugger.PublishTerminated(1);
            }
            else
            {
                WriteRuntimeException(runtimeException);
            }

            return 1;
        }
        catch (Exception ex)
        {
            if (debugger is not null)
            {
                debugger.PublishError(ex);
                debugger.PublishTerminated(1);
            }
            else
            {
                Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            }

            return 1;
        }
    }

    private static void RunScript(
        NodeRuntime runtime,
        IHostTaskQueuePump hostLoop,
        NodeCliOptions cli,
        NodeCliDebuggerHost? debugger
    )
    {
        var scriptPath = Path.GetFullPath(cli.ScriptPath!);
        var preparedScriptPath = scriptPath;
        var previousDirectory = Environment.CurrentDirectory;
        try
        {
            if (debugger is not null && debugger.ShouldStopOnEntry)
            {
                preparedScriptPath = runtime.PrepareMainModuleForDebugging(
                    scriptPath,
                    cli.ScriptArguments.ToArray()
                );
                if (!debugger.PublishEntryStopped(preparedScriptPath))
                    return;
            }

            Environment.CurrentDirectory = Path.GetDirectoryName(scriptPath)!;
            runtime.RunMainModule(scriptPath, cli.ScriptArguments.ToArray());
            PumpHostEventLoopUntilIdle(runtime, hostLoop);
        }
        finally
        {
            if (cli.PrintBytecode)
                NodeCliBytecodePrinter.PrintRegisteredScripts(
                    runtime.Runtime.MainAgent,
                    preparedScriptPath
                );

            Environment.CurrentDirectory = previousDirectory;
        }
    }

    private static void PumpHostEventLoopUntilIdle(
        NodeRuntime runtime,
        IHostTaskQueuePump hostLoop,
        int maxIdleTurns = 3
    )
    {
        var pump = runtime.Runtime.CreateHostPump();
        var idleTurns = 0;
        while (idleTurns < maxIdleTurns)
        {
            var moved = HostTurnRunner.RunTurn(hostLoop, pump);
            if (moved)
            {
                idleTurns = 0;
                continue;
            }

            if (hostLoop is IHostEventLoopDiagnostics diagnostics)
            {
                var snapshot = diagnostics.GetSnapshot();
                if (snapshot.PendingDelayedCount != 0)
                {
                    var wait = snapshot.NextDelayedDueAt is { } nextDueAt
                        ? nextDueAt - DateTimeOffset.UtcNow
                        : TimeSpan.FromMilliseconds(10);
                    if (wait > TimeSpan.Zero)
                        Thread.Sleep(
                            wait > TimeSpan.FromMilliseconds(25)
                                ? TimeSpan.FromMilliseconds(25)
                                : wait
                        );
                    continue;
                }
            }

            idleTurns++;
        }
    }

    private static async Task RunEvalAsync(
        NodeRuntime runtime,
        IHostTaskQueuePump hostLoop,
        NodeCliOptions cli,
        NodeCliDebuggerHost? debugger
    )
    {
        if (cli.ScriptArguments.Count != 0)
            SetEvalArgv(runtime.MainRealm, cli.ScriptArguments);

        var topLevelLexicalNames = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < cli.Expressions.Count; i++)
        {
            var evalSourcePath = Path.GetFullPath($"[eval-{i + 1}].js");
            await ExecuteAndMaybePrintAsync(
                runtime.MainRealm,
                hostLoop,
                topLevelLexicalNames,
                cli.Expressions[i],
                cli.StrictMode,
                cli.PrintEvalResult ? EvalPrintMode.Always : EvalPrintMode.Never,
                cli.PrintBytecode,
                evalSourcePath,
                debugger,
                false
            );
        }

        PumpHostEventLoopUntilIdle(runtime, hostLoop);
    }

    private static async Task RunReplAsync(
        NodeRuntime runtime,
        IHostTaskQueuePump hostLoop,
        int strictMode,
        bool printBytecode
    )
    {
        var realm = runtime.MainRealm;
        using var history = ReplHistoryStore.Load(GetReplHistoryPath());
        var hostPump = runtime.Runtime.CreateHostPump();
        var evaluator = new NodeReplEvaluator(
            realm,
            () => HostTurnRunner.RunTurn(hostLoop, hostPump)
        );

        Console.WriteLine($"Welcome to okojonode {GetOkojonodeVersion()}.");
        Console.WriteLine("Type \".help\" for more information.");
        var replEvaluationIndex = 0;
        await SystemReplLoop
            .RunAsync(
                new()
                {
                    History = history,
                    IsInputComplete = input => ReplInputParser.IsInputComplete(input, true),
                    PumpTurn = () => HostTurnRunner.RunTurn(hostLoop, hostPump),
                    HandleInputAsync = line =>
                        TryHandleReplLineAsync(
                            evaluator,
                            line,
                            strictMode,
                            printBytecode,
                            ++replEvaluationIndex
                        ),
                }
            )
            .ConfigureAwait(false);
    }

    private static async Task<bool> TryHandleReplLineAsync(
        NodeReplEvaluator evaluator,
        string line,
        int strictMode,
        bool printBytecode,
        int replEvaluationIndex
    )
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;

        var trimmed = line.Trim();
        switch (trimmed)
        {
            case ".exit":
                return false;
            case ".help":
                WriteReplHelp();
                return true;
        }

        try
        {
            var sourcePath = $"REPL{replEvaluationIndex}";
            var result = await evaluator
                .EvaluateAsync(
                    line,
                    strictMode,
                    sourcePath,
                    onCompiled: script =>
                    {
                        if (printBytecode)
                            NodeCliBytecodePrinter.PrintCompiledScript(script, sourcePath);
                    }
                )
                .ConfigureAwait(false);
            Console.WriteLine(new ReplFormatter(evaluator.Realm, 2).Format(result));
        }
        catch (JsRuntimeException runtimeException)
        {
            WriteReplRuntimeException(evaluator.Realm, runtimeException);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
        }

        return true;
    }

    private static async Task ExecuteAndMaybePrintAsync(
        JsRealm realm,
        IHostTaskQueuePump hostLoop,
        HashSet<string> topLevelLexicalNames,
        string source,
        int strictMode,
        EvalPrintMode printMode,
        bool printBytecode,
        string sourcePath,
        NodeCliDebuggerHost? debugger,
        bool awaitPromiseResult
    )
    {
        var adjustedSource = ApplyStrictMode(source, strictMode);
        using var ast = FlatJavaScriptParser.ParseScript(
            adjustedSource,
            sourcePath,
            allowTopLevelAwait: true
        );
        ValidateReplTopLevelLexicalRedeclaration(ast, topLevelLexicalNames);

        var script = new JsScriptCompiler(realm).Compile(ast, sourcePath);
        if (printBytecode)
            NodeCliBytecodePrinter.PrintCompiledScript(script, sourcePath);

        if (
            debugger is not null
            && debugger.ShouldStopOnEntry
            && !debugger.PublishEntryStopped(sourcePath)
        )
            return;

        realm.Execute(script);
        var rawResult = realm.Accumulator;

        RegisterTopLevelLexicalDeclarations(ast, topLevelLexicalNames);

        var result =
            awaitPromiseResult || ast.HasTopLevelAwait
                ? await AwaitIfPromiseAsync(realm, hostLoop, rawResult)
                : rawResult;
        switch (printMode)
        {
            case EvalPrintMode.Always:
                Console.WriteLine(new ReplFormatter(realm, 2).Format(result));
                break;
            case EvalPrintMode.IfNotUndefined when !result.IsUndefined:
                Console.WriteLine(new ReplFormatter(realm, 2).Format(result));
                break;
        }
    }

    private static string ApplyStrictMode(string source, int strictMode)
    {
        return strictMode switch
        {
            ReplStrictMode.Strict => "'use strict';\n" + source,
            ReplStrictMode.Sloppy => "void 0;\n" + source,
            _ => source,
        };
    }

    private static async Task<JsValue> AwaitIfPromiseAsync(
        JsRealm realm,
        IHostTaskQueuePump hostLoop,
        JsValue value,
        int timeoutMs = 30000
    )
    {
        if (!value.TryGetObject(out var obj) || obj is not JsPromiseObject promise)
            return value;

        var hostPump = new HostPump(realm.Agent);
        var sw = Stopwatch.StartNew();
        while (promise.IsPending)
        {
            _ = HostTurnRunner.RunTurn(hostLoop, hostPump);
            realm.PumpJobs();
            if (!promise.IsPending)
                break;
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Timed out waiting for Promise settlement.");
            await Task.Delay(1);
        }

        if (promise.IsRejected)
            throw new InvalidOperationException(
                $"UnhandledPromiseRejection: {promise.SettledResult}"
            );

        return promise.SettledResult;
    }

    private static void ValidateReplTopLevelLexicalRedeclaration(
        FlatAst ast,
        HashSet<string> existingLexicalNames
    )
    {
        foreach (var name in EnumerateTopLevelLexicalNames(ast))
            if (existingLexicalNames.Contains(name))
                throw new InvalidOperationException(
                    $"SyntaxError: Identifier '{name}' has already been declared"
                );
    }

    private static void RegisterTopLevelLexicalDeclarations(
        FlatAst ast,
        HashSet<string> lexicalNames
    )
    {
        foreach (var name in EnumerateTopLevelLexicalNames(ast))
            lexicalNames.Add(name);
    }

    private static List<string> EnumerateTopLevelLexicalNames(FlatAst ast)
    {
        var names = new List<string>();
        ref readonly var root = ref ast[ast.Root];
        var statements = ast.ChildRange(root.Arg0, root.Arg1);
        for (var i = 0; i < statements.Length; i++)
        {
            ref readonly var statement = ref ast[statements[i]];
            if (statement.Kind != AstKind.VariableDeclaration)
                continue;
            if (
                (JsVariableDeclarationKind)statement.Arg2
                is not (JsVariableDeclarationKind.Let or JsVariableDeclarationKind.Const)
            )
                continue;

            var declarators = ast.ChildRange(statement.Arg0, statement.Arg1);
            for (var j = 0; j < declarators.Length; j++)
            {
                ref readonly var declarator = ref ast[declarators[j]];
                if (declarator.Kind != AstKind.VariableDeclarator)
                    continue;
                names.Add(ast.GetString(declarator.Arg0));
            }
        }
        return names;
    }

    private static string GetOkojonodeVersion()
    {
        var assembly = typeof(NodeCliApplication).Assembly;
        var informationalVersion = assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion!;

        return assembly.GetName().Version?.ToString() ?? "0.1.0-local";
    }

    private static string GetReplHistoryPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("OKOJO_NODE_REPL_HISTORY_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Path.GetFullPath(overridePath);

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OkojoNode"
        );
        return Path.Combine(root, "repl-history.json");
    }

    private static void SetEvalArgv(JsRealm realm, IReadOnlyList<string> extraArgs)
    {
        if (
            !realm.GlobalObject.TryGetProperty("process", out var processValue)
            || !processValue.TryGetObject(out var processObject)
        )
            return;

        var argv = new JsArray(realm);
        argv[0] = JsValue.FromString("okojonode");
        for (var i = 0; i < extraArgs.Count; i++)
            argv[(uint)(i + 1)] = JsValue.FromString(extraArgs[i]);

        processObject.SetProperty("argv", JsValue.FromObject(argv));
    }

    private static void WriteRuntimeException(JsRuntimeException runtimeException)
    {
        Console.Error.WriteLine($"{runtimeException.Kind}: {runtimeException.Message}");
        Console.Error.WriteLine(runtimeException.FormatOkojoStackTrace());
    }

    private static void WriteReplRuntimeException(
        JsRealm realm,
        JsRuntimeException runtimeException
    )
    {
        Console.Error.WriteLine(FormatReplExceptionSummary(realm, runtimeException));

        foreach (var frame in EnumerateReplFrames(runtimeException))
            Console.Error.WriteLine($"    at {FormatReplFrame(frame)}");
    }

    private static string FormatReplExceptionSummary(
        JsRealm realm,
        JsRuntimeException runtimeException
    )
    {
        if (runtimeException.ThrownValue is { } thrownValue)
            return $"Uncaught {FormatThrownValueForRepl(realm, thrownValue)}";

        return $"Uncaught {runtimeException.Kind}: {runtimeException.Message}";
    }

    private static string FormatThrownValueForRepl(JsRealm realm, in JsValue thrownValue)
    {
        if (!thrownValue.TryGetObject(out var obj))
            return new ReplFormatter(realm, 2).Format(thrownValue);

        string? name = null;
        if (obj.TryGetProperty("name", out var nameValue) && !nameValue.IsUndefined)
            name = nameValue.IsString ? nameValue.AsString() : nameValue.ToString();

        string? message = null;
        if (obj.TryGetProperty("message", out var messageValue) && !messageValue.IsUndefined)
            message = messageValue.IsString ? messageValue.AsString() : messageValue.ToString();

        if (!string.IsNullOrWhiteSpace(name))
            return string.IsNullOrEmpty(message) ? name : $"{name}: {message}";

        return new ReplFormatter(realm, 2).Format(thrownValue);
    }

    private static IEnumerable<StackFrameInfo> EnumerateReplFrames(
        JsRuntimeException runtimeException
    )
    {
        foreach (var frame in runtimeException.StackFrames)
        {
            if (
                frame.FrameKind == CallFrameKind.ScriptFrame
                && string.Equals(frame.FunctionName, "root", StringComparison.Ordinal)
            )
                continue;

            yield return frame;
        }
    }

    private static string FormatReplFrame(in StackFrameInfo frame)
    {
        if (!frame.HasSourceLocation)
            return frame.FunctionName;

        var location = frame.SourcePath is { Length: > 0 }
            ? $"{frame.SourcePath}:{frame.SourceLine}:{frame.SourceColumn}"
            : $"{frame.SourceLine}:{frame.SourceColumn}";

        return string.IsNullOrWhiteSpace(frame.FunctionName)
            ? location
            : $"{frame.FunctionName} ({location})";
    }

    private static void WriteHelp()
    {
        Console.WriteLine(
            """
            Usage:
              okojonode <script> [arguments]
              okojonode inspect <script> [arguments]
              okojonode -e <code> [-e <code> ...] [arguments]
              okojonode -p <code>
              okojonode -p -e <code>
              okojonode

            Options:
              inspect <script>         Start the interactive debugger and stop on entry.
              -e, --eval <code>        Evaluate code and exit. Can be repeated.
              -p, --print [code]       Evaluate and print the result. Can be combined with -e.
              --strict                 Force strict mode for eval and REPL input.
              --no-strict              Force sloppy mode for eval and REPL input.
              --env-file [path]        Load environment variables from a .env-style file.
              --enable-source-maps     Remap stack traces and inspect locations through source maps.
              --inspect                Attach the interactive debugger.
              --inspect-brk            Attach the interactive debugger and stop on entry.
              --print-bytecode         Print Okojo bytecode disassembly for eval input or loaded script units.
              -h, --help               Show help.
            """
        );
    }

    private static void WriteReplHelp()
    {
        Console.WriteLine(
            """
            Commands:
              .help                    Show REPL help
              .exit                    Exit the REPL

            Notes:
              Shift+Enter inserts a new line.
              Auto completion suggestions are currently disabled.
              The REPL runs with Okojo.Node globals like console, process, Buffer, and performance.
            """
        );
    }

    private enum EvalPrintMode
    {
        Never = 0,
        IfNotUndefined = 1,
        Always = 2,
    }
}
