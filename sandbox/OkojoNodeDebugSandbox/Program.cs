using System.Globalization;
using System.Text.RegularExpressions;
using Okojo.Bytecode;
using Okojo.Diagnostics;
using Okojo.Node;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo;

var options = NodeDebugOptions.Parse(args);

Console.WriteLine("OkojoNodeDebugSandbox");
Console.WriteLine($"mode: {options.SourceMode}");
Console.WriteLine($"entry: {options.EntryPath}");
if (options.AppRoot is not null)
    Console.WriteLine($"appRoot: {options.AppRoot}");
Console.WriteLine($"stops: {options.StopMask}");
if (options.CheckInterval is not null)
    Console.WriteLine($"checkInterval: {options.CheckInterval.Value}");
if (options.Breakpoints.Count != 0)
    Console.WriteLine($"breakpoints: {string.Join(", ", options.Breakpoints.Select(static bp => $"{bp.SourcePath}:{bp.Line}"))}");
if (options.LogPath is not null)
    Console.WriteLine($"debugLog: {options.LogPath}");
Console.WriteLine();

var stdout = new StringWriter();
var stderr = new StringWriter();
using var debugWriter = CreateDebugWriter(options);

try
{
    using var runtime = CreateRuntime(options, stdout, stderr, debugWriter);

    ApplyBreakpoints(runtime, options, debugWriter);

    string runEntry = ResolveRuntimeEntryPath(options);
    JsValue result = RunNodeMain(runtime, runEntry, options);
    runtime.MainRealm.PumpJobs();

    Console.WriteLine($"result: {FormatResult(result)}");
    Console.WriteLine($"captured stdout: {EscapeControl(stdout.ToString())}");
    Console.WriteLine($"captured stderr: {EscapeControl(stderr.ToString())}");
}
catch (Exception ex)
{
    Console.WriteLine($"exception: {ex.GetType().Name}");
    Console.WriteLine(ex);
    if (ex is JsRuntimeException jsEx)
        Console.WriteLine(jsEx.FormatOkojoStackTrace());
    return 1;
}

return 0;

static OkojoNodeRuntime CreateRuntime(
    NodeDebugOptions options,
    StringWriter stdout,
    StringWriter stderr,
    TextWriter debugWriter)
{
    var builder = OkojoNodeRuntime.CreateBuilder()
        .ConfigureTerminal(terminal =>
        {
            terminal.Stdout = stdout;
            terminal.Stderr = stderr;
            terminal.StdoutIsTty = true;
            terminal.StderrIsTty = true;
            terminal.StdoutColumns = 120;
            terminal.StdoutRows = 40;
            terminal.StderrColumns = 120;
            terminal.StderrRows = 40;
        })
        .ConfigureRuntime(runtime =>
        {
            runtime.UseAgent(agent =>
            {
                agent.DebuggerSession = new NodeDebugSession(
                    debugWriter,
                    options.StopMask,
                    includeDisassembly: options.IncludeDisassembly,
                    includeLocals: options.IncludeLocals,
                    includeStack: options.IncludeStack);

                if (options.CheckInterval is not null)
                    agent.SetCheckInterval(options.CheckInterval.Value);

                if (options.StopMask.HasFlag(DebugStopMask.Debugger))
                    agent.EnableDebuggerStatementHook();
                if (options.StopMask.HasFlag(DebugStopMask.Breakpoint))
                    agent.EnableBreakpointHook();
                if (options.StopMask.HasFlag(DebugStopMask.CaughtException))
                    agent.EnableCaughtExceptionHook();
                if (options.StopMask.HasFlag(DebugStopMask.Call))
                    agent.EnableCallHook();
                if (options.StopMask.HasFlag(DebugStopMask.Return))
                    agent.EnableReturnHook();
                if (options.StopMask.HasFlag(DebugStopMask.Pump))
                    agent.EnablePumpHook();
                if (options.StopMask.HasFlag(DebugStopMask.SuspendGenerator))
                    agent.EnableSuspendGeneratorHook();
                if (options.StopMask.HasFlag(DebugStopMask.ResumeGenerator))
                    agent.EnableResumeGeneratorHook();
            });
        });

    switch (options.SourceMode)
    {
        case NodeDebugSourceMode.Inline:
        case NodeDebugSourceMode.File:
            builder.UseModuleSourceLoader(new InMemoryModuleLoader(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [options.EntryPath] = options.ReadSource()
            }));
            break;
        case NodeDebugSourceMode.AppRoot:
            break;
        default:
            throw new InvalidOperationException($"Unsupported source mode: {options.SourceMode}");
    }

    return builder.Build();
}

static void ApplyBreakpoints(OkojoNodeRuntime runtime, NodeDebugOptions options, TextWriter debugWriter)
{
    if (options.Breakpoints.Count == 0)
        return;

    foreach (var breakpoint in options.Breakpoints)
    {
        string sourcePath = options.SourceMode == NodeDebugSourceMode.AppRoot
            ? NormalizeFilesystemPath(options.AppRoot!, breakpoint.SourcePath)
            : PathUtil.NormalizePath(breakpoint.SourcePath);
        var handle = runtime.Runtime.MainAgent.AddBreakpoint(sourcePath, breakpoint.Line);
        debugWriter.WriteLine($"[node-debug] breakpoint-added: {sourcePath}:{breakpoint.Line} verified:{handle.IsVerified}");
    }
}

static JsValue RunNodeMain(OkojoNodeRuntime runtime, string runEntry, NodeDebugOptions options)
{
    string? previousDirectory = null;
    if (options.SourceMode == NodeDebugSourceMode.AppRoot)
    {
        previousDirectory = Environment.CurrentDirectory;
        Environment.CurrentDirectory = options.AppRoot!;
    }

    try
    {
        return runtime.RunMainModule(runEntry);
    }
    finally
    {
        if (previousDirectory is not null)
            Environment.CurrentDirectory = previousDirectory;
    }
}

static string ResolveRuntimeEntryPath(NodeDebugOptions options)
{
    return options.SourceMode == NodeDebugSourceMode.AppRoot
        ? NormalizeFilesystemPath(options.AppRoot!, options.EntryPath)
        : options.EntryPath;
}

static string NormalizeFilesystemPath(string appRoot, string path)
{
    return Path.IsPathRooted(path)
        ? Path.GetFullPath(path)
        : Path.GetFullPath(Path.Combine(appRoot, path));
}

static TextWriter CreateDebugWriter(NodeDebugOptions options)
{
    if (options.LogPath is null)
        return Console.Out;

    Directory.CreateDirectory(Path.GetDirectoryName(options.LogPath)!);
    var writer = new StreamWriter(options.LogPath, append: false);
    writer.AutoFlush = true;
    writer.WriteLine("OkojoNodeDebugSandbox log");
    writer.WriteLine($"entry: {options.EntryPath}");
    writer.WriteLine($"stops: {options.StopMask}");
    writer.WriteLine();
    return writer;
}

static string FormatResult(JsValue value)
{
    if (value.TryGetObject(out var obj) &&
        obj.TryGetProperty("default", out var defaultValue) &&
        !defaultValue.IsUndefined)
    {
        return defaultValue.ToString() ?? "<null>";
    }

    return value.ToString() ?? "<null>";
}

static string EscapeControl(string text)
{
    return text
        .Replace("\u001b", "\\u001b", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}

[Flags]
file enum DebugStopMask
{
    None = 0,
    Debugger = 1 << 0,
    Breakpoint = 1 << 1,
    CaughtException = 1 << 2,
    Call = 1 << 3,
    Return = 1 << 4,
    Pump = 1 << 5,
    SuspendGenerator = 1 << 6,
    ResumeGenerator = 1 << 7,
    Periodic = 1 << 8,
    All = Debugger | Breakpoint | CaughtException | Call | Return | Pump | SuspendGenerator | ResumeGenerator | Periodic
}

file enum NodeDebugSourceMode
{
    Inline,
    File,
    AppRoot
}

file readonly record struct BreakpointSpec(string SourcePath, int Line);

file sealed class NodeDebugOptions
{
    public required NodeDebugSourceMode SourceMode { get; init; }
    public string? InlineSource { get; init; }
    public string? FilePath { get; init; }
    public string? AppRoot { get; init; }
    public required string EntryPath { get; init; }
    public DebugStopMask StopMask { get; init; } = DebugStopMask.Debugger | DebugStopMask.CaughtException;
    public ulong? CheckInterval { get; init; }
    public string? LogPath { get; init; }
    public bool IncludeDisassembly { get; init; } = true;
    public bool IncludeLocals { get; init; } = true;
    public bool IncludeStack { get; init; } = true;
    public List<BreakpointSpec> Breakpoints { get; } = new();

    public string ReadSource()
    {
        return SourceMode switch
        {
            NodeDebugSourceMode.Inline => InlineSource ?? throw new InvalidOperationException("Inline source is missing."),
            NodeDebugSourceMode.File => File.ReadAllText(FilePath ?? throw new InvalidOperationException("File path is missing.")),
            _ => throw new InvalidOperationException("ReadSource is only valid for inline/file modes.")
        };
    }

    public static NodeDebugOptions Parse(string[] args)
    {
        NodeDebugSourceMode? sourceMode = null;
        string? inlineSource = null;
        string? filePath = null;
        string? appRoot = null;
        string entryPath = "/app/main.mjs";
        DebugStopMask stopMask = DebugStopMask.Debugger | DebugStopMask.CaughtException;
        ulong? checkInterval = null;
        string? logPath = null;
        bool includeDisassembly = true;
        bool includeLocals = true;
        bool includeStack = true;
        var breakpoints = new List<BreakpointSpec>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--inline":
                    sourceMode = NodeDebugSourceMode.Inline;
                    inlineSource = args[++i];
                    break;
                case "--file":
                    sourceMode = NodeDebugSourceMode.File;
                    filePath = args[++i];
                    break;
                case "--app-root":
                    sourceMode = NodeDebugSourceMode.AppRoot;
                    appRoot = Path.GetFullPath(args[++i]);
                    break;
                case "--entry":
                    entryPath = args[++i];
                    break;
                case "--stop":
                    stopMask = ParseStopMask(args[++i]);
                    break;
                case "--break":
                    breakpoints.Add(ParseBreakpoint(args[++i]));
                    break;
                case "--check-interval":
                    checkInterval = ulong.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--log":
                    logPath = Path.GetFullPath(args[++i]);
                    break;
                case "--no-disasm":
                    includeDisassembly = false;
                    break;
                case "--no-locals":
                    includeLocals = false;
                    break;
                case "--no-stack":
                    includeStack = false;
                    break;
                case "--help":
                case "-h":
                    throw new ArgumentException(GetUsage());
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.{Environment.NewLine}{GetUsage()}");
            }
        }

        if (sourceMode is null)
            throw new ArgumentException(GetUsage());

        if (breakpoints.Count != 0)
            stopMask |= DebugStopMask.Breakpoint;

        return new NodeDebugOptions
        {
            SourceMode = sourceMode.Value,
            InlineSource = inlineSource,
            FilePath = filePath,
            AppRoot = appRoot,
            EntryPath = sourceMode == NodeDebugSourceMode.AppRoot
                ? entryPath
                : PathUtil.NormalizePath(entryPath),
            StopMask = stopMask,
            CheckInterval = checkInterval,
            LogPath = logPath,
            IncludeDisassembly = includeDisassembly,
            IncludeLocals = includeLocals,
            IncludeStack = includeStack
        };
    }

    private static BreakpointSpec ParseBreakpoint(string spec)
    {
        int colonIndex = spec.LastIndexOf(':');
        if (colonIndex <= 0 || colonIndex == spec.Length - 1)
            throw new ArgumentException($"Breakpoint must be in the form sourcePath:line. Got '{spec}'.");

        string sourcePath = spec[..colonIndex];
        int line = int.Parse(spec[(colonIndex + 1)..], CultureInfo.InvariantCulture);
        return new BreakpointSpec(sourcePath, line);
    }

    private static DebugStopMask ParseStopMask(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DebugStopMask.None;

        DebugStopMask result = DebugStopMask.None;
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= part.ToLowerInvariant() switch
            {
                "debugger" => DebugStopMask.Debugger,
                "breakpoint" => DebugStopMask.Breakpoint,
                "caught" or "caught-exception" => DebugStopMask.CaughtException,
                "call" => DebugStopMask.Call,
                "return" => DebugStopMask.Return,
                "pump" => DebugStopMask.Pump,
                "suspend" or "suspend-generator" => DebugStopMask.SuspendGenerator,
                "resume" or "resume-generator" => DebugStopMask.ResumeGenerator,
                "periodic" => DebugStopMask.Periodic,
                "all" => DebugStopMask.All,
                _ => throw new ArgumentException($"Unknown stop kind '{part}'.")
            };
        }

        return result;
    }

    private static string GetUsage()
    {
        return
            """
            Usage:
              dotnet run --project sandbox\OkojoNodeDebugSandbox\OkojoNodeDebugSandbox.csproj -- --inline "<js>" [--entry /app/main.mjs]
              dotnet run --project sandbox\OkojoNodeDebugSandbox\OkojoNodeDebugSandbox.csproj -- --file <path> [--entry /app/main.mjs]
              dotnet run --project sandbox\OkojoNodeDebugSandbox\OkojoNodeDebugSandbox.csproj -- --app-root <dir> [--entry main.mjs]
            
            Options:
              --stop debugger,caught,breakpoint,call,return,pump,suspend,resume,periodic,all
              --break <sourcePath:line>
              --check-interval <n>
              --log <path>
              --no-disasm
              --no-locals
              --no-stack
            
            Notes:
              - Use `debugger;` in source and `--stop debugger` to pause on debugger statements.
              - Inline/file modes run a single in-memory Node entry module.
              - App-root mode runs Okojo.Node against a real app directory.
            """;
    }
}

file sealed class NodeDebugSession(
    TextWriter output,
    DebugStopMask stopMask,
    bool includeDisassembly,
    bool includeLocals,
    bool includeStack) : IDebuggerSession
{
    private const int DisassemblyContextLines = 40;
    private static readonly Regex RegisterRegex = new(@"r(\d+)", RegexOptions.CultureInvariant);
    private int stopCount;

    public void OnCheckpoint(in ExecutionCheckpoint checkpoint)
    {
        if (!ShouldEmit(checkpoint.Kind))
            return;

        stopCount++;
        var snapshot = checkpoint.ToPausedSnapshot();
        output.WriteLine();
        output.WriteLine($"[node-debug] stop #{stopCount}: {snapshot.GetDebuggerStopSummary()}");
        if (snapshot.SourceLocation is { } location)
            output.WriteLine($"[node-debug] location: {FormatLocation(location)}");

        var frame = snapshot.CurrentFrameInfo;
        output.WriteLine($"[node-debug] frame: {frame.FunctionName} pc:{frame.ProgramCounter} kind:{frame.FrameKind}");

        if (includeDisassembly)
            WriteNearbyDisassembly(snapshot);
        if (includeLocals)
            WriteLocals(snapshot);
        if (includeStack)
            WriteStack(snapshot);
    }

    private bool ShouldEmit(ExecutionCheckpointKind kind)
    {
        return kind switch
        {
            ExecutionCheckpointKind.DebuggerStatement => stopMask.HasFlag(DebugStopMask.Debugger),
            ExecutionCheckpointKind.Breakpoint => stopMask.HasFlag(DebugStopMask.Breakpoint),
            ExecutionCheckpointKind.CaughtException => stopMask.HasFlag(DebugStopMask.CaughtException),
            ExecutionCheckpointKind.Call => stopMask.HasFlag(DebugStopMask.Call),
            ExecutionCheckpointKind.Return => stopMask.HasFlag(DebugStopMask.Return),
            ExecutionCheckpointKind.Pump => stopMask.HasFlag(DebugStopMask.Pump),
            ExecutionCheckpointKind.SuspendGenerator => stopMask.HasFlag(DebugStopMask.SuspendGenerator),
            ExecutionCheckpointKind.ResumeGenerator => stopMask.HasFlag(DebugStopMask.ResumeGenerator),
            ExecutionCheckpointKind.Periodic => stopMask.HasFlag(DebugStopMask.Periodic),
            ExecutionCheckpointKind.Step => true,
            _ => false
        };
    }

    private void WriteLocals(PausedExecutionSnapshot snapshot)
    {
        var locals = snapshot.LocalValues;
        if (locals is null || locals.Count == 0)
        {
            output.WriteLine("[node-debug] locals: <none>");
            return;
        }

        output.WriteLine("[node-debug] locals:");
        for (int i = 0; i < locals.Count; i++)
        {
            var local = locals[i];
            output.WriteLine($"  - {local.Name} [{local.StorageKind}:{local.StorageIndex}] = {FormatValue(local.Value)}");
        }
    }

    private void WriteStack(PausedExecutionSnapshot snapshot)
    {
        output.WriteLine("[node-debug] stack:");
        for (int i = 0; i < snapshot.StackFrames.Count; i++)
        {
            var frame = snapshot.StackFrames[i];
            output.WriteLine($"  #{i} {frame.FunctionName} pc:{frame.ProgramCounter} {FormatFrameLocation(frame)}");
        }
    }

    private void WriteNearbyDisassembly(PausedExecutionSnapshot snapshot)
    {
        if (snapshot.Script is null || snapshot.ProgramCounter < 0)
            return;

        var disasm = Disassembler.Dump(snapshot.Script, new DisassemblerOptions
        {
            UnitKind = "function",
            UnitName = snapshot.CurrentFrameInfo.FunctionName,
            IncludeConstants = false
        });

        var lines = disasm.Split(Environment.NewLine, StringSplitOptions.None);
        int codeStart = Array.FindIndex(lines, static line => string.Equals(line, ".code", StringComparison.Ordinal));
        if (codeStart < 0)
            return;

        int instructionIndex = FindNearestInstructionLine(lines, codeStart + 1, snapshot.ProgramCounter);
        if (instructionIndex < 0)
            return;

        output.WriteLine("[node-debug] disasm:");
        int from = Math.Max(codeStart + 1, instructionIndex - DisassemblyContextLines);
        int to = Math.Min(lines.Length - 1, instructionIndex + DisassemblyContextLines);
        for (int i = from; i <= to; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            string prefix = i == instructionIndex ? "=> " : "   ";
            output.WriteLine($"  {prefix}{lines[i].TrimStart()}");
        }

        WriteResolvedInstructionRegisters(snapshot, lines[instructionIndex]);
    }

    private void WriteResolvedInstructionRegisters(PausedExecutionSnapshot snapshot, string instructionLine)
    {
        var locals = snapshot.LocalValues;
        if (locals is null || locals.Count == 0)
            return;

        var registers = new SortedSet<int>();
        foreach (Match match in RegisterRegex.Matches(instructionLine))
        {
            if (int.TryParse(match.Groups[1].ValueSpan, out int register))
                registers.Add(register);
        }

        if (registers.Count == 0)
            return;

        var resolved = new List<string>();
        foreach (int register in registers)
        {
            bool found = false;
            for (int i = 0; i < locals.Count; i++)
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

        output.WriteLine($"[node-debug] operands: {string.Join(", ", resolved)}");
    }

    private static int FindNearestInstructionLine(string[] lines, int startIndex, int programCounter)
    {
        int nearestIndex = -1;
        int nearestPc = -1;
        for (int i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (line.Length < 4 || !char.IsDigit(line[0]))
                continue;

            if (!int.TryParse(line.AsSpan(0, 4), out int instructionPc))
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
        {
            return obj switch
            {
                JsFunction fn => $"Function({fn.Name ?? "<anonymous>"})",
                JsObject jsObject => $"Object({jsObject.GetType().Name})",
                _ => obj.GetType().Name
            };
        }

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

file sealed class InMemoryModuleLoader(Dictionary<string, string> modules) : IModuleSourceLoader
{
    private readonly Dictionary<string, string> modules = modules;

    public string ResolveSpecifier(string specifier, string? referrer)
    {
        if (specifier.StartsWith("./", StringComparison.Ordinal) ||
            specifier.StartsWith("../", StringComparison.Ordinal))
        {
            string basePath = referrer is null ? "/" : PathUtil.NormalizePath(referrer);
            int slash = basePath.LastIndexOf('/');
            string dir = slash >= 0 ? basePath[..(slash + 1)] : "/";
            return PathUtil.NormalizePath(dir + specifier);
        }

        return PathUtil.NormalizePath(specifier);
    }

    public string LoadSource(string resolvedId)
    {
        string normalized = PathUtil.NormalizePath(resolvedId);
        if (modules.TryGetValue(normalized, out var source))
            return source;

        throw new InvalidOperationException("Module not found: " + resolvedId);
    }
}

file static class PathUtil
{
    public static string NormalizePath(string path)
    {
        path = path.Replace('\\', '/');
        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;

            if (part == "..")
            {
                if (parts.Count != 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return "/" + string.Join("/", parts);
    }
}
