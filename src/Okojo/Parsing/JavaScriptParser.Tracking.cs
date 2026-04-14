using System.Buffers;

namespace Okojo.Parsing;

internal sealed partial class JsParser
{
    private struct IdentifierDuplicateTracker
    {
        private int firstId;
        private string? firstName;
        private HashSet<int>? idSet;
        private HashSet<string>? nameSet;

        public bool Add(int nameId, string name)
        {
            if (firstName is null)
            {
                firstId = nameId;
                firstName = name;
                return true;
            }

            if (idSet is null && nameSet is null)
            {
                if (nameId >= 0 && firstId >= 0)
                {
                    if (nameId == firstId)
                        return false;

                    idSet = new() { firstId, nameId };
                    return true;
                }

                if (string.Equals(name, firstName, StringComparison.Ordinal))
                    return false;

                nameSet = new(StringComparer.Ordinal) { firstName, name };
                return true;
            }

            return TryAddIdentifierKey(idSet, nameSet, nameId, name);
        }
    }

    private struct ObjectPropertyDuplicateTracker
    {
        private string? firstKey;
        private string? secondKey;
        private byte firstMask;
        private byte secondMask;
        private Dictionary<string, byte>? masksByKey;

        public bool TryGetMask(string key, out byte mask)
        {
            if (masksByKey is not null)
                return masksByKey.TryGetValue(key, out mask);

            if (firstKey is not null && string.Equals(firstKey, key, StringComparison.Ordinal))
            {
                mask = firstMask;
                return true;
            }

            if (secondKey is not null && string.Equals(secondKey, key, StringComparison.Ordinal))
            {
                mask = secondMask;
                return true;
            }

            mask = 0;
            return false;
        }

        public void SetMask(string key, byte mask)
        {
            if (masksByKey is not null)
            {
                masksByKey[key] = mask;
                return;
            }

            if (firstKey is not null && string.Equals(firstKey, key, StringComparison.Ordinal))
            {
                firstMask = mask;
                return;
            }

            if (secondKey is not null && string.Equals(secondKey, key, StringComparison.Ordinal))
            {
                secondMask = mask;
                return;
            }

            if (firstKey is null)
            {
                firstKey = key;
                firstMask = mask;
                return;
            }

            if (secondKey is null)
            {
                secondKey = key;
                secondMask = mask;
                return;
            }

            masksByKey = new(StringComparer.Ordinal)
            {
                [firstKey] = firstMask,
                [secondKey] = secondMask,
                [key] = mask
            };
            firstKey = null;
            secondKey = null;
            firstMask = 0;
            secondMask = 0;
        }
    }

    private ref struct PooledTokenKindStack(Span<JsTokenKind> initialBuffer)
    {
        private Span<JsTokenKind> buffer = initialBuffer;
        private JsTokenKind[]? rented = null;

        public int Count { get; private set; } = 0;

        public void Push(JsTokenKind value)
        {
            if (Count == buffer.Length)
                Grow();

            buffer[Count++] = value;
        }

        public JsTokenKind Peek()
        {
            return buffer[Count - 1];
        }

        public JsTokenKind Pop()
        {
            return buffer[--Count];
        }

        public void Dispose()
        {
            if (rented is not null)
            {
                ArrayPool<JsTokenKind>.Shared.Return(rented);
                rented = null;
            }
        }

        private void Grow()
        {
            var newBuffer = ArrayPool<JsTokenKind>.Shared.Rent(Math.Max(Count * 2, 8));
            buffer[..Count].CopyTo(newBuffer);
            if (rented is not null)
                ArrayPool<JsTokenKind>.Shared.Return(rented);
            rented = newBuffer;
            buffer = newBuffer;
        }
    }

    private ref struct PooledBinaryParseOperatorInfoStack(Span<BinaryParseOperatorInfo> initialBuffer)
    {
        private Span<BinaryParseOperatorInfo> buffer = initialBuffer;
        private BinaryParseOperatorInfo[]? rented = null;

        public int Count { get; private set; } = 0;

        public void Push(BinaryParseOperatorInfo value)
        {
            if (Count == buffer.Length)
                Grow();

            buffer[Count++] = value;
        }

        public BinaryParseOperatorInfo Peek()
        {
            return buffer[Count - 1];
        }

        public BinaryParseOperatorInfo Pop()
        {
            return buffer[--Count];
        }

        public void Dispose()
        {
            if (rented is not null)
            {
                ArrayPool<BinaryParseOperatorInfo>.Shared.Return(rented);
                rented = null;
            }
        }

        private void Grow()
        {
            var newBuffer = ArrayPool<BinaryParseOperatorInfo>.Shared.Rent(Math.Max(Count * 2, 8));
            buffer[..Count].CopyTo(newBuffer);
            if (rented is not null)
                ArrayPool<BinaryParseOperatorInfo>.Shared.Return(rented);
            rented = newBuffer;
            buffer = newBuffer;
        }
    }

    private ref struct PooledExpressionStack
    {
        private JsExpression? first;
        private JsExpression? second;
        private JsExpression[]? rented;

        public int Count { get; private set; }

        public void Push(JsExpression value)
        {
            switch (Count)
            {
                case 0:
                    first = value;
                    Count = 1;
                    return;
                case 1:
                    second = value;
                    Count = 2;
                    return;
            }

            EnsureCapacity(Count + 1);
            rented![Count - 2] = value;
            Count++;
        }

        public JsExpression Pop()
        {
            var index = Count - 1;
            Count = index;
            return index switch
            {
                0 => TakeFirst(),
                1 => TakeSecond(),
                _ => TakeRented(index - 2)
            };
        }

        private JsExpression TakeFirst()
        {
            var value = first!;
            first = null;
            return value;
        }

        private JsExpression TakeSecond()
        {
            var value = second!;
            second = null;
            return value;
        }

        private JsExpression TakeRented(int index)
        {
            var value = rented![index];
            rented[index] = null!;
            return value;
        }

        public void Dispose()
        {
            if (rented is not null)
            {
                ArrayPool<JsExpression>.Shared.Return(rented, clearArray: true);
                rented = null;
            }

            first = null;
            second = null;
            Count = 0;
        }

        private void EnsureCapacity(int requiredCount)
        {
            var requiredRentedLength = requiredCount - 2;
            if (rented is null)
            {
                rented = ArrayPool<JsExpression>.Shared.Rent(Math.Max(requiredRentedLength, 8));
                return;
            }

            if (requiredRentedLength <= rented.Length)
                return;

            var newBuffer = ArrayPool<JsExpression>.Shared.Rent(Math.Max(requiredRentedLength, rented.Length * 2));
            rented.AsSpan(0, Count - 2).CopyTo(newBuffer);
            ArrayPool<JsExpression>.Shared.Return(rented, clearArray: true);
            rented = newBuffer;
        }
    }
}
