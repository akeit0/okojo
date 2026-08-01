namespace Okojo.Annotations;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GenerateJsObjectAttribute : Attribute
{
    public JsMemberNaming MemberNaming { get; set; } = JsMemberNaming.LowerCamelCase;
}
