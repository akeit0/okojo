namespace Okojo.Runtime;

public partial class Intrinsics
{
    private JsHostFunction CreateArrayBufferConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Constructor ArrayBuffer requires 'new'");

            var requestedByteLength = args.Length == 0 || args[0].IsUndefined
                ? 0d
                : ToRequestedArrayBufferLength(realm, args[0]);
            double? requestedMaxByteLength = null;
            if (args.Length > 1 && !args[1].IsUndefined && !args[1].IsNull &&
                args[1].TryGetObject(out var options))
                if (options.TryGetPropertyByAtom(IdMaxByteLength, out var maxValue) && !maxValue.IsUndefined)
                    requestedMaxByteLength = ToRequestedArrayBufferLength(realm, maxValue);

            if (requestedMaxByteLength.HasValue && requestedByteLength > requestedMaxByteLength.Value)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array buffer max length");

            var prototype =
                GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee, realm.ArrayBufferPrototype);
            var byteLength = ToAllocatableArrayBufferLength(requestedByteLength);
            uint? maxByteLength = requestedMaxByteLength.HasValue
                ? ToAllocatableArrayBufferLength(requestedMaxByteLength.Value)
                : null;
            return new JsArrayBufferObject(realm, byteLength, maxByteLength, prototype);
        }, "ArrayBuffer", 1, true);
    }

    private void InstallArrayBufferConstructorBuiltins()
    {
        const int atomByteLength = IdByteLength;
        const int atomDetached = IdDetached;
        const int atomIsView = IdIsView;
        const int atomMaxByteLength = IdMaxByteLength;
        const int atomResizable = IdResizable;
        const int atomResize = IdResize;
        const int atomSlice = IdSlice;
        const int atomTransfer = IdTransfer;
        const int atomTransferToFixedLength = IdTransferToFixedLength;
        const int atomTransferToImmutable = IdTransferToImmutable;
        const int atomSliceToImmutable = IdSliceToImmutable;

        var speciesGetter = new JsHostFunction(Realm, (in info) =>
            {
                var thisValue = info.ThisValue;
                return thisValue;
            },
            "get [Symbol.species]", 0);

        var byteLengthGetter = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                return JsValue.FromInt32((int)ThisArrayBufferValue(realm, thisValue).ByteLength);
            }, "get byteLength", 0);

        var detachedGetter = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return ThisArrayBufferValue(realm, thisValue).IsDetached ? JsValue.True : JsValue.False;
        }, "get detached", 0);

        var maxByteLengthGetter = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var buffer = ThisArrayBufferValue(realm, thisValue);
            var maxByteLength = buffer.MaxByteLength ?? buffer.ByteLength;
            return JsValue.FromInt32((int)maxByteLength);
        }, "get maxByteLength", 0);

        var resizableGetter = new JsHostFunction(Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                return ThisArrayBufferValue(realm, thisValue).IsResizable ? JsValue.True : JsValue.False;
            }, "get resizable", 0);

        var isViewFn = new JsHostFunction(Realm, (in info) =>
        {
            var args = info.Arguments;
            return args.Length != 0 &&
                   args[0].TryGetObject(out var obj) &&
                   obj is JsTypedArrayObject or JsDataViewObject
                ? JsValue.True
                : JsValue.False;
        }, "isView", 1);

        var resizeFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var buffer = ThisArrayBufferValue(realm, thisValue);
            if (buffer.IsImmutable)
                throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is immutable");
            var newByteLength = args.Length == 0 || args[0].IsUndefined ? 0u : ToArrayBufferLength(realm, args[0]);
            buffer.Resize(newByteLength);
            return JsValue.Undefined;
        }, "resize", 1);

        var sliceFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var buffer = ThisArrayBufferValue(realm, thisValue);
            if (buffer.IsDetached)
                throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");

            long len = buffer.ByteLength;
            var start = args.Length == 0 || args[0].IsUndefined ? 0d : realm.ToIntegerOrInfinity(args[0]);
            var end = args.Length < 2 || args[1].IsUndefined ? len : realm.ToIntegerOrInfinity(args[1]);
            var first = NormalizeRelativeIndex(start, len);
            var final = NormalizeRelativeIndex(end, len);
            var newLen = (uint)Math.Max(0, final - first);

            var ctor = GetArrayBufferSpeciesConstructor(realm, buffer);
            var result = ConstructArrayBufferSpeciesResult(realm, ctor, newLen);
            if (result.IsDetached)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Species constructor returned detached ArrayBuffer");
            if (result.ByteLength < newLen)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Species constructor returned too small ArrayBuffer");
            if (ReferenceEquals(result, buffer))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Species constructor returned same ArrayBuffer");

            if (newLen != 0)
                buffer.CopyBytesTo((uint)first, result, 0, newLen);
            return result;
        }, "slice", 2);

        var sliceToImmutableFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var buffer = ThisArrayBufferValue(realm, thisValue);
            if (buffer.IsDetached)
                throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");

            long len = buffer.ByteLength;
            var start = args.Length == 0 || args[0].IsUndefined ? 0d : realm.ToIntegerOrInfinity(args[0]);
            var end = args.Length < 2 || args[1].IsUndefined ? len : realm.ToIntegerOrInfinity(args[1]);
            var first = NormalizeRelativeIndex(start, len);
            var final = NormalizeRelativeIndex(end, len);
            var newLen = (uint)Math.Max(0, final - first);
            return buffer.Slice((uint)first, newLen, immutableResult: true);
        }, "sliceToImmutable", 2);

        var transferFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var buffer = ThisArrayBufferValue(realm, thisValue);
            var newByteLength = args.Length == 0 || args[0].IsUndefined
                ? buffer.ByteLength
                : ToArrayBufferLength(realm, args[0]);
            return buffer.Transfer(newByteLength, !buffer.IsResizable);
        }, "transfer", 0);

        var transferToFixedLengthFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var buffer = ThisArrayBufferValue(realm, thisValue);
            var newByteLength = args.Length == 0 || args[0].IsUndefined
                ? buffer.ByteLength
                : ToArrayBufferLength(realm, args[0]);
            return buffer.Transfer(newByteLength, true);
        }, "transferToFixedLength", 0);

        var transferToImmutableFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var buffer = ThisArrayBufferValue(realm, thisValue);
            var newByteLength = args.Length == 0 || args[0].IsUndefined
                ? buffer.ByteLength
                : ToArrayBufferLength(realm, args[0]);
            return buffer.Transfer(newByteLength, true, immutableResult: true);
        }, "transferToImmutable", 0);

        Span<PropertyDefinition> ctorDefs =
        [
            PropertyDefinition.Mutable(atomIsView, isViewFn),
            PropertyDefinition.GetterData(IdSymbolSpecies, speciesGetter, configurable: true)
        ];
        ArrayBufferConstructor.InitializePrototypeProperty(ArrayBufferPrototype);
        ArrayBufferConstructor.DefineNewPropertiesNoCollision(Realm, ctorDefs);

        Span<PropertyDefinition> protoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, ArrayBufferConstructor),
            PropertyDefinition.GetterData(atomByteLength, byteLengthGetter, configurable: true),
            PropertyDefinition.GetterData(atomDetached, detachedGetter, configurable: true),
            PropertyDefinition.GetterData(atomMaxByteLength, maxByteLengthGetter, configurable: true),
            PropertyDefinition.GetterData(atomResizable, resizableGetter, configurable: true),
            PropertyDefinition.Mutable(atomResize, resizeFn),
            PropertyDefinition.Mutable(atomSlice, sliceFn),
            PropertyDefinition.Mutable(atomSliceToImmutable, sliceToImmutableFn),
            PropertyDefinition.Mutable(atomTransfer, transferFn),
            PropertyDefinition.Mutable(atomTransferToFixedLength, transferToFixedLengthFn),
            PropertyDefinition.Mutable(atomTransferToImmutable, transferToImmutableFn),
            PropertyDefinition.Const(IdSymbolToStringTag, "ArrayBuffer", configurable: true)
        ];
        ArrayBufferPrototype.DefineNewPropertiesNoCollision(Realm, protoDefs);
    }

    internal static JsArrayBufferObject ThisArrayBufferValue(JsRealm realm, in JsValue value)
    {
        if (value.TryGetObject(out var obj) &&
            obj is JsArrayBufferObject { IsShared: false } arrayBuffer)
            return arrayBuffer;
        throw new JsRuntimeException(JsErrorKind.TypeError,
            "ArrayBuffer.prototype.byteLength called on incompatible receiver");
    }

    private static JsFunction GetArrayBufferSpeciesConstructor(JsRealm realm, JsArrayBufferObject exemplar)
    {
        JsFunction defaultCtor = realm.ArrayBufferConstructor;
        if (!exemplar.TryGetPropertyAtom(realm, IdConstructor, out var ctorValue, out _))
            return defaultCtor;
        if (ctorValue.IsUndefined)
            return defaultCtor;
        if (!ctorValue.TryGetObject(out var ctorObj))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "ArrayBuffer constructor property must be an object");
        if (ctorObj.TryGetPropertyAtom(realm, IdSymbolSpecies, out var speciesValue, out _))
        {
            if (speciesValue.IsUndefined || speciesValue.IsNull)
                return defaultCtor;
            if (!speciesValue.TryGetObject(out var speciesObj) || speciesObj is not JsFunction speciesFn ||
                !speciesFn.IsConstructor)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "ArrayBuffer [Symbol.species] must be a constructor");

            return speciesFn;
        }

        return defaultCtor;
    }

    private static JsArrayBufferObject ConstructArrayBufferSpeciesResult(JsRealm realm, JsFunction ctor,
        uint byteLength)
    {
        Span<JsValue> lenArg =
        [
            JsValue.FromInt32((int)byteLength)
        ];
        var createdValue = realm.ConstructWithExplicitNewTarget(ctor, lenArg, ctor, 0);
        if (!createdValue.TryGetObject(out var createdObj) || createdObj is not JsArrayBufferObject createdBuffer)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "ArrayBuffer species constructor must return an ArrayBuffer");
        return createdBuffer;
    }

    private static uint ToArrayBufferLength(JsRealm realm, in JsValue value)
    {
        return ToAllocatableArrayBufferLength(ToRequestedArrayBufferLength(realm, value));
    }

    private static double ToRequestedArrayBufferLength(JsRealm realm, in JsValue value)
    {
        var number = realm.ToIntegerOrInfinity(value);
        if (double.IsNaN(number) || number == 0d)
            return 0d;
        if (number < 0d || double.IsInfinity(number))
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array buffer length");
        return number;
    }

    private static uint ToAllocatableArrayBufferLength(double number)
    {
        if (number > uint.MaxValue)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array buffer length");
        return (uint)number;
    }
}
