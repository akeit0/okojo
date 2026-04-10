namespace Okojo.Runtime;

/// <summary>
///     Low-level host integration surface for the Okojo engine container.
///     This is the direct host seam for advanced embedders. Higher-level default
///     runtime implementations should live in other assemblies such as
///     <c>Okojo.Hosting</c>, <c>Okojo.WebPlatform</c>, and <c>Okojo.Browser</c>.
/// </summary>
public sealed class JsRuntimeLowLevelHostOptions
{
    public IBackgroundScheduler BackgroundScheduler { get; private set; } = JsDefaultBackgroundScheduler.Shared;
    public IHostTaskScheduler HostTaskScheduler { get; private set; } = DefaultHostTaskScheduler.Shared;
    public IHostMessageSerializer MessageSerializer { get; private set; } = JsDefaultHostMessageSerializer.Shared;
    public IWorkerHost WorkerHost { get; private set; } = DefaultWorkerHost.Shared;
    public HostTaskQueueKey WorkerMessageQueueKey { get; private set; } = InternalHostTaskQueueDefaults.Default;

    public JsRuntimeLowLevelHostOptions UseBackgroundScheduler(IBackgroundScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        BackgroundScheduler = scheduler;
        return this;
    }

    public JsRuntimeLowLevelHostOptions UseTaskScheduler(IHostTaskScheduler hostTaskScheduler)
    {
        ArgumentNullException.ThrowIfNull(hostTaskScheduler);
        HostTaskScheduler = hostTaskScheduler;
        return this;
    }

    public JsRuntimeLowLevelHostOptions UseMessageSerializer(IHostMessageSerializer messageSerializer)
    {
        ArgumentNullException.ThrowIfNull(messageSerializer);
        MessageSerializer = messageSerializer;
        return this;
    }

    public JsRuntimeLowLevelHostOptions UseWorkerHost(IWorkerHost workerHost)
    {
        ArgumentNullException.ThrowIfNull(workerHost);
        WorkerHost = workerHost;
        return this;
    }

    public JsRuntimeLowLevelHostOptions UseWorkerMessageQueue(HostTaskQueueKey queueKey)
    {
        WorkerMessageQueueKey = queueKey;
        return this;
    }

    internal JsRuntimeLowLevelHostOptions Clone()
    {
        return new()
        {
            BackgroundScheduler = BackgroundScheduler,
            HostTaskScheduler = HostTaskScheduler,
            MessageSerializer = MessageSerializer,
            WorkerHost = WorkerHost,
            WorkerMessageQueueKey = WorkerMessageQueueKey
        };
    }
}
