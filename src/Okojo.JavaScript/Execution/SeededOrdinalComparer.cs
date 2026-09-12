using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Okojo.JavaScript.Execution;

// Cold collision fallback. Each comparer has an independent random Marvin seed.
// Marvin round follows dotnet/runtime System/Marvin.cs (MIT, .NET Foundation).
internal sealed class SeededOrdinalComparer
    : IEqualityComparer<string>,
        IAlternateEqualityComparer<ReadOnlySpan<char>, string>
{
    private readonly ulong seed;

    internal SeededOrdinalComparer()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        seed = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);

    public bool Equals(ReadOnlySpan<char> x, string y) => x.SequenceEqual(y);

    public string Create(ReadOnlySpan<char> x) => x.ToString();

    public int GetHashCode(string value) => Hash(value, seed);

    public int GetHashCode(ReadOnlySpan<char> value) => Hash(value, seed);

    internal static int Hash(ReadOnlySpan<char> value, ulong seed)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(value);
        uint p0 = (uint)seed,
            p1 = (uint)(seed >> 32);
        unchecked
        {
            while (bytes.Length >= 4)
            {
                p0 += BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                Block(ref p0, ref p1);
                bytes = bytes[4..];
            }
            uint tail = 0x80u << (bytes.Length * 8);
            for (int i = 0; i < bytes.Length; i++)
                tail |= (uint)bytes[i] << (i * 8);
            p0 += tail;
            Block(ref p0, ref p1);
            Block(ref p0, ref p1);
            return (int)(p0 ^ p1);
        }
    }

    private static void Block(ref uint p0, ref uint p1)
    {
        unchecked
        {
            p1 ^= p0;
            p0 = BitOperations.RotateLeft(p0, 20);
            p0 += p1;
            p1 = BitOperations.RotateLeft(p1, 9);
            p1 ^= p0;
            p0 = BitOperations.RotateLeft(p0, 27);
            p0 += p1;
            p1 = BitOperations.RotateLeft(p1, 19);
        }
    }
}
