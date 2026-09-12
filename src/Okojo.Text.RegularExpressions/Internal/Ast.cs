namespace Okojo.Text.RegularExpressions.Internal;

[Flags]
internal enum NodeOptions : byte
{
    None = 0,
    IgnoreCase = 1 << 0,
    Multiline = 1 << 1,
    DotAll = 1 << 2,
    Unicode = 1 << 3,
    UnicodeSets = 1 << 4,
    InvertClass = 1 << 5,
}

internal enum RegexNodeKind : byte
{
    Empty,
    Literal,
    Dot,
    CharacterClass,
    AnchorStart,
    AnchorEnd,
    WordBoundary,
    Backreference,
    Sequence,
    Alternation,
    Capture,
    Lookaround,
    Quantifier,
}

/// <summary>Compact immutable AST node with analysis facts computed at construction time.</summary>
internal sealed class RegexNode
{
    private static readonly RegexNode[] s_noChildren = [];
    private static readonly RegexNode s_empty = new(
        RegexNodeKind.Empty,
        s_noChildren,
        0,
        NodeOptions.None,
        null,
        groups: null,
        negative: false,
        behind: false,
        greedy: true,
        minimum: 0,
        maximum: 0,
        nullable: true,
        minimumLength: 0,
        firstCapture: -1,
        lastCapture: -1,
        isLinearEligible: true
    );

    private RegexNode(
        RegexNodeKind kind,
        RegexNode[] children,
        int value,
        NodeOptions options,
        CharSet? set,
        int[]? groups,
        bool negative,
        bool behind,
        bool greedy,
        int minimum,
        int maximum,
        bool nullable,
        int minimumLength,
        int firstCapture,
        int lastCapture,
        bool isLinearEligible,
        string[]? strings = null
    )
    {
        Kind = kind;
        Children = children;
        Value = value;
        Options = options;
        Set = set;
        Groups = groups;
        Negative = negative;
        Behind = behind;
        Greedy = greedy;
        Minimum = minimum;
        Maximum = maximum;
        Nullable = nullable;
        MinimumLength = minimumLength;
        FirstCapture = firstCapture;
        LastCapture = lastCapture;
        IsLinearEligible = isLinearEligible;
        Strings = strings;
    }

    internal RegexNodeKind Kind { get; }
    internal RegexNode[] Children { get; }
    internal int Value { get; }
    internal NodeOptions Options { get; }
    internal CharSet? Set { get; }

    /// <summary>
    /// Capture indices sharing a name, for named backreferences to duplicate
    /// named groups. Null when <see cref="Value"/> addresses a single group.
    /// </summary>
    internal int[]? Groups { get; }

    /// <summary>
    /// Multi-code-point members of a /v character class (from \q disjunctions or
    /// properties of strings), when <see cref="Kind"/> is <see cref="RegexNodeKind.CharacterClass"/>.
    /// </summary>
    internal string[]? Strings { get; }
    internal bool Negative { get; }
    internal bool Behind { get; }
    internal bool Greedy { get; }
    internal int Minimum { get; }

    /// <summary>-1 means unbounded.</summary>
    internal int Maximum { get; }
    internal bool Nullable { get; }

    /// <summary>Conservative minimum number of UTF-16 code units consumed.</summary>
    internal int MinimumLength { get; }
    internal int FirstCapture { get; }
    internal int LastCapture { get; }
    internal bool IsLinearEligible { get; }

    internal static RegexNode Literal(int value, NodeOptions options)
    {
        int length = (options & NodeOptions.Unicode) != 0 && value > 0xFFFF ? 2 : 1;
        return Leaf(
            RegexNodeKind.Literal,
            value,
            options,
            null,
            nullable: false,
            length,
            linear: true
        );
    }

    internal static RegexNode Dot(NodeOptions options) =>
        Leaf(RegexNodeKind.Dot, 0, options, null, nullable: false, minimumLength: 1, linear: true);

    internal static RegexNode CharacterClass(
        CharSet set,
        NodeOptions options,
        bool invert = false
    ) => CharacterClassCore(set, null, options, invert);

    internal static RegexNode CharacterClass(
        UnicodeSet set,
        NodeOptions options,
        bool invert = false
    ) =>
        CharacterClassCore(
            set.CodePoints,
            set.Strings.Length == 0 ? null : set.Strings,
            options,
            invert
        );

    private static RegexNode CharacterClassCore(
        CharSet set,
        string[]? strings,
        NodeOptions options,
        bool invert
    ) =>
        new(
            kind: RegexNodeKind.CharacterClass,
            children: s_noChildren,
            value: 0,
            options: options,
            set: set,
            groups: null,
            negative: invert,
            behind: false,
            greedy: true,
            minimum: 0,
            maximum: 0,
            nullable: false,
            minimumLength: 1,
            firstCapture: -1,
            lastCapture: -1,
            isLinearEligible: strings is null,
            strings: strings
        );

    internal static RegexNode Anchor(bool start, NodeOptions options) =>
        Leaf(
            start ? RegexNodeKind.AnchorStart : RegexNodeKind.AnchorEnd,
            0,
            options,
            null,
            nullable: true,
            minimumLength: 0,
            linear: true
        );

    internal static RegexNode WordBoundary(bool negative, NodeOptions options) =>
        new(
            kind: RegexNodeKind.WordBoundary,
            children: s_noChildren,
            value: 0,
            options: options,
            set: null,
            groups: null,
            negative: negative,
            behind: false,
            greedy: true,
            minimum: 0,
            maximum: 0,
            nullable: true,
            minimumLength: 0,
            firstCapture: -1,
            lastCapture: -1,
            isLinearEligible: true
        );

    internal static RegexNode Backreference(int capture, NodeOptions options) =>
        Leaf(
            RegexNodeKind.Backreference,
            capture,
            options,
            null,
            nullable: true,
            minimumLength: 0,
            linear: false
        );

    /// <summary>
    /// Backreference to a name shared by multiple capturing groups. The matcher
    /// uses the most recently matched group among <paramref name="groups"/>.
    /// </summary>
    internal static RegexNode Backreference(int[] groups, NodeOptions options)
    {
        RegexNode node = Leaf(
            RegexNodeKind.Backreference,
            -1,
            options,
            null,
            nullable: true,
            minimumLength: 0,
            linear: false
        );
        return new RegexNode(
            kind: node.Kind,
            children: node.Children,
            value: -1,
            options: node.Options,
            set: null,
            groups: groups,
            negative: false,
            behind: false,
            greedy: true,
            minimum: 0,
            maximum: 0,
            nullable: true,
            minimumLength: 0,
            firstCapture: -1,
            lastCapture: -1,
            isLinearEligible: false
        );
    }

    internal static RegexNode Capture(int capture, RegexNode child)
    {
        int first = child.FirstCapture < 0 ? capture : Math.Min(capture, child.FirstCapture);
        int last = Math.Max(capture, child.LastCapture);
        return new RegexNode(
            kind: RegexNodeKind.Capture,
            children: [child],
            value: capture,
            options: NodeOptions.None,
            set: null,
            groups: null,
            negative: false,
            behind: false,
            greedy: true,
            minimum: 0,
            maximum: 0,
            nullable: child.Nullable,
            minimumLength: child.MinimumLength,
            firstCapture: first,
            lastCapture: last,
            isLinearEligible: child.IsLinearEligible
        );
    }

    internal static RegexNode Lookaround(RegexNode child, bool negative, bool behind) =>
        new(
            kind: RegexNodeKind.Lookaround,
            children: [child],
            value: 0,
            options: NodeOptions.None,
            set: null,
            groups: null,
            negative: negative,
            behind: behind,
            greedy: true,
            minimum: 0,
            maximum: 0,
            nullable: true,
            minimumLength: 0,
            firstCapture: child.FirstCapture,
            lastCapture: child.LastCapture,
            isLinearEligible: false
        );

    internal static RegexNode Quantifier(RegexNode child, int minimum, int maximum, bool greedy)
    {
        int minLength = SaturatingMultiply(child.MinimumLength, minimum);
        bool nullable = minimum == 0 || child.Nullable;
        return new RegexNode(
            kind: RegexNodeKind.Quantifier,
            children: [child],
            value: 0,
            options: NodeOptions.None,
            set: null,
            groups: null,
            negative: false,
            behind: false,
            greedy: greedy,
            minimum: minimum,
            maximum: maximum,
            nullable: nullable,
            minLength,
            child.FirstCapture,
            child.LastCapture,
            child.IsLinearEligible
        );
    }

    internal static RegexNode Sequence(List<RegexNode> terms)
    {
        if (terms.Count == 0)
            return s_empty;

        List<RegexNode> flattened = new(terms.Count);
        foreach (RegexNode term in terms)
        {
            if (term.Kind == RegexNodeKind.Empty)
                continue;
            if (term.Kind == RegexNodeKind.Sequence)
                flattened.AddRange(term.Children);
            else
                flattened.Add(term);
        }

        if (flattened.Count == 0)
            return s_empty;
        if (flattened.Count == 1)
            return flattened[0];
        RegexNode[] children = flattened.ToArray();

        bool nullable = true;
        bool linear = true;
        int length = 0;
        int first = -1;
        int last = -1;
        foreach (RegexNode child in children)
        {
            nullable &= child.Nullable;
            linear &= child.IsLinearEligible;
            length = SaturatingAdd(length, child.MinimumLength);
            MergeCaptureRange(child, ref first, ref last);
        }
        return new RegexNode(
            kind: RegexNodeKind.Sequence,
            children: children,
            value: 0,
            options: NodeOptions.None,
            set: null,
            groups: null,
            negative: false,
            behind: false,
            greedy: true,
            minimum: 0,
            maximum: 0,
            nullable: nullable,
            minimumLength: length,
            firstCapture: first,
            lastCapture: last,
            isLinearEligible: linear
        );
    }

    internal static RegexNode Alternation(List<RegexNode> alternatives)
    {
        if (alternatives.Count == 0)
            return s_empty;
        if (alternatives.Count == 1)
            return alternatives[0];

        RegexNode[] children = alternatives.ToArray();
        bool nullable = false;
        bool linear = true;
        int length = int.MaxValue;
        int first = -1;
        int last = -1;
        foreach (RegexNode child in children)
        {
            nullable |= child.Nullable;
            linear &= child.IsLinearEligible;
            length = Math.Min(length, child.MinimumLength);
            MergeCaptureRange(child, ref first, ref last);
        }
        return new RegexNode(
            kind: RegexNodeKind.Alternation,
            children: children,
            value: 0,
            options: NodeOptions.None,
            set: null,
            groups: null,
            negative: false,
            behind: false,
            greedy: true,
            minimum: 0,
            maximum: 0,
            nullable: nullable,
            minimumLength: length == int.MaxValue ? 0 : length,
            firstCapture: first,
            lastCapture: last,
            isLinearEligible: linear
        );
    }

    private static RegexNode Leaf(
        RegexNodeKind kind,
        int value,
        NodeOptions options,
        CharSet? set,
        bool nullable,
        int minimumLength,
        bool linear
    ) =>
        new(
            kind: kind,
            children: s_noChildren,
            value: value,
            options: options,
            set: set,
            groups: null,
            negative: false,
            behind: false,
            greedy: true,
            minimum: 0,
            maximum: 0,
            nullable: nullable,
            minimumLength: minimumLength,
            firstCapture: -1,
            lastCapture: -1,
            isLinearEligible: linear
        );

    private static void MergeCaptureRange(RegexNode child, ref int first, ref int last)
    {
        if (child.FirstCapture < 0)
            return;
        first = first < 0 ? child.FirstCapture : Math.Min(first, child.FirstCapture);
        last = Math.Max(last, child.LastCapture);
    }

    private static int SaturatingAdd(int left, int right) =>
        left > int.MaxValue - right ? int.MaxValue : left + right;

    private static int SaturatingMultiply(int value, int count)
    {
        if (value == 0 || count == 0)
            return 0;
        return value > int.MaxValue / count ? int.MaxValue : value * count;
    }
}
