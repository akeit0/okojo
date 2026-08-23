namespace Okojo.JavaScript.Parsing;

internal static class TemplateLiteralScanner
{
    public static bool TryDecodeEscape(
        string text,
        int slashIndex,
        out string decoded,
        out int consumed,
        out bool normalizeRawLineContinuation
    )
    {
        decoded = string.Empty;
        consumed = 1;
        normalizeRawLineContinuation = false;
        if (slashIndex + 1 >= text.Length)
            return false;

        var escape = text[slashIndex + 1];
        switch (escape)
        {
            case '`':
                decoded = "`";
                consumed = 2;
                return true;
            case '\'':
                decoded = "'";
                consumed = 2;
                return true;
            case '"':
                decoded = "\"";
                consumed = 2;
                return true;
            case '\\':
                decoded = "\\";
                consumed = 2;
                return true;
            case 'n':
                decoded = "\n";
                consumed = 2;
                return true;
            case 'r':
                decoded = "\r";
                consumed = 2;
                return true;
            case 't':
                decoded = "\t";
                consumed = 2;
                return true;
            case 'b':
                decoded = "\b";
                consumed = 2;
                return true;
            case 'f':
                decoded = "\f";
                consumed = 2;
                return true;
            case 'v':
                decoded = "\v";
                consumed = 2;
                return true;
            case '\n':
            case '\u2028':
            case '\u2029':
                consumed = 2;
                return true;
            case '\r':
                normalizeRawLineContinuation = true;
                consumed = slashIndex + 2 < text.Length && text[slashIndex + 2] == '\n' ? 3 : 2;
                return true;
            case '0':
                consumed = 2;
                if (slashIndex + 2 < text.Length && char.IsDigit(text[slashIndex + 2]))
                {
                    while (
                        slashIndex + consumed < text.Length
                        && consumed < 4
                        && text[slashIndex + consumed] is >= '0' and <= '7'
                    )
                        consumed++;
                    return false;
                }

                decoded = "\0";
                return true;
            case >= '1' and <= '9':
                consumed = 2;
                while (
                    slashIndex + consumed < text.Length
                    && consumed < 4
                    && text[slashIndex + consumed] is >= '0' and <= '7'
                )
                    consumed++;
                return false;
            case 'x':
                consumed = 2;
                if (
                    slashIndex + 3 < text.Length
                    && IsHexDigit(text[slashIndex + 2])
                    && IsHexDigit(text[slashIndex + 3])
                )
                {
                    decoded = (
                        (char)(HexToInt(text[slashIndex + 2]) * 16 + HexToInt(text[slashIndex + 3]))
                    ).ToString();
                    consumed = 4;
                    return true;
                }

                if (slashIndex + 2 < text.Length && IsHexDigit(text[slashIndex + 2]))
                    consumed = 3;
                return false;
            case 'u':
                consumed = 2;
                if (slashIndex + 2 < text.Length && text[slashIndex + 2] == '{')
                {
                    var index = slashIndex + 3;
                    long scalar = 0;
                    var digits = 0;
                    while (index < text.Length && IsHexDigit(text[index]))
                    {
                        scalar = scalar * 16 + HexToInt(text[index]);
                        digits++;
                        index++;
                    }

                    if (index < text.Length && text[index] == '}')
                    {
                        consumed = index - slashIndex + 1;
                        if (digits > 0 && scalar <= 0x10FFFF)
                        {
                            decoded = char.ConvertFromUtf32((int)scalar);
                            return true;
                        }

                        return false;
                    }

                    consumed = Math.Max(2, index - slashIndex);
                    return false;
                }

                if (
                    slashIndex + 5 < text.Length
                    && IsHexDigit(text[slashIndex + 2])
                    && IsHexDigit(text[slashIndex + 3])
                    && IsHexDigit(text[slashIndex + 4])
                    && IsHexDigit(text[slashIndex + 5])
                )
                {
                    decoded = (
                        (char)(
                            (HexToInt(text[slashIndex + 2]) << 12)
                            | (HexToInt(text[slashIndex + 3]) << 8)
                            | (HexToInt(text[slashIndex + 4]) << 4)
                            | HexToInt(text[slashIndex + 5])
                        )
                    ).ToString();
                    consumed = 6;
                    return true;
                }

                return false;
            default:
                decoded = escape.ToString();
                consumed = 2;
                return true;
        }
    }

    public static int FindExpressionEnd(string text, int start)
    {
        var depth = 1;
        var index = start;
        var lastSignificant = '\0';
        while (index < text.Length)
        {
            var value = text[index];
            if (value is '\'' or '"')
            {
                index = ConsumeQuotedString(text, index, value);
                lastSignificant = value;
                continue;
            }

            if (value == '`')
            {
                index = ConsumeNestedTemplateLiteral(text, index);
                lastSignificant = '`';
                continue;
            }

            if (value == '/' && index + 1 < text.Length)
            {
                if (text[index + 1] == '/')
                {
                    index = ConsumeLineComment(text, index);
                    continue;
                }

                if (text[index + 1] == '*')
                {
                    index = ConsumeBlockComment(text, index);
                    continue;
                }

                if (CanStartRegexLiteral(lastSignificant))
                {
                    index = ConsumeRegexLiteral(text, index);
                    lastSignificant = '/';
                    continue;
                }
            }

            if (value == '{')
            {
                depth++;
                index++;
                lastSignificant = '{';
                continue;
            }

            if (value == '}')
            {
                depth--;
                if (depth == 0)
                    return index;
                index++;
                lastSignificant = '}';
                continue;
            }

            if (!char.IsWhiteSpace(value))
                lastSignificant = value;
            index++;
        }

        return -1;
    }

    private static int ConsumeQuotedString(string text, int start, char quote)
    {
        var index = start + 1;
        while (index < text.Length)
        {
            if (text[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (text[index] == quote)
                return index + 1;
            index++;
        }

        return index;
    }

    private static int ConsumeNestedTemplateLiteral(string text, int start)
    {
        var index = start + 1;
        while (index < text.Length)
        {
            if (text[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (text[index] == '`')
                return index + 1;
            if (text[index] == '$' && index + 1 < text.Length && text[index + 1] == '{')
            {
                var expressionEnd = FindExpressionEnd(text, index + 2);
                if (expressionEnd < 0)
                    return text.Length;
                index = expressionEnd + 1;
                continue;
            }

            index++;
        }

        return index;
    }

    private static int ConsumeLineComment(string text, int start)
    {
        var index = start + 2;
        while (index < text.Length && text[index] is not ('\n' or '\r'))
            index++;
        return index;
    }

    private static int ConsumeBlockComment(string text, int start)
    {
        var index = start + 2;
        while (index + 1 < text.Length && !(text[index] == '*' && text[index + 1] == '/'))
            index++;
        return index + 1 < text.Length ? index + 2 : text.Length;
    }

    private static int ConsumeRegexLiteral(string text, int start)
    {
        var index = start + 1;
        var inCharacterClass = false;
        while (index < text.Length)
        {
            var value = text[index];
            if (value == '\\')
            {
                index += 2;
                continue;
            }

            if (value == '[')
                inCharacterClass = true;
            else if (value == ']' && inCharacterClass)
                inCharacterClass = false;
            else if (value == '/' && !inCharacterClass)
            {
                index++;
                while (index < text.Length && IsIdentifierPart(text[index]))
                    index++;
                return index;
            }
            index++;
        }

        return index;
    }

    private static bool CanStartRegexLiteral(char previous) =>
        previous == '\0'
        || previous
            is '('
                or '['
                or '{'
                or ','
                or ';'
                or ':'
                or '?'
                or '!'
                or '~'
                or '='
                or '+'
                or '-'
                or '*'
                or '%'
                or '^'
                or '&'
                or '|'
                or '<'
                or '>';

    private static bool IsIdentifierPart(char value) =>
        value == '_' || value == '$' || char.IsLetterOrDigit(value);

    private static bool IsHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static int HexToInt(char value) =>
        value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'a' and <= 'f' => 10 + value - 'a',
            >= 'A' and <= 'F' => 10 + value - 'A',
            _ => -1,
        };
}
