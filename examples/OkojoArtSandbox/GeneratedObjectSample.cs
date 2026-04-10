using Okojo.Annotations;
using Okojo.DocGenerator.Annotations;

[DocDeclaration("objects\\GeneratedObjectSample", "OkojoArtSandbox")]
[GenerateJsObject]
internal partial class GeneratedObjectSample
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }

    public bool DoSomething()
    {
        return true;
    }
}
