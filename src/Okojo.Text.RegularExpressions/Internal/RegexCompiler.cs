using System.Text;

using Okojo.Text.Unicode;
namespace Okojo.Text.RegularExpressions.Internal;

internal static class RegexCompiler
{
    internal static RegexProgram Compile(       RegexNode root,
        int captureCount,
        EcmaRegexFlagSet flags,
        EcmaRegexOptions options
    )
    {
        var context = new CompilerContext(captureCount, flags, options);
        SegmentBuilder main = context.CreateSegment(
            direction: 1,
            firstCapture: 0,
            lastCapture: captureCount
        );
        main.Emit(OpCode.Save, 0);
        main.CompileNode(root);
        main.Emit(OpCode.Save, 1);
        main.Emit(OpCode.Match);
        return context.Finish(SearchAnalyzer.Analyze(root));
    }

    private sealed class CompilerContext
    {
        private readonly int _captureCount;
        private readonly EcmaRegexFlagSet _flags;
        private readonly EcmaRegexOptions _options;
        private readonly List<SegmentBuilder> _segments = [];
        private readonly List<CharSet> _classes = [];
        private readonly Dictionary<CharSet, int> _classIndices = new(
            ReferenceEqualityComparer.Instance
        );
        private readonly List<RepeatInfo> _repeats = [];
        private readonly List<ScanInfo> _scans = [];
        private readonly List<int[]> _nameGroupSets = [];
        private readonly List<ClassSetInfo> _classSets = [];
        private int _stateSlots;
        private int _instructionCount;

        internal CompilerContext(int captureCount, EcmaRegexFlagSet flags, EcmaRegexOptions options)
        {
            _captureCount = captureCount;
            _flags = flags;
            _options = options;
        }

        internal EcmaRegexOptions Options => _options;

        internal SegmentBuilder CreateSegment(int direction, int firstCapture, int lastCapture)
        {
            int id = _segments.Count;
            var segment = new SegmentBuilder(this, id, direction, firstCapture, lastCapture);
            _segments.Add(segment);
            return segment;
        }

        internal void CountInstruction()
        {
            if (++_instructionCount > _options.MaxProgramSize)
            {
                throw new EcmaRegexParseException(
                    "Compiled regular-expression program exceeds MaxProgramSize.",
                    -1,
                    EcmaRegexError.PatternTooLarge
                );
            }
        }

        internal int GetClass(CharSet set)
        {
            if (_classIndices.TryGetValue(set, out int index))
                return index;
            index = _classes.Count;
            _classes.Add(set);
            _classIndices.Add(set, index);
            return index;
        }

        internal int ReserveScan(ScanInfo scan)
        {
            int id = _scans.Count;
            _scans.Add(scan);
            return id;
        }

        internal int ReserveNameGroupSet(int[] groups)
        {
            int id = _nameGroupSets.Count;
            _nameGroupSets.Add(groups);
            return id;
        }

        internal int ReserveClassSet(ClassSetInfo info)
        {
            int id = _classSets.Count;
            _classSets.Add(info);
            return id;
        }

        internal int ReserveRepeat(int segment, RegexNode node)
        {
            int stateSlot = _stateSlots;
            _stateSlots = checked(_stateSlots + 2);
            int id = _repeats.Count;
            _repeats.Add(
                new RepeatInfo(
                    segment,
                    stateSlot,
                    node.Minimum,
                    node.Maximum,
                    node.Greedy,
                    DecisionPc: -1,
                    BodyPc: -1,
                    ExitPc: -1,
                    node.Children[0].FirstCapture,
                    node.Children[0].LastCapture
                )
            );
            return id;
        }

        internal void CompleteRepeat(int id, int decisionPc, int bodyPc, int exitPc)
        {
            RepeatInfo old = _repeats[id];
            _repeats[id] = old with { DecisionPc = decisionPc, BodyPc = bodyPc, ExitPc = exitPc };
        }

        internal RegexProgram Finish(SearchPlan searchPlan)
        {
            ProgramSegment[] segments = new ProgramSegment[_segments.Count];
            for (int i = 0; i < segments.Length; i++)
                segments[i] = _segments[i].Finish();
            return new RegexProgram
            {
                Segments = segments,
                Classes = _classes.ToArray(),
                Repeats = _repeats.ToArray(),
                Scans = _scans.ToArray(),
                NameGroupSets = _nameGroupSets.ToArray(),
                ClassSets = _classSets.ToArray(),
                Flags = _flags,
                CaptureCount = _captureCount,
                StateSlotCount = _stateSlots,
                SearchPlan = searchPlan,
            };
        }
    }

    private sealed class SegmentBuilder
    {
        private readonly CompilerContext _context;
        private readonly int _id;
        private readonly int _direction;
        private readonly int _firstCapture;
        private readonly int _lastCapture;
        private readonly List<Instruction> _code = [];

        internal SegmentBuilder(
            CompilerContext context,
            int id,
            int direction,
            int firstCapture,
            int lastCapture
        )
        {
            _context = context;
            _id = id;
            _direction = direction;
            _firstCapture = firstCapture;
            _lastCapture = lastCapture;
        }

        private int Position => _code.Count;

        internal int Emit(OpCode op, int a = 0, int b = 0)
        {
            _context.CountInstruction();
            int pc = _code.Count;
            _code.Add(new Instruction(op, a, b));
            return pc;
        }

        internal void CompileNode(RegexNode node)
        {
            switch (node.Kind)
            {
                case RegexNodeKind.Empty:
                    return;
                case RegexNodeKind.Literal:
                    Emit(OpCode.Character, node.Value, (int)node.Options);
                    return;
                case RegexNodeKind.Dot:
                    Emit(OpCode.Any, 0, (int)node.Options);
                    return;
                case RegexNodeKind.CharacterClass:
                {
                    NodeOptions nodeOptions =
                        node.Options | (node.Negative ? NodeOptions.InvertClass : NodeOptions.None);
                    if (node.Strings is null)
                    {
                        Emit(OpCode.CharacterClass, _context.GetClass(node.Set!), (int)nodeOptions);
                    }
                    else
                    {
                        int id = _context.ReserveClassSet(
                            new ClassSetInfo
                            {
                                CodePoints = node.Set!,
                                Strings = node.Strings,
                                Trie = StringTrieBuilder.Build(node.Strings),
                            }
                        );
                        Emit(OpCode.ClassSet, id, (int)nodeOptions);
                    }
                    return;
                }
                case RegexNodeKind.AnchorStart:
                    Emit(OpCode.AssertStart, 0, (int)node.Options);
                    return;
                case RegexNodeKind.AnchorEnd:
                    Emit(OpCode.AssertEnd, 0, (int)node.Options);
                    return;
                case RegexNodeKind.WordBoundary:
                    Emit(OpCode.WordBoundary, node.Negative ? 1 : 0, (int)node.Options);
                    return;
                case RegexNodeKind.Backreference:
                    if (node.Groups is null)
                    {
                        Emit(OpCode.Backreference, node.Value, (int)node.Options);
                    }
                    else
                    {
                        Emit(
                            OpCode.BackreferenceSet,
                            _context.ReserveNameGroupSet(node.Groups),
                            (int)node.Options
                        );
                    }
                    return;
                case RegexNodeKind.Sequence:
                    CompileSequence(node.Children);
                    return;
                case RegexNodeKind.Alternation:
                    CompileAlternation(node.Children);
                    return;
                case RegexNodeKind.Capture:
                    CompileCapture(node);
                    return;
                case RegexNodeKind.Lookaround:
                    CompileLookaround(node);
                    return;
                case RegexNodeKind.Quantifier:
                    CompileQuantifier(node);
                    return;
                default:
                    throw new InvalidOperationException($"Unknown AST node kind {node.Kind}.");
            }
        }

        private void CompileSequence(RegexNode[] children)
        {
            if (_direction > 0)
            {
                foreach (RegexNode child in children)
                    CompileNode(child);
            }
            else
            {
                for (int i = children.Length - 1; i >= 0; i--)
                    CompileNode(children[i]);
            }
        }

        private void CompileAlternation(RegexNode[] alternatives)
        {
            if (alternatives.Length == 0)
                return;
            if (alternatives.Length == 1)
            {
                CompileNode(alternatives[0]);
                return;
            }

            var endJumps = new List<int>(alternatives.Length - 1);
            for (int i = 0; i < alternatives.Length - 1; i++)
            {
                int split = Emit(OpCode.Split);
                int preferred = Position;
                CompileNode(alternatives[i]);
                endJumps.Add(Emit(OpCode.Jump));
                int fallback = Position;
                PatchTargets(split, preferred, fallback);
            }
            CompileNode(alternatives[^1]);
            int end = Position;
            foreach (int jump in endJumps)
                PatchA(jump, end);
        }

        private void CompileCapture(RegexNode node)
        {
            int startSlot = checked(node.Value * 2);
            int endSlot = startSlot + 1;
            if (_direction > 0)
            {
                Emit(OpCode.Save, startSlot);
                CompileNode(node.Children[0]);
                Emit(OpCode.Save, endSlot);
            }
            else
            {
                Emit(OpCode.Save, endSlot);
                CompileNode(node.Children[0]);
                Emit(OpCode.Save, startSlot);
            }
        }

        private void CompileLookaround(RegexNode node)
        {
            RegexNode child = node.Children[0];
            int direction = node.Behind ? -1 : 1;
            SegmentBuilder assertion = _context.CreateSegment(
                direction,
                child.FirstCapture,
                child.LastCapture
            );
            assertion.CompileNode(child);
            assertion.Emit(OpCode.Match);
            Emit(OpCode.Assertion, assertion._id, node.Negative ? 0 : 1);
        }

        private void CompileQuantifier(RegexNode node)
        {
            if (node.Maximum == 0)
                return;
            if (TryCompileSimpleScan(node))
                return;

            if (
                node.Minimum > _context.Options.MaxRepeatCount
                || (node.Maximum >= 0 && node.Maximum > _context.Options.MaxRepeatCount)
            )
            {
                throw new EcmaRegexParseException(
                    "Quantifier exceeds MaxRepeatCount.",
                    -1,
                    EcmaRegexError.PatternTooLarge
                );
            }

            int repeat = _context.ReserveRepeat(_id, node);
            Emit(OpCode.RepeatInit, repeat);
            int decision = Emit(OpCode.RepeatDecision, repeat);
            int body = Position;
            Emit(OpCode.RepeatBody, repeat);
            CompileNode(node.Children[0]);
            Emit(OpCode.RepeatNext, repeat);
            int exit = Position;
            _context.CompleteRepeat(repeat, decision, body, exit);
        }

        private bool TryCompileSimpleScan(RegexNode quantifier)
        {
            RegexNode child = quantifier.Children[0];
            ScanAtomKind kind;
            int value;
            NodeOptions options;

            switch (child.Kind)
            {
                case RegexNodeKind.Literal:
                    kind = ScanAtomKind.Character;
                    value = child.Value;
                    options = child.Options;
                    break;
                case RegexNodeKind.CharacterClass:
                    if (child.Strings is not null)
                        return false;
                    kind = ScanAtomKind.CharacterClass;
                    value = _context.GetClass(child.Set!);
                    options =
                        child.Options
                        | (child.Negative ? NodeOptions.InvertClass : NodeOptions.None);
                    break;
                case RegexNodeKind.Dot:
                    kind = ScanAtomKind.Any;
                    value = 0;
                    options = child.Options;
                    break;
                default:
                    return false;
            }

            int id = _context.ReserveScan(
                new ScanInfo(
                    kind,
                    value,
                    options,
                    quantifier.Minimum,
                    quantifier.Maximum,
                    quantifier.Greedy
                )
            );
            Emit(OpCode.Scan, id);
            return true;
        }

        private void PatchA(int pc, int a) => _code[pc] = _code[pc].WithA(a);

        private void PatchTargets(int pc, int a, int b) => _code[pc] = _code[pc].WithTargets(a, b);

        internal ProgramSegment Finish() =>
            new()
            {
                Code = _code.ToArray(),
                Direction = _direction,
                FirstCapture = _firstCapture,
                LastCapture = _lastCapture,
            };
    }
}

internal static class SearchAnalyzer
{
    private const int MaxPrefixCodeUnits = 64;
    private const int MaxQuantifierPrefixIterations = 64;

    internal static SearchPlan Analyze(RegexNode root) =>
        new()
        {
            Anchor = FindAnchor(root),
            Prefix = TryExtractPrefix(root),
            LeadingSet = TryExtractLeadingSet(root),
            MinimumLength = root.MinimumLength,
        };

    /// <summary>
    /// Returns the code points that can begin a match, as a union over all leading
    /// consuming atoms up to the first atom that must consume. Null when the set
    /// cannot be proven (case-insensitive atoms, unknown structures, or an empty
    /// match-only pattern).
    /// </summary>
    private static CharSet? TryExtractLeadingSet(RegexNode root)
    {
        // A nullable pattern can match empty anywhere, so its leading set cannot
        // prune candidate start positions.
        if (root.Nullable)
            return null;
        CharSetBuilder builder = new();
        if (!TryAppendLeadingSet(root, builder))
            return null;
        CharSet set = builder.Build();
        return set.Ranges.Length == 0 ? null : set;
    }

    private static bool TryAppendLeadingSet(RegexNode node, CharSetBuilder builder)
    {
        switch (node.Kind)
        {
            case RegexNodeKind.Literal:
                if ((node.Options & NodeOptions.IgnoreCase) != 0)
                    return false;
                builder.Add(node.Value);
                return true;
            case RegexNodeKind.Dot:
                if ((node.Options & NodeOptions.IgnoreCase) != 0)
                    return false;
                builder.AddSet(
                    (node.Options & NodeOptions.DotAll) != 0
                        ? CharSet.AllUnicode
                        : CharSet.Subtract(CharSet.AllUnicode, s_lineTerminators)
                );
                return true;
            case RegexNodeKind.CharacterClass:
                if ((node.Options & NodeOptions.IgnoreCase) != 0)
                    return false;
                CharSet set = node.Set!;
                builder.AddSet(node.Negative ? CharSet.Subtract(CharSet.AllUnicode, set) : set);
                return true;
            case RegexNodeKind.Capture:
                return TryAppendLeadingSet(node.Children[0], builder);
            case RegexNodeKind.Sequence:
                foreach (RegexNode child in node.Children)
                {
                    if (!TryAppendLeadingSet(child, builder))
                        return false;
                    if (!child.Nullable)
                        return true;
                }
                return true;
            case RegexNodeKind.Alternation:
                foreach (RegexNode alternative in node.Children)
                {
                    if (!TryAppendLeadingSet(alternative, builder))
                        return false;
                }
                return true;
            case RegexNodeKind.Quantifier:
                return TryAppendLeadingSet(node.Children[0], builder);
            default:
                // Anchors, boundaries, lookarounds, empty: nullable, no leading atom.
                return true;
        }
    }

    private static readonly CharSet s_lineTerminators = new(
        [new CodePointRange('\n', '\n'), new CodePointRange('\r', '\r'), new CodePointRange(0x2028, 0x2028), new CodePointRange(0x2029, 0x2029)]
    );

    private static SearchAnchor FindAnchor(RegexNode node)
    {
        switch (node.Kind)
        {
            case RegexNodeKind.AnchorStart:
                return (node.Options & NodeOptions.Multiline) != 0
                    ? SearchAnchor.LineStart
                    : SearchAnchor.AbsoluteStart;
            case RegexNodeKind.Capture:
                return FindAnchor(node.Children[0]);
            case RegexNodeKind.Sequence:
                foreach (RegexNode child in node.Children)
                {
                    SearchAnchor anchor = FindAnchor(child);
                    if (anchor != SearchAnchor.None)
                        return anchor;
                    if (!child.Nullable)
                        break;
                }
                break;
        }
        return SearchAnchor.None;
    }

    private static string? TryExtractPrefix(RegexNode root)
    {
        StringBuilder builder = new(MaxPrefixCodeUnits);
        _ = AppendPrefix(root, builder);
        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>Returns true only when the complete node was proven to be part of the prefix.</summary>
    private static bool AppendPrefix(RegexNode node, StringBuilder builder)
    {
        if (builder.Length >= MaxPrefixCodeUnits)
            return false;
        switch (node.Kind)
        {
            case RegexNodeKind.Empty:
            case RegexNodeKind.AnchorStart:
            case RegexNodeKind.AnchorEnd:
            case RegexNodeKind.WordBoundary:
            case RegexNodeKind.Lookaround:
                return true;

            case RegexNodeKind.Literal:
            {
                if ((node.Options & NodeOptions.IgnoreCase) != 0)
                    return false;
                int width = Utf16.CodeUnitLength(node.Value);
                if (builder.Length + width > MaxPrefixCodeUnits)
                    return false;
                Utf16.AppendCodePoint(builder, node.Value);
                return true;
            }

            case RegexNodeKind.Capture:
                return AppendPrefix(node.Children[0], builder);

            case RegexNodeKind.Sequence:
                foreach (RegexNode child in node.Children)
                {
                    if (!AppendPrefix(child, builder))
                        return false;
                }
                return true;

            case RegexNodeKind.Quantifier:
            {
                int iterations = Math.Min(node.Minimum, MaxQuantifierPrefixIterations);
                for (int i = 0; i < iterations; i++)
                {
                    if (!AppendPrefix(node.Children[0], builder))
                        return false;
                }
                if (iterations != node.Minimum)
                    return false;
                return node.Maximum == node.Minimum;
            }

            default:
                return false;
        }
    }
}

internal static class StringTrieBuilder
{
    /// <summary>Builds a code-point trie over the given strings (each of at least two code points).</summary>
    internal static StringTrie Build(string[] strings)
    {
        List<List<(int CodePoint, int Next)>> children = [[]];
        List<int> terminals = [-1];

        foreach (string value in strings)
        {
            int node = 0;
            int position = 0;
            while (position < value.Length)
            {
                int codePoint = Utf16.ReadCodePoint(value, position);
                int width = Utf16.CodeUnitLength(codePoint);
                List<(int, int)> edges = children[node];
                int next = -1;
                foreach ((int existing, int target) in edges)
                {
                    if (existing == codePoint)
                    {
                        next = target;
                        break;
                    }
                }
                if (next < 0)
                {
                    next = children.Count;
                    children.Add([]);
                    terminals.Add(-1);
                    edges.Add((codePoint, next));
                }
                node = next;
                position += width;
            }
            terminals[node] = position;
        }

        int[] childOffsets = new int[children.Count];
        int[] edgeCodePoints = new int[children.Count - 1];
        int[] edgeNexts = new int[children.Count - 1];
        int cursor = 0;
        for (int i = 0; i < children.Count; i++)
        {
            childOffsets[i] = cursor;
            List<(int, int)> edges = children[i];
            edges.Sort(static (a, b) => a.Item1.CompareTo(b.Item1));
            foreach ((int codePoint, int target) in edges)
            {
                edgeCodePoints[cursor] = codePoint;
                edgeNexts[cursor] = target;
                cursor++;
            }
        }

        return new StringTrie
        {
            ChildOffsets = childOffsets,
            EdgeCodePoints = edgeCodePoints,
            EdgeNexts = edgeNexts,
            Terminals = terminals.ToArray(),
            NodeCount = children.Count,
        };
    }
}
