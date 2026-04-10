using Okojo.SourceGenerator.GlobalGeneration;
using Okojo.SourceGenerator.ObjectGeneration;

namespace Okojo.DocGenerator.Cli;

internal sealed class DeclarationOutputGroup
{
    public string RelativeFilePath { get; init; } = string.Empty;
    public List<GlobalTypeModel> GlobalModels { get; } = [];
    public List<JsObjectTypeModel> ObjectModels { get; } = [];
}
