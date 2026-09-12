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

internal static unsafe class BigSwitch
{
    public const int TotalOps = 153;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long Run(byte[] stream, int passes)
    {
        long acc = 0;
        long ra = 0,
            rb = 0;
        for (var p = 0; p < passes; p++)
        {
            for (var pc = 0; pc < stream.Length; pc++)
            {
                switch (stream[pc])
                {
                    case 0:
                        acc ^= ra;
                        break;
                    case 1:
                        acc += rb | 1;
                        break;
                    case 2:
                        acc += ra + rb;
                        break;
                    case 3:
                        ra++;
                        break;
                    case 4:
                        acc ^= acc >> 3;
                        break;
                    case 5:
                        rb ^= acc & 0xFF;
                        break;
                    case 6:
                        acc *= 3;
                        break;
                    case 7:
                        rb = ~rb;
                        break;
                    case 8:
                        acc ^= ra;
                        break;
                    case 9:
                        acc += rb | 1;
                        break;
                    case 10:
                        acc += ra + rb;
                        break;
                    case 11:
                        ra++;
                        break;
                    case 12:
                        acc ^= acc >> 3;
                        break;
                    case 13:
                        rb ^= acc & 0xFF;
                        break;
                    case 14:
                        acc *= 3;
                        break;
                    case 15:
                        rb = ~rb;
                        break;
                    default:
                        acc ^= pc;
                        break;
                }
            }
        }
        return acc;
    }

    // Same workload but only 32 distinct cases exist; rest fold to default.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long RunSmall(int[] stream, int passes)
    {
        long acc = 0;
        long ra = 0,
            rb = 0;
        for (var p = 0; p < passes; p++)
        {
            for (var pc = 0; pc < stream.Length; pc++)
            {
                switch (stream[pc])
                {
                    case 0:
                        acc ^= ra;
                        break;
                    case 1:
                        acc += rb | 1;
                        break;
                    case 2:
                        acc += ra + rb;
                        break;
                    case 3:
                        ra++;
                        break;
                    case 4:
                        acc ^= acc >> 3;
                        break;
                    case 5:
                        rb ^= acc & 0xFF;
                        break;
                    case 6:
                        acc *= 3;
                        break;
                    case 7:
                        rb = ~rb;
                        break;
                    case 8:
                        acc ^= ra;
                        break;
                    case 9:
                        acc += rb | 1;
                        break;
                    case 10:
                        acc += ra + rb;
                        break;
                    case 11:
                        ra++;
                        break;
                    case 12:
                        acc ^= acc >> 3;
                        break;
                    case 13:
                        rb ^= acc & 0xFF;
                        break;
                    case 14:
                        acc *= 3;
                        break;
                    case 15:
                        rb = ~rb;
                        break;
                    case 16:
                        acc ^= ra;
                        break;
                    case 17:
                        acc += rb | 1;
                        break;
                    case 18:
                        acc += ra + rb;
                        break;
                    case 19:
                        ra++;
                        break;
                    case 20:
                        acc ^= acc >> 3;
                        break;
                    case 21:
                        rb ^= acc & 0xFF;
                        break;
                    case 22:
                        acc *= 3;
                        break;
                    case 23:
                        rb = ~rb;
                        break;
                    case 24:
                        acc ^= acc << 2;
                        break;
                    case 25:
                        rb ^= acc >> 1;
                        break;
                    case 26:
                        acc -= ra;
                        break;
                    case 27:
                        rb += acc;
                        break;
                    case 28:
                        acc |= rb;
                        break;
                    case 29:
                        ra ^= 1;
                        break;
                    case 30:
                        acc &= rb;
                        break;
                    case 31:
                        rb -= 1;
                        break;
                    default:
                        acc ^= pc;
                        break;
                }
            }
        }
        return acc;
    }
}
