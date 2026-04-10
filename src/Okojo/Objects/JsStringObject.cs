using System.Globalization;

namespace Okojo.Objects;

public sealed class JsStringObject : JsObject
{
    private readonly JsString value;
    private string? flatValue;

    public JsStringObject(JsRealm realm, string value, JsObject? prototype = null)
        : this(realm, (JsString)value, prototype)
    {
    }

    public JsStringObject(JsRealm realm, JsString value, JsObject? prototype = null) : base(realm)
    {
        this.value = value;
        Prototype = prototype ?? realm.StringPrototype;
    }

    public JsString Value => value;

    private string GetFlatValue()
    {
        return flatValue ??= value.Flatten();
    }

    internal override bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value, out SlotInfo slotInfo)
    {
        if (atom == IdLength)
        {
            value = JsValue.FromInt32(this.value.Length);
            slotInfo = SlotInfo.Invalid;
            return true;
        }

        if (TryGetArrayIndexFromCanonicalString(realm.Atoms.AtomToString(atom), out var index) &&
            index < (uint)this.value.Length)
        {
            value = JsValue.FromString(GetFlatValue()[checked((int)index)].ToString());
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
                JsValue.FromInt32(value.Length));
            return true;
        }

        if (TryGetArrayIndexFromCanonicalString(realm.Atoms.AtomToString(atom), out var index) &&
            index < (uint)value.Length)
        {
            if (!needDescriptor)
            {
                descriptor = default;
                return true;
            }

            descriptor = PropertyDescriptor.Data(
                JsValue.FromString(GetFlatValue()[checked((int)index)].ToString()),
                false,
                true);
            return true;
        }

        return base.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out descriptor, needDescriptor);
    }

    internal override bool TryGetOwnElementDescriptor(uint index, out PropertyDescriptor descriptor)
    {
        if (index < (uint)value.Length)
        {
            descriptor = PropertyDescriptor.Data(
                JsValue.FromString(GetFlatValue()[checked((int)index)].ToString()),
                false,
                true);
            return true;
        }

        return base.TryGetOwnElementDescriptor(index, out descriptor);
    }

    public override bool DeleteElement(uint index)
    {
        if (index < (uint)value.Length)
            return false;
        return base.DeleteElement(index);
    }

    internal override bool SetElementWithReceiver(JsRealm realm, JsObject receiver, uint index, JsValue value)
    {
        if (ReferenceEquals(this, receiver) && index < (uint)this.value.Length)
            return false;
        return base.SetElementWithReceiver(realm, receiver, index, value);
    }

    internal override bool SetPropertyAtomWithReceiver(JsRealm realm, JsObject receiver, int atom, JsValue value,
        out SlotInfo slotInfo)
    {
        if (ReferenceEquals(this, receiver))
        {
            slotInfo = SlotInfo.Invalid;
            if (atom == IdLength)
                return false;

            if (TryGetArrayIndexFromCanonicalString(realm.Atoms.AtomToString(atom), out var index) &&
                index < (uint)this.value.Length)
                return false;
        }

        return base.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out slotInfo);
    }

    internal override bool DeletePropertyAtom(JsRealm realm, int atom)
    {
        if (atom == IdLength)
            return false;

        if (TryGetArrayIndexFromCanonicalString(realm.Atoms.AtomToString(atom), out var index) &&
            index < (uint)value.Length)
            return false;

        return base.DeletePropertyAtom(realm, atom);
    }

    internal override void CollectForInEnumerableStringAtomKeys(
        JsRealm realm,
        HashSet<string> visited,
        List<string> enumerableKeysOut)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var key = i.ToString(CultureInfo.InvariantCulture);
            if (visited.Add(key))
                enumerableKeysOut.Add(key);
        }

        // String length is own but non-enumerable; still mark as visited so prototype does not leak it.
        _ = visited.Add("length");
        base.CollectForInEnumerableStringAtomKeys(realm, visited, enumerableKeysOut);
    }

    internal override void CollectOwnElementIndices(List<uint> indicesOut, bool enumerableOnly)
    {
        for (uint i = 0; i < (uint)value.Length; i++)
            indicesOut.Add(i);
        base.CollectOwnElementIndices(indicesOut, enumerableOnly);
    }

    internal override void CollectOwnNamedPropertyAtoms(JsRealm realm, List<int> atomsOut, bool enumerableOnly)
    {
        if (!enumerableOnly)
            atomsOut.Add(IdLength);
        base.CollectOwnNamedPropertyAtoms(realm, atomsOut, enumerableOnly);
    }
}
