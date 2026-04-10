namespace Okojo.DocGenerator.Annotations;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field,
    Inherited = false)]
public sealed class DocIgnoreAttribute : Attribute
{
}
