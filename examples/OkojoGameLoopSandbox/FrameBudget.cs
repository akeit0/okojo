namespace OkojoGameLoopSandbox;

internal readonly record struct FrameBudget(
    ulong MaxInstructions,
    TimeSpan MaxWallTime,
    ulong CheckInterval)
{
    public static FrameBudget Create(ulong maxInstructions, TimeSpan maxWallTime, ulong checkInterval = 256)
    {
        if (checkInterval == 0)
            throw new ArgumentOutOfRangeException(nameof(checkInterval));
        return new(maxInstructions, maxWallTime, checkInterval);
    }
}
