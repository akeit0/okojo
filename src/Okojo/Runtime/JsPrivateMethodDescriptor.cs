namespace Okojo.Runtime;

internal sealed class JsPrivateMethodDescriptor(JsRealm realm, JsFunction method) : JsObject(realm)
{
    public JsFunction Method { get; } = method;
}
