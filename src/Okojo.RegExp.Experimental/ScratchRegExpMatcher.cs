using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace Okojo.RegExp.Experimental;

internal static class ScratchRegExpMatcher
{
    private const int ExhaustiveBacktrackingInputLimit = 256;

    public static RegExpMatchResult? Exec(ExperimentalCompiledProgram compiledProgram, string input, int startIndex)
    {
        var program = compiledProgram.TreeProgram;
        var begin = Math.Max(0, startIndex);
        if (program.HasExactLiteralPattern)
            return ExecExactLiteral(compiledProgram, input, begin);
        if (compiledProgram.BytecodeProgram is not null)
            return ExecLinearBytecode(compiledProgram, input, begin);

        using var stateArena = program.CaptureCount == 0 ? null : new ScratchMatchStateArena(program.CaptureCount);
        if (program.Flags.Sticky)
        {
            var state = stateArena is null ? ScratchMatchState.Empty : stateArena.Root;
            if (TryMatchNode(program, program.Root, input, begin, program.Flags, state, out var endIndex))
                return BuildMatch(program, input, begin, endIndex, state);
            return null;
        }

        for (var pos = FindSearchCandidate(program, input, begin); pos <= input.Length;)
        {
            if (stateArena is not null)
                stateArena.Reset();

            var state = stateArena is null ? ScratchMatchState.Empty : stateArena.Root;
            if (TryMatchNode(program, program.Root, input, pos, program.Flags, state, out var endIndex))
                return BuildMatch(program, input, pos, endIndex, state);

            pos = NextSearchPosition(program, input, pos);
        }

        return null;
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
            return ExperimentalRegExpVm.TryMatch(program, bytecodeProgram, input, begin, program.Flags, captureState,
                out var stickyEnd)
                ? BuildBytecodeMatch(program, input, begin, stickyEnd, captureState)
                : null;
        }

        for (var pos = FindSearchCandidate(program, input, begin); pos <= input.Length;)
        {
            captureState?.Reset();
            if (ExperimentalRegExpVm.TryMatch(program, bytecodeProgram, input, pos, program.Flags, captureState,
                    out var endIndex))
                return BuildBytecodeMatch(program, input, pos, endIndex, captureState);

            pos = NextSearchPosition(program, input, pos);
        }

        return null;
    }

    private static RegExpMatchResult? ExecExactLiteral(ExperimentalCompiledProgram compiledProgram, string input,
        int begin)
    {
        var program = compiledProgram.TreeProgram;
        if (program.Flags.Sticky)
            return TryMatchExactLiteralAt(compiledProgram, input, begin, out var stickyEnd)
                ? BuildSimpleMatch(program, input, begin, stickyEnd)
                : null;

        for (var pos = FindSearchCandidate(program, input, begin); pos <= input.Length;)
        {
            if (TryMatchExactLiteralAt(compiledProgram, input, pos, out var endIndex))
                return BuildSimpleMatch(program, input, pos, endIndex);

            pos = NextSearchPosition(program, input, pos);
        }

        return null;
    }

    private static RegExpMatchResult BuildMatch(ScratchRegExpProgram program, string input, int startIndex,
        int endIndex,
        ScratchMatchState state)
    {
        var groups = new string?[program.CaptureCount + 1];
        var hasIndices = program.Flags.HasIndices;
        var groupIndices = hasIndices ? new RegExpMatchRange?[program.CaptureCount + 1] : null;
        groups[0] = input.Substring(startIndex, endIndex - startIndex);
        if (groupIndices is not null)
            groupIndices[0] = new RegExpMatchRange(startIndex, endIndex);
        for (var i = 1; i < groups.Length; i++)
            if (state.Matched[i])
            {
                groups[i] = input.Substring(state.Starts[i], state.Ends[i] - state.Starts[i]);
                if (groupIndices is not null)
                    groupIndices[i] = new RegExpMatchRange(state.Starts[i], state.Ends[i]);
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
                        if (!state.Matched[index])
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

    private static bool TryMatchNode(ScratchRegExpProgram program, ScratchRegExpProgram.Node node, string input,
        int pos, RegExpRuntimeFlags flags,
        ScratchMatchState state, out int endIndex, bool nestedInQuantifierContext = false)
    {
        switch (node)
        {
            case ScratchRegExpProgram.EmptyNode:
                endIndex = pos;
                return true;
            case ScratchRegExpProgram.LiteralNode literal:
                if (TryReadCodePoint(input, pos, flags.Unicode, out var nextPos, out var cp) &&
                    CodePointEquals(cp, literal.CodePoint, flags.IgnoreCase))
                {
                    endIndex = nextPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.DotNode:
                if (TryReadCodePoint(input, pos, flags.Unicode, out nextPos, out cp) &&
                    (flags.DotAll || !IsLineTerminator(cp)))
                {
                    endIndex = nextPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.AnchorNode anchor:
                if (anchor.Start)
                {
                    if (pos == 0 || (flags.Multiline && pos > 0 && IsLineTerminator(input[pos - 1])))
                    {
                        endIndex = pos;
                        return true;
                    }
                }
                else if (pos == input.Length || (flags.Multiline && pos < input.Length && IsLineTerminator(input[pos])))
                {
                    endIndex = pos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.BoundaryNode boundary:
                var atBoundary = IsWordBoundary(input, pos, flags.Unicode, flags.IgnoreCase);
                if (boundary.Positive == atBoundary)
                {
                    endIndex = pos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.ClassNode cls:
                if (TryMatchClassForward(input, pos, cls, flags, out nextPos))
                {
                    endIndex = nextPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.PropertyEscapeNode propertyEscape:
                if (TryMatchPropertyEscapeForward(input, pos, propertyEscape, flags, out nextPos))
                {
                    endIndex = nextPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.BackReferenceNode backReference:
                Trace($"backref index={backReference.Index} pos={pos} matched={state.Matched[backReference.Index]}");
                if (TryMatchBackReference(backReference, input, pos, flags, state, out nextPos))
                {
                    Trace($"backref success index={backReference.Index} end={nextPos}");
                    endIndex = nextPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.NamedBackReferenceNode namedBackReference:
                if (TryMatchNamedBackReference(program, namedBackReference, input, pos, flags, state, out nextPos))
                {
                    endIndex = nextPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.CaptureNode capture:
            {
                using var snapshotLease = state.RentClone(out var snapshot);
                snapshot.Starts[capture.Index] = pos;
                snapshot.Matched[capture.Index] = true;
                if (TryMatchNode(program, capture.Child, input, pos, flags, snapshot, out nextPos,
                        nestedInQuantifierContext))
                {
                    snapshot.Ends[capture.Index] = nextPos;
                    state.CopyFrom(snapshot);
                    endIndex = nextPos;
                    return true;
                }

                break;
            }
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
            {
                var scopedFlags = ApplyScopedModifiers(flags, scoped);
                if (TryMatchNode(program, scoped.Child, input, pos, scopedFlags, state, out nextPos,
                        nestedInQuantifierContext))
                {
                    endIndex = nextPos;
                    return true;
                }

                break;
            }
            case ScratchRegExpProgram.LookaheadNode lookahead:
            {
                using var snapshotLease = state.RentClone(out var snapshot);
                var matched = TryMatchNode(program, lookahead.Child, input, pos, flags, snapshot, out _,
                    nestedInQuantifierContext);
                Trace($"lookahead positive={lookahead.Positive} pos={pos} matched={matched}");
                if (lookahead.Positive ? matched : !matched)
                {
                    if (lookahead.Positive && matched)
                        state.CopyFrom(snapshot);
                    Trace($"lookahead success positive={lookahead.Positive} pos={pos}");
                    endIndex = pos;
                    return true;
                }

                Trace($"lookahead fail positive={lookahead.Positive} pos={pos}");
                break;
            }
            case ScratchRegExpProgram.LookbehindNode lookbehind:
            {
                using var snapshotLease = state.RentClone(out var snapshot);
                var matched = TryMatchNodeBackward(program, lookbehind.Child, input, pos, flags, snapshot, out _,
                    nestedInQuantifierContext);
                Trace($"lookbehind positive={lookbehind.Positive} pos={pos} matched={matched}");
                if (lookbehind.Positive ? matched : !matched)
                {
                    if (lookbehind.Positive && matched)
                        state.CopyFrom(snapshot);
                    Trace($"lookbehind success positive={lookbehind.Positive} pos={pos}");
                    endIndex = pos;
                    return true;
                }

                Trace($"lookbehind fail positive={lookbehind.Positive} pos={pos}");
                break;
            }
            case ScratchRegExpProgram.SequenceNode sequence:
                return TryMatchSequence(program, sequence.Terms, 0, input, pos, flags, state, out endIndex,
                    nestedInQuantifierContext);
            case ScratchRegExpProgram.AlternationNode alternation:
                foreach (var alternative in alternation.Alternatives)
                {
                    using var snapshotLease = state.RentClone(out var snapshot);
                    if (TryMatchNode(program, alternative, input, pos, flags, snapshot, out endIndex,
                            nestedInQuantifierContext))
                    {
                        state.CopyFrom(snapshot);
                        return true;
                    }
                }

                break;
            case ScratchRegExpProgram.QuantifierNode quantifier:
                return TryMatchQuantifier(program, quantifier, input, pos, flags, state, null, 0, out endIndex,
                    nestedInQuantifierContext);
        }

        endIndex = default;
        return false;
    }

    private static bool TryMatchNodeBackward(ScratchRegExpProgram program, ScratchRegExpProgram.Node node, string input,
        int pos,
        RegExpRuntimeFlags flags, ScratchMatchState state, out int startIndex, bool nestedInQuantifierContext = false)
    {
        switch (node)
        {
            case ScratchRegExpProgram.EmptyNode:
                startIndex = pos;
                return true;
            case ScratchRegExpProgram.LiteralNode literal:
                if (TryReadCodePointBackward(input, pos, flags.Unicode, out var prevPos, out var cp) &&
                    CodePointEquals(cp, literal.CodePoint, flags.IgnoreCase))
                {
                    startIndex = prevPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.DotNode:
                if (TryReadCodePointBackward(input, pos, flags.Unicode, out prevPos, out cp) &&
                    (flags.DotAll || !IsLineTerminator(cp)))
                {
                    startIndex = prevPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.AnchorNode anchor:
                if (anchor.Start)
                {
                    if (pos == 0 || (flags.Multiline && pos > 0 && IsLineTerminator(input[pos - 1])))
                    {
                        startIndex = pos;
                        return true;
                    }
                }
                else if (pos == input.Length || (flags.Multiline && pos < input.Length && IsLineTerminator(input[pos])))
                {
                    startIndex = pos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.BoundaryNode boundary:
                var atBoundary = IsWordBoundary(input, pos, flags.Unicode, flags.IgnoreCase);
                if (boundary.Positive == atBoundary)
                {
                    startIndex = pos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.ClassNode cls:
                if (TryMatchClassBackward(input, pos, cls, flags, out prevPos))
                {
                    startIndex = prevPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.PropertyEscapeNode propertyEscape:
                if (TryMatchPropertyEscapeBackward(input, pos, propertyEscape, flags, out prevPos))
                {
                    startIndex = prevPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.BackReferenceNode backReference:
                if (TryMatchBackReferenceBackward(backReference, input, pos, flags, state, out startIndex))
                    return true;
                break;
            case ScratchRegExpProgram.NamedBackReferenceNode namedBackReference:
                if (TryMatchNamedBackReferenceBackward(program, namedBackReference, input, pos, flags, state,
                        out startIndex))
                    return true;
                break;
            case ScratchRegExpProgram.CaptureNode capture:
            {
                using var snapshotLease = state.RentClone(out var snapshot);
                snapshot.Ends[capture.Index] = pos;
                snapshot.Matched[capture.Index] = true;
                if (TryMatchNodeBackward(program, capture.Child, input, pos, flags, snapshot, out var captureStart,
                        nestedInQuantifierContext))
                {
                    snapshot.Starts[capture.Index] = captureStart;
                    state.CopyFrom(snapshot);
                    startIndex = captureStart;
                    return true;
                }

                break;
            }
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
            {
                var scopedFlags = ApplyScopedModifiers(flags, scoped);
                if (TryMatchNodeBackward(program, scoped.Child, input, pos, scopedFlags, state, out startIndex,
                        nestedInQuantifierContext))
                    return true;
                break;
            }
            case ScratchRegExpProgram.LookaheadNode lookahead:
            {
                using var snapshotLease = state.RentClone(out var snapshot);
                var matched = TryMatchNode(program, lookahead.Child, input, pos, flags, snapshot, out _,
                    nestedInQuantifierContext);
                if (lookahead.Positive ? matched : !matched)
                {
                    if (lookahead.Positive && matched)
                        state.CopyFrom(snapshot);
                    startIndex = pos;
                    return true;
                }

                break;
            }
            case ScratchRegExpProgram.LookbehindNode lookbehind:
            {
                using var snapshotLease = state.RentClone(out var snapshot);
                var matched = TryMatchNodeBackward(program, lookbehind.Child, input, pos, flags, snapshot, out _,
                    nestedInQuantifierContext);
                if (lookbehind.Positive ? matched : !matched)
                {
                    if (lookbehind.Positive && matched)
                        state.CopyFrom(snapshot);
                    startIndex = pos;
                    return true;
                }

                break;
            }
            case ScratchRegExpProgram.SequenceNode sequence:
                return TryMatchSequenceBackward(program, sequence.Terms, sequence.Terms.Length - 1, input, pos, flags,
                    state,
                    out startIndex, nestedInQuantifierContext);
            case ScratchRegExpProgram.AlternationNode alternation:
                foreach (var alternative in alternation.Alternatives)
                {
                    using var snapshotLease = state.RentClone(out var snapshot);
                    if (TryMatchNodeBackward(program, alternative, input, pos, flags, snapshot, out startIndex,
                            nestedInQuantifierContext))
                    {
                        state.CopyFrom(snapshot);
                        return true;
                    }
                }

                break;
            case ScratchRegExpProgram.QuantifierNode quantifier:
                return TryMatchQuantifierBackward(program, quantifier, input, pos, flags, state, null, -1,
                    out startIndex,
                    nestedInQuantifierContext);
        }

        startIndex = default;
        return false;
    }

    private static bool TryMatchSequence(ScratchRegExpProgram program, IReadOnlyList<ScratchRegExpProgram.Node> terms,
        int index, string input, int pos,
        RegExpRuntimeFlags flags, ScratchMatchState state, out int endIndex, bool nestedInQuantifierContext = false)
    {
        if (TryMatchAnchoredWholeStringSequence(terms, index, input, pos, flags, state, out endIndex))
            return true;

        if (index >= terms.Count)
        {
            endIndex = pos;
            return true;
        }

        var term = terms[index];
        if (term is ScratchRegExpProgram.CaptureNode capture &&
            capture.Child is ScratchRegExpProgram.QuantifierNode captureQuantifier)
        {
            using var checkpointLease = state.RentSibling(out var checkpoint);
            checkpoint.CopyFrom(state);
            state.Starts[capture.Index] = pos;
            state.Matched[capture.Index] = true;
            if (TryMatchQuantifier(program, captureQuantifier, input, pos, flags, state, terms, index + 1, out endIndex,
                    nestedInQuantifierContext, capture.Index)) return true;

            state.CopyFrom(checkpoint);
            endIndex = default;
            return false;
        }

        if (term is ScratchRegExpProgram.CaptureNode)
        {
            var trustCaptureFastPath =
                input.Length > ExhaustiveBacktrackingInputLimit || CanUseFastBacktrackingPath(term);
            using var checkpointLease = state.RentSibling(out var checkpoint);
            checkpoint.CopyFrom(state);
            if (TryMatchNode(program, term, input, pos, flags, state, out var greedyNextPos,
                    nestedInQuantifierContext) &&
                TryMatchSequence(program, terms, index + 1, input, greedyNextPos, flags, state, out endIndex,
                    nestedInQuantifierContext) &&
                trustCaptureFastPath)
                return true;

            state.CopyFrom(checkpoint);
            if (input.Length > ExhaustiveBacktrackingInputLimit)
            {
                endIndex = default;
                return false;
            }

            using var captureResults = new ScratchPooledList<(int Pos, ScratchMatchState State)>();
            CollectNodeMatchesForward(program, term, input, pos, flags, state, captureResults,
                nestedInQuantifierContext);
            for (var i = 0; i < captureResults.Count; i++)
            {
                var (captureEnd, captureState) = captureResults[i];
                if (TryMatchSequence(program, terms, index + 1, input, captureEnd, flags, captureState, out endIndex,
                        nestedInQuantifierContext))
                {
                    state.CopyFrom(captureState);
                    return true;
                }
            }

            endIndex = default;
            return false;
        }

        if (term is ScratchRegExpProgram.AlternationNode alternation)
        {
            using var checkpointLease = state.RentSibling(out var checkpoint);
            checkpoint.CopyFrom(state);
            foreach (var alternative in alternation.Alternatives)
            {
                state.CopyFrom(checkpoint);
                if (TryMatchNode(program, alternative, input, pos, flags, state, out var altNextPos,
                        nestedInQuantifierContext) &&
                    TryMatchSequence(program, terms, index + 1, input, altNextPos, flags, state, out endIndex,
                        nestedInQuantifierContext))
                    return true;
            }

            state.CopyFrom(checkpoint);
            endIndex = default;
            return false;
        }

        if (term is ScratchRegExpProgram.QuantifierNode quantifier)
            return TryMatchQuantifier(program, quantifier, input, pos, flags, state, terms, index + 1, out endIndex,
                nestedInQuantifierContext);

        using var fallbackCheckpointLease = state.RentSibling(out var fallbackCheckpoint);
        fallbackCheckpoint.CopyFrom(state);
        if (TryMatchNode(program, term, input, pos, flags, state, out var nextPos, nestedInQuantifierContext) &&
            TryMatchSequence(program, terms, index + 1, input, nextPos, flags, state, out endIndex,
                nestedInQuantifierContext))
            return true;

        state.CopyFrom(fallbackCheckpoint);
        endIndex = default;
        return false;
    }

    private static bool TryMatchAnchoredWholeStringSequence(
        IReadOnlyList<ScratchRegExpProgram.Node> terms,
        int index,
        string input,
        int pos,
        RegExpRuntimeFlags flags,
        ScratchMatchState state,
        out int endIndex)
    {
        if (index != 0 || pos != 0 || terms.Count != 3)
        {
            endIndex = default;
            return false;
        }

        if (terms[0] is not ScratchRegExpProgram.AnchorNode { Start: true } ||
            terms[1] is not ScratchRegExpProgram.QuantifierNode quantifier ||
            terms[2] is not ScratchRegExpProgram.AnchorNode { Start: false })
        {
            endIndex = default;
            return false;
        }

        var currentPos = 0;
        var consumed = 0;
        var currentFlags = flags;
        if (TryConsumeSimpleQuantifiedChildRun(quantifier.Child, input, currentPos, currentFlags, quantifier.Max,
                out var bulkEnd, out var bulkConsumed, out var bulkFlags))
        {
            currentPos = bulkEnd;
            consumed = bulkConsumed;
            currentFlags = bulkFlags;
        }
        else
        {
            while (consumed < quantifier.Max &&
                   TryConsumeSimpleQuantifiedChild(quantifier.Child, input, currentPos, currentFlags, out var nextPos,
                       out var nextFlags))
            {
                if (nextPos == currentPos)
                    break;

                consumed++;
                currentPos = nextPos;
                currentFlags = nextFlags;
            }
        }

        if (consumed < quantifier.Min || currentPos != input.Length)
        {
            endIndex = default;
            return false;
        }

        endIndex = currentPos;
        return true;
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

    private static void ConsumePropertyEscapeRun(
        string input,
        int pos,
        ScratchRegExpProgram.PropertyEscapeNode propertyEscape,
        RegExpRuntimeFlags flags,
        int maxCount,
        out int endPos,
        out int consumed)
    {
        var currentPos = pos;
        consumed = 0;

        while (consumed < maxCount && (uint)currentPos < (uint)input.Length)
        {
            int codePoint;
            int nextPos;

            if (propertyEscape.Kind == ScratchRegExpProgram.PropertyEscapeKind.StringProperty &&
                propertyEscape.PropertyValue is not null)
            {
                if (!ScratchUnicodeStringPropertyTables.TryMatchAt(propertyEscape.PropertyValue, input, currentPos,
                        out nextPos) ||
                    nextPos == currentPos)
                    break;
            }
            else if (flags.Unicode &&
                     currentPos + 1 < input.Length &&
                     char.IsHighSurrogate(input[currentPos]) &&
                     char.IsLowSurrogate(input[currentPos + 1]))
            {
                codePoint = char.ConvertToUtf32(input[currentPos], input[currentPos + 1]);
                nextPos = currentPos + 2;
                if (!FastPropertyEscapeMatches(propertyEscape, codePoint, flags))
                    break;
            }
            else
            {
                codePoint = input[currentPos];
                nextPos = currentPos + 1;
                if (!FastPropertyEscapeMatches(propertyEscape, codePoint, flags))
                    break;
            }

            consumed++;
            currentPos = nextPos;
        }

        endPos = currentPos;
    }

    private static bool FastPropertyEscapeMatches(ScratchRegExpProgram.PropertyEscapeNode propertyEscape, int codePoint,
        RegExpRuntimeFlags flags)
    {
        bool matched;
        switch (propertyEscape.Kind)
        {
            case ScratchRegExpProgram.PropertyEscapeKind.GeneralCategory:
                matched = ScratchUnicodeGeneralCategoryTables.Contains(propertyEscape.Categories, codePoint);
                break;
            case ScratchRegExpProgram.PropertyEscapeKind.Ascii:
                matched = codePoint <= 0x7F;
                break;
            case ScratchRegExpProgram.PropertyEscapeKind.Any:
                matched = true;
                break;
            case ScratchRegExpProgram.PropertyEscapeKind.Assigned:
                matched = !ScratchUnicodeGeneralCategoryTables.Contains(
                    ScratchRegExpProgram.GeneralCategoryMask.Unassigned, codePoint);
                break;
            case ScratchRegExpProgram.PropertyEscapeKind.Script:
                matched = propertyEscape.PropertyValue is not null &&
                          ScratchUnicodeScriptTables.Contains(propertyEscape.PropertyValue, codePoint);
                break;
            case ScratchRegExpProgram.PropertyEscapeKind.ScriptExtensions:
                matched = propertyEscape.PropertyValue is not null &&
                          ScratchUnicodeScriptExtensionsTables.Contains(propertyEscape.PropertyValue, codePoint);
                break;
            case ScratchRegExpProgram.PropertyEscapeKind.UppercaseLetter
                when propertyEscape.Negated && flags.IgnoreCase:
                return true;
            default:
                return PropertyEscapeMatches(propertyEscape, codePoint, flags);
        }

        return propertyEscape.Negated ? !matched : matched;
    }

    private static bool TryConsumeSimpleQuantifiedChild(
        ScratchRegExpProgram.Node node,
        string input,
        int pos,
        RegExpRuntimeFlags flags,
        out int nextPos,
        out RegExpRuntimeFlags nextFlags)
    {
        nextFlags = flags;
        switch (node)
        {
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                var scopedFlags = ApplyScopedModifiers(flags, scoped);
                if (TryConsumeSimpleQuantifiedChild(scoped.Child, input, pos, scopedFlags, out nextPos, out _))
                {
                    nextFlags = flags;
                    return true;
                }

                break;
            case ScratchRegExpProgram.LiteralNode literal:
                if (TryReadCodePoint(input, pos, flags.Unicode, out nextPos, out var literalCp) &&
                    CodePointEquals(literalCp, literal.CodePoint, flags.IgnoreCase))
                    return true;
                break;
            case ScratchRegExpProgram.DotNode:
                if (TryReadCodePoint(input, pos, flags.Unicode, out nextPos, out var dotCp) &&
                    (flags.DotAll || !IsLineTerminator(dotCp)))
                    return true;
                break;
            case ScratchRegExpProgram.ClassNode cls:
                if (TryMatchClassForward(input, pos, cls, flags, out nextPos)) return true;
                break;
            case ScratchRegExpProgram.PropertyEscapeNode propertyEscape:
                if (TryMatchPropertyEscapeForward(input, pos, propertyEscape, flags, out nextPos)) return true;
                break;
        }

        nextPos = default;
        nextFlags = flags;
        return false;
    }

    private static bool TryMatchSequenceBackward(ScratchRegExpProgram program,
        IReadOnlyList<ScratchRegExpProgram.Node> terms, int index,
        string input, int pos, RegExpRuntimeFlags flags, ScratchMatchState state, out int startIndex,
        bool nestedInQuantifierContext = false)
    {
        if (index < 0)
        {
            startIndex = pos;
            return true;
        }

        var term = terms[index];
        if (term is ScratchRegExpProgram.CaptureNode capture &&
            capture.Child is ScratchRegExpProgram.QuantifierNode captureQuantifier)
        {
            using var checkpointLease = state.RentSibling(out var checkpoint);
            checkpoint.CopyFrom(state);
            state.Ends[capture.Index] = pos;
            state.Matched[capture.Index] = true;
            if (TryMatchQuantifierBackward(program, captureQuantifier, input, pos, flags, state, terms, index - 1,
                    out startIndex, nestedInQuantifierContext, capture.Index))
                return true;

            state.CopyFrom(checkpoint);
            startIndex = default;
            return false;
        }

        if (term is ScratchRegExpProgram.CaptureNode)
        {
            var trustCaptureFastPath =
                input.Length > ExhaustiveBacktrackingInputLimit || CanUseFastBacktrackingPath(term);
            using var checkpointLease = state.RentSibling(out var checkpoint);
            checkpoint.CopyFrom(state);
            if (TryMatchNodeBackward(program, term, input, pos, flags, state, out var greedyPrevPos,
                    nestedInQuantifierContext) &&
                TryMatchSequenceBackward(program, terms, index - 1, input, greedyPrevPos, flags, state, out startIndex,
                    nestedInQuantifierContext) &&
                trustCaptureFastPath)
                return true;

            state.CopyFrom(checkpoint);
            using var captureResults = new ScratchPooledList<(int Pos, ScratchMatchState State)>();
            CollectNodeMatchesBackward(program, term, input, pos, flags, state, captureResults,
                nestedInQuantifierContext);
            for (var i = 0; i < captureResults.Count; i++)
            {
                var (captureStart, captureState) = captureResults[i];
                if (TryMatchSequenceBackward(program, terms, index - 1, input, captureStart, flags, captureState,
                        out startIndex, nestedInQuantifierContext))
                {
                    state.CopyFrom(captureState);
                    return true;
                }
            }

            startIndex = default;
            return false;
        }

        if (term is ScratchRegExpProgram.AlternationNode alternation)
        {
            using var checkpointLease = state.RentSibling(out var checkpoint);
            checkpoint.CopyFrom(state);
            foreach (var alternative in alternation.Alternatives)
            {
                state.CopyFrom(checkpoint);
                if (TryMatchNodeBackward(program, alternative, input, pos, flags, state, out var altPrevPos,
                        nestedInQuantifierContext) &&
                    TryMatchSequenceBackward(program, terms, index - 1, input, altPrevPos, flags, state, out startIndex,
                        nestedInQuantifierContext))
                    return true;
            }

            state.CopyFrom(checkpoint);
            startIndex = default;
            return false;
        }

        if (term is ScratchRegExpProgram.QuantifierNode quantifier)
            return TryMatchQuantifierBackward(program, quantifier, input, pos, flags, state, terms, index - 1,
                out startIndex, nestedInQuantifierContext);

        using var fallbackCheckpointLease = state.RentSibling(out var fallbackCheckpoint);
        fallbackCheckpoint.CopyFrom(state);
        if (TryMatchNodeBackward(program, term, input, pos, flags, state, out var prevPos, nestedInQuantifierContext) &&
            TryMatchSequenceBackward(program, terms, index - 1, input, prevPos, flags, state, out startIndex,
                nestedInQuantifierContext))
            return true;

        state.CopyFrom(fallbackCheckpoint);
        startIndex = default;
        return false;
    }

    private static bool TryMatchQuantifier(ScratchRegExpProgram program, ScratchRegExpProgram.QuantifierNode quantifier,
        string input, int pos,
        RegExpRuntimeFlags flags, ScratchMatchState state, IReadOnlyList<ScratchRegExpProgram.Node>? rest,
        int restIndex,
        out int endIndex, bool nestedInQuantifierContext = false, int? captureIndex = null)
    {
        using var fastPositions = new ScratchPooledList<(int Pos, ScratchMatchState State)>();
        var hasRestMinLength = TryComputeSequenceMinMatchLength(rest, restIndex, out var restMinLength);
        var hasRestLeadingLiteral = TryGetSequenceLeadingLiteral(rest, restIndex, out var restLeadingLiteral);
        var preferGreedyOrder = quantifier.Greedy || nestedInQuantifierContext;
        if (quantifier.Min == 0)
            fastPositions.Add((pos, state.Clone()));

        var currentPos = pos;
        var currentState = state.Clone();
        var consumed = 0;
        program.NodeCaptureIndices.TryGetValue(quantifier.Child, out var childCaptureIndices);
        while (consumed < quantifier.Max)
        {
            var stepState = currentState.Clone();
            if (childCaptureIndices is not null)
                for (var i = 0; i < childCaptureIndices.Length; i++)
                    stepState.ClearCapture(childCaptureIndices[i]);

            if (!TryMatchNode(program, quantifier.Child, input, currentPos, flags, stepState, out var childEnd, true))
                break;

            if (childEnd == currentPos)
            {
                consumed++;
                currentPos = childEnd;
                currentState = stepState;

                if (consumed == quantifier.Min)
                    fastPositions.Add((currentPos, currentState.Clone()));

                if (consumed < quantifier.Min)
                    continue;

                break;
            }

            consumed++;
            currentPos = childEnd;
            currentState = stepState;
            if (consumed >= quantifier.Min)
                fastPositions.Add((currentPos, currentState.Clone()));
        }

        bool TryRest(int candidatePos, ScratchMatchState candidateState, out int restEnd)
        {
            if (rest is null)
            {
                restEnd = candidatePos;
                return true;
            }

            return TryMatchSequence(program, rest, restIndex, input, candidatePos, flags, candidateState, out restEnd,
                nestedInQuantifierContext);
        }

        bool TryPositions(ScratchPooledList<(int Pos, ScratchMatchState State)> candidatePositions, bool reverseOrder,
            out int matchedEnd)
        {
            var start = reverseOrder ? candidatePositions.Count - 1 : 0;
            var end = reverseOrder ? -1 : candidatePositions.Count;
            var step = reverseOrder ? -1 : 1;
            using var workingStateLease = state.RentSibling(out var workingState);
            for (var i = start; i != end; i += step)
            {
                var (candidatePos, candidateStateSnapshot) = candidatePositions[i];
                if (hasRestMinLength && input.Length - candidatePos < restMinLength)
                    continue;

                if (hasRestLeadingLiteral && !StartsWithLiteralAt(input, candidatePos, restLeadingLiteral, flags))
                    continue;

                if (IsOnlyTrailingEndAnchors(rest, restIndex) && candidatePos != input.Length)
                    continue;

                workingState.CopyFrom(candidateStateSnapshot);
                if (captureIndex.HasValue)
                {
                    workingState.Starts[captureIndex.Value] = pos;
                    workingState.Ends[captureIndex.Value] = candidatePos;
                    workingState.Matched[captureIndex.Value] = true;
                }

                if (TryRest(candidatePos, workingState, out matchedEnd))
                {
                    state.CopyFrom(workingState);
                    return true;
                }
            }

            matchedEnd = default;
            return false;
        }

        bool TryCandidateState(int candidatePos, ScratchMatchState candidateStateSnapshot,
            ScratchMatchState workingState, out int matchedEnd)
        {
            if (hasRestMinLength && input.Length - candidatePos < restMinLength)
            {
                matchedEnd = default;
                return false;
            }

            if (hasRestLeadingLiteral && !StartsWithLiteralAt(input, candidatePos, restLeadingLiteral, flags))
            {
                matchedEnd = default;
                return false;
            }

            if (IsOnlyTrailingEndAnchors(rest, restIndex) && candidatePos != input.Length)
            {
                matchedEnd = default;
                return false;
            }

            workingState.CopyFrom(candidateStateSnapshot);
            if (captureIndex.HasValue)
            {
                workingState.Starts[captureIndex.Value] = pos;
                workingState.Ends[captureIndex.Value] = candidatePos;
                workingState.Matched[captureIndex.Value] = true;
            }

            if (TryRest(candidatePos, workingState, out matchedEnd))
            {
                state.CopyFrom(workingState);
                return true;
            }

            return false;
        }

        var trustFastPath = input.Length > ExhaustiveBacktrackingInputLimit ||
                            CanUseFastBacktrackingPath(quantifier.Child);
        if (trustFastPath && TryPositions(fastPositions, preferGreedyOrder, out endIndex))
            return true;

        if (input.Length > ExhaustiveBacktrackingInputLimit)
        {
            endIndex = default;
            return false;
        }

        bool TryExhaustiveOrdered(int currentCandidatePos, ScratchMatchState currentCandidateState, int consumedCount,
            out int matchedEnd)
        {
            using var candidateWorkingStateLease = state.RentSibling(out var candidateWorkingState);
            if (!preferGreedyOrder &&
                consumedCount >= quantifier.Min &&
                TryCandidateState(currentCandidatePos, currentCandidateState, candidateWorkingState, out matchedEnd))
                return true;

            if (consumedCount < quantifier.Max)
            {
                using var stepStateLease = state.RentSibling(out var stepState);
                stepState.CopyFrom(currentCandidateState);
                if (childCaptureIndices is not null)
                    for (var i = 0; i < childCaptureIndices.Length; i++)
                        stepState.ClearCapture(childCaptureIndices[i]);

                using var childResults = new ScratchPooledList<(int Pos, ScratchMatchState State)>();
                CollectNodeMatchesForward(program, quantifier.Child, input, currentCandidatePos, flags, stepState,
                    childResults, true);

                for (var i = 0; i < childResults.Count; i++)
                {
                    var (childPos, childState) = childResults[i];
                    if (childPos == currentCandidatePos)
                    {
                        if (consumedCount < quantifier.Min &&
                            TryExhaustiveOrdered(childPos, childState, consumedCount + 1, out matchedEnd))
                            return true;

                        continue;
                    }

                    if (TryExhaustiveOrdered(childPos, childState, consumedCount + 1, out matchedEnd))
                        return true;
                }
            }

            if (preferGreedyOrder &&
                consumedCount >= quantifier.Min &&
                TryCandidateState(currentCandidatePos, currentCandidateState, candidateWorkingState, out matchedEnd))
                return true;

            matchedEnd = default;
            return false;
        }

        if (TryExhaustiveOrdered(pos, state.Clone(), 0, out endIndex))
            return true;

        endIndex = default;
        return false;
    }

    private static bool TryGetSequenceLeadingLiteral(IReadOnlyList<ScratchRegExpProgram.Node>? terms, int index,
        out int codePoint)
    {
        if (terms is null || index < 0 || index >= terms.Count)
        {
            codePoint = default;
            return false;
        }

        for (var i = index; i < terms.Count; i++)
        {
            if (TryGetLeadingLiteral(terms[i], out codePoint))
                return true;

            if (!IsZeroWidthNode(terms[i]))
                return false;
        }

        codePoint = default;
        return false;
    }

    private static bool TryGetLeadingLiteral(ScratchRegExpProgram.Node node, out int codePoint)
    {
        switch (node)
        {
            case ScratchRegExpProgram.LiteralNode literal:
                codePoint = literal.CodePoint;
                return true;
            case ScratchRegExpProgram.CaptureNode capture:
                return TryGetLeadingLiteral(capture.Child, out codePoint);
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                return TryGetLeadingLiteral(scoped.Child, out codePoint);
            case ScratchRegExpProgram.QuantifierNode quantifier when quantifier.Min > 0:
                return TryGetLeadingLiteral(quantifier.Child, out codePoint);
            case ScratchRegExpProgram.SequenceNode sequence:
                return TryGetSequenceLeadingLiteral(sequence.Terms, 0, out codePoint);
            default:
                codePoint = default;
                return false;
        }
    }

    private static bool TryComputeSequenceMinMatchLength(IReadOnlyList<ScratchRegExpProgram.Node>? terms, int index,
        out int minLength)
    {
        if (terms is null || index < 0 || index >= terms.Count)
        {
            minLength = 0;
            return true;
        }

        long sum = 0;
        for (var i = index; i < terms.Count; i++)
        {
            if (!TryComputeMinMatchLength(terms[i], out var termLength))
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

    private static bool TryComputeMinMatchLength(ScratchRegExpProgram.Node node, out int minLength)
    {
        switch (node)
        {
            case ScratchRegExpProgram.EmptyNode:
            case ScratchRegExpProgram.AnchorNode:
            case ScratchRegExpProgram.BoundaryNode:
            case ScratchRegExpProgram.LookaheadNode:
            case ScratchRegExpProgram.LookbehindNode:
                minLength = 0;
                return true;
            case ScratchRegExpProgram.LiteralNode:
            case ScratchRegExpProgram.DotNode:
            case ScratchRegExpProgram.ClassNode:
            case ScratchRegExpProgram.PropertyEscapeNode:
                minLength = 1;
                return true;
            case ScratchRegExpProgram.CaptureNode capture:
                return TryComputeMinMatchLength(capture.Child, out minLength);
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                return TryComputeMinMatchLength(scoped.Child, out minLength);
            case ScratchRegExpProgram.SequenceNode sequence:
                return TryComputeSequenceMinMatchLength(sequence.Terms, 0, out minLength);
            case ScratchRegExpProgram.AlternationNode alternation:
            {
                if (alternation.Alternatives.Length == 0)
                {
                    minLength = 0;
                    return true;
                }

                var best = int.MaxValue;
                for (var i = 0; i < alternation.Alternatives.Length; i++)
                {
                    if (!TryComputeMinMatchLength(alternation.Alternatives[i], out var alternativeLength))
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
            case ScratchRegExpProgram.QuantifierNode quantifier:
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

    private static bool IsZeroWidthNode(ScratchRegExpProgram.Node node)
    {
        return node is ScratchRegExpProgram.EmptyNode
            or ScratchRegExpProgram.AnchorNode
            or ScratchRegExpProgram.BoundaryNode
            or ScratchRegExpProgram.LookaheadNode
            or ScratchRegExpProgram.LookbehindNode;
    }

    private static bool StartsWithLiteralAt(string input, int pos, int literalCodePoint, RegExpRuntimeFlags flags)
    {
        return TryReadCodePoint(input, pos, flags.Unicode, out _, out var cp) &&
               CodePointEquals(cp, literalCodePoint, flags.IgnoreCase);
    }

    private static void CollectNodeMatchesForward(
        ScratchRegExpProgram program,
        ScratchRegExpProgram.Node node,
        string input,
        int pos,
        RegExpRuntimeFlags flags,
        ScratchMatchState state,
        ScratchPooledList<(int Pos, ScratchMatchState State)> results,
        bool nestedInQuantifierContext = false)
    {
        switch (node)
        {
            case ScratchRegExpProgram.SequenceNode sequence:
                CollectSequenceMatchesForward(program, sequence.Terms, 0, input, pos, flags, state, results,
                    nestedInQuantifierContext);
                return;
            case ScratchRegExpProgram.AlternationNode alternation:
                foreach (var alternative in alternation.Alternatives)
                    CollectNodeMatchesForward(program, alternative, input, pos, flags, state.Clone(), results,
                        nestedInQuantifierContext);
                return;
            case ScratchRegExpProgram.CaptureNode capture:
            {
                var captureState = state.Clone();
                captureState.Starts[capture.Index] = pos;
                captureState.Matched[capture.Index] = true;
                using var childResults = new ScratchPooledList<(int Pos, ScratchMatchState State)>();
                CollectNodeMatchesForward(program, capture.Child, input, pos, flags, captureState, childResults,
                    nestedInQuantifierContext);
                for (var i = 0; i < childResults.Count; i++)
                {
                    var (childPos, childStateSnapshot) = childResults[i];
                    var childState = childStateSnapshot;
                    childState.Ends[capture.Index] = childPos;
                    results.Add((childPos, childState));
                }

                return;
            }
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                CollectNodeMatchesForward(program, scoped.Child, input, pos, ApplyScopedModifiers(flags, scoped), state,
                    results, nestedInQuantifierContext);
                return;
            case ScratchRegExpProgram.QuantifierNode quantifier:
                CollectQuantifierMatchesForward(program, quantifier, input, pos, flags, state, results,
                    nestedInQuantifierContext);
                return;
        }

        var snapshot = state.Clone();
        if (TryMatchNode(program, node, input, pos, flags, snapshot, out var nextPos, nestedInQuantifierContext))
            results.Add((nextPos, snapshot));
    }

    private static void CollectSequenceMatchesForward(
        ScratchRegExpProgram program,
        IReadOnlyList<ScratchRegExpProgram.Node> terms,
        int index,
        string input,
        int pos,
        RegExpRuntimeFlags flags,
        ScratchMatchState state,
        ScratchPooledList<(int Pos, ScratchMatchState State)> results,
        bool nestedInQuantifierContext)
    {
        if (index >= terms.Count)
        {
            results.Add((pos, state.Clone()));
            return;
        }

        using var termResults = new ScratchPooledList<(int Pos, ScratchMatchState State)>();
        CollectNodeMatchesForward(program, terms[index], input, pos, flags, state, termResults,
            nestedInQuantifierContext);
        for (var i = 0; i < termResults.Count; i++)
        {
            var (nextPos, nextState) = termResults[i];
            CollectSequenceMatchesForward(program, terms, index + 1, input, nextPos, flags, nextState, results,
                nestedInQuantifierContext);
        }
    }

    private static void CollectQuantifierMatchesForward(
        ScratchRegExpProgram program,
        ScratchRegExpProgram.QuantifierNode quantifier,
        string input,
        int pos,
        RegExpRuntimeFlags flags,
        ScratchMatchState state,
        ScratchPooledList<(int Pos, ScratchMatchState State)> results,
        bool nestedInQuantifierContext)
    {
        program.NodeCaptureIndices.TryGetValue(quantifier.Child, out var childCaptureIndices);

        void Recurse(int currentPos, ScratchMatchState currentState, int consumed)
        {
            if (!quantifier.Greedy && consumed >= quantifier.Min)
                results.Add((currentPos, currentState));

            if (consumed < quantifier.Max)
            {
                var stepState = currentState.Clone();
                if (childCaptureIndices is not null)
                    for (var i = 0; i < childCaptureIndices.Length; i++)
                        stepState.ClearCapture(childCaptureIndices[i]);

                using var childResults = new ScratchPooledList<(int Pos, ScratchMatchState State)>();
                CollectNodeMatchesForward(program, quantifier.Child, input, currentPos, flags, stepState, childResults,
                    true);
                for (var i = 0; i < childResults.Count; i++)
                {
                    var (childPos, childState) = childResults[i];
                    if (childPos == currentPos)
                    {
                        if (consumed + 1 >= quantifier.Min)
                            results.Add((childPos, childState));
                        continue;
                    }

                    Recurse(childPos, childState, consumed + 1);
                }
            }

            if (quantifier.Greedy && consumed >= quantifier.Min)
                results.Add((currentPos, currentState));
        }

        Recurse(pos, state.Clone(), 0);
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

    private static void CollectNodeMatchesBackward(
        ScratchRegExpProgram program,
        ScratchRegExpProgram.Node node,
        string input,
        int pos,
        RegExpRuntimeFlags flags,
        ScratchMatchState state,
        ScratchPooledList<(int Pos, ScratchMatchState State)> results,
        bool nestedInQuantifierContext = false)
    {
        var snapshot = state.Clone();
        if (TryMatchNodeBackward(program, node, input, pos, flags, snapshot, out var prevPos,
                nestedInQuantifierContext))
            results.Add((prevPos, snapshot));
    }

    private static bool IsOnlyTrailingEndAnchors(IReadOnlyList<ScratchRegExpProgram.Node>? rest, int restIndex)
    {
        if (rest is null || restIndex < 0 || restIndex >= rest.Count)
            return false;

        for (var i = restIndex; i < rest.Count; i++)
            if (rest[i] is not ScratchRegExpProgram.AnchorNode { Start: false })
                return false;

        return true;
    }

    private static bool TryMatchQuantifierBackward(ScratchRegExpProgram program,
        ScratchRegExpProgram.QuantifierNode quantifier, string input,
        int pos, RegExpRuntimeFlags flags, ScratchMatchState state, IReadOnlyList<ScratchRegExpProgram.Node>? rest,
        int restIndex,
        out int startIndex, bool nestedInQuantifierContext = false, int? captureIndex = null)
    {
        using var positions = new ScratchPooledList<(int Pos, ScratchMatchState State)>();
        positions.Add((pos, state.Clone()));

        var currentPos = pos;
        var currentState = state.Clone();
        var consumed = 0;
        program.NodeCaptureIndices.TryGetValue(quantifier.Child, out var childCaptureIndices);
        while (consumed < quantifier.Max)
        {
            var stepState = currentState.Clone();
            if (childCaptureIndices is not null)
                for (var i = 0; i < childCaptureIndices.Length; i++)
                    stepState.ClearCapture(childCaptureIndices[i]);

            if (!TryMatchNodeBackward(program, quantifier.Child, input, currentPos, flags, stepState,
                    out var childStart, true))
                break;

            if (childStart == currentPos)
            {
                if (quantifier.Min > 0 && consumed + 1 >= quantifier.Min)
                {
                    consumed++;
                    currentPos = childStart;
                    currentState = stepState;
                    positions.Add((currentPos, currentState.Clone()));
                }

                break;
            }

            consumed++;
            currentPos = childStart;
            currentState = stepState;
            positions.Add((currentPos, currentState.Clone()));
        }

        bool TryRest(int candidatePos, ScratchMatchState candidateState, out int restStart)
        {
            if (rest is null)
            {
                restStart = candidatePos;
                return true;
            }

            return TryMatchSequenceBackward(program, rest, restIndex, input, candidatePos, flags, candidateState,
                out restStart,
                nestedInQuantifierContext);
        }

        if (quantifier.Greedy || nestedInQuantifierContext)
        {
            var workingState = state.CreateSibling();
            for (var i = positions.Count - 1; i >= 0; i--)
            {
                if (i < quantifier.Min)
                    continue;

                workingState.CopyFrom(positions[i].State);
                if (captureIndex.HasValue)
                {
                    workingState.Starts[captureIndex.Value] = positions[i].Pos;
                    workingState.Ends[captureIndex.Value] = pos;
                    workingState.Matched[captureIndex.Value] = true;
                }

                if (TryRest(positions[i].Pos, workingState, out startIndex))
                {
                    state.CopyFrom(workingState);
                    return true;
                }
            }
        }
        else
        {
            var workingState = state.CreateSibling();
            for (var i = 0; i < positions.Count; i++)
            {
                if (i < quantifier.Min)
                    continue;

                workingState.CopyFrom(positions[i].State);
                if (captureIndex.HasValue)
                {
                    workingState.Starts[captureIndex.Value] = positions[i].Pos;
                    workingState.Ends[captureIndex.Value] = pos;
                    workingState.Matched[captureIndex.Value] = true;
                }

                if (TryRest(positions[i].Pos, workingState, out startIndex))
                {
                    state.CopyFrom(workingState);
                    return true;
                }
            }
        }

        startIndex = default;
        return false;
    }

    [Conditional("OKOJO_REGEXP_TRACE")]
    private static void Trace(string message)
    {
        Console.Error.WriteLine(message);
    }

    private static bool TryMatchBackReference(ScratchRegExpProgram.BackReferenceNode backReference, string input,
        int pos,
        RegExpRuntimeFlags flags, ScratchMatchState state, out int endIndex)
    {
        if (!state.Matched[backReference.Index])
        {
            endIndex = pos;
            return true;
        }

        var start = state.Starts[backReference.Index];
        var length = state.Ends[backReference.Index] - start;
        if (length == 0)
        {
            endIndex = pos;
            return true;
        }

        if (pos + length > input.Length)
        {
            endIndex = default;
            return false;
        }

        if (flags.IgnoreCase)
        {
            if (flags.Unicode)
            {
                var leftPos = start;
                var rightPos = pos;
                var leftEnd = start + length;
                var rightEnd = pos + length;
                while (leftPos < leftEnd)
                {
                    if (!TryReadCodePoint(input, leftPos, true, out var nextLeftPos, out var leftCodePoint) ||
                        !TryReadCodePoint(input, rightPos, true, out var nextRightPos, out var rightCodePoint) ||
                        CanonicalizeCodePoint(leftCodePoint) != CanonicalizeCodePoint(rightCodePoint))
                    {
                        endIndex = default;
                        return false;
                    }

                    leftPos = nextLeftPos;
                    rightPos = nextRightPos;
                }
            }
            else
            {
                for (var i = 0; i < length; i++)
                    if (char.ToLowerInvariant(input[start + i]) != char.ToLowerInvariant(input[pos + i]))
                    {
                        endIndex = default;
                        return false;
                    }
            }

            endIndex = pos + length;
            return true;
        }

        for (var i = 0; i < length; i++)
        {
            var expected = input[start + i];
            var actual = input[pos + i];
            if (expected != actual)
            {
                endIndex = default;
                return false;
            }
        }

        endIndex = pos + length;
        return true;
    }

    private static bool TryMatchNamedBackReference(ScratchRegExpProgram program,
        ScratchRegExpProgram.NamedBackReferenceNode backReference,
        string input, int pos, RegExpRuntimeFlags flags, ScratchMatchState state, out int endIndex)
    {
        if (!program.NamedCaptureIndexes.TryGetValue(backReference.Name, out var indexes))
        {
            endIndex = pos;
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
            if (TryMatchBackReference(numericBackReference, input, pos, flags, state, out endIndex))
                return true;
        }

        if (!anyMatched)
        {
            endIndex = pos;
            return true;
        }

        endIndex = default;
        return false;
    }

    private static bool TryMatchBackReferenceBackward(ScratchRegExpProgram.BackReferenceNode backReference,
        string input, int pos,
        RegExpRuntimeFlags flags, ScratchMatchState state, out int startIndex)
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
        if (candidateStart < 0)
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
        ScratchMatchState state, out int startIndex)
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
            if (TryMatchBackReferenceBackward(numericBackReference, input, pos, flags, state, out startIndex))
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

    private static int NextSearchPosition(ScratchRegExpProgram program, string input, int pos)
    {
        if (pos >= input.Length)
            return input.Length + 1;

        var nextPos = AdvanceStringIndex(input, pos, program.Flags.Unicode);
        return FindSearchCandidate(program, input, nextPos);
    }

    private static int FindSearchCandidate(ScratchRegExpProgram program, string input, int start)
    {
        if (start > input.Length)
            return input.Length + 1;

        if (program.MinMatchLength > 0 && input.Length - start < program.MinMatchLength)
            return input.Length + 1;

        if (program.CandidateSearchPlan.AnchorKind != ScratchRegExpProgram.SearchAnchorKind.None)
            return FindAnchoredSearchCandidate(program, input, start);

        if (program.RequiredLiteralPrefixCodePoints.Length == 0)
            return FindSearchPlanCandidate(program, input, start);

        if (!program.Flags.IgnoreCase &&
            program.RequiredLiteralPrefixText is { Length: > 0 } prefixText)
        {
            var textCandidate = input.IndexOf(prefixText, start, StringComparison.Ordinal);
            return textCandidate < 0 ? input.Length + 1 : textCandidate;
        }

        var candidate = FindNextRequiredPrefixCandidate(
            input,
            start,
            program.RequiredLiteralPrefixCodePoints,
            program.Flags.Unicode,
            program.Flags.IgnoreCase);
        return candidate < 0 ? input.Length + 1 : candidate;
    }

    private static int FindAnchoredSearchCandidate(ScratchRegExpProgram program, string input, int start)
    {
        switch (program.CandidateSearchPlan.AnchorKind)
        {
            case ScratchRegExpProgram.SearchAnchorKind.Start:
                if (start > 0)
                    return input.Length + 1;
                 return IsPlausibleSearchCandidate(program, input, 0) ? 0 : input.Length + 1;
            case ScratchRegExpProgram.SearchAnchorKind.LineStart:
                for (var candidate = FindNextLineStart(input, start); candidate <= input.Length;
                     candidate = FindNextLineStart(input, candidate + 1))
                    if (input.Length - candidate >= program.MinMatchLength &&
                        IsPlausibleSearchCandidate(program, input, candidate))
                        return candidate;
                return input.Length + 1;
            default:
                return FindSearchPlanCandidate(program, input, start);
        }
    }

    private static int FindSearchPlanCandidate(ScratchRegExpProgram program, string input, int start)
    {
        if (!program.CandidateSearchPlan.HasAtom)
            return start;

        switch (program.CandidateSearchPlan.AtomKind)
        {
            case ScratchRegExpProgram.SearchAtomKind.LiteralSet:
                return FindNextLiteralSetCandidate(input, start, program.CandidateSearchPlan.LiteralCodePoints,
                    program.Flags.Unicode, program.Flags.IgnoreCase);
            case ScratchRegExpProgram.SearchAtomKind.Class:
            case ScratchRegExpProgram.SearchAtomKind.PropertyEscape:
            case ScratchRegExpProgram.SearchAtomKind.Dot:
            case ScratchRegExpProgram.SearchAtomKind.Any:
                for (var pos = Math.Max(0, start); pos <= input.Length;)
                {
                    if (input.Length - pos < program.MinMatchLength)
                        return input.Length + 1;
                    if (MatchesSearchAtomAt(program, input, pos))
                        return pos;
                    if (pos == input.Length)
                        break;
                    pos = AdvanceStringIndex(input, pos, program.Flags.Unicode);
                }

                return input.Length + 1;
            default:
                return start;
        }
    }

    private static bool IsPlausibleSearchCandidate(ScratchRegExpProgram program, string input, int pos)
    {
        if (program.RequiredLiteralPrefixCodePoints.Length != 0 &&
            !HasRequiredLiteralPrefixAt(input, pos, program.RequiredLiteralPrefixCodePoints, program.Flags.Unicode,
                program.Flags.IgnoreCase))
            return false;

        return !program.CandidateSearchPlan.HasAtom || MatchesSearchAtomAt(program, input, pos);
    }

    private static bool MatchesSearchAtomAt(ScratchRegExpProgram program, string input, int pos)
    {
        switch (program.CandidateSearchPlan.AtomKind)
        {
            case ScratchRegExpProgram.SearchAtomKind.None:
                return true;
            case ScratchRegExpProgram.SearchAtomKind.LiteralSet:
                if (!TryReadCodePoint(input, pos, program.Flags.Unicode, out _, out var cp))
                    return false;
                return MatchesLiteralSet(cp, program.CandidateSearchPlan.LiteralCodePoints, program.Flags.IgnoreCase);
            case ScratchRegExpProgram.SearchAtomKind.Class:
                return TryMatchClassForward(input, pos, program.CandidateSearchPlan.Class!, program.Flags, out _);
            case ScratchRegExpProgram.SearchAtomKind.PropertyEscape:
                return TryMatchPropertyEscapeForward(input, pos, program.CandidateSearchPlan.PropertyEscape!,
                    program.Flags, out _);
            case ScratchRegExpProgram.SearchAtomKind.Dot:
                return TryReadCodePoint(input, pos, program.Flags.Unicode, out _, out var dotCodePoint) &&
                       !IsLineTerminator(dotCodePoint);
            case ScratchRegExpProgram.SearchAtomKind.Any:
                return TryReadCodePoint(input, pos, program.Flags.Unicode, out _, out _);
            default:
                return true;
        }
    }

    private static bool TryMatchExactLiteralAt(ExperimentalCompiledProgram compiledProgram, string input, int start,
        out int endIndex)
    {
        var program = compiledProgram.TreeProgram;
        if (!program.Flags.IgnoreCase &&
            program.ExactLiteralText is { Length: > 0 } exactLiteralText)
        {
            if (start <= input.Length &&
                input.AsSpan(start).StartsWith(exactLiteralText.AsSpan(), StringComparison.Ordinal))
            {
                endIndex = start + exactLiteralText.Length;
                return true;
            }

            endIndex = default;
            return false;
        }

        if (compiledProgram.BytecodeProgram is not null)
            return ExperimentalRegExpVm.TryMatch(program, compiledProgram.BytecodeProgram, input, start, program.Flags,
                null, out endIndex);

        var currentPos = start;
        var exactLiteral = program.ExactLiteralCodePoints;
        for (var i = 0; i < exactLiteral.Length; i++)
        {
            if (!TryReadCodePoint(input, currentPos, program.Flags.Unicode, out var nextPos, out var cp) ||
                !CodePointEquals(cp, exactLiteral[i], program.Flags.IgnoreCase))
            {
                endIndex = default;
                return false;
            }

            currentPos = nextPos;
        }

        endIndex = currentPos;
        return true;
    }

    internal static bool TryReadCodePointForVm(string input, int pos, bool unicode, out int nextPos, out int codePoint)
    {
        return TryReadCodePoint(input, pos, unicode, out nextPos, out codePoint);
    }

    internal static bool CodePointEqualsForVm(int left, int right, bool ignoreCase)
    {
        return CodePointEquals(left, right, ignoreCase);
    }

    internal static bool IsLineTerminatorForVm(int codePoint)
    {
        return IsLineTerminator(codePoint);
    }

    internal static bool IsWordBoundaryForVm(string input, int pos, bool unicode, bool ignoreCase)
    {
        return IsWordBoundary(input, pos, unicode, ignoreCase);
    }

    internal static bool IsStartAnchorSatisfiedForVm(string input, int pos, bool multiline)
    {
        return pos == 0 || (multiline && pos > 0 && IsLineTerminator(input[pos - 1]));
    }

    internal static bool IsEndAnchorSatisfiedForVm(string input, int pos, bool multiline)
    {
        return pos == input.Length || (multiline && pos < input.Length && IsLineTerminator(input[pos]));
    }

    internal static bool TryMatchClassForVm(string input, int pos, ScratchRegExpProgram.ClassNode cls,
        RegExpRuntimeFlags flags, out int nextPos)
    {
        return TryMatchClassForward(input, pos, cls, flags, out nextPos);
    }

    internal static bool TryMatchPropertyEscapeForVm(string input, int pos,
        ScratchRegExpProgram.PropertyEscapeNode propertyEscape, RegExpRuntimeFlags flags, out int nextPos)
    {
        return TryMatchPropertyEscapeForward(input, pos, propertyEscape, flags, out nextPos);
    }

    internal static bool TryMatchBackReferenceForVm(string input, int pos, int captureIndex, RegExpRuntimeFlags flags,
        bool ignoreCase, ExperimentalRegExpCaptureState? captureState, out int endIndex)
    {
        if (captureState is null || !captureState.Matched[captureIndex])
        {
            endIndex = pos;
            return true;
        }

        var start = captureState.Starts[captureIndex];
        var length = captureState.Ends[captureIndex] - start;
        if (length == 0)
        {
            endIndex = pos;
            return true;
        }

        if (pos + length > input.Length)
        {
            endIndex = default;
            return false;
        }

        if (ignoreCase)
        {
            if (flags.Unicode)
            {
                var leftPos = start;
                var rightPos = pos;
                var leftEnd = start + length;
                while (leftPos < leftEnd)
                {
                    if (!TryReadCodePoint(input, leftPos, true, out var nextLeftPos, out var leftCodePoint) ||
                        !TryReadCodePoint(input, rightPos, true, out var nextRightPos, out var rightCodePoint) ||
                        CanonicalizeCodePoint(leftCodePoint) != CanonicalizeCodePoint(rightCodePoint))
                    {
                        endIndex = default;
                        return false;
                    }

                    leftPos = nextLeftPos;
                    rightPos = nextRightPos;
                }
            }
            else
            {
                for (var i = 0; i < length; i++)
                    if (char.ToLowerInvariant(input[start + i]) != char.ToLowerInvariant(input[pos + i]))
                    {
                        endIndex = default;
                        return false;
                    }
            }

            endIndex = pos + length;
            return true;
        }

        for (var i = 0; i < length; i++)
            if (input[start + i] != input[pos + i])
            {
                endIndex = default;
                return false;
            }

        endIndex = pos + length;
        return true;
    }

    internal static bool TryMatchNamedBackReferenceForVm(string input, int pos, int[] captureIndexes,
        RegExpRuntimeFlags flags, bool ignoreCase, ExperimentalRegExpCaptureState? captureState, out int endIndex)
    {
        if (captureState is null)
        {
            endIndex = pos;
            return true;
        }

        var anyMatched = false;
        for (var i = captureIndexes.Length - 1; i >= 0; i--)
        {
            var captureIndex = captureIndexes[i];
            if (!captureState.Matched[captureIndex])
                continue;

            anyMatched = true;
            if (TryMatchBackReferenceForVm(input, pos, captureIndex, flags, ignoreCase, captureState, out endIndex))
                return true;
        }

        if (!anyMatched)
        {
            endIndex = pos;
            return true;
        }

        endIndex = default;
        return false;
    }

    internal static bool TryMatchLookbehindForVm(ScratchRegExpProgram program, ScratchRegExpProgram.Node child,
        string input, int pos, RegExpRuntimeFlags flags, ExperimentalRegExpCaptureState? captureState)
    {
        using var stateArena = program.CaptureCount == 0 ? null : new ScratchMatchStateArena(program.CaptureCount);
        var state = stateArena is null ? ScratchMatchState.Empty : stateArena.Root;
        if (captureState is not null)
            CopyVmCapturesToScratchState(captureState, state);

        if (!TryMatchNodeBackward(program, child, input, pos, flags, state, out _))
            return false;

        if (captureState is not null)
            CopyScratchStateToVmCaptures(state, captureState, program.CaptureCount);

        return true;
    }

    internal static int ScanDotToEndForVm(string input, int pos, bool unicode)
    {
        var currentPos = pos;
        while (TryReadCodePoint(input, currentPos, unicode, out var nextPos, out var codePoint) &&
               !IsLineTerminator(codePoint))
            currentPos = nextPos;

        return currentPos;
    }

    private static void CopyVmCapturesToScratchState(ExperimentalRegExpCaptureState source, ScratchMatchState destination)
    {
        for (var i = 0; i <= source.CaptureCount; i++)
        {
            destination.Starts[i] = source.Starts[i];
            destination.Ends[i] = source.Ends[i];
            destination.Matched[i] = source.Matched[i];
        }
    }

    private static void CopyScratchStateToVmCaptures(ScratchMatchState source, ExperimentalRegExpCaptureState destination,
        int captureCount)
    {
        for (var i = 0; i <= captureCount; i++)
        {
            if (!source.Matched[i])
            {
                if (destination.Matched[i])
                    destination.Clear(i);
                continue;
            }

            if (!destination.Matched[i] || destination.Starts[i] != source.Starts[i])
                destination.SaveStart(i, source.Starts[i]);
            if (!destination.Matched[i] || destination.Ends[i] != source.Ends[i])
                destination.SaveEnd(i, source.Ends[i]);
        }
    }

    internal static int ScanClassToEndForVm(string input, int pos, ScratchRegExpProgram.ClassNode cls,
        RegExpRuntimeFlags flags)
    {
        var currentPos = pos;
        while (TryMatchClassForward(input, currentPos, cls, flags, out var nextPos))
            currentPos = nextPos;

        return currentPos;
    }

    internal static bool TryMatchAsciiClassForVm(string input, int pos, ulong lowBitmap, ulong highBitmap, out int nextPos)
    {
        if ((uint)pos < (uint)input.Length && input[pos] <= 0x7F && MatchesAsciiBitmap(input[pos], lowBitmap, highBitmap))
        {
            nextPos = pos + 1;
            return true;
        }

        nextPos = default;
        return false;
    }

    internal static int ScanAsciiClassToEndForVm(string input, int pos, ulong lowBitmap, ulong highBitmap)
    {
        var currentPos = pos;
        while ((uint)currentPos < (uint)input.Length &&
               input[currentPos] <= 0x7F &&
               MatchesAsciiBitmap(input[currentPos], lowBitmap, highBitmap))
            currentPos++;

        return currentPos;
    }

    private static bool MatchesAsciiBitmap(char ch, ulong lowBitmap, ulong highBitmap)
    {
        return ch < 64
            ? (lowBitmap & (1UL << ch)) != 0
            : (highBitmap & (1UL << (ch - 64))) != 0;
    }

    private static int FindNextRequiredPrefixCandidate(string input, int start, int[] prefixCodePoints, bool unicode,
        bool ignoreCase)
    {
        if (prefixCodePoints.Length == 0)
            return Math.Max(0, start);

        for (var pos = Math.Max(0, start); pos < input.Length;)
        {
            var candidate = FindNextLiteralCandidate(input, pos, prefixCodePoints[0], unicode, ignoreCase);
            if (candidate < 0)
                return -1;

            if (HasRequiredLiteralPrefixAt(input, candidate, prefixCodePoints, unicode, ignoreCase))
                return candidate;

            pos = AdvanceStringIndex(input, candidate, unicode);
        }

        return -1;
    }

    private static int FindNextLiteralSetCandidate(string input, int start, int[] codePoints, bool unicode,
        bool ignoreCase)
    {
        if (codePoints.Length == 0)
            return Math.Max(0, start);
        if (codePoints.Length == 1)
        {
            var candidate = FindNextLiteralCandidate(input, Math.Max(0, start), codePoints[0], unicode, ignoreCase);
            return candidate < 0 ? input.Length + 1 : candidate;
        }

        for (var pos = Math.Max(0, start); pos < input.Length;)
        {
            if (TryReadCodePoint(input, pos, unicode, out _, out var cp) &&
                MatchesLiteralSet(cp, codePoints, ignoreCase))
                return pos;
            pos = AdvanceStringIndex(input, pos, unicode);
        }

        return input.Length + 1;
    }

    private static bool MatchesLiteralSet(int codePoint, int[] candidates, bool ignoreCase)
    {
        for (var i = 0; i < candidates.Length; i++)
            if (CodePointEquals(codePoint, candidates[i], ignoreCase))
                return true;

        return false;
    }

    private static int FindNextLineStart(string input, int start)
    {
        if (start <= 0)
            return 0;

        for (var i = Math.Max(1, start); i <= input.Length; i++)
            if (IsLineTerminator(input[i - 1]))
                return i;

        return input.Length + 1;
    }

    private static bool HasRequiredLiteralPrefixAt(string input, int pos, int[] prefixCodePoints, bool unicode,
        bool ignoreCase)
    {
        var currentPos = pos;
        for (var i = 0; i < prefixCodePoints.Length; i++)
        {
            if (!TryReadCodePoint(input, currentPos, unicode, out var nextPos, out var cp) ||
                !CodePointEquals(cp, prefixCodePoints[i], ignoreCase))
                return false;

            currentPos = nextPos;
        }

        return true;
    }

    private static int AdvanceStringIndex(string input, int index, bool unicode)
    {
        if (index >= input.Length)
            return index + 1;

        if (unicode &&
            index + 1 < input.Length &&
            char.IsHighSurrogate(input[index]) &&
            char.IsLowSurrogate(input[index + 1]))
            return index + 2;

        return index + 1;
    }

    private static int FindNextLiteralCandidate(string input, int start, int literalCodePoint, bool unicode,
        bool ignoreCase)
    {
        for (var pos = Math.Max(0, start); pos < input.Length;)
        {
            if (!TryReadCodePoint(input, pos, unicode, out var nextPos, out var cp))
                return -1;

            if (CodePointEquals(cp, literalCodePoint, ignoreCase))
                return pos;

            pos = nextPos;
        }

        return -1;
    }

    private static bool TryReadCodePoint(string input, int pos, bool unicode, out int nextPos, out int codePoint)
    {
        if ((uint)pos >= (uint)input.Length)
        {
            nextPos = default;
            codePoint = default;
            return false;
        }

        if (unicode && char.IsHighSurrogate(input[pos]) && pos + 1 < input.Length &&
            char.IsLowSurrogate(input[pos + 1]))
        {
            codePoint = char.ConvertToUtf32(input[pos], input[pos + 1]);
            nextPos = pos + 2;
            return true;
        }

        codePoint = input[pos];
        nextPos = pos + 1;
        return true;
    }

    private static bool CodePointEquals(int left, int right, bool ignoreCase)
    {
        if (!ignoreCase)
            return left == right;

        return CanonicalizeCodePoint(left) == CanonicalizeCodePoint(right);
    }

    private static bool CodePointInRange(int codePoint, int start, int end, bool ignoreCase)
    {
        if (!ignoreCase)
            return codePoint >= start && codePoint <= end;

        var canonicalCodePoint = CanonicalizeCodePoint(codePoint);
        var canonicalStart = CanonicalizeCodePoint(start);
        var canonicalEnd = CanonicalizeCodePoint(end);
        if (canonicalStart > canonicalEnd)
            (canonicalStart, canonicalEnd) = (canonicalEnd, canonicalStart);
        return canonicalCodePoint >= canonicalStart && canonicalCodePoint <= canonicalEnd;
    }

    private static int CanonicalizeCodePoint(int codePoint)
    {
        if ((uint)codePoint <= char.MaxValue)
            codePoint = char.ToLowerInvariant((char)codePoint);
        else if (Rune.TryCreate(codePoint, out var rune)) codePoint = Rune.ToLowerInvariant(rune).Value;

        return codePoint switch
        {
            0x017F => 's',
            0x212A => 'k',
            0xFB06 => 0xFB05,
            0x1FD3 => 0x0390,
            0x1FE3 => 0x03B0,
            _ => codePoint
        };
    }

    private static bool IsLineTerminator(char ch)
    {
        return ch is '\n' or '\r' or '\u2028' or '\u2029';
    }

    private static bool IsLineTerminator(int codePoint)
    {
        return codePoint is '\n' or '\r' or '\u2028' or '\u2029';
    }

    private static bool IsWordBoundary(string input, int pos, bool unicode, bool ignoreCase)
    {
        var before = pos > 0 && TryReadCodePointBackward(input, pos, unicode, out _, out var beforeCp) &&
                     IsWord(beforeCp, unicode, ignoreCase);
        var after = pos < input.Length && TryReadCodePoint(input, pos, unicode, out _, out var afterCp) &&
                    IsWord(afterCp, unicode, ignoreCase);
        return before != after;
    }

    private static bool TryReadCodePointBackward(string input, int pos, bool unicode, out int previousPos,
        out int codePoint)
    {
        if (pos <= 0)
        {
            previousPos = default;
            codePoint = default;
            return false;
        }

        var last = input[pos - 1];
        if (unicode && char.IsLowSurrogate(last) && pos >= 2 && char.IsHighSurrogate(input[pos - 2]))
        {
            previousPos = pos - 2;
            codePoint = char.ConvertToUtf32(input[pos - 2], last);
            return true;
        }

        previousPos = pos - 1;
        codePoint = last;
        return true;
    }

    private static bool IsWord(int codePoint, bool unicode, bool ignoreCase)
    {
        if (codePoint is >= '0' and <= '9' ||
            codePoint is >= 'A' and <= 'Z' ||
            codePoint is >= 'a' and <= 'z' ||
            codePoint == '_')
            return true;

        if (!unicode || !ignoreCase)
            return false;

        var canonical = CanonicalizeCodePoint(codePoint);
        return canonical is >= '0' and <= '9' ||
               canonical is >= 'A' and <= 'Z' ||
               canonical is >= 'a' and <= 'z' ||
               canonical == '_';
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

    private static bool TryMatchClassBackward(string input, int pos, ScratchRegExpProgram.ClassNode cls,
        RegExpRuntimeFlags flags, out int startIndex)
    {
        var hasCodePoint = TryReadCodePointBackward(input, pos, flags.Unicode, out var prevPos, out var codePoint);
        if (TryMatchSimpleClassBackward(cls, flags, hasCodePoint, prevPos, codePoint, out startIndex))
            return true;

        using var candidates = GetClassCandidatesBackward(input, pos, cls, flags, hasCodePoint, prevPos, codePoint);
        var bestStart = candidates.MaxOrDefault();

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
        for (var i = 0; i < cls.Items.Length; i++)
        {
            var item = cls.Items[i];
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
            foreach (var end in ScratchUnicodeStringPropertyTables.MatchEndsAt(item.PropertyValue, input, pos))
                results.Add(end);
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
            foreach (var start in
                     ScratchUnicodeStringPropertyTables.MatchStartsBackward(item.PropertyValue, input, pos))
                results.Add(start);
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

    private static bool TryMatchPropertyEscapeForward(string input, int pos,
        ScratchRegExpProgram.PropertyEscapeNode propertyEscape, RegExpRuntimeFlags flags, out int endIndex)
    {
        if (propertyEscape.Kind == ScratchRegExpProgram.PropertyEscapeKind.StringProperty &&
            propertyEscape.PropertyValue is not null)
            return ScratchUnicodeStringPropertyTables.TryMatchAt(propertyEscape.PropertyValue, input, pos,
                out endIndex);

        if (TryReadCodePoint(input, pos, flags.Unicode, out var nextPos, out var cp) &&
            PropertyEscapeMatches(propertyEscape, cp, flags))
        {
            endIndex = nextPos;
            return true;
        }

        endIndex = default;
        return false;
    }

    private static bool TryMatchPropertyEscapeBackward(string input, int pos,
        ScratchRegExpProgram.PropertyEscapeNode propertyEscape, RegExpRuntimeFlags flags, out int startIndex)
    {
        if (propertyEscape.Kind == ScratchRegExpProgram.PropertyEscapeKind.StringProperty &&
            propertyEscape.PropertyValue is not null)
            return ScratchUnicodeStringPropertyTables.TryMatchBackward(propertyEscape.PropertyValue, input, pos,
                out startIndex);

        if (TryReadCodePointBackward(input, pos, flags.Unicode, out var prevPos, out var cp) &&
            PropertyEscapeMatches(propertyEscape, cp, flags))
        {
            startIndex = prevPos;
            return true;
        }

        startIndex = default;
        return false;
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

    private static bool PropertyEscapeMatches(ScratchRegExpProgram.PropertyEscapeNode propertyEscape, int codePoint,
        RegExpRuntimeFlags flags)
    {
        return PropertyEscapeMatches(propertyEscape.Kind, propertyEscape.Negated, propertyEscape.Categories,
            propertyEscape.PropertyValue, codePoint, flags);
    }

    private static bool PropertyEscapeMatches(
        ScratchRegExpProgram.PropertyEscapeKind kind,
        bool negated,
        ScratchRegExpProgram.GeneralCategoryMask categories,
        string? propertyValue,
        int codePoint,
        RegExpRuntimeFlags flags)
    {
        if (negated && flags.IgnoreCase && kind == ScratchRegExpProgram.PropertyEscapeKind.UppercaseLetter)
            return true;

        var matched = kind switch
        {
            ScratchRegExpProgram.PropertyEscapeKind.GeneralCategory => MatchesGeneralCategory(codePoint, categories),
            ScratchRegExpProgram.PropertyEscapeKind.UppercaseLetter => MatchesUppercaseLetterProperty(codePoint,
                flags.IgnoreCase),
            ScratchRegExpProgram.PropertyEscapeKind.AsciiHexDigit =>
                IsHexDigitCodePoint(codePoint) && codePoint <= 0x7F,
            ScratchRegExpProgram.PropertyEscapeKind.HexDigit => IsHexDigitCodePoint(codePoint),
            ScratchRegExpProgram.PropertyEscapeKind.Ascii => codePoint <= 0x7F,
            ScratchRegExpProgram.PropertyEscapeKind.Assigned => IsAssignedCodePoint(codePoint),
            ScratchRegExpProgram.PropertyEscapeKind.Any => true,
            ScratchRegExpProgram.PropertyEscapeKind.Alphabetic or
                ScratchRegExpProgram.PropertyEscapeKind.GraphemeBase or
                ScratchRegExpProgram.PropertyEscapeKind.GraphemeExtend or
                ScratchRegExpProgram.PropertyEscapeKind.Ideographic or
                ScratchRegExpProgram.PropertyEscapeKind.JoinControl or
                ScratchRegExpProgram.PropertyEscapeKind.Cased or
                ScratchRegExpProgram.PropertyEscapeKind.CaseIgnorable or
                ScratchRegExpProgram.PropertyEscapeKind.BidiControl or
                ScratchRegExpProgram.PropertyEscapeKind.BidiMirrored or
                ScratchRegExpProgram.PropertyEscapeKind.Dash or
                ScratchRegExpProgram.PropertyEscapeKind.Deprecated or
                ScratchRegExpProgram.PropertyEscapeKind.IdsBinaryOperator or
                ScratchRegExpProgram.PropertyEscapeKind.IdsTrinaryOperator or
                ScratchRegExpProgram.PropertyEscapeKind.LogicalOrderException or
                ScratchRegExpProgram.PropertyEscapeKind.Lowercase or
                ScratchRegExpProgram.PropertyEscapeKind.ChangesWhenCaseMapped or
                ScratchRegExpProgram.PropertyEscapeKind.ChangesWhenCasefolded or
                ScratchRegExpProgram.PropertyEscapeKind.ChangesWhenLowercased or
                ScratchRegExpProgram.PropertyEscapeKind.ChangesWhenTitlecased or
                ScratchRegExpProgram.PropertyEscapeKind.ChangesWhenUppercased or
                ScratchRegExpProgram.PropertyEscapeKind.ChangesWhenNfkcCasefolded => ScratchUnicodePropertyTables
                    .Contains(kind, codePoint),
            ScratchRegExpProgram.PropertyEscapeKind.Diacritic or
                ScratchRegExpProgram.PropertyEscapeKind.EmojiComponent or
                ScratchRegExpProgram.PropertyEscapeKind.EmojiModifier or
                ScratchRegExpProgram.PropertyEscapeKind.EmojiModifierBase or
                ScratchRegExpProgram.PropertyEscapeKind.Emoji or
                ScratchRegExpProgram.PropertyEscapeKind.EmojiPresentation or
                ScratchRegExpProgram.PropertyEscapeKind.DefaultIgnorableCodePoint or
                ScratchRegExpProgram.PropertyEscapeKind.Extender or
                ScratchRegExpProgram.PropertyEscapeKind.ExtendedPictographic or
                ScratchRegExpProgram.PropertyEscapeKind.IdStart or
                ScratchRegExpProgram.PropertyEscapeKind.IdContinue or
                ScratchRegExpProgram.PropertyEscapeKind.XidStart or
                ScratchRegExpProgram.PropertyEscapeKind.XidContinue or
                ScratchRegExpProgram.PropertyEscapeKind.Math or
                ScratchRegExpProgram.PropertyEscapeKind.NoncharacterCodePoint or
                ScratchRegExpProgram.PropertyEscapeKind.PatternSyntax or
                ScratchRegExpProgram.PropertyEscapeKind.PatternWhiteSpace or
                ScratchRegExpProgram.PropertyEscapeKind.WhiteSpace or
                ScratchRegExpProgram.PropertyEscapeKind.VariationSelector or
                ScratchRegExpProgram.PropertyEscapeKind.Uppercase or
                ScratchRegExpProgram.PropertyEscapeKind.UnifiedIdeograph or
                ScratchRegExpProgram.PropertyEscapeKind.TerminalPunctuation or
                ScratchRegExpProgram.PropertyEscapeKind.SoftDotted or
                ScratchRegExpProgram.PropertyEscapeKind.SentenceTerminal or
                ScratchRegExpProgram.PropertyEscapeKind.QuotationMark or
                ScratchRegExpProgram.PropertyEscapeKind.Radical or
                ScratchRegExpProgram.PropertyEscapeKind.RegionalIndicator => ScratchUnicodePropertyTables.Contains(kind,
                    codePoint),
            ScratchRegExpProgram.PropertyEscapeKind.Script => propertyValue is not null &&
                                                              ScratchUnicodeScriptTables.Contains(propertyValue,
                                                                  codePoint),
            ScratchRegExpProgram.PropertyEscapeKind.ScriptExtensions => propertyValue is not null &&
                                                                        ScratchUnicodeScriptExtensionsTables.Contains(
                                                                            propertyValue, codePoint),
            ScratchRegExpProgram.PropertyEscapeKind.ScriptExtensionsUnknown => false,
            _ => false
        };

        return negated ? !matched : matched;
    }

    private static bool MatchesGeneralCategory(int codePoint, ScratchRegExpProgram.GeneralCategoryMask categories)
    {
        return ScratchUnicodeGeneralCategoryTables.Contains(categories, codePoint);
    }

    private static bool IsAssignedCodePoint(int codePoint)
    {
        return !ScratchUnicodeGeneralCategoryTables.Contains(ScratchRegExpProgram.GeneralCategoryMask.Unassigned,
            codePoint);
    }

    private static bool MatchesUppercaseLetterProperty(int codePoint, bool ignoreCase)
    {
        if (!ignoreCase)
            return MatchesGeneralCategory(codePoint, ScratchRegExpProgram.GeneralCategoryMask.UppercaseLetter);

        return MatchesGeneralCategory(
            codePoint,
            ScratchRegExpProgram.GeneralCategoryMask.UppercaseLetter |
            ScratchRegExpProgram.GeneralCategoryMask.LowercaseLetter |
            ScratchRegExpProgram.GeneralCategoryMask.TitlecaseLetter);
    }

    private static bool IsHexDigitCodePoint(int codePoint)
    {
        return codePoint is >= '0' and <= '9' ||
               codePoint is >= 'A' and <= 'F' ||
               codePoint is >= 'a' and <= 'f' ||
               codePoint is >= 0xFF10 and <= 0xFF19 ||
               codePoint is >= 0xFF21 and <= 0xFF26 ||
               codePoint is >= 0xFF41 and <= 0xFF46;
    }

    private static bool IsSpace(int codePoint)
    {
        return codePoint is '\t' or '\n' or '\r' or '\v' or '\f' or ' ' or '\u00A0' or '\u1680' or '\u2000' or '\u2001'
            or '\u2002' or
            '\u2003' or '\u2004' or '\u2005' or '\u2006' or '\u2007' or '\u2008' or '\u2009' or '\u200A' or '\u202F' or
            '\u2028' or '\u2029' or '\u205F' or '\u3000' or '\uFEFF';
    }
}

internal sealed class ScratchMatchStateArena : IDisposable
{
    private readonly int captureSlots;
    private readonly int stride;
    private int nextOffset;

    public ScratchMatchStateArena(int captureCount)
    {
        captureSlots = captureCount + 1;
        stride = captureSlots * 3;
        Buffer = ArrayPool<int>.Shared.Rent(Math.Max(stride * 8, stride));
        Array.Clear(Buffer, 0, stride);
        nextOffset = stride;
        Root = new(this, 0, captureSlots);
    }

    public ScratchMatchState Root { get; }

    internal int[] Buffer { get; private set; }

    public void Dispose()
    {
        ArrayPool<int>.Shared.Return(Buffer);
        Buffer = Array.Empty<int>();
        nextOffset = 0;
    }

    internal ScratchMatchState Clone(in ScratchMatchState source)
    {
        var offset = Allocate();
        Array.Copy(Buffer, source.BaseOffset, Buffer, offset, stride);
        return new(this, offset, captureSlots);
    }

    internal ScratchMatchState AllocateBlank()
    {
        var offset = Allocate();
        Array.Clear(Buffer, offset, stride);
        return new(this, offset, captureSlots);
    }

    internal ScratchMatchStateLease RentClone(in ScratchMatchState source, out ScratchMatchState state)
    {
        var mark = nextOffset;
        state = Clone(source);
        return new(this, mark);
    }

    internal ScratchMatchStateLease RentBlank(out ScratchMatchState state)
    {
        var mark = nextOffset;
        state = AllocateBlank();
        return new(this, mark);
    }

    public void Reset()
    {
        Array.Clear(Buffer, 0, stride);
        nextOffset = stride;
    }

    internal void Rewind(int mark)
    {
        if (Buffer.Length == 0)
            return;

        if (mark < stride)
            mark = stride;
        if (mark <= nextOffset)
            nextOffset = mark;
    }

    private int Allocate()
    {
        var offset = nextOffset;
        var required = offset + stride;
        if (required > Buffer.Length)
            Grow(required);
        nextOffset = required;
        return offset;
    }

    private void Grow(int minLength)
    {
        var newLength = Buffer.Length * 2;
        if (newLength < minLength)
            newLength = minLength;

        var newBuffer = ArrayPool<int>.Shared.Rent(newLength);
        Array.Copy(Buffer, newBuffer, nextOffset);
        ArrayPool<int>.Shared.Return(Buffer);
        Buffer = newBuffer;
    }
}

internal readonly struct ScratchMatchStateLease(ScratchMatchStateArena? arena, int mark) : IDisposable
{
    public void Dispose()
    {
        arena?.Rewind(mark);
    }
}

internal readonly struct ScratchMatchStateIntView(ScratchMatchStateArena? arena, int offset, int length)
{
    public int Length { get; } = length;

    public int this[int index]
    {
        get => arena is null ? 0 : arena.Buffer[offset + index];
        set
        {
            if (arena is not null)
                arena.Buffer[offset + index] = value;
        }
    }
}

internal readonly struct ScratchMatchStateBoolView(ScratchMatchStateArena? arena, int offset, int length)
{
    public int Length { get; } = length;

    public bool this[int index]
    {
        get => arena is not null && arena.Buffer[offset + index] != 0;
        set
        {
            if (arena is not null)
                arena.Buffer[offset + index] = value ? 1 : 0;
        }
    }
}

internal struct ScratchMatchState
{
    public static ScratchMatchState Empty { get; } = new(null, 0, 1);

    private readonly ScratchMatchStateArena? arena;
    private readonly int captureSlots;

    internal ScratchMatchState(ScratchMatchStateArena? arena, int baseOffset, int captureSlots)
    {
        this.arena = arena;
        this.captureSlots = captureSlots;
        BaseOffset = baseOffset;
        Starts = new(arena, baseOffset, captureSlots);
        Ends = new(arena, baseOffset + captureSlots, captureSlots);
        Matched = new(arena, baseOffset + captureSlots * 2, captureSlots);
    }

    internal int BaseOffset { get; }
    public ScratchMatchStateIntView Starts;
    public ScratchMatchStateIntView Ends;
    public ScratchMatchStateBoolView Matched;

    public ScratchMatchState Clone()
    {
        if (arena is null)
            return this;

        return arena.Clone(this);
    }

    public ScratchMatchState CreateSibling()
    {
        if (arena is null)
            return this;

        return arena.AllocateBlank();
    }

    public ScratchMatchStateLease RentClone(out ScratchMatchState clone)
    {
        if (arena is null)
        {
            clone = this;
            return default;
        }

        return arena.RentClone(this, out clone);
    }

    public ScratchMatchStateLease RentSibling(out ScratchMatchState sibling)
    {
        if (arena is null)
        {
            sibling = this;
            return default;
        }

        return arena.RentBlank(out sibling);
    }

    public void CopyFrom(ScratchMatchState other)
    {
        if (arena is null || (ReferenceEquals(arena, other.arena) && BaseOffset == other.BaseOffset))
            return;

        Array.Copy(other.arena!.Buffer, other.BaseOffset, arena.Buffer, BaseOffset, captureSlots * 3);
    }

    public void ClearCapture(int index)
    {
        if (arena is null)
            return;

        Starts[index] = 0;
        Ends[index] = 0;
        Matched[index] = false;
    }
}
