using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

public abstract class NamedPropertyLayout(JsRealm owner, NamedPropertyLayoutKind kind)
{
    private const int SwissProbeGroupSize = 8;

    protected const byte SwissEmpty = 0x80;
    protected const byte SwissDeleted = 0xFE;
    protected Entry[] Entries = Array.Empty<Entry>();
    protected int LiveCount = 0;

    public JsRealm Owner { get; } = owner;
    internal NamedPropertyLayoutKind Kind { get; set; } = kind;

    internal bool IsDynamic =>
        Kind is NamedPropertyLayoutKind.DynamicLinear or NamedPropertyLayoutKind.DynamicMap;

    protected int EntryLength => Entries.Length;

    protected StaticSwissEntryMap? UnsafeStaticMap { get; set; }

    protected bool TryGetLinearSlotInfo(int atom, out SlotInfo slotInfo)
    {
        return TryGetLinearSlotInfo(Entries, LiveCount, atom, out slotInfo);
    }

    protected IEnumerable<KeyValuePair<int, SlotInfo>> EnumerateEntries()
    {
        return EnumerateLinearEntries(Entries, LiveCount);
    }

    internal bool TryGetSlotInfo(int atom, out SlotInfo slotInfo)
    {
        return TryGetSlotInfo(atom, out slotInfo, out _);
    }

    internal bool TryGetSlotInfo(int atom, out SlotInfo slotInfo, out int hint)
    {
        switch (Kind)
        {
            case NamedPropertyLayoutKind.LinearStatic:
            case NamedPropertyLayoutKind.DynamicLinear:
                hint = -1;
                return TryGetLinearSlotInfo(atom, out slotInfo);
            case NamedPropertyLayoutKind.MapStatic:
                hint = -1;
                return UnsafeStaticMap!.TryGetSlotInfo(Entries, atom, out slotInfo);
            case NamedPropertyLayoutKind.DynamicMap:
                return Unsafe.As<DynamicNamedPropertyLayout>(this)
                    .TryGetMapSlotInfoCore(atom, out slotInfo, out hint);
            default:
                hint = -1;
                slotInfo = SlotInfo.Invalid;
                return false;
        }
    }

    internal IEnumerable<KeyValuePair<int, SlotInfo>> EnumerateSlotInfos()
    {
        return Kind switch
        {
            NamedPropertyLayoutKind.DynamicMap =>
                Unsafe.As<DynamicNamedPropertyLayout>(this).EnumerateMapSlotInfosCore(),
            _ => EnumerateEntries()
        };
    }

    protected static bool TryGetLinearSlotInfo(Entry[] entries, int liveCount, int atom, out SlotInfo slotInfo)
    {
        for (var i = 0; i < liveCount; i++)
            if (entries[i].Atom == atom)
            {
                slotInfo = entries[i].SlotInfo;
                return true;
            }

        slotInfo = SlotInfo.Invalid;
        return false;
    }

    protected static IEnumerable<KeyValuePair<int, SlotInfo>> EnumerateLinearEntries(Entry[] entries, int liveCount)
    {
        for (var i = 0; i < liveCount; i++) yield return new(entries[i].Atom, entries[i].SlotInfo);
    }

    protected static Entry[] CreateLinearEntries(Dictionary<int, SlotInfo> slotInfoByAtom)
    {
        var entries = new Entry[slotInfoByAtom.Count];
        var index = 0;
        foreach (var entry in slotInfoByAtom) entries[index++] = new(entry.Key, entry.Value);

        return entries;
    }

    protected static int ComputeSwissCapacity(int entryCount)
    {
        var capacity = 16;
        while (entryCount * 4 >= capacity * 3) capacity <<= 1;

        return capacity;
    }

    protected static void BuildSwissIndex(Entry[] entries, byte[] control, int[] entryIndexes)
    {
        Array.Fill(control, SwissEmpty);
        Array.Clear(entryIndexes);
        for (var entryIndex = 0; entryIndex < entries.Length; entryIndex++)
            InsertSwissEntry(entries, control, entryIndexes, entryIndex);
    }

    protected static bool TryFindSwissEntry(Entry[] entries, byte[] control, int[] entryIndexes, int atom,
        out int entryIndex)
    {
        return TryFindSwissEntry(entries, control, entryIndexes, atom, out entryIndex, out _);
    }

    protected static bool TryFindSwissEntry(Entry[] entries, byte[] control, int[] entryIndexes, int atom,
        out int entryIndex, out int tableIndex)
    {
        if (control.Length == 0)
        {
            entryIndex = -1;
            tableIndex = -1;
            return false;
        }

        var hash = MixSwissHash(atom);
        var tableLength = control.Length;
        var mask = tableLength - 1;
        var h2 = SwissHashByte(hash);
        var index = SwissBucket(hash, mask);
        while (true)
        {
            var contiguous = tableLength - index;
            if (contiguous > SwissProbeGroupSize)
                contiguous = SwissProbeGroupSize;

            var groupEnd = index + contiguous;
            for (var probeIndex = index; probeIndex < groupEnd; probeIndex++)
            {
                var marker = control[probeIndex];
                if (marker == SwissEmpty)
                {
                    tableIndex = probeIndex;
                    entryIndex = -1;
                    return false;
                }

                if (marker == h2)
                {
                    var candidateIndex = entryIndexes[probeIndex];
                    if (candidateIndex >= 0 && entries[candidateIndex].Atom == atom)
                    {
                        tableIndex = probeIndex;
                        entryIndex = candidateIndex;
                        return true;
                    }
                }
            }

            if (contiguous == SwissProbeGroupSize)
            {
                index += SwissProbeGroupSize;
                if (index > mask)
                    index = 0;
                continue;
            }

            var wrappedCount = SwissProbeGroupSize - contiguous;
            for (var probeIndex = 0; probeIndex < wrappedCount; probeIndex++)
            {
                var marker = control[probeIndex];
                if (marker == SwissEmpty)
                {
                    tableIndex = probeIndex;
                    entryIndex = -1;
                    return false;
                }

                if (marker == h2)
                {
                    var candidateIndex = entryIndexes[probeIndex];
                    if (candidateIndex >= 0 && entries[candidateIndex].Atom == atom)
                    {
                        tableIndex = probeIndex;
                        entryIndex = candidateIndex;
                        return true;
                    }
                }
            }

            index = wrappedCount;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    protected static bool TryFindSwissEntryOrInsertionSlot(
        Entry[] entries,
        byte[] control,
        int[] entryIndexes,
        int atom,
        out int entryIndex,
        out int insertionIndex)
    {
        if (control.Length == 0)
        {
            entryIndex = -1;
            insertionIndex = -1;
            return false;
        }

        var hash = MixSwissHash(atom);
        var tableLength = control.Length;
        var mask = tableLength - 1;
        var h2 = SwissHashByte(hash);
        var index = SwissBucket(hash, mask);
        var firstDeleted = -1;

        while (true)
        {
            var contiguous = tableLength - index;
            if (contiguous > SwissProbeGroupSize)
                contiguous = SwissProbeGroupSize;

            var groupEnd = index + contiguous;
            for (var probeIndex = index; probeIndex < groupEnd; probeIndex++)
            {
                var marker = control[probeIndex];
                if (marker == SwissEmpty)
                {
                    entryIndex = -1;
                    insertionIndex = firstDeleted >= 0 ? firstDeleted : probeIndex;
                    return false;
                }

                if (marker == SwissDeleted)
                {
                    if (firstDeleted < 0)
                        firstDeleted = probeIndex;
                    continue;
                }

                if (marker == h2)
                {
                    var candidateIndex = entryIndexes[probeIndex];
                    if (candidateIndex >= 0 && entries[candidateIndex].Atom == atom)
                    {
                        entryIndex = candidateIndex;
                        insertionIndex = probeIndex;
                        return true;
                    }
                }
            }

            if (contiguous == SwissProbeGroupSize)
            {
                index += SwissProbeGroupSize;
                if (index > mask)
                    index = 0;
                continue;
            }

            var wrappedCount = SwissProbeGroupSize - contiguous;
            for (var probeIndex = 0; probeIndex < wrappedCount; probeIndex++)
            {
                var marker = control[probeIndex];
                if (marker == SwissEmpty)
                {
                    entryIndex = -1;
                    insertionIndex = firstDeleted >= 0 ? firstDeleted : probeIndex;
                    return false;
                }

                if (marker == SwissDeleted)
                {
                    if (firstDeleted < 0)
                        firstDeleted = probeIndex;
                    continue;
                }

                if (marker == h2)
                {
                    var candidateIndex = entryIndexes[probeIndex];
                    if (candidateIndex >= 0 && entries[candidateIndex].Atom == atom)
                    {
                        entryIndex = candidateIndex;
                        insertionIndex = probeIndex;
                        return true;
                    }
                }
            }

            index = wrappedCount;
        }
    }

    protected static int FindSwissInsertIndex(byte[] control, int atom)
    {
        var hash = MixSwissHash(atom);
        var tableLength = control.Length;
        var mask = tableLength - 1;
        var index = SwissBucket(hash, mask);
        var firstDeleted = -1;
        while (true)
        {
            var contiguous = tableLength - index;
            if (contiguous > SwissProbeGroupSize)
                contiguous = SwissProbeGroupSize;

            var groupEnd = index + contiguous;
            for (var tableIndex = index; tableIndex < groupEnd; tableIndex++)
            {
                var marker = control[tableIndex];
                if (marker == SwissEmpty) return firstDeleted >= 0 ? firstDeleted : tableIndex;

                if (marker == SwissDeleted && firstDeleted < 0) firstDeleted = tableIndex;
            }

            if (contiguous == SwissProbeGroupSize)
            {
                index += SwissProbeGroupSize;
                if (index > mask)
                    index = 0;
                continue;
            }

            var wrappedCount = SwissProbeGroupSize - contiguous;
            for (var tableIndex = 0; tableIndex < wrappedCount; tableIndex++)
            {
                var marker = control[tableIndex];
                if (marker == SwissEmpty) return firstDeleted >= 0 ? firstDeleted : tableIndex;

                if (marker == SwissDeleted && firstDeleted < 0) firstDeleted = tableIndex;
            }

            index = wrappedCount;
        }
    }

    protected static void InsertSwissEntry(Entry[] entries, byte[] control, int[] entryIndexes, int entryIndex)
    {
        var atom = entries[entryIndex].Atom;
        var slot = FindSwissInsertIndex(control, atom);
        control[slot] = SwissHashByte(MixSwissHash(atom));
        entryIndexes[slot] = entryIndex;
    }

    protected static int SwissBucket(int atom, int mask)
    {
        var hash = MixSwissHash(atom);
        return SwissBucket(hash, mask);
    }

    protected static int SwissBucket(uint hash, int mask)
    {
        return (int)(hash & (uint)mask);
    }

    protected static byte SwissHashByte(int atom)
    {
        return SwissHashByte(MixSwissHash(atom));
    }

    protected static byte SwissHashByte(uint hash)
    {
        return (byte)(hash & 0x7F);
    }

    protected static uint MixSwissHash(int key)
    {
        var hash = (uint)key;
        hash ^= hash >> 16;
        hash *= 0x7feb352dU;
        hash ^= hash >> 15;
        hash *= 0x846ca68bU;
        hash ^= hash >> 16;
        return hash;
    }

    protected sealed class StaticSwissEntryMap
    {
        private readonly byte[] control;
        private readonly int[] entryIndexes;

        public StaticSwissEntryMap(Entry[] entries)
        {
            var capacity = ComputeSwissCapacity(entries.Length);
            control = new byte[capacity];
            entryIndexes = new int[capacity];
            Array.Fill(control, SwissEmpty);
            BuildSwissIndex(entries, control, entryIndexes);
        }

        public int Capacity => control.Length;

        public bool TryGetSlotInfo(Entry[] entries, int atom, out SlotInfo slotInfo)
        {
            if (TryFindSwissEntry(entries, control, entryIndexes, atom, out var entryIndex))
            {
                slotInfo = entries[entryIndex].SlotInfo;
                return true;
            }

            slotInfo = SlotInfo.Invalid;
            return false;
        }
    }

    protected internal struct Entry(int atom, SlotInfo slotInfo)
    {
        public int Atom = atom;
        public SlotInfo SlotInfo = slotInfo;
    }
}
