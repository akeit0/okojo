using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Okojo.Values;

public readonly partial struct JsString
{
    private const int SmallConcatThreshold = 32;
    private const int FlatConcatThreshold = 96;
    private const int MaxRopeDepth = 24;
    private const int SmallSliceCopyThreshold = 32;
    private const int SliceCopyLengthThreshold = 64;
    private const int SliceCopyBaseLengthThreshold = 1024;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetLength(object value)
    {
        return value switch
        {
            string s => s.Length,
            RopeNode rope => rope.Length,
            SliceNode slice => slice.Length,
            _ => ThrowInvalidStringObject<int>()
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte GetDepth(object value)
    {
        return value switch
        {
            string => 0,
            RopeNode rope => rope.Depth,
            SliceNode => 0,
            _ => ThrowInvalidStringObject<byte>()
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetFlatString(object value, [NotNullWhen(true)] out string? result)
    {
        switch (value)
        {
            case string s:
                result = s;
                return true;
            case RopeNode rope when rope.TryGetFlat(out result):
                return true;
            case SliceNode slice when slice.TryGetFlat(out result):
                return true;
            default:
                result = null;
                return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetFlatWindow(object value, [NotNullWhen(true)] out string? result, out int start,
        out int length)
    {
        switch (value)
        {
            case string s:
                result = s;
                start = 0;
                length = s.Length;
                return true;

            case SliceNode slice:
                if (TryGetFlatWindow(slice.Base, out result, out var baseStart, out _))
                {
                    start = baseStart + slice.Offset;
                    length = slice.Length;
                    return true;
                }

                break;

            case RopeNode rope when rope.TryGetFlat(out result):
                start = 0;
                length = rope.Length;
                return true;
        }

        result = null;
        start = 0;
        length = 0;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetFlatSpan(object value, out ReadOnlySpan<char> span)
    {
        switch (value)
        {
            case string s:
                span = s.AsSpan();
                return true;

            case SliceNode slice:
                if (TryGetFlatSpan(slice.Base, out var baseSpan))
                {
                    span = baseSpan.Slice(slice.Offset, slice.Length);
                    return true;
                }

                break;

            case RopeNode rope when rope.TryGetFlat(out var flat):
                span = flat.AsSpan();
                return true;
        }

        span = default;
        return false;
    }

    private static string ToFlatString(object value)
    {
        return value switch
        {
            string s => s,
            RopeNode rope => rope.Flatten(),
            SliceNode slice => slice.Flatten(),
            _ => ThrowInvalidStringObject<string>()
        };
    }

    private static ReadOnlySpan<char> Flatten(object value, out char[]? pooled)
    {
        if (TryGetFlatSpan(value, out var span))
        {
            pooled = null;
            return span;
        }

        pooled = ArrayPool<char>.Shared.Rent(GetLength(value));
        var rentedSpan = pooled.AsSpan(0, GetLength(value));
        CopyWholeTo(value, rentedSpan);
        return rentedSpan;
    }

    private static object Concat(object left, object right)
    {
        var leftLength = GetLength(left);
        var rightLength = GetLength(right);

        if (leftLength == 0)
            return right;

        if (rightLength == 0)
            return left;

        var totalLength = checked(leftLength + rightLength);

        if (totalLength <= SmallConcatThreshold)
            return ConcatToFlat(left, right, totalLength);

        if (left is string leftString && right is string rightString && totalLength <= FlatConcatThreshold)
            return string.Concat(leftString, rightString);

        if (GetDepth(left) >= MaxRopeDepth || GetDepth(right) >= MaxRopeDepth)
            return ConcatToFlat(left, right, totalLength);

        return new RopeNode(left, right, leftLength, totalLength,
            (byte)(Math.Max(GetDepth(left), GetDepth(right)) + 1));
    }

    private static object Slice(object value, int start, int length)
    {
        var totalLength = GetLength(value);

        if ((uint)start > (uint)totalLength)
            throw new ArgumentOutOfRangeException(nameof(start));

        if ((uint)length > (uint)(totalLength - start))
            throw new ArgumentOutOfRangeException(nameof(length));

        if (length == 0)
            return string.Empty;

        if (start == 0 && length == totalLength)
            return value;

        if (length <= SmallSliceCopyThreshold)
            return ExtractToFlat(value, start, length);

        if (value is string s)
            return new SliceNode(s, start, length);

        if (value is SliceNode slice)
            return Slice(slice.Base, checked(slice.Offset + start), length);

        if (totalLength >= SliceCopyBaseLengthThreshold && length <= SliceCopyLengthThreshold)
            return ExtractToFlat(value, start, length);

        return new SliceNode(value, start, length);
    }

    private static char CharAt(object value, int index)
    {
        return value switch
        {
            string s => s[index],
            RopeNode rope => index < rope.LeftLength
                ? CharAt(rope.Left, index)
                : CharAt(rope.Right, index - rope.LeftLength),
            SliceNode slice => CharAt(slice.Base, slice.Offset + index),
            _ => ThrowInvalidStringObject<char>()
        };
    }

    private static string ConcatToFlat(object left, object right, int totalLength)
    {
        return string.Create(totalLength, (Left: left, Right: right), static (chars, state) =>
        {
            var leftLength = GetLength(state.Left);
            CopyWholeTo(state.Left, chars[..leftLength]);
            CopyWholeTo(state.Right, chars[leftLength..]);
        });
    }

    private static string ExtractToFlat(object value, int start, int length)
    {
        if (length == 0)
            return string.Empty;

        return string.Create(length, (Value: value, Start: start),
            static (chars, state) => { CopyRangeTo(state.Value, state.Start, chars); });
    }

    private static void CopyWholeTo(object value, Span<char> destination)
    {
        if (destination.Length < GetLength(value))
            throw new ArgumentException("Destination is too small.", nameof(destination));

        var enumerator = new ChunkEnumerator(value, 0, GetLength(value));
        var offset = 0;
        while (enumerator.MoveNext())
        {
            var chunk = enumerator.Current;
            chunk.CopyTo(destination[offset..]);
            offset += chunk.Length;
        }
    }

    private static void CopyRangeTo(object value, int start, Span<char> destination)
    {
        var enumerator = new ChunkEnumerator(value, start, destination.Length);
        var offset = 0;
        while (enumerator.MoveNext())
        {
            var chunk = enumerator.Current;
            chunk.CopyTo(destination[offset..]);
            offset += chunk.Length;
        }
    }

    private static void CopyTo(object value, Span<char> destination)
    {
        CopyWholeTo(value, destination);
    }

    private static void CopyTo(object value, int start, int length, Span<char> destination)
    {
        if (destination.Length < length)
            throw new ArgumentException("Destination is too small.", nameof(destination));

        CopyRangeTo(value, start, destination[..length]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AssertValidStringObject(object value)
    {
        if (value is string or RopeNode or SliceNode)
            return;

        ThrowInvalidStringObject();
    }

    [DoesNotReturn]
    private static T ThrowInvalidStringObject<T>()
    {
        throw new InvalidOperationException("Invalid JsString payload.");
    }

    [DoesNotReturn]
    private static void ThrowInvalidStringObject()
    {
        throw new InvalidOperationException("Invalid JsString payload.");
    }

    private sealed class RopeNode(object left, object right, int leftLength, int length, byte depth)
    {
        public byte Depth = depth;

        private string? flat;
        public object Left = left;
        public int LeftLength = leftLength;
        public int Length = length;
        public object Right = right;

        public bool TryGetFlat([NotNullWhen(true)] out string? value)
        {
            value = flat;
            return value is not null;
        }

        public string Flatten()
        {
            if (flat is not null)
                return flat;

            var result = ConcatToFlat(Left, Right, Length);
            flat = result;
            Left = result;
            Right = string.Empty;
            LeftLength = result.Length;
            Length = result.Length;
            Depth = 0;
            return result;
        }
    }

    private sealed class SliceNode(object @base, int offset, int length)
    {
        public object Base = @base;

        private string? flat;
        public int Length = length;
        public int Offset = offset;

        public bool TryGetFlat([NotNullWhen(true)] out string? value)
        {
            value = flat;
            return value is not null;
        }

        public string Flatten()
        {
            if (flat is not null)
                return flat;

            var result = ExtractToFlat(Base, Offset, Length);
            flat = result;
            Base = result;
            Offset = 0;
            Length = result.Length;
            return result;
        }
    }
}
