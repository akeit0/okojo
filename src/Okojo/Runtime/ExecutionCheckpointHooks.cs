namespace Okojo.Runtime;

[Flags]
public enum ExecutionCheckpointHooks
{
    None = 0,
    DebuggerStatement = 1 << 0,
    Call = 1 << 1,
    Return = 1 << 2,
    Pump = 1 << 3,
    SuspendGenerator = 1 << 4,
    ResumeGenerator = 1 << 5,
    Breakpoint = 1 << 6,
    CaughtException = 1 << 7
}
