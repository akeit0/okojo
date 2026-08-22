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
        return this;
    }

    public HostingBuilder UseWorkerGlobals()
    {
        options.UseWorkerMessaging(workerMessaging =>
        {
            var module = new WorkerGlobalsApiModule();
            module.AttachWorkerMessaging(workerMessaging);
            return module;
        });
        return this;
    }

    public HostingBuilder UseThreadPoolDefaults()
    {
        options.UseHostTaskScheduler(new ThreadPoolTaskScheduler());
        return this;
    }
}
