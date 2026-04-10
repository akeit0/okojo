using Okojo.Objects;
using Okojo.Runtime;
using Okojo.Runtime.Interop;
using static Okojo.Runtime.AtomTable;

namespace Okojo.Reflection.Internal;

internal sealed class JsClrNamespaceObject : JsObject, IClrNamespaceReference
{
    internal JsClrNamespaceObject(JsRealm realm, string? path)
        : base(realm)
    {
        NamespacePath = path;
        Prototype = realm.ObjectPrototype;
        PreventExtensions();
    }

    private string DisplayTag =>
        string.IsNullOrEmpty(NamespacePath) ? "CLR Namespace" : $"CLR Namespace {NamespacePath}";

    public string? NamespacePath { get; }

    internal override bool TryGetPropertyAtomWithReceiverValue(JsRealm realm, in JsValue receiverValue, int atom,
        out JsValue value, out SlotInfo slotInfo)
    {
        slotInfo = SlotInfo.Invalid;
        if (atom == IdSymbolToStringTag)
        {
            value = JsValue.FromString(DisplayTag);
            return true;
        }

        if (atom >= 0)
        {
            value = realm.ResolveClrPath(CombinePath(realm.Atoms.AtomToString(atom)));
            return true;
        }

        if (Prototype is not null && Prototype != this)
            return Prototype.TryGetPropertyAtomWithReceiverValue(realm, receiverValue, atom, out value, out _);

        value = JsValue.Undefined;
        return false;
    }

    internal override bool TryGetOwnNamedPropertyDescriptorAtom(JsRealm realm, int atom,
        out PropertyDescriptor descriptor, bool needDescriptor = true)
    {
        if (atom == IdSymbolToStringTag)
        {
            descriptor = needDescriptor
                ? PropertyDescriptor.Const(JsValue.FromString(DisplayTag), false, false, true)
                : default;
            return true;
        }

        if (atom >= 0)
        {
            descriptor = needDescriptor
                ? PropertyDescriptor.Const(realm.ResolveClrPath(CombinePath(realm.Atoms.AtomToString(atom))))
                : default;
            return true;
        }

        descriptor = default;
        return false;
    }

    internal override void CollectOwnNamedPropertyAtoms(JsRealm realm, List<int> atomsOut, bool enumerableOnly)
    {
        if (!enumerableOnly)
            atomsOut.Add(IdSymbolToStringTag);
    }

    public override string ToString()
    {
        return $"[{DisplayTag}]";
    }

    private string CombinePath(string name)
    {
        return string.IsNullOrEmpty(NamespacePath) ? name : $"{NamespacePath}.{name}";
    }
}
