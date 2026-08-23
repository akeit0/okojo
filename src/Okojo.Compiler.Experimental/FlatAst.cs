using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal sealed class FlatAst : IDisposable
{
    private readonly PooledArrayBuilder<FlatFunctionInfo> functions = new(8);

    public FlatAst(string source, string? sourcePath = null)
    {
        Arena = new AstArena(source);
        SourceText = source;
        SourcePath = sourcePath;
    }

    public AstArena Arena { get; }
    public string SourceText { get; }
    public string? SourcePath { get; }
    public bool StrictDeclared { get; set; }
    public int Count => Arena.Count;
    public int Root
    {
        get => Arena.Root;
        set => Arena.Root = value;
    }

    public ref AstNode this[int index] => ref Arena[index];
    public ReadOnlySpan<AstNode> Nodes => Arena.Nodes;

    public ReadOnlySpan<int> ChildRange(int offset, int count) => Arena.ChildRange(offset, count);

    public string GetString(int poolIndex) => Arena.GetString(poolIndex);

    public double GetNumber(int poolIndex) => Arena.GetNumber(poolIndex);

    public int GetPosition(int nodeIndex) => Arena.GetPosition(nodeIndex);

    public int AddFunction(FlatFunctionInfo function)
    {
        var index = functions.Count;
        functions.Add(function);
        return index;
    }

    public FlatFunctionInfo GetFunction(int index) => functions.AsSpan()[index];

    public void Dispose()
    {
        functions.Dispose();
        Arena.Dispose();
    }
}

internal readonly record struct FlatFunctionInfo(
    string Name,
    int NameId,
    FunctionParameterPlan ParameterPlan,
    bool StrictDeclared,
    int Position
);
