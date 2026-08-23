using System.Text;

namespace Okojo.JavaScript.Parsing;

internal static class RegExpLiteralScanner
{
    public static Result Scan(string source, int start)
    {
        var index = start + 1;
        var inCharacterClass = false;
        var escaped = false;
        while (index < source.Length)
        {
            var ch = source[index];
            if (ch is '\n' or '\r' or '\u2028' or '\u2029')
                throw new JsParseException(
                    "Unterminated regular expression literal",
                    start,
                    source
                );

            if (!escaped)
            {
                if (ch == '[')
                    inCharacterClass = true;
                else if (ch == ']' && inCharacterClass)
                    inCharacterClass = false;
                else if (ch == '/' && !inCharacterClass)
                    break;
            }

            escaped = !escaped && ch == '\\';
            index++;
        }

        if (index >= source.Length || source[index] != '/')
            throw new JsParseException("Unterminated regular expression literal", start, source);

        var pattern = source.Substring(start + 1, index - start - 1);
        index++;
        var flags = new StringBuilder();
        while (index < source.Length)
        {
            var ch = source[index];
            if (IsIdentifierPart(ch))
            {
                flags.Append(ch);
                index++;
                continue;
            }

            if (
                ch == '\\'
                && index + 5 < source.Length
                && source[index + 1] == 'u'
                && IsHexDigit(source[index + 2])
                && IsHexDigit(source[index + 3])
                && IsHexDigit(source[index + 4])
                && IsHexDigit(source[index + 5])
            )
            {
                flags.Append(
                    (char)(
                        (HexToInt(source[index + 2]) << 12)
                        | (HexToInt(source[index + 3]) << 8)
                        | (HexToInt(source[index + 4]) << 4)
                        | HexToInt(source[index + 5])
                    )
                );
                index += 6;
                continue;
            }

            break;
        }

        return new(pattern, flags.ToString(), index);
    }

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

    internal readonly record struct Result(string Pattern, string Flags, int End);
}
