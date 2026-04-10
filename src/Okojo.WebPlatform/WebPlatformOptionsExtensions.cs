using Okojo.Hosting;
using Okojo.Runtime;

namespace Okojo.WebPlatform;

public static class WebPlatformOptionsExtensions
{
    extension(JsRuntimeBuilder builder)
    {
        public JsRuntimeBuilder UseServerRuntime(Action<ServerRuntimeOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.UseWebPlatform(web => web.UseServerRuntime(configure));
        }

        public JsRuntimeBuilder UseServerHost(Action<ServerHostOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.UseWebPlatform(web => web.UseServerHost(configure));
        }

        public JsRuntimeBuilder UseWebRuntimeGlobals()
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.UseWebPlatform(static web => web.UseWebRuntimeGlobals());
        }

        public JsRuntimeBuilder UseWebDelayScheduler(IHostDelayScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.UseWebPlatform(web => web.UseWebDelayScheduler(scheduler));
        }

        public JsRuntimeBuilder UseWebTimerQueue(HostTaskQueueKey queueKey)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.UseWebPlatform(web => web.UseWebTimerQueue(queueKey));
        }

        public JsRuntimeBuilder UseFetchCompletionQueue(HostTaskQueueKey queueKey)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.UseWebPlatform(web => web.UseFetchCompletionQueue(queueKey));
        }

        public JsRuntimeBuilder UseWebPlatform(Action<WebPlatformBuilder>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.ConfigureOptions(options => options.UseWebPlatform(configure));
        }

        public JsRuntimeBuilder UseRealmSetup(Action<JsRealm> setup)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.ConfigureOptions(options => options.UseRealmSetup(setup));
        }

        public JsRuntimeBuilder UseGlobal(string name, Func<JsRealm, JsValue> valueFactory)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.UseWebPlatform(web => web.UseGlobal(name, valueFactory));
        }

        public JsRuntimeBuilder UseFetch(Action<FetchOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.UseWebPlatform(web => web.UseFetch(configure));
        }

        public JsRuntimeBuilder UseWebWorkers(Action<WebWorkerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.UseWebPlatform(web => web.UseWebWorkers(configure));
        }
    }

    extension(JsRuntimeOptions options)
    {
        public JsRuntimeOptions UseServerRuntime(Action<ServerRuntimeOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            return options.UseWebPlatform(builder => builder.UseServerRuntime(configure));
        }

        public JsRuntimeOptions UseServerHost(Action<ServerHostOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            return options.UseWebPlatform(builder => builder.UseServerHost(configure));
        }

        public JsRuntimeOptions UseWebRuntimeGlobals()
        {
            ArgumentNullException.ThrowIfNull(options);
            return options.UseWebPlatform(static builder => builder.UseWebRuntimeGlobals());
        }

        public JsRuntimeOptions UseWebDelayScheduler(IHostDelayScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(options);
            return options.UseWebPlatform(web => web.UseWebDelayScheduler(scheduler));
        }

        public JsRuntimeOptions UseWebTimerQueue(HostTaskQueueKey queueKey)
        {
            ArgumentNullException.ThrowIfNull(options);
            return options.UseWebPlatform(web => web.UseWebTimerQueue(queueKey));
        }

        public JsRuntimeOptions UseFetchCompletionQueue(HostTaskQueueKey queueKey)
        {
            ArgumentNullException.ThrowIfNull(options);
            return options.UseWebPlatform(web => web.UseFetchCompletionQueue(queueKey));
        }

        public JsRuntimeOptions UseWebPlatform(Action<WebPlatformBuilder>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(options);

            var builder = new WebPlatformBuilder(options);
            configure?.Invoke(builder);
            return options;
        }

        public JsRuntimeOptions UseRealmSetup(Action<JsRealm> setup)
        {
            ArgumentNullException.ThrowIfNull(options);
            options.Core.UseRealmSetup(setup);
            return options;
        }

        public JsRuntimeOptions UseGlobal(string name, Func<JsRealm, JsValue> valueFactory)
        {
            ArgumentNullException.ThrowIfNull(options);
            return options.UseWebPlatform(web => web.UseGlobal(name, valueFactory));
        }

        public JsRuntimeOptions UseFetch(Action<FetchOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            return options.UseWebPlatform(builder => builder.UseFetch(configure));
        }

        public JsRuntimeOptions UseWebWorkers(Action<WebWorkerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            return options.UseWebPlatform(builder => builder.UseWebWorkers(configure));
        }
    }
}
