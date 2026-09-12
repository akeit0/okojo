namespace Okojo.JavaScript.Compiler;

// In-memory unit identities need no realm/agent and no operand relocation.
// A class evaluation still creates fresh private-brand tokens. Persistent code
// serialization would need an explicit ID relocation format (not part of this split).
internal static class PrivateNameIdAllocator
{
    private static long nextId;

    internal static int Allocate()
    {
        var id = Interlocked.Increment(ref nextId);
        if (id > int.MaxValue)
            throw new InvalidOperationException("Private-name ID space exhausted.");
        return (int)id;
    }
}
