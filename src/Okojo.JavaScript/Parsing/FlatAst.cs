using System.Buffers;

namespace Okojo.JavaScript.Parsing;

internal sealed class FlatAst : IDisposable
{
    private FlatFunctionInfo[] functions;
    private int functionCount;
    private FlatParameter[] parameters;
    private int parameterCount;
    private bool disposed;

    public FlatAst(string source, string? sourcePath = null)
    {
        Arena = new AstArena(source);
        SourceText = source;
        SourcePath = sourcePath;
        functions = ArrayPool<FlatFunctionInfo>.Shared.Rent(8);
        parameters = ArrayPool<FlatParameter>.Shared.Rent(16);
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

    public (int Offset, int Count) AddParameters(ReadOnlySpan<FlatParameter> values)
    {
        EnsureParameterCapacity(values.Length);
        var offset = parameterCount;
        values.CopyTo(parameters.AsSpan(offset));
        parameterCount += values.Length;
        return (offset, values.Length);
    }

    public ReadOnlySpan<FlatParameter> GetParameters(in FlatFunctionInfo function) =>
        parameters.AsSpan(function.ParameterOffset, function.ParameterCount);

    public int AddFunction(FlatFunctionInfo function)
    {
        if (functionCount == functions.Length)
            GrowFunctions();
        var index = functionCount++;
        functions[index] = function;
        return index;
    }

    public FlatFunctionInfo GetFunction(int index)
    {
        if ((uint)index >= (uint)functionCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        return functions[index];
    }

    private void EnsureParameterCapacity(int additional)
    {
        if (parameterCount + additional <= parameters.Length)
            return;
        var next = ArrayPool<FlatParameter>.Shared.Rent(
            Math.Max(parameters.Length * 2, parameterCount + additional)
        );
        Array.Copy(parameters, next, parameterCount);
        ArrayPool<FlatParameter>.Shared.Return(parameters);
        parameters = next;
    }

    private void GrowFunctions()
    {
        var next = ArrayPool<FlatFunctionInfo>.Shared.Rent(functions.Length * 2);
        Array.Copy(functions, next, functionCount);
        ArrayPool<FlatFunctionInfo>.Shared.Return(functions);
        functions = next;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        ArrayPool<FlatFunctionInfo>.Shared.Return(functions);
        ArrayPool<FlatParameter>.Shared.Return(parameters);
        functions = [];
        parameters = [];
        functionCount = 0;
        parameterCount = 0;
        Arena.Dispose();
    }
}

internal readonly record struct FlatFunctionInfo(
    int NameStringIndex,
    int NameId,
    int ParameterOffset,
    int ParameterCount,
    int FunctionLength,
    int RestParameterIndex,
    bool StrictDeclared,
    bool HasSimpleParameterList,
    bool HasDuplicateParameters,
    int Position
);

internal readonly record struct FlatParameter(
    int NameStringIndex,
    int NameId,
    int InitializerNode,
    int PatternNode,
    int Position,
    JsFormalParameterBindingKind Kind
)
{
    public bool IsSimple =>
        Kind == JsFormalParameterBindingKind.Plain && InitializerNode < 0 && PatternNode < 0;
}
