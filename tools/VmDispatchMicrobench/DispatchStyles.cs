using System.Runtime.CompilerServices;

internal struct VmState
{
    public long Acc;
    public long Ra;
    public long Rb;
}

internal static unsafe class Table
{
    public static readonly delegate* <ref VmState, void>[] Handlers =
    [
        &LoadA,
        &LoadB,
        &Add,
        &Inc,
        &Shift,
        &Xor,
        &Mul,
        &Neg,
        &Cold,
        &Cold,
        &Cold,
        &Cold,
        &Cold,
        &Cold,
        &Cold,
        &Cold,
    ];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void LoadA(ref VmState st) => st.Acc ^= st.Ra;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void LoadB(ref VmState st) => st.Acc += st.Rb | 1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Add(ref VmState st) => st.Acc += st.Ra + st.Rb;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Inc(ref VmState st) => st.Ra++;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Shift(ref VmState st) => st.Acc ^= st.Acc >> 3;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Xor(ref VmState st) => st.Rb ^= st.Acc & 0xFF;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Mul(ref VmState st) => st.Acc *= 3;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Neg(ref VmState st) => st.Rb = ~st.Rb;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Cold(ref VmState st) => st.Acc++;
}

internal static class StreamBuilder
{
    public const byte OpLoadA = 0;
    public const byte OpLoadB = 1;
    public const byte OpAdd = 2;
    public const byte OpInc = 3;
    public const byte OpShift = 4;
    public const byte OpXor = 5;
    public const byte OpMul = 6;
    public const byte OpNeg = 7;

    public static byte[] Cycle(int length)
    {
        byte[] pattern = [OpLoadA, OpLoadB, OpAdd, OpInc, OpShift, OpXor, OpMul, OpNeg];
        var stream = new byte[length];
        for (var i = 0; i < length; i++)
            stream[i] = pattern[i % pattern.Length];
        return stream;
    }

    public static byte[] Mixed(int length)
    {
        var stream = new byte[length];
        var rng = new Random(42);
        for (var i = 0; i < length; i++)
            stream[i] = (byte)rng.Next(0, 16); // includes cold tail 8..15
        return stream;
    }
}
