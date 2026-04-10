using Okojo.Bytecode;

namespace Okojo.Runtime;

public readonly record struct PausedExecutionSnapshot(
    ExecutionCheckpointKind Kind,
    string KindLabel,
    ulong ExecutedInstructions,
    int ProgramCounter,
    int StackDepth,
    JsOpCode? CurrentOpcode,
    StackFrameInfo CurrentFrameInfo,
    string? SourcePath,
    JsScript? Script,
    CheckpointSourceLocation? SourceLocation,
    IReadOnlyList<StackFrameInfo> StackFrames,
    IReadOnlyList<JsLocalDebugInfo>? Locals,
    IReadOnlyList<PausedLocalValue>? LocalValues,
    IReadOnlyList<PausedScopeSnapshot>? ScopeChain)
{
    public bool MatchesStep(DebuggerStepMode mode, int startStackDepth)
    {
        return MatchesStep(mode, startStackDepth, null);
    }

    public bool MatchesStep(DebuggerStepMode mode, int startStackDepth, CheckpointSourceLocation? startLocation)
    {
        return mode switch
        {
            DebuggerStepMode.Into => MatchesLineStep(startLocation),
            DebuggerStepMode.Over => StackDepth <= startStackDepth && !IsCallLikeOpcode(CurrentOpcode),
            DebuggerStepMode.Out => StackDepth < startStackDepth,
            _ => false
        };
    }

    private static bool IsCallLikeOpcode(JsOpCode? opcode)
    {
        return opcode is JsOpCode.CallAny or JsOpCode.CallProperty or JsOpCode.CallUndefinedReceiver or
            JsOpCode.CallRuntime or JsOpCode.InvokeIntrinsic or JsOpCode.Construct;
    }

    private bool MatchesLineStep(CheckpointSourceLocation? startLocation)
    {
        if (startLocation is null || SourceLocation is null)
            return true;

        var current = SourceLocation.Value;
        var start = startLocation.Value;
        return !string.Equals(start.SourcePath, current.SourcePath, SourcePathComparer.Comparison) ||
               start.Line != current.Line;
    }

    public PausedExecutionSnapshot WithKind(ExecutionCheckpointKind kind)
    {
        return new(
            kind,
            kind switch
            {
                ExecutionCheckpointKind.Periodic => "periodic",
                ExecutionCheckpointKind.Call => "call",
                ExecutionCheckpointKind.Return => "return",
                ExecutionCheckpointKind.Pump => "pump",
                ExecutionCheckpointKind.DebuggerStatement => "debugger-statement",
                ExecutionCheckpointKind.SuspendGenerator => "suspend-generator",
                ExecutionCheckpointKind.ResumeGenerator => "resume-generator",
                ExecutionCheckpointKind.Breakpoint => "breakpoint",
                ExecutionCheckpointKind.Step => "step",
                ExecutionCheckpointKind.CaughtException => "caught-exception",
                _ => kind.ToString().ToLowerInvariant()
            },
            ExecutedInstructions,
            ProgramCounter,
            StackDepth,
            CurrentOpcode,
            CurrentFrameInfo,
            SourcePath,
            Script,
            SourceLocation,
            StackFrames,
            Locals,
            LocalValues,
            ScopeChain);
    }

    public string GetDebuggerStopSummary()
    {
        var location = SourceLocation;
        var locationSuffix = string.Empty;
        if (CurrentFrameInfo.HasSourceLocation && location is { } locationInfo)
            locationSuffix = locationInfo.SourcePath is not null
                ? $" @ {locationInfo.SourcePath}:{locationInfo.Line}:{locationInfo.Column}"
                : $" @ {locationInfo.Line}:{locationInfo.Column}";

        var generatorSuffix = CurrentFrameInfo.HasGeneratorState
            ? $" gen:{CurrentFrameInfo.GeneratorState} suspend:{CurrentFrameInfo.GeneratorSuspendId}"
            : string.Empty;
        return
            $"{KindLabel} at {CurrentFrameInfo.FunctionName}{locationSuffix} (pc:{CurrentFrameInfo.ProgramCounter}, kind:{CurrentFrameInfo.FrameKind}{generatorSuffix})";
    }

    public bool TryGetLocalValue(string name, out PausedLocalValue value)
    {
        var scopeChain = ScopeChain;
        if (scopeChain is not null)
            for (var i = 0; i < scopeChain.Count; i++)
                if (scopeChain[i].TryGetLocalValue(name, out value))
                    return true;

        value = default;
        return false;
    }
}
