namespace Okojo.Runtime;

/// <summary>
///     Agent-level configuration.
///     The main purpose of this layer is to own the agent's default realm settings and per-agent host data.
/// </summary>
public sealed class JsAgentOptions
{
    private readonly List<IExecutionConstraint> constraints = new();

    public object? HostDefined { get; set; }
    public Action<JsAgent>? Initialize { get; set; }
    public ulong CheckInterval { get; private set; } = ulong.MaxValue;
    public ulong MaxInstructions { get; private set; } = ulong.MaxValue;
    public TimeSpan? ExecutionTimeout { get; private set; }
    public CancellationToken ExecutionCancellationToken { get; private set; }

    public ExecutionCheckpointHooks ExecutionCheckpointHooks { get; private set; } =
        ExecutionCheckpointHooks.DebuggerStatement | ExecutionCheckpointHooks.Breakpoint;

    public IDebuggerSession? DebuggerSession { get; set; }
    public IReadOnlyList<IExecutionConstraint> Constraints => constraints;
    public JsRealmOptions Realm { get; } = new();

    public JsAgentOptions SetCheckInterval(ulong checkInterval)
    {
        if (checkInterval == 0)
            throw new ArgumentOutOfRangeException(nameof(checkInterval));

        CheckInterval = checkInterval;
        return this;
    }

    public JsAgentOptions SetMaxInstructions(ulong maxInstructions)
    {
        MaxInstructions = maxInstructions;
        return this;
    }

    public JsAgentOptions SetExecutionTimeout(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        ExecutionTimeout = timeout;
        return this;
    }

    public JsAgentOptions ClearExecutionTimeout()
    {
        ExecutionTimeout = null;
        return this;
    }

    public JsAgentOptions SetExecutionCancellationToken(CancellationToken cancellationToken)
    {
        ExecutionCancellationToken = cancellationToken;
        return this;
    }

    public JsAgentOptions ClearExecutionCancellationToken()
    {
        ExecutionCancellationToken = default;
        return this;
    }

    public JsAgentOptions EnableCallHook()
    {
        ExecutionCheckpointHooks |= ExecutionCheckpointHooks.Call;
        return this;
    }

    public JsAgentOptions DisableCallHook()
    {
        ExecutionCheckpointHooks &= ~ExecutionCheckpointHooks.Call;
        return this;
    }

    public JsAgentOptions EnableReturnHook()
    {
        ExecutionCheckpointHooks |= ExecutionCheckpointHooks.Return;
        return this;
    }

    public JsAgentOptions DisableReturnHook()
    {
        ExecutionCheckpointHooks &= ~ExecutionCheckpointHooks.Return;
        return this;
    }

    public JsAgentOptions EnablePumpHook()
    {
        ExecutionCheckpointHooks |= ExecutionCheckpointHooks.Pump;
        return this;
    }

    public JsAgentOptions DisablePumpHook()
    {
        ExecutionCheckpointHooks &= ~ExecutionCheckpointHooks.Pump;
        return this;
    }

    public JsAgentOptions EnableSuspendGeneratorHook()
    {
        ExecutionCheckpointHooks |= ExecutionCheckpointHooks.SuspendGenerator;
        return this;
    }

    public JsAgentOptions DisableSuspendGeneratorHook()
    {
        ExecutionCheckpointHooks &= ~ExecutionCheckpointHooks.SuspendGenerator;
        return this;
    }

    public JsAgentOptions EnableResumeGeneratorHook()
    {
        ExecutionCheckpointHooks |= ExecutionCheckpointHooks.ResumeGenerator;
        return this;
    }

    public JsAgentOptions DisableResumeGeneratorHook()
    {
        ExecutionCheckpointHooks &= ~ExecutionCheckpointHooks.ResumeGenerator;
        return this;
    }

    public JsAgentOptions EnableDebuggerStatementHook()
    {
        ExecutionCheckpointHooks |= ExecutionCheckpointHooks.DebuggerStatement;
        return this;
    }

    public JsAgentOptions DisableDebuggerStatementHook()
    {
        ExecutionCheckpointHooks &= ~ExecutionCheckpointHooks.DebuggerStatement;
        return this;
    }

    public JsAgentOptions EnableBreakpointHook()
    {
        ExecutionCheckpointHooks |= ExecutionCheckpointHooks.Breakpoint;
        return this;
    }

    public JsAgentOptions DisableBreakpointHook()
    {
        ExecutionCheckpointHooks &= ~ExecutionCheckpointHooks.Breakpoint;
        return this;
    }

    public JsAgentOptions EnableCaughtExceptionHook()
    {
        ExecutionCheckpointHooks |= ExecutionCheckpointHooks.CaughtException;
        return this;
    }

    public JsAgentOptions DisableCaughtExceptionHook()
    {
        ExecutionCheckpointHooks &= ~ExecutionCheckpointHooks.CaughtException;
        return this;
    }

    public JsAgentOptions AddConstraint(IExecutionConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        constraints.Add(constraint);
        return this;
    }

    internal JsAgentOptions Clone()
    {
        var clone = new JsAgentOptions
        {
            HostDefined = HostDefined,
            Initialize = Initialize,
            CheckInterval = CheckInterval,
            MaxInstructions = MaxInstructions,
            ExecutionTimeout = ExecutionTimeout,
            ExecutionCancellationToken = ExecutionCancellationToken,
            ExecutionCheckpointHooks = ExecutionCheckpointHooks,
            DebuggerSession = DebuggerSession
        };
        clone.constraints.AddRange(constraints);
        return clone.ApplyRealm(Realm.Clone());
    }

    private JsAgentOptions ApplyRealm(JsRealmOptions realm)
    {
        Realm.HostDefined = realm.HostDefined;
        Realm.Initialize = realm.Initialize;
        return this;
    }
}
