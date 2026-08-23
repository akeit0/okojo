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
    private FlatModuleRequest[] moduleRequests;
    private int moduleRequestCount;
    private FlatImportEntry[] importEntries;
    private int importEntryCount;
    private FlatImportAttribute[] importAttributes;
    private int importAttributeCount;
    private FlatExportEntry[] exportEntries;
    private int exportEntryCount;
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
        moduleRequests = [];
        importEntries = [];
        importAttributes = [];
        exportEntries = [];
    }

    public AstArena Arena { get; }
    public string SourceText { get; }
    public string? SourcePath { get; }
    public bool StrictDeclared { get; set; }
    public bool IsModule { get; set; }
    public bool HasTopLevelAwait { get; set; }
    public HashSet<string>? ModuleVarBindings { get; set; }
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

    public int AddModuleRequest(
        int specifierStringIndex,
        int position,
        ReadOnlySpan<FlatImportAttribute> attributes
    )
    {
        EnsureModuleRequestCapacity(1);
        EnsureImportAttributeCapacity(attributes.Length);
        var attributeOffset = importAttributeCount;
        attributes.CopyTo(importAttributes.AsSpan(attributeOffset));
        importAttributeCount += attributes.Length;
        var index = moduleRequestCount++;
        moduleRequests[index] = new(
            specifierStringIndex,
            attributeOffset,
            attributes.Length,
            position
        );
        return index;
    }

    public (int Offset, int Count) AddImportEntries(ReadOnlySpan<FlatImportEntry> values)
    {
        EnsureImportEntryCapacity(values.Length);
        var offset = importEntryCount;
        values.CopyTo(importEntries.AsSpan(offset));
        importEntryCount += values.Length;
        return (offset, values.Length);
    }

    public ReadOnlySpan<FlatModuleRequest> ModuleRequests =>
        moduleRequests.AsSpan(0, moduleRequestCount);

    public ReadOnlySpan<FlatImportEntry> GetImportEntries(in AstNode declaration) =>
        importEntries.AsSpan(declaration.Arg1, declaration.Arg2);

    public ReadOnlySpan<FlatImportEntry> ImportEntries => importEntries.AsSpan(0, importEntryCount);

    public ReadOnlySpan<FlatImportAttribute> GetImportAttributes(in FlatModuleRequest request) =>
        importAttributes.AsSpan(request.AttributeOffset, request.AttributeCount);

    public (int Offset, int Count) AddExportEntries(ReadOnlySpan<FlatExportEntry> values)
    {
        EnsureExportEntryCapacity(values.Length);
        var offset = exportEntryCount;
        values.CopyTo(exportEntries.AsSpan(offset));
        exportEntryCount += values.Length;
        return (offset, values.Length);
    }

    public ReadOnlySpan<FlatExportEntry> GetExportEntries(in AstNode declaration) =>
        exportEntries.AsSpan(declaration.Arg1, declaration.Arg2);

    public ReadOnlySpan<FlatExportEntry> ExportEntries => exportEntries.AsSpan(0, exportEntryCount);

    public void FinalizeModuleDescriptor()
    {
        if (importEntryCount == 0 && exportEntryCount == 0)
            return;

        if (importEntryCount != 0)
        {
            var importsByLocalName = new Dictionary<string, int>(
                importEntryCount,
                StringComparer.Ordinal
            );
            for (var i = 0; i < importEntryCount; i++)
                importsByLocalName.Add(GetString(importEntries[i].LocalNameStringIndex), i);

            for (var i = 0; i < exportEntryCount; i++)
            {
                ref var export = ref exportEntries[i];
                if (
                    export.Kind != FlatExportKind.Local
                    || !importsByLocalName.TryGetValue(
                        GetString(export.LocalNameStringIndex),
                        out var importIndex
                    )
                )
                    continue;

                ref readonly var import = ref importEntries[importIndex];
                export = export with
                {
                    ModuleRequestIndex = import.ModuleRequestIndex,
                    LocalNameStringIndex = -1,
                    ImportNameStringIndex =
                        import.Kind == FlatImportKind.Namespace
                            ? -1
                            : import.ImportedNameStringIndex,
                    Kind =
                        import.Kind == FlatImportKind.Namespace
                            ? FlatExportKind.Namespace
                            : FlatExportKind.Indirect,
                    Position = import.Position,
                };
            }

            var importNames = new List<string>(importEntryCount);
            for (var i = 0; i < importEntryCount; i++)
            {
                if (importEntries[i].Kind != FlatImportKind.Namespace)
                    importNames.Add(GetString(importEntries[i].LocalNameStringIndex));
            }
            importNames.Sort(StringComparer.Ordinal);
            for (var i = 0; i < importEntryCount; i++)
            {
                if (importEntries[i].Kind == FlatImportKind.Namespace)
                    continue;
                importEntries[i] = importEntries[i] with
                {
                    CellIndex = -(
                        importNames.BinarySearch(
                            GetString(importEntries[i].LocalNameStringIndex),
                            StringComparer.Ordinal
                        ) + 1
                    ),
                };
            }
        }

        if (exportEntryCount == 0)
            return;
        var exportNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < exportEntryCount; i++)
        {
            if (exportEntries[i].LocalNameStringIndex >= 0)
                exportNames.Add(GetString(exportEntries[i].LocalNameStringIndex));
        }
        var sortedExportNames = exportNames.ToList();
        sortedExportNames.Sort(StringComparer.Ordinal);
        for (var i = 0; i < exportEntryCount; i++)
        {
            if (exportEntries[i].LocalNameStringIndex < 0)
                continue;
            exportEntries[i] = exportEntries[i] with
            {
                CellIndex =
                    sortedExportNames.BinarySearch(
                        GetString(exportEntries[i].LocalNameStringIndex),
                        StringComparer.Ordinal
                    ) + 1,
            };
        }
    }

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

    public void SetFunction(int index, FlatFunctionInfo function)
    {
        if ((uint)index >= (uint)functionCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        functions[index] = function;
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

    private void EnsureModuleRequestCapacity(int additional)
    {
        if (moduleRequestCount + additional <= moduleRequests.Length)
            return;
        var next = ArrayPool<FlatModuleRequest>.Shared.Rent(
            Math.Max(Math.Max(4, moduleRequests.Length * 2), moduleRequestCount + additional)
        );
        Array.Copy(moduleRequests, next, moduleRequestCount);
        if (moduleRequests.Length != 0)
            ArrayPool<FlatModuleRequest>.Shared.Return(moduleRequests);
        moduleRequests = next;
    }

    private void EnsureImportEntryCapacity(int additional)
    {
        if (importEntryCount + additional <= importEntries.Length)
            return;
        var next = ArrayPool<FlatImportEntry>.Shared.Rent(
            Math.Max(Math.Max(8, importEntries.Length * 2), importEntryCount + additional)
        );
        Array.Copy(importEntries, next, importEntryCount);
        if (importEntries.Length != 0)
            ArrayPool<FlatImportEntry>.Shared.Return(importEntries);
        importEntries = next;
    }

    private void EnsureImportAttributeCapacity(int additional)
    {
        if (importAttributeCount + additional <= importAttributes.Length)
            return;
        var next = ArrayPool<FlatImportAttribute>.Shared.Rent(
            Math.Max(Math.Max(4, importAttributes.Length * 2), importAttributeCount + additional)
        );
        Array.Copy(importAttributes, next, importAttributeCount);
        if (importAttributes.Length != 0)
            ArrayPool<FlatImportAttribute>.Shared.Return(importAttributes);
        importAttributes = next;
    }

    private void EnsureExportEntryCapacity(int additional)
    {
        if (exportEntryCount + additional <= exportEntries.Length)
            return;
        var next = ArrayPool<FlatExportEntry>.Shared.Rent(
            Math.Max(Math.Max(8, exportEntries.Length * 2), exportEntryCount + additional)
        );
        Array.Copy(exportEntries, next, exportEntryCount);
        if (exportEntries.Length != 0)
            ArrayPool<FlatExportEntry>.Shared.Return(exportEntries);
        exportEntries = next;
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
        if (moduleRequests.Length != 0)
            ArrayPool<FlatModuleRequest>.Shared.Return(moduleRequests);
        if (importEntries.Length != 0)
            ArrayPool<FlatImportEntry>.Shared.Return(importEntries);
        if (importAttributes.Length != 0)
            ArrayPool<FlatImportAttribute>.Shared.Return(importAttributes);
        if (exportEntries.Length != 0)
            ArrayPool<FlatExportEntry>.Shared.Return(exportEntries);
        functions = [];
        parameters = [];
        objectProperties = [];
        classes = [];
        classElements = [];
        moduleRequests = [];
        importEntries = [];
        importAttributes = [];
        exportEntries = [];
        functionCount = 0;
        parameterCount = 0;
        objectPropertyCount = 0;
        classCount = 0;
        classElementCount = 0;
        moduleRequestCount = 0;
        importEntryCount = 0;
        importAttributeCount = 0;
        exportEntryCount = 0;
        Arena.Dispose();
    }
}

internal readonly record struct FlatModuleRequest(
    int SpecifierStringIndex,
    int AttributeOffset,
    int AttributeCount,
    int Position
);

internal readonly record struct FlatImportEntry(
    int ModuleRequestIndex,
    int ImportedNameStringIndex,
    int LocalNameStringIndex,
    int LocalNameId,
    FlatImportKind Kind,
    int Position,
    int CellIndex = 0
);

internal readonly record struct FlatImportAttribute(
    int KeyStringIndex,
    int ValueStringIndex,
    int Position
);

internal enum FlatImportKind : byte
{
    Default,
    Named,
    Namespace,
}

internal readonly record struct FlatExportEntry(
    int ModuleRequestIndex,
    int LocalNameStringIndex,
    int ImportNameStringIndex,
    int ExportNameStringIndex,
    FlatExportKind Kind,
    int Position,
    int CellIndex = 0
);

internal enum FlatExportKind : byte
{
    Local,
    Indirect,
    Namespace,
    Star,
    DefaultExpression,
    DefaultDeclaration,
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
    bool EmitImplicitSuperForwardAll = false,
    bool HasSuperPropertyReference = false,
    int ReturnInferredNameStringIndex = -1,
    bool ReturnInferredNameFromFirstParameter = false
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
    Private = 4,
}

internal readonly record struct FlatClassElement(
    int Key,
    int ValueNode,
    int Position,
    JsClassElementKind Kind,
    FlatClassElementFlags Flags,
    int InstanceFieldKeyIndex = -1
)
{
    public bool IsStatic => (Flags & FlatClassElementFlags.Static) != 0;
    public bool IsComputed => (Flags & FlatClassElementFlags.Computed) != 0;
    public bool IsPrivate => (Flags & FlatClassElementFlags.Private) != 0;
}
