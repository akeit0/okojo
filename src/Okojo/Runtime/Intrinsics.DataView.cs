namespace Okojo.Runtime;

public partial class Intrinsics
{
    private void InstallDataViewBuiltins()
    {
        const int atomBuffer = IdBuffer;
        const int atomByteLength = IdByteLength;
        const int atomByteOffset = IdByteOffset;

        var bufferGetter = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromObject(ThisDataViewValue(realm, thisValue).Buffer);
        }, "get buffer", 0);
        var byteLengthGetter = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromInt32((int)ThisDataViewValue(realm, thisValue).ByteLength);
        }, "get byteLength", 0);
        var byteOffsetGetter = new JsHostFunction(Realm, static (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromInt32((int)ThisDataViewValue(realm, thisValue).ByteOffset);
        }, "get byteOffset", 0);

        Span<(string Name, int Size, Func<JsArrayBufferObject, uint, bool, JsValue> Getter)> getDefs =
        [
            ("getInt8", 1, static (buffer, index, _) => JsValue.FromInt32(buffer.GetInt8(index))),
            ("getUint8", 1, static (buffer, index, _) => JsValue.FromInt32(buffer.GetByte(index))),
            ("getInt16", 2, static (buffer, index, le) => JsValue.FromInt32(buffer.GetInt16(index, le))),
            ("getUint16", 2, static (buffer, index, le) => JsValue.FromInt32(buffer.GetUInt16(index, le))),
            ("getInt32", 4, static (buffer, index, le) => JsValue.FromInt32(buffer.GetInt32(index, le))),
            ("getUint32", 4, static (buffer, index, le) => new((double)buffer.GetUInt32(index, le))),
            ("getFloat16", 2, static (buffer, index, le) => new((double)buffer.GetFloat16(index, le))),
            ("getFloat32", 4, static (buffer, index, le) => new(buffer.GetFloat32(index, le))),
            ("getFloat64", 8, static (buffer, index, le) => new(buffer.GetFloat64(index, le))),
            ("getBigInt64", 8,
                static (buffer, index, le) => JsValue.FromBigInt(new(buffer.GetInt64(index, le)))),
            ("getBigUint64", 8,
                static (buffer, index, le) => JsValue.FromBigInt(new(buffer.GetUInt64(index, le))))
        ];

        Span<(string Name, int Size, TypedArrayElementKind Kind, Action<JsArrayBufferObject, uint, bool, JsValue>
            Setter)> setDefs =
        [
            ("setInt8", 1,
                TypedArrayElementKind.Int8,
                static (buffer, index, _, value) => buffer.SetInt8(index, unchecked((sbyte)value.Int32Value))),
            ("setUint8", 1,
                TypedArrayElementKind.Uint8,
                static (buffer, index, _, value) => buffer.SetByte(index, unchecked((byte)value.Int32Value))),
            ("setInt16", 2,
                TypedArrayElementKind.Int16,
                static (buffer, index, le, value) => buffer.SetInt16(index, unchecked((short)value.Int32Value), le)),
            ("setUint16", 2,
                TypedArrayElementKind.Uint16,
                static (buffer, index, le, value) => buffer.SetUInt16(index, unchecked((ushort)value.Int32Value), le)),
            ("setInt32", 4,
                TypedArrayElementKind.Int32,
                static (buffer, index, le, value) => buffer.SetInt32(index, value.Int32Value, le)),
            ("setUint32", 4,
                TypedArrayElementKind.Uint32,
                static (buffer, index, le, value) => buffer.SetUInt32(index, unchecked((uint)value.NumberValue), le)),
            ("setFloat16", 2,
                TypedArrayElementKind.Float16,
                static (buffer, index, le, value) => buffer.SetFloat16(index, (Half)value.NumberValue, le)),
            ("setFloat32", 4,
                TypedArrayElementKind.Float32,
                static (buffer, index, le, value) => buffer.SetFloat32(index, (float)value.NumberValue, le)),
            ("setFloat64", 8,
                TypedArrayElementKind.Float64,
                static (buffer, index, le, value) => buffer.SetFloat64(index, value.NumberValue, le)),
            ("setBigInt64", 8,
                TypedArrayElementKind.BigInt64,
                static (buffer, index, le, value) =>
                    buffer.SetInt64(index, (long)value.AsBigInt().Value, le)),
            ("setBigUint64", 8,
                TypedArrayElementKind.BigUint64,
                static (buffer, index, le, value) =>
                    buffer.SetUInt64(index, (ulong)value.AsBigInt().Value, le))
        ];

        var defs = new List<PropertyDefinition>
        {
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(DataViewConstructor)),
            PropertyDefinition.GetterData(atomBuffer, bufferGetter, configurable: true),
            PropertyDefinition.GetterData(atomByteLength, byteLengthGetter, configurable: true),
            PropertyDefinition.GetterData(atomByteOffset, byteOffsetGetter, configurable: true),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("DataView"), configurable: true)
        };

        foreach (var def in getDefs)
            defs.Add(PropertyDefinition.Mutable(Atoms.InternNoCheck(def.Name), JsValue.FromObject(new JsHostFunction(
                Realm, (in info) =>
                {
                    var realm = info.Realm;
                    var thisValue = info.ThisValue;
                    var args = info.Arguments;
                    var view = ThisDataViewValue(realm, thisValue);
                    var offset = args.Length == 0
                        ? 0u
                        : realm.ToTypedArrayLengthOrOffset(args[0], "Offset is outside the bounds of the DataView");
                    var littleEndian = args.Length > 1 && JsRealm.ToBoolean(args[1]);
                    var index = view.GetViewByteIndex(offset, def.Size);
                    return def.Getter(view.Buffer, index, littleEndian);
                }, def.Name, 1))));

        foreach (var def in setDefs)
            defs.Add(PropertyDefinition.Mutable(Atoms.InternNoCheck(def.Name), JsValue.FromObject(new JsHostFunction(
                Realm, (in info) =>
                {
                    var realm = info.Realm;
                    var thisValue = info.ThisValue;
                    var args = info.Arguments;
                    var view = ThisDataViewValue(realm, thisValue);
                    var offset = args.Length == 0
                        ? 0u
                        : realm.ToTypedArrayLengthOrOffset(args[0], "Offset is outside the bounds of the DataView");
                    var value = args.Length > 1 ? args[1] : JsValue.Undefined;
                    var littleEndian = args.Length > 2 && JsRealm.ToBoolean(args[2]);
                    var normalized = TypedArrayElementKindInfo.NormalizeValue(realm, def.Kind, value);
                    var index = view.GetViewByteIndex(offset, def.Size);
                    def.Setter(view.Buffer, index, littleEndian, normalized);
                    return JsValue.Undefined;
                }, def.Name, 2))));

        DataViewConstructor.InitializePrototypeProperty(DataViewPrototype);
        DataViewPrototype.DefineNewPropertiesNoCollision(Realm, defs.ToArray());
    }

    internal static JsDataViewObject ThisDataViewValue(JsRealm realm, in JsValue value)
    {
        if (value.TryGetObject(out var obj) && obj is JsDataViewObject dataView)
            return dataView;
        throw new JsRuntimeException(JsErrorKind.TypeError, "DataView method called on incompatible receiver");
    }
}
