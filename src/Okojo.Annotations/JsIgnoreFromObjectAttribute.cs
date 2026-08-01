namespace Okojo.Annotations;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class JsIgnoreFromObjectAttribute : Attribute
{
}
