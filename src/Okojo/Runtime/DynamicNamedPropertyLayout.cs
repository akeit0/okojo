using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

internal sealed class DynamicNamedPropertyLayout : NamedPropertyLayout
{
    private const int TombstoneAtom = AtomTable.InvalidAtom;
    private const int LinearEntryLimit = 15;

    private byte[] control = Array.Empty<byte>();
    private int entryCount;
    private int[] entryIndexes = Array.Empty<int>();
    private int tombstones;

    internal DynamicNamedPropertyLayout(JsRealm owner) : base(owner, NamedPropertyLayoutKind.DynamicLinear)
    {
    }

    private DynamicNamedPropertyLayout(JsRealm owner, Entry[] denseEntries, bool isMap)
        : base(owner, isMap ? NamedPropertyLayoutKind.DynamicMap : NamedPropertyLayoutKind.DynamicLinear)
    {
        Entries = denseEntries;
        if (!isMap)
        {
            entryCount = denseEntries.Length;
            LiveCount = denseEntries.Length;
            StorageSlotCount = ComputeStorageSlotCount(denseEntries, denseEntries.Length);
            return;
        }

        BuildMapFromDenseEntries(denseEntries, ComputeSwissCapacity(denseEntries.Length), denseEntries.Length);
    }

    public int Count => LiveCount;
    internal Entry[] UnsafeEntries => Entries;
    internal int StorageSlotCount { get; private set; }

    internal static DynamicNamedPropertyLayout CreateOpenDataNoCollision(JsRealm owner, ReadOnlySpan<int> atoms)
    {
        if (atoms.Length == 0)
            return new(owner);

        var denseEntries = new Entry[atoms.Length];
        for (var i = 0; i < atoms.Length; i++)
            denseEntries[i] = new(atoms[i], new(i, JsShapePropertyFlags.Open));
        return CreateFromDenseEntries(owner, denseEntries);
    }

    internal new bool TryGetSlotInfo(int atom, out SlotInfo slotInfo)
    {
        return base.TryGetSlotInfo(atom, out slotInfo);
    }

    internal new IEnumerable<KeyValuePair<int, SlotInfo>> EnumerateSlotInfos()
    {
        return base.EnumerateSlotInfos();
    }

    internal bool TryGetMapSlotInfoCore(int atom, out SlotInfo slotInfo, out int hint)
    {
        if (TryFindSwissEntryOrInsertionSlot(Entries, control, entryIndexes, atom, out var entryIndex, out hint))
        {
            slotInfo = Entries[entryIndex].SlotInfo;
            return true;
        }

        slotInfo = SlotInfo.Invalid;
        return false;
    }

    public bool Contains(int atom)
    {
        return TryGetSlotInfo(atom, out _);
    }

    public void SetSlotInfo(int atom, in SlotInfo slotInfo)
    {
        if (Kind == NamedPropertyLayoutKind.DynamicLinear)
        {
            for (var i = 0; i < LiveCount; i++)
                if (Entries[i].Atom == atom)
                {
                    Entries[i] = new(atom, slotInfo);
                    return;
                }

            if (LiveCount < LinearEntryLimit)
            {
                EnsureLinearCapacity(LiveCount + 1);
                Entries[LiveCount++] = new(atom, slotInfo);
                entryCount = LiveCount;
                StorageSlotCount = ComputeStorageSlotCount(slotInfo);
                return;
            }

            PromoteLinearToMap();
        }

        SetMap(atom, slotInfo);
    }

    internal void AddNewSlotInfoUnchecked(int atom, SlotInfo slotInfo, int hint = -1)
    {
        if (Kind == NamedPropertyLayoutKind.DynamicLinear)
        {
            if (LiveCount < LinearEntryLimit)
            {
                EnsureLinearCapacity(LiveCount + 1);
                Entries[LiveCount++] = new(atom, slotInfo);
                entryCount = LiveCount;
                StorageSlotCount = ComputeStorageSlotCount(slotInfo);
                return;
            }

            PromoteLinearToMap();
        }

        SetMapNewUnchecked(atom, slotInfo, hint);
    }

    public bool Remove(int atom, out SlotInfo removedInfo)
    {
        if (Kind == NamedPropertyLayoutKind.DynamicLinear)
        {
            for (var i = 0; i < LiveCount; i++)
            {
                if (Entries[i].Atom != atom)
                    continue;

                removedInfo = Entries[i].SlotInfo;
                if (i < LiveCount - 1)
                    Array.Copy(Entries, i + 1, Entries, i, LiveCount - i - 1);
                LiveCount--;
                entryCount = LiveCount;
                if ((uint)LiveCount < (uint)Entries.Length)
                    Entries[LiveCount] = default;
                StorageSlotCount = LiveCount == 0 ? 0 : ComputeStorageSlotCount(Entries[LiveCount - 1].SlotInfo);
                return true;
            }

            removedInfo = SlotInfo.Invalid;
            return false;
        }

        if (!TryFindMap(atom, out var entryIndex, out var tableIndex))
        {
            removedInfo = SlotInfo.Invalid;
            return false;
        }

        removedInfo = Entries[entryIndex].SlotInfo;
        Entries[entryIndex].Atom = TombstoneAtom;
        Entries[entryIndex].SlotInfo = SlotInfo.Invalid;
        control[tableIndex] = SwissDeleted;
        entryIndexes[tableIndex] = -1;
        LiveCount--;
        tombstones++;
        if (LiveCount == 0)
            StorageSlotCount = 0;

        if (LiveCount <= LinearEntryLimit)
            CompactToLinear();
        else if (tombstones > 16 && tombstones * 2 > entryCount) CompactMap();

        return true;
    }

    internal DynamicNamedPropertyLayout RewriteFlags(Func<JsShapePropertyFlags, JsShapePropertyFlags> mapFlags)
    {
        var current = CopyLiveEntries();
        for (var i = 0; i < current.Length; i++)
        {
            ref var entry = ref current[i];
            entry = new(entry.Atom, new(entry.SlotInfo.Slot, mapFlags(entry.SlotInfo.Flags)));
        }

        return CreateFromDenseEntries(Owner, current);
    }

    internal DynamicNamedPropertyLayout RewriteFlags(int atom,
        Func<JsShapePropertyFlags, JsShapePropertyFlags, JsShapePropertyFlags> mapFlags,
        JsShapePropertyFlags newFlags)
    {
        var current = CopyLiveEntries();
        for (var i = 0; i < current.Length; i++)
        {
            ref var entry = ref current[i];
            if (entry.Atom != atom)
                continue;

            entry = new(entry.Atom, new(entry.SlotInfo.Slot, mapFlags(entry.SlotInfo.Flags, newFlags)));
            break;
        }

        return CreateFromDenseEntries(Owner, current);
    }

    internal DynamicNamedPropertyLayout RebuildExcluding(int excludedAtom)
    {
        var current = CopyLiveEntries();
        var next = new Entry[Math.Max(0, current.Length - 1)];
        var nextIndex = 0;
        var slotCursor = 0;
        for (var i = 0; i < current.Length; i++)
        {
            ref readonly var entry = ref current[i];
            if (entry.Atom == excludedAtom)
                continue;

            var flags = entry.SlotInfo.Flags;
            next[nextIndex++] = new(entry.Atom, new(slotCursor, flags));
            slotCursor += (flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.BothAccessor ? 2 : 1;
        }

        return CreateFromDenseEntries(Owner, next);
    }

    internal DynamicNamedPropertyLayout RebuildReplacingProperty(int targetAtom, JsShapePropertyFlags targetFlags)
    {
        var current = CopyLiveEntries();
        var slotCursor = 0;
        for (var i = 0; i < current.Length; i++)
        {
            ref var entry = ref current[i];
            var flags = entry.Atom == targetAtom ? targetFlags : entry.SlotInfo.Flags;
            entry = new(entry.Atom, new(slotCursor, flags));
            slotCursor += (flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.BothAccessor ? 2 : 1;
        }

        return CreateFromDenseEntries(Owner, current);
    }

    internal IEnumerable<KeyValuePair<int, SlotInfo>> EnumerateMapSlotInfosCore()
    {
        for (var i = 0; i < entryCount; i++)
        {
            var atom = Entries[i].Atom;
            if (atom == TombstoneAtom)
                continue;
            yield return new(atom, Entries[i].SlotInfo);
        }
    }

    internal Entry[] CopyLiveEntries()
    {
        if (Kind == NamedPropertyLayoutKind.DynamicLinear)
        {
            if (LiveCount == 0)
                return Array.Empty<Entry>();
            var linearEntries = new Entry[LiveCount];
            Entries.AsSpan(0, LiveCount).CopyTo(linearEntries);
            return linearEntries;
        }

        var denseEntries = new Entry[LiveCount];
        var nextIndex = 0;
        for (var i = 0; i < entryCount; i++)
        {
            var atom = Entries[i].Atom;
            if (atom == TombstoneAtom)
                continue;
            denseEntries[nextIndex++] = Entries[i];
        }

        return denseEntries;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void PromoteLinearToMap()
    {
        Kind = NamedPropertyLayoutKind.DynamicMap;
        var linearEntries = CopyLiveEntries();
        BuildMapFromDenseEntries(linearEntries, ComputeSwissCapacity(linearEntries.Length + 1),
            Math.Max(16, linearEntries.Length * 2));
    }

    private void CompactToLinear()
    {
        var nextLinear = new Entry[LiveCount];
        var nextIndex = 0;
        for (var i = 0; i < entryCount; i++)
        {
            var atom = Entries[i].Atom;
            if (atom == TombstoneAtom)
                continue;
            nextLinear[nextIndex++] = new(atom, Entries[i].SlotInfo);
        }

        Kind = NamedPropertyLayoutKind.DynamicLinear;
        Entries = nextLinear;
        control = Array.Empty<byte>();
        entryIndexes = Array.Empty<int>();
        entryCount = nextLinear.Length;
        LiveCount = nextLinear.Length;
        StorageSlotCount = ComputeStorageSlotCount(nextLinear, nextLinear.Length);
        tombstones = 0;
    }

    private void EnsureLinearCapacity(int minimumCount)
    {
        if (Entries.Length >= minimumCount)
            return;

        var newLength = Entries.Length == 0 ? 4 : Entries.Length * 2;
        if (newLength < minimumCount)
            newLength = minimumCount;
        if (newLength > LinearEntryLimit)
            newLength = LinearEntryLimit;

        Array.Resize(ref Entries, newLength);
    }

    private void SetMap(int atom, in SlotInfo slotInfo)
    {
        if (control.Length == 0)
            EnsureMapStorage();

        if (TryFindSwissEntryOrInsertionSlot(Entries, control, entryIndexes, atom, out var entryIndex,
                out var insertionIndex))
        {
            Entries[entryIndex].SlotInfo = slotInfo;
            return;
        }

        if ((LiveCount + tombstones + 1) * 4 >= control.Length * 3)
        {
            if (tombstones * 2 > LiveCount)
                RehashMap(control.Length);
            else
                RehashMap(control.Length == 0 ? 16 : control.Length * 2);
        }

        if (entryCount == Entries.Length)
        {
            var newLength = Entries.Length == 0 ? 16 : Entries.Length * 2;
            Array.Resize(ref Entries, newLength);
        }

        AppendMapEntryUnchecked(atom, slotInfo, insertionIndex);
    }

    private void SetMapNewUnchecked(int atom, in SlotInfo slotInfo, int hint)
    {
        if (control.Length == 0)
            EnsureMapStorage();

        if ((LiveCount + tombstones + 1) * 4 >= control.Length * 3 || entryCount == Entries.Length)
        {
            SetMapNewUncheckedFallbackNoInlining(atom, slotInfo);
        }
        else
        {
            var insertionIndex = hint;
            if (insertionIndex < 0)
                _ = TryFindSwissEntryOrInsertionSlot(Entries, control, entryIndexes, atom, out _, out insertionIndex);
            AppendMapEntryUnchecked(atom, slotInfo, insertionIndex);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SetMapNewUncheckedFallbackNoInlining(int atom, in SlotInfo slotInfo)
    {
        if ((LiveCount + tombstones + 1) * 4 >= control.Length * 3)
        {
            if (tombstones * 2 > LiveCount)
                RehashMap(control.Length);
            else
                RehashMap(control.Length == 0 ? 16 : control.Length * 2);
        }

        if (entryCount == Entries.Length)
        {
            var newLength = Entries.Length == 0 ? 16 : Entries.Length * 2;
            Array.Resize(ref Entries, newLength);
        }

        _ = TryFindSwissEntryOrInsertionSlot(Entries, control, entryIndexes, atom, out _, out var insertionIndex);
        AppendMapEntryUnchecked(atom, slotInfo, insertionIndex);
    }

    private void AppendMapEntryUnchecked(int atom, in SlotInfo slotInfo, int insertionIndex)
    {
        Entries[entryCount] = new(atom, slotInfo);
        if (control[insertionIndex] == SwissDeleted)
            tombstones--;
        control[insertionIndex] = SwissHashByte(atom);
        entryIndexes[insertionIndex] = entryCount;
        entryCount++;
        LiveCount++;
        StorageSlotCount = ComputeStorageSlotCount(slotInfo);
    }

    private void EnsureMapStorage()
    {
        if (control.Length != 0)
            return;

        control = new byte[16];
        Array.Fill(control, SwissEmpty);
        entryIndexes = new int[16];
        Array.Fill(entryIndexes, -1);
        Entries = new Entry[16];
    }

    private bool TryFindMap(int atom, out int entryIndex, out int tableIndex)
    {
        return TryFindSwissEntry(Entries, control, entryIndexes, atom, out entryIndex, out tableIndex);
    }

    private void CompactMap()
    {
        BuildMapFromDenseEntries(CopyLiveEntries(), ComputeSwissCapacity(LiveCount), Math.Max(16, Entries.Length));
    }

    private void RehashMap(int newCapacity)
    {
        if (newCapacity < 16)
            newCapacity = 16;

        BuildMapFromDenseEntries(CopyLiveEntries(), newCapacity, Math.Max(16, Entries.Length));
    }

    private void InsertIntoSwissMap(int entryIndex)
    {
        var atom = Entries[entryIndex].Atom;
        var slot = FindSwissInsertIndex(control, atom);
        if (control[slot] == SwissDeleted)
            tombstones--;
        control[slot] = SwissHashByte(atom);
        entryIndexes[slot] = entryIndex;
    }

    private void BuildMapFromDenseEntries(Entry[] denseEntries, int capacity, int minimumEntryArrayLength)
    {
        if (capacity < 16)
            capacity = 16;

        control = new byte[capacity];
        Array.Fill(control, SwissEmpty);
        entryIndexes = new int[capacity];
        Array.Fill(entryIndexes, -1);

        var entryArrayLength = Math.Max(Math.Max(denseEntries.Length, minimumEntryArrayLength), 16);
        Entries = new Entry[entryArrayLength];
        if (denseEntries.Length != 0)
            denseEntries.AsSpan().CopyTo(Entries);

        entryCount = denseEntries.Length;
        LiveCount = denseEntries.Length;
        StorageSlotCount = ComputeStorageSlotCount(denseEntries, denseEntries.Length);
        tombstones = 0;

        for (var i = 0; i < denseEntries.Length; i++)
            InsertIntoSwissMap(i);
    }

    private static DynamicNamedPropertyLayout CreateFromDenseEntries(JsRealm owner, Entry[] denseEntries)
    {
        var isMap = denseEntries.Length > LinearEntryLimit;
        return new(owner, denseEntries, isMap);
    }

    private static int ComputeStorageSlotCount(Entry[] denseEntries, int count)
    {
        return count == 0 ? 0 : ComputeStorageSlotCount(denseEntries[count - 1].SlotInfo);
    }

    private static int ComputeStorageSlotCount(in SlotInfo slotInfo)
    {
        return slotInfo.Slot +
               ((slotInfo.Flags & JsShapePropertyFlags.BothAccessor) == JsShapePropertyFlags.BothAccessor ? 2 : 1);
    }
}
