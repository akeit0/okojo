namespace Okojo.Hosting;

public interface IHostDelayScheduler
{
    IHostDelayedOperation ScheduleDelayed(TimeSpan delay, Action<object?> callback, object? state);
}

public interface IHostDelayedOperation : IDisposable
{
    bool Cancel();
}
