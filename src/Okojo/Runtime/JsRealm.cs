namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    private const int MaxInstructions = 100_000_000;
    internal const int FunctionPrototypeConstructorSlot = 0;
    internal const int RegExpOwnSourceSlot = 0;
    internal const int RegExpOwnFlagsSlot = 1;
    internal const int RegExpOwnGlobalSlot = 2;
    internal const int RegExpOwnIgnoreCaseSlot = 3;
    internal const int RegExpOwnMultilineSlot = 4;
    internal const int RegExpOwnLastIndexSlot = 5;
    internal const int RegExpOwnStickySlot = 6;
    internal const int RegExpOwnUnicodeSlot = 7;
    internal const int RegExpOwnDotAllSlot = 8;
    internal const int IteratorResultValueSlot = 0;
    internal const int IteratorResultDoneSlot = 1;
    internal const int IntlPartTypeSlot = 0;
    internal const int IntlPartValueSlot = 1;
    internal const int IntlRangePartTypeSlot = 0;
    internal const int IntlRangePartValueSlot = 1;
    internal const int IntlRangePartSourceSlot = 2;
    private readonly JsPlainObject bootstrapObjectPrototype;
    public readonly Intrinsics Intrinsics;

    internal JsRealm(JsAgent agent, int id, JsRealmOptions? options = null)
    {
        Id = id;
        Engine = agent.Engine;
        Agent = agent;
        EmptyShape = new(this, new());
        FunctionPrototypeObjectShape = new(this,
            new()
            {
                [IdConstructor] = new(FunctionPrototypeConstructorSlot,
                    JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable)
            },
            1);
        FunctionPrototypeObjectShapeNoConstructor = new(this,
            new());
        var atomSource = IdSource;
        var atomFlags = IdFlags;
        var atomGlobal = IdGlobal;
        var atomIgnoreCase = IdIgnoreCase;
        var atomMultiline = IdMultiline;
        var atomLastIndex = IdLastIndex;
        var atomSticky = IdSticky;
        var atomUnicode = IdUnicode;
        var atomDotAll = IdDotAll;
        RegExpOwnShape = new(this,
            new()
            {
                [atomSource] = new(RegExpOwnSourceSlot, JsShapePropertyFlags.Configurable),
                [atomFlags] = new(RegExpOwnFlagsSlot, JsShapePropertyFlags.Configurable),
                [atomGlobal] = new(RegExpOwnGlobalSlot, JsShapePropertyFlags.Configurable),
                [atomIgnoreCase] = new(RegExpOwnIgnoreCaseSlot, JsShapePropertyFlags.Configurable),
                [atomMultiline] = new(RegExpOwnMultilineSlot, JsShapePropertyFlags.Configurable),
                [atomLastIndex] = new(RegExpOwnLastIndexSlot, JsShapePropertyFlags.Writable),
                [atomSticky] = new(RegExpOwnStickySlot, JsShapePropertyFlags.Configurable),
                [atomUnicode] = new(RegExpOwnUnicodeSlot, JsShapePropertyFlags.Configurable),
                [atomDotAll] = new(RegExpOwnDotAllSlot, JsShapePropertyFlags.Configurable)
            },
            9);
        IteratorResultObjectShape = new(this,
            new()
            {
                [IdValue] = new(IteratorResultValueSlot, JsShapePropertyFlags.Open),
                [IdDone] = new(IteratorResultDoneSlot, JsShapePropertyFlags.Open)
            },
            2);
        IntlPartObjectShape = new(this,
            new()
            {
                [IdType] = new(IntlPartTypeSlot, JsShapePropertyFlags.Open),
                [IdValue] = new(IntlPartValueSlot, JsShapePropertyFlags.Open)
            },
            2);
        IntlRangePartObjectShape = new(this,
            new()
            {
                [IdType] = new(IntlRangePartTypeSlot, JsShapePropertyFlags.Open),
                [IdValue] = new(IntlRangePartValueSlot, JsShapePropertyFlags.Open),
                [IdSource] = new(IntlRangePartSourceSlot, JsShapePropertyFlags.Open)
            },
            3);

        ErrorObjectShape = new(this,
            new()
            {
                [IdName] = new(0, JsShapePropertyFlags.Open),
                [IdMessage] = new(1, JsShapePropertyFlags.Open)
            }
            , 2);
        bootstrapObjectPrototype = new(this, false);
        Intrinsics = new(this, bootstrapObjectPrototype);
        GlobalObject = new(this);
        Global = new(this);
        GlobalObject.DefineDataPropertyAtom(this, IdGlobalThis, JsValue.FromObject(GlobalObject),
            JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable);

        SymbolAsyncIteratorSymbol =
            Atoms.TryGetSymbolByAtom(IdSymbolAsyncIterator, out var symbolAsyncIterator)
                ? symbolAsyncIterator
                : new(IdSymbolAsyncIterator, "Symbol.asyncIterator", true);
        SymbolIteratorSymbol = Atoms.TryGetSymbolByAtom(IdSymbolIterator, out var symbolIterator)
            ? symbolIterator
            : new(IdSymbolIterator, "Symbol.iterator", true);
        SymbolHasInstanceSymbol = Atoms.TryGetSymbolByAtom(IdSymbolHasInstance, out var symbolHasInstance)
            ? symbolHasInstance
            : new(IdSymbolHasInstance, "Symbol.hasInstance", true);
        SymbolToStringTagSymbol = Atoms.TryGetSymbolByAtom(IdSymbolToStringTag, out var symbolToStringTag)
            ? symbolToStringTag
            : new(IdSymbolToStringTag, "Symbol.toStringTag", true);
        SymbolToPrimitiveSymbol = Atoms.TryGetSymbolByAtom(IdSymbolToPrimitive, out var symbolToPrimitive)
            ? symbolToPrimitive
            : new(IdSymbolToPrimitive, "Symbol.toPrimitive", true);
        SymbolSpeciesSymbol = Atoms.TryGetSymbolByAtom(IdSymbolSpecies, out var symbolSpecies)
            ? symbolSpecies
            : new(IdSymbolSpecies, "Symbol.species", true);
        SymbolIsConcatSpreadableSymbol =
            Atoms.TryGetSymbolByAtom(IdSymbolIsConcatSpreadable, out var symbolIsConcatSpreadable)
                ? symbolIsConcatSpreadable
                : new(IdSymbolIsConcatSpreadable, "Symbol.isConcatSpreadable", true);
        SymbolMatchSymbol = Atoms.TryGetSymbolByAtom(IdSymbolMatch, out var symbolMatch)
            ? symbolMatch
            : new(IdSymbolMatch, "Symbol.match", true);
        SymbolUnscopablesSymbol = Atoms.TryGetSymbolByAtom(IdSymbolUnscopables, out var symbolUnscopables)
            ? symbolUnscopables
            : new(IdSymbolUnscopables, "Symbol.unscopables", true);
        SymbolDisposeSymbol = Atoms.TryGetSymbolByAtom(IdSymbolDispose, out var symbolDispose)
            ? symbolDispose
            : new(IdSymbolDispose, "Symbol.dispose", true);
        SymbolAsyncDisposeSymbol = Atoms.TryGetSymbolByAtom(IdSymbolAsyncDispose, out var symbolAsyncDispose)
            ? symbolAsyncDispose
            : new(IdSymbolAsyncDispose, "Symbol.asyncDispose", true);
        SymbolReplaceSymbol = Atoms.TryGetSymbolByAtom(IdSymbolReplace, out var symbolReplace)
            ? symbolReplace
            : new(IdSymbolReplace, "Symbol.replace", true);
        SymbolMatchAllSymbol = Atoms.TryGetSymbolByAtom(IdSymbolMatchAll, out var symbolMatchAll)
            ? symbolMatchAll
            : new(IdSymbolMatchAll, "Symbol.matchAll", true);
        SymbolSplitSymbol = Atoms.TryGetSymbolByAtom(IdSymbolSplit, out var symbolSplit)
            ? symbolSplit
            : new(IdSymbolSplit, "Symbol.split", true);
        SymbolSearchSymbol = Atoms.TryGetSymbolByAtom(IdSymbolSearch, out var symbolSearch)
            ? symbolSearch
            : new(IdSymbolSearch, "Symbol.search", true);

        Intrinsics.InstallIntrinsics();
        EnsureWorkerMessageDispatchHook();
        Stack.AsSpan().Fill(JsValue.Undefined);

        if (options is not null)
        {
            HostDefined = options.HostDefined;
            options.Initialize?.Invoke(this);
        }
    }

    public int Id { get; }
    public JsRuntime Engine { get; }
    public JsAgent Agent { get; }

    public object? HostDefined { get; set; }
    internal JsHostFunction? BootstrapFunctionPrototype { get; set; }

    public GlobalBindingsView Global { get; }
    public JsGlobalObject GlobalObject { get; }
    internal StaticNamedPropertyLayout EmptyShape { get; }
    internal StaticNamedPropertyLayout FunctionPrototypeObjectShape { get; }
    internal StaticNamedPropertyLayout FunctionPrototypeObjectShapeNoConstructor { get; }
    internal StaticNamedPropertyLayout RegExpOwnShape { get; }
    internal StaticNamedPropertyLayout IteratorResultObjectShape { get; }
    internal StaticNamedPropertyLayout IntlPartObjectShape { get; }
    internal StaticNamedPropertyLayout IntlRangePartObjectShape { get; }
    internal StaticNamedPropertyLayout ErrorObjectShape { get; }
    internal JsPlainObject ObjectPrototype => Intrinsics?.ObjectPrototype ?? bootstrapObjectPrototype;
    internal JsHostFunction FunctionPrototype => Intrinsics?.FunctionPrototype ?? BootstrapFunctionPrototype!;
    internal JsPlainObject GeneratorFunctionPrototype => Intrinsics.GeneratorFunctionPrototype;
    internal JsPlainObject AsyncFunctionPrototype => Intrinsics.AsyncFunctionPrototype;
    internal JsPlainObject AsyncGeneratorFunctionPrototype => Intrinsics.AsyncGeneratorFunctionPrototype;
    internal JsArray ArrayPrototype => Intrinsics.ArrayPrototype;
    internal JsHostFunction ArrayPrototypeValuesFunction => Intrinsics.ArrayPrototypeValuesFunction;
    internal JsNumberObject NumberPrototype => Intrinsics.NumberPrototype;
    internal JsBooleanObject BooleanPrototype => Intrinsics.BooleanPrototype;
    internal JsStringObject StringPrototype => Intrinsics.StringPrototype;
    internal JsPlainObject BigIntPrototype => Intrinsics.BigIntPrototype;
    internal JsPlainObject SymbolPrototype => Intrinsics.SymbolPrototype;
    internal JsPlainObject ArrayBufferPrototype => Intrinsics.ArrayBufferPrototype;
    internal JsPlainObject SharedArrayBufferPrototype => Intrinsics.SharedArrayBufferPrototype;
    internal JsPlainObject ArrayIteratorPrototype => Intrinsics.ArrayIteratorPrototype;
    internal JsPlainObject StringIteratorPrototype => Intrinsics.StringIteratorPrototype;
    internal JsPlainObject RegExpStringIteratorPrototype => Intrinsics.RegExpStringIteratorPrototype;
    internal JsPlainObject TypedArrayPrototype => Intrinsics.TypedArrayPrototype;
    internal JsPlainObject[] TypedArrayPrototypes => Intrinsics.TypedArrayPrototypes;
    internal JsPlainObject Uint8ArrayPrototype => TypedArrayPrototypes[(int)TypedArrayElementKind.Uint8];
    internal JsPlainObject DataViewPrototype => Intrinsics.DataViewPrototype;
    internal JsPlainObject TypedArrayIteratorPrototype => Intrinsics.TypedArrayIteratorPrototype;
    internal JsPlainObject IteratorPrototype => Intrinsics.IteratorPrototype;
    internal JsPlainObject IteratorWrapPrototype => Intrinsics.IteratorWrapPrototype;
    internal JsPlainObject AsyncIteratorPrototype => Intrinsics.AsyncIteratorPrototype;
    internal JsHostFunction IteratorSelfFunction => Intrinsics.IteratorSelfFunction;
    internal JsHostFunction AsyncIteratorSelfFunction => Intrinsics.AsyncIteratorSelfFunction;
    internal JsPlainObject GeneratorObjectPrototypeForFunctions => Intrinsics.GeneratorObjectPrototypeForFunctions;
    internal JsPlainObject MapPrototype => Intrinsics.MapPrototype;
    internal JsPlainObject MapIteratorPrototype => Intrinsics.MapIteratorPrototype;
    internal JsPlainObject SetPrototype => Intrinsics.SetPrototype;
    internal JsPlainObject SetIteratorPrototype => Intrinsics.SetIteratorPrototype;
    internal JsPlainObject WeakMapPrototype => Intrinsics.WeakMapPrototype;
    internal JsPlainObject WeakSetPrototype => Intrinsics.WeakSetPrototype;
    internal JsPlainObject WeakRefPrototype => Intrinsics.WeakRefPrototype;
    internal JsPlainObject FinalizationRegistryPrototype => Intrinsics.FinalizationRegistryPrototype;
    internal JsPlainObject AsyncGeneratorObjectPrototype => Intrinsics.AsyncGeneratorObjectPrototype;
    internal JsHostFunction ObjectConstructor => Intrinsics.ObjectConstructor;
    internal JsHostFunction ArrayConstructor => Intrinsics.ArrayConstructor;
    internal JsHostFunction FunctionConstructor => Intrinsics.FunctionConstructor;
    internal JsHostFunction GeneratorFunctionConstructor => Intrinsics.GeneratorFunctionConstructor;
    internal JsHostFunction AsyncFunctionConstructor => Intrinsics.AsyncFunctionConstructor;
    internal JsHostFunction AsyncGeneratorFunctionConstructor => Intrinsics.AsyncGeneratorFunctionConstructor;
    internal JsHostFunction ErrorConstructor => Intrinsics.ErrorConstructor;
    internal JsHostFunction TypeErrorConstructor => Intrinsics.TypeErrorConstructor;
    internal JsHostFunction ReferenceErrorConstructor => Intrinsics.ReferenceErrorConstructor;
    internal JsHostFunction RangeErrorConstructor => Intrinsics.RangeErrorConstructor;
    internal JsHostFunction SyntaxErrorConstructor => Intrinsics.SyntaxErrorConstructor;
    internal JsHostFunction EvalErrorConstructor => Intrinsics.EvalErrorConstructor;
    internal JsHostFunction UriErrorConstructor => Intrinsics.UriErrorConstructor;
    internal JsHostFunction AggregateErrorConstructor => Intrinsics.AggregateErrorConstructor;
    internal JsHostFunction NumberConstructor => Intrinsics.NumberConstructor;
    internal JsHostFunction BooleanConstructor => Intrinsics.BooleanConstructor;
    internal JsHostFunction StringConstructor => Intrinsics.StringConstructor;
    internal JsHostFunction BigIntConstructor => Intrinsics.BigIntConstructor;
    internal JsHostFunction ArrayBufferConstructor => Intrinsics.ArrayBufferConstructor;
    internal JsHostFunction SharedArrayBufferConstructor => Intrinsics.SharedArrayBufferConstructor;
    internal JsHostFunction IteratorConstructor => Intrinsics.IteratorConstructor;
    internal JsHostFunction TypedArrayConstructor => Intrinsics.TypedArrayConstructor;
    internal JsHostFunction[] TypedArrayConstructors => Intrinsics.TypedArrayConstructors;
    internal JsHostFunction Uint8ArrayConstructor => Intrinsics.Uint8ArrayConstructor;
    internal JsHostFunction DataViewConstructor => Intrinsics.DataViewConstructor;
    internal JsHostFunction MapConstructor => Intrinsics.MapConstructor;
    internal JsHostFunction SetConstructor => Intrinsics.SetConstructor;
    internal JsHostFunction WeakMapConstructor => Intrinsics.WeakMapConstructor;
    internal JsHostFunction WeakSetConstructor => Intrinsics.WeakSetConstructor;
    internal JsHostFunction WeakRefConstructor => Intrinsics.WeakRefConstructor;
    internal JsHostFunction FinalizationRegistryConstructor => Intrinsics.FinalizationRegistryConstructor;
    internal JsHostFunction RegExpConstructor => Intrinsics.RegExpConstructor;
    internal JsHostFunction DateConstructor => Intrinsics.DateConstructor;
    internal JsHostFunction PromiseConstructor => Intrinsics.PromiseConstructor;
    internal JsHostFunction SymbolConstructor => Intrinsics.SymbolConstructor;
    internal JsHostFunction ProxyConstructor => Intrinsics.ProxyConstructor;
    internal JsPlainObject AtomicsObject => Intrinsics.AtomicsObject;
    internal JsFunction ObjectPrototypeToStringIntrinsic => Intrinsics.ObjectPrototypeToStringIntrinsic;
    internal JsHostFunction ThrowTypeErrorIntrinsic => Intrinsics.ThrowTypeErrorIntrinsic;
    internal JsPlainObject ErrorPrototype => Intrinsics.ErrorPrototype;
    internal JsPlainObject TypeErrorPrototype => Intrinsics.TypeErrorPrototype;
    internal JsPlainObject ReferenceErrorPrototype => Intrinsics.ReferenceErrorPrototype;
    internal JsPlainObject RangeErrorPrototype => Intrinsics.RangeErrorPrototype;
    internal JsPlainObject SyntaxErrorPrototype => Intrinsics.SyntaxErrorPrototype;
    internal JsPlainObject EvalErrorPrototype => Intrinsics.EvalErrorPrototype;
    internal JsPlainObject UriErrorPrototype => Intrinsics.UriErrorPrototype;
    internal JsPlainObject AggregateErrorPrototype => Intrinsics.AggregateErrorPrototype;
    internal JsPlainObject PromisePrototype => Intrinsics.PromisePrototype;
    internal JsPlainObject RegExpPrototype => Intrinsics.RegExpPrototype;
    internal JsPlainObject DatePrototype => Intrinsics.DatePrototype;
    internal Symbol SymbolIteratorSymbol { get; }
    internal Symbol SymbolHasInstanceSymbol { get; }
    internal Symbol SymbolToStringTagSymbol { get; }
    internal Symbol SymbolToPrimitiveSymbol { get; }
    internal Symbol SymbolSpeciesSymbol { get; }
    internal Symbol SymbolAsyncIteratorSymbol { get; }
    internal Symbol SymbolIsConcatSpreadableSymbol { get; }
    internal Symbol SymbolMatchSymbol { get; }
    internal Symbol SymbolUnscopablesSymbol { get; }
    internal Symbol SymbolDisposeSymbol { get; }
    internal Symbol SymbolAsyncDisposeSymbol { get; }
    internal Symbol SymbolReplaceSymbol { get; }
    internal Symbol SymbolMatchAllSymbol { get; }
    internal Symbol SymbolSplitSymbol { get; }
    internal Symbol SymbolSearchSymbol { get; }
    public AtomTable Atoms => Agent.Atoms;

    public void RequestStepInto(CheckpointSourceLocation? startLocation = null)
    {
        Agent.RequestStepInto(GetExecutionContextDepth(), startLocation);
    }

    public void RequestStepOver(CheckpointSourceLocation? startLocation = null)
    {
        Agent.RequestStepOver(GetExecutionContextDepth(), startLocation);
    }

    public void RequestStepOut(CheckpointSourceLocation? startLocation = null)
    {
        Agent.RequestStepOut(GetExecutionContextDepth(), startLocation);
    }

    public void ClearStepRequest()
    {
        Agent.ClearStepRequest();
    }

    public sealed class GlobalBindingsView
    {
        private readonly JsRealm realm;

        internal GlobalBindingsView(JsRealm realm)
        {
            this.realm = realm;
        }

        public JsValue this[string name]
        {
            get
            {
                if (!realm.TryGetGlobalBinding(name, out var value))
                    throw new KeyNotFoundException($"Global binding '{name}' not found.");
                return value;
            }
            set => realm.SetGlobalBinding(name, value);
        }

        public bool TryGetValue(string name, out JsValue value)
        {
            return realm.TryGetGlobalBinding(name, out value);
        }
    }
}
