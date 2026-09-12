using System.Numerics;
using System.Runtime.CompilerServices;

namespace Okojo.JavaScript.Execution;

internal struct AtomStringEntry
{
    public string Name;
    public uint Hash;
    public int NextPlusOne;
}

// No deletion, no concurrent writers. Entry position is the stable atom ID.
internal sealed class AtomStringTable
{
    AtomStringEntry[] entries;
    int[] buckets;
    ulong multiplier;
    SeededOrdinalComparer? randomized;

    internal AtomStringTable(int capacity = 16)
    {
        int size = checked((int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(16, capacity)));
        entries = new AtomStringEntry[size];
        buckets = new int[NextPrime(size)];
        multiplier = ulong.MaxValue / (uint)buckets.Length + 1;
    }

    internal bool TryGetValue(string key, out int id)
    {
        ArgumentNullException.ThrowIfNull(key);
        id = Locate(key, key, Hash(key));
        if (id >= 0)
            return true;
        id = 0;
        return false;
    }

    public int Count { get; private set; }
    public bool IsRandomized => randomized is not null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint Hash(ReadOnlySpan<char> key) =>
        randomized is null ? AtomStringHash.Hash(key) : (uint)randomized.GetHashCode(key);

    public string Name(int id)
    {
        if ((uint)id >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(id));
        return entries[id].Name;
    }

    public int Intern(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        uint hash = Hash(key);
        int found = Locate(key, key, hash);
        if (found >= 0)
            return found;
        if (~found > 100 && randomized is null)
        {
            Promote();
            hash = Hash(key);
        }
        return Append(key, hash);
    }

    public int Intern(ReadOnlySpan<char> key)
    {
        uint hash = Hash(key);
        int found = Locate(key, null, hash);
        if (found >= 0)
            return found;
        if (~found > 100 && randomized is null)
        {
            Promote();
            hash = Hash(key);
        }
        return Append(key.ToString(), hash);
    }

    // A miss encodes traversed bucket entries, including unequal full hashes.
    int Locate(ReadOnlySpan<char> key, string? original, uint hash)
    {
        int collisions = 0;
        for (int next = buckets[Reduce(hash, buckets.Length, multiplier)]; next != 0; )
        {
            int id = next - 1;
            ref AtomStringEntry entry = ref entries[id];
            if (
                entry.Hash == hash
                && (ReferenceEquals(entry.Name, original) || key.SequenceEqual(entry.Name))
            )
                return id;
            next = entry.NextPlusOne;
            collisions++;
        }
        return ~collisions;
    }

    int Append(string name, uint hash)
    {
        EnsureRoom();
        int bucket = Reduce(hash, buckets.Length, multiplier);
        int id = Count;
        entries[id] = new AtomStringEntry
        {
            Name = name,
            Hash = hash,
            NextPlusOne = buckets[bucket],
        };
        buckets[bucket] = id + 1;
        Count = id + 1;
        return id;
    }

    void EnsureRoom()
    {
        if (Count < entries.Length)
            return;
        int size = checked(entries.Length * 2);
        AtomStringEntry[] grown = new AtomStringEntry[size];
        entries.CopyTo(grown, 0);
        int[] newBuckets = new int[NextPrime(size)];
        ulong newMultiplier = ulong.MaxValue / (uint)newBuckets.Length + 1;
        for (int i = 0; i < Count; i++)
        {
            int bucket = Reduce(grown[i].Hash, newBuckets.Length, newMultiplier);
            grown[i].NextPlusOne = newBuckets[bucket];
            newBuckets[bucket] = i + 1;
        }
        buckets = newBuckets;
        multiplier = newMultiplier;
        entries = grown;
    }

    // Lemire reciprocal reduction, as used in .NET HashHelpers.FastMod.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int Reduce(uint hash, int divisor, ulong reciprocal) =>
        (int)unchecked(((((reciprocal * hash) >> 32) + 1) * (uint)divisor) >> 32);

    static int NextPrime(int minimum)
    {
        for (int candidate = minimum | 1; ; candidate = checked(candidate + 2))
        {
            bool prime = true;
            for (int factor = 3; factor <= candidate / factor; factor += 2)
                if (candidate % factor == 0)
                {
                    prime = false;
                    break;
                }
            if (prime)
                return candidate;
        }
    }

    void Promote()
    {
        // Allocate before mutating links. The string/ID store stays compact for
        // the rest of this table's lifetime, including all subsequent growth.
        var comparer = new SeededOrdinalComparer();
        int[] newBuckets = new int[buckets.Length];
        for (int i = 0; i < Count; i++)
        {
            uint hash = (uint)comparer.GetHashCode(entries[i].Name);
            int bucket = Reduce(hash, newBuckets.Length, multiplier);
            entries[i].Hash = hash;
            entries[i].NextPlusOne = newBuckets[bucket];
            newBuckets[bucket] = i + 1;
        }
        randomized = comparer;
        buckets = newBuckets;
    }
}
