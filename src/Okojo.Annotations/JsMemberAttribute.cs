namespace Okojo.Annotations;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class JsMemberAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}
