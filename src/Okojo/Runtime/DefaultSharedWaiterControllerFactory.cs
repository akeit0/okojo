namespace Okojo.Runtime;

internal sealed class DefaultSharedWaiterControllerFactory : ISharedWaiterControllerFactory
{
    public static DefaultSharedWaiterControllerFactory Shared { get; } = new();

    public JsArrayBufferObject.ISharedWaiterController CreateController(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        return new DefaultSharedWaiterController(realm.Engine.TimeProvider);
    }

    private sealed class DefaultSharedWaiterController(TimeProvider timeProvider)
        : JsArrayBufferObject.ISharedWaiterController
    {
        private ITimer? timeoutTimer;

        public void ArmAsyncTimeout(JsArrayBufferObject.SharedWaiter waiter, TimeSpan? timeout)
        {
            Interlocked.Exchange(ref timeoutTimer, null)?.Dispose();
            if (timeout is null)
                return;

            var dueTime = timeout.Value <= TimeSpan.Zero
                ? TimeSpan.Zero
                : timeout.Value.TotalMilliseconds >= int.MaxValue
                    ? TimeSpan.FromMilliseconds(int.MaxValue)
                    : timeout.Value;
            timeoutTimer = timeProvider.CreateTimer(static state =>
            {
                var waiter = (JsArrayBufferObject.SharedWaiter)state!;
                if (waiter.TryTimeout())
                    waiter.Complete();
            }, waiter, dueTime, Timeout.InfiniteTimeSpan);
        }

        public bool Wait(JsArrayBufferObject.SharedWaiter waiter, TimeSpan? timeout)
        {
            if (timeout is null)
            {
                waiter.Event.Wait();
                return waiter.Notified;
            }

            waiter.Event.Wait(timeout.Value);
            return waiter.Notified;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref timeoutTimer, null)?.Dispose();
        }
    }
}
