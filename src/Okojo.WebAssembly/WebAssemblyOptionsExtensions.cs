using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

namespace Okojo.WebAssembly;

public static class WebAssemblyOptionsExtensions
{
    extension(JsRuntimeBuilder builder)
    {
        public JsRuntimeBuilder UseWebAssembly(Action<WebAssemblyBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.ConfigureOptions(options => options.UseWebAssembly(configure));
        }
    }

    extension(JsRuntimeOptions options)
    {
        public JsRuntimeOptions UseWebAssembly(Action<WebAssemblyBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(configure);

            var builder = new WebAssemblyBuilder(options);
            configure(builder);
            return options;
        }
    }
}
