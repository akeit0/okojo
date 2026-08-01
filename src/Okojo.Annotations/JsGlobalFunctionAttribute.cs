namespace Okojo.Annotations;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class JsGlobalFunctionAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
    public int Length { get; set; } = -1;
    public bool IsConstructor { get; set; }
}
