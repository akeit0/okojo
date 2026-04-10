namespace Okojo.DocGenerator.Annotations;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
public sealed class DocDeclarationAttribute : Attribute
{
    public DocDeclarationAttribute(string fileName)
    {
        FileName = fileName;
    }

    public DocDeclarationAttribute(string fileName, string @namespace)
    {
        FileName = fileName;
        Namespace = @namespace;
    }

    public string FileName { get; }
    public string? Namespace { get; }
}
