using System.Diagnostics;
using System.Text;

namespace Okojo.RegExp.Experimental;

internal static partial class ScratchRegExpMatcher
{
    private const int ExhaustiveBacktrackingInputLimit = 256;

    public static RegExpMatchResult? Exec(ExperimentalCompiledProgram compiledProgram, string input, int startIndex)
    {
        var program = compiledProgram.TreeProgram;
        var begin = Math.Max(0, startIndex);
        if (compiledProgram.WholeInputSimpleRunPlan is { } wholeInputSimpleRunPlan)
            return TryExecWholeInputSimpleRun(program, wholeInputSimpleRunPlan, input, begin);

        return ExecLinearBytecode(compiledProgram, input, begin);
    }

    private static RegExpMatchResult? TryExecWholeInputSimpleRun(ScratchRegExpProgram program,
        ExperimentalWholeInputSimpleRunPlan plan, string input, int begin)
    {
        if (begin != 0)
            return null;

        var currentPos = 0;
        var consumed = 0;
        if (TryConsumeWholeInputSimpleRun(plan.Atom, input, 0, plan.MaxCount, out var bulkEnd, out var bulkConsumed))
        {
            currentPos = bulkEnd;
            consumed = bulkConsumed;
        }

        if (consumed < plan.MinCount || currentPos != input.Length)
            return null;

        return BuildSimpleMatch(program, input, 0, currentPos);
    }

    private static bool TryConsumeWholeInputSimpleRun(ExperimentalWholeInputSimpleAtomPlan atom, string input, int pos,
        int maxCount, out int endPos, out int consumed)
    {
        switch (atom.Kind)
        {
            case ExperimentalWholeInputSimpleAtomKind.Literal:
                return ConsumeLiteralRun(atom.LiteralCodePoint, input, pos, atom.Flags, maxCount, out endPos, out consumed);
            case ExperimentalWholeInputSimpleAtomKind.Dot:
                return ConsumeDotRun(input, pos, atom.Flags, maxCount, out endPos, out consumed);
            case ExperimentalWholeInputSimpleAtomKind.CharacterSet:
                return ConsumeCharacterSetRun(atom.CharacterSet!, input, pos, atom.Flags, maxCount, out endPos,
                    out consumed);
            case ExperimentalWholeInputSimpleAtomKind.PropertyEscape:
                ConsumePropertyEscapeRunForVm(input, pos, atom.PropertyEscape, atom.Flags, input.Length, maxCount,
                    out endPos, out consumed);
                return consumed != 0;
            default:
                endPos = pos;
                consumed = 0;
                return false;
        }
    }

    private static bool ConsumeLiteralRun(int literalCodePoint, string input, int pos, RegExpRuntimeFlags flags,
        int maxCount, out int endPos, out int consumed)
    {
        var currentPos = pos;
        consumed = 0;
        while (consumed < maxCount &&
               TryReadCodePoint(input, currentPos, flags.Unicode, out var nextPos, out var codePoint) &&
               CodePointEquals(codePoint, literalCodePoint, flags.IgnoreCase))
        {
            consumed++;
            currentPos = nextPos;
        }

        endPos = currentPos;
        return consumed != 0;
    }

    private static bool ConsumeDotRun(string input, int pos, RegExpRuntimeFlags flags, int maxCount, out int endPos,
        out int consumed)
    {
        var currentPos = pos;
        consumed = 0;
        while (consumed < maxCount &&
               TryReadCodePoint(input, currentPos, flags.Unicode, out var nextPos, out var codePoint) &&
               (flags.DotAll || !IsLineTerminator(codePoint)))
        {
            consumed++;
            currentPos = nextPos;
        }

        endPos = currentPos;
        return consumed != 0;
    }

    private static bool ConsumeCharacterSetRun(ExperimentalRegExpCharacterSet characterSet, string input, int pos,
        RegExpRuntimeFlags flags, int maxCount, out int endPos, out int consumed)
    {
        if (characterSet.SimpleClass is { } simpleClass &&
            TryConsumeSpecialSimpleClassRun(simpleClass, input, pos, flags, maxCount, out endPos, out consumed))
            return consumed != 0;

        var currentPos = pos;
        consumed = 0;
        while (consumed < maxCount &&
               TryMatchCharacterSetForVm(input, currentPos, characterSet, flags, input.Length, out var nextPos))
        {
            consumed++;
            currentPos = nextPos;
        }

        endPos = currentPos;
        return consumed != 0;
    }

    private static bool TryConsumeSpecialSimpleClassRun(ExperimentalRegExpSimpleClass simpleClass, string input, int pos,
        RegExpRuntimeFlags flags, int maxCount, out int endPos, out int consumed)
    {
        if (simpleClass.Negated || simpleClass.Items.Length != 1)
        {
            endPos = pos;
            consumed = 0;
            return false;
        }

        switch (simpleClass.Items[0].Kind)
        {
            case ExperimentalRegExpSimpleClassItemKind.Space:
                ConsumeWhitespaceRun(input, pos, maxCount, out endPos, out consumed);
                return true;
            case ExperimentalRegExpSimpleClassItemKind.NotSpace:
                ConsumeNonWhitespaceRun(input, pos, flags.Unicode, maxCount, out endPos, out consumed);
                return true;
            default:
                endPos = pos;
                consumed = 0;
                return false;
        }
    }

    private static void ConsumeWhitespaceRun(string input, int pos, int maxCount, out int endPos, out int consumed)
    {
        var currentPos = pos;
        consumed = 0;
        while (consumed < maxCount &&
               (uint)currentPos < (uint)input.Length &&
               IsSpace(input[currentPos]))
        {
            consumed++;
            currentPos++;
        }

        endPos = currentPos;
    }

    private static void ConsumeNonWhitespaceRun(string input, int pos, bool unicode, int maxCount, out int endPos,
        out int consumed)
    {
        var currentPos = pos;
        consumed = 0;
        while (consumed < maxCount && (uint)currentPos < (uint)input.Length)
        {
            if (IsSpace(input[currentPos]))
                break;

            consumed++;
            currentPos = AdvanceWholeInputSimpleRunCodePoint(input, currentPos, unicode);
        }

        endPos = currentPos;
    }

    private static int AdvanceWholeInputSimpleRunCodePoint(string input, int pos, bool unicode)
    {
        if (unicode &&
            pos + 1 < input.Length &&
            char.IsHighSurrogate(input[pos]) &&
            char.IsLowSurrogate(input[pos + 1]))
            return pos + 2;

        return pos + 1;
    }

    private static RegExpMatchResult? ExecLinearBytecode(ExperimentalCompiledProgram compiledProgram, string input,
        int begin)
    {
        var program = compiledProgram.TreeProgram;
        var bytecodeProgram = compiledProgram.BytecodeProgram!;
        using var captureState = program.CaptureCount == 0 ? null : new ExperimentalRegExpCaptureState(program.CaptureCount);
        if (program.Flags.Sticky)
        {
            captureState?.Reset();
            return ExperimentalRegExpVm.TryMatch(compiledProgram, bytecodeProgram, input, begin, program.Flags,
                captureState,
                out var stickyEnd)
                ? BuildBytecodeMatch(program, input, begin, stickyEnd, captureState)
                : null;
        }

        for (var pos = FindSearchCandidate(program, input, begin); pos <= input.Length;)
        {
            captureState?.Reset();
            if (ExperimentalRegExpVm.TryMatch(compiledProgram, bytecodeProgram, input, pos, program.Flags, captureState,
                    out var endIndex))
                return BuildBytecodeMatch(program, input, pos, endIndex, captureState);

            pos = NextSearchPosition(program, input, pos);
        }

        return null;
    }

    private static RegExpMatchResult BuildSimpleMatch(ScratchRegExpProgram program, string input, int startIndex,
        int endIndex)
    {
        string?[] groups = [null];
        groups[0] = input.Substring(startIndex, endIndex - startIndex);
        var groupIndices = program.Flags.HasIndices ? new RegExpMatchRange?[1] : null;
        if (groupIndices is not null)
            groupIndices[0] = new RegExpMatchRange(startIndex, endIndex);
        return new(startIndex, endIndex - startIndex, groups, null, groupIndices, null);
    }

    private static RegExpMatchResult BuildBytecodeMatch(ScratchRegExpProgram program, string input, int startIndex,
        int endIndex, ExperimentalRegExpCaptureState? captureState)
    {
        if (captureState is null)
            return BuildSimpleMatch(program, input, startIndex, endIndex);

        var groups = new string?[program.CaptureCount + 1];
        var hasIndices = program.Flags.HasIndices;
        var groupIndices = hasIndices ? new RegExpMatchRange?[program.CaptureCount + 1] : null;
        groups[0] = input.Substring(startIndex, endIndex - startIndex);
        if (groupIndices is not null)
            groupIndices[0] = new RegExpMatchRange(startIndex, endIndex);

        for (var i = 1; i < groups.Length; i++)
            if (captureState.Matched[i])
            {
                groups[i] = input.Substring(captureState.Starts[i], captureState.Ends[i] - captureState.Starts[i]);
                if (groupIndices is not null)
                    groupIndices[i] = new RegExpMatchRange(captureState.Starts[i], captureState.Ends[i]);
            }

        IReadOnlyDictionary<string, string?>? namedGroups = null;
        IReadOnlyDictionary<string, RegExpMatchRange?>? namedGroupIndices = null;
        if (program.NamedGroupNames.Length != 0)
        {
            var dict = new Dictionary<string, string?>(program.NamedGroupNames.Length, StringComparer.Ordinal);
            var indexDict = hasIndices
                ? new Dictionary<string, RegExpMatchRange?>(program.NamedGroupNames.Length, StringComparer.Ordinal)
                : null;
            for (var i = 0; i < program.NamedGroupNames.Length; i++)
            {
                var name = program.NamedGroupNames[i];
                string? value = null;
                RegExpMatchRange? range = null;
                if (program.NamedCaptureIndexes.TryGetValue(name, out var indexes))
                    for (var j = 0; j < indexes.Count; j++)
                    {
                        var index = indexes[j];
                        if (!captureState.Matched[index])
                            continue;

                        value = groups[index];
                        if (groupIndices is not null)
                            range = groupIndices[index];
                        break;
                    }

                dict[name] = value;
                if (indexDict is not null)
                    indexDict[name] = range;
            }

            namedGroups = dict;
            namedGroupIndices = indexDict;
        }

        return new(startIndex, endIndex - startIndex, groups, namedGroups, groupIndices, namedGroupIndices);
    }

    private static bool TryConsumeSimpleQuantifiedChildRun(
        ScratchRegExpProgram.Node node,
        string input,
        int pos,
        RegExpRuntimeFlags flags,
        int maxCount,
        out int endPos,
        out int consumed,
        out RegExpRuntimeFlags nextFlags)
    {
        nextFlags = flags;
        endPos = pos;
        consumed = 0;

        switch (node)
        {
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                if (TryConsumeSimpleQuantifiedChildRun(scoped.Child, input, pos, ApplyScopedModifiers(flags, scoped),
                        maxCount, out endPos, out consumed, out _))
                {
                    nextFlags = flags;
                    return true;
                }

                return false;
            case ScratchRegExpProgram.PropertyEscapeNode propertyEscape:
                break;
            default:
                return false;
        }

        ConsumePropertyEscapeRun(input, pos, (ScratchRegExpProgram.PropertyEscapeNode)node, flags, maxCount, out endPos,
            out consumed);
        return consumed != 0;
    }

    private static bool CanUseFastBacktrackingPath(ScratchRegExpProgram.Node node)
    {
        switch (node)
        {
            case ScratchRegExpProgram.EmptyNode:
            case ScratchRegExpProgram.LiteralNode:
            case ScratchRegExpProgram.DotNode:
            case ScratchRegExpProgram.AnchorNode:
            case ScratchRegExpProgram.BoundaryNode:
            case ScratchRegExpProgram.ClassNode:
            case ScratchRegExpProgram.PropertyEscapeNode:
            case ScratchRegExpProgram.BackReferenceNode:
            case ScratchRegExpProgram.NamedBackReferenceNode:
                return true;
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                return CanUseFastBacktrackingPath(scoped.Child);
            case ScratchRegExpProgram.CaptureNode capture:
                return CanUseFastBacktrackingPath(capture.Child);
            case ScratchRegExpProgram.SequenceNode sequence:
                foreach (var term in sequence.Terms)
                    if (!CanUseFastBacktrackingPath(term))
                        return false;
                return true;
            default:
                return false;
        }
    }

    private static bool TryMatchBackReferenceBackward(ScratchRegExpProgram.BackReferenceNode backReference,
        string input, int pos,
        RegExpRuntimeFlags flags, ScratchMatchState state, out int startIndex, int startLimit = 0)
    {
        if (!state.Matched[backReference.Index])
        {
            startIndex = pos;
            return true;
        }

        var captureStart = state.Starts[backReference.Index];
        var length = state.Ends[backReference.Index] - captureStart;
        if (length == 0)
        {
            startIndex = pos;
            return true;
        }

        var candidateStart = pos - length;
        if (candidateStart < 0 || candidateStart < startLimit)
        {
            startIndex = default;
            return false;
        }

        var captureOffset = captureStart;
        var candidateOffset = candidateStart;
        var captureEnd = state.Ends[backReference.Index];
        while (captureOffset < captureEnd)
        {
            if (!TryReadCodePoint(input, captureOffset, flags.Unicode, out var nextCaptureOffset, out var captureCp) ||
                !TryReadCodePoint(input, candidateOffset, flags.Unicode, out var nextCandidateOffset,
                    out var candidateCp) ||
                !CodePointEquals(captureCp, candidateCp, flags.IgnoreCase))
            {
                startIndex = default;
                return false;
            }

            captureOffset = nextCaptureOffset;
            candidateOffset = nextCandidateOffset;
        }

        if (candidateOffset != pos)
        {
            startIndex = default;
            return false;
        }

        startIndex = candidateStart;
        return true;
    }

    private static bool TryMatchNamedBackReferenceBackward(ScratchRegExpProgram program,
        ScratchRegExpProgram.NamedBackReferenceNode backReference, string input, int pos, RegExpRuntimeFlags flags,
        ScratchMatchState state, out int startIndex, int startLimit = 0)
    {
        if (!program.NamedCaptureIndexes.TryGetValue(backReference.Name, out var indexes))
        {
            startIndex = pos;
            return true;
        }

        var anyMatched = false;
        for (var i = indexes.Count - 1; i >= 0; i--)
        {
            var index = indexes[i];
            if (!state.Matched[index])
                continue;

            anyMatched = true;
            var numericBackReference = new ScratchRegExpProgram.BackReferenceNode(index);
            if (TryMatchBackReferenceBackward(numericBackReference, input, pos, flags, state, out startIndex,
                    startLimit))
                return true;
        }

        if (!anyMatched)
        {
            startIndex = pos;
            return true;
        }

        startIndex = default;
        return false;
    }

    private static bool TryMatchSimpleClassForward(ExperimentalRegExpSimpleClass cls, string input, int pos,
        RegExpRuntimeFlags flags, out int endIndex)
    {
        var hasCodePoint = TryReadCodePoint(input, pos, flags.Unicode, out var nextPos, out var codePoint);
        if (!TryMatchSimpleClassCodePoint(cls, codePoint, flags, out var matched, hasCodePoint))
        {
            endIndex = default;
            return false;
        }

        if (!cls.Negated)
        {
            endIndex = matched ? nextPos : default;
            return matched;
        }

        if (matched || !hasCodePoint)
        {
            endIndex = default;
            return false;
        }

        endIndex = nextPos;
        return true;
    }

    private static bool TryMatchClassForward(string input, int pos, ScratchRegExpProgram.ClassNode cls,
        RegExpRuntimeFlags flags, out int endIndex)
    {
        var hasCodePoint = TryReadCodePoint(input, pos, flags.Unicode, out var nextPos, out var codePoint);
        if (TryMatchSimpleClassForward(cls, flags, hasCodePoint, nextPos, codePoint, out endIndex))
            return true;

        using var candidates = GetClassCandidatesForward(input, pos, cls, flags, hasCodePoint, nextPos, codePoint);
        var bestEnd = candidates.MaxOrDefault();

        if (!cls.Negated)
        {
            endIndex = bestEnd;
            return bestEnd >= 0;
        }

        if (bestEnd >= 0 || !hasCodePoint)
        {
            endIndex = default;
            return false;
        }

        endIndex = nextPos;
        return true;
    }

    private static bool TryMatchSimpleClassCodePoint(ExperimentalRegExpSimpleClass cls, int codePoint,
        RegExpRuntimeFlags flags, out bool matched, bool hasCodePoint)
    {
        if (!hasCodePoint)
        {
            matched = false;
            return true;
        }

        matched = false;
        for (var i = 0; i < cls.Items.Length; i++)
            if (SimpleClassItemMatchesCodePoint(cls.Items[i], codePoint, flags))
            {
                matched = true;
                return true;
            }

        return true;
    }

    private static bool SimpleClassItemMatchesCodePoint(ExperimentalRegExpSimpleClassItem item, int codePoint,
        RegExpRuntimeFlags flags)
    {
        return item.Kind switch
        {
            ExperimentalRegExpSimpleClassItemKind.Digit => codePoint is >= '0' and <= '9',
            ExperimentalRegExpSimpleClassItemKind.NotDigit => !(codePoint is >= '0' and <= '9'),
            ExperimentalRegExpSimpleClassItemKind.Space => IsSpace(codePoint),
            ExperimentalRegExpSimpleClassItemKind.NotSpace => !IsSpace(codePoint),
            ExperimentalRegExpSimpleClassItemKind.Word => IsWord(codePoint, flags.Unicode, flags.IgnoreCase),
            ExperimentalRegExpSimpleClassItemKind.NotWord => !IsWord(codePoint, flags.Unicode, flags.IgnoreCase),
            ExperimentalRegExpSimpleClassItemKind.Literal => CodePointEquals(codePoint, item.CodePoint, flags.IgnoreCase),
            ExperimentalRegExpSimpleClassItemKind.Range => CodePointInRange(codePoint, item.RangeStart, item.RangeEnd,
                flags.IgnoreCase),
            ExperimentalRegExpSimpleClassItemKind.PropertyEscape => FastPropertyEscapeMatches(item.PropertyEscape,
                codePoint, flags),
            _ => false
        };
    }

    private static bool TryMatchClassBackward(string input, int pos, ScratchRegExpProgram.ClassNode cls,
        RegExpRuntimeFlags flags, out int startIndex, int startLimit = 0)
    {
        var hasCodePoint = TryReadCodePointBackward(input, pos, flags.Unicode, out var prevPos, out var codePoint);
        if (TryMatchSimpleClassBackward(cls, flags, hasCodePoint && prevPos >= startLimit, prevPos, codePoint,
                out startIndex))
            return true;

        using var candidates = GetClassCandidatesBackward(input, pos, cls, flags, hasCodePoint, prevPos, codePoint);
        var bestStart = GetMaxCandidateAtOrAbove(candidates, startLimit);

        if (!cls.Negated)
        {
            startIndex = bestStart;
            return bestStart >= 0;
        }

        if (bestStart >= 0 || !hasCodePoint)
        {
            startIndex = default;
            return false;
        }

        startIndex = prevPos;
        return true;
    }

    private static int GetMaxCandidateAtOrAbove(ScratchPooledIntSet candidates, int minimum)
    {
        var best = -1;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate >= minimum && candidate > best)
                best = candidate;
        }

        return best;
    }

    private static int GetLookbehindStartLimit(ScratchRegExpProgram.Node node, int pos)
    {
        return ScratchRegExpProgram.TryGetNodeMaxMatchLength(node, out var maxMatchLength) && maxMatchLength >= 0
            ? Math.Max(0, pos - maxMatchLength)
            : 0;
    }

    private static bool TryMatchSimpleClassForward(ScratchRegExpProgram.ClassNode cls, RegExpRuntimeFlags flags,
        bool hasCodePoint, int nextPos, int codePoint, out int endIndex)
    {
        if (!TryMatchSimpleClassCodePoint(cls, codePoint, flags, out var matched, hasCodePoint))
        {
            endIndex = default;
            return false;
        }

        if (!cls.Negated)
        {
            endIndex = matched ? nextPos : default;
            return matched;
        }

        if (matched || !hasCodePoint)
        {
            endIndex = default;
            return false;
        }

        endIndex = nextPos;
        return true;
    }

    private static bool TryMatchSimpleClassBackward(ScratchRegExpProgram.ClassNode cls, RegExpRuntimeFlags flags,
        bool hasCodePoint, int prevPos, int codePoint, out int startIndex)
    {
        if (!TryMatchSimpleClassCodePoint(cls, codePoint, flags, out var matched, hasCodePoint))
        {
            startIndex = default;
            return false;
        }

        if (!cls.Negated)
        {
            startIndex = matched ? prevPos : default;
            return matched;
        }

        if (matched || !hasCodePoint)
        {
            startIndex = default;
            return false;
        }

        startIndex = prevPos;
        return true;
    }

    private static bool TryMatchSimpleClassCodePoint(ScratchRegExpProgram.ClassNode cls, int codePoint,
        RegExpRuntimeFlags flags, out bool matched, bool hasCodePoint)
    {
        if (!hasCodePoint || cls.Expression is not null)
        {
            matched = false;
            return cls.Expression is null;
        }

        matched = false;
        var clsItems = cls.Items;
        for (var i = 0; i < clsItems.Length; i++)
        {
            var item = clsItems[i];
            if (item.Kind == ScratchRegExpProgram.ClassItemKind.StringLiteral ||
                item.Kind == ScratchRegExpProgram.ClassItemKind.PropertyEscape &&
                item.PropertyKind == ScratchRegExpProgram.PropertyEscapeKind.StringProperty)
                return false;

            if (ClassItemMatchesCodePoint(item, codePoint, flags))
            {
                matched = true;
                return true;
            }
        }

        return true;
    }

    private static ScratchPooledIntSet GetClassCandidatesForward(
        string input,
        int pos,
        ScratchRegExpProgram.ClassNode cls,
        RegExpRuntimeFlags flags,
        bool hasCodePoint,
        int nextPos,
        int codePoint)
    {
        if (cls.Expression is not null)
            return EvaluateClassSetForward(cls.Expression, input, pos, flags, hasCodePoint, nextPos, codePoint);

        var candidates = new ScratchPooledIntSet();
        foreach (var item in cls.Items)
            AddClassItemCandidatesForward(candidates, item, input, pos, flags, hasCodePoint, nextPos, codePoint);
        return candidates;
    }

    private static ScratchPooledIntSet GetClassCandidatesBackward(
        string input,
        int pos,
        ScratchRegExpProgram.ClassNode cls,
        RegExpRuntimeFlags flags,
        bool hasCodePoint,
        int prevPos,
        int codePoint)
    {
        if (cls.Expression is not null)
            return EvaluateClassSetBackward(cls.Expression, input, pos, flags, hasCodePoint, prevPos, codePoint);

        var candidates = new ScratchPooledIntSet();
        foreach (var item in cls.Items)
            AddClassItemCandidatesBackward(candidates, item, input, pos, flags, hasCodePoint, prevPos, codePoint);
        return candidates;
    }

    private static ScratchPooledIntSet EvaluateClassSetForward(
        ScratchRegExpProgram.ClassSetExpression expression,
        string input,
        int pos,
        RegExpRuntimeFlags flags,
        bool hasCodePoint,
        int nextPos,
        int codePoint)
    {
        switch (expression)
        {
            case ScratchRegExpProgram.ClassSetItemExpression itemExpression:
                {
                    var results = new ScratchPooledIntSet();
                    AddClassItemCandidatesForward(results, itemExpression.Item, input, pos, flags, hasCodePoint, nextPos,
                        codePoint);
                    return results;
                }
            case ScratchRegExpProgram.ClassSetNestedExpression nested:
                return GetClassCandidatesForward(input, pos, nested.Class, flags, hasCodePoint, nextPos, codePoint);
            case ScratchRegExpProgram.ClassSetUnionExpression union:
                {
                    var results = new ScratchPooledIntSet();
                    foreach (var term in union.Terms)
                    {
                        using var termResults =
                            EvaluateClassSetForward(term, input, pos, flags, hasCodePoint, nextPos, codePoint);
                        results.UnionWith(termResults);
                    }

                    return results;
                }
            case ScratchRegExpProgram.ClassSetIntersectionExpression intersection:
                {
                    var left = EvaluateClassSetForward(intersection.Left, input, pos, flags, hasCodePoint, nextPos,
                        codePoint);
                    using var right = EvaluateClassSetForward(intersection.Right, input, pos, flags, hasCodePoint, nextPos,
                        codePoint);
                    left.IntersectWith(right);
                    return left;
                }
            case ScratchRegExpProgram.ClassSetDifferenceExpression difference:
                {
                    var left = EvaluateClassSetForward(difference.Left, input, pos, flags, hasCodePoint, nextPos,
                        codePoint);
                    using var right = EvaluateClassSetForward(difference.Right, input, pos, flags, hasCodePoint, nextPos,
                        codePoint);
                    left.ExceptWith(right);
                    return left;
                }
            default:
                return new();
        }
    }

    private static ScratchPooledIntSet EvaluateClassSetBackward(
        ScratchRegExpProgram.ClassSetExpression expression,
        string input,
        int pos,
        RegExpRuntimeFlags flags,
        bool hasCodePoint,
        int prevPos,
        int codePoint)
    {
        switch (expression)
        {
            case ScratchRegExpProgram.ClassSetItemExpression itemExpression:
                {
                    var results = new ScratchPooledIntSet();
                    AddClassItemCandidatesBackward(results, itemExpression.Item, input, pos, flags, hasCodePoint, prevPos,
                        codePoint);
                    return results;
                }
            case ScratchRegExpProgram.ClassSetNestedExpression nested:
                return GetClassCandidatesBackward(input, pos, nested.Class, flags, hasCodePoint, prevPos, codePoint);
            case ScratchRegExpProgram.ClassSetUnionExpression union:
                {
                    var results = new ScratchPooledIntSet();
                    foreach (var term in union.Terms)
                    {
                        using var termResults =
                            EvaluateClassSetBackward(term, input, pos, flags, hasCodePoint, prevPos, codePoint);
                        results.UnionWith(termResults);
                    }

                    return results;
                }
            case ScratchRegExpProgram.ClassSetIntersectionExpression intersection:
                {
                    var left = EvaluateClassSetBackward(intersection.Left, input, pos, flags, hasCodePoint, prevPos,
                        codePoint);
                    using var right = EvaluateClassSetBackward(intersection.Right, input, pos, flags, hasCodePoint, prevPos,
                        codePoint);
                    left.IntersectWith(right);
                    return left;
                }
            case ScratchRegExpProgram.ClassSetDifferenceExpression difference:
                {
                    var left = EvaluateClassSetBackward(difference.Left, input, pos, flags, hasCodePoint, prevPos,
                        codePoint);
                    using var right = EvaluateClassSetBackward(difference.Right, input, pos, flags, hasCodePoint, prevPos,
                        codePoint);
                    left.ExceptWith(right);
                    return left;
                }
            default:
                return new();
        }
    }

    private static void AddClassItemCandidatesForward(
        ScratchPooledIntSet results,
        ScratchRegExpProgram.ClassItem item,
        string input,
        int pos,
        RegExpRuntimeFlags flags,
        bool hasCodePoint,
        int nextPos,
        int codePoint)
    {
        if (item.Kind == ScratchRegExpProgram.ClassItemKind.StringLiteral)
        {
            if (item.Strings is not null)
                foreach (var candidate in item.Strings)
                    if (MatchesStringAt(input, pos, candidate))
                        results.Add(pos + candidate.Length);

            return;
        }

        if (item.Kind == ScratchRegExpProgram.ClassItemKind.PropertyEscape &&
            item.PropertyKind == ScratchRegExpProgram.PropertyEscapeKind.StringProperty &&
            item.PropertyValue is not null)
        {
            ScratchUnicodeStringPropertyTables.AddMatchEndsAt(item.PropertyValue, input, pos, results);
            return;
        }

        if (hasCodePoint && ClassItemMatchesCodePoint(item, codePoint, flags))
            results.Add(nextPos);
    }

    private static void AddClassItemCandidatesBackward(
        ScratchPooledIntSet results,
        ScratchRegExpProgram.ClassItem item,
        string input,
        int pos,
        RegExpRuntimeFlags flags,
        bool hasCodePoint,
        int prevPos,
        int codePoint)
    {
        if (item.Kind == ScratchRegExpProgram.ClassItemKind.StringLiteral)
        {
            if (item.Strings is not null)
                foreach (var candidate in item.Strings)
                    if (MatchesStringBackward(input, pos, candidate))
                        results.Add(pos - candidate.Length);

            return;
        }

        if (item.Kind == ScratchRegExpProgram.ClassItemKind.PropertyEscape &&
            item.PropertyKind == ScratchRegExpProgram.PropertyEscapeKind.StringProperty &&
            item.PropertyValue is not null)
        {
            ScratchUnicodeStringPropertyTables.AddMatchStartsBackward(item.PropertyValue, input, pos, results);
            return;
        }

        if (hasCodePoint && ClassItemMatchesCodePoint(item, codePoint, flags))
            results.Add(prevPos);
    }

    private static bool ClassItemMatchesCodePoint(ScratchRegExpProgram.ClassItem item, int codePoint,
        RegExpRuntimeFlags flags)
    {
        return item.Kind switch
        {
            ScratchRegExpProgram.ClassItemKind.Digit => codePoint is >= '0' and <= '9',
            ScratchRegExpProgram.ClassItemKind.NotDigit => !(codePoint is >= '0' and <= '9'),
            ScratchRegExpProgram.ClassItemKind.Space => IsSpace(codePoint),
            ScratchRegExpProgram.ClassItemKind.NotSpace => !IsSpace(codePoint),
            ScratchRegExpProgram.ClassItemKind.Word => IsWord(codePoint, flags.Unicode, flags.IgnoreCase),
            ScratchRegExpProgram.ClassItemKind.NotWord => !IsWord(codePoint, flags.Unicode, flags.IgnoreCase),
            ScratchRegExpProgram.ClassItemKind.Literal => CodePointEquals(codePoint, item.CodePoint, flags.IgnoreCase),
            ScratchRegExpProgram.ClassItemKind.Range => CodePointInRange(codePoint, item.RangeStart, item.RangeEnd,
                flags.IgnoreCase),
            ScratchRegExpProgram.ClassItemKind.PropertyEscape => PropertyEscapeMatches(item.PropertyKind,
                item.PropertyNegated, item.PropertyCategories, item.PropertyValue, codePoint, flags),
            _ => false
        };
    }

    private static bool MatchesStringAt(string input, int pos, string candidate)
    {
        return pos + candidate.Length <= input.Length &&
               input.AsSpan(pos, candidate.Length).SequenceEqual(candidate);
    }

    private static bool MatchesStringBackward(string input, int pos, string candidate)
    {
        return pos >= candidate.Length &&
               input.AsSpan(pos - candidate.Length, candidate.Length).SequenceEqual(candidate);
    }

    private static RegExpRuntimeFlags ApplyScopedModifiers(RegExpRuntimeFlags flags,
        ScratchRegExpProgram.ScopedModifiersNode scoped)
    {
        return new(
            flags.Global,
            scoped.IgnoreCase ?? flags.IgnoreCase,
            scoped.Multiline ?? flags.Multiline,
            flags.HasIndices,
            flags.Sticky,
            flags.Unicode,
            flags.UnicodeSets,
            scoped.DotAll ?? flags.DotAll);
    }

}
