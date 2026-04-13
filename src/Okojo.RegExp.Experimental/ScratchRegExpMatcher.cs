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
                var lookbehindStartLimit = GetLookbehindStartLimit(lookbehind.Child, pos);
                var matched = TryMatchNodeBackward(program, lookbehind.Child, input, pos, flags, snapshot, out _,
                    nestedInQuantifierContext, lookbehindStartLimit);
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
        RegExpRuntimeFlags flags, ScratchMatchState state, out int startIndex, bool nestedInQuantifierContext = false,
        int startLimit = 0)
    {
        if (pos < startLimit)
        {
            startIndex = default;
            return false;
        }

        switch (node)
        {
            case ScratchRegExpProgram.EmptyNode:
                startIndex = pos;
                return true;
            case ScratchRegExpProgram.LiteralNode literal:
                if (TryReadCodePointBackward(input, pos, flags.Unicode, out var prevPos, out var cp) &&
                    prevPos >= startLimit &&
                    CodePointEquals(cp, literal.CodePoint, flags.IgnoreCase))
                {
                    startIndex = prevPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.DotNode:
                if (TryReadCodePointBackward(input, pos, flags.Unicode, out prevPos, out cp) &&
                    prevPos >= startLimit &&
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
                if (TryMatchClassBackward(input, pos, cls, flags, out prevPos, startLimit))
                {
                    startIndex = prevPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.PropertyEscapeNode propertyEscape:
                if (TryMatchPropertyEscapeBackward(input, pos, propertyEscape, flags, out prevPos, startLimit))
                {
                    startIndex = prevPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.BackReferenceNode backReference:
                if (TryMatchBackReferenceBackward(backReference, input, pos, flags, state, out startIndex, startLimit))
                    return true;
                break;
            case ScratchRegExpProgram.NamedBackReferenceNode namedBackReference:
                if (TryMatchNamedBackReferenceBackward(program, namedBackReference, input, pos, flags, state,
                        out startIndex, startLimit))
                    return true;
                break;
            case ScratchRegExpProgram.CaptureNode capture:
            {
                using var snapshotLease = state.RentClone(out var snapshot);
                snapshot.Ends[capture.Index] = pos;
                snapshot.Matched[capture.Index] = true;
                if (TryMatchNodeBackward(program, capture.Child, input, pos, flags, snapshot, out var captureStart,
                        nestedInQuantifierContext, startLimit))
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
                        nestedInQuantifierContext, startLimit))
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
                var lookbehindStartLimit = Math.Max(startLimit, GetLookbehindStartLimit(lookbehind.Child, pos));
                var matched = TryMatchNodeBackward(program, lookbehind.Child, input, pos, flags, snapshot, out _,
                    nestedInQuantifierContext, lookbehindStartLimit);
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
                    out startIndex, nestedInQuantifierContext, startLimit);
            case ScratchRegExpProgram.AlternationNode alternation:
                foreach (var alternative in alternation.Alternatives)
                {
                    using var snapshotLease = state.RentClone(out var snapshot);
                    if (TryMatchNodeBackward(program, alternative, input, pos, flags, snapshot, out startIndex,
                            nestedInQuantifierContext, startLimit))
                    {
                        state.CopyFrom(snapshot);
                        return true;
                    }
                }

                break;
            case ScratchRegExpProgram.QuantifierNode quantifier:
                return TryMatchQuantifierBackward(program, quantifier, input, pos, flags, state, null, -1,
                    out startIndex,
                    nestedInQuantifierContext, null, startLimit);
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
        bool nestedInQuantifierContext = false, int startLimit = 0)
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
                    out startIndex, nestedInQuantifierContext, capture.Index, startLimit))
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
                    nestedInQuantifierContext, startLimit) &&
                TryMatchSequenceBackward(program, terms, index - 1, input, greedyPrevPos, flags, state, out startIndex,
                    nestedInQuantifierContext, startLimit) &&
                trustCaptureFastPath)
                return true;

            state.CopyFrom(checkpoint);
            using var captureResults = new ScratchPooledList<(int Pos, ScratchMatchState State)>();
            CollectNodeMatchesBackward(program, term, input, pos, flags, state, captureResults,
                nestedInQuantifierContext, startLimit);
            for (var i = 0; i < captureResults.Count; i++)
            {
                var (captureStart, captureState) = captureResults[i];
                if (TryMatchSequenceBackward(program, terms, index - 1, input, captureStart, flags, captureState,
                        out startIndex, nestedInQuantifierContext, startLimit))
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
                        nestedInQuantifierContext, startLimit) &&
                    TryMatchSequenceBackward(program, terms, index - 1, input, altPrevPos, flags, state, out startIndex,
                        nestedInQuantifierContext, startLimit))
                    return true;
            }

            state.CopyFrom(checkpoint);
            startIndex = default;
            return false;
        }

        if (term is ScratchRegExpProgram.QuantifierNode quantifier)
            return TryMatchQuantifierBackward(program, quantifier, input, pos, flags, state, terms, index - 1,
                out startIndex, nestedInQuantifierContext, null, startLimit);

        using var fallbackCheckpointLease = state.RentSibling(out var fallbackCheckpoint);
        fallbackCheckpoint.CopyFrom(state);
        if (TryMatchNodeBackward(program, term, input, pos, flags, state, out var prevPos, nestedInQuantifierContext,
                startLimit) &&
            TryMatchSequenceBackward(program, terms, index - 1, input, prevPos, flags, state, out startIndex,
                nestedInQuantifierContext, startLimit))
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
        var hasRestMinLength = ScratchRegExpProgram.TryGetSequenceMinMatchLength(rest, restIndex, out var restMinLength);
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
        bool nestedInQuantifierContext = false,
        int startLimit = 0)
    {
        var snapshot = state.Clone();
        if (TryMatchNodeBackward(program, node, input, pos, flags, snapshot, out var prevPos,
                nestedInQuantifierContext, startLimit))
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
        out int startIndex, bool nestedInQuantifierContext = false, int? captureIndex = null, int startLimit = 0)
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
                    out var childStart, true, startLimit))
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
                nestedInQuantifierContext, startLimit);
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
