using Okojo.JavaScript.Bytecode;

namespace Okojo.JavaScript.Objects;

public sealed class JsBytecodeFunction : JsFunction
{
    private JsContext? functionMetadataContext;
    private readonly JsFunctionDescriptor functionTemplate;
    private FunctionSourceTextSegment sourceTextOverride;
    private string? sourceTextOverrideString;

    public JsBytecodeFunction(JsScript script)
        : base(
            script.Realm,
            script.Function.Name,
            true,
            script.Function.ExpectedArgumentCount,
            script.Function.HasPrototypeProperty,
            script.Function.PrototypeHasConstructor,
            script.Function.IsConstructor
        )
    {
        Script = script;
        functionTemplate = script.Function;
        SuperBaseContextSlot = functionTemplate.SuperBaseContextSlot;
        DerivedThisContextSlot = functionTemplate.DerivedThisContextSlot;
        LexicalThisContextSlot = functionTemplate.LexicalThisContextSlot;
        LexicalThisContextDepth = functionTemplate.LexicalThisContextDepth;
        var realm = script.Realm;
        Prototype = realm.Intrinsics.GetFunctionPrototypeForKind(Kind);

        if (
            IsClassConstructor
            && TryGetOwnNamedPropertyDescriptorAtom(realm, IdPrototype, out var descriptor)
            && !descriptor.IsAccessor
        )
            _ = DefineOwnDataPropertyExact(
                realm,
                IdPrototype,
                descriptor.Value,
                JsShapePropertyFlags.None
            );
    }

    public JsScript Script { get; }
    public JsFunctionDescriptor Descriptor => functionTemplate;
    internal bool HasFunctionSourceText =>
        !sourceTextOverride.IsEmpty || Script.HasFunctionSourceText;

    internal string? GetFunctionSourceTextString() =>
        !sourceTextOverride.IsEmpty
            ? sourceTextOverrideString ??= sourceTextOverride.ToString()
            : Script.GetFunctionSourceTextString();

    internal void SetFunctionSourceText(FunctionSourceTextSegment sourceText)
    {
        sourceTextOverride = sourceText;
        sourceTextOverrideString = null;
    }

    public JsBytecodeFunctionKind Kind => functionTemplate.Kind;
    public bool RequiresClosureBinding => functionTemplate.RequiresClosureBinding;
    public bool HasNewTarget => functionTemplate.HasNewTarget;
    public bool IsDerivedConstructor => functionTemplate.IsDerivedConstructor;
    public bool IsArrow => functionTemplate.IsArrow;
    public bool IsMethod => functionTemplate.IsMethod;
    public bool UsesResumeModeDispatch => Kind != JsBytecodeFunctionKind.Normal;
    public int FormalParameterCount => functionTemplate.FormalParameterCount;
    public bool HasSimpleParameterList => functionTemplate.HasSimpleParameterList;
    internal int[]? ArgumentsMappedSlots => functionTemplate.ArgumentsMappedSlots;
    public bool HasEagerGeneratorParameterBinding =>
        functionTemplate.HasEagerGeneratorParameterBinding;
    public bool IsClassConstructor => functionTemplate.IsClassConstructor;
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

    public bool IsStrict => functionTemplate.IsStrict;
    public int PrivateBrandId => functionTemplate.PrivateBrandId;
    public int SuperBaseContextSlot { get; set; } = -1;
    public int DerivedThisContextSlot { get; set; } = -1;
    public int LexicalThisContextSlot { get; set; } = -1;
    public int LexicalThisContextDepth { get; set; } = -1;
    public bool UsesClassLexicalBinding { get; set; }
    public bool UsesMethodEnvironmentCapture { get; set; }

    internal bool TryResolvePrivateBrandToken(int brandId, out JsObject token)
    {
        if (
            functionMetadataContext?.Metadata?.PrivateBrandTokensByBrandId is { } mappings
            && mappings.TryGetValue(brandId, out token!)
        )
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

    internal override bool TryGetPropertyAtomWithReceiverValue(
        JsRealm realm,
        in JsValue receiverValue,
        int atom,
        out JsValue value,
        out SlotInfo slotInfo
    )
    {
        if (IsRestrictedClassConstructorProperty(realm, atom))
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Cannot access restricted function property"
            );

        return base.TryGetPropertyAtomWithReceiverValue(
            realm,
            receiverValue,
            atom,
            out value,
            out slotInfo
        );
    }

    internal override bool SetPropertyAtomWithReceiver(
        JsRealm realm,
        JsObject receiver,
        int atom,
        JsValue value,
        out SlotInfo slotInfo
    )
    {
        if (IsRestrictedClassConstructorProperty(realm, atom))
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Cannot access restricted function property"
            );

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
            int derivedThisSlot
        )
        {
            FramePointer = framePointer;
            ConstructorFunction = constructorFunction;
            NewTarget = newTarget;
            DerivedThisContext = derivedThisContext;
            DerivedThisSlot = derivedThisSlot;
        }
    }
}
