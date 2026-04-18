using System.Text.RegularExpressions;
using Okojo.Compiler;
using Okojo.Diagnostics;
using Okojo.Parsing;
using Okojo.Runtime;
using Okojo;

var options = ParseArguments(args);
Console.WriteLine("OkojoRuntimeDebugSandbox");
Console.WriteLine($"input: {options.Input}");
Console.WriteLine($"execution: {options.Execution}");
Console.WriteLine($"mode: {options.Mode}");
if (options.BreakpointLine is int breakpointLine)
    Console.WriteLine($"breakpoint: {breakpointLine}");
Console.WriteLine();

var inputIsFile = File.Exists(options.Input);
var sourcePath = inputIsFile
    ? Path.GetFullPath(options.Input)
    : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "__inline_debug_module__.mjs"));
var source = inputIsFile
    ? File.ReadAllText(sourcePath)
    : options.Input;

using var output = options.LogPath is null
    ? Console.Out
    : new StreamWriter(options.LogPath, append: false) { AutoFlush = true };

var runtimeBuilder = JsRuntime.CreateBuilder()
    .UseAgent(agent =>
    {
        agent.DebuggerSession = new SandboxDebuggerSession(output, options.Mode);
        if (options.Mode is DebugMode.CaughtException)
            agent.EnableCaughtExceptionHook();
    });

if (options.Execution is ExecutionMode.Module && !inputIsFile)
    runtimeBuilder.UseModuleSourceLoader(new SingleModuleSourceLoader(sourcePath, source));

using var runtime = runtimeBuilder.Build();

var realm = runtime.DefaultRealm;
var agent = runtime.MainAgent;

try
{
    JsValue result;
    if (options.Execution is ExecutionMode.Module)
    {
        if (options.BreakpointLine is int line)
            agent.AddBreakpoint(sourcePath, line);

        result = realm.Import(sourcePath);
        realm.PumpJobs();
    }
    else
    {
        var parsed = JavaScriptParser.ParseScript(source, sourcePath: inputIsFile ? sourcePath : null);
        var script = JsCompiler.Compile(realm, parsed);

        if (options.BreakpointLine is int line)
            agent.AddBreakpoint(script, line);

        realm.Execute(script);
        realm.PumpJobs();
        result = realm.Accumulator;
    }

    Console.WriteLine($"result: {FormatValue(result)}");
}
catch (Exception ex)
{
    Console.WriteLine($"exception: {ex.GetType().Name}");
    Console.WriteLine(ex);
    if (ex is JsRuntimeException jsEx)
        Console.WriteLine(jsEx.FormatOkojoStackTrace());
}

static SandboxOptions ParseArguments(string[] args)
{
    if (args.Length == 0)
        throw new ArgumentException(
            "Usage: --inline <js> | <file> [--module] [--caught|--debugger|--breakpoint] [--breakpoint-line <n>] [--log <path>]");

    string? input = null;
    var execution = ExecutionMode.Script;
    var mode = DebugMode.CaughtException;
    int? breakpointLine = null;
    string? logPath = null;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--inline":
                input = args[++i];
                break;
            case "--module":
                execution = ExecutionMode.Module;
                break;
            case "--script":
                execution = ExecutionMode.Script;
                break;
            case "--caught":
                mode = DebugMode.CaughtException;
                break;
            case "--debugger":
                mode = DebugMode.DebuggerStatement;
                break;
            case "--breakpoint":
                mode = DebugMode.Breakpoint;
                break;
            case "--breakpoint-line":
                breakpointLine = int.Parse(args[++i]);
                break;
            case "--log":
                logPath = args[++i];
                break;
            default:
                input ??= args[i];
                break;
        }
    }

    if (string.IsNullOrWhiteSpace(input))
        throw new ArgumentException("Missing script input.");

    return new SandboxOptions(input, execution, mode, breakpointLine, logPath);
}

static string FormatValue(JsValue value) => value.ToString() ?? "<null>";

file enum ExecutionMode
{
    Script,
    Module
}

file enum DebugMode
{
    CaughtException,
    DebuggerStatement,
    Breakpoint
}

file readonly record struct SandboxOptions(
    string Input,
    ExecutionMode Execution,
    DebugMode Mode,
    int? BreakpointLine,
    string? LogPath);

file sealed class SandboxDebuggerSession(TextWriter output, DebugMode mode) : IDebuggerSession
{
    private const int DisassemblyContextLines = 8;
    private static readonly Regex RegisterRegex = new(@"r(\d+)", RegexOptions.CultureInvariant);
    private int stopCount;

    public void OnCheckpoint(in ExecutionCheckpoint checkpoint)
    {
        if (mode == DebugMode.CaughtException && checkpoint.Kind != ExecutionCheckpointKind.CaughtException)
            return;
        if (mode == DebugMode.DebuggerStatement && checkpoint.Kind != ExecutionCheckpointKind.DebuggerStatement)
            return;
        if (mode == DebugMode.Breakpoint && checkpoint.Kind != ExecutionCheckpointKind.Breakpoint)
            return;

        stopCount++;
        var snapshot = checkpoint.ToPausedSnapshot();
        output.WriteLine();
        output.WriteLine($"[runtime-debug] stop #{stopCount}: {snapshot.GetDebuggerStopSummary()}");
        WriteDisassembly(snapshot);
        WriteLocals(snapshot);
    }

    private void WriteDisassembly(PausedExecutionSnapshot snapshot)
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
        var codeStart = Array.FindIndex(lines, static line => string.Equals(line, ".code", StringComparison.Ordinal));
        if (codeStart < 0)
            return;

        int instructionIndex = FindNearestInstructionLine(lines, codeStart + 1, snapshot.ProgramCounter);
        if (instructionIndex < 0)
            return;

        output.WriteLine("[runtime-debug] disasm:");
        int from = Math.Max(codeStart + 1, instructionIndex - DisassemblyContextLines);
        int to = Math.Min(lines.Length - 1, instructionIndex + DisassemblyContextLines);
        for (int i = from; i <= to; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var prefix = i == instructionIndex ? "=> " : "   ";
            output.WriteLine($"  {prefix}{lines[i].TrimStart()}");
        }
    }

    private void WriteLocals(PausedExecutionSnapshot snapshot)
    {
        var locals = snapshot.LocalValues;
        if (locals is null || locals.Count == 0)
        {
            output.WriteLine("[runtime-debug] locals: <none>");
            return;
        }

        output.WriteLine("[runtime-debug] locals:");
        for (int i = 0; i < locals.Count; i++)
        {
            var local = locals[i];
            output.WriteLine($"  - {local.Name} [{local.StorageKind}:{local.StorageIndex}] = {FormatValue(local.Value)}");
        }

        if (snapshot.Script is null || snapshot.ProgramCounter < 0)
            return;

        var disasm = Disassembler.Dump(snapshot.Script, new DisassemblerOptions
        {
            UnitKind = "function",
            UnitName = snapshot.CurrentFrameInfo.FunctionName,
            IncludeConstants = false
        });
        var lines = disasm.Split(Environment.NewLine, StringSplitOptions.None);
        var codeStart = Array.FindIndex(lines, static line => string.Equals(line, ".code", StringComparison.Ordinal));
        if (codeStart < 0)
            return;

        int instructionIndex = FindNearestInstructionLine(lines, codeStart + 1, snapshot.ProgramCounter);
        if (instructionIndex < 0)
            return;

        var registers = new SortedSet<int>();
        foreach (Match match in RegisterRegex.Matches(lines[instructionIndex]))
        {
            if (int.TryParse(match.Groups[1].ValueSpan, out int register))
                registers.Add(register);
        }

        if (registers.Count == 0)
            return;

        var resolved = new List<string>();
        foreach (var register in registers)
        {
            var named = locals.FirstOrDefault(local =>
                local.StorageKind == JsLocalDebugStorageKind.Register && local.StorageIndex == register);
            if (!string.IsNullOrEmpty(named.Name))
                resolved.Add($"r{register}={FormatValue(named.Value)} ({named.Name})");
            else
                resolved.Add($"r{register}=<unnamed>");
        }

        output.WriteLine($"[runtime-debug] operands: {string.Join(", ", resolved)}");
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

            if (!int.TryParse(line.AsSpan(0, 4), out int pc))
                continue;

            if (pc > programCounter)
                break;

            nearestPc = pc;
            nearestIndex = i;
        }

        return nearestPc <= programCounter ? nearestIndex : -1;
    }

    private static string FormatValue(JsValue value) => value.ToString() ?? "<null>";
}

file sealed class SingleModuleSourceLoader(string resolvedId, string source) : IModuleSourceLoader
{
    public string ResolveSpecifier(string specifier, string? referrer)
    {
        if (string.Equals(specifier, resolvedId, StringComparison.Ordinal))
            return resolvedId;

        if (Path.IsPathRooted(specifier))
            return Path.GetFullPath(specifier);

        if (!string.IsNullOrEmpty(referrer))
        {
            var baseDir = Path.GetDirectoryName(referrer);
            if (!string.IsNullOrEmpty(baseDir))
                return Path.GetFullPath(Path.Combine(baseDir, specifier));
        }

        return Path.GetFullPath(specifier);
    }

    public string LoadSource(string requestedResolvedId)
    {
        if (!string.Equals(requestedResolvedId, resolvedId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Module not found: {requestedResolvedId}");
        return source;
    }
}
