using Okojo.Reflection;
using Okojo.Runtime;

namespace Okojo.DotNet.Modules;

public sealed class DotNetModuleImportSupport
{
    private readonly DotNetModuleImportBridge bridge;

    public DotNetModuleImportSupport(Action<DotNetModuleImportOptions>? configure = null)
    {
        var options = new DotNetModuleImportOptions();
        configure?.Invoke(options);
        bridge = new DotNetModuleImportBridge(options);
    }

    public void ConfigureRuntime(JsRuntimeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AllowClrAccess();
        builder.AddRealmApiModule(new DotNetModuleApiModule(bridge));
    }

    public IModuleSourceLoader WrapModuleSourceLoader(IModuleSourceLoader inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return new DotNetModuleSourceLoader(inner, bridge);
    }
}
