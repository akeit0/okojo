using Okojo.JavaScript.Execution;

namespace Okojo.JavaScript.Objects;

/// <summary>
/// Provides a public extension seam for host objects backed by a dynamic indexed collection.
/// </summary>
public abstract class JsIndexedObject : JsObject
{
    protected JsIndexedObject(JsRealm realm, JsObject? prototype = null)
        : base(realm)
    {
        if (prototype is not null && !TrySetPrototype(prototype))
            throw new InvalidOperationException("Could not set the indexed object's prototype.");
    }

    protected abstract int IndexedElementCount { get; }

    protected abstract bool TryGetIndexedValue(uint index, out JsValue value);

    protected virtual bool TryGetNamedValue(string name, out JsValue value)
    {
        value = JsValue.Undefined;
        return false;
    }

    internal override bool TryGetPropertyAtomWithReceiverValue(
        JsRealm realm,
        in JsValue receiverValue,
        int atom,
        out JsValue value,
        out SlotInfo slotInfo
    )
    {
        if (atom == AtomTable.IdLength)
        {
            value = Math.Max(0, IndexedElementCount);
            slotInfo = SlotInfo.Invalid;
            return true;
        }

        if (atom >= 0 && TryGetNamedValue(realm.Atoms.AtomToString(atom), out value))
        {
            slotInfo = SlotInfo.Invalid;
            return true;
        }

        return base.TryGetPropertyAtomWithReceiverValue(
            realm,
            receiverValue,
            atom,
            out value,
            out slotInfo
        );
    }

    internal override bool TryGetElementWithReceiver(
        JsRealm realm,
        JsObject receiver,
        uint index,
        out JsValue value
    )
    {
        if (TryGetIndexedValue(index, out value))
            return true;

        return base.TryGetElementWithReceiver(realm, receiver, index, out value);
    }

    internal override bool TryGetOwnElementDescriptor(uint index, out PropertyDescriptor descriptor)
    {
        if (TryGetIndexedValue(index, out var value))
        {
            descriptor = PropertyDescriptor.Data(
                value,
                writable: false,
                enumerable: true,
                configurable: true
            );
            return true;
        }

        return base.TryGetOwnElementDescriptor(index, out descriptor);
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(
        JsRealm realm,
        int atom,
        out PropertyDescriptor descriptor,
        bool needDescriptor = true
    )
    {
        if (atom >= 0 && TryGetNamedValue(realm.Atoms.AtomToString(atom), out var value))
        {
            descriptor = needDescriptor
                ? PropertyDescriptor.Data(
                    value,
                    writable: false,
                    enumerable: true,
                    configurable: true
                )
                : default;
            return true;
        }

        return base.TryGetOwnNamedPropertyDescriptorAtom(
            realm,
            atom,
            out descriptor,
            needDescriptor
        );
    }

    internal override void CollectOwnElementIndices(List<uint> indicesOut, bool enumerableOnly)
    {
        _ = enumerableOnly;
        var count = Math.Max(0, IndexedElementCount);
        for (var index = 0; index < count; index++)
            indicesOut.Add((uint)index);
        base.CollectOwnElementIndices(indicesOut, enumerableOnly);
    }
}
