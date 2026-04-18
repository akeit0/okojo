using System.Reflection;

namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    public JsObject GetClrNamespace(string? namespacePath = null)
    {
        EnsureClrAccessEnabled();
        return GetClrAccessProvider().GetClrNamespace(this, namespacePath);
    }

    public void AddClrAssembly(params Assembly[] assemblies)
    {
        EnsureClrAccessEnabled();
        Engine.Options.AddClrAssembliesCore(assemblies);
    }

    public bool TryGetClrValue(string path, out JsValue value)
    {
        ArgumentNullException.ThrowIfNull(path);
        EnsureClrAccessEnabled();
        return GetClrAccessProvider().TryResolveClrPathExactly(this, path, out value);
    }

    internal JsValue ResolveClrPath(string path)
    {
        return GetClrAccessProvider().ResolveClrPath(this, path);
    }

    internal JsHostFunction CreateClrTypedNullHelperFunction()
    {
        return GetClrAccessProvider().CreateClrTypedNullHelperFunction(this);
    }

    internal JsHostFunction CreateClrPlaceHolderHelperFunction()
    {
        return GetClrAccessProvider().CreateClrPlaceHolderHelperFunction(this);
    }

    internal JsHostFunction CreateClrCastHelperFunction()
    {
        return GetClrAccessProvider().CreateClrCastHelperFunction(this);
    }

    internal JsHostFunction CreateClrUsingHelperFunction()
    {
        return GetClrAccessProvider().CreateClrUsingHelperFunction(this);
    }

    internal static bool TryExtractClrType(in JsValue value, out Type type)
    {
        if (value.TryGetObject(out var obj))
        {
            if (obj is JsHostFunction fn && fn.UserData is IClrTypeReference typeReference)
            {
                type = typeReference.ClrType;
                return true;
            }

            if (obj is JsHostObject host && host.Data is Type hostType)
            {
                type = hostType;
                return true;
            }
        }

        type = null!;
        return false;
    }

    internal bool TryResolveClrPathExactly(string path, out JsValue value)
    {
        return GetClrAccessProvider().TryResolveClrPathExactly(this, path, out value);
    }

    internal static bool TryExtractClrNamespacePath(in JsValue value, out string? path)
    {
        if (value.TryGetObject(out var obj) && obj is IClrNamespaceReference ns)
        {
            path = ns.NamespacePath;
            return true;
        }

        path = null;
        return false;
    }

    private IClrAccessProvider GetClrAccessProvider()
    {
        return Engine.ClrAccessProvider ?? throw new InvalidOperationException(
            "CLR access is disabled. Configure JsRuntime with options => options.AllowClrAccess().");
    }
}
