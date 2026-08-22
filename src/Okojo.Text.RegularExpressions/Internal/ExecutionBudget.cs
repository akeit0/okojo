using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Okojo.Text.RegularExpressions.Internal;

internal struct ExecutionBudget
{
    private readonly long _maxSteps;
    private readonly long _maxBacktracks;
    private readonly long _deadline;
    private long _steps;
    private long _backtracks;

    internal ExecutionBudget(RegExpOptions options)
    {
        _maxSteps = options.MaxSteps;
        _maxBacktracks = options.MaxBacktracks;
        _steps = 0;
        _backtracks = 0;
        if (options.MatchTimeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            _deadline = long.MaxValue;
        }
        else
        {
            long now = Stopwatch.GetTimestamp();
            double delta = options.MatchTimeout.TotalSeconds * Stopwatch.Frequency;
            _deadline = delta >= long.MaxValue - now ? long.MaxValue : now + (long)delta;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Step()
    {
        long value = ++_steps;
        if (value > _maxSteps)
            Throw(RegExpExecutionLimit.Steps);
        if ((value & 0x3FFF) == 0 && Stopwatch.GetTimestamp() > _deadline)
            Throw(RegExpExecutionLimit.Timeout);
    }

    /// <summary>Charges a vectorized batch of work to the step budget.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AddSteps(int count)
    {
        if (count <= 0)
            return;
        long value = _steps + count;
        if (value > _maxSteps)
            Throw(RegExpExecutionLimit.Steps);
        if (Stopwatch.GetTimestamp() > _deadline)
            Throw(RegExpExecutionLimit.Timeout);
        _steps = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Backtrack()
    {
        if (++_backtracks > _maxBacktracks)
            Throw(RegExpExecutionLimit.Backtracks);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Throw(RegExpExecutionLimit kind) =>
        throw new RegExpExecutionException(kind);
}
