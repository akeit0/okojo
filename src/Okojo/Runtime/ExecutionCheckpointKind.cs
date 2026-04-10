namespace Okojo.Runtime;

public enum ExecutionCheckpointKind
{
    Periodic = 0,
    Call = 1,
    Return = 2,
    Pump = 3,
    DebuggerStatement = 4,
    SuspendGenerator = 5,
    ResumeGenerator = 6,
    Breakpoint = 7,
    Step = 8,
    CaughtException = 9
}
