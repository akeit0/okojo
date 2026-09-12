namespace Okojo.JavaScript.Execution;

public sealed partial class JsAgent
{
    private int activeVmRuns;
    private int pendingCodeReloads;
    private ulong executionCodeGeneration;
    private ulong deferredExecutionCheckCountdown;
    private bool codeReloadScheduled;

    internal ulong EnterVmExecution()
    {
        activeVmRuns++;
        return executionCodeGeneration;
    }

    internal void ExitVmExecution(ulong observedGeneration)
    {
        if (observedGeneration != executionCodeGeneration)
            pendingCodeReloads--;
        activeVmRuns--;
        if (codeReloadScheduled && pendingCodeReloads == 0)
        {
            ExecutionCheckCountdown = deferredExecutionCheckCountdown;
            codeReloadScheduled = false;
        }
    }

    // Debugger mutations, like execution itself, are agent-confined. Publication of
    // the new array is atomic, but it does not permit unsynchronized debugger writes.
    internal void RequestExecutionCodeReload()
    {
        if (activeVmRuns == 0)
            return;
        if (!codeReloadScheduled)
        {
            deferredExecutionCheckCountdown = ExecutionCheckCountdown;
            codeReloadScheduled = true;
        }
        executionCodeGeneration++;
        pendingCodeReloads = activeVmRuns;
        ExecutionCheckCountdown = 1;
    }

    // A forced cursor refresh is NOT an execution-policy checkpoint. Keep the
    // original countdown, consuming exactly one instruction per dispatch. Nested
    // Run invocations must each acknowledge the generation before removing the
    // forced slow path; otherwise a suspended host caller could retain old code.
    internal bool BeginExecutionCodeCheck(ref ulong observedGeneration)
    {
        if (!codeReloadScheduled)
            return true;
        if (observedGeneration != executionCodeGeneration)
        {
            observedGeneration = executionCodeGeneration;
            pendingCodeReloads--;
        }
        ExecutionCheckCountdown = --deferredExecutionCheckCountdown;
        return ExecutionCheckCountdown == 0;
    }

    internal void EndExecutionCodeCheck()
    {
        if (!codeReloadScheduled)
            return;
        deferredExecutionCheckCountdown = ExecutionCheckCountdown;
        if (pendingCodeReloads == 0)
            codeReloadScheduled = false;
        else
            ExecutionCheckCountdown = 1;
    }
}
