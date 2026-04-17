using Okojo.Reflection;
using Okojo.Runtime;

namespace Okojo.DotNet.Modules;

public static class DotNetModuleImportBuilderExtensions
{
    public static JsRuntimeBuilder UseDotNetModuleImports(
        this JsRuntimeBuilder builder,
        IModuleSourceLoader? fallbackLoader = null,
        Action<DotNetModuleImportOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var support = new DotNetModuleImportSupport(configure);
        support.ConfigureRuntime(builder);
        builder.UseModuleSourceLoader(support.WrapModuleSourceLoader(fallbackLoader ?? new FileModuleSourceLoader()));
        return builder;
    }
}
