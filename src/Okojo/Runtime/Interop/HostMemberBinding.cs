namespace Okojo.Runtime.Interop;

public sealed class HostMemberBinding(
    string name,
    HostMemberBindingKind kind,
    bool isStatic,
    JsHostFunctionBody? getterBody = null,
    JsHostFunctionBody? setterBody = null,
    JsHostFunctionBody? methodBody = null,
    int functionLength = 0)
{
    public string Name { get; } = name;
    public HostMemberBindingKind Kind { get; } = kind;
    public bool IsStatic { get; } = isStatic;
    public JsHostFunctionBody? GetterBody { get; } = getterBody;
    public JsHostFunctionBody? SetterBody { get; } = setterBody;
    public JsHostFunctionBody? MethodBody { get; } = methodBody;
    public int FunctionLength { get; } = functionLength;
}
