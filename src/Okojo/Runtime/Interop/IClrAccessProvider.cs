using System.Reflection;

namespace Okojo.Runtime.Interop;

internal interface IClrAccessProvider
{
    HostTypeDescriptor CreateHostTypeDescriptor(JsAgent agent, Type clrType, int typeId);

    JsHostFunction GetClrTypeFunction(JsRealm realm, Type type, HostBinding? binding = null);

    bool TryConvertTaskObjectToJsValue(JsRealm realm, object value, out JsValue jsValue);

    bool TryConvertJsValueToTaskObject(JsRealm realm, JsValue value, Type targetType, out object? result,
        out int score);

    JsValue BindGenericMethod(JsRealm realm, string memberName, object? target, MethodInfo[] methods,
        ReadOnlySpan<JsValue> typeArguments, int genericParameterCount);

    Array CreateParamsArray(Type elementType, int length);

    JsObject GetClrNamespace(JsRealm realm, string? namespacePath = null);

    JsValue ResolveClrPath(JsRealm realm, string path);

    bool TryResolveClrPathExactly(JsRealm realm, string path, out JsValue value);

    JsHostFunction CreateClrTypedNullHelperFunction(JsRealm realm);

    JsHostFunction CreateClrPlaceHolderHelperFunction(JsRealm realm);

    JsHostFunction CreateClrCastHelperFunction(JsRealm realm);

    JsHostFunction CreateClrUsingHelperFunction(JsRealm realm);
}
