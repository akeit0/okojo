using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Okojo.Bytecode;
using Okojo.Diagnostics;
using Okojo.Objects;
using Okojo.Runtime;
using Okojo.SourceMaps;
using Okojo.Values;

namespace Okojo.DebugServer;

public sealed class DebuggerSession : IDebuggerSession
{
    private static readonly StringComparison SSourcePathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly JsAgent agent;
    private readonly DebugServerOptions options;
    private readonly BlockingCollection<DebuggerCommand> commands = new();
    private readonly SourceMapRegistry? sourceMapRegistry;
    private readonly object breakpointGate = new();
    private readonly List<JsBreakpointHandle> breakpointHandles = new();
    private readonly Dictionary<int, BreakpointSpec> originalBreakpointRequestsByHandleId = new();
    private readonly object consoleGate = new();
    private readonly Action<string> outputLine;

    private volatile bool stopRequested;
    private volatile bool stepPending;
    private DebuggerStepMode? stepMode;
    private int stepStartStackDepth;
    private int stepStartProgramCounter;
    private CheckpointSourceLocation? stepStartLocation;
    private CheckpointSourceLocation? stepTargetLocation;
    private PausedExecutionSnapshot? lastSnapshot;
    private DebugStepGranularity stepGranularity;
    private bool traceCommands;

    public DebuggerSession(JsAgent agent, DebugServerOptions options, Action<string>? outputLine = null)
    {
        this.agent = agent;
        this.options = options;
        this.outputLine = outputLine ?? Console.WriteLine;
        sourceMapRegistry = agent.Engine.SourceMapRegistry;
        stepGranularity = options.StepGranularity;
        traceCommands = Environment.GetEnvironmentVariable("OKOJO_DEBUG_TRACE_COMMANDS") == "1";
        agent.SubscribeBreakpointResolved(HandleBreakpointResolved);
    }

    public void RunCommandLoop()
    {
        while (!stopRequested)
        {
            string? line = Console.ReadLine();
            if (line is null)
                break;

            HandleCommand(line.Trim());
        }
    }

    public void StopCommandLoop()
    {
        stopRequested = true;
        commands.CompleteAdding();
    }

    public void SubmitCommand(string commandLine)
    {
        HandleCommand(commandLine.Trim());
    }

    public void OnCheckpoint(in ExecutionCheckpoint checkpoint)
    {
        lastSnapshot = checkpoint.ToPausedSnapshot();

        if (!ShouldPause(in checkpoint))
            return;

        var pauseCheckpoint = lastSnapshot.Value;
        if (stepPending && ShouldRewriteStepKind(pauseCheckpoint.Kind))
            pauseCheckpoint = pauseCheckpoint.WithKind(ExecutionCheckpointKind.Step);

        lastSnapshot = pauseCheckpoint;
        PublishStopped(pauseCheckpoint);
        stepPending = false;
        stepMode = null;
        stepStartLocation = null;
        stepTargetLocation = null;
        stepStartProgramCounter = 0;
        WaitForResume();
    }

    public void PublishError(Exception ex)
    {
        WriteJson(new JsonObject
        {
            ["event"] = "error",
            ["type"] = ex.GetType().FullName,
            ["message"] = ex.Message,
            ["stack"] = ex.StackTrace
        });
    }

    public void PublishTerminated(int exitCode)
    {
        WriteJson(new JsonObject
        {
            ["event"] = "terminated",
            ["exitCode"] = exitCode
        });
    }

    public bool PublishEntryStopped(string sourcePath)
    {
        var normalized = Path.GetFullPath(sourcePath);
        var sourceLocation = RemapSourceLocation(normalized, 1, 1) ?? new SourceMapLocation(normalized, 1, 1);
        var currentFrame = new StackFrameInfo(
            "<entry>",
            0,
            CallFrameKind.ScriptFrame,
            CallFrameFlag.None,
            false,
            GeneratorState.SuspendedStart,
            -1,
            true,
            sourceLocation.Line,
            sourceLocation.Column,
            sourceLocation.SourcePath);
        lastSnapshot = new PausedExecutionSnapshot(
            ExecutionCheckpointKind.DebuggerStatement,
            "entry",
            0,
            0,
            0,
            null,
            currentFrame,
            sourceLocation.SourcePath,
            null,
            new CheckpointSourceLocation(sourceLocation.SourcePath, sourceLocation.Line, sourceLocation.Column),
            [currentFrame],
            Array.Empty<JsLocalDebugInfo>(),
            Array.Empty<PausedLocalValue>(),
            Array.Empty<PausedScopeSnapshot>());
        var frame = new JsonObject
        {
            ["functionName"] = "<entry>",
            ["programCounter"] = 0,
            ["frameKind"] = "Entry",
            ["flags"] = "None",
            ["hasGeneratorState"] = false,
            ["generatorState"] = "SuspendedStart",
            ["generatorSuspendId"] = -1,
            ["hasSourceLocation"] = true,
            ["sourceLine"] = sourceLocation.Line,
            ["sourceColumn"] = sourceLocation.Column,
            ["sourcePath"] = sourceLocation.SourcePath
        };

        WriteJson(new JsonObject
        {
            ["event"] = "stopped",
            ["kind"] = "entry",
            ["summary"] = $"entry at {Path.GetFileName(sourceLocation.SourcePath)}",
            ["sourceLocation"] = CreateSourceLocationNode(new CheckpointSourceLocation(sourceLocation.SourcePath, sourceLocation.Line, sourceLocation.Column)),
            ["currentFrame"] = frame.DeepClone(),
            ["stackFrames"] = new JsonArray(frame),
            ["locals"] = new JsonArray(),
            ["localValues"] = new JsonArray(),
            ["scopeChain"] = new JsonArray()
        });

        return WaitForResume();
    }

    public JsBreakpointHandle AddBreakpoint(string sourcePath, int line)
    {
        var requested = new BreakpointSpec(Path.GetFullPath(sourcePath), line);
        var resolved = ResolveBreakpointRequest(requested);
        var handle = agent.AddBreakpoint(resolved.SourcePath, resolved.Line);
        if (!string.Equals(requested.SourcePath, resolved.SourcePath, SSourcePathComparison) || requested.Line != resolved.Line)
            originalBreakpointRequestsByHandleId[handle.HandleId] = requested;
        RegisterBreakpointHandle(handle);
        return handle;
    }

    public void RegisterBreakpointHandle(JsBreakpointHandle handle)
    {
        lock (breakpointGate)
            breakpointHandles.Add(handle);
        WriteJson(CreateBreakpointPayload("breakpoint-added", handle));
    }

    private void HandleCommand(string commandLine)
    {
        if (commandLine.Length == 0)
            return;

        if (traceCommands)
            Console.Error.WriteLine($"[okojo] host command {commandLine}");

        string[] parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return;

        switch (parts[0].ToLowerInvariant())
        {
            case "c":
            case "continue":
                stepPending = false;
                stepMode = null;
                stepStartLocation = null;
                stepTargetLocation = null;
                stepStartProgramCounter = 0;
                Enqueue(DebuggerCommand.Continue);
                return;
            case "si":
            case "stepin":
                if (options.CheckInterval == ulong.MaxValue)
                {
                    PublishError(new InvalidOperationException("Step commands require --check-interval."));
                    return;
                }
                stepStartProgramCounter = lastSnapshot?.ProgramCounter ?? 0;
                stepStartLocation = lastSnapshot?.SourceLocation;
                stepStartStackDepth = lastSnapshot?.StackDepth ?? 0;
                stepTargetLocation = ResolveNextLineTarget(lastSnapshot);
                stepMode = DebuggerStepMode.Into;
                stepPending = true;
                Enqueue(DebuggerCommand.Continue);
                return;
            case "step":
            case "stepover":
            case "so":
                if (options.CheckInterval == ulong.MaxValue)
                {
                    PublishError(new InvalidOperationException("Step commands require --check-interval."));
                    return;
                }
                stepStartProgramCounter = lastSnapshot?.ProgramCounter ?? 0;
                stepStartLocation = lastSnapshot?.SourceLocation;
                stepStartStackDepth = lastSnapshot?.StackDepth ?? 0;
                stepTargetLocation = ResolveNextLineTarget(lastSnapshot);
                stepMode = DebuggerStepMode.Over;
                stepPending = true;
                Enqueue(DebuggerCommand.Continue);
                return;
            case "su":
            case "stepout":
                if (options.CheckInterval == ulong.MaxValue)
                {
                    PublishError(new InvalidOperationException("Step commands require --check-interval."));
                    return;
                }
                stepStartProgramCounter = lastSnapshot?.ProgramCounter ?? 0;
                stepStartLocation = lastSnapshot?.SourceLocation;
                stepStartStackDepth = lastSnapshot?.StackDepth ?? 0;
                stepTargetLocation = null;
                stepMode = DebuggerStepMode.Out;
                stepPending = true;
                Enqueue(DebuggerCommand.Continue);
                return;
            case "q":
            case "quit":
                Enqueue(DebuggerCommand.Quit);
                return;
            case "b":
            case "bytecode":
            case "disasm":
            case "disassemble":
                PublishBytecodeDump();
                return;
            case "bp":
            case "break":
                if (parts.Length >= 2)
                {
                    try
                    {
                        var breakpoint = ParseBreakpoint(parts[1]);
                        AddBreakpoint(breakpoint.SourcePath, breakpoint.Line);
                    }
                    catch (Exception ex)
                    {
                        PublishError(ex);
                    }
                }

                return;
            case "clear":
            case "clearbreakpoint":
                if (parts.Length >= 2 && int.TryParse(parts[1], out var handleId))
                {
                    ClearBreakpoint(handleId);
                    WriteJson(new JsonObject
                    {
                        ["event"] = "breakpoint-cleared",
                        ["handleId"] = handleId
                    });
                }

                return;
            case "evaluate":
            case "eval":
                HandleEvaluateCommand(commandLine);
                return;
            case "toggle":
                if (parts.Length >= 2)
                {
                    ToggleDebuggerOption(parts[1]);
                    return;
                }

                break;
            case "stepmode":
            case "stepgranularity":
                if (parts.Length >= 2)
                {
                    SetStepGranularity(parts[1]);
                    return;
                }

                break;
            case "help":
                WriteJson(new JsonObject
                {
                    ["event"] = "help",
                    ["commands"] = CreateStringArray(
                        "continue|c",
                        "step|stepover|so (line-by-line, requires --check-interval)",
                        "stepin|si (step into, requires --check-interval)",
                        "stepout|su (requires --check-interval)",
                        "stepmode <line|instruction>",
                        "bytecode|disasm (shows bytecode for the current stop)",
                        "break|bp <sourcePath:line>",
                        "clear|clearbreakpoint <handleId>",
                        "evaluate|eval <requestId> <frameId> <json-string-expression>",
                        "toggle <debugger|breakpoint|call|return|pump|suspend|resume>",
                        "quit|q")
                });
                return;
        }

        WriteJson(new JsonObject
        {
            ["event"] = "unknown-command",
            ["command"] = commandLine
        });
    }

    private void Enqueue(DebuggerCommand command)
    {
        try
        {
            commands.Add(command);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private bool ShouldPause(in ExecutionCheckpoint checkpoint)
    {
        if (stepPending && stepMode is { } mode)
        {
            var snapshot = checkpoint.ToPausedSnapshot();
            if (ShouldPauseForExplicitStop(snapshot, checkpoint.Kind))
                return true;

            return ShouldPauseForStep(snapshot, mode);
        }

        return checkpoint.Kind switch
        {
            ExecutionCheckpointKind.Step => true,
            ExecutionCheckpointKind.DebuggerStatement => options.StopOnDebuggerStatement,
            ExecutionCheckpointKind.Breakpoint => options.StopOnBreakpoint,
            ExecutionCheckpointKind.Call => options.StopOnCall,
            ExecutionCheckpointKind.Return => options.StopOnReturn,
            ExecutionCheckpointKind.Pump => options.StopOnPump,
            ExecutionCheckpointKind.SuspendGenerator => options.StopOnSuspendGenerator,
            ExecutionCheckpointKind.ResumeGenerator => options.StopOnResumeGenerator,
            ExecutionCheckpointKind.Periodic => options.StopOnPeriodic,
            _ => false
        };
    }

    private bool ShouldPauseForExplicitStop(PausedExecutionSnapshot snapshot, ExecutionCheckpointKind kind)
    {
        bool shouldPause = kind switch
        {
            ExecutionCheckpointKind.DebuggerStatement => options.StopOnDebuggerStatement,
            ExecutionCheckpointKind.Breakpoint => options.StopOnBreakpoint,
            _ => false
        };

        if (!shouldPause)
            return false;

        if (!stepPending || stepGranularity != DebugStepGranularity.Line || stepStartLocation is null)
            return true;

        return !IsSameStepLine(snapshot);
    }

    private bool IsSameStepLine(PausedExecutionSnapshot snapshot)
    {
        if (snapshot.StackDepth != stepStartStackDepth)
            return false;

        if (!TryGetExactStepLocation(snapshot, out var current))
            return false;

        var start = stepStartLocation!.Value;
        return string.Equals(start.SourcePath, current.SourcePath, SSourcePathComparison) &&
               start.Line == current.Line;
    }

    private static bool ShouldRewriteStepKind(ExecutionCheckpointKind kind)
    {
        return kind is not ExecutionCheckpointKind.Step
            and not ExecutionCheckpointKind.DebuggerStatement
            and not ExecutionCheckpointKind.Breakpoint;
    }

    private bool ShouldPauseForStep(PausedExecutionSnapshot snapshot, DebuggerStepMode mode)
    {
        return mode switch
        {
            DebuggerStepMode.Into => ShouldPauseForStepInto(snapshot),
            DebuggerStepMode.Over => ShouldPauseForStepOver(snapshot),
            DebuggerStepMode.Out => snapshot.StackDepth < stepStartStackDepth &&
                                    (stepGranularity == DebugStepGranularity.Instruction || HasMovedToNewLine(snapshot)),
            _ => false
        };
    }

    private bool ShouldPauseForStepInto(PausedExecutionSnapshot snapshot)
    {
        if (TryMatchStepTargetLocation(snapshot))
            return true;

        return stepGranularity == DebugStepGranularity.Line
            ? HasMovedToNewLine(snapshot)
            : HasMovedToNewInstruction(snapshot);
    }

    private bool ShouldPauseForStepOver(PausedExecutionSnapshot snapshot)
    {
        if (TryMatchStepTargetLocation(snapshot))
            return true;

        if (snapshot.StackDepth < stepStartStackDepth)
            return stepGranularity == DebugStepGranularity.Instruction
                ? true
                : stepTargetLocation is null && HasMovedToAcceptableUnwoundLine(snapshot);

        return snapshot.StackDepth == stepStartStackDepth &&
               (stepGranularity == DebugStepGranularity.Line
                   ? HasMovedToNewLine(snapshot)
                   : HasMovedToNewInstruction(snapshot));
    }

    private bool HasMovedToNewLine(PausedExecutionSnapshot snapshot)
    {
        if (!TryGetExactStepLocation(snapshot, out var current))
            return false;
        if (stepStartLocation is null)
            return true;

        var start = stepStartLocation.Value;
        return !string.Equals(start.SourcePath, current.SourcePath, SSourcePathComparison) ||
               start.Line != current.Line;
    }

    private bool HasMovedToNewInstruction(PausedExecutionSnapshot snapshot)
    {
        return snapshot.ProgramCounter != stepStartProgramCounter ||
               snapshot.StackDepth != stepStartStackDepth;
    }

    private bool HasMovedToAcceptableUnwoundLine(PausedExecutionSnapshot snapshot)
    {
        if (!HasMovedToNewLine(snapshot))
            return false;
        if (!TryGetExactStepLocation(snapshot, out var current) || stepStartLocation is null)
            return false;

        var start = stepStartLocation.Value;
        if (!string.Equals(start.SourcePath, current.SourcePath, SSourcePathComparison))
            return true;

        return current.Line > start.Line;
    }

    private bool TryMatchStepTargetLocation(PausedExecutionSnapshot snapshot)
    {
        if (stepGranularity != DebugStepGranularity.Line || stepTargetLocation is not { } target)
            return false;
        if (snapshot.StackDepth != stepStartStackDepth)
            return false;
        if (!TryGetExactStepLocation(snapshot, out var current))
            return false;

        return string.Equals(target.SourcePath, current.SourcePath, SSourcePathComparison) &&
               current.Line >= target.Line;
    }

    private void PublishStopped(PausedExecutionSnapshot snapshot)
    {
        WriteJson(new JsonObject
        {
            ["event"] = "stopped",
            ["kind"] = snapshot.KindLabel,
            ["summary"] = snapshot.GetDebuggerStopSummary(),
            ["sourceLocation"] = CreateSourceLocationNode(snapshot.SourceLocation),
            ["currentFrame"] = SnapshotFrame(snapshot.CurrentFrameInfo),
            ["stackFrames"] = CreateObjectArray(snapshot.StackFrames, SnapshotFrame),
            ["locals"] = CreateObjectArray(snapshot.Locals, SnapshotLocal),
            ["localValues"] = CreateObjectArray(snapshot.LocalValues, SnapshotLocalValue),
            ["scopeChain"] = CreateObjectArray(snapshot.ScopeChain, SnapshotScope)
        });
    }

    private void PublishBytecodeDump()
    {
        var snapshot = lastSnapshot;
        if (snapshot is not { } paused)
        {
            WriteJson(new JsonObject
            {
                ["event"] = "error",
                ["type"] = "InvalidOperationException",
                ["message"] = "No paused stop is available for bytecode viewing."
            });
            return;
        }

        if (!TryBuildBytecodeDump(paused, out var title, out var text))
        {
            WriteJson(new JsonObject
            {
                ["event"] = "error",
                ["type"] = "InvalidOperationException",
                ["message"] = "Unable to resolve the current paused script for bytecode viewing."
            });
            return;
        }

        WriteJson(new JsonObject
        {
            ["event"] = "bytecode",
            ["title"] = title,
            ["sourcePath"] = paused.SourcePath,
            ["sourceLocation"] = CreateSourceLocationNode(paused.SourceLocation),
            ["programCounter"] = paused.ProgramCounter,
            ["text"] = text
        });
    }

    private void HandleBreakpointResolved(JsBreakpointHandle handle)
    {
        WriteJson(CreateBreakpointPayload("breakpoint-updated", handle));
    }

    private void HandleEvaluateCommand(string commandLine)
    {
        int? requestId = TryGetEvaluateRequestId(commandLine);

        try
        {
            var payload = ParseEvaluateCommand(commandLine);
            if (lastSnapshot is not { } snapshot)
            {
                WriteJson(new JsonObject
                {
                    ["event"] = "evaluate",
                    ["requestId"] = payload.RequestId,
                    ["success"] = false,
                    ["message"] = "No paused stop is available for evaluation."
                });
                return;
            }

            JsValue result = EvaluatePausedExpression(snapshot, payload.Expression, payload.FrameId);
            WriteJson(new JsonObject
            {
                ["event"] = "evaluate",
                ["requestId"] = payload.RequestId,
                ["success"] = true,
                ["expression"] = payload.Expression,
                ["result"] = JsValueDebugString.FormatValue(result)
            });
        }
        catch (Exception ex)
        {
            WriteJson(new JsonObject
            {
                ["event"] = "evaluate",
                ["requestId"] = requestId,
                ["success"] = false,
                ["message"] = ex.Message
            });
        }
    }

    private bool TryBuildBytecodeDump(PausedExecutionSnapshot snapshot, out string title, out string text)
    {
        title = string.Empty;
        text = string.Empty;

        string sourcePath = snapshot.SourcePath
            ?? snapshot.CurrentFrameInfo.SourcePath
            ?? snapshot.Script?.SourcePath
            ?? string.Empty;

        JsScript? selectedScript = snapshot.Script;
        if (selectedScript is null)
        {
            var scripts = agent.ScriptDebugRegistry.GetRegisteredScripts(sourcePath);
            if (scripts.Count == 0)
                scripts = agent.ScriptDebugRegistry.GetAllRegisteredScripts();

            foreach (var script in scripts)
            {
                if (snapshot.SourceLocation is { } sourceLocationInfo &&
                    script.TryGetSourceLocationAtPc(snapshot.ProgramCounter, out int line, out int column) &&
                    line == sourceLocationInfo.Line &&
                    column == sourceLocationInfo.Column)
                {
                    selectedScript = script;
                    break;
                }

                if (selectedScript is null && string.Equals(script.SourcePath, sourcePath, SSourcePathComparison))
                    selectedScript = script;

                selectedScript ??= script;
            }
        }

        if (selectedScript is null)
            return false;

        int highlightedProgramCounter = ResolveHighlightedProgramCounter(snapshot, selectedScript);

        var options = new DisassemblerOptions
        {
            UnitKind = snapshot.CurrentFrameInfo.FrameKind.ToString().ToLowerInvariant(),
            UnitName = snapshot.CurrentFrameInfo.FunctionName,
            ContextSlots = 0,
            HighlightedProgramCounter = highlightedProgramCounter
        };
        string locationSuffix = snapshot.SourceLocation is { } stopLocationInfo
            ? $" @ {stopLocationInfo.SourcePath}:{stopLocationInfo.Line}:{stopLocationInfo.Column}"
            : string.Empty;
        string fileName = sourcePath.Length > 0 ? Path.GetFileName(sourcePath) : "<anonymous>";
        title = highlightedProgramCounter == snapshot.ProgramCounter
            ? $"{fileName} @ {snapshot.CurrentFrameInfo.FunctionName}{locationSuffix} pc {snapshot.ProgramCounter}"
            : $"{fileName} @ {snapshot.CurrentFrameInfo.FunctionName}{locationSuffix} pc {snapshot.ProgramCounter} (highlight {highlightedProgramCounter})";
        text = Disassembler.Dump(selectedScript!, options, ResolveInstructionOverride);
        return true;
    }

    private static int ResolveHighlightedProgramCounter(PausedExecutionSnapshot snapshot, JsScript script)
    {
        if (snapshot.Kind != ExecutionCheckpointKind.CaughtException)
            return snapshot.ProgramCounter;
        if (snapshot.SourceLocation is not { } sourceLocation)
            return snapshot.ProgramCounter;
        if (script.TryGetExactSourceLocationAtPc(snapshot.ProgramCounter, out int line, out int column) &&
            line == sourceLocation.Line &&
            column == sourceLocation.Column)
        {
            return snapshot.ProgramCounter;
        }

        if (TryFindPcForExactSourceLocation(script, sourceLocation, out int highlightedPc))
            return highlightedPc;

        return snapshot.ProgramCounter;
    }

    private static bool TryFindPcForExactSourceLocation(
        JsScript script,
        CheckpointSourceLocation sourceLocation,
        out int programCounter)
    {
        programCounter = -1;

        if (script.DebugPcOffsets is not { Length: > 0 } pcOffsets)
            return false;

        for (int i = 0; i < pcOffsets.Length; i++)
        {
            int candidatePc = pcOffsets[i];
            if (!script.TryGetExactSourceLocationAtPc(candidatePc, out int line, out int column))
                continue;
            if (line != sourceLocation.Line || column != sourceLocation.Column)
                continue;

            programCounter = candidatePc;
            return true;
        }

        return false;
    }

    private (JsOpCode OpCode, byte[] Operands)? ResolveInstructionOverride(JsScript script, int pc)
    {
        return agent.TryGetOriginalBreakpointInstruction(script, pc, out var originalInstruction)
            ? (originalInstruction.OpCode, originalInstruction.Operands)
            : null;
    }

    private JsValue EvaluatePausedExpression(PausedExecutionSnapshot snapshot, string expression, int frameId)
    {
        var path = ParseExpressionPath(expression);
        if (path.Count == 0)
            throw new InvalidOperationException("Expression is empty.");

        JsValue current = ResolveExpressionRoot(snapshot, path[0], frameId);
        for (int i = 1; i < path.Count; i++)
        {
            if (!current.IsObject)
                throw new InvalidOperationException($"'{path[i - 1]}' is not an object.");

            var obj = current.AsObject();
            if (!obj.TryGetProperty(path[i], out current))
                throw new InvalidOperationException($"Property '{path[i]}' is not available.");
        }

        return current;
    }

    private JsValue ResolveExpressionRoot(PausedExecutionSnapshot snapshot, string name, int frameId)
    {
        if (string.Equals(name, "globalThis", StringComparison.Ordinal))
            return JsValue.FromObject(agent.MainRealm.GlobalObject);

        if (TryGetValue(snapshot, name, frameId, out var value))
            return value;

        if (agent.MainRealm.GlobalObject.TryGetProperty(name, out value))
            return value;

        throw new InvalidOperationException($"Identifier '{name}' is not available in the current pause.");
    }

    private static bool TryGetValue(PausedExecutionSnapshot snapshot, string name, int frameId, out JsValue value)
    {
        var scopeChain = snapshot.ScopeChain ?? [];
        int index = Math.Clamp(frameId - 1, 0, Math.Max(0, scopeChain.Count - 1));
        if (scopeChain.Count > 0)
        {
            for (int i = index; i < scopeChain.Count; i++)
            {
                if (scopeChain[i].TryGetLocalValue(name, out var local))
                {
                    value = local.Value;
                    return true;
                }
            }
        }

        if (snapshot.TryGetLocalValue(name, out var snapshotLocal))
        {
            value = snapshotLocal.Value;
            return true;
        }

        value = default;
        return false;
    }

    private bool TryGetExactStepLocation(PausedExecutionSnapshot snapshot, out CheckpointSourceLocation location)
    {
        if (snapshot.Script is { } script)
        {
            if (script.TryGetExactSourceLocationAtPc(snapshot.ProgramCounter, out int exactLine, out int exactColumn))
            {
                location = new CheckpointSourceLocation(
                    snapshot.Script.SourcePath ?? snapshot.SourcePath,
                    exactLine,
                    exactColumn);
                return true;
            }

            location = default;
            return false;
        }

        string sourcePath = snapshot.SourcePath
            ?? snapshot.CurrentFrameInfo.SourcePath
            ?? string.Empty;
        if (sourcePath.Length > 0)
        {
            foreach (var candidate in agent.ScriptDebugRegistry.GetRegisteredScripts(sourcePath))
            {
                if (!candidate.TryGetExactSourceLocationAtPc(snapshot.ProgramCounter, out int candidateLine, out int candidateColumn))
                    continue;

                location = new CheckpointSourceLocation(candidate.SourcePath ?? sourcePath, candidateLine, candidateColumn);
                return true;
            }
        }

        location = default;
        return false;
    }

    private CheckpointSourceLocation? ResolveNextLineTarget(PausedExecutionSnapshot? snapshot)
    {
        if (stepGranularity != DebugStepGranularity.Line || snapshot is not { } paused)
            return null;
        if (paused.Script is not { DebugPcOffsets: { Length: > 0 } pcOffsets } script)
            return null;
        if (paused.SourceLocation is not { } start)
            return null;

        for (int i = 0; i < pcOffsets.Length; i++)
        {
            int pc = pcOffsets[i];
            if (pc <= paused.ProgramCounter)
                continue;
            if (!script.TryGetExactSourceLocationAtPc(pc, out int line, out int column))
                continue;
            if (line == start.Line)
                continue;

            return new CheckpointSourceLocation(script.SourcePath ?? start.SourcePath, line, column);
        }

        return null;
    }

    private void ToggleDebuggerOption(string optionName)
    {
        bool enabled;
        switch (optionName.ToLowerInvariant())
        {
            case "debugger":
            case "statement":
                enabled = ToggleHook(agent.IsDebuggerStatementHookEnabled,
                    agent.EnableDebuggerStatementHook, agent.DisableDebuggerStatementHook);
                break;
            case "breakpoint":
                enabled = ToggleHook(agent.IsBreakpointHookEnabled,
                    agent.EnableBreakpointHook, agent.DisableBreakpointHook);
                break;
            case "call":
                enabled = ToggleHook(agent.IsCallHookEnabled,
                    agent.EnableCallHook, agent.DisableCallHook);
                break;
            case "return":
                enabled = ToggleHook(agent.IsReturnHookEnabled,
                    agent.EnableReturnHook, agent.DisableReturnHook);
                break;
            case "pump":
                enabled = ToggleHook(agent.IsPumpHookEnabled,
                    agent.EnablePumpHook, agent.DisablePumpHook);
                break;
            case "suspend":
                enabled = ToggleHook(agent.IsSuspendGeneratorHookEnabled,
                    agent.EnableSuspendGeneratorHook, agent.DisableSuspendGeneratorHook);
                break;
            case "resume":
                enabled = ToggleHook(agent.IsResumeGeneratorHookEnabled,
                    agent.EnableResumeGeneratorHook, agent.DisableResumeGeneratorHook);
                break;
            default:
                WriteJson(new JsonObject
                {
                    ["event"] = "error",
                    ["type"] = "ArgumentException",
                    ["message"] = $"Unknown debugger option '{optionName}'."
                });
                return;
        }

        WriteJson(new JsonObject
        {
            ["event"] = "option-updated",
            ["name"] = optionName,
            ["enabled"] = enabled
        });
    }

    private void SetStepGranularity(string raw)
    {
        stepGranularity = raw.ToLowerInvariant() switch
        {
            "line" => DebugStepGranularity.Line,
            "instruction" => DebugStepGranularity.Instruction,
            "pc" => DebugStepGranularity.Instruction,
            _ => throw new ArgumentException($"Unknown step granularity '{raw}'.", nameof(raw))
        };

        WriteJson(new JsonObject
        {
            ["event"] = "option-updated",
            ["name"] = "stepGranularity",
            ["value"] = stepGranularity.ToString()
        });
    }

    private static bool ToggleHook(bool enabled, Action enable, Action disable)
    {
        if (enabled)
            disable();
        else
            enable();

        return !enabled;
    }

    private bool WaitForResume()
    {
        while (!stopRequested)
        {
            DebuggerCommand command;
            try
            {
                command = commands.Take();
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            switch (command)
            {
                case DebuggerCommand.Continue:
                    return true;
                case DebuggerCommand.Quit:
                    stopRequested = true;
                    agent.Terminate();
                    return false;
            }
        }

        return false;
    }

    private void WriteJson(JsonObject value)
    {
        lock (consoleGate)
        {
            outputLine(JsonSerializer.Serialize(value, DebuggerJsonContext.Default.JsonObject));
        }
    }

    private static JsonObject SnapshotFrame(StackFrameInfo frame)
    {
        return new JsonObject
        {
            ["functionName"] = frame.FunctionName,
            ["programCounter"] = frame.ProgramCounter,
            ["frameKind"] = frame.FrameKind.ToString(),
            ["flags"] = frame.Flags.ToString(),
            ["hasGeneratorState"] = frame.HasGeneratorState,
            ["generatorState"] = frame.GeneratorState.ToString(),
            ["generatorSuspendId"] = frame.GeneratorSuspendId,
            ["hasSourceLocation"] = frame.HasSourceLocation,
            ["sourceLine"] = frame.SourceLine,
            ["sourceColumn"] = frame.SourceColumn,
            ["sourcePath"] = frame.SourcePath
        };
    }

    private static BreakpointSpec ParseBreakpoint(string spec)
    {
        int colon = spec.LastIndexOf(':');
        if (colon <= 0 || colon == spec.Length - 1 || !int.TryParse(spec[(colon + 1)..], out int line))
            throw new ArgumentException("Breakpoint must be in the form sourcePath:line.", nameof(spec));

        string sourcePath = spec[..colon];
        if (!Path.IsPathRooted(sourcePath))
            sourcePath = Path.GetFullPath(sourcePath);
        else
            sourcePath = Path.GetFullPath(sourcePath);

        return new BreakpointSpec(sourcePath, line);
    }

    private BreakpointSpec ResolveBreakpointRequest(BreakpointSpec requested)
    {
        if (sourceMapRegistry is null)
            return requested;

        return sourceMapRegistry.TryMapToGenerated(requested.SourcePath, requested.Line, 1, out var generated)
            ? new BreakpointSpec(generated.SourcePath, generated.Line)
            : requested;
    }

    private JsonObject CreateBreakpointPayload(string eventName, JsBreakpointHandle handle)
    {
        var requested = originalBreakpointRequestsByHandleId.TryGetValue(handle.HandleId, out var originalRequested)
            ? originalRequested
            : new BreakpointSpec(
                handle.SourcePath
                ?? handle.ResolvedSourcePath
                ?? string.Empty,
                handle.Line);

        var resolved = ResolveBreakpointDisplayLocation(handle, requested);
        return new JsonObject
        {
            ["event"] = eventName,
            ["sourcePath"] = requested.SourcePath,
            ["requestedLine"] = requested.Line,
            ["handleId"] = handle.HandleId,
            ["verified"] = handle.IsVerified,
            ["resolvedSourcePath"] = resolved?.SourcePath,
            ["resolvedLine"] = resolved?.Line,
            ["resolvedColumn"] = resolved?.Column,
            ["programCounter"] = handle.ResolvedProgramCounter
        };
    }

    private SourceMapLocation? ResolveBreakpointDisplayLocation(JsBreakpointHandle handle, BreakpointSpec requested)
    {
        if (!handle.IsVerified)
            return null;

        if (!string.IsNullOrEmpty(handle.ResolvedSourcePath) &&
            handle.ResolvedLine > 0 &&
            sourceMapRegistry?.TryMapToOriginal(
                handle.ResolvedSourcePath,
                handle.ResolvedLine,
                Math.Max(1, handle.ResolvedColumn),
                out var mapped) == true)
        {
            return mapped;
        }

        if (originalBreakpointRequestsByHandleId.ContainsKey(handle.HandleId))
            return new SourceMapLocation(requested.SourcePath, requested.Line, 1);

        if (!string.IsNullOrEmpty(handle.ResolvedSourcePath) && handle.ResolvedLine > 0)
            return new SourceMapLocation(handle.ResolvedSourcePath, handle.ResolvedLine, Math.Max(1, handle.ResolvedColumn));

        return null;
    }

    private SourceMapLocation? RemapSourceLocation(string sourcePath, int line, int column)
    {
        if (sourceMapRegistry is null)
            return null;

        return sourceMapRegistry.TryMapToOriginal(sourcePath, line, column, out var mapped)
            ? mapped
            : null;
    }

    private static (int RequestId, int FrameId, string Expression) ParseEvaluateCommand(string commandLine)
    {
        int firstSpace = commandLine.IndexOf(' ');
        if (firstSpace < 0)
            throw new InvalidOperationException("Missing evaluate request id.");

        int secondSpace = commandLine.IndexOf(' ', firstSpace + 1);
        if (secondSpace < 0)
            throw new InvalidOperationException("Missing evaluate frame id.");

        int thirdSpace = commandLine.IndexOf(' ', secondSpace + 1);
        if (thirdSpace < 0)
            throw new InvalidOperationException("Missing evaluate expression.");

        if (!int.TryParse(commandLine[(firstSpace + 1)..secondSpace], out int requestId))
            throw new InvalidOperationException("Invalid evaluate request id.");
        if (!int.TryParse(commandLine[(secondSpace + 1)..thirdSpace], out int frameId) || frameId <= 0)
            throw new InvalidOperationException("Invalid evaluate frame id.");

        string expression = JsonSerializer.Deserialize(commandLine[(thirdSpace + 1)..], DebuggerJsonContext.Default.String) ?? string.Empty;
        return (requestId, frameId, expression);
    }

    private static JsonObject? CreateSourceLocationNode(CheckpointSourceLocation? sourceLocation)
    {
        if (sourceLocation is not { } value)
            return null;

        return new JsonObject
        {
            ["sourcePath"] = value.SourcePath,
            ["line"] = value.Line,
            ["column"] = value.Column
        };
    }

    private static JsonObject SnapshotLocal(JsLocalDebugInfo local)
    {
        return new JsonObject
        {
            ["name"] = local.Name,
            ["storageKind"] = local.StorageKind.ToString(),
            ["storageIndex"] = local.StorageIndex,
            ["startPc"] = local.StartPc,
            ["endPc"] = local.EndPc,
            ["flags"] = local.Flags.ToString()
        };
    }

    private static JsonObject SnapshotLocalValue(PausedLocalValue local)
    {
        return new JsonObject
        {
            ["name"] = local.Name,
            ["storageKind"] = local.StorageKind.ToString(),
            ["storageIndex"] = local.StorageIndex,
            ["value"] = local.Value.ToString(),
            ["startPc"] = local.StartPc,
            ["endPc"] = local.EndPc,
            ["flags"] = local.Flags.ToString()
        };
    }

    private static JsonObject SnapshotScope(PausedScopeSnapshot scope)
    {
        return new JsonObject
        {
            ["framePointer"] = scope.FramePointer,
            ["frameInfo"] = SnapshotFrame(scope.FrameInfo),
            ["locals"] = CreateObjectArray(scope.Locals, SnapshotLocal),
            ["localValues"] = CreateObjectArray(scope.LocalValues, SnapshotLocalValue)
        };
    }

    private static JsonArray CreateStringArray(params string[] values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add((JsonNode?)JsonValue.Create(value));
        return array;
    }

    private static JsonArray CreateObjectArray<T>(IEnumerable<T>? values, Func<T, JsonObject> projector)
    {
        var array = new JsonArray();
        if (values is null)
            return array;

        foreach (var value in values)
            array.Add((JsonNode?)projector(value));
        return array;
    }

    private static int? TryGetEvaluateRequestId(string commandLine)
    {
        int firstSpace = commandLine.IndexOf(' ');
        if (firstSpace < 0)
            return null;

        int secondSpace = commandLine.IndexOf(' ', firstSpace + 1);
        if (secondSpace < 0)
            return null;

        return int.TryParse(commandLine[(firstSpace + 1)..secondSpace], out int requestId)
            ? requestId
            : null;
    }

    private static List<string> ParseExpressionPath(string expression)
    {
        var segments = new List<string>(4);
        ReadOnlySpan<char> span = expression.AsSpan().Trim();
        int index = 0;

        while (index < span.Length)
        {
            SkipWhitespace(span, ref index);
            if (index >= span.Length)
                break;

            if (span[index] == '.')
            {
                index++;
                continue;
            }

            if (span[index] == '[')
            {
                index++;
                SkipWhitespace(span, ref index);
                if (index >= span.Length)
                    throw new InvalidOperationException("Unterminated bracket expression.");

                if (span[index] is '"' or '\'')
                {
                    char quote = span[index++];
                    int start = index;
                    while (index < span.Length && span[index] != quote)
                        index++;
                    if (index >= span.Length)
                        throw new InvalidOperationException("Unterminated string index.");
                    segments.Add(span[start..index].ToString());
                    index++;
                }
                else
                {
                    int start = index;
                    while (index < span.Length && span[index] != ']')
                        index++;
                    if (index >= span.Length)
                        throw new InvalidOperationException("Unterminated numeric index.");
                    segments.Add(span[start..index].ToString().Trim());
                }

                SkipWhitespace(span, ref index);
                if (index >= span.Length || span[index] != ']')
                    throw new InvalidOperationException("Unterminated bracket expression.");
                index++;
                continue;
            }

            int startIdentifier = index;
            if (!IsIdentifierStart(span[index]))
                throw new InvalidOperationException($"Unsupported expression token '{span[index]}'.");

            index++;
            while (index < span.Length && IsIdentifierPart(span[index]))
                index++;

            segments.Add(span[startIdentifier..index].ToString());
        }

        return segments;
    }

    private static void SkipWhitespace(ReadOnlySpan<char> span, ref int index)
    {
        while (index < span.Length && char.IsWhiteSpace(span[index]))
            index++;
    }

    private static bool IsIdentifierStart(char ch) =>
        ch == '_' || ch == '$' || char.IsLetter(ch);

    private static bool IsIdentifierPart(char ch) =>
        IsIdentifierStart(ch) || char.IsDigit(ch);

    private void ClearBreakpoint(int handleId)
    {
        lock (breakpointGate)
        {
            var handle = breakpointHandles.FirstOrDefault(h => h.HandleId == handleId);
            if (handle is null)
                return;
            if (handle.IsDisposed)
                return;

            handle.Dispose();
        }
    }

    private enum DebuggerCommand
    {
        Continue,
        Quit
    }
}
