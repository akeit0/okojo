using Okojo.Parsing;

namespace Okojo.Compiler;

internal enum FunctionParameterBindingKind : byte
{
    Plain,
    Rest,
    Pattern,
    RestPattern
}

internal readonly record struct BoundIdentifier(string Name, int NameId);

internal readonly record struct FunctionParameterBindingEntry(
    string Name,
    int NameId,
    int Position,
    FunctionParameterBindingKind Kind,
    JsExpression? Initializer,
    JsExpression? Pattern,
    IReadOnlyList<BoundIdentifier> BoundIdentifiers)
{
    public bool IsRest => Kind is FunctionParameterBindingKind.Rest or FunctionParameterBindingKind.RestPattern;

    public bool IsPattern =>
        Kind is FunctionParameterBindingKind.Pattern or FunctionParameterBindingKind.RestPattern;
}

internal sealed class FunctionParameterPlan
{
    public FunctionParameterPlan(
        IReadOnlyList<string> names,
        IReadOnlyList<JsExpression?> initializers,
        IReadOnlyList<FunctionParameterBindingEntry> bindings,
        int functionLength,
        bool hasSimpleParameterList,
        int restParameterIndex,
        bool hasPatternBindings,
        bool hasInitializers)
    {
        Names = names;
        Initializers = initializers;
        Bindings = bindings;
        FunctionLength = functionLength;
        HasSimpleParameterList = hasSimpleParameterList;
        RestParameterIndex = restParameterIndex;
        HasPatternBindings = hasPatternBindings;
        HasRestBinding = RestParameterIndex >= 0;
        HasInitializers = hasInitializers;
    }

    public IReadOnlyList<string> Names { get; }
    public IReadOnlyList<JsExpression?> Initializers { get; }
    public IReadOnlyList<FunctionParameterBindingEntry> Bindings { get; }
    public int FunctionLength { get; }
    public bool HasSimpleParameterList { get; }
    public int RestParameterIndex { get; }

    public bool HasPatternBindings { get; }
    public bool HasRestBinding { get; }
    public bool HasInitializers { get; }
    public FunctionParameterBindingEntry this[int index] => Bindings[index];

    public static FunctionParameterPlan FromFunction(JsFunctionExpression function)
    {
        if (function.Parameters.Count == 0)
            return Empty();

        return Create(
            function.Parameters,
            function.ParameterIds,
            function.ParameterInitializers,
            function.ParameterPatterns,
            function.ParameterPositions,
            function.ParameterBindingKinds,
            function.FunctionLength,
            function.HasSimpleParameterList,
            function.RestParameterIndex);
    }

    public static FunctionParameterPlan FromFunction(JsFunctionDeclaration function)
    {
        if (function.Parameters.Count == 0)
            return Empty();

        return Create(
            function.Parameters,
            function.ParameterIds,
            function.ParameterInitializers,
            function.ParameterPatterns,
            function.ParameterPositions,
            function.ParameterBindingKinds,
            function.FunctionLength,
            function.HasSimpleParameterList,
            function.RestParameterIndex);
    }

    public static FunctionParameterPlan Empty()
    {
        return new(
            Array.Empty<string>(),
            Array.Empty<JsExpression?>(),
            Array.Empty<FunctionParameterBindingEntry>(),
            0,
            true,
            -1,
            false,
            false);
    }

    public static FunctionParameterPlan FromCompilerInputs(
        IReadOnlyList<string> names,
        IReadOnlyList<int>? nameIds,
        IReadOnlyList<JsExpression?> initializers,
        int restParameterIndex,
        bool hasSimpleParameterList = true)
    {
        if (names.Count == 0)
            return Empty();

        return Create(
            names,
            nameIds ?? JsFunctionExpression.CreateDefaultParameterIds(names.Count),
            initializers,
            JsFunctionExpression.CreateDefaultInitializers(names.Count),
            JsFunctionExpression.CreateDefaultParameterPositions(names.Count),
            JsFunctionExpression.CreateDefaultParameterBindingKinds(names.Count, restParameterIndex),
            names.Count,
            hasSimpleParameterList,
            restParameterIndex);
    }

    private static FunctionParameterPlan Create(
        IReadOnlyList<string> names,
        IReadOnlyList<int> nameIds,
        IReadOnlyList<JsExpression?> initializers,
        IReadOnlyList<JsExpression?> patterns,
        IReadOnlyList<int> positions,
        IReadOnlyList<JsFormalParameterBindingKind> bindingKinds,
        int functionLength,
        bool hasSimpleParameterList,
        int restParameterIndex)
    {
        var bindings = new FunctionParameterBindingEntry[names.Count];
        var computedRestParameterIndex = -1;
        var hasPatternBindings = false;
        var hasInitializers = false;
        for (var i = 0; i < names.Count; i++)
        {
            var initializer = i < initializers.Count ? initializers[i] : null;
            var pattern = i < patterns.Count ? patterns[i] : null;
            var position = i < positions.Count ? positions[i] : -1;
            var kind = i < bindingKinds.Count
                ? ConvertBindingKind(bindingKinds[i])
                : i == restParameterIndex
                    ? FunctionParameterBindingKind.Rest
                    : FunctionParameterBindingKind.Plain;
            if (initializer is not null)
                hasInitializers = true;
            if (kind is FunctionParameterBindingKind.Pattern or FunctionParameterBindingKind.RestPattern)
                hasPatternBindings = true;
            if (computedRestParameterIndex < 0 &&
                kind is FunctionParameterBindingKind.Rest or FunctionParameterBindingKind.RestPattern)
                computedRestParameterIndex = i;
            bindings[i] = new(
                names[i],
                i < nameIds.Count ? nameIds[i] : -1,
                position,
                kind,
                initializer,
                pattern,
                CollectPatternBoundIdentifiers(pattern));
        }

        return new(
            names,
            initializers,
            bindings,
            functionLength,
            hasSimpleParameterList,
            computedRestParameterIndex,
            hasPatternBindings,
            hasInitializers);
    }

    private static FunctionParameterBindingKind ConvertBindingKind(JsFormalParameterBindingKind kind)
    {
        return kind switch
        {
            JsFormalParameterBindingKind.Plain => FunctionParameterBindingKind.Plain,
            JsFormalParameterBindingKind.Rest => FunctionParameterBindingKind.Rest,
            JsFormalParameterBindingKind.Pattern => FunctionParameterBindingKind.Pattern,
            JsFormalParameterBindingKind.RestPattern => FunctionParameterBindingKind.RestPattern,
            _ => FunctionParameterBindingKind.Plain
        };
    }

    private static IReadOnlyList<BoundIdentifier> CollectPatternBoundIdentifiers(JsExpression? pattern)
    {
        if (pattern is null)
            return Array.Empty<BoundIdentifier>();

        if (pattern is JsIdentifierExpression id)
            return new[] { new BoundIdentifier(id.Name, id.NameId) };

        var identifiers = new List<BoundIdentifier>(4);
        CollectPatternBoundIdentifiersCore(pattern, identifiers);
        return identifiers.Count == 0 ? Array.Empty<BoundIdentifier>() : identifiers.ToArray();
    }

    private static void CollectPatternBoundIdentifiersCore(JsExpression pattern, List<BoundIdentifier> identifiers)
    {
        switch (pattern)
        {
            case JsIdentifierExpression id:
                identifiers.Add(new(id.Name, id.NameId));
                return;
            case JsSpreadExpression spread:
                CollectPatternBoundIdentifiersCore(spread.Argument, identifiers);
                return;
            case JsArrayExpression arrayPattern:
                for (var i = 0; i < arrayPattern.Elements.Count; i++)
                {
                    var element = arrayPattern.Elements[i];
                    if (element is not null)
                        CollectPatternBoundIdentifiersCore(element, identifiers);
                }

                return;
            case JsObjectExpression objectPattern:
                for (var i = 0; i < objectPattern.Properties.Count; i++)
                {
                    var property = objectPattern.Properties[i];
                    if (property.Kind is JsObjectPropertyKind.Spread)
                    {
                    }

                    CollectPatternBoundIdentifiersCore(property.Value, identifiers);
                }

                return;
            case JsAssignmentExpression { Operator: JsAssignmentOperator.Assign, Left: var left }:
                CollectPatternBoundIdentifiersCore(left, identifiers);
                return;
        }
    }

    public int GetLastBindingIndex(string name, int nameId = -1)
    {
        for (var i = Bindings.Count - 1; i >= 0; i--)
        {
            if (nameId >= 0)
            {
                if (Bindings[i].NameId == nameId)
                    return i;
                continue;
            }

            if (string.Equals(Bindings[i].Name, name, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    public bool TryGetRestBinding(out int index, out FunctionParameterBindingEntry binding)
    {
        index = RestParameterIndex;
        if ((uint)index >= (uint)Bindings.Count)
        {
            binding = default;
            return false;
        }

        binding = Bindings[index];
        return binding.IsRest;
    }
}
