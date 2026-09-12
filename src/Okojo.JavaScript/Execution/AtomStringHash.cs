using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Okojo.JavaScript.Execution;

// Two-accumulator ordinal hash recurrence with unaligned 32-bit pair reads.
// Every pair read is guarded by at least two remaining UTF-16 code units.
internal static class AtomStringHash
{
    public static uint Hash(ReadOnlySpan<char> text)
    {
        unchecked
        {
            uint first = (5381u << 16) + 5381u,
                second = first;
            ref char start = ref MemoryMarshal.GetReference(text);
            int remaining = text.Length;
            while (remaining >= 4)
            {
                first = Mix(first, ReadPair(ref start));
                second = Mix(second, ReadPair(ref Unsafe.Add(ref start, 2)));
                start = ref Unsafe.Add(ref start, 4);
                remaining -= 4;
            }
            if (remaining >= 2)
            {
                uint pair = ReadPair(ref start);
                if (remaining == 2)
                    second = Mix(second, pair);
                else
                {
                    first = Mix(first, pair);
                    second = Mix(second, Unsafe.Add(ref start, 2));
                }
            }
            else if (remaining == 1)
                second = Mix(second, start);
            return first + second * 1566083941u;
        }
    }

    static uint ReadPair(ref char first)
    {
        uint word = Unsafe.ReadUnaligned<uint>(ref Unsafe.As<char, byte>(ref first));
        // Swap UTF-16 halves, not bytes within each char, on a big-endian runtime.
        return BitConverter.IsLittleEndian ? word : BitOperations.RotateLeft(word, 16);
    }

    static uint Mix(uint hash, uint word) =>
        unchecked((BitOperations.RotateLeft(hash, 5) + hash) ^ word);
}
