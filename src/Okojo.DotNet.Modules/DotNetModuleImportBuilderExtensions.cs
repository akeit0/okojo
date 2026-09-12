using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.Reflection;

namespace Okojo.DotNet.Modules;

public static class DotNetModuleImportBuilderExtensions
{
    public static JsRuntimeBuilder UseDotNetModuleImports(
        this JsRuntimeBuilder builder,
        Action<DotNetModuleImportOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new DotNetModuleImportOptions();
        configure?.Invoke(options);

        var bridge = new DotNetModuleImportBridge(options);
        builder.AllowClrAccess();
        builder.AddRealmApiModule(new DotNetModuleApiModule(bridge));
        builder.DecorateModuleSourceLoader(inner => new DotNetModuleSourceLoader(inner, bridge));
        return builder;
    }

    public static JsRuntimeBuilder UseDotNetModuleImports(
        this JsRuntimeBuilder builder,
        IModuleSourceLoader fallbackLoader,
        Action<DotNetModuleImportOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(fallbackLoader);

        builder.UseModuleSourceLoader(fallbackLoader);
        builder.UseDotNetModuleImports(configure);
        return builder;
    }
}
