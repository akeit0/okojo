using System.Globalization;
using System.Text;

namespace Okojo.RegExp.Experimental;

internal sealed class ScratchRegExpProgram
{
    private const int MaxRequiredLiteralPrefixLength = 8;
    private const int MaxExactLiteralPatternLength = 64;

    public required Node Root { get; init; }
    public required RegExpRuntimeFlags Flags { get; init; }
    public required string[] NamedGroupNames { get; init; }
    public required Dictionary<string, List<int>> NamedCaptureIndexes { get; init; }
    public required int CaptureCount { get; init; }
    public required int MinMatchLength { get; init; }
    public required int LeadingLiteralCodePoint { get; init; }
    public required bool HasLeadingLiteral { get; init; }
    public required int[] RequiredLiteralPrefixCodePoints { get; init; }
    public required string? RequiredLiteralPrefixText { get; init; }
    public required int[] ExactLiteralCodePoints { get; init; }
    public required bool HasExactLiteralPattern { get; init; }
    public required string? ExactLiteralText { get; init; }
    public required bool HasScopedModifiers { get; init; }
    public required Dictionary<Node, int[]> NodeCaptureIndices { get; init; }

    public static ScratchRegExpProgram Parse(string pattern, RegExpRuntimeFlags flags)
    {
        var parser = new Parser(pattern, flags);
        return parser.Parse();
    }

    public static RegExpRuntimeFlags ParseFlags(string flags)
    {
        var global = false;
        var ignoreCase = false;
        var multiline = false;
        var hasIndices = false;
        var sticky = false;
        var unicode = false;
        var unicodeSets = false;
        var dotAll = false;

        for (var i = 0; i < flags.Length; i++)
            switch (flags[i])
            {
                case 'g':
                    if (global)
                        ThrowInvalidFlags();
                    global = true;
                    break;
                case 'i':
                    if (ignoreCase)
                        ThrowInvalidFlags();
                    ignoreCase = true;
                    break;
                case 'm':
                    if (multiline)
                        ThrowInvalidFlags();
                    multiline = true;
                    break;
                case 'd':
                    if (hasIndices)
                        ThrowInvalidFlags();
                    hasIndices = true;
                    break;
                case 'y':
                    if (sticky)
                        ThrowInvalidFlags();
                    sticky = true;
                    break;
                case 'u':
                    if (unicode)
                        ThrowInvalidFlags();
                    unicode = true;
                    break;
                case 'v':
                    if (unicode || unicodeSets)
                        ThrowInvalidFlags();
                    unicode = true;
                    unicodeSets = true;
                    break;
                case 's':
                    if (dotAll)
                        ThrowInvalidFlags();
                    dotAll = true;
                    break;
                default:
                    ThrowInvalidFlags();
                    break;
            }

        return new(global, ignoreCase, multiline, hasIndices, sticky, unicode, unicodeSets, dotAll);
    }

    public static string CanonicalizeFlags(in RegExpRuntimeFlags flags)
    {
        var sb = new StringBuilder(8);
        if (flags.HasIndices) sb.Append('d');
        if (flags.Global) sb.Append('g');
        if (flags.IgnoreCase) sb.Append('i');
        if (flags.Multiline) sb.Append('m');
        if (flags.DotAll) sb.Append('s');
        if (flags.UnicodeSets) sb.Append('v');
        else if (flags.Unicode) sb.Append('u');
        if (flags.Sticky) sb.Append('y');
        return sb.ToString();
    }

    private static void ThrowInvalidFlags()
    {
        throw new ArgumentException("Invalid regular expression flags");
    }

    internal abstract record Node;

    internal sealed record EmptyNode : Node;

    internal sealed record LiteralNode(int CodePoint) : Node;

    internal sealed record DotNode : Node;

    internal sealed record AnchorNode(bool Start) : Node;

    internal sealed record BoundaryNode(bool Positive) : Node;

    internal sealed record SequenceNode(Node[] Terms) : Node;

    internal sealed record AlternationNode(Node[] Alternatives) : Node;

    internal sealed record CaptureNode(int Index, Node Child) : Node;

    internal sealed record LookaheadNode(bool Positive, Node Child) : Node;

    internal sealed record LookbehindNode(bool Positive, Node Child) : Node;

    internal sealed record BackReferenceNode(int Index) : Node;

    internal sealed record NamedBackReferenceNode(string Name) : Node;

    internal sealed record ScopedModifiersNode(bool? IgnoreCase, bool? Multiline, bool? DotAll, Node Child) : Node;

    internal sealed record PropertyEscapeNode(
        PropertyEscapeKind Kind,
        bool Negated,
        GeneralCategoryMask Categories = GeneralCategoryMask.None,
        string? PropertyValue = null) : Node;

    internal sealed record QuantifierNode(Node Child, int Min, int Max, bool Greedy) : Node;

    internal sealed record ClassNode(ClassItem[] Items, bool Negated, ClassSetExpression? Expression = null) : Node;

    internal abstract record ClassSetExpression;

    internal sealed record ClassSetItemExpression(ClassItem Item) : ClassSetExpression;

    internal sealed record ClassSetNestedExpression(ClassNode Class) : ClassSetExpression;

    internal sealed record ClassSetUnionExpression(ClassSetExpression[] Terms) : ClassSetExpression;

    internal sealed record ClassSetIntersectionExpression(ClassSetExpression Left, ClassSetExpression Right)
        : ClassSetExpression;

    internal sealed record ClassSetDifferenceExpression(ClassSetExpression Left, ClassSetExpression Right)
        : ClassSetExpression;

    internal enum PropertyEscapeKind
    {
        UppercaseLetter,
        AsciiHexDigit,
        HexDigit,
        Ascii,
        Assigned,
        Any,
        Alphabetic,
        GraphemeBase,
        GraphemeExtend,
        Ideographic,
        JoinControl,
        Cased,
        CaseIgnorable,
        BidiControl,
        BidiMirrored,
        Dash,
        Deprecated,
        IdsBinaryOperator,
        IdsTrinaryOperator,
        LogicalOrderException,
        Lowercase,
        ChangesWhenCaseMapped,
        ChangesWhenCasefolded,
        ChangesWhenLowercased,
        ChangesWhenTitlecased,
        ChangesWhenUppercased,
        ChangesWhenNfkcCasefolded,
        Diacritic,
        EmojiComponent,
        EmojiModifier,
        EmojiModifierBase,
        Emoji,
        EmojiPresentation,
        DefaultIgnorableCodePoint,
        Extender,
        ExtendedPictographic,
        IdStart,
        IdContinue,
        XidStart,
        XidContinue,
        Math,
        NoncharacterCodePoint,
        PatternSyntax,
        PatternWhiteSpace,
        WhiteSpace,
        VariationSelector,
        Uppercase,
        UnifiedIdeograph,
        TerminalPunctuation,
        SoftDotted,
        SentenceTerminal,
        StringProperty,
        QuotationMark,
        Radical,
        RegionalIndicator,
        GeneralCategory,
        Script,
        ScriptExtensions,
        ScriptExtensionsUnknown
    }

    [Flags]
    internal enum GeneralCategoryMask : ulong
    {
        None = 0,
        UppercaseLetter = 1UL << 0,
        LowercaseLetter = 1UL << 1,
        TitlecaseLetter = 1UL << 2,
        ModifierLetter = 1UL << 3,
        OtherLetter = 1UL << 4,
        NonSpacingMark = 1UL << 5,
        SpacingCombiningMark = 1UL << 6,
        EnclosingMark = 1UL << 7,
        DecimalDigitNumber = 1UL << 8,
        LetterNumber = 1UL << 9,
        OtherNumber = 1UL << 10,
        ConnectorPunctuation = 1UL << 11,
        DashPunctuation = 1UL << 12,
        OpenPunctuation = 1UL << 13,
        ClosePunctuation = 1UL << 14,
        InitialQuotePunctuation = 1UL << 15,
        FinalQuotePunctuation = 1UL << 16,
        OtherPunctuation = 1UL << 17,
        MathSymbol = 1UL << 18,
        CurrencySymbol = 1UL << 19,
        ModifierSymbol = 1UL << 20,
        OtherSymbol = 1UL << 21,
        SpaceSeparator = 1UL << 22,
        LineSeparator = 1UL << 23,
        ParagraphSeparator = 1UL << 24,
        Control = 1UL << 25,
        Format = 1UL << 26,
        Surrogate = 1UL << 27,
        PrivateUse = 1UL << 28,
        Unassigned = 1UL << 29,

        Letter = UppercaseLetter | LowercaseLetter | TitlecaseLetter | ModifierLetter | OtherLetter,
        CasedLetter = UppercaseLetter | LowercaseLetter | TitlecaseLetter,
        Mark = NonSpacingMark | SpacingCombiningMark | EnclosingMark,
        Number = DecimalDigitNumber | LetterNumber | OtherNumber,

        Punctuation = ConnectorPunctuation | DashPunctuation | OpenPunctuation | ClosePunctuation |
                      InitialQuotePunctuation | FinalQuotePunctuation | OtherPunctuation,
        Symbol = MathSymbol | CurrencySymbol | ModifierSymbol | OtherSymbol,
        Separator = SpaceSeparator | LineSeparator | ParagraphSeparator,
        Other = Control | Format | Surrogate | PrivateUse | Unassigned
    }

    internal enum ClassItemKind
    {
        Literal,
        StringLiteral,
        Range,
        Digit,
        NotDigit,
        Space,
        NotSpace,
        Word,
        NotWord,
        PropertyEscape
    }

    internal readonly record struct ClassItem(
        ClassItemKind Kind,
        int CodePoint = 0,
        int RangeStart = 0,
        int RangeEnd = 0,
        PropertyEscapeKind PropertyKind = default,
        bool PropertyNegated = false,
        GeneralCategoryMask PropertyCategories = GeneralCategoryMask.None,
        string? PropertyValue = null,
        string[]? Strings = null);

    private sealed class Parser(string pattern, RegExpRuntimeFlags flags)
    {
        private readonly Dictionary<string, List<int>> namedCaptureIndexes = new(StringComparer.Ordinal);
        private readonly List<string> namedGroupNames = new();
        private readonly List<string> unresolvedNamedBackreferences = new();
        private readonly List<int> unresolvedNumericBackreferences = new();
        private readonly bool hasNamedCapturingGroups = ContainsNamedCapturingGroups(pattern);
        private HashSet<string> activeNamedCaptureNames = new(StringComparer.Ordinal);
        private int captureCount;
        private int pos;

        public ScratchRegExpProgram Parse()
        {
            var root = ParseAlternation();
            if (pos != pattern.Length)
                throw new ArgumentException("Invalid regular expression pattern");

            foreach (var name in unresolvedNamedBackreferences)
                if (!namedCaptureIndexes.ContainsKey(name))
                    throw new ArgumentException("Invalid named capture referenced");

            foreach (var number in unresolvedNumericBackreferences)
                if (captureCount < number)
                    throw new ArgumentException("Invalid regular expression pattern");

            var hasScopedModifiers = ContainsScopedModifiers(root);
            var leadingLiteral = 0;
            var hasLeadingLiteral = !hasScopedModifiers && TryGetLeadingLiteral(root, out leadingLiteral);
            var requiredLiteralPrefix = hasScopedModifiers ? [] : GetRequiredLiteralPrefix(root);
            var exactLiteralCodePoints = hasScopedModifiers ? [] : GetExactLiteralPattern(root);
            int[] exactLiteral = [];
            var hasExactLiteralPattern = !hasScopedModifiers &&
                                         captureCount == 0 &&
                                         TryGetExactLiteralPattern(root, out exactLiteral) &&
                                         exactLiteral.Length != 0;
            return new()
            {
                Root = root,
                Flags = flags,
                NamedGroupNames = namedGroupNames.ToArray(),
                NamedCaptureIndexes = namedCaptureIndexes,
                CaptureCount = captureCount,
                MinMatchLength = TryComputeMinMatchLength(root, out var minMatchLength) ? minMatchLength : -1,
                HasLeadingLiteral = hasLeadingLiteral,
                LeadingLiteralCodePoint = leadingLiteral,
                RequiredLiteralPrefixCodePoints = requiredLiteralPrefix,
                RequiredLiteralPrefixText = !hasScopedModifiers &&
                                            TryBuildLiteralText(requiredLiteralPrefix, out var prefixText)
                    ? prefixText
                    : null,
                ExactLiteralCodePoints = exactLiteralCodePoints,
                HasExactLiteralPattern = hasExactLiteralPattern,
                ExactLiteralText = !hasScopedModifiers &&
                                    TryGetExactLiteralPattern(root, out exactLiteral) &&
                                    TryBuildLiteralText(exactLiteral, out var exactLiteralText)
                    ? exactLiteralText
                    : null,
                HasScopedModifiers = hasScopedModifiers,
                NodeCaptureIndices = BuildCaptureIndexMap(root)
            };
        }

        private static bool ContainsScopedModifiers(Node node)
        {
            return node switch
            {
                ScopedModifiersNode => true,
                CaptureNode capture => ContainsScopedModifiers(capture.Child),
                LookaheadNode lookahead => ContainsScopedModifiers(lookahead.Child),
                LookbehindNode lookbehind => ContainsScopedModifiers(lookbehind.Child),
                SequenceNode sequence => Array.Exists(sequence.Terms, ContainsScopedModifiers),
                AlternationNode alternation => Array.Exists(alternation.Alternatives, ContainsScopedModifiers),
                QuantifierNode quantifier => ContainsScopedModifiers(quantifier.Child),
                _ => false
            };
        }

        private static Dictionary<Node, int[]> BuildCaptureIndexMap(Node root)
        {
            var map = new Dictionary<Node, int[]>(ReferenceEqualityComparer.Instance);
            CollectCaptureIndices(root, map);
            return map;
        }

        private static int[] CollectCaptureIndices(Node node, Dictionary<Node, int[]> map)
        {
            if (map.TryGetValue(node, out var existing))
                return existing;

            HashSet<int> indices = new();
            switch (node)
            {
                case CaptureNode capture:
                    indices.Add(capture.Index);
                    indices.UnionWith(CollectCaptureIndices(capture.Child, map));
                    break;
                case LookaheadNode lookahead:
                    indices.UnionWith(CollectCaptureIndices(lookahead.Child, map));
                    break;
                case LookbehindNode lookbehind:
                    indices.UnionWith(CollectCaptureIndices(lookbehind.Child, map));
                    break;
                case ScopedModifiersNode scoped:
                    indices.UnionWith(CollectCaptureIndices(scoped.Child, map));
                    break;
                case SequenceNode sequence:
                    foreach (var term in sequence.Terms)
                        indices.UnionWith(CollectCaptureIndices(term, map));
                    break;
                case AlternationNode alternation:
                    foreach (var alternative in alternation.Alternatives)
                        indices.UnionWith(CollectCaptureIndices(alternative, map));
                    break;
                case QuantifierNode quantifier:
                    indices.UnionWith(CollectCaptureIndices(quantifier.Child, map));
                    break;
            }

            var array = indices.Count == 0 ? Array.Empty<int>() : new int[indices.Count];
            if (array.Length != 0)
            {
                indices.CopyTo(array);
                if (array.Length > 1)
                    Array.Sort(array);
            }

            map[node] = array;
            return array;
        }

        private Node ParseAlternation()
        {
            var branchBase = new HashSet<string>(activeNamedCaptureNames, StringComparer.Ordinal);
            var branchUnion = new HashSet<string>(branchBase, StringComparer.Ordinal);
            using var alternatives = new ScratchPooledList<Node>();
            activeNamedCaptureNames = new(branchBase, StringComparer.Ordinal);
            alternatives.Add(ParseSequence());
            branchUnion.UnionWith(activeNamedCaptureNames);
            while (Peek('|'))
            {
                pos++;
                activeNamedCaptureNames = new(branchBase, StringComparer.Ordinal);
                alternatives.Add(ParseSequence());
                branchUnion.UnionWith(activeNamedCaptureNames);
            }

            activeNamedCaptureNames = branchUnion;

            return alternatives.Count == 1 ? alternatives[0] : new AlternationNode(alternatives.ToArray());
        }

        private Node ParseSequence()
        {
            using var terms = new ScratchPooledList<Node>();
            while (pos < pattern.Length && pattern[pos] != ')' && pattern[pos] != '|')
                terms.Add(ParseQuantifiedAtom());

            return terms.Count == 0 ? new EmptyNode() : terms.Count == 1 ? terms[0] : new SequenceNode(terms.ToArray());
        }

        private Node ParseQuantifiedAtom()
        {
            var atom = ParseAtom();
            if (pos >= pattern.Length)
                return atom;

            int min;
            int max;
            switch (pattern[pos])
            {
                case '*':
                    min = 0;
                    max = int.MaxValue;
                    pos++;
                    break;
                case '+':
                    min = 1;
                    max = int.MaxValue;
                    pos++;
                    break;
                case '?':
                    min = 0;
                    max = 1;
                    pos++;
                    break;
                case '{':
                    if (!TryParseBraceQuantifier(out min, out max))
                        return atom;
                    break;
                default:
                    return atom;
            }

            var greedy = true;
            if (Peek('?'))
            {
                greedy = false;
                pos++;
            }

            if (flags.Unicode && atom is LookaheadNode)
                throw new ArgumentException("Invalid regular expression pattern");

            return new QuantifierNode(atom, min, max, greedy);
        }

        private bool TryParseBraceQuantifier(out int min, out int max)
        {
            min = 0;
            max = 0;
            var save = pos;
            if (!Peek('{'))
                return false;
            pos++;

            if (!TryReadNumber(out min))
            {
                pos = save;
                return false;
            }

            if (Peek('}'))
            {
                pos++;
                max = min;
                return true;
            }

            if (!Peek(','))
            {
                pos = save;
                return false;
            }

            pos++;
            if (Peek('}'))
            {
                pos++;
                max = int.MaxValue;
                return true;
            }

            if (!TryReadNumber(out max) || !Peek('}'))
            {
                pos = save;
                return false;
            }

            pos++;
            if (min > max)
                throw new ArgumentException("Invalid regular expression pattern");
            return true;
        }

        private bool TryReadNumber(out int value)
        {
            value = 0;
            var start = pos;
            long parsed = 0;
            while (pos < pattern.Length && char.IsAsciiDigit(pattern[pos]))
            {
                parsed = parsed * 10 + (pattern[pos] - '0');
                pos++;
            }

            if (pos == start)
                return false;

            value = parsed >= int.MaxValue ? int.MaxValue : (int)parsed;
            return true;
        }

        private Node ParseAtom()
        {
            if (pos >= pattern.Length)
                return new EmptyNode();

            var ch = pattern[pos++];
            if (ch is '*' or '+' or '?')
                throw new ArgumentException("Invalid regular expression pattern");
            if (ch == '{')
            {
                var save = pos - 1;
                pos = save;
                var isQuantifier = TryParseBraceQuantifier(out _, out _);
                pos = save + 1;
                if (isQuantifier)
                    throw new ArgumentException("Invalid regular expression pattern");
            }

            if (flags.Unicode && ch is ')' or ']' or '}' or '{' or '*' or '+' or '?')
                throw new ArgumentException("Invalid regular expression pattern");

            return ch switch
            {
                '(' => ParseGroup(),
                '[' => ParseClass(),
                '.' => new DotNode(),
                '^' => new AnchorNode(true),
                '$' => new AnchorNode(false),
                '\\' => ParseEscape(),
                _ => ParseLiteral(ch)
            };
        }

        private Node ParseLiteral(char ch)
        {
            if (flags.Unicode && char.IsHighSurrogate(ch) && pos < pattern.Length && char.IsLowSurrogate(pattern[pos]))
                return new LiteralNode(char.ConvertToUtf32(ch, pattern[pos++]));
            return new LiteralNode(ch);
        }

        private Node ParseGroup()
        {
            if (Peek('?'))
            {
                pos++;
                if (Peek(':'))
                {
                    pos++;
                    var child = ParseAlternation();
                    Expect(')');
                    return child;
                }

                if (Peek('='))
                {
                    pos++;
                    var child = ParseAlternation();
                    Expect(')');
                    return new LookaheadNode(true, child);
                }

                if (Peek('!'))
                {
                    pos++;
                    var child = ParseAlternation();
                    Expect(')');
                    return new LookaheadNode(false, child);
                }

                if (Peek('<'))
                {
                    pos++;
                    if (Peek('='))
                    {
                        pos++;
                        var lookbehindChild = ParseAlternation();
                        Expect(')');
                        return new LookbehindNode(true, lookbehindChild);
                    }

                    if (Peek('!'))
                    {
                        pos++;
                        var lookbehindChild = ParseAlternation();
                        Expect(')');
                        return new LookbehindNode(false, lookbehindChild);
                    }

                    var name = ParseGroupName();
                    ValidateNamedCaptureIdentifier(name);
                    if (activeNamedCaptureNames.Contains(name))
                        throw new ArgumentException("Duplicate named capturing groups in the same alternative");
                    activeNamedCaptureNames.Add(name);
                    var index = ++captureCount;
                    namedGroupNames.Add(name);
                    if (!namedCaptureIndexes.TryGetValue(name, out var indexes))
                        namedCaptureIndexes[name] = indexes = new();
                    indexes.Add(index);
                    var child = ParseAlternation();
                    Expect(')');
                    return new CaptureNode(index, child);
                }

                if (TryParseScopedModifierPrefix(out var ignoreCaseOverride, out var multilineOverride,
                        out var dotAllOverride))
                {
                    var child = ParseAlternation();
                    Expect(')');
                    return new ScopedModifiersNode(ignoreCaseOverride, multilineOverride, dotAllOverride, child);
                }

                throw new ArgumentException("Invalid regular expression pattern");
            }

            var captureIndex = ++captureCount;
            var captureChild = ParseAlternation();
            Expect(')');
            return new CaptureNode(captureIndex, captureChild);
        }

        private Node ParseEscape()
        {
            if (pos >= pattern.Length)
                throw new ArgumentException("Invalid regular expression pattern");

            var ch = pattern[pos++];
            switch (ch)
            {
                case 'd':
                    return new ClassNode([new(ClassItemKind.Digit)], false);
                case 'D':
                    return new ClassNode([new(ClassItemKind.NotDigit)], false);
                case 's':
                    return new ClassNode([new(ClassItemKind.Space)], false);
                case 'S':
                    return new ClassNode([new(ClassItemKind.NotSpace)], false);
                case 'w':
                    return new ClassNode([new(ClassItemKind.Word)], false);
                case 'W':
                    return new ClassNode([new(ClassItemKind.NotWord)], false);
                case 'b':
                    return new BoundaryNode(true);
                case 'B':
                    return new BoundaryNode(false);
                case 'n':
                    return new LiteralNode('\n');
                case 'r':
                    return new LiteralNode('\r');
                case 't':
                    return new LiteralNode('\t');
                case 'v':
                    return new LiteralNode('\v');
                case 'f':
                    return new LiteralNode('\f');
                case 'c':
                    if (pos < pattern.Length && char.IsAsciiLetter(pattern[pos]))
                    {
                        var controlLetter = char.ToUpperInvariant(pattern[pos++]);
                        return new LiteralNode((char)(controlLetter % 32));
                    }

                    if (flags.Unicode)
                        throw new ArgumentException("Invalid regular expression pattern");
                    return new LiteralNode('c');
                case '0':
                    if (flags.Unicode && pos < pattern.Length && char.IsAsciiDigit(pattern[pos]))
                        throw new ArgumentException("Invalid regular expression pattern");
                    return new LiteralNode('\0');
                case 'u':
                    return ParseUnicodeEscape();
                case 'x':
                    if (!flags.Unicode &&
                        (pos + 1 >= pattern.Length || HexToInt(pattern[pos]) < 0 || HexToInt(pattern[pos + 1]) < 0))
                        return new LiteralNode('x');
                    return ParseHexEscape();
                case 'p':
                    if (flags.Unicode)
                        return ParsePropertyEscape(false);
                    break;
                case 'P':
                    if (flags.Unicode)
                        return ParsePropertyEscape(true);
                    break;
                case 'k':
                    if (Peek('<'))
                    {
                        if (flags.Unicode || hasNamedCapturingGroups)
                            return ParseNamedBackreference();
                        return new LiteralNode('k');
                    }
                    if (flags.Unicode)
                        throw new ArgumentException("Invalid regular expression pattern");
                    return new LiteralNode('k');
            }

            if (char.IsAsciiDigit(ch))
            {
                var number = ch - '0';
                while (pos < pattern.Length && char.IsAsciiDigit(pattern[pos]))
                {
                    number = checked(number * 10 + (pattern[pos] - '0'));
                    pos++;
                }

                if (number > captureCount)
                    unresolvedNumericBackreferences.Add(number);
                return new BackReferenceNode(number);
            }

            if (flags.Unicode && IsInvalidUnicodeIdentityEscape(ch))
                throw new ArgumentException("Invalid regular expression pattern");

            return new LiteralNode(ch);
        }

        private bool TryParseScopedModifierPrefix(out bool? ignoreCaseOverride, out bool? multilineOverride,
            out bool? dotAllOverride)
        {
            ignoreCaseOverride = null;
            multilineOverride = null;
            dotAllOverride = null;

            var save = pos;
            var sawAny = false;
            var sawIgnoreCase = false;
            var sawMultiline = false;
            var sawDotAll = false;

            while (pos < pattern.Length)
            {
                var ch = pattern[pos];
                switch (ch)
                {
                    case 'i':
                        if (sawIgnoreCase)
                            throw new ArgumentException("Invalid regular expression pattern");
                        ignoreCaseOverride = true;
                        sawIgnoreCase = true;
                        sawAny = true;
                        pos++;
                        continue;
                    case 'm':
                        if (sawMultiline)
                            throw new ArgumentException("Invalid regular expression pattern");
                        multilineOverride = true;
                        sawMultiline = true;
                        sawAny = true;
                        pos++;
                        continue;
                    case 's':
                        if (sawDotAll)
                            throw new ArgumentException("Invalid regular expression pattern");
                        dotAllOverride = true;
                        sawDotAll = true;
                        sawAny = true;
                        pos++;
                        continue;
                }

                break;
            }

            if (Peek('-'))
            {
                pos++;
                while (pos < pattern.Length)
                {
                    var ch = pattern[pos];
                    switch (ch)
                    {
                        case 'i':
                            if (sawIgnoreCase)
                                throw new ArgumentException("Invalid regular expression pattern");
                            ignoreCaseOverride = false;
                            sawIgnoreCase = true;
                            sawAny = true;
                            pos++;
                            continue;
                        case 'm':
                            if (sawMultiline)
                                throw new ArgumentException("Invalid regular expression pattern");
                            multilineOverride = false;
                            sawMultiline = true;
                            sawAny = true;
                            pos++;
                            continue;
                        case 's':
                            if (sawDotAll)
                                throw new ArgumentException("Invalid regular expression pattern");
                            dotAllOverride = false;
                            sawDotAll = true;
                            sawAny = true;
                            pos++;
                            continue;
                    }

                    break;
                }
            }

            if (!sawAny || !Peek(':'))
            {
                pos = save;
                ignoreCaseOverride = null;
                multilineOverride = null;
                dotAllOverride = null;
                return false;
            }

            pos++;
            return true;
        }

        private Node ParseUnicodeEscape()
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
                        throw new ArgumentException("Invalid regular expression pattern");
                    scalar = checked((scalar << 4) | hex);
                    pos++;
                }

                if (pos == start || pos >= pattern.Length)
                    throw new ArgumentException("Invalid regular expression pattern");
                pos++;
                return new LiteralNode(scalar);
            }

            if (pos + 4 > pattern.Length)
                throw new ArgumentException("Invalid regular expression pattern");

            var value = 0;
            for (var i = 0; i < 4; i++)
            {
                var hex = HexToInt(pattern[pos++]);
                if (hex < 0)
                    throw new ArgumentException("Invalid regular expression pattern");
                value = (value << 4) | hex;
            }

            if (flags.Unicode &&
                char.IsHighSurrogate((char)value) &&
                TryConsumeTrailingLowSurrogateEscape((char)value, out var codePoint))
                return new LiteralNode(codePoint);

            return new LiteralNode(value);
        }

        private Node ParseNamedBackreference()
        {
            if (!Peek('<'))
                return new LiteralNode('k');

            pos++;
            var name = ParseGroupName();
            ValidateNamedCaptureIdentifier(name);
            if (!namedCaptureIndexes.TryGetValue(name, out var indexes) || indexes.Count == 0)
                unresolvedNamedBackreferences.Add(name);
            return new NamedBackReferenceNode(name);
        }

        private static bool ContainsNamedCapturingGroups(string pattern)
        {
            var escaped = false;
            var inCharacterClass = false;

            for (var i = 0; i < pattern.Length; i++)
            {
                var ch = pattern[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (inCharacterClass)
                {
                    if (ch == ']')
                        inCharacterClass = false;
                    continue;
                }

                if (ch == '[')
                {
                    inCharacterClass = true;
                    continue;
                }

                if (ch != '(' || i + 3 >= pattern.Length || pattern[i + 1] != '?' || pattern[i + 2] != '<')
                    continue;

                var discriminator = pattern[i + 3];
                if (discriminator is '=' or '!')
                    continue;

                return true;
            }

            return false;
        }

        private Node ParsePropertyEscape(bool negated)
        {
            if (!Peek('{'))
                throw new ArgumentException("Invalid regular expression pattern");

            pos++;
            var start = pos;
            while (pos < pattern.Length && pattern[pos] != '}')
                pos++;
            if (pos >= pattern.Length || pos == start)
                throw new ArgumentException("Invalid regular expression pattern");

            var body = pattern.Substring(start, pos - start);
            pos++;
            var (kind, categories, propertyValue) = ParsePropertyEscapeSpec(body, negated);
            return new PropertyEscapeNode(kind, negated, categories, propertyValue);
        }

        private (PropertyEscapeKind Kind, GeneralCategoryMask Categories, string? PropertyValue)
            ParsePropertyEscapeSpec(string body, bool negated)
        {
            var equalsIndex = body.IndexOf('=');
            if (equalsIndex < 0)
            {
                if (flags.UnicodeSets && ScratchUnicodeStringPropertyTables.IsSupported(body))
                {
                    if (negated)
                        throw new ArgumentException("Invalid regular expression pattern");
                    return (PropertyEscapeKind.StringProperty, GeneralCategoryMask.None, body);
                }

                return body switch
                {
                    "ASCII_Hex_Digit" or "AHex" => (PropertyEscapeKind.AsciiHexDigit, GeneralCategoryMask.None, null),
                    "Hex_Digit" or "Hex" => (PropertyEscapeKind.HexDigit, GeneralCategoryMask.None, null),
                    "ASCII" => (PropertyEscapeKind.Ascii, GeneralCategoryMask.None, null),
                    "Assigned" => (PropertyEscapeKind.Assigned, GeneralCategoryMask.None, null),
                    "Any" => (PropertyEscapeKind.Any, GeneralCategoryMask.None, null),
                    "Alphabetic" or "Alpha" => (PropertyEscapeKind.Alphabetic, GeneralCategoryMask.None, null),
                    "Grapheme_Base" or "Gr_Base" => (PropertyEscapeKind.GraphemeBase, GeneralCategoryMask.None, null),
                    "Grapheme_Extend" or "Gr_Ext" => (PropertyEscapeKind.GraphemeExtend, GeneralCategoryMask.None,
                        null),
                    "Ideographic" or "Ideo" => (PropertyEscapeKind.Ideographic, GeneralCategoryMask.None, null),
                    "Join_Control" or "Join_C" => (PropertyEscapeKind.JoinControl, GeneralCategoryMask.None, null),
                    "Lu" or "Uppercase_Letter" => (PropertyEscapeKind.UppercaseLetter, GeneralCategoryMask.None, null),
                    "Cased" => (PropertyEscapeKind.Cased, GeneralCategoryMask.None, null),
                    "Case_Ignorable" or "CI" => (PropertyEscapeKind.CaseIgnorable, GeneralCategoryMask.None, null),
                    "Bidi_Control" or "Bidi_C" => (PropertyEscapeKind.BidiControl, GeneralCategoryMask.None, null),
                    "Bidi_Mirrored" or "Bidi_M" => (PropertyEscapeKind.BidiMirrored, GeneralCategoryMask.None, null),
                    "Dash" => (PropertyEscapeKind.Dash, GeneralCategoryMask.None, null),
                    "Deprecated" or "Dep" => (PropertyEscapeKind.Deprecated, GeneralCategoryMask.None, null),
                    "IDS_Binary_Operator" or "IDSB" => (PropertyEscapeKind.IdsBinaryOperator, GeneralCategoryMask.None,
                        null),
                    "IDS_Trinary_Operator" or "IDST" => (PropertyEscapeKind.IdsTrinaryOperator,
                        GeneralCategoryMask.None, null),
                    "Logical_Order_Exception" or "LOE" => (PropertyEscapeKind.LogicalOrderException,
                        GeneralCategoryMask.None, null),
                    "Lowercase" or "Lower" => (PropertyEscapeKind.Lowercase, GeneralCategoryMask.None, null),
                    "Changes_When_Casemapped" or "CWCM" => (PropertyEscapeKind.ChangesWhenCaseMapped,
                        GeneralCategoryMask.None, null),
                    "Changes_When_Casefolded" or "CWCF" => (PropertyEscapeKind.ChangesWhenCasefolded,
                        GeneralCategoryMask.None, null),
                    "Changes_When_Lowercased" or "CWL" => (PropertyEscapeKind.ChangesWhenLowercased,
                        GeneralCategoryMask.None, null),
                    "Changes_When_Titlecased" or "CWT" => (PropertyEscapeKind.ChangesWhenTitlecased,
                        GeneralCategoryMask.None, null),
                    "Changes_When_Uppercased" or "CWU" => (PropertyEscapeKind.ChangesWhenUppercased,
                        GeneralCategoryMask.None, null),
                    "Changes_When_NFKC_Casefolded" or "CWKCF" => (PropertyEscapeKind.ChangesWhenNfkcCasefolded,
                        GeneralCategoryMask.None, null),
                    "Diacritic" or "Dia" => (PropertyEscapeKind.Diacritic, GeneralCategoryMask.None, null),
                    "Emoji_Component" or "EComp" => (PropertyEscapeKind.EmojiComponent, GeneralCategoryMask.None, null),
                    "Emoji_Modifier" or "EMod" => (PropertyEscapeKind.EmojiModifier, GeneralCategoryMask.None, null),
                    "Emoji_Modifier_Base" or "EBase" => (PropertyEscapeKind.EmojiModifierBase, GeneralCategoryMask.None,
                        null),
                    "Emoji" => (PropertyEscapeKind.Emoji, GeneralCategoryMask.None, null),
                    "Emoji_Presentation" or "EPres" => (PropertyEscapeKind.EmojiPresentation, GeneralCategoryMask.None,
                        null),
                    "Default_Ignorable_Code_Point" or "DI" => (PropertyEscapeKind.DefaultIgnorableCodePoint,
                        GeneralCategoryMask.None, null),
                    "Extender" or "Ext" => (PropertyEscapeKind.Extender, GeneralCategoryMask.None, null),
                    "Extended_Pictographic" or "ExtPict" => (PropertyEscapeKind.ExtendedPictographic,
                        GeneralCategoryMask.None, null),
                    "ID_Start" or "IDS" => (PropertyEscapeKind.IdStart, GeneralCategoryMask.None, null),
                    "ID_Continue" or "IDC" => (PropertyEscapeKind.IdContinue, GeneralCategoryMask.None, null),
                    "XID_Start" or "XIDS" => (PropertyEscapeKind.XidStart, GeneralCategoryMask.None, null),
                    "XID_Continue" or "XIDC" => (PropertyEscapeKind.XidContinue, GeneralCategoryMask.None, null),
                    "Math" => (PropertyEscapeKind.Math, GeneralCategoryMask.None, null),
                    "Noncharacter_Code_Point" or "NChar" => (PropertyEscapeKind.NoncharacterCodePoint,
                        GeneralCategoryMask.None, null),
                    "Pattern_Syntax" or "Pat_Syn" => (PropertyEscapeKind.PatternSyntax, GeneralCategoryMask.None, null),
                    "Pattern_White_Space" or "Pat_WS" => (PropertyEscapeKind.PatternWhiteSpace,
                        GeneralCategoryMask.None, null),
                    "White_Space" or "space" => (PropertyEscapeKind.WhiteSpace, GeneralCategoryMask.None, null),
                    "Variation_Selector" or "VS" => (PropertyEscapeKind.VariationSelector, GeneralCategoryMask.None,
                        null),
                    "Uppercase" or "Upper" => (PropertyEscapeKind.Uppercase, GeneralCategoryMask.None, null),
                    "Unified_Ideograph" or "UIdeo" => (PropertyEscapeKind.UnifiedIdeograph, GeneralCategoryMask.None,
                        null),
                    "Terminal_Punctuation" or "Term" => (PropertyEscapeKind.TerminalPunctuation,
                        GeneralCategoryMask.None, null),
                    "Soft_Dotted" or "SD" => (PropertyEscapeKind.SoftDotted, GeneralCategoryMask.None, null),
                    "Sentence_Terminal" or "STerm" => (PropertyEscapeKind.SentenceTerminal, GeneralCategoryMask.None,
                        null),
                    "Quotation_Mark" or "QMark" => (PropertyEscapeKind.QuotationMark, GeneralCategoryMask.None, null),
                    "Radical" => (PropertyEscapeKind.Radical, GeneralCategoryMask.None, null),
                    "Regional_Indicator" or "RI" => (PropertyEscapeKind.RegionalIndicator, GeneralCategoryMask.None,
                        null),
                    _ when TryParseGeneralCategory(body, out var categories) => (PropertyEscapeKind.GeneralCategory,
                        categories, null),
                    _ => throw new ArgumentException("Invalid regular expression pattern")
                };
            }

            var property = body[..equalsIndex];
            var value = body[(equalsIndex + 1)..];
            if (property is "General_Category" or "gc")
            {
                if (TryParseGeneralCategory(value, out var categories))
                    return (PropertyEscapeKind.GeneralCategory, categories, null);
                throw new ArgumentException("Invalid regular expression pattern");
            }

            if (property is "Script" or "sc")
            {
                if (ScratchUnicodeScriptTables.IsSupported(value))
                    return (PropertyEscapeKind.Script, GeneralCategoryMask.None, value);
                throw new ArgumentException("Invalid regular expression pattern");
            }

            if (property is "Script_Extensions" or "scx")
            {
                if (ScratchUnicodeScriptExtensionsTables.IsSupported(value))
                    return (PropertyEscapeKind.ScriptExtensions, GeneralCategoryMask.None, value);
                return value switch
                {
                    "Unknown" or "Zzzz" => (PropertyEscapeKind.ScriptExtensionsUnknown, GeneralCategoryMask.None, null),
                    _ => throw new ArgumentException("Invalid regular expression pattern")
                };
            }

            throw new ArgumentException("Invalid regular expression pattern");
        }

        private static bool TryParseGeneralCategory(string value, out GeneralCategoryMask categories)
        {
            categories = value switch
            {
                "Lu" or "Uppercase_Letter" => GeneralCategoryMask.UppercaseLetter,
                "Ll" or "Lowercase_Letter" => GeneralCategoryMask.LowercaseLetter,
                "Lt" or "Titlecase_Letter" => GeneralCategoryMask.TitlecaseLetter,
                "Lm" or "Modifier_Letter" => GeneralCategoryMask.ModifierLetter,
                "Lo" or "Other_Letter" => GeneralCategoryMask.OtherLetter,
                "L" or "Letter" => GeneralCategoryMask.Letter,
                "LC" or "Cased_Letter" => GeneralCategoryMask.CasedLetter,
                "Mn" or "Nonspacing_Mark" => GeneralCategoryMask.NonSpacingMark,
                "Mc" or "Spacing_Mark" or "Spacing_Combining_Mark" => GeneralCategoryMask.SpacingCombiningMark,
                "Me" or "Enclosing_Mark" => GeneralCategoryMask.EnclosingMark,
                "M" or "Mark" or "Combining_Mark" => GeneralCategoryMask.Mark,
                "Nd" or "Decimal_Number" or "digit" => GeneralCategoryMask.DecimalDigitNumber,
                "Nl" or "Letter_Number" => GeneralCategoryMask.LetterNumber,
                "No" or "Other_Number" => GeneralCategoryMask.OtherNumber,
                "N" or "Number" => GeneralCategoryMask.Number,
                "Pc" or "Connector_Punctuation" => GeneralCategoryMask.ConnectorPunctuation,
                "Pd" or "Dash_Punctuation" => GeneralCategoryMask.DashPunctuation,
                "Ps" or "Open_Punctuation" => GeneralCategoryMask.OpenPunctuation,
                "Pe" or "Close_Punctuation" => GeneralCategoryMask.ClosePunctuation,
                "Pi" or "Initial_Punctuation" or "Initial_Quote_Punctuation" => GeneralCategoryMask
                    .InitialQuotePunctuation,
                "Pf" or "Final_Punctuation" or "Final_Quote_Punctuation" => GeneralCategoryMask.FinalQuotePunctuation,
                "Po" or "Other_Punctuation" => GeneralCategoryMask.OtherPunctuation,
                "P" or "Punctuation" or "punct" => GeneralCategoryMask.Punctuation,
                "Sm" or "Math_Symbol" => GeneralCategoryMask.MathSymbol,
                "Sc" or "Currency_Symbol" => GeneralCategoryMask.CurrencySymbol,
                "Sk" or "Modifier_Symbol" => GeneralCategoryMask.ModifierSymbol,
                "So" or "Other_Symbol" => GeneralCategoryMask.OtherSymbol,
                "S" or "Symbol" => GeneralCategoryMask.Symbol,
                "Zs" or "Space_Separator" => GeneralCategoryMask.SpaceSeparator,
                "Zl" or "Line_Separator" => GeneralCategoryMask.LineSeparator,
                "Zp" or "Paragraph_Separator" => GeneralCategoryMask.ParagraphSeparator,
                "Z" or "Separator" => GeneralCategoryMask.Separator,
                "Cc" or "Control" or "cntrl" => GeneralCategoryMask.Control,
                "Cf" or "Format" => GeneralCategoryMask.Format,
                "Cs" or "Surrogate" => GeneralCategoryMask.Surrogate,
                "Co" or "Private_Use" => GeneralCategoryMask.PrivateUse,
                "Cn" or "Unassigned" => GeneralCategoryMask.Unassigned,
                "C" or "Other" => GeneralCategoryMask.Other,
                _ => GeneralCategoryMask.None
            };

            return categories != GeneralCategoryMask.None;
        }

        private Node ParseClass()
        {
            if (flags.UnicodeSets)
                return ParseUnicodeSetClass();

            var negated = Peek('^');
            if (negated)
                pos++;

            using var items = new ScratchPooledList<ClassItem>();
            if (Peek(']'))
            {
                pos++;
                return new ClassNode(Array.Empty<ClassItem>(), negated);
            }

            while (pos < pattern.Length)
            {
                if (Peek(']'))
                {
                    pos++;
                    break;
                }

                var left = ParseClassAtom();
                if (negated && IsStringClassItem(left))
                    throw new ArgumentException("Invalid regular expression pattern");
                if (Peek('-') && pos + 1 < pattern.Length && pattern[pos + 1] != ']')
                {
                    pos++;
                    var right = ParseClassAtom();
                    if (negated && IsStringClassItem(right))
                        throw new ArgumentException("Invalid regular expression pattern");
                    if (left.Kind != ClassItemKind.Literal || right.Kind != ClassItemKind.Literal)
                        throw new ArgumentException("Invalid regular expression pattern");
                    if (right.CodePoint < left.CodePoint)
                        throw new ArgumentException("Range out of order in character class");
                    items.Add(new(ClassItemKind.Range, RangeStart: left.CodePoint, RangeEnd: right.CodePoint));
                }
                else
                {
                    items.Add(left);
                }
            }

            if (pos == 0 || pattern[pos - 1] != ']')
                throw new ArgumentException("Invalid regular expression pattern");

            return new ClassNode(items.ToArray(), negated);
        }

        private Node ParseUnicodeSetClass()
        {
            var negated = Peek('^');
            if (negated)
                pos++;

            if (Peek(']'))
            {
                pos++;
                return new ClassNode(Array.Empty<ClassItem>(), negated);
            }

            var expression = ParseClassSetExpression();
            if (!Peek(']'))
                throw new ArgumentException("Invalid regular expression pattern");

            pos++;

            if (negated && ClassSetExpressionContainsStrings(expression))
                throw new ArgumentException("Invalid regular expression pattern");

            return new ClassNode(Array.Empty<ClassItem>(), negated, expression);
        }

        private ClassSetExpression ParseClassSetExpression()
        {
            var left = ParseClassSetUnionExpression();
            while (true)
            {
                if (Peek('&') && pos + 1 < pattern.Length && pattern[pos + 1] == '&')
                {
                    pos += 2;
                    var right = ParseClassSetUnionExpression();
                    left = new ClassSetIntersectionExpression(left, right);
                    continue;
                }

                if (Peek('-') && pos + 1 < pattern.Length && pattern[pos + 1] == '-')
                {
                    pos += 2;
                    var right = ParseClassSetUnionExpression();
                    left = new ClassSetDifferenceExpression(left, right);
                    continue;
                }

                break;
            }

            return left;
        }

        private ClassSetExpression ParseClassSetUnionExpression()
        {
            using var terms = new ScratchPooledList<ClassSetExpression>();
            while (pos < pattern.Length && !Peek(']') && !IsClassSetBinaryOperatorAhead())
                terms.Add(ParseClassSetOperandExpression());

            if (terms.Count == 0)
                throw new ArgumentException("Invalid regular expression pattern");

            return terms.Count == 1 ? terms[0] : new ClassSetUnionExpression(terms.ToArray());
        }

        private ClassSetExpression ParseClassSetOperandExpression()
        {
            if (Peek('['))
            {
                pos++;
                var nested = (ClassNode)ParseUnicodeSetClass();
                return new ClassSetNestedExpression(nested);
            }

            var left = ParseClassAtom();
            if (Peek('-') &&
                pos + 1 < pattern.Length &&
                pattern[pos + 1] != ']' &&
                pattern[pos + 1] != '-' &&
                left.Kind == ClassItemKind.Literal)
            {
                pos++;
                var right = ParseClassAtom();
                if (right.Kind != ClassItemKind.Literal)
                    throw new ArgumentException("Invalid regular expression pattern");
                if (right.CodePoint < left.CodePoint)
                    throw new ArgumentException("Range out of order in character class");
                return new ClassSetItemExpression(new(ClassItemKind.Range, RangeStart: left.CodePoint,
                    RangeEnd: right.CodePoint));
            }

            return new ClassSetItemExpression(left);
        }

        private bool IsClassSetBinaryOperatorAhead()
        {
            return (Peek('&') && pos + 1 < pattern.Length && pattern[pos + 1] == '&') ||
                   (Peek('-') && pos + 1 < pattern.Length && pattern[pos + 1] == '-');
        }

        private ClassItem ParseClassAtom()
        {
            if (pos >= pattern.Length)
                throw new ArgumentException("Invalid regular expression pattern");

            var ch = pattern[pos++];
            if (ch == '\\' && pos < pattern.Length)
            {
                var next = pattern[pos++];
                return next switch
                {
                    'b' => new(ClassItemKind.Literal, '\b'),
                    'f' => new(ClassItemKind.Literal, '\f'),
                    'n' => new(ClassItemKind.Literal, '\n'),
                    'r' => new(ClassItemKind.Literal, '\r'),
                    't' => new(ClassItemKind.Literal, '\t'),
                    'v' => new(ClassItemKind.Literal, '\v'),
                    'q' when flags.UnicodeSets && Peek('{') => ParseClassStringDisjunction(),
                    'd' => new(ClassItemKind.Digit),
                    'D' => new(ClassItemKind.NotDigit),
                    's' => new(ClassItemKind.Space),
                    'S' => new(ClassItemKind.NotSpace),
                    'w' => new(ClassItemKind.Word),
                    'W' => new(ClassItemKind.NotWord),
                    'p' when flags.Unicode => ParseClassPropertyEscape(false),
                    'P' when flags.Unicode => ParseClassPropertyEscape(true),
                    'x' => ParseClassHexEscape(),
                    'u' when Peek('{') => ParseClassUnicodeEscape(),
                    'u' => ParseClassUnicodeEscapeFixed(),
                    'c' => ParseClassControlEscape(),
                    _ when flags.Unicode && IsInvalidUnicodeClassIdentityEscape(next) => throw new ArgumentException(
                        "Invalid regular expression pattern"),
                    _ => new(ClassItemKind.Literal, next)
                };
            }

            if (flags.Unicode && char.IsHighSurrogate(ch) && pos < pattern.Length && char.IsLowSurrogate(pattern[pos]))
                return new(ClassItemKind.Literal, char.ConvertToUtf32(ch, pattern[pos++]));

            return new(ClassItemKind.Literal, ch);
        }

        private ClassItem ParseClassControlEscape()
        {
            if (pos >= pattern.Length)
                return new(ClassItemKind.Literal, 'c');

            var control = pattern[pos];
            if (!char.IsAsciiLetter(control))
            {
                if (flags.Unicode)
                    throw new ArgumentException("Invalid regular expression pattern");
                return new(ClassItemKind.Literal, 'c');
            }

            pos++;
            int upper = char.ToUpperInvariant(control);
            return new(ClassItemKind.Literal, upper % 32);
        }

        private ClassItem ParseClassStringDisjunction()
        {
            pos++;
            using var strings = new ScratchPooledList<string>();
            var current = new StringBuilder();
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

                AppendClassStringCharacter(current);
            }

            throw new ArgumentException("Invalid regular expression pattern");
        }

        private void AppendClassStringCharacter(StringBuilder builder)
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
                    AppendCodePoint(builder, ParseFixedHexEscape(2));
                    return;
                case 'u' when Peek('{'):
                    pos++;
                    AppendCodePoint(builder, ParseBracedUnicodeScalar());
                    return;
                case 'u':
                    AppendCodePoint(builder, ParseFixedHexEscape(4));
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

        private static void AppendCodePoint(StringBuilder builder, int codePoint)
        {
            if (codePoint <= 0xFFFF)
            {
                builder.Append((char)codePoint);
                return;
            }

            builder.Append(char.ConvertFromUtf32(codePoint));
        }

        private ClassItem ParseClassUnicodeEscape()
        {
            pos++;
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
            return new(ClassItemKind.Literal, scalar);
        }

        private Node ParseHexEscape()
        {
            if (pos + 2 > pattern.Length)
                throw new ArgumentException("Invalid regular expression pattern");

            var value = 0;
            for (var i = 0; i < 2; i++)
            {
                var hex = HexToInt(pattern[pos++]);
                if (hex < 0)
                    throw new ArgumentException("Invalid regular expression pattern");
                value = (value << 4) | hex;
            }

            return new LiteralNode(value);
        }

        private ClassItem ParseClassHexEscape()
        {
            if (pos + 2 > pattern.Length)
                throw new ArgumentException("Invalid regular expression pattern");

            var value = 0;
            for (var i = 0; i < 2; i++)
            {
                var hex = HexToInt(pattern[pos++]);
                if (hex < 0)
                    throw new ArgumentException("Invalid regular expression pattern");
                value = (value << 4) | hex;
            }

            return new(ClassItemKind.Literal, value);
        }

        private ClassItem ParseClassPropertyEscape(bool negated)
        {
            if (!Peek('{'))
                throw new ArgumentException("Invalid regular expression pattern");

            pos++;
            var start = pos;
            while (pos < pattern.Length && pattern[pos] != '}')
                pos++;
            if (pos >= pattern.Length || pos == start)
                throw new ArgumentException("Invalid regular expression pattern");

            var body = pattern.Substring(start, pos - start);
            pos++;
            var (kind, categories, propertyValue) = ParsePropertyEscapeSpec(body, negated);
            return new(ClassItemKind.PropertyEscape, PropertyKind: kind, PropertyNegated: negated,
                PropertyCategories: categories, PropertyValue: propertyValue);
        }

        private ClassItem ParseClassUnicodeEscapeFixed()
        {
            if (pos + 4 > pattern.Length)
                throw new ArgumentException("Invalid regular expression pattern");

            var value = 0;
            for (var i = 0; i < 4; i++)
            {
                var hex = HexToInt(pattern[pos++]);
                if (hex < 0)
                    throw new ArgumentException("Invalid regular expression pattern");
                value = (value << 4) | hex;
            }

            if (flags.Unicode &&
                char.IsHighSurrogate((char)value) &&
                TryConsumeTrailingLowSurrogateEscape((char)value, out var codePoint))
                return new(ClassItemKind.Literal, codePoint);

            return new(ClassItemKind.Literal, value);
        }

        private bool TryConsumeTrailingLowSurrogateEscape(char highSurrogate, out int codePoint)
        {
            codePoint = 0;
            if (pos + 6 > pattern.Length || pattern[pos] != '\\' || pattern[pos + 1] != 'u')
                return false;

            var value = 0;
            for (var i = pos + 2; i < pos + 6; i++)
            {
                var hex = HexToInt(pattern[i]);
                if (hex < 0)
                    return false;
                value = (value << 4) | hex;
            }

            if (!char.IsLowSurrogate((char)value))
                return false;

            codePoint = char.ConvertToUtf32(highSurrogate, (char)value);
            pos += 6;
            return true;
        }

        private static int HexToInt(char ch)
        {
            if (ch is >= '0' and <= '9') return ch - '0';
            if (ch is >= 'a' and <= 'f') return ch - 'a' + 10;
            if (ch is >= 'A' and <= 'F') return ch - 'A' + 10;
            return -1;
        }

        private string ParseGroupName()
        {
            var builder = new StringBuilder();
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

                AppendGroupNameUnicodeEscape(builder);
            }

            if (pos >= pattern.Length || builder.Length == 0)
                throw new ArgumentException("Invalid capture group name");

            pos++;
            return builder.ToString();
        }

        private void AppendGroupNameUnicodeEscape(StringBuilder builder)
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
                builder.Append(char.ConvertFromUtf32(scalar));
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

        private static bool IsInvalidUnicodeIdentityEscape(char ch)
        {
            if (ch > 0x7F)
                return false;

            return ch is not ('^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}'
                or '|' or '/');
        }

        private static bool IsInvalidUnicodeClassIdentityEscape(char ch)
        {
            if (ch > 0x7F)
                return false;

            return ch is not ('^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}'
                or '|' or '/' or '-');
        }

        private bool Peek(char ch)
        {
            return pos < pattern.Length && pattern[pos] == ch;
        }

        private static bool IsStringClassItem(ClassItem item)
        {
            return item.Kind == ClassItemKind.StringLiteral ||
                   (item.Kind == ClassItemKind.PropertyEscape &&
                    item.PropertyKind == PropertyEscapeKind.StringProperty);
        }

        private static bool ClassSetExpressionContainsStrings(ClassSetExpression expression)
        {
            return expression switch
            {
                ClassSetItemExpression itemExpr => IsStringClassItem(itemExpr.Item),
                ClassSetNestedExpression nested => nested.Class.Expression is not null
                    ? ClassSetExpressionContainsStrings(nested.Class.Expression)
                    : Array.Exists(nested.Class.Items, IsStringClassItem),
                ClassSetUnionExpression union => Array.Exists(union.Terms, ClassSetExpressionContainsStrings),
                ClassSetIntersectionExpression intersection => ClassSetExpressionContainsStrings(intersection.Left) ||
                                                               ClassSetExpressionContainsStrings(intersection.Right),
                ClassSetDifferenceExpression difference => ClassSetExpressionContainsStrings(difference.Left) ||
                                                           ClassSetExpressionContainsStrings(difference.Right),
                _ => false
            };
        }

        private void Expect(char ch)
        {
            if (!Peek(ch))
                throw new ArgumentException("Invalid regular expression pattern");
            pos++;
        }

        private static void ValidateNamedCaptureIdentifier(string name)
        {
            if (name.Length == 0)
                throw new ArgumentException("Invalid capture group name");

            var first = true;
            foreach (var rune in name.EnumerateRunes())
            {
                if (first)
                {
                    if (!IsIdentifierStart(rune))
                        throw new ArgumentException("Invalid capture group name");
                    first = false;
                    continue;
                }

                if (!IsIdentifierContinue(rune))
                    throw new ArgumentException("Invalid capture group name");
            }
        }

        private static bool IsIdentifierStart(Rune rune)
        {
            if (rune.Value is '$' or '_')
                return true;

            var category = Rune.GetUnicodeCategory(rune);
            return category is
                UnicodeCategory.UppercaseLetter or
                UnicodeCategory.LowercaseLetter or
                UnicodeCategory.TitlecaseLetter or
                UnicodeCategory.ModifierLetter or
                UnicodeCategory.OtherLetter or
                UnicodeCategory.LetterNumber;
        }

        private static bool IsIdentifierContinue(Rune rune)
        {
            if (IsIdentifierStart(rune))
                return true;

            var category = Rune.GetUnicodeCategory(rune);
            return rune.Value is 0x200C or 0x200D ||
                   category is
                       UnicodeCategory.DecimalDigitNumber or
                       UnicodeCategory.NonSpacingMark or
                       UnicodeCategory.SpacingCombiningMark or
                       UnicodeCategory.ConnectorPunctuation;
        }

        private static bool TryGetLeadingLiteral(Node node, out int codePoint)
        {
            switch (node)
            {
                case EmptyNode:
                case AnchorNode:
                case BoundaryNode:
                    codePoint = default;
                    return false;
                case LiteralNode literal:
                    codePoint = literal.CodePoint;
                    return true;
                case CaptureNode capture:
                    return TryGetLeadingLiteral(capture.Child, out codePoint);
                case LookaheadNode:
                case LookbehindNode:
                    codePoint = default;
                    return false;
                case ScopedModifiersNode scoped:
                    return TryGetLeadingLiteral(scoped.Child, out codePoint);
                case PropertyEscapeNode:
                    codePoint = default;
                    return false;
                case QuantifierNode quantifier:
                    if (quantifier.Min == 0)
                        goto default;
                    return TryGetLeadingLiteral(quantifier.Child, out codePoint);
                case SequenceNode sequence:
                    foreach (var term in sequence.Terms)
                    {
                        if (term is EmptyNode or AnchorNode or BoundaryNode)
                            continue;

                        if (term is CaptureNode captureTerm && TryGetLeadingLiteral(captureTerm.Child, out codePoint))
                            return true;
                        if (term is LookaheadNode or LookbehindNode)
                            continue;
                        if (term is QuantifierNode quantifierTerm)
                        {
                            if (quantifierTerm.Min == 0)
                            {
                                codePoint = default;
                                return false;
                            }

                            if (TryGetLeadingLiteral(quantifierTerm.Child, out codePoint))
                                return true;
                            return false;
                        }

                        if (term is LiteralNode literalTerm)
                        {
                            codePoint = literalTerm.CodePoint;
                            return true;
                        }

                        codePoint = default;
                        return false;
                    }

                    codePoint = default;
                    return false;
                default:
                    codePoint = default;
                    return false;
            }
        }

        private static int[] GetRequiredLiteralPrefix(Node node)
        {
            var prefix = new List<int>(4);
            AppendRequiredLiteralPrefix(node, prefix);
            if (prefix.Count > MaxRequiredLiteralPrefixLength)
                prefix.RemoveRange(MaxRequiredLiteralPrefixLength, prefix.Count - MaxRequiredLiteralPrefixLength);
            return prefix.ToArray();
        }

        private static bool AppendRequiredLiteralPrefix(Node node, List<int> prefix)
        {
            if (prefix.Count >= MaxRequiredLiteralPrefixLength)
                return false;

            switch (node)
            {
                case EmptyNode:
                case AnchorNode:
                case BoundaryNode:
                case LookaheadNode:
                case LookbehindNode:
                    return true;
                case SequenceNode sequence:
                    for (var i = 0; i < sequence.Terms.Length && prefix.Count < MaxRequiredLiteralPrefixLength; i++)
                    {
                        if (!AppendRequiredLiteralPrefix(sequence.Terms[i], prefix))
                            return false;
                    }

                    return true;
                case QuantifierNode quantifier:
                {
                    if (quantifier.Min == 0 || !TryGetExactLiteral(quantifier.Child, out var literalCodePoints))
                        return false;

                    for (var repeat = 0; repeat < quantifier.Min && prefix.Count < MaxRequiredLiteralPrefixLength; repeat++)
                    for (var i = 0; i < literalCodePoints.Length && prefix.Count < MaxRequiredLiteralPrefixLength; i++)
                        prefix.Add(literalCodePoints[i]);

                    return quantifier.Max == quantifier.Min;
                }
                default:
                    if (!TryGetExactLiteral(node, out var exactLiteral))
                        return false;

                    for (var i = 0; i < exactLiteral.Length && prefix.Count < MaxRequiredLiteralPrefixLength; i++)
                        prefix.Add(exactLiteral[i]);
                    return true;
            }
        }

        private static bool TryGetExactLiteral(Node node, out int[] codePoints)
        {
            switch (node)
            {
                case EmptyNode:
                case AnchorNode:
                case BoundaryNode:
                case LookaheadNode:
                case LookbehindNode:
                    codePoints = [];
                    return true;
                case LiteralNode literal:
                    codePoints = [literal.CodePoint];
                    return true;
                case CaptureNode capture:
                    return TryGetExactLiteral(capture.Child, out codePoints);
                case ScopedModifiersNode scoped:
                    return TryGetExactLiteral(scoped.Child, out codePoints);
                case SequenceNode sequence:
                {
                    var builder = new List<int>(sequence.Terms.Length);
                    for (var i = 0; i < sequence.Terms.Length; i++)
                    {
                        if (!TryGetExactLiteral(sequence.Terms[i], out var termLiteral))
                        {
                            codePoints = [];
                            return false;
                        }

                        builder.AddRange(termLiteral);
                    }

                    codePoints = builder.ToArray();
                    return true;
                }
                case QuantifierNode quantifier when quantifier.Min == quantifier.Max:
                {
                    if (!TryGetExactLiteral(quantifier.Child, out var childLiteral))
                    {
                        codePoints = [];
                        return false;
                    }

                    var builder = new List<int>(Math.Min(MaxRequiredLiteralPrefixLength, childLiteral.Length * quantifier.Min));
                    for (var repeat = 0; repeat < quantifier.Min && builder.Count < MaxRequiredLiteralPrefixLength; repeat++)
                        builder.AddRange(childLiteral);
                    if (builder.Count > MaxRequiredLiteralPrefixLength)
                        builder.RemoveRange(MaxRequiredLiteralPrefixLength, builder.Count - MaxRequiredLiteralPrefixLength);
                    codePoints = builder.ToArray();
                    return true;
                }
                default:
                    codePoints = [];
                    return false;
            }
        }

        private static int[] GetExactLiteralPattern(Node node)
        {
            return TryGetExactLiteralPattern(node, out var exactLiteral) ? exactLiteral : [];
        }

        private static bool TryGetExactLiteralPattern(Node node, out int[] codePoints)
        {
            switch (node)
            {
                case EmptyNode:
                    codePoints = [];
                    return true;
                case LiteralNode literal:
                    codePoints = [literal.CodePoint];
                    return true;
                case CaptureNode capture:
                    return TryGetExactLiteralPattern(capture.Child, out codePoints);
                case ScopedModifiersNode scoped:
                    return TryGetExactLiteralPattern(scoped.Child, out codePoints);
                case SequenceNode sequence:
                {
                    var builder = new List<int>(sequence.Terms.Length);
                    for (var i = 0; i < sequence.Terms.Length; i++)
                    {
                        if (!TryGetExactLiteralPattern(sequence.Terms[i], out var termLiteral))
                        {
                            codePoints = [];
                            return false;
                        }

                        builder.AddRange(termLiteral);
                        if (builder.Count > MaxExactLiteralPatternLength)
                        {
                            codePoints = [];
                            return false;
                        }
                    }

                    codePoints = builder.ToArray();
                    return true;
                }
                case QuantifierNode quantifier when quantifier.Min == quantifier.Max:
                {
                    if (!TryGetExactLiteralPattern(quantifier.Child, out var childLiteral))
                    {
                        codePoints = [];
                        return false;
                    }

                    if (childLiteral.Length == 0)
                    {
                        codePoints = [];
                        return false;
                    }

                    if ((long)childLiteral.Length * quantifier.Min > MaxExactLiteralPatternLength)
                    {
                        codePoints = [];
                        return false;
                    }

                    var builder = new List<int>(childLiteral.Length * quantifier.Min);
                    for (var repeat = 0; repeat < quantifier.Min; repeat++)
                        builder.AddRange(childLiteral);
                    codePoints = builder.ToArray();
                    return true;
                }
                default:
                    codePoints = [];
                    return false;
            }
        }

        private static bool IsZeroWidthOnly(Node node)
        {
            return node switch
            {
                EmptyNode => true,
                AnchorNode => true,
                BoundaryNode => true,
                LookaheadNode => true,
                LookbehindNode => true,
                CaptureNode capture => IsZeroWidthOnly(capture.Child),
                ScopedModifiersNode scoped => IsZeroWidthOnly(scoped.Child),
                SequenceNode sequence => sequence.Terms.All(IsZeroWidthOnly),
                QuantifierNode quantifier when quantifier.Min == 0 => true,
                QuantifierNode quantifier when quantifier.Min == quantifier.Max => IsZeroWidthOnly(quantifier.Child),
                _ => false
            };
        }

        private static bool TryBuildLiteralText(int[] codePoints, out string text)
        {
            if (codePoints.Length == 0)
            {
                text = string.Empty;
                return true;
            }

            var builder = new StringBuilder(codePoints.Length);
            for (var i = 0; i < codePoints.Length; i++)
            {
                var codePoint = codePoints[i];
                if (!Rune.TryCreate(codePoint, out var rune))
                {
                    text = string.Empty;
                    return false;
                }

                builder.Append(rune.ToString());
            }

            text = builder.ToString();
            return true;
        }

        private static bool TryComputeMinMatchLength(Node node, out int minLength)
        {
            switch (node)
            {
                case EmptyNode:
                case AnchorNode:
                case BoundaryNode:
                    minLength = 0;
                    return true;
                case LiteralNode:
                case DotNode:
                case ClassNode:
                    minLength = 1;
                    return true;
                case CaptureNode capture:
                    return TryComputeMinMatchLength(capture.Child, out minLength);
                case LookaheadNode:
                case LookbehindNode:
                    minLength = 0;
                    return true;
                case ScopedModifiersNode scoped:
                    return TryComputeMinMatchLength(scoped.Child, out minLength);
                case PropertyEscapeNode:
                    minLength = 1;
                    return true;
                case BackReferenceNode:
                    minLength = default;
                    return false;
                case SequenceNode sequence:
                {
                    long sum = 0;
                    foreach (var term in sequence.Terms)
                    {
                        if (!TryComputeMinMatchLength(term, out var termLength))
                        {
                            minLength = default;
                            return false;
                        }

                        sum += termLength;
                        if (sum > int.MaxValue)
                        {
                            minLength = int.MaxValue;
                            return true;
                        }
                    }

                    minLength = (int)sum;
                    return true;
                }
                case AlternationNode alternation:
                {
                    if (alternation.Alternatives.Length == 0)
                    {
                        minLength = 0;
                        return true;
                    }

                    var best = int.MaxValue;
                    foreach (var alternative in alternation.Alternatives)
                    {
                        if (!TryComputeMinMatchLength(alternative, out var alternativeLength))
                        {
                            minLength = default;
                            return false;
                        }

                        if (alternativeLength < best)
                            best = alternativeLength;
                    }

                    minLength = best;
                    return true;
                }
                case QuantifierNode quantifier:
                {
                    if (!TryComputeMinMatchLength(quantifier.Child, out var childLength))
                    {
                        minLength = default;
                        return false;
                    }

                    var total = (long)childLength * quantifier.Min;
                    minLength = total > int.MaxValue ? int.MaxValue : (int)total;
                    return true;
                }
                default:
                    minLength = default;
                    return false;
            }
        }
    }
}
