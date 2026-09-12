using System.Globalization;
using System.Text;
using Okojo.Text.Unicode;

namespace Okojo.Text.RegularExpressions.Internal;

internal sealed class ParseResult
{
    internal required RegexNode Root { get; init; }
    internal required int CaptureCount { get; init; }

    /// <summary>Capture indices (ascending) sharing each group name.</summary>
    internal required Dictionary<string, int[]> GroupNames { get; init; }
}

internal sealed class RegexParser
{
    private readonly string _pattern;
    private readonly bool _unicode;
    private readonly bool _unicodeSets;
    private readonly int _totalCaptures;
    private readonly Dictionary<string, int[]> _groupNames;
    private readonly int _maxParseDepth;
    private int _position;
    private int _captureCursor;
    private int _groupDepth;
    private NodeOptions _options;

    private RegexParser(string pattern, RegExpFlags flags, RegExpOptions options)
    {
        _pattern = pattern;
        _unicodeSets = (flags & RegExpFlags.UnicodeSets) != 0;
        _unicode = _unicodeSets || (flags & RegExpFlags.Unicode) != 0;
        _options = ToNodeOptions(flags);
        _maxParseDepth = options.MaxParseDepth;
        (_totalCaptures, _groupNames) = ScanCaptures(pattern, _unicodeSets);
        if (_totalCaptures > options.MaxCaptureCount)
        {
            throw new RegExpParseException(
                "Pattern exceeds MaxCaptureCount.",
                0,
                RegExpParseError.PatternTooLarge
            );
        }
    }

    internal static ParseResult Parse(string pattern, RegExpFlags flags, RegExpOptions options)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        RegexParser parser = new(pattern, flags, options);
        RegexNode root = parser.ParseDisjunction(stopAtCloseParenthesis: false, names: null);
        if (parser._position != pattern.Length)
        {
            parser.Throw("Unexpected token.", RegExpParseError.UnexpectedToken);
        }
        if (parser._captureCursor != parser._totalCaptures)
        {
            throw new InvalidOperationException("Capture pre-scan and parser disagreed.");
        }
        return new ParseResult
        {
            Root = root,
            CaptureCount = parser._totalCaptures,
            GroupNames = parser._groupNames,
        };
    }

    private RegexNode ParseDisjunction(bool stopAtCloseParenthesis, HashSet<string>? names)
    {
        List<RegexNode> alternatives = [];
        HashSet<string>? disjunctionNames = names;
        while (true)
        {
            HashSet<string> alternativeNames = [];
            alternatives.Add(ParseAlternative(stopAtCloseParenthesis, alternativeNames));
            if (disjunctionNames is not null)
                disjunctionNames.UnionWith(alternativeNames);
            if (_position < _pattern.Length && _pattern[_position] == '|')
            {
                _position++;
                continue;
            }
            break;
        }
        return RegexNode.Alternation(alternatives);
    }

    private RegexNode ParseAlternative(bool stopAtCloseParenthesis, HashSet<string> names)
    {
        List<RegexNode> terms = [];
        while (_position < _pattern.Length)
        {
            char ch = _pattern[_position];
            if (ch == '|' || (stopAtCloseParenthesis && ch == ')'))
                break;
            if (ch == ')' && !stopAtCloseParenthesis)
            {
                Throw("Unmatched closing parenthesis.", RegExpParseError.UnexpectedToken);
            }

            if (ch is '*' or '+' or '?')
            {
                Throw("Nothing to repeat.", RegExpParseError.NothingToRepeat);
            }
            if (ch == '{' && LooksLikeQuantifier(_position))
            {
                Throw("Nothing to repeat.", RegExpParseError.NothingToRepeat);
            }

            RegexNode atom = ParseAtom(out bool quantifiable, out HashSet<string> contributed);
            AddNames(names, contributed);
            if (_position < _pattern.Length && IsQuantifierStart(_pattern[_position]))
            {
                int quantifierPosition = _position;
                if (TryParseQuantifier(out int minimum, out int maximum))
                {
                    if (!quantifiable)
                    {
                        _position = quantifierPosition;
                        Throw(
                            "Assertions cannot be quantified in Unicode-aware mode.",
                            RegExpParseError.NothingToRepeat
                        );
                    }
                    bool greedy = true;
                    if (_position < _pattern.Length && _pattern[_position] == '?')
                    {
                        greedy = false;
                        _position++;
                    }
                    atom = RegexNode.Quantifier(atom, minimum, maximum, greedy);
                }
                else if (_pattern[quantifierPosition] == '{' && _unicode)
                {
                    _position = quantifierPosition;
                    Throw("Invalid Unicode-mode quantifier.", RegExpParseError.InvalidQuantifier);
                }
            }
            terms.Add(atom);
        }
        return RegexNode.Sequence(terms);
    }

    private void AddNames(HashSet<string> alternativeNames, HashSet<string> contributed)
    {
        foreach (string name in contributed)
        {
            if (!alternativeNames.Add(name))
            {
                Throw(
                    $"Duplicate capture group name '{name}' in the same alternative.",
                    RegExpParseError.DuplicateGroupName
                );
            }
        }
    }

    private RegexNode ParseAtom(out bool quantifiable, out HashSet<string> contributed)
    {
        if (_position >= _pattern.Length)
        {
            Throw("Unexpected end of pattern.", RegExpParseError.UnexpectedEnd);
        }

        int atomPosition = _position;
        char ch = _pattern[_position++];
        quantifiable = true;
        contributed = [];
        switch (ch)
        {
            case '^':
                quantifiable = false;
                return RegexNode.Anchor(start: true, _options);
            case '$':
                quantifiable = false;
                return RegexNode.Anchor(start: false, _options);
            case '.':
                return RegexNode.Dot(_options);
            case '[':
                if (_unicodeSets)
                {
                    return RegexNode.CharacterClass(ParseUnicodeSetClassAfterOpen(), _options);
                }
                ParsedCharacterClass parsedClass = ParseClassicClassAfterOpen();
                return RegexNode.CharacterClass(parsedClass.Set, _options, parsedClass.Invert);
            case '(':
                return ParseGroup(out quantifiable, contributed);
            case '\\':
                return ParseEscapeOutside(out quantifiable);
            case ']':
            case '}':
                if (_unicode)
                {
                    _position = atomPosition;
                    Throw($"Unescaped syntax character '{ch}'.", RegExpParseError.UnexpectedToken);
                }
                return RegexNode.Literal(ch, _options);
            case '{':
                if (_unicode)
                {
                    _position = atomPosition;
                    Throw(
                        "Invalid quantifier or unescaped '{'.",
                        RegExpParseError.InvalidQuantifier
                    );
                }
                return RegexNode.Literal(ch, _options);
            default:
                if (ch is '*' or '+' or '?' or '|' or ')')
                {
                    _position = atomPosition;
                    Throw("Unexpected regular-expression token.", RegExpParseError.UnexpectedToken);
                }
                return RegexNode.Literal(ReadPatternCodePoint(ch), _options);
        }
    }

    private RegexNode ParseGroup(out bool quantifiable, HashSet<string> contributed)
    {
        if (++_groupDepth > _maxParseDepth)
        {
            Throw(
                "Regular-expression group nesting exceeds MaxParseDepth.",
                RegExpParseError.PatternTooLarge
            );
        }

        quantifiable = true;
        bool capturing = true;
        bool lookaround = false;
        bool negative = false;
        bool behind = false;
        int capture = 0;
        string? name = null;
        NodeOptions savedOptions = _options;

        if (Consume('?'))
        {
            if (Consume(':'))
            {
                capturing = false;
            }
            else if (Consume('='))
            {
                capturing = false;
                lookaround = true;
                quantifiable = !_unicode; // Annex B permits quantified lookahead only without u/v.
            }
            else if (Consume('!'))
            {
                capturing = false;
                lookaround = true;
                negative = true;
                quantifiable = !_unicode;
            }
            else if (Consume('<'))
            {
                if (Consume('='))
                {
                    capturing = false;
                    lookaround = true;
                    behind = true;
                    quantifiable = false;
                }
                else if (Consume('!'))
                {
                    capturing = false;
                    lookaround = true;
                    negative = true;
                    behind = true;
                    quantifiable = false;
                }
                else
                {
                    name = ReadGroupNameAfterOpen();
                    capture = ++_captureCursor;
                    if (
                        !_groupNames.TryGetValue(name, out int[]? groups)
                        || Array.IndexOf(groups, capture) < 0
                    )
                    {
                        throw new InvalidOperationException(
                            "Named-capture pre-scan and parser disagreed."
                        );
                    }
                }
            }
            else if (TryParseModifierGroup(ref savedOptions))
            {
                capturing = false;
            }
            else
            {
                Throw("Invalid group prefix.", RegExpParseError.UnexpectedToken);
            }
        }
        else
        {
            capture = ++_captureCursor;
        }

        HashSet<string> bodyNames = [];
        RegexNode body = ParseDisjunction(stopAtCloseParenthesis: true, bodyNames);
        if (!Consume(')'))
        {
            Throw("Unterminated group.", RegExpParseError.UnterminatedGroup);
        }

        _options = savedOptions;
        _groupDepth--;
        if (name is not null && bodyNames.Contains(name))
        {
            Throw(
                $"Duplicate capture group name '{name}' in the same alternative.",
                RegExpParseError.DuplicateGroupName
            );
        }
        contributed.UnionWith(bodyNames);
        if (name is not null)
            contributed.Add(name);
        if (lookaround)
        {
            return RegexNode.Lookaround(body, negative, behind);
        }
        return capturing ? RegexNode.Capture(capture, body) : body;
    }

    private bool TryParseModifierGroup(ref NodeOptions savedOptions)
    {
        int start = _position;
        NodeOptions add = NodeOptions.None;
        NodeOptions remove = NodeOptions.None;
        bool any = false;
        while (
            _position < _pattern.Length && TryInlineFlag(_pattern[_position], out NodeOptions flag)
        )
        {
            if ((add & flag) != 0)
                Throw("Duplicate inline modifier flag.", RegExpParseError.InvalidFlag);
            add |= flag;
            any = true;
            _position++;
        }
        if (Consume('-'))
        {
            while (
                _position < _pattern.Length
                && TryInlineFlag(_pattern[_position], out NodeOptions flag)
            )
            {
                if ((remove & flag) != 0)
                    Throw("Duplicate inline modifier flag.", RegExpParseError.InvalidFlag);
                remove |= flag;
                any = true;
                _position++;
            }
            if (!any && !Consume(':'))
            {
                _position = start;
                return false;
            }
        }
        if (!any || !Consume(':'))
        {
            _position = start;
            return false;
        }
        if ((add & remove) != 0)
        {
            Throw(
                "An inline modifier cannot be both enabled and disabled.",
                RegExpParseError.InvalidFlag
            );
        }
        savedOptions = _options;
        _options = (_options | add) & ~remove;
        return true;
    }

    private RegexNode ParseEscapeOutside(out bool quantifiable)
    {
        quantifiable = true;
        if (_position >= _pattern.Length)
        {
            Throw("Trailing reverse solidus.", RegExpParseError.InvalidEscape);
        }
        int escapePosition = _position - 1;
        char ch = _pattern[_position++];
        switch (ch)
        {
            case 'b':
                quantifiable = false;
                return RegexNode.WordBoundary(negative: false, _options);
            case 'B':
                quantifiable = false;
                return RegexNode.WordBoundary(negative: true, _options);
            case 'd':
                return RegexNode.CharacterClass(UnicodePropertyDatabase.Digit, _options);
            case 'D':
                return RegexNode.CharacterClass(
                    ComplementBuiltin(UnicodePropertyDatabase.Digit),
                    _options
                );
            case 's':
                return RegexNode.CharacterClass(UnicodePropertyDatabase.WhiteSpace, _options);
            case 'S':
                return RegexNode.CharacterClass(
                    ComplementBuiltin(UnicodePropertyDatabase.WhiteSpace),
                    _options
                );
            case 'w':
                return RegexNode.CharacterClass(UnicodePropertyDatabase.Word, _options);
            case 'W':
                return RegexNode.CharacterClass(ComplementWordBuiltin(), _options);
            case 'p':
            case 'P':
                if (_unicode)
                {
                    bool negate = ch == 'P';
                    if (_unicodeSets && TryResolveStringProperty(negate, out UnicodeSet stringSet))
                    {
                        return RegexNode.CharacterClass(stringSet, _options);
                    }
                    return RegexNode.CharacterClass(ParsePropertyEscape(negate), _options);
                }
                return RegexNode.Literal(ch, _options);
            case 'k':
                if (
                    _position < _pattern.Length
                    && _pattern[_position] == '<'
                    && _groupNames.Count != 0
                )
                {
                    _position++;
                    string name = ReadGroupNameAfterOpen();
                    if (!_groupNames.TryGetValue(name, out int[]? groups))
                    {
                        _position = escapePosition;
                        Throw(
                            $"Unknown named capture '{name}'.",
                            RegExpParseError.UnknownGroupName
                        );
                    }
                    return groups!.Length == 1
                        ? RegexNode.Backreference(groups[0], _options)
                        : RegexNode.Backreference(groups, _options);
                }
                if (_unicode)
                {
                    _position = escapePosition;
                    Throw("Invalid named backreference.", RegExpParseError.InvalidBackreference);
                }
                return RegexNode.Literal('k', _options);
            default:
                if (ch is >= '1' and <= '9')
                {
                    return ParseDecimalEscapeOutside(ch, escapePosition);
                }
                int value = ParseCharacterEscapeTail(ch, inClass: false, escapePosition);
                return RegexNode.Literal(value, _options);
        }
    }

    private RegexNode ParseDecimalEscapeOutside(char first, int escapePosition)
    {
        int digitStart = _position - 1;
        long value = first - '0';
        while (_position < _pattern.Length && char.IsAsciiDigit(_pattern[_position]))
        {
            value = value * 10 + (_pattern[_position] - '0');
            if (value > int.MaxValue)
                break;
            _position++;
        }

        if (value <= _totalCaptures)
        {
            return RegexNode.Backreference((int)value, _options);
        }
        if (_unicode)
        {
            _position = escapePosition;
            Throw(
                "Backreference exceeds the number of capturing groups.",
                RegExpParseError.InvalidBackreference
            );
        }

        // Annex B legacy octal consumes at most three octal digits and never exceeds 0xFF.
        _position = digitStart;
        int octal = 0;
        int count = 0;
        while (_position < _pattern.Length && count < 3 && _pattern[_position] is >= '0' and <= '7')
        {
            int next = (octal << 3) + (_pattern[_position] - '0');
            if (next > 0xFF)
                break;
            octal = next;
            count++;
            _position++;
        }
        if (count == 0)
        {
            _position = digitStart + 1;
            return RegexNode.Literal(first, _options);
        }
        return RegexNode.Literal(octal, _options);
    }

    private int ParseCharacterEscapeTail(char ch, bool inClass, int escapePosition)
    {
        switch (ch)
        {
            case 'f':
                return 0x0C;
            case 'n':
                return 0x0A;
            case 'r':
                return 0x0D;
            case 't':
                return 0x09;
            case 'v':
                return 0x0B;
            case 'b' when inClass:
                return 0x08;
            case '0':
                if (_position < _pattern.Length && char.IsAsciiDigit(_pattern[_position]))
                {
                    if (_unicode)
                    {
                        _position = escapePosition;
                        Throw(
                            "Legacy octal escapes are invalid in Unicode-aware mode.",
                            RegExpParseError.InvalidEscape
                        );
                    }
                    return ParseLegacyOctal('0');
                }
                return 0;
            case 'c':
                if (_position < _pattern.Length)
                {
                    char control = _pattern[_position];
                    if (char.IsAsciiLetter(control))
                    {
                        _position++;
                        return char.ToUpperInvariant(control) % 32;
                    }
                    if (!_unicode && inClass && (char.IsAsciiDigit(control) || control == '_'))
                    {
                        _position++;
                        return control % 32;
                    }
                }
                if (_unicode)
                {
                    _position = escapePosition;
                    Throw("Invalid control escape.", RegExpParseError.InvalidEscape);
                }
                return 'c';
            case 'x':
                if (TryReadFixedHex(2, out int hexadecimal))
                    return hexadecimal;
                if (_unicode)
                {
                    _position = escapePosition;
                    Throw("Invalid hexadecimal escape.", RegExpParseError.InvalidEscape);
                }
                return 'x';
            case 'u':
                return ReadUnicodeEscape(escapePosition);
            default:
                if (ch is >= '0' and <= '7' && !_unicode)
                {
                    return ParseLegacyOctal(ch);
                }
                if (_unicode && !IsValidUnicodeIdentityEscape(ch, inClass))
                {
                    _position = escapePosition;
                    Throw($"Invalid identity escape '\\{ch}'.", RegExpParseError.InvalidEscape);
                }
                return ch;
        }
    }

    private int ParseLegacyOctal(char first)
    {
        int value = first - '0';
        int count = 1;
        while (_position < _pattern.Length && count < 3 && _pattern[_position] is >= '0' and <= '7')
        {
            int next = (value << 3) + (_pattern[_position] - '0');
            if (next > 0xFF)
                break;
            value = next;
            _position++;
            count++;
        }
        return value;
    }

    private int ReadUnicodeEscape(int escapePosition)
    {
        if (_unicode && Consume('{'))
        {
            return ReadBracedUnicodeEscape(escapePosition, allowSurrogate: true);
        }

        int saved = _position;
        if (!TryReadFixedHex(4, out int first))
        {
            if (!_unicode)
            {
                _position = saved;
                return 'u';
            }
            _position = escapePosition;
            Throw("Invalid Unicode escape.", RegExpParseError.InvalidEscape);
        }

        if (
            _unicode
            && Utf16.IsHighSurrogate(first)
            && _position + 5 < _pattern.Length
            && _pattern[_position] == '\\'
            && _pattern[_position + 1] == 'u'
        )
        {
            int secondEscape = _position;
            _position += 2;
            if (TryReadFixedHex(4, out int second) && Utf16.IsLowSurrogate(second))
            {
                return Utf16.CombineSurrogates(first, second);
            }
            _position = secondEscape;
        }
        return first;
    }

    private int ReadGroupNameUnicodeEscape(int escapePosition)
    {
        if (Consume('{'))
        {
            return ReadBracedUnicodeEscape(escapePosition, allowSurrogate: false);
        }

        if (!TryReadFixedHex(4, out int first))
        {
            _position = escapePosition;
            Throw(
                "Invalid Unicode escape in capture group name.",
                RegExpParseError.InvalidGroupName
            );
        }
        if (
            Utf16.IsHighSurrogate(first)
            && _position + 5 < _pattern.Length
            && _pattern[_position] == '\\'
            && _pattern[_position + 1] == 'u'
        )
        {
            int secondEscape = _position;
            _position += 2;
            if (TryReadFixedHex(4, out int second) && Utf16.IsLowSurrogate(second))
            {
                return Utf16.CombineSurrogates(first, second);
            }
            _position = secondEscape;
        }
        return first;
    }

    private int ReadBracedUnicodeEscape(int escapePosition, bool allowSurrogate)
    {
        int value = 0;
        int digits = 0;
        while (_position < _pattern.Length && _pattern[_position] != '}')
        {
            int hex = HexValue(_pattern[_position]);
            if (hex < 0 || value > 0x10FFFF >> 4)
            {
                _position = escapePosition;
                Throw("Invalid braced Unicode escape.", RegExpParseError.InvalidEscape);
            }
            value = (value << 4) | hex;
            digits++;
            _position++;
        }
        if (
            digits == 0
            || !Consume('}')
            || value > 0x10FFFF
            || (!allowSurrogate && value is >= 0xD800 and <= 0xDFFF)
        )
        {
            _position = escapePosition;
            Throw("Invalid braced Unicode escape.", RegExpParseError.InvalidEscape);
        }
        return value;
    }

    private bool TryReadFixedHex(int count, out int value)
    {
        value = 0;
        if (_position + count > _pattern.Length)
            return false;
        for (int i = 0; i < count; i++)
        {
            int hex = HexValue(_pattern[_position + i]);
            if (hex < 0)
                return false;
            value = (value << 4) | hex;
        }
        _position += count;
        return true;
    }

    private CharSet ParsePropertyEscape(bool negate)
    {
        int start = _position;
        if (!Consume('{'))
        {
            _position = start;
            Throw(
                "Unicode property escape requires braces.",
                RegExpParseError.InvalidUnicodeProperty
            );
        }
        int contentStart = _position;
        while (_position < _pattern.Length && _pattern[_position] != '}')
        {
            char ch = _pattern[_position];
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '='))
            {
                Throw(
                    "Invalid character in Unicode property escape.",
                    RegExpParseError.InvalidUnicodeProperty
                );
            }
            _position++;
        }
        if (_position == contentStart || !Consume('}'))
        {
            Throw("Unterminated Unicode property escape.", RegExpParseError.InvalidUnicodeProperty);
        }
        string expression = _pattern[contentStart..(_position - 1)];
        if (!UnicodePropertyDatabase.TryResolve(expression, out int propertyId))
        {
            Throw(
                $"Unknown or unsupported Unicode property '{expression}'.",
                RegExpParseError.InvalidUnicodeProperty
            );
        }
        int maximum = _unicode ? 0x10FFFF : 0xFFFF;
        CharSet set = UnicodePropertyDatabase.GetSet(propertyId);
        if (maximum != 0x10FFFF)
        {
            CharSetBuilder domain = new();
            domain.Add(0, maximum);
            set = CharSet.Intersect(set, domain.Build());
        }
        if (_unicodeSets && (_options & NodeOptions.IgnoreCase) != 0)
        {
            set = set.FoldForUnicodeSets();
        }
        if (!negate)
            return set;
        return _unicodeSets
            ? CharSet.ComplementUnicodeSets(set, (_options & NodeOptions.IgnoreCase) != 0)
            : CharSet.Complement(set, maximum);
    }

    private ParsedCharacterClass ParseClassicClassAfterOpen()
    {
        bool negated = Consume('^');
        CharSetBuilder builder = new();
        bool closed = false;
        while (_position < _pattern.Length)
        {
            if (Consume(']'))
            {
                closed = true;
                break;
            }

            ClassAtom left = ParseClassicClassAtom();
            if (
                _position < _pattern.Length
                && _pattern[_position] == '-'
                && _position + 1 < _pattern.Length
                && _pattern[_position + 1] != ']'
            )
            {
                _position++;
                ClassAtom right = ParseClassicClassAtom();
                if (!left.IsSingle || !right.IsSingle)
                {
                    if (_unicode)
                    {
                        Throw(
                            "A character-class range endpoint must be a character.",
                            RegExpParseError.InvalidCharacterRange
                        );
                    }
                    builder.AddSet(left.Set.CodePoints);
                    builder.Add('-');
                    builder.AddSet(right.Set.CodePoints);
                    continue;
                }
                if (left.Single > right.Single)
                {
                    Throw(
                        "Character-class range is out of order.",
                        RegExpParseError.InvalidCharacterRange
                    );
                }
                builder.Add(left.Single, right.Single);
            }
            else
            {
                builder.AddSet(left.Set.CodePoints);
            }
        }
        if (!closed)
        {
            Throw("Unterminated character class.", RegExpParseError.UnterminatedCharacterClass);
        }
        CharSet set = builder.Build();
        return new ParsedCharacterClass(set, negated);
    }

    private ClassAtom ParseClassicClassAtom()
    {
        if (_position >= _pattern.Length)
        {
            Throw("Unterminated character class.", RegExpParseError.UnterminatedCharacterClass);
        }
        int escapePosition = _position;
        char ch = _pattern[_position++];
        if (ch != '\\')
        {
            return ClassAtom.FromSingle(ReadPatternCodePoint(ch));
        }
        if (_position >= _pattern.Length)
        {
            Throw("Trailing reverse solidus in character class.", RegExpParseError.InvalidEscape);
        }
        char escaped = _pattern[_position++];
        if (_unicodeSets && IsClassSetReservedPunctuator(escaped))
        {
            // In /v classes these characters are valid only as escaped literals.
            return ClassAtom.FromSingle(escaped);
        }
        return escaped switch
        {
            'd' => ClassAtom.FromSet(UnicodePropertyDatabase.Digit),
            'D' => ClassAtom.FromSet(ComplementBuiltin(UnicodePropertyDatabase.Digit)),
            's' => ClassAtom.FromSet(UnicodePropertyDatabase.WhiteSpace),
            'S' => ClassAtom.FromSet(ComplementBuiltin(UnicodePropertyDatabase.WhiteSpace)),
            'w' => ClassAtom.FromSet(UnicodePropertyDatabase.Word),
            'W' => ClassAtom.FromSet(ComplementWordBuiltin()),
            'p' when _unicode => ClassAtom.FromSet(ParsePropertyEscape(negate: false)),
            'P' when _unicode => ClassAtom.FromSet(ParsePropertyEscape(negate: true)),
            _ => ClassAtom.FromSingle(
                ParseCharacterEscapeTail(escaped, inClass: true, escapePosition)
            ),
        };
    }

    private UnicodeSet ParseUnicodeSetClassAfterOpen()
    {
        bool negated = Consume('^');
        UnicodeSet value;

        if (_position < _pattern.Length && _pattern[_position] == ']')
        {
            value = UnicodeSet.Empty;
        }
        else
        {
            // ClassSetExpression is exactly one of ClassUnion, ClassIntersection, or
            // ClassSubtraction.  It is not a general precedence expression: mixing
            // an adjacent union/range with &&/-- at the same nesting level is a
            // syntax error in ECMAScript.
            int expressionStart = _position;
            ClassAtom first = ParseUnicodeSetOperand();
            if (AtUnicodeSetOperator('&'))
            {
                value = MaybeFoldUnicodeSet(first.Set);
                do
                {
                    _position += 2;
                    ClassAtom right = ParseUnicodeSetOperand();
                    value = UnicodeSet.Intersect(value, MaybeFoldUnicodeSet(right.Set));
                } while (AtUnicodeSetOperator('&'));

                if (
                    AtUnicodeSetOperator('-')
                    || (_position < _pattern.Length && _pattern[_position] != ']')
                )
                {
                    Throw(
                        "A /v character class cannot mix intersection with union or subtraction at one level.",
                        RegExpParseError.UnexpectedToken
                    );
                }
            }
            else if (AtUnicodeSetOperator('-'))
            {
                value = MaybeFoldUnicodeSet(first.Set);
                do
                {
                    _position += 2;
                    ClassAtom right = ParseUnicodeSetOperand();
                    value = UnicodeSet.Subtract(value, MaybeFoldUnicodeSet(right.Set));
                } while (AtUnicodeSetOperator('-'));

                if (
                    AtUnicodeSetOperator('&')
                    || (_position < _pattern.Length && _pattern[_position] != ']')
                )
                {
                    Throw(
                        "A /v character class cannot mix subtraction with union or intersection at one level.",
                        RegExpParseError.UnexpectedToken
                    );
                }
            }
            else
            {
                _position = expressionStart;
                value = ParseUnicodeSetUnion();
                if (AtUnicodeSetOperator('&') || AtUnicodeSetOperator('-'))
                {
                    Throw(
                        "A /v character class cannot mix union or ranges with &&/-- at one level.",
                        RegExpParseError.UnexpectedToken
                    );
                }
            }
        }

        if (!Consume(']'))
        {
            Throw("Unterminated Unicode set class.", RegExpParseError.UnterminatedCharacterClass);
        }
        if (!negated)
            return value;
        if (value.Strings.Length != 0)
        {
            Throw(
                "Negated /v character classes may not contain strings.",
                RegExpParseError.UnsupportedUnicodeSetString
            );
        }
        return UnicodeSet.FromCodePoints(
            CharSet.ComplementUnicodeSets(
                value.CodePoints,
                (_options & NodeOptions.IgnoreCase) != 0
            )
        );
    }

    private UnicodeSet ParseUnicodeSetUnion()
    {
        UnicodeSet value = UnicodeSet.Empty;
        bool any = false;
        while (_position < _pattern.Length && _pattern[_position] != ']')
        {
            if (AtUnicodeSetOperator('&') || AtUnicodeSetOperator('-'))
            {
                break;
            }

            ClassAtom left = ParseUnicodeSetOperand();
            UnicodeSet item;
            if (
                left.IsSingle
                && _position < _pattern.Length
                && _pattern[_position] == '-'
                && !AtUnicodeSetOperator('-')
                && _position + 1 < _pattern.Length
                && _pattern[_position + 1] != ']'
            )
            {
                _position++;
                ClassAtom right = ParseUnicodeSetOperand();
                if (!right.IsSingle || left.Single > right.Single)
                {
                    Throw("Invalid Unicode set range.", RegExpParseError.InvalidCharacterRange);
                }
                CharSetBuilder range = new();
                range.Add(left.Single, right.Single);
                item = MaybeFoldUnicodeSet(UnicodeSet.FromCodePoints(range.Build()));
            }
            else
            {
                item = MaybeFoldUnicodeSet(left.Set);
            }
            value = UnicodeSet.Union(value, item);
            any = true;
        }
        if (!any)
        {
            Throw("Unicode set expression requires an operand.", RegExpParseError.UnexpectedToken);
        }
        return value;
    }

    private ClassAtom ParseUnicodeSetOperand()
    {
        if (_position >= _pattern.Length)
        {
            Throw("Unterminated Unicode set class.", RegExpParseError.UnterminatedCharacterClass);
        }
        if (_pattern[_position] == ']')
        {
            Throw(
                "Unicode set operator requires a right operand.",
                RegExpParseError.UnexpectedToken
            );
        }
        if (Consume('['))
        {
            return ClassAtom.FromUnicodeSet(ParseUnicodeSetClassAfterOpen());
        }
        if (
            _pattern[_position] == '\\'
            && _position + 2 < _pattern.Length
            && _pattern[_position + 1] == 'q'
            && _pattern[_position + 2] == '{'
        )
        {
            return ClassAtom.FromUnicodeSet(ParseStringDisjunction());
        }
        if (
            _pattern[_position] == '\\'
            && _position + 1 < _pattern.Length
            && _pattern[_position + 1] is 'p' or 'P'
        )
        {
            return ClassAtom.FromUnicodeSet(
                ParseUnicodeSetPropertyEscape(_pattern[_position + 1] == 'P')
            );
        }

        char ch = _pattern[_position];
        if (
            _position + 1 < _pattern.Length
            && _pattern[_position + 1] == ch
            && IsClassSetReservedDoublePunctuator(ch)
        )
        {
            Throw(
                $"Reserved double punctuator '{ch}{ch}' cannot be a /v class operand.",
                RegExpParseError.UnexpectedToken
            );
        }
        if (ch != '\\' && (IsClassSetSyntaxCharacter(ch) || IsClassSetReservedPunctuator(ch)))
        {
            Throw(
                $"Reserved Unicode-set punctuator '{ch}' must be escaped.",
                RegExpParseError.UnexpectedToken
            );
        }
        return ParseClassicClassAtom();
    }

    private bool AtUnicodeSetOperator(char first) =>
        _position + 1 < _pattern.Length
        && _pattern[_position] == first
        && _pattern[_position + 1] == first;

    private UnicodeSet ParseStringDisjunction()
    {
        _position += 3;
        List<string> alternatives = [];
        while (true)
        {
            StringBuilder builder = new();
            while (_position < _pattern.Length && _pattern[_position] is not '|' and not '}')
            {
                Utf16.AppendCodePoint(builder, ReadStringItemCodePoint(_position - 1));
            }
            alternatives.Add(builder.ToString());
            if (Consume('}'))
                break;
            if (_position < _pattern.Length && _pattern[_position] == '|')
            {
                _position++;
                continue;
            }
            Throw(
                "Unterminated /v string disjunction.",
                RegExpParseError.UnterminatedCharacterClass
            );
        }
        return UnicodeSet.FromStrings(alternatives);
    }

    private int ReadStringItemCodePoint(int escapePosition)
    {
        if (_position >= _pattern.Length)
        {
            Throw("Unexpected end of /v string disjunction.", RegExpParseError.UnexpectedEnd);
        }
        char ch = _pattern[_position++];
        if (ch == '-')
        {
            Throw(
                "A '-' in a /v string disjunction must be escaped.",
                RegExpParseError.UnexpectedToken
            );
        }
        if (ch != '\\')
            return ReadPatternCodePoint(ch);
        if (_position >= _pattern.Length)
        {
            Throw(
                "Trailing reverse solidus in string disjunction.",
                RegExpParseError.InvalidEscape
            );
        }
        char escaped = _pattern[_position++];
        switch (escaped)
        {
            case 'f':
                return 0x0C;
            case 'n':
                return 0x0A;
            case 'r':
                return 0x0D;
            case 't':
                return 0x09;
            case 'v':
                return 0x0B;
            case 'b':
                return 0x08;
            case '0':
                if (_position < _pattern.Length && char.IsAsciiDigit(_pattern[_position]))
                {
                    Throw(
                        "Decimal escapes are invalid in /v string disjunctions.",
                        RegExpParseError.InvalidEscape
                    );
                }
                return 0;
            case 'c':
                if (_position < _pattern.Length && char.IsAsciiLetter(_pattern[_position]))
                {
                    char control = _pattern[_position++];
                    return char.ToUpperInvariant(control) % 32;
                }
                _position = escapePosition;
                Throw("Invalid control escape.", RegExpParseError.InvalidEscape);
                return 0;
            case 'x':
                if (TryReadFixedHex(2, out int hexadecimal))
                    return hexadecimal;
                _position = escapePosition;
                Throw("Invalid hexadecimal escape.", RegExpParseError.InvalidEscape);
                return 0;
            case 'u':
                return ReadUnicodeEscape(escapePosition);
            default:
                if (IsClassSetSyntaxCharacter(escaped))
                    return escaped;
                _position = escapePosition;
                Throw(
                    $"Invalid identity escape '\\{escaped}' in string disjunction.",
                    RegExpParseError.InvalidEscape
                );
                return 0;
        }
    }

    private UnicodeSet ParseUnicodeSetPropertyEscape(bool negate)
    {
        _position += 2;
        if (TryResolveStringProperty(negate, out UnicodeSet stringSet))
            return stringSet;
        return UnicodeSet.FromCodePoints(ParsePropertyEscape(negate));
    }

    /// <summary>
    /// Parses <c>{name}</c> from <see cref="_position"/> (which must point at the
    /// opening brace after <c>\p</c>/<c>\P</c>) and, if the name resolves to a
    /// property of strings, returns it. On failure the position is restored to
    /// the opening brace so callers can fall back to code-point resolution.
    /// </summary>
    private bool TryResolveStringProperty(bool negate, out UnicodeSet stringSet)
    {
        int bracePosition = _position;
        if (!Consume('{'))
        {
            stringSet = UnicodeSet.Empty;
            return false;
        }
        int contentStart = _position;
        while (_position < _pattern.Length && _pattern[_position] != '}')
        {
            char ch = _pattern[_position];
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '='))
            {
                _position = bracePosition;
                stringSet = UnicodeSet.Empty;
                return false;
            }
            _position++;
        }
        if (_position == contentStart || !Consume('}'))
        {
            _position = bracePosition;
            stringSet = UnicodeSet.Empty;
            return false;
        }
        string expression = _pattern[contentStart..(_position - 1)];
        if (!UnicodePropertyDatabase.TryResolveStrings(expression, out string[] strings))
        {
            _position = bracePosition;
            stringSet = UnicodeSet.Empty;
            return false;
        }
        if (negate)
        {
            Throw(
                $"Property of strings '{expression}' cannot be negated.",
                RegExpParseError.InvalidUnicodeProperty
            );
        }
        stringSet = UnicodeSet.FromStrings(strings);
        return true;
    }

    private bool TryParseQuantifier(out int minimum, out int maximum)
    {
        minimum = 0;
        maximum = 0;
        if (_position >= _pattern.Length)
            return false;
        switch (_pattern[_position])
        {
            case '*':
                _position++;
                maximum = -1;
                return true;
            case '+':
                _position++;
                minimum = 1;
                maximum = -1;
                return true;
            case '?':
                _position++;
                maximum = 1;
                return true;
            case '{':
                int saved = _position;
                _position++;
                if (!ReadDecimalInteger(out minimum))
                {
                    _position = saved;
                    return false;
                }
                if (Consume('}'))
                {
                    maximum = minimum;
                    return true;
                }
                if (!Consume(','))
                {
                    _position = saved;
                    return false;
                }
                if (Consume('}'))
                {
                    maximum = -1;
                    return true;
                }
                if (!ReadDecimalInteger(out maximum) || !Consume('}'))
                {
                    _position = saved;
                    return false;
                }
                if (maximum < minimum)
                {
                    _position = saved;
                    Throw(
                        "Quantifier maximum is smaller than its minimum.",
                        RegExpParseError.QuantifierRangeOutOfOrder
                    );
                }
                return true;
            default:
                return false;
        }
    }

    private bool ReadDecimalInteger(out int value)
    {
        value = 0;
        int start = _position;
        long accumulator = 0;
        while (_position < _pattern.Length && char.IsAsciiDigit(_pattern[_position]))
        {
            int digit = _pattern[_position] - '0';
            if (accumulator > (int.MaxValue - digit) / 10)
            {
                accumulator = int.MaxValue;
                _position++;
                while (_position < _pattern.Length && char.IsAsciiDigit(_pattern[_position]))
                    _position++;
                break;
            }
            accumulator = accumulator * 10 + digit;
            _position++;
        }
        if (_position == start)
            return false;
        value = (int)accumulator;
        return true;
    }

    private bool LooksLikeQuantifier(int position)
    {
        if (position >= _pattern.Length || _pattern[position] != '{')
            return false;
        int i = position + 1;
        if (i >= _pattern.Length || !char.IsAsciiDigit(_pattern[i]))
            return false;
        while (i < _pattern.Length && char.IsAsciiDigit(_pattern[i]))
            i++;
        if (i < _pattern.Length && _pattern[i] == '}')
            return true;
        if (i >= _pattern.Length || _pattern[i] != ',')
            return false;
        i++;
        while (i < _pattern.Length && char.IsAsciiDigit(_pattern[i]))
            i++;
        return i < _pattern.Length && _pattern[i] == '}';
    }

    private int ReadPatternCodePoint(char first)
    {
        if (
            _unicode
            && Utf16.IsHighSurrogate(first)
            && _position < _pattern.Length
            && Utf16.IsLowSurrogate(_pattern[_position])
        )
        {
            return Utf16.CombineSurrogates(first, _pattern[_position++]);
        }
        return first;
    }

    private int ReadGroupNameCodePoint(char first)
    {
        if (
            Utf16.IsHighSurrogate(first)
            && _position < _pattern.Length
            && Utf16.IsLowSurrogate(_pattern[_position])
        )
        {
            return Utf16.CombineSurrogates(first, _pattern[_position++]);
        }
        return first;
    }

    private string ReadGroupNameAfterOpen()
    {
        int startPosition = _position;
        StringBuilder? builder = null;
        bool first = true;
        while (_position < _pattern.Length && _pattern[_position] != '>')
        {
            int cp;
            int segmentStart = _position;
            if (_pattern[_position] == '\\')
            {
                _position++;
                if (!Consume('u'))
                {
                    Throw(
                        "Only Unicode escapes are allowed in a capture name.",
                        RegExpParseError.InvalidGroupName
                    );
                }
                cp = ReadGroupNameUnicodeEscape(segmentStart);
                builder ??= new StringBuilder(
                    _pattern.AsSpan(startPosition, segmentStart - startPosition).ToString()
                );
                Utf16.AppendCodePoint(builder, cp);
            }
            else
            {
                char ch = _pattern[_position++];
                cp = ReadGroupNameCodePoint(ch);
                if (builder is not null)
                    Utf16.AppendCodePoint(builder, cp);
            }

            if (first ? !IsGroupNameStart(cp) : !IsGroupNameContinue(cp))
            {
                Throw("Invalid capture group name.", RegExpParseError.InvalidGroupName);
            }
            first = false;
        }
        if (first || !Consume('>'))
        {
            Throw("Unterminated or empty capture group name.", RegExpParseError.InvalidGroupName);
        }
        return builder?.ToString() ?? _pattern[startPosition..(_position - 1)];
    }

    private CharSet ComplementBuiltin(CharSet set)
    {
        if (_unicodeSets)
        {
            if ((_options & NodeOptions.IgnoreCase) != 0)
                set = set.FoldForUnicodeSets();
            return CharSet.ComplementUnicodeSets(set, (_options & NodeOptions.IgnoreCase) != 0);
        }
        return CharSet.Complement(set, _unicode ? 0x10FFFF : 0xFFFF);
    }

    private CharSet ComplementWordBuiltin()
    {
        CharSet word = UnicodePropertyDatabase.Word;
        if (_unicode && !_unicodeSets && (_options & NodeOptions.IgnoreCase) != 0)
        {
            word = word.UnicodeCaseClosure();
        }
        return ComplementBuiltin(word);
    }

    private UnicodeSet MaybeFoldUnicodeSet(UnicodeSet set) =>
        _unicodeSets && (_options & NodeOptions.IgnoreCase) != 0
            ? UnicodeSet.FromCodePoints(set.CodePoints.FoldForUnicodeSets())
            : set;

    private bool Consume(char expected)
    {
        if (_position < _pattern.Length && _pattern[_position] == expected)
        {
            _position++;
            return true;
        }
        return false;
    }

    private void Throw(string message, RegExpParseError error) =>
        throw new RegExpParseException(message, _position, error);

    private static bool IsQuantifierStart(char ch) => ch is '*' or '+' or '?' or '{';

    private static bool TryInlineFlag(char ch, out NodeOptions flag)
    {
        flag = ch switch
        {
            'i' => NodeOptions.IgnoreCase,
            'm' => NodeOptions.Multiline,
            's' => NodeOptions.DotAll,
            _ => NodeOptions.None,
        };
        return flag != NodeOptions.None;
    }

    private static bool IsValidUnicodeIdentityEscape(char ch, bool inClass) =>
        ch
            is '^'
                or '$'
                or '\\'
                or '.'
                or '*'
                or '+'
                or '?'
                or '('
                or ')'
                or '['
                or ']'
                or '{'
                or '}'
                or '|'
                or '/'
        || (inClass && ch == '-');

    private static bool IsClassSetSyntaxCharacter(char ch) =>
        ch is '(' or ')' or '[' or ']' or '{' or '}' or '/' or '-' or '\\' or '|';

    private static bool IsClassSetReservedPunctuator(char ch) =>
        ch
            is '&'
                or '-'
                or '!'
                or '#'
                or '%'
                or ','
                or ':'
                or ';'
                or '<'
                or '='
                or '>'
                or '@'
                or '`'
                or '~';

    private static bool IsClassSetReservedDoublePunctuator(char ch) =>
        ch
            is '&'
                or '!'
                or '#'
                or '$'
                or '%'
                or '*'
                or '+'
                or ','
                or '.'
                or ':'
                or ';'
                or '<'
                or '='
                or '>'
                or '?'
                or '@'
                or '^'
                or '`'
                or '~';

    private static int HexValue(char ch) =>
        ch switch
        {
            >= '0' and <= '9' => ch - '0',
            >= 'a' and <= 'f' => ch - 'a' + 10,
            >= 'A' and <= 'F' => ch - 'A' + 10,
            _ => -1,
        };

    private static bool IsGroupNameStart(int cp) => UnicodePropertyDatabase.IsIdentifierStart(cp);

    private static bool IsGroupNameContinue(int cp) =>
        UnicodePropertyDatabase.IsIdentifierContinue(cp);

    private static NodeOptions ToNodeOptions(RegExpFlags flags)
    {
        NodeOptions result = NodeOptions.None;
        if ((flags & RegExpFlags.IgnoreCase) != 0)
            result |= NodeOptions.IgnoreCase;
        if ((flags & RegExpFlags.Multiline) != 0)
            result |= NodeOptions.Multiline;
        if ((flags & RegExpFlags.DotAll) != 0)
            result |= NodeOptions.DotAll;
        if ((flags & (RegExpFlags.Unicode | RegExpFlags.UnicodeSets)) != 0)
            result |= NodeOptions.Unicode;
        if ((flags & RegExpFlags.UnicodeSets) != 0)
            result |= NodeOptions.UnicodeSets;
        return result;
    }

    private static (int Count, Dictionary<string, int[]> Names) ScanCaptures(
        string pattern,
        bool unicodeSets
    )
    {
        int count = 0;
        Dictionary<string, List<int>> nameList = new(StringComparer.Ordinal);
        int i = 0;
        int classDepth = 0;
        while (i < pattern.Length)
        {
            char ch = pattern[i++];
            if (ch == '\\')
            {
                if (i < pattern.Length)
                    i++;
                continue;
            }
            if (ch == '[')
            {
                classDepth = 1;
                while (i < pattern.Length && classDepth > 0)
                {
                    ch = pattern[i++];
                    if (ch == '\\')
                    {
                        if (i < pattern.Length)
                            i++;
                    }
                    else if (unicodeSets && ch == '[')
                    {
                        classDepth++;
                    }
                    else if (ch == ']')
                    {
                        classDepth--;
                    }
                }
                continue;
            }
            if (ch != '(')
                continue;

            if (i < pattern.Length && pattern[i] == '?')
            {
                i++;
                if (i >= pattern.Length)
                    continue;
                char kind = pattern[i];
                if (kind is ':' or '=' or '!')
                {
                    i++;
                    continue;
                }
                if (kind == '<')
                {
                    i++;
                    if (i < pattern.Length && pattern[i] is '=' or '!')
                    {
                        i++;
                        continue;
                    }
                    int nameStart = i;
                    while (i < pattern.Length && pattern[i] != '>')
                    {
                        if (pattern[i] == '\\' && i + 1 < pattern.Length)
                            i += 2;
                        else
                            i++;
                    }
                    if (i >= pattern.Length)
                    {
                        throw new RegExpParseException(
                            "Unterminated named capture.",
                            nameStart,
                            RegExpParseError.InvalidGroupName
                        );
                    }
                    string raw = pattern[nameStart..i];
                    i++;
                    string name = DecodeScannedGroupName(raw, nameStart);
                    count++;
                    if (!nameList.TryGetValue(name, out List<int>? groupIndices))
                    {
                        groupIndices = [];
                        nameList.Add(name, groupIndices);
                    }
                    groupIndices.Add(count);
                    continue;
                }
                // Inline modifier group: not capturing.
                continue;
            }
            count++;
        }
        Dictionary<string, int[]> names = new(StringComparer.Ordinal);
        foreach ((string name, List<int> indices) in nameList)
            names.Add(name, indices.ToArray());
        return (count, names);
    }

    private static string DecodeScannedGroupName(string raw, int sourcePosition)
    {
        if (!raw.Contains('\\'))
            return raw;
        StringBuilder builder = new(raw.Length);
        for (int i = 0; i < raw.Length; )
        {
            if (raw[i] != '\\')
            {
                builder.Append(raw[i++]);
                continue;
            }

            int escapeStart = i++;
            if (i >= raw.Length || raw[i++] != 'u')
            {
                throw new RegExpParseException(
                    "Invalid escape in capture group name.",
                    sourcePosition + escapeStart,
                    RegExpParseError.InvalidGroupName
                );
            }

            int codePoint;
            if (i < raw.Length && raw[i] == '{')
            {
                int digitsStart = ++i;
                int close = raw.IndexOf('}', digitsStart);
                if (
                    close < 0
                    || close == digitsStart
                    || close - digitsStart > 6
                    || !int.TryParse(
                        raw.AsSpan(digitsStart, close - digitsStart),
                        NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,
                        out codePoint
                    )
                    || codePoint > 0x10FFFF
                    || codePoint is >= 0xD800 and <= 0xDFFF
                )
                {
                    throw new RegExpParseException(
                        "Invalid Unicode escape in capture group name.",
                        sourcePosition + escapeStart,
                        RegExpParseError.InvalidGroupName
                    );
                }
                i = close + 1;
            }
            else
            {
                if (
                    i + 4 > raw.Length
                    || !int.TryParse(
                        raw.AsSpan(i, 4),
                        NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,
                        out int first
                    )
                )
                {
                    throw new RegExpParseException(
                        "Invalid Unicode escape in capture group name.",
                        sourcePosition + escapeStart,
                        RegExpParseError.InvalidGroupName
                    );
                }
                i += 4;
                codePoint = first;
                if (
                    Utf16.IsHighSurrogate(first)
                    && i + 6 <= raw.Length
                    && raw[i] == '\\'
                    && raw[i + 1] == 'u'
                    && int.TryParse(
                        raw.AsSpan(i + 2, 4),
                        NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,
                        out int second
                    )
                    && Utf16.IsLowSurrogate(second)
                )
                {
                    codePoint = Utf16.CombineSurrogates(first, second);
                    i += 6;
                }
            }
            Utf16.AppendCodePoint(builder, codePoint);
        }
        return builder.ToString();
    }

    private readonly record struct ParsedCharacterClass(CharSet Set, bool Invert);

    private readonly struct ClassAtom
    {
        private ClassAtom(UnicodeSet set, bool isSingle, int single)
        {
            Set = set;
            IsSingle = isSingle;
            Single = single;
        }

        internal UnicodeSet Set { get; }
        internal bool IsSingle { get; }
        internal int Single { get; }

        internal static ClassAtom FromSingle(int value) =>
            new(UnicodeSet.FromSingle(value), true, value);

        internal static ClassAtom FromSet(CharSet set) =>
            new(UnicodeSet.FromCodePoints(set), set.IsSingle, set.IsSingle ? set.Single : -1);

        internal static ClassAtom FromUnicodeSet(UnicodeSet set) =>
            new(
                set,
                set.Strings.Length == 0 && set.CodePoints.IsSingle,
                set.Strings.Length == 0 && set.CodePoints.IsSingle ? set.CodePoints.Single : -1
            );
    }
}
