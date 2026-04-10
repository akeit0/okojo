using Okojo.Objects;
using Okojo.Runtime;

internal sealed class Test262RunnerSharedWaiterControllerFactory : ISharedWaiterControllerFactory
{
    public static Test262RunnerSharedWaiterControllerFactory Shared { get; } = new();

    public JsArrayBufferObject.ISharedWaiterController CreateController(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        return new Test262RunnerSharedWaiterController(realm);
    }

    private sealed class Test262RunnerSharedWaiterController(JsRealm realm)
        : JsArrayBufferObject.ISharedWaiterController
    {
        private readonly Test262RunnerTimeProvider? runnerTime = realm.Engine.TimeProvider as Test262RunnerTimeProvider;
        private ITimer? asyncTimeoutTimer;

        public void ArmAsyncTimeout(JsArrayBufferObject.SharedWaiter waiter, TimeSpan? timeout)
        {
            Interlocked.Exchange(ref asyncTimeoutTimer, null)?.Dispose();
            if (timeout is null)
                return;

            var dueTime = timeout.Value <= TimeSpan.Zero
                ? TimeSpan.Zero
                : timeout.Value.TotalMilliseconds >= int.MaxValue
                    ? TimeSpan.FromMilliseconds(int.MaxValue)
                    : timeout.Value;
            asyncTimeoutTimer = realm.Engine.TimeProvider.CreateTimer(static state =>
            {
                var waiter = (JsArrayBufferObject.SharedWaiter)state!;
                if (waiter.TryTimeout())
                    waiter.Complete();
            }, waiter, dueTime, Timeout.InfiniteTimeSpan);
        }

        public bool Wait(JsArrayBufferObject.SharedWaiter waiter, TimeSpan? timeout)
        {
            if (timeout == null || timeout == TimeSpan.MaxValue)
            {
                waiter.Event.Wait();
                return waiter.Notified;
            }

            if (runnerTime is null)
            {
                waiter.Event.Wait(timeout.Value);
                return waiter.Notified;
            }

            var dueTime = timeout.Value <= TimeSpan.Zero
                ? TimeSpan.Zero
                : timeout.Value.TotalMilliseconds >= int.MaxValue
                    ? TimeSpan.FromMilliseconds(int.MaxValue)
                    : timeout.Value;
            using var waitTimer = runnerTime.CreateWaitTimer(static state =>
            {
                var waiter = (JsArrayBufferObject.SharedWaiter)state!;
                if (waiter.TryTimeout())
                    waiter.Complete();
            }, waiter, dueTime);

            while (!waiter.Event.Wait(0))
            {
                if (!runnerTime.AdvanceForAsyncPump())
                    runnerTime.Advance(TimeSpan.FromMilliseconds(1));
                if (realm.Engine.Agents.Count > 1)
                    Thread.Yield();
            }

            return waiter.Notified;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref asyncTimeoutTimer, null)?.Dispose();
        }
    }
}
