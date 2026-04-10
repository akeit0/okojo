#if NETSTANDARD2_1
using Rune = Okojo.Internals.Rune;
#else
using Rune = System.Text.Rune;
#endif
using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Okojo.Parsing;

internal enum JsTokenKind
{
    Eof,
    Identifier,
    PrivateIdentifier,
    Number,
    BigInt,
    String,
    Template,
    True,
    False,
    Null,
    Undefined,
    NaN,
    Infinity,
    Var,
    Let,
    Const,
    If,
    Else,
    Return,
    Function,
    For,
    While,
    Do,
    Break,
    Continue,
    Debugger,
    Typeof,
    Void,
    Delete,
    Switch,
    Case,
    Default,
    Throw,
    Try,
    Catch,
    Finally,
    With,
    In,
    Instanceof,
    Of,
    New,
    This,
    LeftParen,
    RightParen,
    LeftBrace,
    RightBrace,
    LeftBracket,
    RightBracket,
    At,
    Comma,
    Dot,
    Ellipsis,
    Semicolon,
    Colon,
    Question,
    NullishCoalescing,
    Plus,
    PlusPlus,
    Minus,
    MinusMinus,
    Star,
    Pow,
    PowAssign,
    Slash,
    Percent,
    Bang,
    Tilde,
    Assign,
    Arrow,
    PlusAssign,
    MinusAssign,
    StarAssign,
    SlashAssign,
    PercentAssign,
    Eq,
    Neq,
    StrictEq,
    StrictNeq,
    Lt,
    Lte,
    Gt,
    Gte,
    Shl,
    ShlAssign,
    Sar,
    SarAssign,
    Shr,
    ShrAssign,
    Ampersand,
    AmpersandAssign,
    Pipe,
    PipeAssign,
    Caret,
    CaretAssign,
    AndAnd,
    AndAndAssign,
    OrOr,
    OrOrAssign,
    NullishCoalescingAssign,
    ReservedWord
}

internal sealed partial class JsLexer(string source)
{
    private readonly StringBuilder sb = new();
    private readonly string source = source ?? throw new ArgumentNullException(nameof(source));
    private int index;

    public int GetIndex()
    {
        return index;
    }

    public JsToken NextToken()
    {
        var hasLineTerminatorBefore = SkipTrivia();
        if (index >= source.Length)
            return new(JsTokenKind.Eof, index, 0, hasLineTerminatorBefore: hasLineTerminatorBefore);

        var start = index;
        var c = source[index++];
        switch (c)
        {
            case '#':
                return ReadPrivateIdentifier(start, hasLineTerminatorBefore);
            case '(':
                return Tok(JsTokenKind.LeftParen, "(", start, hasLineTerminatorBefore);
            case ')':
                return Tok(JsTokenKind.RightParen, ")", start, hasLineTerminatorBefore);
            case '{':
                return Tok(JsTokenKind.LeftBrace, "{", start, hasLineTerminatorBefore);
            case '}':
                return Tok(JsTokenKind.RightBrace, "}", start, hasLineTerminatorBefore);
            case '[':
                return Tok(JsTokenKind.LeftBracket, "[", start, hasLineTerminatorBefore);
            case ']':
                return Tok(JsTokenKind.RightBracket, "]", start, hasLineTerminatorBefore);
            case '@':
                return Tok(JsTokenKind.At, "@", start, hasLineTerminatorBefore);
            case ',':
                return Tok(JsTokenKind.Comma, ",", start, hasLineTerminatorBefore);
            case '.':
                if (index + 1 < source.Length && source[index] == '.' && source[index + 1] == '.')
                {
                    index += 2;
                    return Tok(JsTokenKind.Ellipsis, "...", start, hasLineTerminatorBefore);
                }

                if (index < source.Length && char.IsDigit(source[index]))
                    return ReadNumber(start, c, hasLineTerminatorBefore);

                return Tok(JsTokenKind.Dot, ".", start, hasLineTerminatorBefore);
            case ';':
                return Tok(JsTokenKind.Semicolon, ";", start, hasLineTerminatorBefore);
            case ':':
                return Tok(JsTokenKind.Colon, ":", start, hasLineTerminatorBefore);
            case '?':
                if (Match('?'))
                    return Match('=')
                        ? Tok(JsTokenKind.NullishCoalescingAssign, "??=", start, hasLineTerminatorBefore)
                        : Tok(JsTokenKind.NullishCoalescing, "??", start, hasLineTerminatorBefore);

                return Tok(JsTokenKind.Question, "?", start, hasLineTerminatorBefore);
            case '+':
                if (Match('+')) return Tok(JsTokenKind.PlusPlus, "++", start, hasLineTerminatorBefore);

                return Match('=')
                    ? Tok(JsTokenKind.PlusAssign, "+=", start, hasLineTerminatorBefore)
                    : Tok(JsTokenKind.Plus, "+", start, hasLineTerminatorBefore);
            case '-':
                if (Match('-')) return Tok(JsTokenKind.MinusMinus, "--", start, hasLineTerminatorBefore);

                return Match('=')
                    ? Tok(JsTokenKind.MinusAssign, "-=", start, hasLineTerminatorBefore)
                    : Tok(JsTokenKind.Minus, "-", start, hasLineTerminatorBefore);
            case '*':
                if (Match('*'))
                    return Match('=')
                        ? Tok(JsTokenKind.PowAssign, "**=", start, hasLineTerminatorBefore)
                        : Tok(JsTokenKind.Pow, "**", start, hasLineTerminatorBefore);

                return Match('=')
                    ? Tok(JsTokenKind.StarAssign, "*=", start, hasLineTerminatorBefore)
                    : Tok(JsTokenKind.Star, "*", start, hasLineTerminatorBefore);
            case '/':
                return Match('=')
                    ? Tok(JsTokenKind.SlashAssign, "/=", start, hasLineTerminatorBefore)
                    : Tok(JsTokenKind.Slash, "/", start, hasLineTerminatorBefore);
            case '%':
                return Match('=')
                    ? Tok(JsTokenKind.PercentAssign, "%=", start, hasLineTerminatorBefore)
                    : Tok(JsTokenKind.Percent, "%", start, hasLineTerminatorBefore);
            case '!':
                if (Match('='))
                    return Match('=')
                        ? Tok(JsTokenKind.StrictNeq, "!==", start, hasLineTerminatorBefore)
                        : Tok(JsTokenKind.Neq, "!=", start, hasLineTerminatorBefore);

                return Tok(JsTokenKind.Bang, "!", start, hasLineTerminatorBefore);
            case '~':
                return Tok(JsTokenKind.Tilde, "~", start, hasLineTerminatorBefore);
            case '=':
                if (Match('>')) return Tok(JsTokenKind.Arrow, "=>", start, hasLineTerminatorBefore);

                if (Match('='))
                    return Match('=')
                        ? Tok(JsTokenKind.StrictEq, "===", start, hasLineTerminatorBefore)
                        : Tok(JsTokenKind.Eq, "==", start, hasLineTerminatorBefore);

                return Tok(JsTokenKind.Assign, "=", start, hasLineTerminatorBefore);
            case '<':
                if (Match('<'))
                    return Match('=')
                        ? Tok(JsTokenKind.ShlAssign, "<<=", start, hasLineTerminatorBefore)
                        : Tok(JsTokenKind.Shl, "<<", start, hasLineTerminatorBefore);

                return Match('=')
                    ? Tok(JsTokenKind.Lte, "<=", start, hasLineTerminatorBefore)
                    : Tok(JsTokenKind.Lt, "<", start, hasLineTerminatorBefore);
            case '>':
                if (Match('>'))
                {
                    if (Match('>'))
                        return Match('=')
                            ? Tok(JsTokenKind.ShrAssign, ">>>=", start, hasLineTerminatorBefore)
                            : Tok(JsTokenKind.Shr, ">>>", start, hasLineTerminatorBefore);

                    return Match('=')
                        ? Tok(JsTokenKind.SarAssign, ">>=", start, hasLineTerminatorBefore)
                        : Tok(JsTokenKind.Sar, ">>", start, hasLineTerminatorBefore);
                }

                return Match('=')
                    ? Tok(JsTokenKind.Gte, ">=", start, hasLineTerminatorBefore)
                    : Tok(JsTokenKind.Gt, ">", start, hasLineTerminatorBefore);
            case '&':
                if (Match('&'))
                    return Match('=')
                        ? Tok(JsTokenKind.AndAndAssign, "&&=", start, hasLineTerminatorBefore)
                        : Tok(JsTokenKind.AndAnd, "&&", start, hasLineTerminatorBefore);

                return Match('=')
                    ? Tok(JsTokenKind.AmpersandAssign, "&=", start, hasLineTerminatorBefore)
                    : Tok(JsTokenKind.Ampersand, "&", start, hasLineTerminatorBefore);
            case '|':
                if (Match('|'))
                    return Match('=')
                        ? Tok(JsTokenKind.OrOrAssign, "||=", start, hasLineTerminatorBefore)
                        : Tok(JsTokenKind.OrOr, "||", start, hasLineTerminatorBefore);

                return Match('=')
                    ? Tok(JsTokenKind.PipeAssign, "|=", start, hasLineTerminatorBefore)
                    : Tok(JsTokenKind.Pipe, "|", start, hasLineTerminatorBefore);
            case '^':
                return Match('=')
                    ? Tok(JsTokenKind.CaretAssign, "^=", start, hasLineTerminatorBefore)
                    : Tok(JsTokenKind.Caret, "^", start, hasLineTerminatorBefore);
            case '"':
            case '\'':
                return ReadString(c, start, hasLineTerminatorBefore);
            case '`':
                return ReadTemplate(start, hasLineTerminatorBefore);
            default:
                if (char.IsDigit(c) || (c == '.' && index < source.Length && char.IsDigit(source[index])))
                    return ReadNumber(start, c, hasLineTerminatorBefore);

                if (IsIdentifierStart(c)) return ReadIdentifier(start, c, hasLineTerminatorBefore);

                if (char.IsHighSurrogate(c) &&
                    index < source.Length &&
                    char.IsLowSurrogate(source[index]))
                {
                    var pair = string.Concat(c, source[index]);
                    if (IsIdentifierStartText(pair))
                    {
                        index++;
                        return ReadIdentifier(start, pair, hasLineTerminatorBefore);
                    }
                }

                if (c == '\\')
                {
                    if (!TryReadUnicodeEscapeAfterBackslash(out var escapedText) ||
                        escapedText.Length == 0 ||
                        !IsIdentifierStart(escapedText[0]))
                        throw Error($"Unexpected character '{c}'", start);

                    return ReadIdentifier(start, escapedText, hasLineTerminatorBefore);
                }

                throw Error($"Unexpected character '{c}'", start);
        }
    }

    public void SetIndex(int index)
    {
        if ((uint)index > (uint)source.Length) throw new ArgumentOutOfRangeException(nameof(index));

        this.index = index;
    }

    private bool SkipTrivia()
    {
        var hasLineTerminator = false;
        while (index < source.Length)
        {
            if (index == 0 && source.Length >= 2 && source[0] == '#' && source[1] == '!')
            {
                index = 2;
                while (index < source.Length &&
                       source[index] is not ('\n' or '\r' or '\u2028' or '\u2029'))
                    index++;

                continue;
            }

            if (IsEcmaTrivia(source[index]))
            {
                if (IsLineTerminator(source[index])) hasLineTerminator = true;

                index++;
                continue;
            }

            if (source[index] == '/' && index + 1 < source.Length)
            {
                if (source[index + 1] == '/')
                {
                    index += 2;
                    var sawLineTerminator = false;
                    while (index < source.Length &&
                           source[index] is not ('\n' or '\r' or '\u2028' or '\u2029'))
                        index++;

                    if (index < source.Length && source[index] is '\n' or '\r' or '\u2028' or '\u2029')
                        sawLineTerminator = true;

                    if (sawLineTerminator) hasLineTerminator = true;

                    continue;
                }

                if (source[index + 1] == '*')
                {
                    index += 2;
                    while (index + 1 < source.Length && !(source[index] == '*' && source[index + 1] == '/'))
                    {
                        if (source[index] is '\n' or '\r' or '\u2028' or '\u2029') hasLineTerminator = true;

                        index++;
                    }

                    if (index + 1 >= source.Length) throw Error("Unterminated block comment", index);

                    index += 2;
                    continue;
                }
            }

            break;
        }

        return hasLineTerminator;
    }

    private JsToken ReadString(char quote, int start, bool hasLineTerminatorBefore)
    {
        var sb = this.sb;
        sb.Clear();
        while (index < source.Length)
        {
            var c = source[index++];
            if (c == quote)
                return new(JsTokenKind.String, start, index - start, dataIndex: AddStringLiteral(sb.ToString()),
                    hasLineTerminatorBefore: hasLineTerminatorBefore);

            if (c == '\\')
            {
                if (index >= source.Length) throw Error("Unterminated string", start);

                var escape = source[index++];
                if (escape is '\n' or '\u2028' or '\u2029')
                    // Line continuation: backslash + line terminator is removed from the literal.
                    continue;

                if (escape == '\r')
                {
                    // Handle CRLF line continuation.
                    if (index < source.Length && source[index] == '\n') index++;

                    continue;
                }

                if (escape == 'u')
                {
                    index--;
                    if (!TryReadUnicodeEscapeAfterBackslash(out var escapedText))
                        throw Error("Invalid unicode escape", start);
                    sb.Append(escapedText);
                    continue;
                }

                if (escape == 'x')
                {
                    if (index + 1 >= source.Length) throw Error("Invalid hex escape", start);

                    var h0 = source[index++];
                    var h1 = source[index++];
                    if (!IsHexDigit(h0) || !IsHexDigit(h1)) throw Error("Invalid hex escape", start);

                    sb.Append((char)((HexToInt(h0) << 4) | HexToInt(h1)));
                    continue;
                }

                if (escape is >= '0' and <= '7')
                {
                    sb.Append(ReadLegacyOctalEscape(escape));
                    continue;
                }

                sb.Append(escape switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'b' => '\b',
                    'f' => '\f',
                    'v' => '\v',
                    '\\' => '\\',
                    '\'' => '\'',
                    '"' => '"',
                    '0' => '\0',
                    _ => escape
                });
                continue;
            }

            if (c is '\n' or '\r') throw Error("Unterminated string", start);

            sb.Append(c);
        }

        throw Error("Unterminated string", start);
    }

    private char ReadLegacyOctalEscape(char firstDigit)
    {
        var maxAdditionalDigits = firstDigit is >= '4' and <= '7' ? 1 : 2;
        var value = firstDigit - '0';
        var consumed = 0;

        while (consumed < maxAdditionalDigits &&
               index < source.Length &&
               source[index] is >= '0' and <= '7')
        {
            value = (value << 3) | (source[index] - '0');
            index++;
            consumed++;
        }

        return (char)value;
    }

    private JsToken ReadTemplate(int start, bool hasLineTerminatorBefore)
    {
        var sb = this.sb;
        sb.Clear();
        var expressionDepth = 0;
        while (index < source.Length)
        {
            var c = source[index++];
            if (c == '\\')
            {
                if (index >= source.Length) throw Error("Unterminated template literal", start);

                var escape = source[index++];
                sb.Append(c);
                sb.Append(escape);
                continue;
            }

            if (expressionDepth == 0)
            {
                if (c == '`')
                    return new(JsTokenKind.Template, start, index - start, dataIndex: AddStringLiteral(sb.ToString()),
                        hasLineTerminatorBefore: hasLineTerminatorBefore);

                if (c == '$' && index < source.Length && source[index] == '{')
                {
                    expressionDepth = 1;
                    sb.Append(c);
                    sb.Append(source[index++]);
                    continue;
                }

                sb.Append(c);
                continue;
            }

            if (c == '{')
            {
                expressionDepth++;
                sb.Append(c);
                continue;
            }

            if (c == '}') expressionDepth--;

            sb.Append(c);
        }

        throw Error("Unterminated template literal", start);
    }

    private JsToken ReadNumber(int start, char firstChar, bool hasLineTerminatorBefore)
    {
        if (firstChar == '0' && TryReadPrefixedIntegerLiteral(start, hasLineTerminatorBefore, out var prefixedToken))
            return prefixedToken;

        return ReadDecimalOrFloatLiteral(start, firstChar, hasLineTerminatorBefore);
    }

    private bool TryReadPrefixedIntegerLiteral(int start, bool hasLineTerminatorBefore, out JsToken token)
    {
        token = default;
        if (index >= source.Length)
            return false;

        var prefix = source[index];
        switch (prefix)
        {
            case 'x':
            case 'X':
                token = ReadHexIntegerLiteral(start, hasLineTerminatorBefore);
                return true;
            case 'b':
            case 'B':
                token = ReadRadixIntegerLiteral(start, hasLineTerminatorBefore, 2, "Invalid binary literal");
                return true;
            case 'o':
            case 'O':
                token = ReadRadixIntegerLiteral(start, hasLineTerminatorBefore, 8, "Invalid octal literal");
                return true;
            default:
                return false;
        }
    }

    private JsToken ReadHexIntegerLiteral(int start, bool hasLineTerminatorBefore)
    {
        index++; // consume x/X
        var digitStart = index;
        var hadSeparator = ReadSeparatedDigits(IsHexDigit);

        if (index == digitStart)
            throw Error("Invalid hexadecimal literal", start);

        var digitEnd = index;
        var isBigInt = Match('n');
        var rawLength = index - start;
        var digitSpan = source.AsSpan(digitStart, digitEnd - digitStart);
        if (!hadSeparator)
        {
            if (isBigInt)
                return new(JsTokenKind.BigInt, start, rawLength,
                    dataIndex: AddBigIntLiteral(new(ParseBigInteger(digitSpan, 16))),
                    hasLineTerminatorBefore: hasLineTerminatorBefore);

            if (!long.TryParse(digitSpan, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex))
                throw Error($"Invalid numeric literal '{source.Substring(start, rawLength)}'", start);

            return new(JsTokenKind.Number, start, rawLength,
                hex,
                hasLineTerminatorBefore: hasLineTerminatorBefore);
        }

        char[]? rented = null;
        try
        {
            var normalized = digitSpan.Length <= NumericSeparatorStackallocThreshold
                ? stackalloc char[digitSpan.Length]
                : rented = ArrayPool<char>.Shared.Rent(digitSpan.Length);
            CopyWithoutNumericSeparators(digitSpan, normalized, out var normalizedLength);
            var parseDigits = normalized[..normalizedLength];

            if (isBigInt)
                return new(JsTokenKind.BigInt, start, rawLength,
                    dataIndex: AddBigIntLiteral(new(ParseBigInteger(parseDigits, 16))),
                    hasLineTerminatorBefore: hasLineTerminatorBefore);

            if (!long.TryParse(parseDigits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex))
                throw Error($"Invalid numeric literal '{source.Substring(start, rawLength)}'", start);

            return new(JsTokenKind.Number, start, rawLength,
                hex,
                hasLineTerminatorBefore: hasLineTerminatorBefore);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    private JsToken ReadRadixIntegerLiteral(int start, bool hasLineTerminatorBefore, int radix, string errorMessage)
    {
        index++; // consume radix prefix char
        var digitStart = index;
        var hadSeparator = ReadSeparatedDigits(c => IsRadixDigit(c, radix));

        if (index == digitStart)
            throw Error(errorMessage, start);

        var digitEnd = index;
        var isBigInt = Match('n');
        var rawLength = index - start;
        var digitSpan = source.AsSpan(digitStart, digitEnd - digitStart);
        if (!hadSeparator)
        {
            if (isBigInt)
                return new(JsTokenKind.BigInt, start, rawLength,
                    dataIndex: AddBigIntLiteral(new(ParseBigInteger(digitSpan, radix))),
                    hasLineTerminatorBefore: hasLineTerminatorBefore);

            double number = 0;
            for (var i = 0; i < digitSpan.Length; i++)
                number = number * radix + (digitSpan[i] - '0');

            return new(JsTokenKind.Number, start, rawLength, number,
                hasLineTerminatorBefore: hasLineTerminatorBefore);
        }

        char[]? rented = null;
        try
        {
            var normalized = digitSpan.Length <= NumericSeparatorStackallocThreshold
                ? stackalloc char[digitSpan.Length]
                : rented = ArrayPool<char>.Shared.Rent(digitSpan.Length);
            CopyWithoutNumericSeparators(digitSpan, normalized, out var normalizedLength);
            var parseDigits = normalized[..normalizedLength];

            if (isBigInt)
                return new(JsTokenKind.BigInt, start, rawLength,
                    dataIndex: AddBigIntLiteral(new(ParseBigInteger(parseDigits, radix))),
                    hasLineTerminatorBefore: hasLineTerminatorBefore);

            double number = 0;
            for (var i = 0; i < parseDigits.Length; i++)
                number = number * radix + (parseDigits[i] - '0');

            return new(JsTokenKind.Number, start, rawLength, number,
                hasLineTerminatorBefore: hasLineTerminatorBefore);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    private JsToken ReadDecimalOrFloatLiteral(int start, char firstChar, bool hasLineTerminatorBefore)
    {
        var hadSeparator = false;
        var sawDecimalPoint = firstChar == '.';
        if (char.IsDigit(firstChar))
        {
            var sawDigit = true;
            var previousWasSeparator = false;
            while (index < source.Length)
            {
                var c = source[index];
                if (char.IsDigit(c))
                {
                    sawDigit = true;
                    previousWasSeparator = false;
                    index++;
                    continue;
                }

                if (c == '_')
                {
                    if (!sawDigit || previousWasSeparator)
                        throw Error("Invalid numeric separator", index);
                    if (index + 1 >= source.Length || !char.IsDigit(source[index + 1]))
                        throw Error("Invalid numeric separator", index);
                    hadSeparator = true;
                    previousWasSeparator = true;
                    index++;
                    continue;
                }

                break;
            }
        }
        else
        {
            hadSeparator |= ReadSeparatedDigits(char.IsDigit);
        }

        if (index < source.Length && source[index] == '.')
        {
            sawDecimalPoint = true;
            index++;
            hadSeparator |= ReadSeparatedDigits(char.IsDigit);
        }

        var sawExponent = false;
        if (index < source.Length && (source[index] == 'e' || source[index] == 'E'))
        {
            sawExponent = true;
            index++;
            if (index < source.Length && (source[index] == '+' || source[index] == '-'))
                index++;

            hadSeparator |= ReadSeparatedDigits(char.IsDigit);
        }

        if (index < source.Length && source[index] == 'n')
        {
            if (sawDecimalPoint || sawExponent)
                throw Error("Invalid BigInt literal", start);

            index++;
            var bigintDigits = source.AsSpan(start, index - start - 1);
            if (!hadSeparator)
                return new(JsTokenKind.BigInt, start, index - start,
                    dataIndex: AddBigIntLiteral(new(ParseBigInteger(bigintDigits, 10))),
                    hasLineTerminatorBefore: hasLineTerminatorBefore);

            char[]? rented = null;
            try
            {
                var normalized = bigintDigits.Length <= NumericSeparatorStackallocThreshold
                    ? stackalloc char[bigintDigits.Length]
                    : rented = ArrayPool<char>.Shared.Rent(bigintDigits.Length);
                CopyWithoutNumericSeparators(bigintDigits, normalized, out var normalizedLength);
                return new(JsTokenKind.BigInt, start, index - start,
                    dataIndex: AddBigIntLiteral(new(ParseBigInteger(normalized[..normalizedLength],
                        10))),
                    hasLineTerminatorBefore: hasLineTerminatorBefore);
            }
            finally
            {
                if (rented is not null)
                    ArrayPool<char>.Shared.Return(rented);
            }
        }

        var rawLength = index - start;
        var numberSpan = source.AsSpan(start, rawLength);
        if (!hadSeparator)
        {
            if (!double.TryParse(numberSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                throw Error($"Invalid numeric literal '{source.Substring(start, rawLength)}'", start);

            return new(JsTokenKind.Number, start, rawLength, number,
                hasLineTerminatorBefore: hasLineTerminatorBefore);
        }

        char[]? rentedNumber = null;
        try
        {
            var normalized = numberSpan.Length <= NumericSeparatorStackallocThreshold
                ? stackalloc char[numberSpan.Length]
                : rentedNumber = ArrayPool<char>.Shared.Rent(numberSpan.Length);
            CopyWithoutNumericSeparators(numberSpan, normalized, out var normalizedLength);
            if (!double.TryParse(normalized[..normalizedLength], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var number))
                throw Error($"Invalid numeric literal '{source.Substring(start, rawLength)}'", start);

            return new(JsTokenKind.Number, start, rawLength, number,
                hasLineTerminatorBefore: hasLineTerminatorBefore);
        }
        finally
        {
            if (rentedNumber is not null)
                ArrayPool<char>.Shared.Return(rentedNumber);
        }
    }

    private bool ReadSeparatedDigits(Func<char, bool> isDigit, bool sawDigitAlready = false)
    {
        var sawDigit = sawDigitAlready;
        var hadSeparator = false;
        var previousWasSeparator = false;

        while (index < source.Length)
        {
            var c = source[index];
            if (isDigit(c))
            {
                sawDigit = true;
                previousWasSeparator = false;
                index++;
                continue;
            }

            if (c == '_')
            {
                if (!sawDigit || previousWasSeparator)
                    throw Error("Invalid numeric separator", index);
                if (index + 1 >= source.Length || !isDigit(source[index + 1]))
                    throw Error("Invalid numeric separator", index);
                hadSeparator = true;
                previousWasSeparator = true;
                index++;
                continue;
            }

            break;
        }

        return hadSeparator;
    }

    private static bool IsRadixDigit(char c, int radix)
    {
        return radix switch
        {
            2 => c is '0' or '1',
            8 => c >= '0' && c <= '7',
            _ => throw new ArgumentOutOfRangeException(nameof(radix))
        };
    }

    private static BigInteger ParseBigInteger(ReadOnlySpan<char> digits, int radix)
    {
        if (radix == 10)
            return BigInteger.Parse(digits, CultureInfo.InvariantCulture);

        var value = BigInteger.Zero;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[i] switch
            {
                >= '0' and <= '9' => digits[i] - '0',
                >= 'a' and <= 'f' => 10 + (digits[i] - 'a'),
                >= 'A' and <= 'F' => 10 + (digits[i] - 'A'),
                _ => throw new FormatException("Invalid BigInt digit")
            };

            value = value * radix + digit;
        }

        return value;
    }

    private JsToken ReadPrivateIdentifier(int start, bool hasLineTerminatorBefore)
    {
        if (index >= source.Length) throw Error("Unexpected character '#'", start);

        var next = source[index];
        string firstText;
        if (next == '\\')
        {
            index++;
            if (!TryReadUnicodeEscapeAfterBackslash(out firstText) ||
                firstText.Length == 0 ||
                !IsIdentifierStartText(firstText))
                throw Error("Invalid private identifier", start);
        }
        else
        {
            if (char.IsHighSurrogate(next) &&
                index + 1 < source.Length &&
                char.IsLowSurrogate(source[index + 1]))
            {
                firstText = string.Concat(next, source[index + 1]);
                if (!IsIdentifierStartText(firstText)) throw Error("Invalid private identifier", start);

                index += 2;
                var token2 = ReadIdentifier(start + 1, firstText, hasLineTerminatorBefore);
                return new(JsTokenKind.PrivateIdentifier, start, index - start, dataIndex: token2.DataIndex,
                    hasLineTerminatorBefore: hasLineTerminatorBefore);
            }

            if (!IsIdentifierStart(next)) throw Error("Invalid private identifier", start);

            index++;
            firstText = new(next, 1);
        }

        var token = ReadIdentifier(start + 1, firstText, hasLineTerminatorBefore);
        return new(JsTokenKind.PrivateIdentifier, start, index - start, dataIndex: token.DataIndex,
            hasLineTerminatorBefore: hasLineTerminatorBefore);
    }

    private JsToken ReadIdentifier(int start, char firstChar, bool hasLineTerminatorBefore)
    {
        var asciiKeywordCandidate = firstChar is >= 'A' and <= 'z';
        while (index < source.Length)
        {
            var c = source[index];
            if (IsIdentifierPart(c))
            {
                asciiKeywordCandidate &= c is >= 'A' and <= 'z';
                index++;
                continue;
            }

            if (c == '\\') return ReadIdentifierSlow(start, hasLineTerminatorBefore);

            break;
        }

        var rawSpan = source.AsSpan(start, index - start);
        if (asciiKeywordCandidate)
        {
            if (TryGetKeywordKind(rawSpan, out var kind))
                return new(kind, start, rawSpan.Length,
                    hasLineTerminatorBefore: hasLineTerminatorBefore);

            if (IsReservedWord(rawSpan))
                return new(JsTokenKind.ReservedWord, start, rawSpan.Length,
                    hasLineTerminatorBefore: hasLineTerminatorBefore);
        }

        return new(JsTokenKind.Identifier, start, rawSpan.Length,
            dataIndex: AddIdentifierLiteral(rawSpan),
            hasLineTerminatorBefore: hasLineTerminatorBefore);
    }

    private JsToken ReadIdentifier(int start, string firstText, bool hasLineTerminatorBefore)
    {
        if (firstText.Length == 1 &&
            start < source.Length &&
            source[start] == firstText[0] &&
            source[start] != '\\')
            return ReadIdentifier(start, firstText[0], hasLineTerminatorBefore);

        return ReadIdentifierSlow(start, hasLineTerminatorBefore, firstText);
    }

    private JsToken ReadIdentifierSlow(int start, bool hasLineTerminatorBefore, string? firstText = null)
    {
        var builder = new PooledCharBuilder(stackalloc char[64]);
        try
        {
            if (firstText is null)
                builder.Append(source.AsSpan(start, index - start));
            else
                builder.Append(firstText);

            var asciiKeywordCandidate = builder.Length == 1 && builder.AsSpan()[0] is >= 'A' and <= 'z';
            while (index < source.Length)
            {
                var c = source[index];
                if (IsIdentifierPart(c))
                {
                    builder.Append(c);
                    asciiKeywordCandidate &= c is >= 'A' and <= 'z';
                    index++;
                    continue;
                }

                if (c == '\\')
                {
                    index++;
                    if (!TryReadUnicodeEscapeCodePointAfterBackslash(out var codePoint) ||
                        !IsIdentifierPartCodePoint(codePoint))
                        throw Error("Invalid identifier escape", index);

                    builder.AppendRune(codePoint);
                    asciiKeywordCandidate = false;
                    continue;
                }

                if (char.IsHighSurrogate(c) &&
                    index + 1 < source.Length &&
                    char.IsLowSurrogate(source[index + 1]) &&
                    Rune.TryGetRuneAt(source, index, out var rune) &&
                    IsIdentifierPartRune(rune))
                {
                    builder.Append(source.AsSpan(index, 2));
                    asciiKeywordCandidate = false;
                    index += 2;
                    continue;
                }

                break;
            }

            if (asciiKeywordCandidate)
            {
                var rawIdentifier = source.AsSpan(start, index - start);
                if (TryGetKeywordKind(rawIdentifier, out var kind))
                    return new(kind, start, index - start,
                        hasLineTerminatorBefore: hasLineTerminatorBefore);

                if (IsReservedWord(rawIdentifier))
                    return new(JsTokenKind.ReservedWord, start, index - start,
                        hasLineTerminatorBefore: hasLineTerminatorBefore);
            }

            return new(JsTokenKind.Identifier, start, index - start, dataIndex: AddIdentifierLiteral(builder.AsSpan()),
                hasLineTerminatorBefore: hasLineTerminatorBefore);
        }
        finally
        {
            builder.Dispose();
        }
    }

    private bool TryReadUnicodeEscapeAfterBackslash(out string escaped)
    {
        escaped = string.Empty;
        if (!TryReadUnicodeEscapeCodePointAfterBackslash(out var codePoint)) return false;

        escaped = codePoint <= char.MaxValue
            ? new((char)codePoint, 1)
            : char.ConvertFromUtf32(codePoint);
        return true;
    }

    private bool TryReadUnicodeEscapeCodePointAfterBackslash(out int codePoint)
    {
        codePoint = 0;
        if (index >= source.Length || source[index] != 'u') return false;

        index++;
        if (index < source.Length && source[index] == '{')
        {
            index++;
            var digitsStart = index;
            var digits = 0;
            while (index < source.Length && source[index] != '}')
            {
                var c = source[index];
                if (!IsHexDigit(c)) return false;

                codePoint = (codePoint << 4) | HexToInt(c);
                digits++;
                if (digits > 6) return false;

                index++;
            }

            if (index >= source.Length || source[index] != '}' || index == digitsStart) return false;

            index++;
            return codePoint <= 0x10FFFF;
        }

        if (index + 3 >= source.Length) return false;

        var h0 = source[index++];
        var h1 = source[index++];
        var h2 = source[index++];
        var h3 = source[index++];
        if (!IsHexDigit(h0) || !IsHexDigit(h1) || !IsHexDigit(h2) || !IsHexDigit(h3)) return false;

        codePoint = (HexToInt(h0) << 12) | (HexToInt(h1) << 8) | (HexToInt(h2) << 4) | HexToInt(h3);
        return true;
    }

    private bool Match(char expected)
    {
        if (index >= source.Length || source[index] != expected) return false;

        index++;
        return true;
    }

    private static bool IsIdentifierStart(char c)
    {
        if (c is '_' or '$') return true;

        if (c >= 0x80 && !IsEcmaTrivia(c) && !char.IsControl(c))
            return char.GetUnicodeCategory(c) != UnicodeCategory.Format;

        return char.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.UppercaseLetter => true,
            UnicodeCategory.LowercaseLetter => true,
            UnicodeCategory.TitlecaseLetter => true,
            UnicodeCategory.ModifierLetter => true,
            UnicodeCategory.OtherLetter => true,
            UnicodeCategory.LetterNumber => true,
            _ => false
        };
    }

    private static bool IsIdentifierPart(char c)
    {
        if (IsIdentifierStart(c)) return true;

        if (c is '\u200C' or '\u200D') return true;

        return char.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.DecimalDigitNumber => true,
            UnicodeCategory.ConnectorPunctuation => true,
            UnicodeCategory.NonSpacingMark => true,
            UnicodeCategory.SpacingCombiningMark => true,
            _ => false
        };
    }

    private static bool IsIdentifierStartText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        if (text.Length == 1) return IsIdentifierStart(text[0]);

        if (!Rune.TryGetRuneAt(text, 0, out var rune)) return false;

        return IsIdentifierStartRune(rune);
    }

    private static bool IsIdentifierPartText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        if (text.Length == 1) return IsIdentifierPart(text[0]);

        if (!Rune.TryGetRuneAt(text, 0, out var rune)) return false;

        return IsIdentifierPartRune(rune);
    }

    private static bool IsIdentifierPartCodePoint(int codePoint)
    {
        if (codePoint <= char.MaxValue) return IsIdentifierPart((char)codePoint);

        return IsIdentifierPartRune(new(codePoint));
    }

    private static bool IsIdentifierStartRune(Rune rune)
    {
        if (rune.Value >= 0x80 && !IsEcmaTrivia(rune) && Rune.GetUnicodeCategory(rune) != UnicodeCategory.Control)
            return Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format;

        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.UppercaseLetter => true,
            UnicodeCategory.LowercaseLetter => true,
            UnicodeCategory.TitlecaseLetter => true,
            UnicodeCategory.ModifierLetter => true,
            UnicodeCategory.OtherLetter => true,
            UnicodeCategory.LetterNumber => true,
            _ => false
        };
    }

    private static bool IsIdentifierPartRune(Rune rune)
    {
        if (IsIdentifierStartRune(rune)) return true;

        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.DecimalDigitNumber => true,
            UnicodeCategory.ConnectorPunctuation => true,
            UnicodeCategory.NonSpacingMark => true,
            UnicodeCategory.SpacingCombiningMark => true,
            _ => false
        };
    }

    private static bool IsEcmaTrivia(char c)
    {
        return c == '\uFEFF' || char.IsWhiteSpace(c);
    }

    private static bool IsEcmaTrivia(Rune rune)
    {
        return rune.Value == 0xFEFF || Rune.IsWhiteSpace(rune);
    }

    private static bool IsLineTerminator(char c)
    {
        return c is '\n' or '\r' or '\u2028' or '\u2029';
    }

    private static bool IsHexDigit(char c)
    {
        return c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    private static JsToken Tok(JsTokenKind kind, string text, int position, bool hasLineTerminatorBefore)
    {
        return new(kind, position, text.Length, hasLineTerminatorBefore: hasLineTerminatorBefore);
    }

    private static int HexToInt(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => 10 + (c - 'a'),
            >= 'A' and <= 'F' => 10 + (c - 'A'),
            _ => -1
        };
    }

    private JsParseException Error(string message, int position)
    {
        return new(message, position, source);
    }

    private static bool TryGetKeywordKind(ReadOnlySpan<char> text, out JsTokenKind kind)
    {
        switch (text)
        {
            case "do":
                kind = JsTokenKind.Do;
                return true;
            case "if":
                kind = JsTokenKind.If;
                return true;
            case "in":
                kind = JsTokenKind.In;
                return true;
            case "of":
                kind = JsTokenKind.Of;
                return true;
            case "for":
                kind = JsTokenKind.For;
                return true;
            case "let":
                kind = JsTokenKind.Let;
                return true;
            case "new":
                kind = JsTokenKind.New;
                return true;
            case "try":
                kind = JsTokenKind.Try;
                return true;
            case "var":
                kind = JsTokenKind.Var;
                return true;
            case "case":
                kind = JsTokenKind.Case;
                return true;
            case "else":
                kind = JsTokenKind.Else;
                return true;
            case "null":
                kind = JsTokenKind.Null;
                return true;
            case "this":
                kind = JsTokenKind.This;
                return true;
            case "true":
                kind = JsTokenKind.True;
                return true;
            case "void":
                kind = JsTokenKind.Void;
                return true;
            case "with":
                kind = JsTokenKind.With;
                return true;
            case "break":
                kind = JsTokenKind.Break;
                return true;
            case "catch":
                kind = JsTokenKind.Catch;
                return true;
            case "const":
                kind = JsTokenKind.Const;
                return true;
            case "false":
                kind = JsTokenKind.False;
                return true;
            case "throw":
                kind = JsTokenKind.Throw;
                return true;
            case "while":
                kind = JsTokenKind.While;
                return true;
            case "delete":
                kind = JsTokenKind.Delete;
                return true;
            case "return":
                kind = JsTokenKind.Return;
                return true;
            case "switch":
                kind = JsTokenKind.Switch;
                return true;
            case "typeof":
                kind = JsTokenKind.Typeof;
                return true;
            case "default":
                kind = JsTokenKind.Default;
                return true;
            case "finally":
                kind = JsTokenKind.Finally;
                return true;
            case "continue":
                kind = JsTokenKind.Continue;
                return true;
            case "debugger":
                kind = JsTokenKind.Debugger;
                return true;
            case "function":
                kind = JsTokenKind.Function;
                return true;
            case "instanceof":
                kind = JsTokenKind.Instanceof;
                return true;
        }


        kind = default;
        return false;
    }

    private static bool IsReservedWord(ReadOnlySpan<char> text)
    {
        switch (text.Length)
        {
            case 4:
                return text is "enum";
            case 5:
                return text is "class" or "super";
            case 6:
                return text is "export" or "import";
            case 7:
                return text is "extends";
            default:
                return false;
        }
    }
}
