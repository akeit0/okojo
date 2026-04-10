namespace Okojo.Runtime;

internal sealed class JsPrivateAccessorDescriptor(JsRealm realm, JsFunction? getter, JsFunction? setter)
    : JsObject(realm)
{
    public JsFunction? Getter { get; } = getter;
    public JsFunction? Setter { get; } = setter;
}
