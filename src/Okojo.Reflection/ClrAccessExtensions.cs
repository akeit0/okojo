using System.Reflection;
using Okojo.Reflection.Internal;
using Okojo.Runtime;

namespace Okojo.Reflection;

/// <summary>
///     Reflection-based CLR interop opt-in surface for Okojo runtimes.
///     Reference <c>Okojo.Reflection</c> when enabling CLR namespace/type access.
/// </summary>
public static class ClrAccessExtensions
{
    private static readonly ClrInteropProvider SProvider = new();

    public static JsRuntimeBuilder AllowClrAccess(this JsRuntimeBuilder builder, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.EnableClrAccess(SProvider);
        if (assemblies.Length != 0)
            builder.AddClrAssembly(assemblies);
        return builder;
    }

    public static JsRuntimeOptions AllowClrAccess(this JsRuntimeOptions options, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnableClrAccess(SProvider);
        if (assemblies.Length != 0)
            options.AddClrAssembly(assemblies);
        return options;
    }

    public static JsRuntimeCoreOptions AllowClrAccess(this JsRuntimeCoreOptions options, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnableClrAccess(SProvider);
        if (assemblies.Length != 0)
            options.AddClrAssembly(assemblies);
        return options;
    }
}
