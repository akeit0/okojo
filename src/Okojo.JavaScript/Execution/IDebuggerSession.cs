namespace Okojo.JavaScript.Execution;

/// <summary>
///     Receives periodic execution checkpoints while attached to an agent.
/// </summary>
public interface IDebuggerSession
{
    void OnCheckpoint(in ExecutionCheckpoint checkpoint);
}
