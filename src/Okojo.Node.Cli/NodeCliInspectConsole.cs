using System.Text.Json;
using Okojo.DebugServer;
using Okojo.SourceMaps;

namespace Okojo.Node.Cli;

internal sealed class NodeCliInspectConsole : IDisposable
{
    private readonly Dictionary<string, SourceMapLocation> breakpointDisplayOverrides =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly List<int> breakpointOrder = new();
    private readonly Dictionary<int, BreakpointEntry> breakpoints = new();
    private readonly object consoleGate = new();
    private readonly string? entrySourcePath;
    private readonly Thread inputThread;
    private readonly ManualResetEventSlim pauseReady = new(false);
    private readonly DebuggerSession session;
    private readonly SourceMapRegistry? sourceMaps;
    private readonly string workingDirectory;
    private BreakpointEntry? autoContinueTarget;
    private StopLocation? currentStop;

    private volatile bool stopped;
    private volatile bool terminated;

    public NodeCliInspectConsole(
        DebuggerSession session,
        string workingDirectory,
        string? entrySourcePath,
        SourceMapRegistry? sourceMaps)
    {
        this.session = session;
        this.workingDirectory = Path.GetFullPath(workingDirectory);
        this.entrySourcePath = entrySourcePath is null ? null : Path.GetFullPath(entrySourcePath);
        this.sourceMaps = sourceMaps;
        inputThread = new(RunInputLoop)
        {
            IsBackground = true,
            Name = "Okojo.Node.Cli.InspectConsole"
        };
    }

    public void Dispose()
    {
        terminated = true;
        pauseReady.Set();
        pauseReady.Dispose();
    }

    public void Start()
    {
        inputThread.Start();
    }

    public void OnSessionOutput(string line)
    {
        if (!TryParseJson(line, out var payload))
        {
            WriteLine(line);
            return;
        }

        var eventName = GetString(payload, "event");
        switch (eventName)
        {
            case "stopped":
                HandleStopped(payload);
                return;
            case "breakpoint-added":
            case "breakpoint-updated":
                HandleBreakpoint(payload);
                return;
            case "breakpoint-cleared":
                HandleBreakpointCleared(payload);
                return;
            case "error":
                WriteLine(GetString(payload, "message") ?? "Debugger error.");
                return;
            case "terminated":
                terminated = true;
                pauseReady.Set();
                return;
            case "unknown-command":
                WriteLine($"Unknown command: {GetString(payload, "command")}");
                return;
            case "help":
                return;
        }

        WriteLine(line);
    }

    private void RunInputLoop()
    {
        while (!terminated)
        {
            pauseReady.Wait();
            if (terminated)
                break;

            lock (consoleGate)
            {
                Console.Write("debug> ");
                Console.Out.Flush();
            }

            var line = Console.ReadLine();
            if (line is null)
                break;

            HandleUserCommand(line.Trim());
        }
    }

    private void HandleUserCommand(string command)
    {
        if (command.Length == 0)
            return;

        if (TryHandleSetBreakpointCommand(command))
            return;

        if (string.Equals(command, "breakpoints", StringComparison.OrdinalIgnoreCase))
        {
            PrintBreakpoints();
            return;
        }

        if (string.Equals(command, "c", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "continue", StringComparison.OrdinalIgnoreCase))
        {
            stopped = false;
            pauseReady.Reset();
            if (!TryContinueToUserBreakpoint())
                session.SubmitCommand("continue");
            return;
        }

        if (TryHandleListCommand(command))
            return;

        if (string.Equals(command, "help", StringComparison.OrdinalIgnoreCase))
        {
            WriteLine("Commands: c(continue), sb('file', line), breakpoints, list(n), q(quit)");
            return;
        }

        if (IsResumeCommand(command))
        {
            stopped = false;
            pauseReady.Reset();
        }

        session.SubmitCommand(command);
    }

    private bool TryHandleSetBreakpointCommand(string command)
    {
        if (!command.StartsWith("sb(", StringComparison.OrdinalIgnoreCase) || !command.EndsWith(')'))
            return false;

        var inner = command[3..^1];
        var comma = inner.LastIndexOf(',');
        if (comma <= 0 || !int.TryParse(inner[(comma + 1)..].Trim(), out var line))
        {
            WriteLine("Usage: sb('file.js', 10)");
            return true;
        }

        var rawPath = inner[..comma].Trim();
        rawPath = rawPath.Trim('\'', '"');
        if (rawPath.Length == 0)
        {
            WriteLine("Usage: sb('file.js', 10)");
            return true;
        }

        var requestedSourcePath = ResolveSourcePath(rawPath);
        var breakpointSourcePath = requestedSourcePath;
        var breakpointLine = line;
        if (sourceMaps is not null &&
            sourceMaps.TryMapToGenerated(requestedSourcePath, line, 1, out var generatedLocation))
        {
            breakpointSourcePath = generatedLocation.SourcePath;
            breakpointLine = generatedLocation.Line;
            breakpointDisplayOverrides[CreateBreakpointDisplayKey(breakpointSourcePath, breakpointLine)] =
                new(requestedSourcePath, line, 1);
        }

        session.SubmitCommand($"break {breakpointSourcePath}:{breakpointLine}");
        return true;
    }

    private bool TryHandleListCommand(string command)
    {
        if (!command.StartsWith("list", StringComparison.OrdinalIgnoreCase))
            return false;

        var radius = 5;
        if (command.Length > 4)
            if (!command.StartsWith("list(", StringComparison.OrdinalIgnoreCase) || !command.EndsWith(')') ||
                !int.TryParse(command[5..^1], out radius))
            {
                WriteLine("Usage: list(5)");
                return true;
            }

        PrintSourceListing(Math.Max(1, radius));
        return true;
    }

    private void HandleStopped(JsonElement payload)
    {
        var sourcePath = GetNestedString(payload, "sourceLocation", "sourcePath");
        var line = GetNestedInt(payload, "sourceLocation", "line") ?? 1;
        var column = GetNestedInt(payload, "sourceLocation", "column") ?? 1;
        if (sourcePath is not null)
        {
            sourcePath = Path.GetFullPath(sourcePath);
            if (sourceMaps is not null &&
                sourceMaps.TryMapToOriginal(sourcePath, line, column, out var mappedLocation))
            {
                sourcePath = mappedLocation.SourcePath;
                line = mappedLocation.Line;
            }
        }

        currentStop = sourcePath is null ? null : new StopLocation(Path.GetFullPath(sourcePath), line);
        if (autoContinueTarget is { } target && currentStop is { } stop)
        {
            if (!string.Equals(stop.SourcePath, target.SourcePath, StringComparison.OrdinalIgnoreCase) ||
                stop.Line < target.Line)
            {
                session.SubmitCommand("stepover");
                return;
            }

            autoContinueTarget = null;
        }

        stopped = true;

        var displayPath = currentStop is null ? "<unknown>" : FormatDisplayPath(currentStop.SourcePath);
        var kind = GetString(payload, "kind") ?? string.Empty;
        if (string.Equals(kind, "entry", StringComparison.OrdinalIgnoreCase))
        {
            WriteLine($"Break on start in {displayPath}:{line}");
            PrintSourceListing(3);
            pauseReady.Set();
            return;
        }

        WriteLine($"break in {displayPath}:{line}");
        pauseReady.Set();
    }

    private void HandleBreakpoint(JsonElement payload)
    {
        var handleId = GetInt(payload, "handleId");
        var resolvedLine = GetInt(payload, "resolvedLine");
        var requestedLine = GetInt(payload, "requestedLine");
        var line = resolvedLine is > 0 ? resolvedLine : requestedLine;
        var resolvedColumn = GetInt(payload, "resolvedColumn") ?? 1;
        var path = GetString(payload, "resolvedSourcePath") ?? GetString(payload, "sourcePath");
        if (handleId is null || line is null || path is null)
            return;

        path = Path.GetFullPath(path);
        if (breakpointDisplayOverrides.TryGetValue(CreateBreakpointDisplayKey(path, line.Value),
                out var displayOverride))
        {
            path = displayOverride.SourcePath;
            line = displayOverride.Line;
        }
        else if (sourceMaps is not null &&
                 sourceMaps.TryMapToOriginal(path, line.Value, resolvedColumn, out var mappedLocation))
        {
            path = mappedLocation.SourcePath;
            line = mappedLocation.Line;
        }

        if (!breakpoints.ContainsKey(handleId.Value))
            breakpointOrder.Add(handleId.Value);

        breakpoints[handleId.Value] = new(handleId.Value, Path.GetFullPath(path), line.Value);
    }

    private void HandleBreakpointCleared(JsonElement payload)
    {
        var handleId = GetInt(payload, "handleId");
        if (handleId is null)
            return;

        breakpoints.Remove(handleId.Value);
        breakpointOrder.Remove(handleId.Value);
    }

    private void PrintBreakpoints()
    {
        if (breakpointOrder.Count == 0)
        {
            WriteLine("(no breakpoints)");
            return;
        }

        for (var i = 0; i < breakpointOrder.Count; i++)
        {
            var entry = breakpoints[breakpointOrder[i]];
            WriteLine($"#{i} {FormatDisplayPath(entry.SourcePath)}:{entry.Line}");
        }
    }

    private void PrintSourceListing(int radius)
    {
        if (!stopped || currentStop is not { } stop)
        {
            WriteLine("Program is not paused.");
            return;
        }

        if (!File.Exists(stop.SourcePath))
        {
            WriteLine($"Source file not found: {stop.SourcePath}");
            return;
        }

        var lines = File.ReadAllLines(stop.SourcePath);
        var totalCount = Math.Max(1, radius);
        var start = Math.Max(1, stop.Line - totalCount / 2);
        var end = Math.Min(lines.Length, start + totalCount - 1);
        start = Math.Max(1, end - totalCount + 1);

        for (var lineNumber = start; lineNumber <= end; lineNumber++)
        {
            var marker = lineNumber == stop.Line ? ">" : " ";
            WriteLine($"{marker} {lineNumber,4} {lines[lineNumber - 1]}");
        }
    }

    private bool TryContinueToUserBreakpoint()
    {
        if (!stopped || currentStop is not { } stop)
            return false;

        BreakpointEntry? target = null;
        for (var i = 0; i < breakpointOrder.Count; i++)
        {
            var candidate = breakpoints[breakpointOrder[i]];
            if (!string.Equals(candidate.SourcePath, stop.SourcePath, StringComparison.OrdinalIgnoreCase))
                continue;
            if (candidate.Line <= stop.Line)
                continue;

            target = candidate;
            break;
        }

        if (target is null)
            return false;

        autoContinueTarget = target;
        stopped = false;
        session.SubmitCommand("stepover");
        return true;
    }

    private static bool IsResumeCommand(string command)
    {
        return string.Equals(command, "si", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(command, "stepin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(command, "step", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(command, "stepover", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(command, "so", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(command, "su", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(command, "stepout", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveSourcePath(string rawPath)
    {
        if (Path.IsPathRooted(rawPath))
            return Path.GetFullPath(rawPath);

        if (entrySourcePath is not null)
        {
            var candidate = Path.GetFullPath(rawPath, Path.GetDirectoryName(entrySourcePath)!);
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.GetFullPath(rawPath, workingDirectory);
    }

    private static string CreateBreakpointDisplayKey(string sourcePath, int line)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        if (OperatingSystem.IsWindows())
            sourcePath = sourcePath.ToUpperInvariant();
        return $"{sourcePath}|{line}";
    }

    private string FormatDisplayPath(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        if (fileName.Length != 0)
            return fileName;

        return sourcePath;
    }

    private void WriteLine(string text)
    {
        lock (consoleGate)
        {
            Console.WriteLine(text);
        }
    }

    private static bool TryParseJson(string line, out JsonElement payload)
    {
        payload = default;
        if (!line.TrimStart().StartsWith('{'))
            return false;

        try
        {
            using var document = JsonDocument.Parse(line);
            payload = document.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? GetNestedString(JsonElement element, string parentPropertyName, string propertyName)
    {
        return element.TryGetProperty(parentPropertyName, out var parent)
               && parent.ValueKind == JsonValueKind.Object
               && parent.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static int? GetNestedInt(JsonElement element, string parentPropertyName, string propertyName)
    {
        return element.TryGetProperty(parentPropertyName, out var parent)
               && parent.ValueKind == JsonValueKind.Object
               && parent.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private sealed record BreakpointEntry(int HandleId, string SourcePath, int Line);

    private sealed record StopLocation(string SourcePath, int Line);
}
