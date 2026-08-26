using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.Hosting;

public sealed class HostingBuilder
{
    private readonly JsRuntimeOptions options;

    internal HostingBuilder(JsRuntimeOptions options)
    {
        this.options = options;
    }

    public HostingBuilder UseMessageSerializer(IHostingMessageSerializer messageSerializer)
    {
        ArgumentNullException.ThrowIfNull(messageSerializer);
        options.UseMessageSerializer(new HostingMessageSerializerAdapter(messageSerializer));
        return this;
    }

    public HostingBuilder UseJsWorkerHost(IHostingJsWorkerHost workerHost)
    {
        ArgumentNullException.ThrowIfNull(workerHost);
        options.UseWorkerHost(new HostingJsWorkerHostAdapter(workerHost));
        EnsureWorkerInfrastructure();
        return this;
    }

    public HostingBuilder UseWorkerGlobals()
    {
        if (options.LowLevelHost.WorkerHost is null)
            options.UseWorkerHost(DefaultWorkerHost.Shared);
        EnsureWorkerInfrastructure(useDefaultAtomicsWaitPolicy: true);
        options.UseWorkerMessaging(workerMessaging => new WorkerGlobalsApiModule(workerMessaging));
        return this;
    }

    public HostingBuilder UseThreadPoolDefaults()
    {
        options.UseHostTaskScheduler(new ThreadPoolTaskScheduler());
        if (options.Host.AtomicsWaitPolicy is null)
            options.UseAtomicsWaitPolicy(DefaultAtomicsWaitPolicy.Shared);
        return this;
    }

    private void EnsureWorkerInfrastructure(bool useDefaultAtomicsWaitPolicy = false)
    {
        if (options.LowLevelHost.HostTaskScheduler is null)
            options.UseHostTaskScheduler(DefaultHostTaskScheduler.Shared);
        if (options.LowLevelHost.MessageSerializer is null)
            options.UseMessageSerializer(JsDefaultHostMessageSerializer.Shared);
        if (useDefaultAtomicsWaitPolicy && options.Host.AtomicsWaitPolicy is null)
            options.UseAtomicsWaitPolicy(DefaultAtomicsWaitPolicy.Shared);
    }
}
