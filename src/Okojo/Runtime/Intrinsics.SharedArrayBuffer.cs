namespace Okojo.Runtime;

public partial class Intrinsics
{
    private JsHostFunction CreateSharedArrayBufferConstructor()
    {
        return new(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var callee = (JsHostFunction)info.Function;
            if (!info.IsConstruct)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Constructor SharedArrayBuffer requires 'new'");

            var requestedByteLength = args.Length == 0 || args[0].IsUndefined
                ? 0d
                : realm.ToIntegerOrInfinity(args[0]);
            if (double.IsNaN(requestedByteLength) || requestedByteLength == 0d)
                requestedByteLength = 0d;
            if (requestedByteLength < 0d)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid shared array buffer length");

            double? requestedMaxByteLength = null;
            if (args.Length > 1 && !args[1].IsUndefined && !args[1].IsNull &&
                args[1].TryGetObject(out var options))
                if (options.TryGetPropertyByAtom(IdMaxByteLength, out var maxValue) && !maxValue.IsUndefined)
                {
                    requestedMaxByteLength = realm.ToIntegerOrInfinity(maxValue);
                    if (double.IsNaN(requestedMaxByteLength.Value) || requestedMaxByteLength.Value == 0d)
                        requestedMaxByteLength = 0d;
                    if (requestedMaxByteLength.Value < 0d)
                        throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid shared array buffer length");
                }

            if (requestedMaxByteLength.HasValue && requestedByteLength > requestedMaxByteLength.Value)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid shared array buffer max length");

            var prototype =
                GetPrototypeFromConstructorOrIntrinsic(info.NewTarget, callee, realm.SharedArrayBufferPrototype);
            var byteLength = ToAllocatableSharedArrayBufferLength(requestedByteLength);
            uint? maxByteLength = requestedMaxByteLength.HasValue
                ? ToAllocatableSharedArrayBufferLength(requestedMaxByteLength.Value)
                : null;
            return new JsArrayBufferObject(realm,
                new JsArrayBufferObject.SharedBufferStorage(byteLength, maxByteLength), prototype);
        }, "SharedArrayBuffer", 1, true);
    }

    private void InstallSharedArrayBufferConstructorBuiltins()
    {
        const int atomByteLength = IdByteLength;
        const int atomGrow = IdGrow;
        const int atomGrowable = IdGrowable;
        const int atomMaxByteLength = IdMaxByteLength;
        const int atomSlice = IdSlice;

        var byteLengthGetter = new JsHostFunction(Realm, (in info) =>
        {
            var buffer = ThisSharedArrayBufferValue(info.Realm, info.ThisValue);
            return JsValue.FromInt32((int)buffer.ByteLength);
        }, "get byteLength", 0);

        var growableGetter = new JsHostFunction(Realm, (in info) =>
        {
            var buffer = ThisSharedArrayBufferValue(info.Realm, info.ThisValue);
            return buffer.IsGrowable ? JsValue.True : JsValue.False;
        }, "get growable", 0);

        var maxByteLengthGetter = new JsHostFunction(Realm, (in info) =>
        {
            var buffer = ThisSharedArrayBufferValue(info.Realm, info.ThisValue);
            var max = buffer.MaxByteLength ?? buffer.ByteLength;
            return JsValue.FromInt32((int)max);
        }, "get maxByteLength", 0);

        var growFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var buffer = ThisSharedArrayBufferValue(realm, info.ThisValue);
            var newByteLength = args.Length == 0 || args[0].IsUndefined ? 0u : ToArrayBufferLength(realm, args[0]);
            buffer.GrowShared(newByteLength);
            return JsValue.Undefined;
        }, "grow", 1);

        var sliceFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            var buffer = ThisSharedArrayBufferValue(realm, info.ThisValue);
            long len = buffer.ByteLength;
            var start = args.Length == 0 || args[0].IsUndefined ? 0d : realm.ToIntegerOrInfinity(args[0]);
            var end = args.Length < 2 || args[1].IsUndefined ? len : realm.ToIntegerOrInfinity(args[1]);
            var first = NormalizeRelativeIndex(start, len);
            var final = NormalizeRelativeIndex(end, len);
            var newLen = (uint)Math.Max(0, final - first);
            return buffer.Slice((uint)first, newLen, realm.SharedArrayBufferPrototype);
        }, "slice", 2);

        SharedArrayBufferConstructor.InitializePrototypeProperty(SharedArrayBufferPrototype);

        Span<PropertyDefinition> protoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, SharedArrayBufferConstructor),
            PropertyDefinition.GetterData(atomByteLength, byteLengthGetter, configurable: true),
            PropertyDefinition.GetterData(atomGrowable, growableGetter, configurable: true),
            PropertyDefinition.GetterData(atomMaxByteLength, maxByteLengthGetter, configurable: true),
            PropertyDefinition.Mutable(atomGrow, growFn),
            PropertyDefinition.Mutable(atomSlice, sliceFn),
            PropertyDefinition.Const(IdSymbolToStringTag, "SharedArrayBuffer", configurable: true)
        ];
        SharedArrayBufferPrototype.DefineNewPropertiesNoCollision(Realm, protoDefs);
    }

    internal static JsArrayBufferObject ThisSharedArrayBufferValue(JsRealm realm, in JsValue value)
    {
        if (value.TryGetObject(out var obj) && obj is JsArrayBufferObject { IsShared: true } buffer)
            return buffer;
        throw new JsRuntimeException(JsErrorKind.TypeError,
            "SharedArrayBuffer.prototype.byteLength called on incompatible receiver");
    }

    private static uint ToAllocatableSharedArrayBufferLength(double number)
    {
        if (double.IsNaN(number) || number == 0d)
            return 0;
        if (number < 0d || double.IsInfinity(number) || number > uint.MaxValue)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid shared array buffer length");
        return (uint)number;
    }
}
