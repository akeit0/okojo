using System.Globalization;
using System.Runtime.InteropServices;

namespace Okojo.JavaScript.Objects;

public sealed class JsArgumentsObject : JsObject
{
    private readonly JsContext? context;
    private readonly bool mapped;
    private readonly int[]? mappedSlots;
    private uint argumentCount;
    private HashSet<uint>? deletedElements;
    private HashSet<uint>? unmappedElements;
    private JsValue[] values;

    internal bool TryGetPackedValues(out ReadOnlySpan<JsValue> packed)
    {
        if (
            mappedSlots is null or { Length: 0 }
            && deletedElements is null
            && IndexedProperties is null
            && TryGetOwnNamedPropertyDescriptorAtom(Shape.Owner, IdLength, out var length)
            && !length.IsAccessor
            && length.Value.IsInt32
            && (uint)length.Value.Int32Value <= Math.Min(argumentCount, (uint)values.Length)
        )
        {
            packed = values.AsSpan(0, length.Value.Int32Value);
            return true;
        }

        packed = default;
        return false;
    }

    internal JsArgumentsObject(
        JsRealm realm,
        ReadOnlySpan<JsValue> args,
        bool mapped,
        int[]? mappedSlots,
        JsContext? context
    )
        : base(realm.GetArgumentsObjectShape(mapped))
    {
        values = args.ToArray();
        argumentCount = (uint)values.Length;
        this.mapped = mapped;
        this.mappedSlots = mappedSlots;
        this.context = context;
        Prototype = realm.ObjectPrototype;
        SlotsArray[0] = JsValue.FromInt32(values.Length);
        SlotsArray[1] = realm.ArrayPrototypeValuesFunction;
        if (!mapped)
        {
            JsValue throwTypeError = realm.ThrowTypeErrorIntrinsic;
            SlotsArray[2] = throwTypeError;
            SlotsArray[3] = throwTypeError;
        }
    }

    internal override bool TryGetElementWithReceiver(
        JsRealm realm,
        JsObject receiver,
        uint index,
        out JsValue value
    )
    {
        if (
            IndexedProperties is not null
            && IndexedProperties.TryGetValue(index, out var descriptor)
        )
        {
            if (descriptor.IsAccessor)
            {
                var getter = descriptor.Getter;
                if (getter is null)
                {
                    value = JsValue.Undefined;
                    return true;
                }

                value = InvokeAccessorFunction(
                    realm,
                    receiver,
                    getter,
                    ReadOnlySpan<JsValue>.Empty
                );
                return true;
            }

            value = IsMappedIndex(index) ? GetMappedIndexValue(index) : descriptor.Value;
            return true;
        }

        if (IsVirtualDeleted(index))
        {
            if (Prototype is not null)
                return Prototype.TryGetElementWithReceiver(realm, receiver, index, out value);
            value = JsValue.Undefined;
            return false;
        }

        if (IsMappedIndex(index))
        {
            var ctx = context;
            if (ctx is not null)
            {
                value = ctx.Slots[mappedSlots![(int)index]];
                return true;
            }
        }

        if (index < (uint)values.Length)
        {
            value = values[index];
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    internal override bool SetElementWithReceiver(
        JsRealm realm,
        JsObject receiver,
        uint index,
        JsValue value
    )
    {
        if (!ReferenceEquals(this, receiver))
            return base.SetElementWithReceiver(realm, receiver, index, value);

        if (
            IndexedProperties is not null
            && IndexedProperties.TryGetValue(index, out var existingDescriptor)
        )
        {
            if (existingDescriptor.IsAccessor)
            {
                if (existingDescriptor.Setter is null)
                    return false;
                var arg = MemoryMarshal.CreateReadOnlySpan(in value, 1);
                _ = InvokeAccessorFunction(realm, receiver, existingDescriptor.Setter, arg);
                return true;
            }

            if (!existingDescriptor.Writable)
                return false;
            IndexedProperties[index] = new(value, null, existingDescriptor.Flags);
            EnsureCapacity(index + 1);
            values[index] = value;
            if (IsMappedIndex(index))
            {
                var ctx = context;
                if (ctx is not null)
                    ctx.Slots[mappedSlots![(int)index]] = value;
            }

            return true;
        }

        EnsureCapacity(index + 1);
        values[index] = value;
        deletedElements?.Remove(index);

        if (IsMappedIndex(index))
        {
            var ctx = context;
            if (ctx is not null)
                ctx.Slots[mappedSlots![(int)index]] = value;
        }

        return true;
    }

    internal override bool TrySetOwnElement(uint index, JsValue value, out bool hadOwnElement)
    {
        if (
            IndexedProperties is not null
            && IndexedProperties.TryGetValue(index, out var existingDescriptor)
        )
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
            EnsureCapacity(index + 1);
            values[index] = value;
            if (IsMappedIndex(index))
            {
                var ctx = context;
                if (ctx is not null)
                    ctx.Slots[mappedSlots![(int)index]] = value;
            }

            return true;
        }

        if (!IsVirtualPresent(index))
        {
            hadOwnElement = false;
            return false;
        }

        hadOwnElement = true;
        EnsureCapacity(index + 1);
        values[index] = value;
        deletedElements?.Remove(index);
        if (IsMappedIndex(index))
        {
            var ctx = context;
            if (ctx is not null)
                ctx.Slots[mappedSlots![(int)index]] = value;
        }

        return true;
    }

    internal override bool TryGetOwnElementDescriptor(uint index, out PropertyDescriptor descriptor)
    {
        if (IndexedProperties is not null && IndexedProperties.TryGetValue(index, out descriptor))
        {
            if (!descriptor.IsAccessor && IsMappedIndex(index))
                descriptor = new(GetMappedIndexValue(index), null, descriptor.Flags);
            return true;
        }

        if (IsVirtualPresent(index))
        {
            var v = IsMappedIndex(index) ? GetMappedIndexValue(index) : values[index];

            descriptor = PropertyDescriptor.OpenData(v);
            return true;
        }

        descriptor = default;
        return false;
    }

    public override bool DeleteElement(uint index)
    {
        if (
            IndexedProperties is not null
            && IndexedProperties.TryGetValue(index, out var descriptor)
        )
        {
            if (!descriptor.Configurable)
                return false;
            IndexedProperties.Remove(index);
            (deletedElements ??= new()).Add(index);
            if (IsMappedIndex(index))
                (unmappedElements ??= new()).Add(index);
            return true;
        }

        if (!IsVirtualPresent(index))
            return true;

        (deletedElements ??= new()).Add(index);
        if (IsMappedIndex(index))
            (unmappedElements ??= new()).Add(index);
        return true;
    }

    internal override void DefineElementDescriptor(uint index, in PropertyDescriptor descriptor)
    {
        var wasMapped = IsMappedIndex(index);
        if (!descriptor.IsAccessor && wasMapped)
        {
            var ctx = context;
            if (ctx is not null)
                ctx.Slots[mappedSlots![(int)index]] = descriptor.Value;
        }

        var shouldUnmap = wasMapped && (descriptor.IsAccessor || !descriptor.Writable);

        var openData =
            !descriptor.IsAccessor
            && descriptor.Writable
            && descriptor.Enumerable
            && descriptor.Configurable;
        var canStayMappedOpenData = openData && IsMappedIndex(index) && !IsVirtualDeleted(index);

        if (canStayMappedOpenData)
        {
            EnsureCapacity(index + 1);
            values[index] = descriptor.Value;
            var ctx = context;
            if (ctx is not null)
                ctx.Slots[mappedSlots![(int)index]] = descriptor.Value;
            deletedElements?.Remove(index);
        }
        else
        {
            (IndexedProperties ??= new())[index] = descriptor;
            if (shouldUnmap)
                (unmappedElements ??= new()).Add(index);
            // Descriptor-defined argument element should not fall back to virtual backing
            // after deletion; keep virtual storage hidden for this index.
            (deletedElements ??= new()).Add(index);
            if (!descriptor.IsAccessor)
            {
                EnsureCapacity(index + 1);
                values[index] = descriptor.Value;
            }
        }

        var next = index + 1;
        if (next > argumentCount)
            argumentCount = next;
    }

    internal override void CollectOwnElementIndices(List<uint> indicesOut, bool enumerableOnly)
    {
        var seen = new HashSet<uint>();
        for (uint i = 0; i < argumentCount && i < (uint)values.Length; i++)
        {
            if (
                IndexedProperties is not null
                && IndexedProperties.TryGetValue(i, out var indexedDescriptor)
            )
            {
                if ((!enumerableOnly || indexedDescriptor.Enumerable) && seen.Add(i))
                    indicesOut.Add(i);
                continue;
            }

            if (!IsVirtualDeleted(i) && seen.Add(i))
                indicesOut.Add(i);
        }

        if (IndexedProperties is null)
            return;

        foreach (var kvp in IndexedProperties)
        {
            if (kvp.Key < argumentCount && kvp.Key < (uint)values.Length)
                continue;
            if (enumerableOnly && !kvp.Value.Enumerable)
                continue;
            if (seen.Add(kvp.Key))
                indicesOut.Add(kvp.Key);
        }
    }

    internal override void CollectForInEnumerableStringAtomKeys(
        JsRealm realm,
        HashSet<string> visited,
        List<string> enumerableKeysOut
    )
    {
        for (uint i = 0; i < argumentCount && i < (uint)values.Length; i++)
        {
            if (
                IndexedProperties is not null
                && IndexedProperties.TryGetValue(i, out var indexedDescriptor)
            )
            {
                if (!indexedDescriptor.Enumerable)
                    continue;
            }
            else if (IsVirtualDeleted(i))
            {
                continue;
            }

            var key = i.ToString(CultureInfo.InvariantCulture);
            if (visited.Add(key))
                enumerableKeysOut.Add(key);
        }

        if (IndexedProperties is not null)
        {
            var extraIndices = new List<uint>();
            foreach (var kvp in IndexedProperties)
            {
                if (kvp.Key < argumentCount && kvp.Key < (uint)values.Length)
                    continue;
                if (!kvp.Value.Enumerable)
                    continue;
                extraIndices.Add(kvp.Key);
            }

            extraIndices.Sort();
            for (var i = 0; i < extraIndices.Count; i++)
            {
                var key = extraIndices[i].ToString(CultureInfo.InvariantCulture);
                if (visited.Add(key))
                    enumerableKeysOut.Add(key);
            }
        }

        base.CollectForInEnumerableStringAtomKeys(realm, visited, enumerableKeysOut);
    }

    internal override void FreezeDataProperties()
    {
        for (uint i = 0; i < argumentCount && i < (uint)values.Length; i++)
        {
            if (IsVirtualDeleted(i))
                continue;
            if (IndexedProperties is not null && IndexedProperties.ContainsKey(i))
                continue;

            JsValue v;
            if (IsMappedIndex(i))
            {
                var ctx = context;
                v = ctx is null ? values[i] : ctx.Slots[(int)i];
            }
            else
            {
                v = values[i];
            }

            (IndexedProperties ??= new())[i] = PropertyDescriptor.Data(v, false, true);
            (deletedElements ??= new()).Add(i);
            if (IsMappedIndex(i))
                (unmappedElements ??= new()).Add(i);
            values[i] = v;
        }

        base.FreezeDataProperties();
    }

    internal override bool AreAllOwnPropertiesFrozen()
    {
        for (uint i = 0; i < argumentCount && i < (uint)values.Length; i++)
        {
            if (!IsVirtualPresent(i))
                continue;
            if (IndexedProperties is not null && IndexedProperties.ContainsKey(i))
                continue;
            return false;
        }

        return base.AreAllOwnPropertiesFrozen();
    }

    private bool IsMappedIndex(uint index)
    {
        if (
            !mapped
            || mappedSlots is null
            || index >= (uint)mappedSlots.Length
            || mappedSlots[(int)index] < 0
        )
            return false;
        return unmappedElements is null || !unmappedElements.Contains(index);
    }

    private bool IsVirtualDeleted(uint index)
    {
        return deletedElements is not null && deletedElements.Contains(index);
    }

    private bool IsVirtualPresent(uint index)
    {
        return index < argumentCount && index < (uint)values.Length && !IsVirtualDeleted(index);
    }

    private JsValue GetMappedIndexValue(uint index)
    {
        var ctx = context;
        return ctx is null ? values[index] : ctx.Slots[mappedSlots![(int)index]];
    }

    private void EnsureCapacity(uint needed)
    {
        if (needed <= (uint)values.Length)
            return;
        var target = values.Length == 0 ? 4 : values.Length;
        while ((uint)target < needed)
            target <<= 1;
        Array.Resize(ref values, target);
    }
}
