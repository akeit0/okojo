using System.Runtime.CompilerServices;
using Okojo.Bytecode;

namespace Okojo.Runtime;

internal sealed class ExecutionCheckPolicy
{
    private readonly object gate = new();
    private ulong checkInterval = ulong.MaxValue;
    private IExecutionConstraint[] constraints = [];
    private IDebuggerSession? debuggerSession;
    private ulong executedInstructions;
    private CancellationToken executionCancellationToken;
    private DateTimeOffset? executionDeadline;
    private TimeSpan? executionTimeout;
    private ulong maxInstructions = ulong.MaxValue;

    internal ExecutionCheckPolicy(JsAgentOptions options, TimeProvider timeProvider)
    {
        checkInterval = options.CheckInterval;
        maxInstructions = options.MaxInstructions;
        executionTimeout = options.ExecutionTimeout;
        executionCancellationToken = options.ExecutionCancellationToken;
        if (executionTimeout is TimeSpan timeout)
            executionDeadline = timeProvider.GetUtcNow() + timeout;
        constraints = options.Constraints.Count == 0 ? [] : options.Constraints.ToArray();
        debuggerSession = options.DebuggerSession;
    }

    internal bool HasChecks
    {
        get
        {
            lock (gate)
            {
                return debuggerSession is not null ||
                       constraints.Length != 0 ||
                       executionDeadline is not null ||
                       executionCancellationToken.CanBeCanceled ||
                       maxInstructions != ulong.MaxValue ||
                       checkInterval != ulong.MaxValue;
            }
        }
    }

    internal bool HasPeriodicChecks
    {
        get
        {
            lock (gate)
            {
                return constraints.Length != 0 ||
                       executionDeadline is not null ||
                       executionCancellationToken.CanBeCanceled ||
                       maxInstructions != ulong.MaxValue ||
                       checkInterval != ulong.MaxValue;
            }
        }
    }

    internal ulong CheckInterval
    {
        get
        {
            lock (gate)
            {
                return checkInterval;
            }
        }
    }

    internal bool HasDebugger
    {
        get
        {
            lock (gate)
            {
                return debuggerSession is not null;
            }
        }
    }

    internal void AttachDebugger(IDebuggerSession debugger)
    {
        ArgumentNullException.ThrowIfNull(debugger);
        lock (gate)
        {
            debuggerSession = debugger;
        }
    }

    internal void DetachDebugger()
    {
        lock (gate)
        {
            debuggerSession = null;
        }
    }

    internal void AddConstraint(IExecutionConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        lock (gate)
        {
            var updated = new IExecutionConstraint[constraints.Length + 1];
            Array.Copy(constraints, updated, constraints.Length);
            updated[^1] = constraint;
            constraints = updated;
        }
    }

    internal void SetCheckInterval(ulong interval)
    {
        if (interval == 0)
            throw new ArgumentOutOfRangeException(nameof(interval));

        lock (gate)
        {
            checkInterval = interval;
        }
    }

    internal void SetMaxInstructions(ulong max)
    {
        lock (gate)
        {
            maxInstructions = max;
        }
    }

    internal void ResetExecutedInstructions()
    {
        lock (gate)
        {
            executedInstructions = 0;
        }
    }

    internal void SetExecutionTimeout(TimeProvider timeProvider, TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        lock (gate)
        {
            executionTimeout = timeout;
            executionDeadline = timeProvider.GetUtcNow() + timeout;
        }
    }

    internal void ClearExecutionTimeout()
    {
        lock (gate)
        {
            executionTimeout = null;
            executionDeadline = null;
        }
    }

    internal bool ResetExecutionTimeout(TimeProvider timeProvider)
    {
        lock (gate)
        {
            if (executionTimeout is not TimeSpan timeout)
                return false;

            executionDeadline = timeProvider.GetUtcNow() + timeout;
            return true;
        }
    }

    internal void SetExecutionCancellationToken(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            executionCancellationToken = cancellationToken;
        }
    }

    internal void ClearExecutionCancellationToken()
    {
        lock (gate)
        {
            executionCancellationToken = default;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void EmitBoundaryCheckpoint(
        JsRealm realm,
        Span<JsValue> fullStack,
        int fp,
        ExecutionCheckpointKind kind,
        int programCounter)
    {
        EmitBoundaryCheckpointCore(realm, fullStack, fp, kind, programCounter, null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void EmitBoundaryCheckpoint(
        JsRealm realm,
        Span<JsValue> fullStack,
        int fp,
        ExecutionCheckpointKind kind,
        ref byte bytecode,
        ref byte pc)
    {
        EmitBoundaryCheckpointCore(realm, fullStack, fp, kind, JsRealm.GetPcOffset(ref bytecode, ref pc),
            null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void EmitDebuggerStatementCheckpoint(
        JsRealm realm,
        Span<JsValue> fullStack,
        int fp,
        ref byte bytecode,
        ref byte pc)
    {
        EmitBoundaryCheckpointCore(realm, fullStack, fp, ExecutionCheckpointKind.DebuggerStatement,
            JsRealm.GetPcOffset(ref bytecode, ref pc), null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void EmitBreakpointCheckpoint(
        JsRealm realm,
        Span<JsValue> fullStack,
        int fp,
        ref byte bytecode,
        ref byte pc)
    {
        EmitBoundaryCheckpointCore(realm, fullStack, fp, ExecutionCheckpointKind.Breakpoint,
            JsRealm.GetPcOffset(ref bytecode, ref pc), null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void EmitBoundaryCheckpoint(
        JsRealm realm,
        Span<JsValue> fullStack,
        int fp,
        ExecutionCheckpointKind kind)
    {
        EmitBoundaryCheckpointCore(realm, fullStack, fp, kind, 0, null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EmitBoundaryCheckpointCore(
        JsRealm realm,
        Span<JsValue> fullStack,
        int fp,
        ExecutionCheckpointKind kind,
        int programCounter,
        JsOpCode? currentOpcode)
    {
        DateTimeOffset? executionDeadline;
        CancellationToken executionCancellationToken;
        IExecutionConstraint[] constraints;
        IDebuggerSession? debuggerSession;
        ulong currentExecutedInstructions;
        lock (gate)
        {
            executionDeadline = this.executionDeadline;
            executionCancellationToken = this.executionCancellationToken;
            constraints = this.constraints;
            debuggerSession = this.debuggerSession;
            currentExecutedInstructions = executedInstructions;
        }

        ThrowIfCanceled(executionCancellationToken);
        if (executionDeadline is not null && realm.TimeProvider.GetUtcNow() >= executionDeadline.Value)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Execution timeout exceeded",
                "EXECUTION_TIMEOUT_EXCEEDED");

        if (debuggerSession is null && constraints.Length == 0)
            return;

        var currentFrame = realm.GetCurrentFrameInfo(fullStack, fp, programCounter);
        var sourcePath = currentFrame.SourcePath;
        var script = realm.GetCallFrameAt(fp).Function is JsBytecodeFunction bytecodeFn
            ? bytecodeFn.Script
            : null;
        var stackFrames = debuggerSession is null
            ? null
            : realm.CaptureStackTraceSnapshot(fullStack, fp, programCounter);
        IReadOnlyList<JsLocalDebugInfo>? locals = null;
        IReadOnlyList<PausedLocalValue>? localValues = null;
        IReadOnlyList<PausedScopeSnapshot>? scopeChain = null;
        if (debuggerSession is not null && realm.GetCallFrameAt(fp).Function is JsBytecodeFunction bytecodeFunction)
        {
            locals = JsScriptDebugInfo.GetVisibleLocalInfos(bytecodeFunction.Script, programCounter);
            localValues = realm.CapturePausedLocalValues(fullStack, fp, programCounter);
            scopeChain = realm.CapturePausedScopeChain(fullStack, fp, programCounter);
            if (scopeChain is not null && scopeChain.Count != 0)
                localValues = scopeChain[0].LocalValues;
        }

        var checkpoint = new ExecutionCheckpoint(
            kind,
            currentExecutedInstructions,
            programCounter,
            realm.GetExecutionContextDepth(),
            currentOpcode,
            currentFrame,
            sourcePath,
            script,
            stackFrames,
            locals,
            localValues,
            scopeChain);
        if (realm.Agent.TryConsumeStepRequest(in checkpoint, out var stepKind))
            checkpoint = checkpoint.WithKind(stepKind);
        debuggerSession?.OnCheckpoint(in checkpoint);
        for (var i = 0; i < constraints.Length; i++)
            constraints[i].OnCheckpoint(in checkpoint);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void CheckSlowPath(
        JsRealm realm,
        Span<JsValue> fullStack,
        int fp,
        ref byte bytecode,
        ref byte pc,
        JsOpCode currentOpcode,
        ref ulong nextCheck)
    {
        ulong interval;
        ulong maxInstructions;
        ulong nextExecuted;
        DateTimeOffset? executionDeadline;
        CancellationToken executionCancellationToken;
        IExecutionConstraint[] constraints;
        IDebuggerSession? debuggerSession;
        lock (gate)
        {
            interval = checkInterval;
            maxInstructions = this.maxInstructions;
            nextExecuted = executedInstructions + interval;
            executedInstructions = nextExecuted;
            executionDeadline = this.executionDeadline;
            executionCancellationToken = this.executionCancellationToken;
            constraints = this.constraints;
            debuggerSession = this.debuggerSession;
        }

        ThrowIfCanceled(executionCancellationToken);
        if (executionDeadline is not null && realm.TimeProvider.GetUtcNow() >= executionDeadline.Value)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Execution timeout exceeded",
                "EXECUTION_TIMEOUT_EXCEEDED");

        if (maxInstructions != ulong.MaxValue && nextExecuted > maxInstructions)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Execution limit exceeded",
                "EXECUTION_LIMIT_EXCEEDED");

        var programCounter = JsRealm.GetPcOffset(ref bytecode, ref pc);
        var currentFrame = realm.GetCurrentFrameInfo(fullStack, fp, programCounter);
        var sourcePath = currentFrame.SourcePath;
        var script = realm.GetCallFrameAt(fp).Function is JsBytecodeFunction bytecodeFn
            ? bytecodeFn.Script
            : null;
        var stackFrames = debuggerSession is null
            ? null
            : realm.CaptureStackTraceSnapshot(fullStack, fp, programCounter);
        IReadOnlyList<JsLocalDebugInfo>? locals = null;
        IReadOnlyList<PausedLocalValue>? localValues = null;
        IReadOnlyList<PausedScopeSnapshot>? scopeChain = null;
        if (debuggerSession is not null && realm.GetCallFrameAt(fp).Function is JsBytecodeFunction bytecodeFunction)
        {
            locals = JsScriptDebugInfo.GetVisibleLocalInfos(bytecodeFunction.Script, programCounter);
            localValues = realm.CapturePausedLocalValues(fullStack, fp, programCounter);
            scopeChain = realm.CapturePausedScopeChain(fullStack, fp, programCounter);
            if (scopeChain is not null && scopeChain.Count != 0)
                localValues = scopeChain[0].LocalValues;
        }

        var checkpoint = new ExecutionCheckpoint(
            ExecutionCheckpointKind.Periodic,
            nextExecuted,
            programCounter,
            realm.GetExecutionContextDepth(),
            currentOpcode,
            currentFrame,
            sourcePath,
            script,
            stackFrames,
            locals,
            localValues,
            scopeChain);
        debuggerSession?.OnCheckpoint(in checkpoint);
        for (var i = 0; i < constraints.Length; i++)
            constraints[i].OnCheckpoint(in checkpoint);

        nextCheck = interval;
    }

    private static void ThrowIfCanceled(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Execution canceled",
                "EXECUTION_CANCELED");
    }
}
