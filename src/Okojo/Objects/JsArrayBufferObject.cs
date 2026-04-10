using System.Buffers.Binary;

namespace Okojo.Objects;

public class JsArrayBufferObject : JsObject
{
    private readonly IExternalBufferBackingStore? externalBackingStore;
    private readonly bool externalIsShared;
    private readonly Dictionary<uint, List<SharedWaiter>>? externalSharedWaitersByByteIndex;
    private readonly uint? maxByteLength;
    private readonly SharedBufferStorage? sharedStorage;

    private byte[] bytes;

    public JsArrayBufferObject(JsRealm realm, uint byteLength, JsObject? prototype = null)
        : this(realm, byteLength, null, prototype)
    {
    }

    public JsArrayBufferObject(
        JsRealm realm,
        uint byteLength,
        uint? maxByteLength,
        JsObject? prototype = null,
        bool immutable = false) : base(realm)
    {
        if (maxByteLength.HasValue && maxByteLength.Value < byteLength)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array buffer length");

        bytes = new byte[byteLength];
        this.maxByteLength = maxByteLength;
        IsImmutable = immutable;
        Prototype = prototype ?? realm.ArrayBufferPrototype;
    }

    internal JsArrayBufferObject(JsRealm realm, SharedBufferStorage sharedStorage, JsObject? prototype = null)
        : base(realm)
    {
        bytes = Array.Empty<byte>();
        this.sharedStorage = sharedStorage;
        maxByteLength = sharedStorage.MaxByteLength;
        Prototype = prototype ?? realm.SharedArrayBufferPrototype;
    }

    private JsArrayBufferObject(
        JsRealm realm,
        IExternalBufferBackingStore externalBackingStore,
        uint? maxByteLength,
        bool isShared,
        JsObject? prototype = null) : base(realm)
    {
        bytes = Array.Empty<byte>();
        this.externalBackingStore = externalBackingStore;
        externalIsShared = isShared;
        externalSharedWaitersByByteIndex = isShared ? new Dictionary<uint, List<SharedWaiter>>() : null;
        this.maxByteLength = maxByteLength;
        Prototype = prototype ?? (isShared ? realm.SharedArrayBufferPrototype : realm.ArrayBufferPrototype);
    }

    public uint ByteLength
    {
        get
        {
            if (sharedStorage is not null)
                lock (sharedStorage.SyncRoot)
                {
                    return (uint)sharedStorage.Bytes.Length;
                }

            if (externalBackingStore is not null)
                return IsDetached ? 0u : (uint)externalBackingStore.ByteLength;

            return IsDetached ? 0u : (uint)bytes.Length;
        }
    }

    public bool IsDetached { get; private set; }

    public bool IsImmutable { get; }

    public bool IsShared => sharedStorage is not null || externalIsShared;
    public bool IsResizable => sharedStorage is null && externalBackingStore is null && maxByteLength.HasValue;
    public bool IsGrowable => sharedStorage is not null && maxByteLength.HasValue;
    public uint? MaxByteLength => maxByteLength;

    public static JsArrayBufferObject CreateExternal(
        JsRealm realm,
        IExternalBufferBackingStore externalBackingStore,
        uint? maxByteLength = null,
        JsObject? prototype = null)
    {
        return new(realm, externalBackingStore, maxByteLength, false, prototype);
    }

    public static JsArrayBufferObject CreateExternalShared(
        JsRealm realm,
        IExternalBufferBackingStore externalBackingStore,
        uint? maxByteLength = null,
        JsObject? prototype = null)
    {
        return new(realm, externalBackingStore, maxByteLength, true, prototype);
    }

    internal SharedBufferStorage GetSharedStorage()
    {
        if (sharedStorage is null)
            throw new InvalidOperationException("ArrayBuffer is not shared.");
        return sharedStorage;
    }

    internal object GetSharedSyncRoot()
    {
        if (sharedStorage is not null)
            return sharedStorage.SyncRoot;
        if (externalIsShared && externalBackingStore is not null)
            return externalBackingStore.SyncRoot;
        throw new InvalidOperationException("ArrayBuffer is not shared.");
    }

    internal JsArrayBufferObject CreateSharedWrapper(JsRealm realm, JsObject? prototype = null)
    {
        if (sharedStorage is null)
            throw new InvalidOperationException("ArrayBuffer is not shared.");
        return new(realm, sharedStorage, prototype);
    }

    internal void Resize(uint newByteLength)
    {
        if (sharedStorage is not null)
            throw new JsRuntimeException(JsErrorKind.TypeError, "SharedArrayBuffer cannot be resized");
        if (externalBackingStore is not null)
            throw new JsRuntimeException(JsErrorKind.TypeError, "External ArrayBuffer cannot be resized");
        if (!maxByteLength.HasValue)
            throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is not resizable");
        if (IsDetached)
            throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");
        if (IsImmutable)
            throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is immutable");
        if (newByteLength > maxByteLength.Value)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array buffer length");

        Array.Resize(ref bytes, (int)newByteLength);
    }

    internal void GrowShared(uint newByteLength)
    {
        if (sharedStorage is null)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "SharedArrayBuffer.prototype.grow called on incompatible receiver");

        lock (sharedStorage.SyncRoot)
        {
            var current = (uint)sharedStorage.Bytes.Length;
            if (!sharedStorage.MaxByteLength.HasValue)
                throw new JsRuntimeException(JsErrorKind.TypeError, "SharedArrayBuffer is not growable");
            if (newByteLength < current || newByteLength > sharedStorage.MaxByteLength.Value)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid shared array buffer length");
            if (newByteLength == current)
                return;
            Array.Resize(ref sharedStorage.Bytes, (int)newByteLength);
        }
    }

    public void Detach()
    {
        if (sharedStorage is not null)
            throw new JsRuntimeException(JsErrorKind.TypeError, "SharedArrayBuffer cannot be detached");
        if (externalBackingStore is not null)
            throw new JsRuntimeException(JsErrorKind.TypeError, "External ArrayBuffer cannot be detached");
        IsDetached = true;
        bytes = Array.Empty<byte>();
    }

    public void RefreshExternalBackingStore()
    {
    }

    internal JsArrayBufferObject Slice(uint startByteIndex, uint newByteLength, JsObject? prototype = null,
        uint? newMaxByteLength = null, bool immutableResult = false)
    {
        if (sharedStorage is not null)
        {
            var result = new JsArrayBufferObject(Shape.Owner,
                new SharedBufferStorage(newByteLength, newMaxByteLength),
                prototype ?? Shape.Owner.SharedArrayBufferPrototype);
            if (newByteLength != 0)
                CopyBytesTo(startByteIndex, result, 0, newByteLength);
            return result;
        }

        if (IsDetached)
            throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");
        if (startByteIndex > ByteLength || newByteLength > ByteLength - startByteIndex)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array buffer length");

        var output = new JsArrayBufferObject(Shape.Owner, newByteLength, newMaxByteLength, prototype, immutableResult);
        if (newByteLength != 0)
            Array.Copy(bytes, (int)startByteIndex, output.bytes, 0, (int)newByteLength);
        return output;
    }

    internal JsArrayBufferObject Transfer(uint newByteLength, bool fixedLength, JsObject? prototype = null,
        bool immutableResult = false)
    {
        if (sharedStorage is not null)
            throw new JsRuntimeException(JsErrorKind.TypeError, "SharedArrayBuffer cannot be transferred");
        if (IsDetached)
            throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");
        if (IsImmutable)
            throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is immutable");

        var targetMaxByteLength = fixedLength ? null : maxByteLength;
        if (targetMaxByteLength.HasValue && newByteLength > targetMaxByteLength.Value)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array buffer length");

        var result =
            new JsArrayBufferObject(Shape.Owner, newByteLength, targetMaxByteLength, prototype, immutableResult);
        var copyLength = Math.Min((uint)bytes.Length, newByteLength);
        if (copyLength != 0)
            Array.Copy(bytes, 0, result.bytes, 0, (int)copyLength);
        Detach();
        return result;
    }

    internal byte GetByte(uint byteIndex)
    {
        if (sharedStorage is not null)
            lock (sharedStorage.SyncRoot)
            {
                EnsureReadableRange(sharedStorage, byteIndex, 1);
                return sharedStorage.Bytes[byteIndex];
            }

        if (externalBackingStore is not null)
            lock (externalBackingStore.SyncRoot)
            {
                EnsureReadableRange(byteIndex, 1);
                return externalBackingStore.GetSpan()[(int)byteIndex];
            }

        EnsureReadableRange(byteIndex, 1);
        return bytes[byteIndex];
    }

    internal void SetByte(uint byteIndex, byte value)
    {
        if (sharedStorage is not null)
            lock (sharedStorage.SyncRoot)
            {
                EnsureWritableRange(sharedStorage, byteIndex, 1);
                sharedStorage.Bytes[byteIndex] = value;
                return;
            }

        if (externalBackingStore is not null)
            lock (externalBackingStore.SyncRoot)
            {
                EnsureWritableRange(byteIndex, 1);
                externalBackingStore.GetSpan()[(int)byteIndex] = value;
                return;
            }

        EnsureWritableRange(byteIndex, 1);
        bytes[byteIndex] = value;
    }

    internal sbyte GetInt8(uint byteIndex)
    {
        return unchecked((sbyte)GetByte(byteIndex));
    }

    internal void SetInt8(uint byteIndex, sbyte value)
    {
        SetByte(byteIndex, unchecked((byte)value));
    }

    internal short GetInt16(uint byteIndex, bool littleEndian = false)
    {
        return unchecked((short)GetUInt16(byteIndex, littleEndian));
    }

    internal ushort GetUInt16(uint byteIndex, bool littleEndian = false)
    {
        if (sharedStorage is not null)
            lock (sharedStorage.SyncRoot)
            {
                EnsureReadableRange(sharedStorage, byteIndex, 2);
                var span = sharedStorage.Bytes.AsSpan((int)byteIndex, 2);
                return littleEndian == BitConverter.IsLittleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(span)
                    : BinaryPrimitives.ReadUInt16BigEndian(span);
            }

        if (externalBackingStore is not null)
            lock (externalBackingStore.SyncRoot)
            {
                EnsureReadableRange(byteIndex, 2);
                var span = externalBackingStore.GetSpan().Slice((int)byteIndex, 2);
                return littleEndian == BitConverter.IsLittleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(span)
                    : BinaryPrimitives.ReadUInt16BigEndian(span);
            }

        EnsureReadableRange(byteIndex, 2);
        var local = bytes.AsSpan((int)byteIndex, 2);
        return littleEndian == BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(local)
            : BinaryPrimitives.ReadUInt16BigEndian(local);
    }

    internal void SetInt16(uint byteIndex, short value, bool littleEndian = false)
    {
        SetUInt16(byteIndex, unchecked((ushort)value), littleEndian);
    }

    internal void SetUInt16(uint byteIndex, ushort value, bool littleEndian = false)
    {
        if (sharedStorage is not null)
            lock (sharedStorage.SyncRoot)
            {
                EnsureWritableRange(sharedStorage, byteIndex, 2);
                var span = sharedStorage.Bytes.AsSpan((int)byteIndex, 2);
                if (littleEndian == BitConverter.IsLittleEndian)
                    BinaryPrimitives.WriteUInt16LittleEndian(span, value);
                else
                    BinaryPrimitives.WriteUInt16BigEndian(span, value);
                return;
            }

        if (externalBackingStore is not null)
            lock (externalBackingStore.SyncRoot)
            {
                EnsureWritableRange(byteIndex, 2);
                var span = externalBackingStore.GetSpan().Slice((int)byteIndex, 2);
                if (littleEndian == BitConverter.IsLittleEndian)
                    BinaryPrimitives.WriteUInt16LittleEndian(span, value);
                else
                    BinaryPrimitives.WriteUInt16BigEndian(span, value);
                return;
            }

        EnsureWritableRange(byteIndex, 2);
        var local = bytes.AsSpan((int)byteIndex, 2);
        if (littleEndian == BitConverter.IsLittleEndian)
            BinaryPrimitives.WriteUInt16LittleEndian(local, value);
        else
            BinaryPrimitives.WriteUInt16BigEndian(local, value);
    }

    internal int GetInt32(uint byteIndex, bool littleEndian = false)
    {
        return unchecked((int)GetUInt32(byteIndex, littleEndian));
    }

    internal uint GetUInt32(uint byteIndex, bool littleEndian = false)
    {
        if (sharedStorage is not null)
            lock (sharedStorage.SyncRoot)
            {
                EnsureReadableRange(sharedStorage, byteIndex, 4);
                var span = sharedStorage.Bytes.AsSpan((int)byteIndex, 4);
                return littleEndian == BitConverter.IsLittleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(span)
                    : BinaryPrimitives.ReadUInt32BigEndian(span);
            }

        if (externalBackingStore is not null)
            lock (externalBackingStore.SyncRoot)
            {
                EnsureReadableRange(byteIndex, 4);
                var span = externalBackingStore.GetSpan().Slice((int)byteIndex, 4);
                return littleEndian == BitConverter.IsLittleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(span)
                    : BinaryPrimitives.ReadUInt32BigEndian(span);
            }

        EnsureReadableRange(byteIndex, 4);
        var local = bytes.AsSpan((int)byteIndex, 4);
        return littleEndian == BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(local)
            : BinaryPrimitives.ReadUInt32BigEndian(local);
    }

    internal void SetInt32(uint byteIndex, int value, bool littleEndian = false)
    {
        SetUInt32(byteIndex, unchecked((uint)value), littleEndian);
    }

    internal void SetUInt32(uint byteIndex, uint value, bool littleEndian = false)
    {
        if (sharedStorage is not null)
            lock (sharedStorage.SyncRoot)
            {
                EnsureWritableRange(sharedStorage, byteIndex, 4);
                var span = sharedStorage.Bytes.AsSpan((int)byteIndex, 4);
                if (littleEndian == BitConverter.IsLittleEndian)
                    BinaryPrimitives.WriteUInt32LittleEndian(span, value);
                else
                    BinaryPrimitives.WriteUInt32BigEndian(span, value);
                return;
            }

        if (externalBackingStore is not null)
            lock (externalBackingStore.SyncRoot)
            {
                EnsureWritableRange(byteIndex, 4);
                var span = externalBackingStore.GetSpan().Slice((int)byteIndex, 4);
                if (littleEndian == BitConverter.IsLittleEndian)
                    BinaryPrimitives.WriteUInt32LittleEndian(span, value);
                else
                    BinaryPrimitives.WriteUInt32BigEndian(span, value);
                return;
            }

        EnsureWritableRange(byteIndex, 4);
        var local = bytes.AsSpan((int)byteIndex, 4);
        if (littleEndian == BitConverter.IsLittleEndian)
            BinaryPrimitives.WriteUInt32LittleEndian(local, value);
        else
            BinaryPrimitives.WriteUInt32BigEndian(local, value);
    }

    internal long GetInt64(uint byteIndex, bool littleEndian = false)
    {
        return unchecked((long)GetUInt64(byteIndex, littleEndian));
    }

    internal ulong GetUInt64(uint byteIndex, bool littleEndian = false)
    {
        if (sharedStorage is not null)
            lock (sharedStorage.SyncRoot)
            {
                EnsureReadableRange(sharedStorage, byteIndex, 8);
                var span = sharedStorage.Bytes.AsSpan((int)byteIndex, 8);
                return littleEndian == BitConverter.IsLittleEndian
                    ? BinaryPrimitives.ReadUInt64LittleEndian(span)
                    : BinaryPrimitives.ReadUInt64BigEndian(span);
            }

        if (externalBackingStore is not null)
            lock (externalBackingStore.SyncRoot)
            {
                EnsureReadableRange(byteIndex, 8);
                var span = externalBackingStore.GetSpan().Slice((int)byteIndex, 8);
                return littleEndian == BitConverter.IsLittleEndian
                    ? BinaryPrimitives.ReadUInt64LittleEndian(span)
                    : BinaryPrimitives.ReadUInt64BigEndian(span);
            }

        EnsureReadableRange(byteIndex, 8);
        var local = bytes.AsSpan((int)byteIndex, 8);
        return littleEndian == BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReadUInt64LittleEndian(local)
            : BinaryPrimitives.ReadUInt64BigEndian(local);
    }

    internal void SetInt64(uint byteIndex, long value, bool littleEndian = false)
    {
        SetUInt64(byteIndex, unchecked((ulong)value), littleEndian);
    }

    internal void SetUInt64(uint byteIndex, ulong value, bool littleEndian = false)
    {
        if (sharedStorage is not null)
            lock (sharedStorage.SyncRoot)
            {
                EnsureWritableRange(sharedStorage, byteIndex, 8);
                var span = sharedStorage.Bytes.AsSpan((int)byteIndex, 8);
                if (littleEndian == BitConverter.IsLittleEndian)
                    BinaryPrimitives.WriteUInt64LittleEndian(span, value);
                else
                    BinaryPrimitives.WriteUInt64BigEndian(span, value);
                return;
            }

        if (externalBackingStore is not null)
            lock (externalBackingStore.SyncRoot)
            {
                EnsureWritableRange(byteIndex, 8);
                var span = externalBackingStore.GetSpan().Slice((int)byteIndex, 8);
                if (littleEndian == BitConverter.IsLittleEndian)
                    BinaryPrimitives.WriteUInt64LittleEndian(span, value);
                else
                    BinaryPrimitives.WriteUInt64BigEndian(span, value);
                return;
            }

        EnsureWritableRange(byteIndex, 8);
        var local = bytes.AsSpan((int)byteIndex, 8);
        if (littleEndian == BitConverter.IsLittleEndian)
            BinaryPrimitives.WriteUInt64LittleEndian(local, value);
        else
            BinaryPrimitives.WriteUInt64BigEndian(local, value);
    }

    internal Half GetFloat16(uint byteIndex, bool littleEndian = false)
    {
        return BitConverter.UInt16BitsToHalf(GetUInt16(byteIndex, littleEndian));
    }

    internal void SetFloat16(uint byteIndex, Half value, bool littleEndian = false)
    {
        SetUInt16(byteIndex, BitConverter.HalfToUInt16Bits(value), littleEndian);
    }

    internal float GetFloat32(uint byteIndex, bool littleEndian = false)
    {
        return BitConverter.Int32BitsToSingle(unchecked((int)GetUInt32(byteIndex, littleEndian)));
    }

    internal void SetFloat32(uint byteIndex, float value, bool littleEndian = false)
    {
        SetUInt32(byteIndex, unchecked((uint)BitConverter.SingleToInt32Bits(value)), littleEndian);
    }

    internal double GetFloat64(uint byteIndex, bool littleEndian = false)
    {
        return BitConverter.Int64BitsToDouble(unchecked((long)GetUInt64(byteIndex, littleEndian)));
    }

    internal void SetFloat64(uint byteIndex, double value, bool littleEndian = false)
    {
        SetUInt64(byteIndex, unchecked((ulong)BitConverter.DoubleToInt64Bits(value)), littleEndian);
    }

    internal JsValue ReadTypedArrayElement(JsRealm realm, TypedArrayElementKind kind, uint byteIndex)
    {
        var nativeLittleEndian = BitConverter.IsLittleEndian;
        return kind switch
        {
            TypedArrayElementKind.Int8 => JsValue.FromInt32(GetInt8(byteIndex)),
            TypedArrayElementKind.Uint8 => JsValue.FromInt32(GetByte(byteIndex)),
            TypedArrayElementKind.Uint8Clamped => JsValue.FromInt32(GetByte(byteIndex)),
            TypedArrayElementKind.Int16 => JsValue.FromInt32(GetInt16(byteIndex, nativeLittleEndian)),
            TypedArrayElementKind.Uint16 => JsValue.FromInt32(GetUInt16(byteIndex, nativeLittleEndian)),
            TypedArrayElementKind.Int32 => JsValue.FromInt32(GetInt32(byteIndex, nativeLittleEndian)),
            TypedArrayElementKind.Uint32 => new((double)GetUInt32(byteIndex, nativeLittleEndian)),
            TypedArrayElementKind.Float16 => new((double)GetFloat16(byteIndex, nativeLittleEndian)),
            TypedArrayElementKind.Float32 => new(GetFloat32(byteIndex, nativeLittleEndian)),
            TypedArrayElementKind.Float64 => new(GetFloat64(byteIndex, nativeLittleEndian)),
            TypedArrayElementKind.BigInt64 => JsValue.FromBigInt(
                new(new(GetInt64(byteIndex, nativeLittleEndian)))),
            TypedArrayElementKind.BigUint64 => JsValue.FromBigInt(
                new(new(GetUInt64(byteIndex, nativeLittleEndian)))),
            _ => JsValue.Undefined
        };
    }

    internal void WriteTypedArrayElement(JsRealm realm, TypedArrayElementKind kind, uint byteIndex, in JsValue value)
    {
        var normalized = TypedArrayElementKindInfo.NormalizeValue(realm, kind, value);
        WriteNormalizedTypedArrayElement(kind, byteIndex, normalized);
    }

    internal void WriteNormalizedTypedArrayElement(TypedArrayElementKind kind, uint byteIndex, in JsValue normalized)
    {
        var nativeLittleEndian = BitConverter.IsLittleEndian;
        switch (kind)
        {
            case TypedArrayElementKind.Int8:
                SetInt8(byteIndex, unchecked((sbyte)normalized.Int32Value));
                break;
            case TypedArrayElementKind.Uint8:
            case TypedArrayElementKind.Uint8Clamped:
                SetByte(byteIndex, unchecked((byte)normalized.Int32Value));
                break;
            case TypedArrayElementKind.Int16:
                SetInt16(byteIndex, unchecked((short)normalized.Int32Value), nativeLittleEndian);
                break;
            case TypedArrayElementKind.Uint16:
                SetUInt16(byteIndex, unchecked((ushort)normalized.Int32Value), nativeLittleEndian);
                break;
            case TypedArrayElementKind.Int32:
                SetInt32(byteIndex, normalized.Int32Value, nativeLittleEndian);
                break;
            case TypedArrayElementKind.Uint32:
                SetUInt32(byteIndex, unchecked((uint)normalized.NumberValue), nativeLittleEndian);
                break;
            case TypedArrayElementKind.Float16:
                SetFloat16(byteIndex, (Half)normalized.NumberValue, nativeLittleEndian);
                break;
            case TypedArrayElementKind.Float32:
                SetFloat32(byteIndex, (float)normalized.NumberValue, nativeLittleEndian);
                break;
            case TypedArrayElementKind.Float64:
                SetFloat64(byteIndex, normalized.NumberValue, nativeLittleEndian);
                break;
            case TypedArrayElementKind.BigInt64:
                SetInt64(byteIndex, unchecked((long)normalized.AsBigInt().Value), nativeLittleEndian);
                break;
            case TypedArrayElementKind.BigUint64:
                SetUInt64(byteIndex, unchecked((ulong)normalized.AsBigInt().Value), nativeLittleEndian);
                break;
        }
    }

    internal void CopyBytesWithin(uint targetByteIndex, uint sourceByteIndex, uint byteCount)
    {
        if (byteCount == 0)
            return;

        if (sharedStorage is not null)
            lock (sharedStorage.SyncRoot)
            {
                EnsureReadableRange(sharedStorage, sourceByteIndex, checked((int)byteCount));
                EnsureWritableRange(sharedStorage, targetByteIndex, checked((int)byteCount));
                Array.Copy(sharedStorage.Bytes, (int)sourceByteIndex, sharedStorage.Bytes, (int)targetByteIndex,
                    (int)byteCount);
                return;
            }

        EnsureReadableRange(sourceByteIndex, checked((int)byteCount));
        EnsureWritableRange(targetByteIndex, checked((int)byteCount));
        Array.Copy(bytes, (int)sourceByteIndex, bytes, (int)targetByteIndex, (int)byteCount);
    }

    internal void CopyBytesTo(uint sourceByteIndex, JsArrayBufferObject target, uint targetByteIndex, uint byteCount)
    {
        if (byteCount == 0)
            return;

        if (sharedStorage is not null)
            lock (sharedStorage.SyncRoot)
            {
                EnsureReadableRange(sharedStorage, sourceByteIndex, checked((int)byteCount));
                target.CopyBytesFromShared(sharedStorage.Bytes, targetByteIndex, sourceByteIndex, byteCount);
                return;
            }

        EnsureReadableRange(sourceByteIndex, checked((int)byteCount));
        target.CopyBytesFromLocal(bytes, targetByteIndex, sourceByteIndex, byteCount);
    }

    internal SharedWaiter AddSharedWaiter(JsRealm realm, uint byteIndex)
    {
        if (sharedStorage is null && externalSharedWaitersByByteIndex is null)
            throw new InvalidOperationException("ArrayBuffer is not shared.");
        var waiter = new SharedWaiter(realm.Engine.Options.SharedWaiterControllerFactory.CreateController(realm));
        lock (GetSharedSyncRoot())
        {
            var waitersByByteIndex = sharedStorage is not null
                ? sharedStorage.WaitersByByteIndex
                : externalSharedWaitersByByteIndex!;
            if (!waitersByByteIndex.TryGetValue(byteIndex, out var waiters))
            {
                waiters = [];
                waitersByByteIndex[byteIndex] = waiters;
            }

            waiters.Add(waiter);
        }

        return waiter;
    }

    internal SharedWaiter AddSharedWaiterLocked(JsRealm realm, uint byteIndex)
    {
        if (sharedStorage is null && externalSharedWaitersByByteIndex is null)
            throw new InvalidOperationException("ArrayBuffer is not shared.");
        var waitersByByteIndex = sharedStorage is not null
            ? sharedStorage.WaitersByByteIndex
            : externalSharedWaitersByByteIndex!;
        if (!waitersByByteIndex.TryGetValue(byteIndex, out var waiters))
        {
            waiters = [];
            waitersByByteIndex[byteIndex] = waiters;
        }

        var waiter = new SharedWaiter(realm.Engine.Options.SharedWaiterControllerFactory.CreateController(realm));
        waiters.Add(waiter);
        return waiter;
    }

    internal void RemoveSharedWaiter(uint byteIndex, SharedWaiter waiter)
    {
        if (sharedStorage is null && externalSharedWaitersByByteIndex is null)
            throw new InvalidOperationException("ArrayBuffer is not shared.");
        lock (GetSharedSyncRoot())
        {
            var waitersByByteIndex = sharedStorage is not null
                ? sharedStorage.WaitersByByteIndex
                : externalSharedWaitersByByteIndex!;
            if (!waitersByByteIndex.TryGetValue(byteIndex, out var waiters))
                return;
            waiters.Remove(waiter);
            if (waiters.Count == 0)
                waitersByByteIndex.Remove(byteIndex);
        }
    }

    internal int NotifySharedWaiters(uint byteIndex, int count)
    {
        if (sharedStorage is null && externalSharedWaitersByByteIndex is null)
            throw new InvalidOperationException("ArrayBuffer is not shared.");
        List<SharedWaiter>? waitersToWake = null;
        lock (GetSharedSyncRoot())
        {
            var waitersByByteIndex = sharedStorage is not null
                ? sharedStorage.WaitersByByteIndex
                : externalSharedWaitersByByteIndex!;
            if (!waitersByByteIndex.TryGetValue(byteIndex, out var waiters) || waiters.Count == 0)
                return 0;

            var wakeCount = count < 0 ? waiters.Count : Math.Min(count, waiters.Count);
            waitersToWake = new(wakeCount);
            for (var i = 0; i < wakeCount; i++)
            {
                var waiter = waiters[i];
                if (waiter.TryNotify())
                    waitersToWake.Add(waiter);
            }

            if (wakeCount == waiters.Count)
                waitersByByteIndex.Remove(byteIndex);
            else
                waiters.RemoveRange(0, wakeCount);
        }

        for (var i = 0; i < waitersToWake.Count; i++)
            waitersToWake[i].Complete();
        return waitersToWake.Count;
    }

    private void CopyBytesFromShared(byte[] source, uint targetByteIndex, uint sourceByteIndex, uint byteCount)
    {
        if (sharedStorage is not null)
            lock (sharedStorage.SyncRoot)
            {
                EnsureWritableRange(sharedStorage, targetByteIndex, checked((int)byteCount));
                Array.Copy(source, (int)sourceByteIndex, sharedStorage.Bytes, (int)targetByteIndex, (int)byteCount);
                return;
            }

        EnsureWritableRange(targetByteIndex, checked((int)byteCount));
        Array.Copy(source, (int)sourceByteIndex, bytes, (int)targetByteIndex, (int)byteCount);
    }

    private void CopyBytesFromLocal(byte[] source, uint targetByteIndex, uint sourceByteIndex, uint byteCount)
    {
        if (sharedStorage is not null)
            lock (sharedStorage.SyncRoot)
            {
                EnsureWritableRange(sharedStorage, targetByteIndex, checked((int)byteCount));
                Array.Copy(source, (int)sourceByteIndex, sharedStorage.Bytes, (int)targetByteIndex, (int)byteCount);
                return;
            }

        EnsureWritableRange(targetByteIndex, checked((int)byteCount));
        Array.Copy(source, (int)sourceByteIndex, bytes, (int)targetByteIndex, (int)byteCount);
    }

    private void EnsureReadableRange(uint byteIndex, int size)
    {
        if (IsDetached || byteIndex > ByteLength || size > ByteLength - byteIndex)
            throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");
    }

    private void EnsureWritableRange(uint byteIndex, int size)
    {
        if (IsDetached || byteIndex > ByteLength || size > ByteLength - byteIndex)
            throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");
    }

    private static void EnsureReadableRange(SharedBufferStorage storage, uint byteIndex, int size)
    {
        var byteLength = (uint)storage.Bytes.Length;
        if (byteIndex > byteLength || size > byteLength - byteIndex)
            throw new JsRuntimeException(JsErrorKind.TypeError, "SharedArrayBuffer is out of bounds");
    }

    private static void EnsureWritableRange(SharedBufferStorage storage, uint byteIndex, int size)
    {
        var byteLength = (uint)storage.Bytes.Length;
        if (byteIndex > byteLength || size > byteLength - byteIndex)
            throw new JsRuntimeException(JsErrorKind.TypeError, "SharedArrayBuffer is out of bounds");
    }

    public interface IExternalBufferBackingStore
    {
        int ByteLength { get; }
        IntPtr Pointer { get; }
        object SyncRoot { get; }
        Span<byte> GetSpan();
    }

    public sealed class DelegateExternalBufferBackingStore(
        Func<Span<byte>> getSpan,
        Func<IntPtr> getPointer,
        object syncRoot)
        : IExternalBufferBackingStore
    {
        private readonly Func<IntPtr> getPointer = getPointer ?? throw new ArgumentNullException(nameof(getPointer));
        private readonly Func<Span<byte>> getSpan = getSpan ?? throw new ArgumentNullException(nameof(getSpan));

        public int ByteLength => getSpan().Length;

        public IntPtr Pointer => getPointer();

        public object SyncRoot { get; } = syncRoot ?? throw new ArgumentNullException(nameof(syncRoot));

        public Span<byte> GetSpan()
        {
            return getSpan();
        }
    }

    internal interface ISharedWaiterController : IDisposable
    {
        void ArmAsyncTimeout(SharedWaiter waiter, TimeSpan? timeout);
        bool Wait(SharedWaiter waiter, TimeSpan? timeout);
    }

    internal sealed class SharedBufferStorage
    {
        public byte[] Bytes;

        public SharedBufferStorage(uint byteLength, uint? maxByteLength)
        {
            if (maxByteLength.HasValue && maxByteLength.Value < byteLength)
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array buffer length");

            Bytes = new byte[byteLength];
            MaxByteLength = maxByteLength;
        }

        public uint? MaxByteLength { get; }
        public object SyncRoot { get; } = new();
        public Dictionary<uint, List<SharedWaiter>> WaitersByByteIndex { get; } = new();
    }

    internal sealed class SharedWaiter(ISharedWaiterController controller) : IDisposable
    {
        private int signalState;

        public ManualResetEventSlim Event { get; } = new(false);
        public Action<object?>? Continuation { get; set; }
        public object? ContinuationState { get; set; }

        public bool Notified => Volatile.Read(ref signalState) == 1;

        public void Dispose()
        {
            controller.Dispose();
            Event.Dispose();
        }

        public bool TryNotify()
        {
            if (Interlocked.CompareExchange(ref signalState, 1, 0) != 0)
                return false;

            Event.Set();
            return true;
        }

        public bool TryTimeout()
        {
            if (Interlocked.CompareExchange(ref signalState, 2, 0) != 0)
                return false;

            Event.Set();
            return true;
        }

        public void Complete()
        {
            Continuation?.Invoke(ContinuationState);
        }

        public void ArmAsyncTimeout(TimeSpan? timeout)
        {
            controller.ArmAsyncTimeout(this, timeout);
        }

        public bool Wait(TimeSpan? timeout)
        {
            return controller.Wait(this, timeout);
        }
    }
}
