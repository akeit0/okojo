namespace Okojo.JavaScript.Bytecode;

/// <summary>
/// A function's immutable creation recipe. Compilation produces this descriptor,
/// never a JavaScript function object. Many closures use one realm-local instance.
/// </summary>
public sealed class JsFunctionDescriptor
{
    internal JsFunctionDescriptor(
        JsFunctionCode code,
        string name = "",
        bool requiresClosureBinding = false,
        bool isStrict = false,
        int privateBrandId = 0,
        bool hasNewTarget = false,
        bool isDerivedConstructor = false,
        JsBytecodeFunctionKind kind = JsBytecodeFunctionKind.Normal,
        bool isArrow = false,
        bool isMethod = false,
        int formalParameterCount = 0,
        bool hasSimpleParameterList = true,
        bool isClassConstructor = false,
        bool hasEagerGeneratorParameterBinding = false,
        int expectedArgumentCount = 0
    )
    {
        Code = code;
        Name = name;
        Kind = kind;
        RequiresClosureBinding = requiresClosureBinding;
        IsStrict = isStrict;
        PrivateBrandId = privateBrandId;
        HasNewTarget = hasNewTarget;
        IsDerivedConstructor = isDerivedConstructor;
        IsArrow = isArrow;
        IsMethod = isMethod;
        FormalParameterCount = formalParameterCount;
        HasSimpleParameterList = hasSimpleParameterList;
        IsClassConstructor = isClassConstructor;
        HasEagerGeneratorParameterBinding = hasEagerGeneratorParameterBinding;
        ExpectedArgumentCount = expectedArgumentCount;
        HasPrototypeProperty =
            !isArrow
            && (
                kind is JsBytecodeFunctionKind.Generator or JsBytecodeFunctionKind.AsyncGenerator
                || (!isMethod && kind is not JsBytecodeFunctionKind.Async)
            );
        IsConstructor = !isArrow && !isMethod && kind == JsBytecodeFunctionKind.Normal;
        PrototypeHasConstructor =
            kind is not (JsBytecodeFunctionKind.Generator or JsBytecodeFunctionKind.AsyncGenerator);
    }

    public JsFunctionCode Code { get; }
    public string Name { get; }
    public JsBytecodeFunctionKind Kind { get; }
    public int FormalParameterCount { get; }
    public int ExpectedArgumentCount { get; }
    public bool IsStrict { get; }
    public bool IsArrow { get; }
    public bool IsMethod { get; }
    public bool IsClassConstructor { get; }
    public bool IsDerivedConstructor { get; }
    public bool HasSimpleParameterList { get; }
    public bool HasNewTarget { get; }
    internal bool RequiresClosureBinding { get; }
    internal int PrivateBrandId { get; }
    internal bool HasEagerGeneratorParameterBinding { get; }
    internal bool HasPrototypeProperty { get; }
    internal bool PrototypeHasConstructor { get; }
    internal bool IsConstructor { get; }
    internal int[]? ArgumentsMappedSlots { get; init; }
    internal int SuperBaseContextSlot { get; init; } = -1;
    internal int DerivedThisContextSlot { get; init; } = -1;
    internal int LexicalThisContextSlot { get; init; } = -1;
    internal int LexicalThisContextDepth { get; init; } = -1;

    /// <summary>Links this recipe to a realm and creates a fresh, unbound closure.</summary>
    public JsBytecodeFunction CreateClosure(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        return new(realm.LinkFunction(this));
    }
}
