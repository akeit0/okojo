using Okojo.JavaScript.Execution;

namespace Okojo.JavaScript.Objects;

/// <summary>Reports how a dynamic named-property host handled an assignment.</summary>
public enum JsDynamicNamedPropertySetResult
{
    /// <summary>Use ordinary object assignment behavior for this property.</summary>
    NotHandled,

    /// <summary>The host accepted the assignment.</summary>
    Succeeded,

    /// <summary>The host rejected the assignment.</summary>
    Rejected,
}

/// <summary>
/// Provides a public extension seam for host objects backed by dynamic named properties.
/// </summary>
/// <remarks>
/// Dynamic keys are non-index string property keys. Symbols, array-index keys, and ordinary
/// properties defined directly on the object continue through <see cref="JsObject"/>'s normal
/// property path. Dynamic properties are exposed as data properties; override
/// <see cref="GetDynamicNamedPropertyFlags"/> to customize their attributes.
/// </remarks>
public abstract class JsDynamicNamedObject : JsObject
{
    protected JsDynamicNamedObject(JsRealm realm, JsObject? prototype = null)
        : base(realm)
    {
        if (prototype is not null && !TrySetPrototype(prototype))
            throw new InvalidOperationException(
                "Could not set the dynamic named object's prototype."
            );
    }

    /// <summary>Gets the current value of a dynamic named property.</summary>
    protected virtual bool TryGetDynamicNamedProperty(string name, out JsValue value)
    {
        _ = name;
        value = JsValue.Undefined;
        return false;
    }

    /// <summary>
    /// Sets or creates a dynamic named property. Returning
    /// <see cref="JsDynamicNamedPropertySetResult.Rejected"/> produces a <c>TypeError</c> in strict
    /// JavaScript code; returning <see cref="JsDynamicNamedPropertySetResult.NotHandled"/> uses
    /// ordinary assignment behavior.
    /// </summary>
    protected virtual JsDynamicNamedPropertySetResult SetDynamicNamedProperty(
        string name,
        JsValue value
    )
    {
        _ = name;
        _ = value;
        return JsDynamicNamedPropertySetResult.NotHandled;
    }

    /// <summary>
    /// Tests whether a dynamic named property currently exists without invoking its getter.
    /// </summary>
    protected virtual bool HasDynamicNamedProperty(string name)
    {
        _ = name;
        return false;
    }

    /// <summary>Appends the current dynamic own-property names in host-defined order.</summary>
    protected virtual void CollectDynamicNamedPropertyNames(List<string> namesOut)
    {
        _ = namesOut;
    }

    /// <summary>Gets the data-property attributes reported for a dynamic named property.</summary>
    protected virtual JsShapePropertyFlags GetDynamicNamedPropertyFlags(string name)
    {
        _ = name;
        return JsShapePropertyFlags.Writable | JsShapePropertyFlags.Enumerable;
    }

    internal override bool TryGetPropertyAtomWithReceiverValue(
        JsRealm realm,
        in JsValue receiverValue,
        int atom,
        out JsValue value,
        out SlotInfo slotInfo
    )
    {
        if (atom < 0 || NamedPropertyLayout.TryGetSlotInfo(atom, out _))
            return base.TryGetPropertyAtomWithReceiverValue(
                realm,
                receiverValue,
                atom,
                out value,
                out slotInfo
            );

        var name = realm.Atoms.AtomToString(atom);
        if (HasDynamicNamedProperty(name))
        {
            if (!TryGetDynamicNamedProperty(name, out value))
                value = JsValue.Undefined;
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

    internal override bool SetPropertyAtomWithReceiver(
        JsRealm realm,
        JsObject receiver,
        int atom,
        JsValue value,
        out SlotInfo slotInfo
    )
    {
        slotInfo = SlotInfo.Invalid;
        if (atom < 0 || NamedPropertyLayout.TryGetSlotInfo(atom, out _))
            return base.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out slotInfo);

        var name = realm.Atoms.AtomToString(atom);
        if (HasDynamicNamedProperty(name))
        {
            if ((GetDataPropertyFlags(name) & JsShapePropertyFlags.Writable) == 0)
                return false;

            if (!ReferenceEquals(this, receiver))
                return receiver.TryDefineOwnDataPropertyForSet(realm, atom, value, out slotInfo);

            return SetDynamicNamedProperty(name, value)
                == JsDynamicNamedPropertySetResult.Succeeded;
        }

        if (
            Prototype is not null
            && Prototype != this
            && TrySetInheritedDescriptor(realm, receiver, atom, value, out var inheritedHandled)
        )
            return inheritedHandled;

        if (!ReferenceEquals(this, receiver))
            return receiver.TryDefineOwnDataPropertyForSet(realm, atom, value, out slotInfo);

        if (!IsExtensible)
            return false;

        return SetDynamicNamedProperty(name, value) switch
        {
            JsDynamicNamedPropertySetResult.Succeeded => true,
            JsDynamicNamedPropertySetResult.Rejected => false,
            _ => base.SetPropertyAtomWithReceiver(realm, receiver, atom, value, out slotInfo),
        };
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(
        JsRealm realm,
        int atom,
        out PropertyDescriptor descriptor,
        bool needDescriptor = true
    )
    {
        if (atom >= 0 && !NamedPropertyLayout.TryGetSlotInfo(atom, out _))
        {
            var name = realm.Atoms.AtomToString(atom);
            if (HasDynamicNamedProperty(name))
            {
                if (!needDescriptor)
                {
                    descriptor = default;
                    return true;
                }

                if (!TryGetDynamicNamedProperty(name, out var value))
                    value = JsValue.Undefined;
                var flags = GetDataPropertyFlags(name);
                descriptor = PropertyDescriptor.Data(
                    value,
                    (flags & JsShapePropertyFlags.Writable) != 0,
                    (flags & JsShapePropertyFlags.Enumerable) != 0,
                    (flags & JsShapePropertyFlags.Configurable) != 0
                );
                return true;
            }
        }

        return base.TryGetOwnNamedPropertyDescriptorAtom(
            realm,
            atom,
            out descriptor,
            needDescriptor
        );
    }

    internal override void CollectOwnNamedPropertyAtoms(
        JsRealm realm,
        List<int> atomsOut,
        bool enumerableOnly
    )
    {
        var allOrdinaryAtoms = new List<int>();
        base.CollectOwnNamedPropertyAtoms(realm, allOrdinaryAtoms, false);
        var seenAtoms = new HashSet<int>(allOrdinaryAtoms);
        var names = new List<string>();
        CollectDynamicNamedPropertyNames(names);
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (TryGetArrayIndexFromCanonicalString(name, out _) || !HasDynamicNamedProperty(name))
                continue;

            if (
                enumerableOnly
                && (GetDataPropertyFlags(name) & JsShapePropertyFlags.Enumerable) == 0
            )
                continue;

            var atom = realm.Atoms.InternNoCheck(name);
            if (seenAtoms.Add(atom))
                atomsOut.Add(atom);
        }

        if (!enumerableOnly)
        {
            atomsOut.AddRange(allOrdinaryAtoms);
            return;
        }

        var enumerableOrdinaryAtoms = new List<int>();
        base.CollectOwnNamedPropertyAtoms(realm, enumerableOrdinaryAtoms, true);
        atomsOut.AddRange(enumerableOrdinaryAtoms);
    }

    private JsShapePropertyFlags GetDataPropertyFlags(string name)
    {
        return GetDynamicNamedPropertyFlags(name)
            & (
                JsShapePropertyFlags.Writable
                | JsShapePropertyFlags.Enumerable
                | JsShapePropertyFlags.Configurable
            );
    }
}
