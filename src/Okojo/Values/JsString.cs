using System.Runtime.CompilerServices;

namespace Okojo.Values;

public readonly partial struct JsString : IEquatable<JsString>
{
    internal readonly object StringLikeObject;

    public JsString(string value)
    {
        StringLikeObject = value ?? string.Empty;
    }

    internal JsString(object value)
    {
        AssertValidStringObject(value);
        StringLikeObject = value;
    }

    public static JsString Empty => new(string.Empty);

    public int Length => GetLength(StringLikeObject);

    public char this[int index] => CharAt(StringLikeObject, index);

    public CharEnumerator GetEnumerator()
    {
        return new(this);
    }

    public RuneEnumerable EnumerateRunes()
    {
        return new(this);
    }

    public bool Equals(JsString other)
    {
        return EqualsOrdinal(this, other);
    }

    public override bool Equals(object? obj)
    {
        return obj is JsString other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Flatten());
    }

    public override string ToString()
    {
        return Flatten();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator JsString(string value)
    {
        return new(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetFlatString(out string? value)
    {
        return TryGetFlatString(StringLikeObject, out value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetFlatWindow(out string? value, out int start, out int length)
    {
        return TryGetFlatWindow(StringLikeObject, out value, out start, out length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetFlatSpan(out ReadOnlySpan<char> span)
    {
        return TryGetFlatSpan(StringLikeObject, out span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<char> Flatten(out char[]? pooled)
    {
        return Flatten(StringLikeObject, out pooled);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Flatten()
    {
        return ToFlatString(StringLikeObject);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CopyTo(Span<char> destination)
    {
        CopyTo(StringLikeObject, destination);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CopyTo(int start, int length, Span<char> destination)
    {
        CopyTo(StringLikeObject, start, length, destination);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsString Slice(int start, int length)
    {
        return Slice(this, start, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool StartsWith(JsString prefix)
    {
        return StartsWith(this, prefix);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool EndsWith(JsString suffix)
    {
        return EndsWith(this, suffix);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int IndexOf(JsString needle, int startIndex = 0)
    {
        return IndexOf(this, needle, startIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int LastIndexOf(JsString needle, int startIndex)
    {
        return LastIndexOf(this, needle, startIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsString Repeat(long count)
    {
        return Repeat(this, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsString Concat(JsString left, JsString right)
    {
        return new(Concat(left.StringLikeObject, right.StringLikeObject));
    }

    public static JsString Slice(JsString value, int start, int length)
    {
        return new(Slice(value.StringLikeObject, start, length));
    }
}
