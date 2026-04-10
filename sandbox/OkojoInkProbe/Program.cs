using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Okojo;
using Okojo.Bytecode;
using Okojo.Diagnostics;
using Okojo.Node;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;
using Okojo.WebAssembly.Wasmtime;

var options = ParseArguments(args);
var entryPath = Path.IsPathRooted(options.EntryArgument)
    ? options.EntryArgument
    : Path.Combine(options.AppRoot, options.EntryArgument);

Console.WriteLine("OkojoInkProbe");
Console.WriteLine($"appRoot: {options.AppRoot}");
Console.WriteLine($"entry: {entryPath}");
if (options.EnableDebugger && options.DebuggerLogPath is not null)
    Console.WriteLine($"debug: caught-exception -> {options.DebuggerLogPath}");
Console.WriteLine();

var stdout = new StringWriter();
var stderr = new StringWriter();
using var debuggerLog = options.EnableDebugger
    ? CreateDebuggerLogWriter(options)
    : null;

try
{
    var previous = Environment.CurrentDirectory;
    Environment.CurrentDirectory = options.AppRoot;
    try
    {
        var runtimeBuilder = NodeRuntime.CreateBuilder();
        if (debuggerLog is not null)
            runtimeBuilder.ConfigureRuntime(builder => builder.UseAgent(agent =>
            {
                agent.DebuggerSession = new InkProbeDebuggerSession(debuggerLog);
                agent.EnableCaughtExceptionHook();
            }));

        using var runtime = runtimeBuilder
            .UseWebAssembly(wasm => wasm
                .UseBackend(static () => new WasmtimeBackend())
                .InstallGlobals())
            .ConfigureTerminal(options =>
            {
                options.Stdout = stdout;
                options.Stderr = stderr;
                options.StdoutIsTty = true;
                options.StderrIsTty = true;
                options.StdoutColumns = 120;
                options.StdoutRows = 40;
                options.StderrColumns = 120;
                options.StderrRows = 40;
            })
            .Build();

        runtime.MainRealm.UnhandledRejection += value =>
        {
            Console.WriteLine($"unhandled rejection: {value}");
            if (TryGetInnermostNativeException(value, out var nativeException))
            {
                Console.WriteLine(
                    $"native exception: {nativeException.GetType().FullName ?? nativeException.GetType().Name}");
                Console.WriteLine(nativeException.ToString());
            }
        };

        var result = runtime.RunMainModule(entryPath);
        runtime.MainRealm.PumpJobs();
        Console.WriteLine($"result: {result}");
        if (TryGetDefaultExport(result, out var defaultExport))
            Console.WriteLine($"default: {defaultExport}");
    }
    finally
    {
        Environment.CurrentDirectory = previous;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"exception: {ex.GetType().Name}");
    Console.WriteLine(ex.ToString());
    var innerException = ex.InnerException;
    while (innerException is not null)
    {
        Console.WriteLine($"inner exception: {innerException.GetType().Name}");
        Console.WriteLine(innerException.ToString());
        innerException = innerException.InnerException;
    }

    if (ex is JsRuntimeException jsEx)
        Console.WriteLine(jsEx.FormatOkojoStackTrace());
}

Console.WriteLine();
Console.WriteLine($"captured stdout: {EscapeControl(stdout.ToString())}");
Console.WriteLine($"captured stderr: {EscapeControl(stderr.ToString())}");

static string EscapeControl(string text)
{
    return text
        .Replace("\u001b", "\\u001b", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}

static ProbeOptions ParseArguments(
    string[] args,
    [CallerFilePath] string callerFilePath = "")
{
    var appName = "app";
    var entryArgument = "main.mjs";
    var enableDebugger = false;
    string? debuggerLogPath = null;

    for (var i = 0; i < args.Length; i++)
    {
        if (string.Equals(args[i], "--app", StringComparison.Ordinal))
        {
            if (i + 1 >= args.Length)
                throw new ArgumentException("Missing value after --app.");

            appName = args[++i];
            continue;
        }

        if (string.Equals(args[i], "--debugger", StringComparison.Ordinal))
        {
            enableDebugger = true;
            continue;
        }

        if (string.Equals(args[i], "--debug-log", StringComparison.Ordinal))
        {
            if (i + 1 >= args.Length)
                throw new ArgumentException("Missing value after --debug-log.");

            enableDebugger = true;
            debuggerLogPath = args[++i];
            continue;
        }

        entryArgument = args[i];
        break;
    }

    var appRoot = ResolveAppRoot(appName, callerFilePath);
    if (enableDebugger)
        debuggerLogPath = ResolveDebuggerLogPath(appRoot, debuggerLogPath);

    return new(appRoot, entryArgument, enableDebugger, debuggerLogPath);
}

static string ResolveAppRoot(
    string appName,
    [CallerFilePath] string callerFilePath = "")
{
    if (string.IsNullOrEmpty(callerFilePath))
        throw new InvalidOperationException("Caller file path is required for dev probe app resolution.");

    return Path.Combine(Path.GetDirectoryName(callerFilePath)!, appName);
}

static bool TryGetDefaultExport(JsValue moduleNamespace, out JsValue value)
{
    value = default;
    if (!moduleNamespace.TryGetObject(out var namespaceObject))
        return false;

    if (namespaceObject is not JsObject jsObject)
        return false;

    return jsObject.TryGetProperty("default", out value);
}

static bool TryGetInnermostNativeException(JsValue value, out Exception exception)
{
    exception = null!;
    if (!value.TryGetObject(out var obj) || obj is not JsNativeErrorObject nativeError)
        return false;

    exception = nativeError.NativeException;
    while (exception.InnerException is not null)
        exception = exception.InnerException;
    return true;
}

static StreamWriter CreateDebuggerLogWriter(ProbeOptions options)
{
    Directory.CreateDirectory(Path.GetDirectoryName(options.DebuggerLogPath!)!);
    var writer = new StreamWriter(options.DebuggerLogPath!, false);
    writer.AutoFlush = true;
    writer.WriteLine("OkojoInkProbe debugger log");
    writer.WriteLine($"appRoot: {options.AppRoot}");
    writer.WriteLine($"entry: {options.EntryArgument}");
    writer.WriteLine();
    return writer;
}

static string ResolveDebuggerLogPath(string appRoot, string? configuredPath)
{
    if (!string.IsNullOrWhiteSpace(configuredPath))
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(appRoot, configuredPath);

    var logDirectory = Path.Combine(appRoot, "artifacts");
    var logFile = $"ink-probe-debugger-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log";
    return Path.Combine(logDirectory, logFile);
}

file readonly record struct ProbeOptions(
    string AppRoot,
    string EntryArgument,
    bool EnableDebugger,
    string? DebuggerLogPath);

file sealed class InkProbeDebuggerSession(TextWriter output) : IDebuggerSession
{
    private const int DisassemblyContextLines = 12;
    private static readonly Regex RegisterRegex = new(@"r(\d+)", RegexOptions.CultureInvariant);
    private int stopCount;

    public void OnCheckpoint(in ExecutionCheckpoint checkpoint)
    {
        if (checkpoint.Kind != ExecutionCheckpointKind.CaughtException)
            return;

        stopCount++;
        var snapshot = checkpoint.ToPausedSnapshot();
        output.WriteLine();
        output.WriteLine($"[ink-probe-debugger] stop #{stopCount}: {snapshot.GetDebuggerStopSummary()}");

        if (snapshot.SourceLocation is { } location)
            output.WriteLine($"[ink-probe-debugger] location: {FormatLocation(location)}");

        WriteCurrentFrame(snapshot);
        WriteLocals(snapshot);
        WriteStack(snapshot);
    }

    private void WriteCurrentFrame(PausedExecutionSnapshot snapshot)
    {
        var frame = snapshot.CurrentFrameInfo;
        output.WriteLine(
            $"[ink-probe-debugger] frame: {frame.FunctionName} pc:{frame.ProgramCounter} kind:{frame.FrameKind}");
        WriteNearbyDisassembly(snapshot);
    }

    private void WriteLocals(PausedExecutionSnapshot snapshot)
    {
        var locals = snapshot.LocalValues;
        if (locals is null || locals.Count == 0)
        {
            output.WriteLine("[ink-probe-debugger] locals: <none>");
            return;
        }

        output.WriteLine("[ink-probe-debugger] locals:");
        for (var i = 0; i < locals.Count; i++)
        {
            var local = locals[i];
            output.WriteLine(
                $"  - {local.Name} [{local.StorageKind}:{local.StorageIndex}] = {FormatValue(local.Value)}");
        }
    }

    private void WriteStack(PausedExecutionSnapshot snapshot)
    {
        var frames = snapshot.StackFrames;
        output.WriteLine("[ink-probe-debugger] stack:");
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            output.WriteLine(
                $"  #{i} {frame.FunctionName} pc:{frame.ProgramCounter} {FormatFrameLocation(frame)}");
        }
    }

    private void WriteNearbyDisassembly(PausedExecutionSnapshot snapshot)
    {
        if (snapshot.Script is null || snapshot.ProgramCounter < 0)
            return;

        var highlightedProgramCounter = ResolveHighlightedProgramCounter(snapshot);
        if (highlightedProgramCounter != snapshot.ProgramCounter)
            output.WriteLine(
                $"[ink-probe-debugger] highlight-pc: {highlightedProgramCounter} (raw stop pc:{snapshot.ProgramCounter})");

        var disasm = Disassembler.Dump(snapshot.Script, new()
        {
            UnitKind = "function",
            UnitName = snapshot.CurrentFrameInfo.FunctionName,
            IncludeConstants = false,
            HighlightedProgramCounter = highlightedProgramCounter
        });

        var lines = disasm.Split(Environment.NewLine);
        var codeStart = Array.FindIndex(lines, static line => string.Equals(line, ".code", StringComparison.Ordinal));
        if (codeStart < 0)
            return;

        var instructionIndex = FindNearestInstructionLine(lines, codeStart + 1, highlightedProgramCounter);
        if (instructionIndex < 0)
            return;

        output.WriteLine("[ink-probe-debugger] disasm:");
        var from = Math.Max(codeStart + 1, instructionIndex - DisassemblyContextLines);
        var to = Math.Min(lines.Length - 1, instructionIndex + DisassemblyContextLines);
        for (var i = from; i <= to; i++)
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                var prefix = i == instructionIndex ? "=> " : "   ";
                output.WriteLine($"  {prefix}{lines[i].TrimStart()}");
            }

        WriteResolvedInstructionRegisters(snapshot, lines[instructionIndex]);
    }

    private void WriteResolvedInstructionRegisters(PausedExecutionSnapshot snapshot, string instructionLine)
    {
        var locals = snapshot.LocalValues;
        if (locals is null || locals.Count == 0)
            return;

        var registers = new HashSet<int>();
        foreach (Match match in RegisterRegex.Matches(instructionLine))
            if (int.TryParse(match.Groups[1].ValueSpan, out var register))
                registers.Add(register);

        if (registers.Count == 0)
            return;

        var resolved = new List<string>();
        foreach (var register in registers.OrderBy(static value => value))
        {
            var found = false;
            for (var i = 0; i < locals.Count; i++)
            {
                var local = locals[i];
                if (local.StorageKind != JsLocalDebugStorageKind.Register || local.StorageIndex != register)
                    continue;

                resolved.Add($"r{register}={FormatValue(local.Value)} ({local.Name})");
                found = true;
            }

            if (!found)
                resolved.Add($"r{register}=<unnamed>");
        }

        output.WriteLine($"[ink-probe-debugger] operands: {string.Join(", ", resolved)}");
    }

    private static int ResolveHighlightedProgramCounter(PausedExecutionSnapshot snapshot)
    {
        return snapshot.ProgramCounter;
    }

    private static bool TryFindPcForExactSourceLocation(
        JsScript script,
        CheckpointSourceLocation sourceLocation,
        int preferredProgramCounter,
        out int programCounter)
    {
        programCounter = -1;
        if (script.DebugPcOffsets is not { Length: > 0 } pcOffsets)
            return false;

        var bestEarlierOrEqualPc = -1;
        var bestLaterPc = -1;
        for (var i = 0; i < pcOffsets.Length; i++)
        {
            var candidatePc = pcOffsets[i];
            if (!TryGetExactSourceLocationAtPc(script, candidatePc, out var line, out var column))
                continue;
            if (line != sourceLocation.Line || column != sourceLocation.Column)
                continue;

            if (candidatePc <= preferredProgramCounter)
            {
                if (candidatePc > bestEarlierOrEqualPc)
                    bestEarlierOrEqualPc = candidatePc;
                continue;
            }

            if (bestLaterPc < 0)
                bestLaterPc = candidatePc;
        }

        programCounter = bestEarlierOrEqualPc >= 0 ? bestEarlierOrEqualPc : bestLaterPc;
        return programCounter >= 0;
    }

    private static bool TryGetExactSourceLocationAtPc(JsScript script, int opcodePc, out int line, out int column)
    {
        line = 0;
        column = 0;

        if (script.SourceCode is not { } sourceCode ||
            script.DebugPcOffsets is null ||
            script.DebugSourceOffsets is null)
            return false;

        if (script.DebugPcOffsets.Length == 0 || script.DebugSourceOffsets.Length != script.DebugPcOffsets.Length)
            return false;

        var index = Array.BinarySearch(script.DebugPcOffsets, opcodePc);
        if (index < 0)
            return false;

        var sourceOffset = script.DebugSourceOffsets[index];
        (line, column) = SourceLocation.GetLineColumn(sourceCode, sourceOffset);
        return true;
    }

    private static int FindNearestInstructionLine(string[] lines, int startIndex, int programCounter)
    {
        var nearestIndex = -1;
        var nearestPc = -1;
        for (var i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (line.Length < 4 || !char.IsDigit(line[0]))
                continue;

            if (!int.TryParse(line.AsSpan(0, 4), out var instructionPc))
                continue;
            if (instructionPc > programCounter)
                break;

            nearestPc = instructionPc;
            nearestIndex = i;
        }

        return nearestPc >= 0 ? nearestIndex : -1;
    }

    private static string FormatLocation(CheckpointSourceLocation location)
    {
        return location.SourcePath is null
            ? $"{location.Line}:{location.Column}"
            : $"{location.SourcePath}:{location.Line}:{location.Column}";
    }

    private static string FormatFrameLocation(StackFrameInfo frame)
    {
        if (!frame.HasSourceLocation)
            return "<no source>";

        return frame.SourcePath is null
            ? $"@ {frame.SourceLine}:{frame.SourceColumn}"
            : $"@ {frame.SourcePath}:{frame.SourceLine}:{frame.SourceColumn}";
    }

    private static string FormatValue(in JsValue value)
    {
        if (value.IsUndefined) return "undefined";
        if (value.IsNull) return "null";
        if (value.IsBool) return value.IsTrue ? "true" : "false";
        if (value.IsInt32) return value.Int32Value.ToString(CultureInfo.InvariantCulture);
        if (value.IsFloat64) return value.Float64Value.ToString(CultureInfo.InvariantCulture);
        if (value.IsString) return $"\"{Escape(value.AsString())}\"";
        if (value.TryGetObject(out var obj))
            return obj switch
            {
                JsFunction fn => $"Function({fn.Name ?? "<anonymous>"})",
                JsObject jsObject => $"Object({jsObject.GetType().Name})",
                _ => obj.GetType().Name
            };

        return value.ToString() ?? "<unknown>";
    }

    private static string Escape(string text)
    {
        return text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
