namespace Okojo.Hosting;

/// <summary>
///     Observes host turn progression.
///     This is a low-level hosting hook for diagnostics, instrumentation, and
///     custom event-loop coordination. It does not own queue selection or execution.
/// </summary>
public interface IHostTurnObserver
{
    void OnTurnEvent(HostTurnNotification notification);
}
