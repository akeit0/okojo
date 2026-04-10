namespace Okojo.Objects;

public sealed class JsDataViewObject : JsObject
{
    private readonly bool lengthTracking;

    public JsDataViewObject(JsRealm realm, JsArrayBufferObject buffer, uint byteOffset, uint byteLength,
        bool lengthTracking = false, JsObject? prototype = null)
        : base(realm)
    {
        Buffer = buffer;
        StoredByteOffset = byteOffset;
        StoredByteLength = byteLength;
        this.lengthTracking = lengthTracking;
        Prototype = prototype ?? realm.ObjectPrototype;
    }

    public JsArrayBufferObject Buffer { get; }

    internal uint StoredByteOffset { get; }

    internal uint StoredByteLength { get; }

    public uint ByteOffset
    {
        get
        {
            _ = GetCurrentViewByteLength();
            return StoredByteOffset;
        }
    }

    public uint ByteLength => GetCurrentViewByteLength();

    internal uint GetViewByteIndex(uint offset, int elementSize)
    {
        var viewByteLength = GetCurrentViewByteLength();
        if (offset > viewByteLength || elementSize > viewByteLength - offset)
            throw new JsRuntimeException(JsErrorKind.RangeError, "Offset is outside the bounds of the DataView");

        return checked(StoredByteOffset + offset);
    }

    private uint GetCurrentViewByteLength()
    {
        if (Buffer.IsDetached)
            throw new JsRuntimeException(JsErrorKind.TypeError, "ArrayBuffer is detached");

        var bufferByteLength = Buffer.ByteLength;
        if (StoredByteOffset > bufferByteLength)
            throw new JsRuntimeException(JsErrorKind.TypeError, "DataView is out of bounds");

        var remaining = bufferByteLength - StoredByteOffset;
        if (lengthTracking)
            return remaining;
        if (StoredByteLength > remaining)
            throw new JsRuntimeException(JsErrorKind.TypeError, "DataView is out of bounds");
        return StoredByteLength;
    }
}
