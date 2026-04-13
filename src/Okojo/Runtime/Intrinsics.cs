namespace Okojo.Runtime;

public partial class Intrinsics
{
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

    public readonly JsRealm Realm;


    public Intrinsics(JsRealm realm, JsPlainObject objectPrototype)
    {
        Realm = realm;
        ObjectPrototype = objectPrototype;
        FunctionPrototype = new(Realm, (in info) => { return JsValue.Undefined; },
            "", 0,
            false, false)
        {
            Prototype = ObjectPrototype
        };
        realm.BootstrapFunctionPrototype = FunctionPrototype;
        GeneratorFunctionPrototype = new(Realm, false)
            { Prototype = FunctionPrototype };
        AsyncFunctionPrototype = new(Realm, false)
            { Prototype = FunctionPrototype };
        AsyncGeneratorFunctionPrototype = new(Realm, false)
            { Prototype = FunctionPrototype };
        ArrayPrototype = new(Realm);
        NumberPrototype = new(Realm, 0, ObjectPrototype);
        BooleanPrototype = new(Realm, false, ObjectPrototype);
        StringPrototype = new(Realm, string.Empty, ObjectPrototype);
        BigIntPrototype = new(Realm, false) { Prototype = ObjectPrototype };
        SymbolPrototype = new(Realm, false) { Prototype = ObjectPrototype };
        ArrayBufferPrototype = new(Realm, false)
            { Prototype = ObjectPrototype };
        SharedArrayBufferPrototype = new(Realm, false)
            { Prototype = ObjectPrototype };
        TypedArrayPrototype = new(Realm, false) { Prototype = ObjectPrototype };
        TypedArrayPrototypes = new JsPlainObject[12];
        for (var i = 0; i < TypedArrayPrototypes.Length; i++)
            TypedArrayPrototypes[i] = new(Realm, false)
                { Prototype = TypedArrayPrototype };
        DataViewPrototype = new(Realm, false) { Prototype = ObjectPrototype };
        IteratorPrototype = new(Realm, false) { Prototype = ObjectPrototype };
        StringIteratorPrototype = new(Realm, false)
            { Prototype = IteratorPrototype };
        RegExpStringIteratorPrototype = new(Realm, false)
            { Prototype = IteratorPrototype };
        ArrayIteratorPrototype = new(Realm, false)
            { Prototype = IteratorPrototype };
        TypedArrayIteratorPrototype = new(Realm, false)
            { Prototype = IteratorPrototype };
        IteratorWrapPrototype = new(Realm, false)
            { Prototype = IteratorPrototype };
        AsyncIteratorPrototype = new(Realm, false)
            { Prototype = ObjectPrototype };
        MapPrototype = new(Realm, false) { Prototype = ObjectPrototype };
        MapIteratorPrototype = new(Realm, false)
            { Prototype = IteratorPrototype };
        SetPrototype = new(Realm, false) { Prototype = ObjectPrototype };
        SetIteratorPrototype = new(Realm, false)
            { Prototype = IteratorPrototype };
        WeakMapPrototype = new(Realm, false) { Prototype = ObjectPrototype };
        WeakSetPrototype = new(Realm, false) { Prototype = ObjectPrototype };
        WeakRefPrototype = new(Realm, false) { Prototype = ObjectPrototype };
        FinalizationRegistryPrototype = new(Realm, false)
            { Prototype = ObjectPrototype };
        AsyncGeneratorObjectPrototype = new(Realm, false)
            { Prototype = AsyncIteratorPrototype };

        ObjectConstructor = CreateObjectConstructor();
        ArrayConstructor = CreateArrayConstructor();
        FunctionConstructor = CreateFunctionConstructor();
        GeneratorFunctionConstructor = CreateGeneratorFunctionConstructor();
        AsyncFunctionConstructor = CreateAsyncFunctionConstructor();
        AsyncGeneratorFunctionConstructor = CreateAsyncGeneratorFunctionConstructor();
        GeneratorFunctionConstructor.Prototype = FunctionConstructor;
        AsyncFunctionConstructor.Prototype = FunctionConstructor;
        AsyncGeneratorFunctionConstructor.Prototype = FunctionConstructor;
        ErrorPrototype = new(realm, false) { Prototype = ObjectPrototype };
        TypeErrorPrototype = new(realm, false) { Prototype = ErrorPrototype };
        ReferenceErrorPrototype = new(realm, false)
            { Prototype = ErrorPrototype };
        RangeErrorPrototype = new(realm, false) { Prototype = ErrorPrototype };
        SyntaxErrorPrototype = new(realm, false) { Prototype = ErrorPrototype };
        EvalErrorPrototype = new(realm, false) { Prototype = ErrorPrototype };
        UriErrorPrototype = new(realm, false) { Prototype = ErrorPrototype };
        AggregateErrorPrototype = new(realm, false)
            { Prototype = ErrorPrototype };
        ErrorConstructor = CreateErrorConstructor();
        TypeErrorConstructor = CreateNativeErrorConstructor("TypeError", TypeErrorPrototype);
        ReferenceErrorConstructor = CreateNativeErrorConstructor("ReferenceError", ReferenceErrorPrototype);
        RangeErrorConstructor = CreateNativeErrorConstructor("RangeError", RangeErrorPrototype);
        SyntaxErrorConstructor = CreateNativeErrorConstructor("SyntaxError", SyntaxErrorPrototype);
        EvalErrorConstructor = CreateNativeErrorConstructor("EvalError", EvalErrorPrototype);
        UriErrorConstructor = CreateNativeErrorConstructor("URIError", UriErrorPrototype);
        AggregateErrorConstructor = CreateAggregateErrorConstructor();
        AggregateErrorConstructor.Prototype = ErrorConstructor;
        PromisePrototype = new(Realm, false) { Prototype = ObjectPrototype };
        PromiseConstructor = CreatePromiseConstructor();
        NumberConstructor = CreateNumberConstructor();
        BooleanConstructor = CreateBooleanConstructor();
        StringConstructor = CreateStringConstructor();
        BigIntConstructor = CreateBigIntConstructor();
        ArrayBufferConstructor = CreateArrayBufferConstructor();
        SharedArrayBufferConstructor = CreateSharedArrayBufferConstructor();
        IteratorConstructor = CreateIteratorConstructor();
        TypedArrayConstructor = new(realm,
            static (in info) =>
            {
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Abstract class TypedArray not directly constructable");
            }, "TypedArray", 0, true);
        TypedArrayConstructors = new JsHostFunction[12];
        for (var i = 0; i < TypedArrayConstructors.Length; i++)
        {
            var ctor = CreateTypedArrayConstructor((TypedArrayElementKind)i);
            ctor.Prototype = TypedArrayConstructor;
            TypedArrayConstructors[i] = ctor;
        }

        DataViewConstructor = CreateDataViewConstructor();
        MapConstructor = CreateMapConstructor();
        SetConstructor = CreateSetConstructor();
        WeakMapConstructor = CreateWeakMapConstructor();
        WeakSetConstructor = CreateWeakSetConstructor();
        WeakRefConstructor = CreateWeakRefConstructor();
        FinalizationRegistryConstructor = CreateFinalizationRegistryConstructor();
        RegExpPrototype = new(Realm, false) { Prototype = ObjectPrototype };
        RegExpConstructor = CreateRegExpConstructor();
        DatePrototype = new(Realm, false) { Prototype = ObjectPrototype };
        DateConstructor = CreateDateConstructor();


        SymbolConstructor = CreateSymbolConstructor();
        ProxyConstructor = CreateProxyConstructor();
        AtomicsObject = CreateAtomicsObject();
        ThrowTypeErrorIntrinsic = new(Realm,
            static (in info) =>
            {
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "%ThrowTypeError% always throws", errorRealm: info.Function.Realm);
            }, string.Empty, 0);
        _ = ThrowTypeErrorIntrinsic.DefineOwnDataPropertyExact(Realm, IdLength, JsValue.FromInt32(0),
            JsShapePropertyFlags.None);
        _ = ThrowTypeErrorIntrinsic.DefineOwnDataPropertyExact(Realm, IdName, JsValue.FromString(string.Empty),
            JsShapePropertyFlags.None);
        ThrowTypeErrorIntrinsic.PreventExtensions();
    }

    internal AtomTable Atoms => Realm.Atoms;

    internal JsPlainObject ObjectPrototype { get; }
    internal JsHostFunction FunctionPrototype { get; }
    internal JsPlainObject GeneratorFunctionPrototype { get; }
    internal JsPlainObject AsyncFunctionPrototype { get; }
    internal JsPlainObject AsyncGeneratorFunctionPrototype { get; }
    internal JsArray ArrayPrototype { get; }
    internal JsHostFunction ArrayPrototypeValuesFunction { get; set; } = null!;
    internal JsHostFunction StringFromCodePointFunction { get; set; } = null!;
    internal JsNumberObject NumberPrototype { get; }
    internal JsBooleanObject BooleanPrototype { get; }
    internal JsStringObject StringPrototype { get; }
    internal JsPlainObject BigIntPrototype { get; }
    internal JsPlainObject SymbolPrototype { get; }
    internal JsPlainObject ArrayBufferPrototype { get; }
    internal JsPlainObject SharedArrayBufferPrototype { get; }
    internal JsPlainObject ArrayIteratorPrototype { get; }
    internal JsPlainObject StringIteratorPrototype { get; }
    internal JsPlainObject RegExpStringIteratorPrototype { get; }
    internal JsPlainObject TypedArrayPrototype { get; }
    internal JsPlainObject[] TypedArrayPrototypes { get; }
    internal JsPlainObject Uint8ArrayPrototype => GetTypedArrayPrototype(TypedArrayElementKind.Uint8);
    internal JsPlainObject DataViewPrototype { get; }
    internal JsPlainObject TypedArrayIteratorPrototype { get; }
    internal JsPlainObject IteratorPrototype { get; }
    internal JsPlainObject IteratorWrapPrototype { get; }
    internal JsPlainObject AsyncIteratorPrototype { get; }
    internal JsHostFunction IteratorSelfFunction { get; set; } = null!;
    internal JsHostFunction AsyncIteratorSelfFunction { get; set; } = null!;
    internal JsPlainObject GeneratorObjectPrototypeForFunctions { get; private set; } = null!;

    internal JsPlainObject MapPrototype { get; }
    internal JsPlainObject MapIteratorPrototype { get; }
    internal JsPlainObject SetPrototype { get; }
    internal JsPlainObject SetIteratorPrototype { get; }
    internal JsPlainObject WeakMapPrototype { get; }
    internal JsPlainObject WeakSetPrototype { get; }
    internal JsPlainObject WeakRefPrototype { get; }
    internal JsPlainObject FinalizationRegistryPrototype { get; }
    internal JsPlainObject AsyncGeneratorObjectPrototype { get; }
    internal JsHostFunction ObjectConstructor { get; }
    internal JsHostFunction ArrayConstructor { get; }
    internal JsHostFunction FunctionConstructor { get; }
    internal JsHostFunction GeneratorFunctionConstructor { get; }
    internal JsHostFunction AsyncFunctionConstructor { get; }
    internal JsHostFunction AsyncGeneratorFunctionConstructor { get; }
    internal JsHostFunction ErrorConstructor { get; }
    internal JsHostFunction TypeErrorConstructor { get; }
    internal JsHostFunction ReferenceErrorConstructor { get; }
    internal JsHostFunction RangeErrorConstructor { get; }
    internal JsHostFunction SyntaxErrorConstructor { get; }
    internal JsHostFunction EvalErrorConstructor { get; }
    internal JsHostFunction UriErrorConstructor { get; }
    internal JsHostFunction AggregateErrorConstructor { get; }
    internal JsHostFunction NumberConstructor { get; }
    internal JsHostFunction BooleanConstructor { get; }
    internal JsHostFunction StringConstructor { get; }
    internal JsHostFunction BigIntConstructor { get; }
    internal JsHostFunction ArrayBufferConstructor { get; }
    internal JsHostFunction SharedArrayBufferConstructor { get; }
    internal JsHostFunction IteratorConstructor { get; }
    internal JsHostFunction TypedArrayConstructor { get; }
    internal JsHostFunction[] TypedArrayConstructors { get; }
    internal JsHostFunction Uint8ArrayConstructor => GetTypedArrayConstructor(TypedArrayElementKind.Uint8);
    internal JsHostFunction DataViewConstructor { get; }
    internal JsHostFunction MapConstructor { get; }
    internal JsHostFunction SetConstructor { get; }
    internal JsHostFunction WeakMapConstructor { get; }
    internal JsHostFunction WeakSetConstructor { get; }
    internal JsHostFunction WeakRefConstructor { get; }
    internal JsHostFunction FinalizationRegistryConstructor { get; }
    internal JsHostFunction RegExpConstructor { get; }
    internal JsHostFunction DateConstructor { get; }
    internal JsHostFunction PromiseConstructor { get; }
    internal JsHostFunction SymbolConstructor { get; }
    internal JsHostFunction ProxyConstructor { get; }
    internal JsPlainObject AtomicsObject { get; }
    internal JsFunction ObjectPrototypeToStringIntrinsic { get; set; } = null!;
    internal JsHostFunction ThrowTypeErrorIntrinsic { get; }
    internal JsPlainObject ErrorPrototype { get; }
    internal JsPlainObject TypeErrorPrototype { get; }
    internal JsPlainObject ReferenceErrorPrototype { get; }
    internal JsPlainObject RangeErrorPrototype { get; }
    internal JsPlainObject SyntaxErrorPrototype { get; }
    internal JsPlainObject EvalErrorPrototype { get; }
    internal JsPlainObject UriErrorPrototype { get; }
    internal JsPlainObject AggregateErrorPrototype { get; }
    internal JsPlainObject PromisePrototype { get; }
    internal JsPlainObject RegExpPrototype { get; }
    internal JsPlainObject DatePrototype { get; }

    internal JsPlainObject GetTypedArrayPrototype(TypedArrayElementKind kind)
    {
        return TypedArrayPrototypes[(int)kind];
    }

    internal JsHostFunction GetTypedArrayConstructor(TypedArrayElementKind kind)
    {
        return TypedArrayConstructors[(int)kind];
    }

    internal JsObject GetFunctionPrototypeForKind(JsBytecodeFunctionKind kind)
    {
        return kind switch
        {
            JsBytecodeFunctionKind.Generator => GeneratorFunctionPrototype,
            JsBytecodeFunctionKind.Async => AsyncFunctionPrototype,
            JsBytecodeFunctionKind.AsyncGenerator => AsyncGeneratorFunctionPrototype,
            _ => FunctionPrototype
        };
    }
}
