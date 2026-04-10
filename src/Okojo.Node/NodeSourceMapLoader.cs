using Okojo.SourceMaps;

namespace Okojo.Node;

internal sealed class NodeSourceMapLoader(SourceMapRegistry registry)
{
    private readonly SourceMapScriptLoader sourceMapLoader = new(registry);

    public void TryRegister(string generatedSourcePath, string sourceText)
    {
        sourceMapLoader.TryRegister(generatedSourcePath, sourceText);
    }
}
