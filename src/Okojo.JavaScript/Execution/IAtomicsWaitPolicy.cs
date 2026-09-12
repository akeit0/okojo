namespace Okojo.JavaScript.Execution;

/// <summary>Defines host policy for blocking Atomics waits and asynchronous wait timeouts.</summary>
public interface IAtomicsWaitPolicy
{
    /// <summary>Returns whether the current agent may suspend for <c>Atomics.wait</c>.</summary>
    bool CanSuspend(JsRealm realm);

    /// <summary>
    ///     Waits until the signal is set or the optional timeout expires. Returns
    ///     <see langword="true" /> when signaled and <see langword="false" /> on timeout.
    /// </summary>
    bool Wait(JsRealm realm, AtomicsWaitSignal signal, TimeSpan? timeout);

    /// <summary>
    ///     Schedules a finite <c>Atomics.waitAsync</c> timeout. The returned registration is
    ///     disposed when the wait completes and may be <see langword="null" />.
    /// </summary>
    IDisposable? ScheduleTimeout(JsRealm realm, TimeSpan timeout, Action timeoutCallback);
}

/// <summary>A read-only notification signal supplied to an <see cref="IAtomicsWaitPolicy" />.</summary>
public sealed class AtomicsWaitSignal
{
    private readonly ManualResetEventSlim signal = new(false);

    internal AtomicsWaitSignal() { }

    /// <summary>Gets whether the engine has signaled the wait.</summary>
    public bool IsSet => signal.IsSet;

    /// <summary>Waits until the engine signals the wait.</summary>
    public void Wait()
    {
        signal.Wait();
    }

    /// <summary>Waits until the engine signals the wait or the timeout expires.</summary>
    public bool Wait(TimeSpan timeout)
    {
        return signal.Wait(timeout);
    }

    internal void Set()
    {
        signal.Set();
    }

    internal void Dispose()
    {
        signal.Dispose();
    }
}
