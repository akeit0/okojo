using Microsoft.CodeAnalysis;

namespace Okojo.SourceGenerator.GlobalGeneration;

internal sealed class GlobalParameterModel(string name, ITypeSymbol type, bool hasDefaultValue, object? defaultValue)
{
    public string Name { get; } = name;
    public ITypeSymbol Type { get; } = type;
    public bool HasDefaultValue { get; } = hasDefaultValue;
    public object? DefaultValue { get; } = defaultValue;
}

internal sealed class GlobalFunctionModel(
    string name,
    IMethodSymbol symbol,
    int length,
    bool isConstructor,
    IReadOnlyList<GlobalParameterModel> parameters)
{
    public string Name { get; } = name;
    public IMethodSymbol Symbol { get; } = symbol;
    public int Length { get; } = length;
    public bool IsConstructor { get; } = isConstructor;
    public IReadOnlyList<GlobalParameterModel> Parameters { get; } = parameters;
}

internal sealed class GlobalPropertyModel(
    string name,
    ISymbol symbol,
    ITypeSymbol type,
    bool writable,
    bool enumerable,
    bool configurable)
{
    public string Name { get; } = name;
    public ISymbol Symbol { get; } = symbol;
    public ITypeSymbol Type { get; } = type;
    public bool Writable { get; } = writable;
    public bool Enumerable { get; } = enumerable;
    public bool Configurable { get; } = configurable;
}

internal sealed class GlobalTypeModel(
    INamedTypeSymbol symbol,
    string installerMethodName,
    string propertySourceMethodName,
    IReadOnlyList<GlobalFunctionModel> functions,
    IReadOnlyList<GlobalPropertyModel> properties)
{
    public INamedTypeSymbol Symbol { get; } = symbol;
    public string InstallerMethodName { get; } = installerMethodName;
    public string PropertySourceMethodName { get; } = propertySourceMethodName;
    public IReadOnlyList<GlobalFunctionModel> Functions { get; } = functions;
    public IReadOnlyList<GlobalPropertyModel> Properties { get; } = properties;
}
