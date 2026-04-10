namespace Okojo.Runtime.Interop;

internal sealed class HostRealmLayoutInfo
{
    private readonly object lazyMethodLock = new();
    private readonly Dictionary<int, (HostNamedMemberDescriptor Member, int Slot)>? lazyMethodsByAtom;

    internal HostRealmLayoutInfo(StaticNamedPropertyLayout layout, JsValue[] slotTemplate,
        Dictionary<int, (HostNamedMemberDescriptor Member, int Slot)>? lazyMethodsByAtom)
    {
        Layout = layout;
        SlotTemplate = slotTemplate;
        this.lazyMethodsByAtom = lazyMethodsByAtom;
    }

    internal StaticNamedPropertyLayout Layout { get; }
    internal JsValue[] SlotTemplate { get; }

    internal bool TryGetOrCreateMethodValue(JsRealm realm, int atom, out JsValue value)
    {
        if (lazyMethodsByAtom is null || !lazyMethodsByAtom.TryGetValue(atom, out var entry))
        {
            value = JsValue.TheHole;
            return false;
        }

        lock (lazyMethodLock)
        {
            value = SlotTemplate[entry.Slot];
            if (!value.IsTheHole)
                return true;

            var function = new JsHostFunction(realm,
                entry.Member.BindableMethodBody ?? HostTypeDescriptor.InvokeHostMethod, entry.Member.Name,
                entry.Member.FunctionLength)
            {
                UserData = entry.Member
            };
            value = JsValue.FromObject(function);
            SlotTemplate[entry.Slot] = value;
            return true;
        }
    }
}
