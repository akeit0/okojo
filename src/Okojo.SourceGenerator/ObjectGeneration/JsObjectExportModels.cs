using Microsoft.CodeAnalysis;

namespace Okojo.SourceGenerator.ObjectGeneration;

internal enum JsObjectMemberKind : byte
{
    Field,
    Property,
    Method
}

internal sealed class JsObjectParameterModel(string name, ITypeSymbol type)
{
    public string Name { get; } = name;
    public ITypeSymbol Type { get; } = type;
}

internal sealed class JsObjectMemberModel(
    string name,
    JsObjectMemberKind kind,
    bool isStatic,
    ISymbol symbol,
    ITypeSymbol type,
    bool canRead,
    bool canWrite,
    IReadOnlyList<JsObjectParameterModel>? parameters = null)
{
    public string Name { get; } = name;
    public JsObjectMemberKind Kind { get; } = kind;
    public bool IsStatic { get; } = isStatic;
    public ISymbol Symbol { get; } = symbol;
    public ITypeSymbol Type { get; } = type;
    public bool CanRead { get; } = canRead;
    public bool CanWrite { get; } = canWrite;

    public IReadOnlyList<JsObjectParameterModel> Parameters { get; } =
        parameters ?? Array.Empty<JsObjectParameterModel>();
}

internal sealed class JsObjectTypeModel(
    INamedTypeSymbol symbol,
    IReadOnlyList<JsObjectMemberModel> instanceMembers,
    IReadOnlyList<JsObjectMemberModel> staticMembers)
{
    public INamedTypeSymbol Symbol { get; } = symbol;
    public IReadOnlyList<JsObjectMemberModel> InstanceMembers { get; } = instanceMembers;
    public IReadOnlyList<JsObjectMemberModel> StaticMembers { get; } = staticMembers;
}
