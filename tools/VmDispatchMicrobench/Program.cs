// A7 E1 microbench: raw dispatch cost of three interpreter styles on this
// JIT/CPU. Synthetic opcodes, identical tiny handler bodies across styles.
//
//   S  switch in one method (current Okojo shape)
//   F  function-pointer table, state struct byref, NoInlining handlers
//   H  hybrid: hot ops inline in switch, cold tail via fptr call
//
// Streams:
//   cycle    fixed 8-opcode repeating pattern (loop-like BTB behavior)
//   mixed    uniform random over all opcodes
//
// Output: ns per dispatched opcode, median of runs.

using System.Diagnostics;
using System.Runtime.CompilerServices;

var results = new List<string>();

foreach (
    var (streamName, stream) in new[]
    {
        ("cycle", StreamBuilder.Cycle(1 << 20)),
        ("mixed", StreamBuilder.Mixed(1 << 20)),
    }
)
{
    foreach (var reps in new[] { 3, 5, 5 })
    {
        // warmup + measurement handled inside runners
        var s = Measure("S", stream, reps);
        var f = Measure("F", stream, reps);
        var h = Measure("H", stream, reps);
        results.Add(
            $"{streamName, -6} S={s, 8:F2}  F={f, 8:F2} ({f / s - 1, +7:P1})  H={h, 8:F2} ({h / s - 1, +7:P1})"
        );
        break;
    }
}

Console.WriteLine();
Console.WriteLine("ns/op by dispatch style");
foreach (var line in results)
    Console.WriteLine(line);

return;

static double Measure(string style, byte[] stream, int rounds)
{
    var samples = new double[rounds];
    for (var r = 0; r < rounds; r++)
    {
        // warmup
        Run(style, stream, 2);
        var sw = Stopwatch.StartNew();
        _ = Run(style, stream, 4);
        sw.Stop();
        samples[r] = sw.Elapsed.TotalNanoseconds / (stream.Length * 4.0);
    }
    Array.Sort(samples);
    return samples[samples.Length / 2];
}

static long Run(string style, byte[] stream, int passes)
{
    return style switch
    {
        "S" => SwitchRun(stream, passes),
        "F" => TableRun(stream, passes),
        "H" => HybridRun(stream, passes),
        _ => throw new NotSupportedException(),
    };
}

[MethodImpl(MethodImplOptions.NoInlining)]
static long SwitchRun(byte[] stream, int passes)
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
                case StreamBuilder.OpLoadA:
                    acc ^= ra;
                    break;
                case StreamBuilder.OpLoadB:
                    acc += rb | 1;
                    break;
                case StreamBuilder.OpAdd:
                    acc += ra + rb;
                    break;
                case StreamBuilder.OpInc:
                    ra++;
                    break;
                case StreamBuilder.OpShift:
                    acc ^= acc >> 3;
                    break;
                case StreamBuilder.OpXor:
                    rb ^= acc & 0xFF;
                    break;
                case StreamBuilder.OpMul:
                    acc *= 3;
                    break;
                case StreamBuilder.OpNeg:
                    rb = ~rb;
                    break;
                default:
                    acc++; // stand-in cold tail
                    break;
            }
        }
    }
    return acc;
}

static unsafe long TableRun(byte[] stream, int passes)
{
    var st = default(VmState);
    var handlers = Table.Handlers;
    for (var p = 0; p < passes; p++)
    {
        for (var pc = 0; pc < stream.Length; pc++)
        {
            handlers[stream[pc]](ref st);
        }
    }
    return st.Acc;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static unsafe long HybridRun(byte[] stream, int passes)
{
    var st = default(VmState);
    var handlers = Table.Handlers;
    for (var p = 0; p < passes; p++)
    {
        for (var pc = 0; pc < stream.Length; pc++)
        {
            switch (stream[pc])
            {
                case StreamBuilder.OpLoadA:
                    st.Acc ^= st.Ra;
                    break;
                case StreamBuilder.OpAdd:
                    st.Acc += st.Ra + st.Rb;
                    break;
                case StreamBuilder.OpInc:
                    st.Ra++;
                    break;
                case StreamBuilder.OpMul:
                    st.Acc *= 3;
                    break;
                default:
                    handlers[stream[pc]](ref st);
                    break;
            }
        }
    }
    return st.Acc;
}
