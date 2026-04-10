using Okojo.Bytecode;

namespace Okojo.Runtime;

/// <summary>
///     Periodic execution state observed by debugger and constraint hooks.
/// </summary>
public readonly struct ExecutionCheckpoint(
    ExecutionCheckpointKind kind,
    ulong executedInstructions,
    int programCounter,
    int stackDepth,
    JsOpCode? currentOpcode,
    StackFrameInfo currentFrameInfo,
    string? sourcePath = null,
    JsScript? script = null,
    IReadOnlyList<StackFrameInfo>? stackFrames = null,
    IReadOnlyList<JsLocalDebugInfo>? locals = null,
    IReadOnlyList<PausedLocalValue>? localValues = null,
    IReadOnlyList<PausedScopeSnapshot>? scopeChain = null)
{
    public ExecutionCheckpointKind Kind { get; } = kind;
    public ulong ExecutedInstructions { get; } = executedInstructions;
    public int ProgramCounter { get; } = programCounter;
    public int StackDepth { get; } = stackDepth;
    public JsOpCode? CurrentOpcode { get; } = currentOpcode;
    public StackFrameInfo CurrentFrameInfo { get; } = currentFrameInfo;
    public string? SourcePath { get; } = sourcePath;
    public JsScript? Script { get; } = script;
    public IReadOnlyList<JsLocalDebugInfo>? Locals { get; } = locals;
    public IReadOnlyList<PausedLocalValue>? LocalValues { get; } = localValues;
    public IReadOnlyList<PausedScopeSnapshot>? ScopeChain { get; } = scopeChain;

    public CheckpointSourceLocation? SourceLocation => CurrentFrameInfo.HasSourceLocation
        ? new CheckpointSourceLocation(SourcePath, CurrentFrameInfo.SourceLine, CurrentFrameInfo.SourceColumn)
        : null;

    public string KindLabel => Kind switch
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
        _ => Kind.ToString().ToLowerInvariant()
    };

    public IReadOnlyList<StackFrameInfo> StackFrames => stackFrames ?? [CurrentFrameInfo];

    public ExecutionCheckpoint WithKind(ExecutionCheckpointKind kind)
    {
        return new(kind, ExecutedInstructions, ProgramCounter, StackDepth, CurrentOpcode, CurrentFrameInfo,
            SourcePath, Script, stackFrames, Locals, LocalValues, ScopeChain);
    }

    public PausedExecutionSnapshot ToPausedSnapshot()
    {
        return new(
            Kind,
            KindLabel,
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
        return ToPausedSnapshot().GetDebuggerStopSummary();
    }

    public bool TryGetLocalValue(string name, out PausedLocalValue value)
    {
        var snapshot = ToPausedSnapshot();
        return snapshot.TryGetLocalValue(name, out value);
    }
}
