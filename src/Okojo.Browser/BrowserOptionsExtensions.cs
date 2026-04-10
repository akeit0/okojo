using Okojo.Runtime;
using Okojo.WebPlatform;

namespace Okojo.Browser;

public static class BrowserOptionsExtensions
{
    extension(JsRuntimeBuilder builder)
    {
        public JsRuntimeBuilder UseBrowser(Action<BrowserBuilder>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.ConfigureOptions(options => options.UseBrowser(configure));
        }

        public JsRuntimeBuilder UseBrowserGlobals(Action<FetchOptions>? configureFetch = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.UseBrowser(browser => browser.UseBrowserGlobals(configureFetch));
        }

        public JsRuntimeBuilder UseBrowserHost(Action<BrowserHostOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.UseBrowser(browser => browser.UseBrowserHost(configure));
        }

        public JsRuntimeBuilder UseAnimationFrameQueue(HostTaskQueueKey queueKey)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.UseBrowser(browser => browser.UseAnimationFrameQueue(queueKey));
        }

        public JsRuntimeBuilder UseAnimationFrameInterval(TimeSpan interval)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.UseBrowser(browser => browser.UseAnimationFrameInterval(interval));
        }
    }

    extension(JsRuntimeOptions options)
    {
        public JsRuntimeOptions UseBrowser(Action<BrowserBuilder>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            var builder = new BrowserBuilder(options);
            configure?.Invoke(builder);
            return options;
        }
    }
}
