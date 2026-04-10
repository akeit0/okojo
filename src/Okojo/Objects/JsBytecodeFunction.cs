using Okojo.Bytecode;

namespace Okojo.Objects;

public sealed class JsBytecodeFunction : JsFunction
{
    private JsContext? functionMetadataContext;

    public JsBytecodeFunction(JsRealm realm, JsScript script, string name = "",
        bool requiresClosureBinding = false, bool isStrict = false, int privateBrandId = 0,
        bool assignFunctionPrototype = true, bool hasNewTarget = false, bool isDerivedConstructor = false,
        JsBytecodeFunctionKind kind = JsBytecodeFunctionKind.Normal, bool isArrow = false, bool isMethod = false,
        int formalParameterCount = 0, bool hasSimpleParameterList = true,
        bool isClassConstructor = false,
        bool hasEagerGeneratorParameterBinding = false,
        int expectedArgumentCount = 0)
        : base(realm, name, assignFunctionPrototype,
            expectedArgumentCount,
            !isArrow &&
            (kind is JsBytecodeFunctionKind.Generator or JsBytecodeFunctionKind.AsyncGenerator ||
             (!isMethod && kind is not JsBytecodeFunctionKind.Async)),
            kind is not (JsBytecodeFunctionKind.Generator or JsBytecodeFunctionKind.AsyncGenerator),
            !isArrow && !isMethod &&
            kind != JsBytecodeFunctionKind.Generator &&
            kind != JsBytecodeFunctionKind.Async &&
            kind != JsBytecodeFunctionKind.AsyncGenerator)
    {
        Script = script;
        Script.BindAgent(realm.Agent);
        Kind = kind;
        RequiresClosureBinding = requiresClosureBinding;
        HasNewTarget = hasNewTarget;
        IsDerivedConstructor = isDerivedConstructor;
        IsArrow = isArrow;
        IsMethod = isMethod;
        FormalParameterCount = formalParameterCount;
        HasSimpleParameterList = hasSimpleParameterList;
        HasEagerGeneratorParameterBinding = hasEagerGeneratorParameterBinding;
        IsClassConstructor = isClassConstructor;
        IsStrict = isStrict;
        PrivateBrandId = privateBrandId;

        if (assignFunctionPrototype)
            Prototype = realm.Intrinsics.GetFunctionPrototypeForKind(kind);

        if (isClassConstructor &&
            TryGetOwnNamedPropertyDescriptorAtom(realm, IdPrototype, out var classPrototypeDescriptor) &&
            !classPrototypeDescriptor.IsAccessor)
            _ = DefineOwnDataPropertyExact(realm, IdPrototype, classPrototypeDescriptor.Value,
                JsShapePropertyFlags.None);
    }

    public JsScript Script { get; internal set; }
    public JsBytecodeFunctionKind Kind { get; }
    public bool RequiresClosureBinding { get; }
    public bool HasNewTarget { get; }
    public bool IsDerivedConstructor { get; }
    public bool IsArrow { get; }
    public bool IsMethod { get; }
    public bool UsesResumeModeDispatch => Kind != JsBytecodeFunctionKind.Normal;
    public int FormalParameterCount { get; }
    public bool HasSimpleParameterList { get; }
    public int[]? ArgumentsMappedSlots { get; set; }
    public bool HasEagerGeneratorParameterBinding { get; }
    public bool IsClassConstructor { get; }
    public JsContext? BoundParentContext { get; set; }

    public JsValue[]? PrecomputedPrivateMethodValues
    {
        get => functionMetadataContext?.Metadata?.PrecomputedPrivateMethodValues;
        set => GetOrCreateFunctionMetadata().PrecomputedPrivateMethodValues = value;
    }

    public Dictionary<int, JsObject>? PrivateBrandTokensByBrandId
    {
        get => functionMetadataContext?.Metadata?.PrivateBrandTokensByBrandId;
        set => GetOrCreateFunctionMetadata().PrivateBrandTokensByBrandId = value;
    }

    public JsObject? PrivateBrandToken
    {
        get => functionMetadataContext?.Metadata?.PrivateBrandToken;
        set => GetOrCreateFunctionMetadata().PrivateBrandToken = value;
    }

    public JsValue BoundThisValue { get; set; } = JsValue.Undefined;
    public JsValue BoundNewTargetValue { get; set; } = JsValue.Undefined;
    internal DerivedSuperCallState? BoundDerivedSuperCallState { get; set; }
    internal JsValue[]? PrecomputedInstanceFieldKeys { get; set; }

    public bool IsStrict { get; }
    public int PrivateBrandId { get; }
    public int SuperBaseContextSlot { get; set; } = -1;
    public int DerivedThisContextSlot { get; set; } = -1;
    public int LexicalThisContextSlot { get; set; } = -1;
    public int LexicalThisContextDepth { get; set; } = -1;
    public bool UsesClassLexicalBinding { get; set; }
    public bool UsesMethodEnvironmentCapture { get; set; }

    public JsBytecodeFunction CloneForClosure(JsRealm realm)
    {
        var clone = new JsBytecodeFunction(realm, Script, Name, RequiresClosureBinding, IsStrict, PrivateBrandId,
            true, HasNewTarget, IsDerivedConstructor,
            Kind, IsArrow, IsMethod,
            FormalParameterCount, HasSimpleParameterList,
            IsClassConstructor,
            HasEagerGeneratorParameterBinding,
            Length)
        {
            ArgumentsMappedSlots = ArgumentsMappedSlots is null ? null : (int[])ArgumentsMappedSlots.Clone(),
            BoundThisValue = BoundThisValue,
            BoundNewTargetValue = BoundNewTargetValue,
            BoundDerivedSuperCallState = BoundDerivedSuperCallState,
            PrecomputedInstanceFieldKeys = PrecomputedInstanceFieldKeys is null
                ? null
                : (JsValue[])PrecomputedInstanceFieldKeys.Clone(),
            SuperBaseContextSlot = SuperBaseContextSlot,
            DerivedThisContextSlot = DerivedThisContextSlot,
            LexicalThisContextSlot = LexicalThisContextSlot,
            LexicalThisContextDepth = LexicalThisContextDepth,
            UsesClassLexicalBinding = UsesClassLexicalBinding,
            UsesMethodEnvironmentCapture = UsesMethodEnvironmentCapture,
            Prototype = Prototype
        };

        if (functionMetadataContext?.Metadata is { } metadata)
            clone.functionMetadataContext = CreateMetadataContext(metadata.Clone());

        if (Kind is JsBytecodeFunctionKind.Generator or JsBytecodeFunctionKind.AsyncGenerator &&
            TryGetMaterializedPrototypePropertyObject(out var templatePrototypeObject))
            if (clone.TryGetPropertyAtom(realm, IdPrototype, out var clonePrototypeValue, out _) &&
                clonePrototypeValue.TryGetObject(out var clonePrototypeObject))
                clonePrototypeObject.Prototype = templatePrototypeObject.Prototype;

        return clone;
    }

    internal bool TryResolvePrivateBrandToken(int brandId, out JsObject token)
    {
        if (functionMetadataContext?.Metadata?.PrivateBrandTokensByBrandId is { } mappings &&
            mappings.TryGetValue(brandId, out token!))
            return true;

        token = functionMetadataContext?.Metadata?.PrivateBrandToken!;
        return token is not null;
    }

    internal JsObject ResolvePrivateBrandSourceToken()
    {
        return functionMetadataContext?.Metadata?.PrivateBrandToken ?? this;
    }

    internal JsObject ResolvePrivateBrandMappingSource(int brandId)
    {
        return TryResolvePrivateBrandToken(brandId, out var token) ? token : this;
    }

    internal void SetPrivateBrandToken(JsObject token)
    {
        GetOrCreateFunctionMetadata().PrivateBrandToken = token;
    }

    internal void SetPrivateBrandMapping(int brandId, JsObject token)
    {
        var metadata = GetOrCreateFunctionMetadata();
        metadata.PrivateBrandTokensByBrandId ??= new();
        metadata.PrivateBrandTokensByBrandId[brandId] = token;
    }

    internal void StorePrivateMethodValue(int index, in JsValue value)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        var metadata = GetOrCreateFunctionMetadata();
        var values = metadata.PrecomputedPrivateMethodValues;
        if (values is null || values.Length <= index)
        {
            var grown = new JsValue[index + 1];
            if (values is not null)
                values.CopyTo(grown, 0);
            metadata.PrecomputedPrivateMethodValues = values = grown;
        }

        values[index] = value;
    }

    internal bool TryLoadPrivateMethodValue(int index, out JsValue value)
    {
        value = JsValue.Undefined;
        if (index < 0)
            return false;

        var values = functionMetadataContext?.Metadata?.PrecomputedPrivateMethodValues;
        if (values is null || index >= values.Length)
            return false;

        value = values[index];
        return true;
    }

    private JsContext.FunctionMetadata GetOrCreateFunctionMetadata()
    {
        if (functionMetadataContext is null)
        {
            functionMetadataContext = CreateMetadataContext(new());
            return functionMetadataContext.Metadata!;
        }

        return functionMetadataContext.Metadata ??= new();
    }

    private static JsContext CreateMetadataContext(JsContext.FunctionMetadata metadata)
    {
        return new(null, 0) { Metadata = metadata };
    }

    protected override JsObject GetPrototypePropertyObjectPrototype(JsRealm realm)
    {
        if (Kind == JsBytecodeFunctionKind.AsyncGenerator)
            return realm.AsyncGeneratorObjectPrototype;
        if (Kind == JsBytecodeFunctionKind.Generator)
            return realm.GeneratorObjectPrototypeForFunctions;
        return base.GetPrototypePropertyObjectPrototype(realm);
    }

    internal override bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value, out SlotInfo slotInfo)
    {
        if (IsRestrictedClassConstructorProperty(realm, atom))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Cannot access restricted function property");

        return base.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out slotInfo);
    }

    internal override bool SetPropertyAtomWithReceiver(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        if (IsRestrictedClassConstructorProperty(realm, atom))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Cannot access restricted function property");

        return base.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out slotInfo);
    }

    private bool IsRestrictedClassConstructorProperty(JsRealm realm, int atom)
    {
        if (!IsClassConstructor)
            return false;

        var name = realm.Atoms.AtomToString(atom);
        return (name == "caller" || name == "arguments") && !HasOwnPropertyAtom(realm, atom);
    }

    internal sealed class DerivedSuperCallState
    {
        internal readonly JsBytecodeFunction ConstructorFunction;
        internal readonly JsContext? DerivedThisContext;
        internal readonly int DerivedThisSlot;
        internal readonly int FramePointer;
        internal readonly JsValue NewTarget;

        internal DerivedSuperCallState(
            int framePointer,
            JsBytecodeFunction constructorFunction,
            JsValue newTarget,
            JsContext? derivedThisContext,
            int derivedThisSlot)
        {
            FramePointer = framePointer;
            ConstructorFunction = constructorFunction;
            NewTarget = newTarget;
            DerivedThisContext = derivedThisContext;
            DerivedThisSlot = derivedThisSlot;
        }
    }
}
