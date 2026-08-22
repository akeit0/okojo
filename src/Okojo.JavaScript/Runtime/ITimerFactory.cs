namespace Okojo.Runtime;

internal interface ITimerFactory
{
    ITimer CreateJsTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period);
    ITimer CreateWaitTimer(TimerCallback callback, object? state, TimeSpan dueTime);
}
