using Okojo.Runtime;

namespace Okojo.Hosting;

/// <summary>
///     Delayed scheduling targeted at a specific host task queue.
///     This matches ECMA-262 HostEnqueueTimeoutJob and the HTML task-queue model
///     more closely than one implicit ready queue. See ECMA-262 9.5.6 and WHATWG HTML
///     8.1.7.1:
///     https://tc39.es/ecma262/multipage/executable-code-and-execution-contexts.html#sec-hostenqueuetimeoutjob
///     https://html.spec.whatwg.org/multipage/webappapis.html#event-loops
/// </summary>
public interface IQueuedHostDelayScheduler : IHostDelayScheduler
{
    IHostDelayedOperation ScheduleDelayed(
        TimeSpan delay,
        HostTaskQueueKey targetQueue,
        Action<object?> callback,
        object? state);
}
