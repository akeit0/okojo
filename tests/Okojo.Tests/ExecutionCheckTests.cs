using Okojo.Runtime;

namespace Okojo.Tests;

public partial class ExecutionCheckTests
{
    private sealed class ThrowingConstraint : IExecutionConstraint
    {
        public int CallCount { get; private set; }

        public void OnCheckpoint(in ExecutionCheckpoint checkpoint)
        {
            CallCount++;
            throw new InvalidOperationException($"checkpoint:{checkpoint.ExecutedInstructions}");
        }
    }

    private sealed class RecordingDebugger : IDebuggerSession
    {
        public int CallCount { get; private set; }
        public ExecutionCheckpoint? LastCheckpoint { get; private set; }
        public List<ExecutionCheckpoint> Checkpoints { get; } = new();

        public void OnCheckpoint(in ExecutionCheckpoint checkpoint)
        {
            CallCount++;
            LastCheckpoint = checkpoint;
            Checkpoints.Add(checkpoint);
        }
    }

    private sealed class RecordingConstraint : IExecutionConstraint
    {
        public int CallCount { get; private set; }
        public ExecutionCheckpoint? LastCheckpoint { get; private set; }

        public void OnCheckpoint(in ExecutionCheckpoint checkpoint)
        {
            CallCount++;
            LastCheckpoint = checkpoint;
        }
    }

    private sealed class ToggleHookDebugger : IDebuggerSession
    {
        public JsAgent? Agent { get; set; }
        public int DebuggerStops { get; private set; }
        public List<ExecutionCheckpoint> Checkpoints { get; } = new();

        public void OnCheckpoint(in ExecutionCheckpoint checkpoint)
        {
            Checkpoints.Add(checkpoint);
            if (checkpoint.Kind != ExecutionCheckpointKind.DebuggerStatement)
                return;

            DebuggerStops++;
            if (DebuggerStops == 1)
            {
                Agent?.EnableCallHook();
                Agent?.EnableReturnHook();
            }
            else if (DebuggerStops == 2)
            {
                Agent?.DisableCallHook();
                Agent?.DisableReturnHook();
            }
        }
    }

    private sealed class StepRequestDebugger : IDebuggerSession
    {
        public JsRealm? Realm { get; set; }
        public int DebuggerStops { get; private set; }
        public List<ExecutionCheckpoint> Checkpoints { get; } = new();

        public void OnCheckpoint(in ExecutionCheckpoint checkpoint)
        {
            Checkpoints.Add(checkpoint);
            if (checkpoint.Kind != ExecutionCheckpointKind.DebuggerStatement)
                return;

            DebuggerStops++;
            if (DebuggerStops == 1)
                Realm?.RequestStepOver();
        }
    }
}
