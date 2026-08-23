using System.Buffers;

namespace Okojo.JavaScript.Parsing;

internal sealed class FlatAst : IDisposable
{
    private FlatFunctionInfo[] functions;
    private int functionCount;
    private FlatParameter[] parameters;
    private int parameterCount;
    private FlatObjectProperty[] objectProperties;
    private int objectPropertyCount;
    private FlatClassInfo[] classes;
    private int classCount;
    private FlatClassElement[] classElements;
    private int classElementCount;
    private bool disposed;

    public FlatAst(string source, string? sourcePath = null)
    {
        Arena = new AstArena(source);
        SourceText = source;
        SourcePath = sourcePath;
        functions = ArrayPool<FlatFunctionInfo>.Shared.Rent(8);
        parameters = ArrayPool<FlatParameter>.Shared.Rent(16);
        objectProperties = ArrayPool<FlatObjectProperty>.Shared.Rent(16);
        classes = ArrayPool<FlatClassInfo>.Shared.Rent(4);
        classElements = ArrayPool<FlatClassElement>.Shared.Rent(16);
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

    public (int Offset, int Count) AddObjectProperties(ReadOnlySpan<FlatObjectProperty> values)
    {
        EnsureObjectPropertyCapacity(values.Length);
        var offset = objectPropertyCount;
        values.CopyTo(objectProperties.AsSpan(offset));
        objectPropertyCount += values.Length;
        return (offset, values.Length);
    }

    public ReadOnlySpan<FlatObjectProperty> GetObjectProperties(int offset, int count) =>
        objectProperties.AsSpan(offset, count);

    public (int Offset, int Count) AddClassElements(ReadOnlySpan<FlatClassElement> values)
    {
        EnsureClassElementCapacity(values.Length);
        var offset = classElementCount;
        values.CopyTo(classElements.AsSpan(offset));
        classElementCount += values.Length;
        return (offset, values.Length);
    }

    public ReadOnlySpan<FlatClassElement> GetClassElements(in FlatClassInfo info) =>
        classElements.AsSpan(info.ElementOffset, info.ElementCount);

    public int AddClass(FlatClassInfo info)
    {
        if (classCount == classes.Length)
        {
            var next = ArrayPool<FlatClassInfo>.Shared.Rent(classes.Length * 2);
            Array.Copy(classes, next, classCount);
            ArrayPool<FlatClassInfo>.Shared.Return(classes);
            classes = next;
        }
        var index = classCount++;
        classes[index] = info;
        return index;
    }

    public FlatClassInfo GetClass(int index)
    {
        if ((uint)index >= (uint)classCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        return classes[index];
    }

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

    private void EnsureObjectPropertyCapacity(int additional)
    {
        if (objectPropertyCount + additional <= objectProperties.Length)
            return;
        var next = ArrayPool<FlatObjectProperty>.Shared.Rent(
            Math.Max(objectProperties.Length * 2, objectPropertyCount + additional)
        );
        Array.Copy(objectProperties, next, objectPropertyCount);
        ArrayPool<FlatObjectProperty>.Shared.Return(objectProperties);
        objectProperties = next;
    }

    private void EnsureClassElementCapacity(int additional)
    {
        if (classElementCount + additional <= classElements.Length)
            return;
        var next = ArrayPool<FlatClassElement>.Shared.Rent(
            Math.Max(classElements.Length * 2, classElementCount + additional)
        );
        Array.Copy(classElements, next, classElementCount);
        ArrayPool<FlatClassElement>.Shared.Return(classElements);
        classElements = next;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        ArrayPool<FlatFunctionInfo>.Shared.Return(functions);
        ArrayPool<FlatParameter>.Shared.Return(parameters);
        ArrayPool<FlatObjectProperty>.Shared.Return(objectProperties);
        ArrayPool<FlatClassInfo>.Shared.Return(classes);
        ArrayPool<FlatClassElement>.Shared.Return(classElements);
        functions = [];
        parameters = [];
        objectProperties = [];
        classes = [];
        classElements = [];
        functionCount = 0;
        parameterCount = 0;
        objectPropertyCount = 0;
        classCount = 0;
        classElementCount = 0;
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
    int Position,
    bool IsMethod,
    bool IsArrow = false,
    bool IsGenerator = false,
    bool IsAsync = false,
    bool IsClassConstructor = false,
    bool IsDerivedConstructor = false,
    bool EmitImplicitSuperForwardAll = false
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

[Flags]
internal enum FlatObjectPropertyFlags : byte
{
    None = 0,
    Computed = 1,
    Rest = 2,
    CoverInitializedName = 4,
    Getter = 8,
    Setter = 16,
}

internal readonly record struct FlatObjectProperty(
    int Key,
    int ValueNode,
    int Position,
    FlatObjectPropertyFlags Flags
)
{
    public bool IsComputed => (Flags & FlatObjectPropertyFlags.Computed) != 0;
    public bool IsRest => (Flags & FlatObjectPropertyFlags.Rest) != 0;
    public bool IsGetter => (Flags & FlatObjectPropertyFlags.Getter) != 0;
    public bool IsSetter => (Flags & FlatObjectPropertyFlags.Setter) != 0;
    public bool IsAccessor => IsGetter || IsSetter;
    public bool IsCoverInitializedName =>
        (Flags & FlatObjectPropertyFlags.CoverInitializedName) != 0;
}

internal readonly record struct FlatClassInfo(
    int NameStringIndex,
    int NameId,
    int ElementOffset,
    int ElementCount,
    int ConstructorNode,
    int ExtendsNode,
    int Position
)
{
    public bool HasExtends => ExtendsNode >= 0;
}

[Flags]
internal enum FlatClassElementFlags : byte
{
    None = 0,
    Static = 1,
    Computed = 2,
}

internal readonly record struct FlatClassElement(
    int Key,
    int ValueNode,
    int Position,
    JsClassElementKind Kind,
    FlatClassElementFlags Flags
)
{
    public bool IsStatic => (Flags & FlatClassElementFlags.Static) != 0;
    public bool IsComputed => (Flags & FlatClassElementFlags.Computed) != 0;
}
