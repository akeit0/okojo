using System.Globalization;

namespace Okojo.Objects;

public sealed class JsTypedArrayObject : JsObject
{
    public JsTypedArrayObject(JsRealm realm, uint length, JsObject? prototype = null)
        : this(realm, new(realm, length), 0, length, TypedArrayElementKind.Uint8, prototype)
    {
    }

    public JsTypedArrayObject(JsRealm realm, JsArrayBufferObject buffer, uint byteOffset, uint length,
        JsObject? prototype = null)
        : this(realm, buffer, byteOffset, length, TypedArrayElementKind.Uint8, prototype)
    {
    }

    internal JsTypedArrayObject(JsRealm realm, uint length, TypedArrayElementKind kind,
        JsObject? prototype = null)
        : this(realm, new(realm, checked((uint)(length * kind.GetBytesPerElement()))), 0, length,
            kind, prototype)
    {
    }

    internal JsTypedArrayObject(JsRealm realm, JsArrayBufferObject buffer, uint byteOffset, uint length,
        TypedArrayElementKind kind, JsObject? prototype = null)
        : this(realm, buffer, byteOffset, length, kind, false, prototype)
    {
    }

    internal JsTypedArrayObject(JsRealm realm, JsArrayBufferObject buffer, uint byteOffset, uint length,
        TypedArrayElementKind kind, bool lengthTracking, JsObject? prototype = null) : base(realm)
    {
        Buffer = buffer;
        StoredByteOffset = byteOffset;
        StoredLength = length;
        Kind = kind;
        IsLengthTracking = lengthTracking;
        Prototype = prototype ?? realm.Uint8ArrayPrototype;
    }

    public JsArrayBufferObject Buffer { get; }

    public uint Length
    {
        get
        {
            var bpe = (uint)Kind.GetBytesPerElement();
            var bufferByteLength = Buffer.ByteLength;
            if (bufferByteLength <= StoredByteOffset)
                return 0;
            var available = bufferByteLength - StoredByteOffset;
            if (IsLengthTracking)
                return available / bpe;
            var requestedBytes = checked(StoredLength * bpe);
            return requestedBytes <= available ? StoredLength : 0;
        }
    }

    public uint ByteLength => checked(Length * (uint)Kind.GetBytesPerElement());
    public uint ByteOffset => IsOutOfBounds ? 0u : StoredByteOffset;
    public int BytesPerElement => Kind.GetBytesPerElement();
    public string TypeName => Kind.GetConstructorName();
    internal TypedArrayElementKind Kind { get; }

    internal uint StoredLength { get; }

    internal uint StoredByteOffset { get; }

    internal bool IsLengthTracking { get; }

    internal bool IsOutOfBounds
    {
        get
        {
            if (Buffer.IsDetached)
                return true;

            var bufferByteLength = Buffer.ByteLength;
            if (bufferByteLength < StoredByteOffset)
                return true;

            if (IsLengthTracking)
                return false;

            var available = bufferByteLength - StoredByteOffset;
            var requestedBytes = checked(StoredLength * (uint)Kind.GetBytesPerElement());
            return requestedBytes > available;
        }
    }

    internal override bool TryGetElementWithReceiver(JsRealm realm, JsObject receiver, uint index, out JsValue value)
    {
        var effectiveLength = Length;
        if (index < effectiveLength)
        {
            value = Buffer.ReadTypedArrayElement(Realm, Kind,
                checked(StoredByteOffset + index * (uint)Kind.GetBytesPerElement()));
            return true;
        }

        if (Prototype is not null)
            return Prototype.TryGetElementWithReceiver(realm, receiver, index, out value);

        value = JsValue.Undefined;
        return false;
    }

    internal override bool SetElementWithReceiver(JsRealm realm, JsObject receiver, uint index, JsValue value)
    {
        if (!ReferenceEquals(this, receiver))
            return base.SetElementWithReceiver(realm, receiver, index, value);

        var normalized = TypedArrayElementKindInfo.NormalizeValue(Realm, Kind, value);
        return TrySetNormalizedElement(index, normalized);
    }

    internal override bool TrySetOwnElement(uint index, JsValue value, out bool hadOwnElement)
    {
        var effectiveLength = Length;
        if (index >= effectiveLength)
        {
            hadOwnElement = false;
            return false;
        }

        hadOwnElement = true;
        var normalized = TypedArrayElementKindInfo.NormalizeValue(Realm, Kind, value);
        return TrySetNormalizedElement(index, normalized);
    }

    internal override bool SetPropertyAtomWithReceiver(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        if (atom >= 0)
        {
            var text = realm.Atoms.AtomToString(atom);
            if (Intrinsics.TryGetCanonicalNumericIndexString(realm, JsValue.FromString(text), out var numericIndex))
            {
                slotInfo = SlotInfo.Invalid;
                if (ReferenceEquals(receiver, this))
                    return Intrinsics.SetCanonicalNumericIndexOnTypedArrayForSet(this, numericIndex, value);

                if (!Intrinsics.IsValidTypedArrayCanonicalNumericIndex(this, numericIndex))
                    return true;

                if (receiver is JsTypedArrayObject receiverTypedArray)
                {
                    if (!Intrinsics.IsValidTypedArrayCanonicalNumericIndex(receiverTypedArray, numericIndex))
                        return false;

                    receiverTypedArray.SetValidatedIntegerIndexedValue((uint)numericIndex, value);
                    return true;
                }

                if (numericIndex != Math.Truncate(numericIndex) || numericIndex < 0d || numericIndex > uint.MaxValue)
                    return true;

                return Intrinsics.OrdinarySetOwnWritableDataIndex(realm, receiver, (uint)numericIndex, value);
            }
        }

        return base.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out slotInfo);
    }

    internal bool TrySetNormalizedElement(uint index, in JsValue normalized)
    {
        var effectiveLength = Length;
        if (index >= effectiveLength)
            return true;

        Buffer.WriteNormalizedTypedArrayElement(Kind,
            checked(StoredByteOffset + index * (uint)Kind.GetBytesPerElement()),
            normalized);
        return true;
    }

    internal bool TryDefineIntegerIndexedValue(uint index, in JsValue value)
    {
        var normalized = TypedArrayElementKindInfo.NormalizeValue(Realm, Kind, value);
        var effectiveLength = Length;
        if (index >= effectiveLength)
            return false;

        Buffer.WriteNormalizedTypedArrayElement(Kind,
            checked(StoredByteOffset + index * (uint)Kind.GetBytesPerElement()),
            normalized);
        return true;
    }

    internal void SetValidatedIntegerIndexedValue(uint index, in JsValue value)
    {
        var normalized = TypedArrayElementKindInfo.NormalizeValue(Realm, Kind, value);
        var effectiveLength = Length;
        if (index >= effectiveLength)
            return;

        Buffer.WriteNormalizedTypedArrayElement(Kind,
            checked(StoredByteOffset + index * (uint)Kind.GetBytesPerElement()),
            normalized);
    }

    internal JsValue GetDirectElementValue(uint index)
    {
        return Buffer.ReadTypedArrayElement(Realm, Kind,
            checked(StoredByteOffset + index * (uint)Kind.GetBytesPerElement()));
    }

    public override bool DeleteElement(uint index)
    {
        if (index < Length)
            return false;
        return base.DeleteElement(index);
    }

    internal override void PreventExtensions()
    {
        if (Buffer.IsResizable || (Buffer.IsShared && IsLengthTracking))
            return;

        base.PreventExtensions();
    }

    internal override bool TryGetOwnElementDescriptor(uint index, out PropertyDescriptor descriptor)
    {
        if (index < Length)
        {
            descriptor = PropertyDescriptor.Data(
                Buffer.ReadTypedArrayElement(Realm, Kind,
                    checked(StoredByteOffset + index * (uint)Kind.GetBytesPerElement())),
                true,
                true,
                true);
            return true;
        }

        descriptor = default;
        return false;
    }

    internal override void CollectOwnElementIndices(List<uint> indicesOut, bool enumerableOnly)
    {
        var effectiveLength = Length;
        for (uint i = 0; i < effectiveLength; i++)
            indicesOut.Add(i);
        base.CollectOwnElementIndices(indicesOut, enumerableOnly);
    }

    internal override void CollectForInEnumerableStringAtomKeys(
        JsRealm realm,
        HashSet<string> visited,
        List<string> enumerableKeysOut)
    {
        var effectiveLength = Length;
        for (uint i = 0; i < effectiveLength; i++)
        {
            var key = i.ToString(CultureInfo.InvariantCulture);
            if (visited.Add(key))
                enumerableKeysOut.Add(key);
        }

        base.CollectForInEnumerableStringAtomKeys(realm, visited, enumerableKeysOut);
    }

    internal void CopyWithin(uint targetIndex, uint sourceIndex, uint elementCount)
    {
        if (elementCount == 0)
            return;

        var bytesPerElement = (uint)Kind.GetBytesPerElement();
        var targetByteIndex = checked(StoredByteOffset + targetIndex * bytesPerElement);
        var sourceByteIndex = checked(StoredByteOffset + sourceIndex * bytesPerElement);
        var byteCount = checked(elementCount * bytesPerElement);
        Buffer.CopyBytesWithin(targetByteIndex, sourceByteIndex, byteCount);
    }

    internal void Reverse()
    {
        uint left = 0;
        var right = Length;
        if (right <= 1)
            return;

        right--;
        while (left < right)
        {
            var leftByteIndex = checked(StoredByteOffset + left * (uint)Kind.GetBytesPerElement());
            var rightByteIndex = checked(StoredByteOffset + right * (uint)Kind.GetBytesPerElement());
            var leftValue = Buffer.ReadTypedArrayElement(Realm, Kind, leftByteIndex);
            var rightValue = Buffer.ReadTypedArrayElement(Realm, Kind, rightByteIndex);
            Buffer.WriteNormalizedTypedArrayElement(Kind, leftByteIndex, rightValue);
            Buffer.WriteNormalizedTypedArrayElement(Kind, rightByteIndex, leftValue);
            left++;
            right--;
        }
    }
}
