using System.Text;

namespace Okojo.Values;

public readonly partial struct JsString
{
    public ref struct CharEnumerator
    {
        private ChunkEnumerator chunks;
        private ReadOnlySpan<char> currentChunk;
        private int chunkIndex;

        internal CharEnumerator(JsString value)
        {
            chunks = new(value.StringLikeObject, 0, value.Length);
            currentChunk = default;
            chunkIndex = -1;
            Current = default;
        }

        public char Current { get; private set; }

        public bool MoveNext()
        {
            while (true)
            {
                var nextIndex = chunkIndex + 1;
                if (nextIndex < currentChunk.Length)
                {
                    chunkIndex = nextIndex;
                    Current = currentChunk[nextIndex];
                    return true;
                }

                if (!chunks.MoveNext())
                    return false;

                currentChunk = chunks.Current;
                chunkIndex = -1;
            }
        }
    }

    public readonly struct RuneEnumerable
    {
        private readonly JsString value;

        internal RuneEnumerable(JsString value)
        {
            this.value = value;
        }

        public RuneEnumerator GetEnumerator()
        {
            return new(value);
        }
    }

    public ref struct RuneEnumerator
    {
        private CharEnumerator chars;
        private bool hasPending;
        private char pending;

        internal RuneEnumerator(JsString value)
        {
            chars = new(value);
            hasPending = false;
            pending = default;
            Current = default;
        }

        public Rune Current { get; private set; }

        public bool MoveNext()
        {
            char first;
            if (hasPending)
            {
                first = pending;
                hasPending = false;
            }
            else if (!chars.MoveNext())
            {
                return false;
            }
            else
            {
                first = chars.Current;
            }

            if (char.IsHighSurrogate(first) && chars.MoveNext())
            {
                var second = chars.Current;
                if (char.IsLowSurrogate(second))
                {
                    Current = new(first, second);
                    return true;
                }

                hasPending = true;
                pending = second;
            }

            Current = new(first);
            return true;
        }
    }

    private ref struct ChunkEnumerator(object value, int start, int length)
    {
        private int nextIndex = start;
        private readonly int endIndex = start + length;

        public ReadOnlySpan<char> Current { get; private set; } = default;

        public bool MoveNext()
        {
            if (nextIndex >= endIndex)
                return false;

            var current = value;
            var localIndex = nextIndex;
            var remaining = endIndex - nextIndex;

            while (true)
                switch (current)
                {
                    case string s:
                        var length = Math.Min(remaining, s.Length - localIndex);
                        Current = s.AsSpan(localIndex, length);
                        nextIndex += length;
                        return true;

                    case SliceNode slice:
                        current = slice.Base;
                        localIndex += slice.Offset;
                        break;

                    case RopeNode rope:
                        if (localIndex < rope.LeftLength)
                        {
                            var leftRemaining = rope.LeftLength - localIndex;
                            if (remaining <= leftRemaining)
                            {
                                current = rope.Left;
                            }
                            else
                            {
                                current = rope.Left;
                                remaining = leftRemaining;
                            }
                        }
                        else
                        {
                            localIndex -= rope.LeftLength;
                            current = rope.Right;
                        }

                        break;

                    default:
                        ThrowInvalidStringObject();
                        return false;
                }
        }
    }
}
