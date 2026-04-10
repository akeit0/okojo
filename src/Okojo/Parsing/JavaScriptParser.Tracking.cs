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
}
