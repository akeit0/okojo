namespace Okojo.RegExp.Experimental;

internal static partial class ScratchRegExpMatcher
{
    private readonly struct ReverseLookbehindContext(
        ExperimentalCompiledProgram? compiledProgram,
        ScratchRegExpProgram program,
        string input,
        int startLimit)
    {
        public ExperimentalCompiledProgram? CompiledProgram { get; } = compiledProgram;
        public ScratchRegExpProgram Program { get; } = program;
        public string Input { get; } = input;
        public int StartLimit { get; } = startLimit;

        public ReverseLookbehindContext WithStartLimit(int startLimit)
        {
            return new(CompiledProgram, Program, Input, startLimit);
        }
    }

    private static bool TryMatchLookbehindAssertion(ExperimentalCompiledProgram? compiledProgram,
        ScratchRegExpProgram program, ScratchRegExpProgram.Node child,
        string input, int pos, RegExpRuntimeFlags flags, ScratchMatchState state, out int startIndex,
        int startLimit, bool nestedInQuantifierContext = false)
    {
        return TryMatchLookbehindNode(new(compiledProgram, program, input, startLimit), child, pos, flags, state,
            out startIndex,
            nestedInQuantifierContext);
    }

    private static bool TryMatchLookbehindNode(ReverseLookbehindContext context, ScratchRegExpProgram.Node node,
        int pos, RegExpRuntimeFlags flags, ScratchMatchState state, out int startIndex,
        bool nestedInQuantifierContext = false)
    {
        if (pos < context.StartLimit)
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
                if (TryReadCodePointBackward(context.Input, pos, flags.Unicode, out var prevPos, out var cp) &&
                    prevPos >= context.StartLimit &&
                    CodePointEquals(cp, literal.CodePoint, flags.IgnoreCase))
                {
                    startIndex = prevPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.DotNode:
                if (TryReadCodePointBackward(context.Input, pos, flags.Unicode, out prevPos, out cp) &&
                    prevPos >= context.StartLimit &&
                    (flags.DotAll || !IsLineTerminator(cp)))
                {
                    startIndex = prevPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.AnchorNode anchor:
                if (anchor.Start)
                {
                    if (pos == 0 || (flags.Multiline && pos > 0 && IsLineTerminator(context.Input[pos - 1])))
                    {
                        startIndex = pos;
                        return true;
                    }
                }
                else if (pos == context.Input.Length ||
                         (flags.Multiline && pos < context.Input.Length && IsLineTerminator(context.Input[pos])))
                {
                    startIndex = pos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.BoundaryNode boundary:
                var atBoundary = IsWordBoundary(context.Input, pos, flags.Unicode, flags.IgnoreCase);
                if (boundary.Positive == atBoundary)
                {
                    startIndex = pos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.ClassNode cls:
                if (TryMatchClassBackward(context.Input, pos, cls, flags, out prevPos, context.StartLimit))
                {
                    startIndex = prevPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.PropertyEscapeNode propertyEscape:
                if (TryMatchPropertyEscapeBackward(context.Input, pos, propertyEscape, flags, out prevPos,
                        context.StartLimit))
                {
                    startIndex = prevPos;
                    return true;
                }

                break;
            case ScratchRegExpProgram.BackReferenceNode backReference:
                if (TryMatchBackReferenceBackward(backReference, context.Input, pos, flags, state, out startIndex,
                        context.StartLimit))
                    return true;
                break;
            case ScratchRegExpProgram.NamedBackReferenceNode namedBackReference:
                if (TryMatchNamedBackReferenceBackward(context.Program, namedBackReference, context.Input, pos, flags,
                        state, out startIndex, context.StartLimit))
                    return true;
                break;
            case ScratchRegExpProgram.CaptureNode capture:
            {
                using var snapshotLease = state.RentClone(out var snapshot);
                snapshot.Ends[capture.Index] = pos;
                snapshot.Matched[capture.Index] = true;
                if (TryMatchLookbehindNode(context, capture.Child, pos, flags, snapshot, out var captureStart,
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
                if (TryMatchLookbehindNode(context, scoped.Child, pos, scopedFlags, state, out startIndex,
                        nestedInQuantifierContext))
                    return true;
                break;
            }
            case ScratchRegExpProgram.LookaheadNode lookahead:
            {
                using var snapshotLease = state.RentClone(out var snapshot);
                var matched = context.CompiledProgram is not null
                    ? TryMatchLookaheadAssertionForVm(context.CompiledProgram, lookahead.Child, context.Input, pos,
                        flags, snapshot)
                    : TryMatchNode(context.Program, lookahead.Child, context.Input, pos, flags, snapshot, out _,
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
                var nestedContext = context.WithStartLimit(GetLookbehindStartLimit(lookbehind.Child, pos));
                var matched = TryMatchLookbehindNode(nestedContext, lookbehind.Child, pos, flags, snapshot, out _,
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
                return TryMatchLookbehindSequence(context, sequence.Terms, sequence.Terms.Length - 1, pos, flags, state,
                    out startIndex, nestedInQuantifierContext);
            case ScratchRegExpProgram.AlternationNode alternation:
                foreach (var alternative in alternation.Alternatives)
                {
                    using var snapshotLease = state.RentClone(out var snapshot);
                    if (TryMatchLookbehindNode(context, alternative, pos, flags, snapshot, out startIndex,
                            nestedInQuantifierContext))
                    {
                        state.CopyFrom(snapshot);
                        return true;
                    }
                }

                break;
            case ScratchRegExpProgram.QuantifierNode quantifier:
                return TryMatchLookbehindQuantifier(context, quantifier, pos, flags, state, null, -1, out startIndex,
                    nestedInQuantifierContext);
        }

        startIndex = default;
        return false;
    }

    private static bool TryMatchLookbehindSequence(ReverseLookbehindContext context,
        IReadOnlyList<ScratchRegExpProgram.Node> terms, int index, int pos, RegExpRuntimeFlags flags,
        ScratchMatchState state, out int startIndex, bool nestedInQuantifierContext = false)
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
            if (TryMatchLookbehindQuantifier(context, captureQuantifier, pos, flags, state, terms, index - 1,
                    out startIndex, nestedInQuantifierContext, capture.Index))
                return true;

            state.CopyFrom(checkpoint);
            startIndex = default;
            return false;
        }

        if (term is ScratchRegExpProgram.CaptureNode)
        {
            var trustCaptureFastPath =
                context.Input.Length > ExhaustiveBacktrackingInputLimit || CanUseFastBacktrackingPath(term);
            using var checkpointLease = state.RentSibling(out var checkpoint);
            checkpoint.CopyFrom(state);
            if (TryMatchLookbehindNode(context, term, pos, flags, state, out var greedyPrevPos,
                    nestedInQuantifierContext) &&
                TryMatchLookbehindSequence(context, terms, index - 1, greedyPrevPos, flags, state, out startIndex,
                    nestedInQuantifierContext) &&
                trustCaptureFastPath)
                return true;

            state.CopyFrom(checkpoint);
            using var captureResults = new ScratchPooledList<(int Pos, ScratchMatchState State)>();
            CollectLookbehindNodeMatches(context, term, pos, flags, state, captureResults, nestedInQuantifierContext);
            for (var i = 0; i < captureResults.Count; i++)
            {
                var (captureStart, captureState) = captureResults[i];
                if (TryMatchLookbehindSequence(context, terms, index - 1, captureStart, flags, captureState,
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
                if (TryMatchLookbehindNode(context, alternative, pos, flags, state, out var altPrevPos,
                        nestedInQuantifierContext) &&
                    TryMatchLookbehindSequence(context, terms, index - 1, altPrevPos, flags, state, out startIndex,
                        nestedInQuantifierContext))
                    return true;
            }

            state.CopyFrom(checkpoint);
            startIndex = default;
            return false;
        }

        if (term is ScratchRegExpProgram.QuantifierNode quantifier)
            return TryMatchLookbehindQuantifier(context, quantifier, pos, flags, state, terms, index - 1,
                out startIndex, nestedInQuantifierContext);

        using var fallbackCheckpointLease = state.RentSibling(out var fallbackCheckpoint);
        fallbackCheckpoint.CopyFrom(state);
        if (TryMatchLookbehindNode(context, term, pos, flags, state, out var prevPos, nestedInQuantifierContext) &&
            TryMatchLookbehindSequence(context, terms, index - 1, prevPos, flags, state, out startIndex,
                nestedInQuantifierContext))
            return true;

        state.CopyFrom(fallbackCheckpoint);
        startIndex = default;
        return false;
    }

    private static void CollectLookbehindNodeMatches(ReverseLookbehindContext context, ScratchRegExpProgram.Node node,
        int pos, RegExpRuntimeFlags flags, ScratchMatchState state,
        ScratchPooledList<(int Pos, ScratchMatchState State)> results, bool nestedInQuantifierContext = false)
    {
        var snapshot = state.Clone();
        if (TryMatchLookbehindNode(context, node, pos, flags, snapshot, out var prevPos, nestedInQuantifierContext))
            results.Add((prevPos, snapshot));
    }

    private static bool TryMatchLookbehindQuantifier(ReverseLookbehindContext context,
        ScratchRegExpProgram.QuantifierNode quantifier, int pos, RegExpRuntimeFlags flags, ScratchMatchState state,
        IReadOnlyList<ScratchRegExpProgram.Node>? rest, int restIndex, out int startIndex,
        bool nestedInQuantifierContext = false, int? captureIndex = null)
    {
        using var positions = new ScratchPooledList<(int Pos, ScratchMatchState State)>();
        positions.Add((pos, state.Clone()));

        var currentPos = pos;
        var currentState = state.Clone();
        var consumed = 0;
        context.Program.NodeCaptureIndices.TryGetValue(quantifier.Child, out var childCaptureIndices);
        while (consumed < quantifier.Max)
        {
            var stepState = currentState.Clone();
            if (childCaptureIndices is not null)
                for (var i = 0; i < childCaptureIndices.Length; i++)
                    stepState.ClearCapture(childCaptureIndices[i]);

            if (!TryMatchLookbehindNode(context, quantifier.Child, currentPos, flags, stepState, out var childStart,
                    true))
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

            return TryMatchLookbehindSequence(context, rest, restIndex, candidatePos, flags, candidateState,
                out restStart, nestedInQuantifierContext);
        }

        if (quantifier.Greedy || nestedInQuantifierContext)
        {
            using var workingStateLease = state.RentSibling(out var workingState);
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
            using var workingStateLease = state.RentSibling(out var workingState);
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
}
