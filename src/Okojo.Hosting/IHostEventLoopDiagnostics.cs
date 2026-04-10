namespace Okojo.Hosting;

/// <summary>
///     Exposes host queue and delayed-work state for diagnostics.
///     This is a hosting/debugging surface, not an ECMAScript core surface.
/// </summary>
public interface IHostEventLoopDiagnostics
{
    HostEventLoopSnapshot GetSnapshot();
}
