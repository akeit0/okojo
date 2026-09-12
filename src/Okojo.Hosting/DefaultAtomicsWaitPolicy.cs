using Okojo.JavaScript;
using Okojo.JavaScript.Execution;

namespace Okojo.Hosting;

internal sealed class DefaultAtomicsWaitPolicy : IAtomicsWaitPolicy
{
    public static DefaultAtomicsWaitPolicy Shared { get; } = new();

    public bool CanSuspend(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        return true;
    }

    public bool Wait(JsRealm realm, AtomicsWaitSignal signal, TimeSpan? timeout)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(signal);
        if (timeout is null)
        {
            signal.Wait();
            return true;
        }

        return signal.Wait(timeout.Value);
    }

    public IDisposable? ScheduleTimeout(JsRealm realm, TimeSpan timeout, Action timeoutCallback)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(timeoutCallback);
        var dueTime =
            timeout <= TimeSpan.Zero ? TimeSpan.Zero
            : timeout.TotalMilliseconds >= int.MaxValue ? TimeSpan.FromMilliseconds(int.MaxValue)
            : timeout;
        return realm.TimeProvider.CreateTimer(
            static state => ((Action)state!).Invoke(),
            timeoutCallback,
            dueTime,
            Timeout.InfiniteTimeSpan
        );
    }
}
