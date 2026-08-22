using System.Numerics;

namespace Okojo.Values;

public sealed class JsBigInt(BigInteger value) : IEquatable<JsBigInt>
{
    public BigInteger Value { get; } = value;

    public bool Equals(JsBigInt? other)
    {
        return other is not null && Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is JsBigInt other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return Value + "n";
    }
}
