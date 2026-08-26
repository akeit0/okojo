namespace Okojo.JavaScript.Embedding;

/// <summary>
///     Low-level host integration surface for the Okojo engine container.
///     This is the direct host seam for advanced embedders. Higher-level default
///     runtime implementations should live in other assemblies such as
///     <c>Okojo.Hosting</c>, <c>Okojo.WebPlatform</c>, and <c>Okojo.Browser</c>.
/// </summary>
public sealed class JsRuntimeLowLevelHostOptions
{
    public IHostTaskScheduler? HostTaskScheduler { get; private set; }
    public IHostMessageSerializer? MessageSerializer { get; private set; }
    public IWorkerHost? WorkerHost { get; private set; }
    public HostTaskQueueKey WorkerMessageQueueKey { get; private set; } =
        InternalHostTaskQueueDefaults.Default;

    public JsRuntimeLowLevelHostOptions UseTaskScheduler(IHostTaskScheduler hostTaskScheduler)
    {
        ArgumentNullException.ThrowIfNull(hostTaskScheduler);
        HostTaskScheduler = hostTaskScheduler;
        return this;
    }

    public JsRuntimeLowLevelHostOptions UseMessageSerializer(
        IHostMessageSerializer messageSerializer
    )
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
            HostTaskScheduler = HostTaskScheduler,
            MessageSerializer = MessageSerializer,
            WorkerHost = WorkerHost,
            WorkerMessageQueueKey = WorkerMessageQueueKey,
        };
    }
}
