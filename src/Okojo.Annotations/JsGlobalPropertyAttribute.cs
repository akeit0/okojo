namespace Okojo.Annotations;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class JsGlobalPropertyAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
    public bool Writable { get; set; }
    public bool Enumerable { get; set; } = true;
    public bool Configurable { get; set; } = true;
}
