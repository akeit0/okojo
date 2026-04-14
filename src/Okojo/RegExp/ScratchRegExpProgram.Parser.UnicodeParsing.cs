using Okojo.Parsing;

namespace Okojo.RegExp;

internal sealed partial class ScratchRegExpProgram
{
    private sealed partial class Parser
    {
        private ClassItem ParseClassStringDisjunction()
        {
            pos++;
            using var strings = new ScratchPooledList<string>();
            Span<char> initialBuffer = stackalloc char[32];
            var current = new PooledCharBuilder(initialBuffer);
            try
            {
                while (pos < pattern.Length)
                {
                    if (Peek('}'))
                    {
                        if (current.Length == 0)
                            throw new ArgumentException("Invalid regular expression pattern");

                        strings.Add(current.ToString());
                        pos++;
                        return new(ClassItemKind.StringLiteral, Strings: strings.ToArray());
                    }

                    if (Peek('|'))
                    {
                        if (current.Length == 0)
                            throw new ArgumentException("Invalid regular expression pattern");

                        strings.Add(current.ToString());
                        current.Clear();
                        pos++;
                        continue;
                    }

                    AppendClassStringCharacter(ref current);
                }
            }
            finally
            {
                current.Dispose();
            }

            throw new ArgumentException("Invalid regular expression pattern");
        }

        private void AppendClassStringCharacter(ref PooledCharBuilder builder)
        {
            if (pos >= pattern.Length)
                throw new ArgumentException("Invalid regular expression pattern");

            var ch = pattern[pos++];
            if (ch != '\\')
            {
                builder.Append(ch);
                return;
            }

            if (pos >= pattern.Length)
                throw new ArgumentException("Invalid regular expression pattern");

            var next = pattern[pos++];
            switch (next)
            {
                case 'x':
                    AppendCodePoint(ref builder, ParseFixedHexEscape(2));
                    return;
                case 'u' when Peek('{'):
                    pos++;
                    AppendCodePoint(ref builder, ParseBracedUnicodeScalar());
                    return;
                case 'u':
                    AppendCodePoint(ref builder, ParseFixedHexEscape(4));
                    return;
                default:
                    builder.Append(next);
                    return;
            }
        }

        private int ParseFixedHexEscape(int digits)
        {
            if (pos + digits > pattern.Length)
                throw new ArgumentException("Invalid regular expression pattern");

            var value = 0;
            for (var i = 0; i < digits; i++)
            {
                var hex = HexToInt(pattern[pos++]);
                if (hex < 0)
                    throw new ArgumentException("Invalid regular expression pattern");
                value = (value << 4) | hex;
            }

            return value;
        }

        private int ParseBracedUnicodeScalar()
        {
            var scalar = 0;
            var start = pos;
            while (pos < pattern.Length && pattern[pos] != '}')
            {
                var hex = HexToInt(pattern[pos]);
                if (hex < 0)
                    throw new ArgumentException("Invalid regular expression pattern");

                scalar = checked((scalar << 4) | hex);
                pos++;
            }

            if (pos == start || pos >= pattern.Length)
                throw new ArgumentException("Invalid regular expression pattern");

            pos++;
            return scalar;
        }

        private static void AppendCodePoint(ref PooledCharBuilder builder, int codePoint)
        {
            builder.AppendRune(codePoint);
        }

        private string ParseGroupName()
        {
            Span<char> initialBuffer = stackalloc char[32];
            var builder = new PooledCharBuilder(initialBuffer);
            try
            {
                while (pos < pattern.Length && pattern[pos] != '>')
                {
                    var ch = pattern[pos++];
                    if (ch != '\\')
                    {
                        builder.Append(ch);
                        continue;
                    }

                    if (pos >= pattern.Length || pattern[pos++] != 'u')
                        throw new ArgumentException("Invalid capture group name");

                    AppendGroupNameUnicodeEscape(ref builder);
                }

                if (pos >= pattern.Length || builder.Length == 0)
                    throw new ArgumentException("Invalid capture group name");

                pos++;
                return builder.ToString();
            }
            finally
            {
                builder.Dispose();
            }
        }

        private void AppendGroupNameUnicodeEscape(ref PooledCharBuilder builder)
        {
            if (Peek('{'))
            {
                pos++;
                var scalar = 0;
                var start = pos;
                while (pos < pattern.Length && pattern[pos] != '}')
                {
                    var hex = HexToInt(pattern[pos]);
                    if (hex < 0)
                        throw new ArgumentException("Invalid capture group name");
                    scalar = checked((scalar << 4) | hex);
                    pos++;
                }

                if (pos == start || pos >= pattern.Length)
                    throw new ArgumentException("Invalid capture group name");

                pos++;
                builder.AppendRune(scalar);
                return;
            }

            if (pos + 4 > pattern.Length)
                throw new ArgumentException("Invalid capture group name");

            var value = 0;
            for (var i = 0; i < 4; i++)
            {
                var hex = HexToInt(pattern[pos++]);
                if (hex < 0)
                    throw new ArgumentException("Invalid capture group name");
                value = (value << 4) | hex;
            }

            builder.Append((char)value);
        }
    }
}
