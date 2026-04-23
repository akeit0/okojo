namespace Okojo.Runtime;

public enum NamedPropertyLayoutKind : byte
{
    LinearStatic,
    MapStatic,
    DynamicLinear,
    DynamicMap
}

public sealed class StaticNamedPropertyLayout : NamedPropertyLayout
{
    private const int LinearEntryLimit = 15;

    private readonly StaticSwissEntryMap? slotInfoByAtom;
    private readonly object transitionLock = new();
    private Dictionary<ulong, StaticNamedPropertyLayout>? transitions;

    internal StaticNamedPropertyLayout(JsRealm owner, Dictionary<int, SlotInfo> slotInfoByAtom,
        int storageSlotCount = 0)
        : base(owner, slotInfoByAtom.Count <= LinearEntryLimit
            ? NamedPropertyLayoutKind.LinearStatic
            : NamedPropertyLayoutKind.MapStatic)
    {
        StorageSlotCount = storageSlotCount;
        if (slotInfoByAtom.Count <= LinearEntryLimit)
        {
            Entries = CreateLinearEntries(slotInfoByAtom);
            LiveCount = Entries.Length;
            return;
        }

        Entries = CreateLinearEntries(slotInfoByAtom);
        LiveCount = Entries.Length;
        this.slotInfoByAtom = new(Entries);
        UnsafeStaticMap = this.slotInfoByAtom;
    }

    private StaticNamedPropertyLayout(JsRealm owner, Entry[] entries, int storageSlotCount, bool isMap)
        : base(owner, isMap ? NamedPropertyLayoutKind.MapStatic : NamedPropertyLayoutKind.LinearStatic)
    {
        Entries = entries;
        LiveCount = entries.Length;
        StorageSlotCount = storageSlotCount;
        if (isMap)
        {
            slotInfoByAtom = new(entries);
            UnsafeStaticMap = slotInfoByAtom;
        }
    }

    public int PropertyCount => Entries.Length;
    public int StorageSlotCount { get; }

    internal Entry[] UnsafeEntries => Entries;

    internal new bool TryGetSlotInfo(int atom, out SlotInfo info)
    {
        return base.TryGetSlotInfo(atom, out info);
    }

    internal new IEnumerable<KeyValuePair<int, SlotInfo>> EnumerateSlotInfos()
    {
        return base.EnumerateSlotInfos();
    }

    internal StaticNamedPropertyLayout RewriteFlags(Func<JsShapePropertyFlags, JsShapePropertyFlags> mapFlags)
    {
        if (Entries.Length == 0)
            return this;

        var nextEntries = new Entry[Entries.Length];
        for (var i = 0; i < Entries.Length; i++)
        {
            ref readonly var entry = ref Entries[i];
            nextEntries[i] = new(entry.Atom, new(entry.SlotInfo.Slot, mapFlags(entry.SlotInfo.Flags)));
        }

        return CreateFromEntries(Owner, nextEntries, StorageSlotCount);
    }

    internal StaticNamedPropertyLayout RewriteFlags(int atom,
        Func<JsShapePropertyFlags, JsShapePropertyFlags, JsShapePropertyFlags> mapFlags,
        JsShapePropertyFlags newFlags)
    {
        if (Entries.Length == 0)
            return this;

        var nextEntries = new Entry[Entries.Length];
        for (var i = 0; i < Entries.Length; i++)
        {
            ref readonly var entry = ref Entries[i];
            var flags = entry.Atom == atom ? mapFlags(entry.SlotInfo.Flags, newFlags) : entry.SlotInfo.Flags;
            nextEntries[i] = new(entry.Atom, new(entry.SlotInfo.Slot, flags));
        }

        return CreateFromEntries(Owner, nextEntries, StorageSlotCount);
    }

    internal StaticNamedPropertyLayout RebuildExcluding(int excludedAtom)
    {
        var nextEntries = new Entry[Math.Max(0, Entries.Length - 1)];
        var nextIndex = 0;
        var slotCursor = 0;
        for (var i = 0; i < Entries.Length; i++)
        {
            ref readonly var entry = ref Entries[i];
            if (entry.Atom == excludedAtom)
                continue;

            var flags = entry.SlotInfo.Flags;
            nextEntries[nextIndex++] = new(entry.Atom, new(slotCursor, flags));
            slotCursor += (flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.BothAccessor ? 2 : 1;
        }

        return CreateFromEntries(Owner, nextEntries, slotCursor);
    }

    internal StaticNamedPropertyLayout RebuildReplacingProperty(int targetAtom, JsShapePropertyFlags targetFlags)
    {
        var nextEntries = new Entry[Entries.Length];
        var slotCursor = 0;
        for (var i = 0; i < Entries.Length; i++)
        {
            ref readonly var entry = ref Entries[i];
            var flags = entry.Atom == targetAtom ? targetFlags : entry.SlotInfo.Flags;
            nextEntries[i] = new(entry.Atom, new(slotCursor, flags));
            slotCursor += (flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.BothAccessor ? 2 : 1;
        }

        return CreateFromEntries(Owner, nextEntries, slotCursor);
    }

    internal StaticNamedPropertyLayout AppendNoCollision(ReadOnlySpan<PropertyDefinition> definitions)
    {
        if (definitions.Length == 0)
            return this;

        var nextEntries = new Entry[Entries.Length + definitions.Length];
        var nextIndex = 0;
        var slotCursor = 0;
        for (var i = 0; i < Entries.Length; i++)
        {
            ref readonly var entry = ref Entries[i];
            nextEntries[nextIndex++] = new(entry.Atom, new(slotCursor, entry.SlotInfo.Flags));
            slotCursor += (entry.SlotInfo.Flags & JsShapePropertyFlags.BothAccessor) ==
                          JsShapePropertyFlags.BothAccessor
                ? 2
                : 1;
        }

        for (var i = 0; i < definitions.Length; i++)
        {
            ref readonly var def = ref definitions[i];
            nextEntries[nextIndex++] = new(def.Atom, new(slotCursor, def.Flags));
            slotCursor += def.HasTwoValues ? 2 : 1;
        }

        return CreateFromEntries(Owner, nextEntries, slotCursor);
    }

    public bool TryGetSlot(int atom, out int slot)
    {
        if (TryGetSlotInfo(atom, out var info))
        {
            slot = info.Slot;
            return true;
        }

        slot = -1;
        return false;
    }

    public StaticNamedPropertyLayout GetOrAddTransition(int atom, out int slot)
    {
        var next = GetOrAddTransition(atom, JsShapePropertyFlags.Open, out var info);
        slot = info.Slot;
        return next;
    }

    public StaticNamedPropertyLayout GetOrAddTransition(int atom, JsShapePropertyFlags flags,
        out SlotInfo slotInfo)
    {
        if (TryGetSlotInfo(atom, out slotInfo))
            return this;

        var transitionKey = MakeTransitionKey(atom, flags);
        var transitions = this.transitions;
        if (transitions is not null && transitions.TryGetValue(transitionKey, out var existing))
        {
            existing.TryGetSlotInfo(atom, out slotInfo);
            return existing;
        }

        lock (transitionLock)
        {
            this.transitions ??= new();
            if (this.transitions.TryGetValue(transitionKey, out existing))
            {
                existing.TryGetSlotInfo(atom, out slotInfo);
                return existing;
            }

            var width = (flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.BothAccessor ? 2 : 1;
            slotInfo = new(StorageSlotCount, flags);
            var nextEntries = new Entry[Entries.Length + 1];
            if (Entries.Length != 0) Entries.AsSpan().CopyTo(nextEntries);

            nextEntries[Entries.Length] = new(atom, slotInfo);
            var nextIsMap = nextEntries.Length > LinearEntryLimit;
            var next = new StaticNamedPropertyLayout(Owner, nextEntries, StorageSlotCount + width, nextIsMap);

            this.transitions.Add(transitionKey, next);
            return next;
        }
    }

    private static ulong MakeTransitionKey(int atom, JsShapePropertyFlags flags)
    {
        return ((ulong)(byte)flags << 32) | (uint)atom;
    }

    private static StaticNamedPropertyLayout CreateFromEntries(JsRealm owner, Entry[] entries,
        int storageSlotCount)
    {
        var isMap = entries.Length > LinearEntryLimit;
        return new(owner, entries, storageSlotCount, isMap);
    }
}
