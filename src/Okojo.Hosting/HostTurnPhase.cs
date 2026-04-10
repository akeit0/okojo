namespace Okojo.Hosting;

public enum HostTurnPhase
{
    BeforeTurn = 0,
    AfterHostTask = 1,
    BeforeMicrotaskCheckpoint = 2,
    AfterMicrotaskCheckpoint = 3,
    AfterTurn = 4
}
