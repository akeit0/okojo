using Okojo.Internals;
using Okojo.Parsing;
using System.Text;

namespace Okojo.RegExp;

internal sealed partial class ScratchRegExpProgram
{
    private sealed partial class Parser
    {
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
            Span<int> initialBuffer = stackalloc int[MaxRequiredLiteralPrefixLength];
            var prefix = new PooledArrayBuilder<int>(initialBuffer);
            try
            {
                AppendRequiredLiteralPrefix(node, ref prefix);
                return prefix.ToArray();
            }
            finally
            {
                prefix.Dispose();
            }
        }

        private static bool TryBuildSearchPlan(Node node, RegExpRuntimeFlags flags, out SearchPlan searchPlan)
        {
            if (!TryAnalyzeSearch(node, flags, out var analysis) ||
                analysis.AnchorKind == SearchAnchorKind.None && analysis.AtomKind == SearchAtomKind.None)
            {
                searchPlan = SearchPlan.None;
                return false;
            }

            searchPlan = new(analysis.AnchorKind, analysis.AtomKind, analysis.LiteralCodePoints, analysis.Class,
                analysis.PropertyEscape);
            return true;
        }

        private static bool TryAnalyzeSearch(Node node, RegExpRuntimeFlags flags, out SearchAnalysis analysis)
        {
            switch (node)
            {
                case EmptyNode:
                case BoundaryNode:
                case LookaheadNode:
                case LookbehindNode:
                    analysis = SearchAnalysis.Empty;
                    return true;
                case AnchorNode anchor when anchor.Start:
                    analysis = SearchAnalysis.Empty with
                    {
                        AnchorKind = flags.Multiline ? SearchAnchorKind.LineStart : SearchAnchorKind.Start
                    };
                    return true;
                case AnchorNode:
                    analysis = SearchAnalysis.Empty;
                    return true;
                case LiteralNode literal:
                    analysis = new(false, SearchAnchorKind.None, SearchAtomKind.LiteralSet, [literal.CodePoint], null,
                        null);
                    return true;
                case DotNode:
                    analysis = new(false, SearchAnchorKind.None,
                        flags.DotAll ? SearchAtomKind.Any : SearchAtomKind.Dot, [], null, null);
                    return true;
                case ClassNode cls:
                    analysis = TryGetSmallLiteralClassCodePoints(cls, out var classLiteralSet)
                        ? new(false, SearchAnchorKind.None, SearchAtomKind.LiteralSet, classLiteralSet, null, null)
                        : new(false, SearchAnchorKind.None, SearchAtomKind.Class, [], cls, null);
                    return true;
                case PropertyEscapeNode propertyEscape:
                    analysis = new(false, SearchAnchorKind.None, SearchAtomKind.PropertyEscape, [], null, propertyEscape);
                    return true;
                case CaptureNode capture:
                    return TryAnalyzeSearch(capture.Child, flags, out analysis);
                case ScopedModifiersNode scoped:
                    return TryAnalyzeSearch(scoped.Child, flags with
                    {
                        IgnoreCase = scoped.IgnoreCase ?? flags.IgnoreCase,
                        Multiline = scoped.Multiline ?? flags.Multiline,
                        DotAll = scoped.DotAll ?? flags.DotAll
                    }, out analysis);
                case QuantifierNode quantifier:
                    if (!TryAnalyzeSearch(quantifier.Child, flags, out var quantified))
                    {
                        analysis = default;
                        return false;
                    }

                    analysis = quantifier.Min == 0
                        ? quantified with { CanBeEmpty = true, AnchorKind = SearchAnchorKind.None }
                        : quantified;
                    return true;
                case SequenceNode sequence:
                    return TryAnalyzeSequence(sequence.Terms, flags, out analysis);
                case AlternationNode alternation:
                    return TryAnalyzeAlternation(alternation.Alternatives, flags, out analysis);
                default:
                    analysis = default;
                    return false;
            }
        }

        private static bool TryAnalyzeSequence(Node[] terms, RegExpRuntimeFlags flags, out SearchAnalysis analysis)
        {
            var current = SearchAnalysis.Empty;
            for (var i = 0; i < terms.Length; i++)
            {
                if (!TryAnalyzeSearch(terms[i], flags, out var termAnalysis))
                {
                    analysis = default;
                    return false;
                }

                if (termAnalysis.AnchorKind != SearchAnchorKind.None)
                {
                    if (current.AtomKind != SearchAtomKind.None ||
                        current.AnchorKind != SearchAnchorKind.None && current.AnchorKind != termAnalysis.AnchorKind)
                    {
                        analysis = default;
                        return false;
                    }

                    current = current with { AnchorKind = termAnalysis.AnchorKind };
                }

                if (termAnalysis.AtomKind != SearchAtomKind.None)
                {
                    if (!TryMergeSearchAtom(current, termAnalysis, out current))
                    {
                        analysis = default;
                        return false;
                    }
                }

                if (!termAnalysis.CanBeEmpty)
                {
                    analysis = current with { CanBeEmpty = false };
                    return true;
                }
            }

            analysis = current with { CanBeEmpty = true };
            return true;
        }

        private static bool TryAnalyzeAlternation(Node[] alternatives, RegExpRuntimeFlags flags, out SearchAnalysis analysis)
        {
            var combined = SearchAnalysis.Empty;
            var initialized = false;
            for (var i = 0; i < alternatives.Length; i++)
            {
                if (!TryAnalyzeSearch(alternatives[i], flags, out var alternativeAnalysis))
                {
                    analysis = default;
                    return false;
                }

                if (!initialized)
                {
                    combined = alternativeAnalysis;
                    initialized = true;
                    continue;
                }

                if (combined.AnchorKind != alternativeAnalysis.AnchorKind)
                {
                    analysis = default;
                    return false;
                }

                if (!TryMergeSearchAtom(combined, alternativeAnalysis, out combined))
                {
                    analysis = default;
                    return false;
                }

                combined = combined with { CanBeEmpty = combined.CanBeEmpty || alternativeAnalysis.CanBeEmpty };
            }

            analysis = initialized ? combined : SearchAnalysis.Empty;
            return true;
        }

        private static bool TryMergeSearchAtom(SearchAnalysis left, SearchAnalysis right, out SearchAnalysis merged)
        {
            if (left.AtomKind == SearchAtomKind.None)
            {
                merged = left with
                {
                    AtomKind = right.AtomKind,
                    LiteralCodePoints = right.LiteralCodePoints,
                    Class = right.Class,
                    PropertyEscape = right.PropertyEscape
                };
                return true;
            }

            if (right.AtomKind == SearchAtomKind.None)
            {
                merged = left;
                return true;
            }

            if (left.AtomKind == SearchAtomKind.Any || right.AtomKind == SearchAtomKind.Any)
            {
                merged = left with
                {
                    AtomKind = SearchAtomKind.Any,
                    LiteralCodePoints = [],
                    Class = null,
                    PropertyEscape = null
                };
                return true;
            }

            if (left.AtomKind == SearchAtomKind.Dot && right.AtomKind == SearchAtomKind.Dot)
            {
                merged = left;
                return true;
            }

            if (left.AtomKind == SearchAtomKind.Dot &&
                right.AtomKind == SearchAtomKind.LiteralSet &&
                !ContainsLineTerminator(right.LiteralCodePoints))
            {
                merged = left;
                return true;
            }

            if (right.AtomKind == SearchAtomKind.Dot &&
                left.AtomKind == SearchAtomKind.LiteralSet &&
                !ContainsLineTerminator(left.LiteralCodePoints))
            {
                merged = right with { AnchorKind = left.AnchorKind, CanBeEmpty = left.CanBeEmpty || right.CanBeEmpty };
                return true;
            }

            if (left.AtomKind == SearchAtomKind.LiteralSet && right.AtomKind == SearchAtomKind.LiteralSet)
            {
                if (!TryUnionLiteralSets(left.LiteralCodePoints, right.LiteralCodePoints, out var union))
                {
                    merged = default;
                    return false;
                }

                merged = left with { LiteralCodePoints = union };
                return true;
            }

            if (left.AtomKind == SearchAtomKind.Class && right.AtomKind == SearchAtomKind.Class &&
                Equals(left.Class, right.Class))
            {
                merged = left;
                return true;
            }

            if (left.AtomKind == SearchAtomKind.PropertyEscape &&
                right.AtomKind == SearchAtomKind.PropertyEscape &&
                Equals(left.PropertyEscape, right.PropertyEscape))
            {
                merged = left;
                return true;
            }

            merged = default;
            return false;
        }

        private static bool TryUnionLiteralSets(int[] left, int[] right, out int[] union)
        {
            using var codePoints = new ScratchPooledIntSet();
            codePoints.UnionWith(left);
            codePoints.UnionWith(right);

            if (codePoints.Count > MaxSearchLiteralSetSize)
            {
                union = [];
                return false;
            }

            union = codePoints.Count == 0 ? [] : new int[codePoints.Count];
            if (union.Length > 0)
            {
                codePoints.CopyTo(union);
                if (union.Length > 1)
                    Array.Sort(union);
            }

            return true;
        }

        private static bool ContainsLineTerminator(int[] codePoints)
        {
            for (var i = 0; i < codePoints.Length; i++)
                if (codePoints[i] is 0x000A or 0x000D or 0x2028 or 0x2029)
                    return true;

            return false;
        }

        private readonly record struct SearchAnalysis(
            bool CanBeEmpty,
            SearchAnchorKind AnchorKind,
            SearchAtomKind AtomKind,
            int[] LiteralCodePoints,
            ClassNode? Class,
            PropertyEscapeNode? PropertyEscape)
        {
            public static SearchAnalysis Empty { get; } = new(true, SearchAnchorKind.None, SearchAtomKind.None, [], null,
                null);
        }

        private static bool AppendRequiredLiteralPrefix(Node node, ref PooledArrayBuilder<int> prefix)
        {
            if (prefix.Length >= MaxRequiredLiteralPrefixLength)
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
                    for (var i = 0; i < sequence.Terms.Length && prefix.Length < MaxRequiredLiteralPrefixLength; i++)
                    {
                        if (!AppendRequiredLiteralPrefix(sequence.Terms[i], ref prefix))
                            return false;
                    }

                    return true;
                case QuantifierNode quantifier:
                {
                    if (quantifier.Min == 0 || !TryGetExactLiteral(quantifier.Child, out var literalCodePoints))
                        return false;

                    for (var repeat = 0; repeat < quantifier.Min && prefix.Length < MaxRequiredLiteralPrefixLength; repeat++)
                    for (var i = 0; i < literalCodePoints.Length && prefix.Length < MaxRequiredLiteralPrefixLength; i++)
                        prefix.Add(literalCodePoints[i]);

                    return quantifier.Max == quantifier.Min;
                }
                default:
                    if (!TryGetExactLiteral(node, out var exactLiteral))
                        return false;

                    for (var i = 0; i < exactLiteral.Length && prefix.Length < MaxRequiredLiteralPrefixLength; i++)
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
                    Span<int> initialBuffer = stackalloc int[Math.Min(Math.Max(sequence.Terms.Length, 1), 32)];
                    using var builder = new PooledArrayBuilder<int>(initialBuffer);
                    for (var i = 0; i < sequence.Terms.Length; i++)
                    {
                        if (!TryGetExactLiteral(sequence.Terms[i], out var termLiteral))
                        {
                            codePoints = [];
                            return false;
                        }

                        for (var j = 0; j < termLiteral.Length; j++)
                            builder.Add(termLiteral[j]);
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

                    Span<int> initialBuffer = stackalloc int[MaxRequiredLiteralPrefixLength];
                    using var builder = new PooledArrayBuilder<int>(initialBuffer);
                    for (var repeat = 0; repeat < quantifier.Min && builder.Length < MaxRequiredLiteralPrefixLength; repeat++)
                    for (var i = 0; i < childLiteral.Length && builder.Length < MaxRequiredLiteralPrefixLength; i++)
                        builder.Add(childLiteral[i]);
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

            Span<char> initialBuffer = stackalloc char[Math.Min(Math.Max(codePoints.Length * 2, 8), 128)];
            using var builder = new PooledCharBuilder(initialBuffer);
            for (var i = 0; i < codePoints.Length; i++)
            {
                var codePoint = codePoints[i];
                if (!Rune.TryCreate(codePoint, out var rune))
                {
                    text = string.Empty;
                    return false;
                }

                builder.AppendRune(rune);
            }

            text = builder.ToString();
            return true;
        }
    }
}
