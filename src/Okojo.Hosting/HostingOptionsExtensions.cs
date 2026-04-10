using Okojo.Runtime;

namespace Okojo.Hosting;

public static class HostingOptionsExtensions
{
    public static JsRuntimeBuilder UseHosting(this JsRuntimeBuilder builder, Action<HostingBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ConfigureOptions(options => options.UseHosting(configure));
    }

    public static JsRuntimeOptions UseHosting(this JsRuntimeOptions options, Action<HostingBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new HostingBuilder(options);
        configure?.Invoke(builder);
        return options;
    }

    public static JsRuntimeOptions UseThreadPoolHosting(this JsRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UseHosting(static builder => builder.UseThreadPoolDefaults());
    }

    public static JsRuntimeBuilder UseThreadPoolHosting(this JsRuntimeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseHosting(static hosting => hosting.UseThreadPoolDefaults());
    }

    public static JsRuntimeOptions UseWorkerGlobals(this JsRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UseHosting(static hosting => hosting.UseWorkerGlobals());
    }

    public static JsRuntimeBuilder UseWorkerGlobals(this JsRuntimeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseHosting(static hosting => hosting.UseWorkerGlobals());
    }
}
