namespace Okojo.Runtime.Interop;

internal sealed class HostRealmLayoutInfo
{
    private const byte SpecialMethodIterator = 1 << 0;
    private const byte SpecialMethodAsyncIterator = 1 << 1;
    private const byte SpecialMethodDispose = 1 << 2;
    private const byte SpecialMethodAsyncDispose = 1 << 3;
    private const int SpecialMethodCount = 4;

    private readonly object lazyMethodLock = new();
    private readonly Dictionary<int, (HostNamedMemberDescriptor Member, int Slot)>? lazyMethodsByAtom;
    private readonly byte specialMethodMask;
    private readonly JsHostFunction?[] specialMethodFunctions;

    internal HostRealmLayoutInfo(StaticNamedPropertyLayout layout, JsValue[] slotTemplate,
        Dictionary<int, (HostNamedMemberDescriptor Member, int Slot)>? lazyMethodsByAtom,
        byte specialMethodMask)
    {
        Layout = layout;
        SlotTemplate = slotTemplate;
        this.lazyMethodsByAtom = lazyMethodsByAtom;
        this.specialMethodMask = specialMethodMask;
        specialMethodFunctions = specialMethodMask == 0 ? [] : new JsHostFunction?[SpecialMethodCount];
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

    internal bool TryGetOrCreateSpecialMethodValue(JsRealm realm, int atom, out JsValue value)
    {
        if (!TryGetSpecialMethodSlot(atom, out var slot) || !IsSpecialMethodEnabled(slot))
        {
            value = JsValue.TheHole;
            return false;
        }

        lock (lazyMethodLock)
        {
            var function = specialMethodFunctions[slot];
            if (function is not null)
            {
                value = JsValue.FromObject(function);
                return true;
            }

            function = new JsHostFunction(realm, GetSpecialMethodBody(slot), GetSpecialMethodName(slot), 0);
            value = JsValue.FromObject(function);
            specialMethodFunctions[slot] = function;
            return true;
        }
    }

    internal bool HasSpecialMethod(int atom)
    {
        return TryGetSpecialMethodSlot(atom, out var slot) && IsSpecialMethodEnabled(slot);
    }

    internal void CollectOwnSpecialMethodAtoms(List<int> atomsOut)
    {
        if ((specialMethodMask & SpecialMethodIterator) != 0)
            atomsOut.Add(IdSymbolIterator);
        if ((specialMethodMask & SpecialMethodAsyncIterator) != 0)
            atomsOut.Add(IdSymbolAsyncIterator);
        if ((specialMethodMask & SpecialMethodDispose) != 0)
            atomsOut.Add(IdSymbolDispose);
        if ((specialMethodMask & SpecialMethodAsyncDispose) != 0)
            atomsOut.Add(IdSymbolAsyncDispose);
    }

    private bool IsSpecialMethodEnabled(int slot)
    {
        return (specialMethodMask & (1 << slot)) != 0;
    }

    private static bool TryGetSpecialMethodSlot(int atom, out int slot)
    {
        switch (atom)
        {
            case IdSymbolIterator:
                slot = 0;
                return true;
            case IdSymbolAsyncIterator:
                slot = 1;
                return true;
            case IdSymbolDispose:
                slot = 2;
                return true;
            case IdSymbolAsyncDispose:
                slot = 3;
                return true;
            default:
                slot = -1;
                return false;
        }
    }

    private static string GetSpecialMethodName(int slot)
    {
        return slot switch
        {
            0 => "[Symbol.iterator]",
            1 => "[Symbol.asyncIterator]",
            2 => "[Symbol.dispose]",
            3 => "[Symbol.asyncDispose]",
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
    }

    private static JsHostFunctionBody GetSpecialMethodBody(int slot)
    {
        return slot switch
        {
            0 => JsHostObject.InvokeHostIteratorMethod,
            1 => JsHostObject.InvokeHostAsyncIteratorMethod,
            2 => JsHostObject.InvokeHostDisposeMethod,
            3 => JsHostObject.InvokeHostAsyncDisposeMethod,
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
    }
}
