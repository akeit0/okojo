using System.Reflection;
using Okojo.Runtime.Interop;

namespace Okojo.Reflection.Internal;

internal static class HostBindingResolver
{
    private const string CreateBindingMethodName = "CreateHostBinding";

    internal static HostBinding? TryGetHostBinding(Type clrType)
    {
        var method = clrType.GetMethod(CreateBindingMethodName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy,
            null, Type.EmptyTypes, null);
        if (method is null || !typeof(HostBinding).IsAssignableFrom(method.ReturnType))
            return null;

        return (HostBinding?)method.Invoke(null, null);
    }
}
