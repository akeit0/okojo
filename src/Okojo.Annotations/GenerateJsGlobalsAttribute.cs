namespace Okojo.Annotations;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GenerateJsGlobalsAttribute : Attribute
{
    public string InstallerMethodName { get; set; } = "InstallGeneratedGlobals";
    public string PropertySourceMethodName { get; set; } = "GetGeneratedGlobalProperties";
}
