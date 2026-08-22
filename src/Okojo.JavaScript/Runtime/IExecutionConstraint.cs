namespace Okojo.Runtime;

/// <summary>
///     Receives periodic execution checkpoints and may enforce host-specific limits.
/// </summary>
public interface IExecutionConstraint
{
    void OnCheckpoint(in ExecutionCheckpoint checkpoint);
}
