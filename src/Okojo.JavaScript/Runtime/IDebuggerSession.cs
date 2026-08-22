namespace Okojo.Runtime;

/// <summary>
///     Receives periodic execution checkpoints while attached to an agent.
/// </summary>
public interface IDebuggerSession
{
    void OnCheckpoint(in ExecutionCheckpoint checkpoint);
}
