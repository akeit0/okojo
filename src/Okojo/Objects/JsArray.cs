using System.Globalization;
using System.Runtime.InteropServices;

namespace Okojo.Objects;

public sealed class JsArray : JsObject
{
    private const int DenseInitialCapacity = 4;

    private const int DenseToSparseGapThreshold = 256;

    // Dense-first array storage. When indices become too sparse, we fall back to sparse map storage.
    internal JsValue[]? Dense;
    private bool lengthWritable = true;

    public JsArray(JsRealm realm) : base(realm)
    {
        Dense = Array.Empty<JsValue>();
        Length = 0;
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        Prototype = realm.Intrinsics is null ? realm.ObjectPrototype : realm.ArrayPrototype;
    }

    public uint Length { get; private set; }

    internal void SetLength(uint length)
    {
        this.Length = length;
    }

    internal JsValue[] InitializeDenseElementsNoCollision(int length)
    {
        Dense = length == 0 ? Array.Empty<JsValue>() : new JsValue[length];
        this.Length = (uint)length;
        return Dense;
    }

    internal override bool SetElementWithReceiver(JsRealm realm, JsObject receiver, uint index, JsValue value)
    {
        if (!ReferenceEquals(this, receiver))
            return base.SetElementWithReceiver(realm, receiver, index, value);

        if (!lengthWritable && index >= Length)
            return false;

        if (!IsExtensible && !HasOwnElement(index))
            return false;

        if (IndexedProperties is not null && IndexedProperties.TryGetValue(index, out var existingDescriptor))
        {
            if (existingDescriptor.IsAccessor)
            {
                if (existingDescriptor.Setter is not null)
                {
                    var arg = MemoryMarshal.CreateReadOnlySpan(in value, 1);
                    _ = InvokeAccessorFunction(realm, receiver, existingDescriptor.Setter, arg);
                }

                return existingDescriptor.Setter is not null;
            }

            if (!existingDescriptor.Writable)
                return false;

            IndexedProperties[index] = new(value, null, existingDescriptor.Flags);
            var updateNext = index + 1;
            if (updateNext > Length)
                Length = updateNext;
            return true;
        }

        if (Prototype is not null &&
            TrySetInheritedElementDescriptor(Shape.Owner, this, index, value, out var inheritedHandled))
            return inheritedHandled;

        if (Dense is not null)
        {
            if (ShouldFallbackToSparse(index))
            {
                ConvertDenseToSparse();
            }
            else
            {
                EnsureDenseCapacity(index);
                Dense[index] = value;
            }
        }

        if (Dense is null) (IndexedProperties ??= new())[index] = PropertyDescriptor.OpenData(value);

        var next = index + 1;
        if (next > Length)
            Length = next;
        return true;
    }

    internal override bool TrySetOwnElement(uint index, JsValue value, out bool hadOwnElement)
    {
        if (IndexedProperties is not null && IndexedProperties.TryGetValue(index, out var existingDescriptor))
        {
            hadOwnElement = true;
            if (existingDescriptor.IsAccessor)
            {
                if (existingDescriptor.Setter is null)
                    return false;

                var arg = MemoryMarshal.CreateReadOnlySpan(in value, 1);
                _ = InvokeAccessorFunction(Shape.Owner, this, existingDescriptor.Setter, arg);
                return true;
            }

            if (!existingDescriptor.Writable)
                return false;

            IndexedProperties[index] = new(value, null, existingDescriptor.Flags);
            var next = index + 1;
            if (next > Length)
                Length = next;
            return true;
        }

        var dense = Dense;
        if (dense is not null && index < (uint)dense.Length)
        {
            ref var slot = ref dense[index];
            if (!slot.IsTheHole)
            {
                hadOwnElement = true;
                slot = value;
                var next = index + 1;
                if (next > Length)
                    Length = next;
                return true;
            }
        }

        hadOwnElement = false;
        return false;
    }

    internal bool CanDefineElementAtIndex(uint index)
    {
        if (!lengthWritable && index >= Length)
            return false;
        if (!IsExtensible && !HasOwnElement(index))
            return false;
        return true;
    }

    internal override bool TryGetElementWithReceiver(JsRealm realm, JsObject receiver, uint index, out JsValue value)
    {
        if (IndexedProperties is not null && IndexedProperties.TryGetValue(index, out var descriptor))
        {
            if (descriptor.IsAccessor)
            {
                var getter = descriptor.Getter;
                if (getter is null)
                {
                    value = JsValue.Undefined;
                    return true;
                }

                value = InvokeAccessorFunction(realm, receiver, getter, ReadOnlySpan<JsValue>.Empty);
                return true;
            }

            value = descriptor.Value;
            return true;
        }

        if (Dense is not null)
            if (index < (uint)Dense.Length)
            {
                var v = Dense[index];
                if (!v.IsTheHole)
                {
                    value = v;
                    return true;
                }
            }

        if (Prototype is not null)
            return Prototype.TryGetElementWithReceiver(realm, receiver, index, out value);

        value = JsValue.Undefined;
        return false;
    }

    public override bool DeleteElement(uint index)
    {
        if (IndexedProperties is not null && IndexedProperties.TryGetValue(index, out var descriptor))
        {
            if (!descriptor.Configurable)
                return false;
            IndexedProperties.Remove(index);
            return true;
        }

        if (Dense is not null)
        {
            if (index < (uint)Dense.Length)
                Dense[index] = JsValue.TheHole;
        }

        return true;
    }

    internal override bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value, out SlotInfo slotInfo)
    {
        if (atom == IdLength)
        {
            value = Length <= int.MaxValue ? JsValue.FromInt32((int)Length) : new((double)Length);
            slotInfo = SlotInfo.Invalid;
            return true;
        }

        return base.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out slotInfo);
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(JsRealm realm, int atom,
        out PropertyDescriptor descriptor,
        bool needDescriptor = true)
    {
        if (atom == IdLength)
        {
            if (!needDescriptor)
            {
                descriptor = default;
                return true;
            }

            descriptor = PropertyDescriptor.Data(
                Length <= int.MaxValue ? JsValue.FromInt32((int)Length) : new((double)Length),
                lengthWritable);
            return true;
        }

        return base.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out descriptor, needDescriptor);
    }

    internal override bool DeletePropertyAtom(JsRealm realm, int atom)
    {
        if (atom == IdLength)
            return false;

        return base.DeletePropertyAtom(realm, atom);
    }

    internal override bool SetPropertyAtomWithReceiver(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        if (atom == IdLength && ReferenceEquals(this, receiver))
        {
            slotInfo = SlotInfo.Invalid;
            if (!lengthWritable)
                return false;
            if (!TryConvertToArrayLength(realm, value, out var newLength))
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid array length",
                    "ARRAY_LENGTH_INVALID");
            return TrySetLengthCore(newLength, false);
        }

        return base.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out slotInfo);
    }

    internal override void CollectOwnNamedPropertyAtoms(JsRealm realm, List<int> atomsOut, bool enumerableOnly)
    {
        if (!enumerableOnly)
            atomsOut.Add(IdLength);
        base.CollectOwnNamedPropertyAtoms(realm, atomsOut, enumerableOnly);
    }

    internal bool TryDefineLengthDescriptor(
        bool hasValue,
        in JsValue value,
        bool hasWritable,
        bool requestedWritable,
        bool hasEnumerable,
        bool requestedEnumerable,
        bool hasConfigurable,
        bool requestedConfigurable,
        out bool isRangeError)
    {
        isRangeError = false;

        var newLength = Length;
        var deferWritableFalse = hasWritable && !requestedWritable;
        if (hasValue)
            if (!TryConvertToArrayLength(Shape.Owner, value, out newLength))
            {
                isRangeError = true;
                return false;
            }

        if (hasConfigurable && requestedConfigurable)
            return false;
        if (hasEnumerable && requestedEnumerable)
            return false;
        if (!lengthWritable && hasWritable && requestedWritable)
            return false;
        if (hasValue)
        {
            if (!TrySetLengthCore(newLength, deferWritableFalse))
                return false;
        }
        else if (deferWritableFalse)
        {
            // No [[Value]] update; only writable:false transition.
            lengthWritable = false;
        }

        return true;
    }

    private bool ShouldFallbackToSparse(uint index)
    {
        if (Dense is null) return false;
        var denseLen = Dense.Length;
        if (denseLen == 0) return index > DenseToSparseGapThreshold;
        return index > (uint)(denseLen + DenseToSparseGapThreshold);
    }

    private void EnsureDenseCapacity(uint index)
    {
        if (Dense is null) return;
        if (index < (uint)Dense.Length) return;

        var needed = (int)index + 1;
        var capacity = Dense.Length == 0 ? DenseInitialCapacity : Dense.Length;
        while (capacity < needed)
            capacity <<= 1;

        var oldLength = Dense.Length;
        Array.Resize(ref Dense, capacity);
        for (var i = oldLength; i < Dense.Length; i++)
            Dense[i] = JsValue.TheHole;
    }

    private void ConvertDenseToSparse()
    {
        if (Dense is null) return;
        IndexedProperties ??= new();
        for (uint i = 0; i < Dense.Length; i++)
        {
            var v = Dense[i];
            if (!v.IsTheHole)
                IndexedProperties[i] = PropertyDescriptor.OpenData(v);
        }

        Dense = null;
    }

    internal override bool TryGetOwnElementDescriptor(uint index, out PropertyDescriptor descriptor)
    {
        if (IndexedProperties is not null && IndexedProperties.TryGetValue(index, out descriptor))
            return true;

        if (Dense is not null && index < (uint)Dense.Length)
        {
            var v = Dense[index];
            if (!v.IsTheHole)
            {
                descriptor = PropertyDescriptor.OpenData(v);
                return true;
            }
        }

        descriptor = default;
        return false;
    }

    internal override void DefineElementDescriptor(uint index, in PropertyDescriptor descriptor)
    {
        if (Dense is not null &&
            !descriptor.IsAccessor &&
            descriptor.Writable &&
            descriptor.Enumerable &&
            descriptor.Configurable &&
            !ShouldFallbackToSparse(index))
        {
            EnsureDenseCapacity(index);
            Dense[index] = descriptor.Value;
            IndexedProperties?.Remove(index);
        }
        else
        {
            if (Dense is not null && index < (uint)Dense.Length)
                Dense[index] = JsValue.TheHole;
            (IndexedProperties ??= new())[index] = descriptor;
        }

        var next = index + 1;
        if (next > Length)
            Length = next;
    }

    internal void DefineOwnOpenElementSparse(uint index, JsValue value)
    {
        if (Dense is not null && index < (uint)Dense.Length)
            Dense[index] = JsValue.TheHole;

        (IndexedProperties ??= new())[index] = PropertyDescriptor.OpenData(value);

        var next = index + 1;
        if (next > Length)
            Length = next;
    }

    internal void InitializeLiteralElement(uint index, JsValue value)
    {
        if (Dense is not null && !ShouldFallbackToSparse(index))
        {
            EnsureDenseCapacity(index);
            Dense[index] = value;
        }
        else if (value.IsTheHole)
        {
            if (Dense is not null && index < (uint)Dense.Length)
                Dense[index] = JsValue.TheHole;
            IndexedProperties?.Remove(index);
        }
        else
        {
            if (Dense is not null && index < (uint)Dense.Length)
                Dense[index] = JsValue.TheHole;
            (IndexedProperties ??= new())[index] =
                PropertyDescriptor.OpenData(value);
        }

        var next = index + 1;
        if (next > Length)
            Length = next;
    }

    private bool TrySetLengthCore(uint newLength, bool deferWritableFalse)
    {
        if (!lengthWritable && newLength != Length)
            return false;

        if (newLength == Length)
        {
            if (deferWritableFalse)
                lengthWritable = false;
            return true;
        }

        if (newLength >= Length)
        {
            Length = newLength;
            if (deferWritableFalse)
                lengthWritable = false;
            return true;
        }

        if (!lengthWritable)
            return false;

        var keysToDelete = CollectOwnIndicesAtOrAbove(newLength);
        for (var i = 0; i < keysToDelete.Count; i++)
        {
            var k = keysToDelete[i];

            if (DeleteElement(k))
                continue;

            // Deletion failed at k: length becomes k+1.
            Length = k + 1;
            if (deferWritableFalse)
                lengthWritable = false;
            return false;
        }

        Length = newLength;
        if (deferWritableFalse)
            lengthWritable = false;
        return true;
    }

    private List<uint> CollectOwnIndicesAtOrAbove(uint newLength)
    {
        var keys = new List<uint>();

        if (Dense is not null)
        {
            var upper = Math.Min(Length, (uint)Dense.Length);
            for (var i = upper; i > newLength;)
            {
                i--;
                if (!Dense[i].IsTheHole)
                    keys.Add(i);
            }
        }

        if (IndexedProperties is not null && IndexedProperties.Count != 0)
            foreach (var key in IndexedProperties.Keys)
                if (key >= newLength)
                    keys.Add(key);

        if (keys.Count > 1)
            keys.Sort(static (a, b) => b.CompareTo(a));

        return keys;
    }

    private static bool TryConvertToArrayLength(JsRealm realm, in JsValue value, out uint result)
    {
        var uint32Source = realm.ToNumber(value);
        var len = ToUint32(uint32Source);

        var n = realm.ToNumber(value);
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (double.IsNaN(n) || double.IsInfinity(n) || n < 0 || n != len)
        {
            result = 0;
            return false;
        }

        result = len;
        return true;
    }

    private static uint ToUint32(double value)
    {
        if (double.IsNaN(value) || value == 0 || double.IsInfinity(value))
            return 0;

        var positive = Math.Sign(value) * Math.Floor(Math.Abs(value));
        var int32Bit = positive % 4294967296d;
        if (int32Bit < 0)
            int32Bit += 4294967296d;
        return (uint)int32Bit;
    }

    internal override void FreezeDataProperties()
    {
        if (Dense is not null && Dense.Length != 0)
        {
            IndexedProperties ??= new();
            for (uint i = 0; i < Dense.Length; i++)
            {
                var v = Dense[i];
                if (v.IsTheHole)
                    continue;
                IndexedProperties[i] = PropertyDescriptor.Data(
                    v,
                    false,
                    true);
                Dense[i] = JsValue.TheHole;
            }
        }

        base.FreezeDataProperties();
        lengthWritable = false;
    }

    internal override bool AreAllOwnPropertiesFrozen()
    {
        return !lengthWritable && base.AreAllOwnPropertiesFrozen();
    }

    internal override void CollectForInEnumerableStringAtomKeys(JsRealm realm, HashSet<string> visited,
        List<string> enumerableKeysOut)
    {
        if (Dense is not null && Dense.Length != 0)
            for (uint i = 0; i < Dense.Length; i++)
            {
                if (Dense[i].IsTheHole)
                    continue;
                if (IndexedProperties is not null && IndexedProperties.TryGetValue(i, out _))
                    continue;
                var key = i.ToString(CultureInfo.InvariantCulture);
                if (visited.Add(key))
                    enumerableKeysOut.Add(key);
            }

        if (IndexedProperties is not null && IndexedProperties.Count != 0)
        {
            var keys = new uint[IndexedProperties.Count];
            var cursor = 0;
            foreach (var k in IndexedProperties.Keys)
                keys[cursor++] = k;
            Array.Sort(keys);
            for (var i = 0; i < keys.Length; i++)
            {
                if (!IndexedProperties.TryGetValue(keys[i], out var descriptor))
                    continue;
                if (!descriptor.Enumerable)
                    continue;
                var key = keys[i].ToString(CultureInfo.InvariantCulture);
                if (visited.Add(key))
                    enumerableKeysOut.Add(key);
            }
        }

        base.CollectForInEnumerableStringAtomKeys(realm, visited, enumerableKeysOut);
    }

    internal override void CollectOwnElementIndices(List<uint> indicesOut, bool enumerableOnly)
    {
        if (Dense is not null && Dense.Length != 0)
            for (uint i = 0; i < Dense.Length; i++)
            {
                if (Dense[i].IsTheHole)
                    continue;
                if (IndexedProperties is not null && IndexedProperties.TryGetValue(i, out _))
                    continue;
                indicesOut.Add(i);
            }

        base.CollectOwnElementIndices(indicesOut, enumerableOnly);
    }
}
