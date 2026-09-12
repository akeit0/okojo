using System.Buffers;
using System.Runtime.CompilerServices;
using Okojo.Text.Unicode;

namespace Okojo.Text.RegularExpressions.Internal;

internal static class BacktrackingVm
{
    private enum BacktrackKind : byte
    {
        Resume,
        GreedyScan,
        LazyScan,
    }

    private readonly struct BacktrackFrame
    {
        internal BacktrackFrame(
            BacktrackKind kind,
            int programCounter,
            int position,
            int captureTrail,
            int stateTrail,
            int scanId = -1,
            int count = 0
        )
        {
            Kind = kind;
            ProgramCounter = programCounter;
            Position = position;
            CaptureTrail = captureTrail;
            StateTrail = stateTrail;
            ScanId = scanId;
            Count = count;
        }

        internal BacktrackKind Kind { get; }
        internal int ProgramCounter { get; }
        internal int Position { get; }
        internal int CaptureTrail { get; }
        internal int StateTrail { get; }
        internal int ScanId { get; }
        internal int Count { get; }
    }

    private readonly record struct TrailEntry(int Slot, int PreviousValue);

    [SkipLocalsInit]
    internal static bool TrySearch(
        RegexProgram program,
        ReadOnlySpan<char> input,
        int startIndex,
        bool sticky,
        Span<int> captures,
        RegExpOptions options,
        out int endPosition
    )
    {
        int requiredCaptureSlots = checked((program.CaptureCount + 1) * 2);
        if (captures.Length < requiredCaptureSlots)
            throw new ArgumentException("Capture-register buffer is too small.", nameof(captures));

        int[]? rentedStates = null;
        Span<int> states =
            program.StateSlotCount <= 128
                ? stackalloc int[program.StateSlotCount]
                : (rentedStates = ArrayPool<int>.Shared.Rent(program.StateSlotCount)).AsSpan(
                    0,
                    program.StateSlotCount
                );

        Span<TrailEntry> initialCaptureTrail = stackalloc TrailEntry[256];
        Span<TrailEntry> initialStateTrail = stackalloc TrailEntry[128];
        var captureTrail = new ValueStack<TrailEntry>(initialCaptureTrail);
        var stateTrail = new ValueStack<TrailEntry>(initialStateTrail);
        var budget = new ExecutionBudget(options);

        try
        {
            bool unicode = (program.Flags & (RegExpFlags.Unicode | RegExpFlags.UnicodeSets)) != 0;
            SearchPlan plan = program.SearchPlan;

            if (sticky)
            {
                return TryCandidate(
                    program,
                    input,
                    startIndex,
                    captures,
                    states,
                    options,
                    ref captureTrail,
                    ref stateTrail,
                    ref budget,
                    out endPosition
                );
            }

            if (plan.Anchor == SearchAnchor.AbsoluteStart)
            {
                if (startIndex != 0)
                {
                    captures[..requiredCaptureSlots].Fill(-1);
                    endPosition = -1;
                    return false;
                }
                return TryCandidate(
                    program,
                    input,
                    0,
                    captures,
                    states,
                    options,
                    ref captureTrail,
                    ref stateTrail,
                    ref budget,
                    out endPosition
                );
            }

            int lastCandidate =
                plan.MinimumLength > input.Length ? -1 : input.Length - plan.MinimumLength;
            if (lastCandidate < startIndex)
            {
                captures[..requiredCaptureSlots].Fill(-1);
                endPosition = -1;
                return false;
            }

            if (plan.Anchor == SearchAnchor.LineStart)
            {
                int candidate = startIndex;
                while (candidate <= lastCandidate)
                {
                    bool lineStart = candidate == 0 || Utf16.IsLineTerminator(input[candidate - 1]);
                    if (
                        lineStart
                        && PrefixMatches(input, candidate, plan.Prefix)
                        && LeadingSetMatches(plan, input, candidate, unicode)
                        && TryCandidate(
                            program,
                            input,
                            candidate,
                            captures,
                            states,
                            options,
                            ref captureTrail,
                            ref stateTrail,
                            ref budget,
                            out endPosition
                        )
                    )
                    {
                        return true;
                    }
                    candidate = Utf16.AdvanceStringIndex(input, candidate, unicode);
                }
            }
            else if (plan.Prefix is { Length: > 0 } prefix)
            {
                int searchPosition = startIndex;
                while (searchPosition <= lastCandidate)
                {
                    int offset = input[searchPosition..]
                        .IndexOf(prefix.AsSpan(), StringComparison.Ordinal);
                    if (offset < 0)
                        break;
                    int candidate = searchPosition + offset;
                    if (candidate > lastCandidate)
                        break;

                    bool reachable =
                        !unicode
                        || candidate == startIndex
                        || candidate == 0
                        || !char.IsLowSurrogate(input[candidate])
                        || !char.IsHighSurrogate(input[candidate - 1]);
                    if (
                        reachable
                        && TryCandidate(
                            program,
                            input,
                            candidate,
                            captures,
                            states,
                            options,
                            ref captureTrail,
                            ref stateTrail,
                            ref budget,
                            out endPosition
                        )
                    )
                    {
                        return true;
                    }
                    searchPosition = Utf16.AdvanceStringIndex(input, candidate, unicode);
                }
            }
            else
            {
                int candidate = startIndex;

                // R8-irregexp: when the leading set contains exactly one BMP
                // code unit, scan directly with IndexOf instead of stepping
                // through every position. This mirrors V8 irregexp's
                // one-byte/rare-character pre-filter.
                var leadChar = plan.LeadingSet is { } ls ? ls.TryGetSingleBmp() : null;
                while (candidate <= lastCandidate)
                {
                    if (leadChar.HasValue)
                    {
                        int skip = input[candidate..].IndexOf(leadChar.Value);
                        if (skip < 0 || candidate + skip > lastCandidate)
                            break;
                        candidate += skip;
                    }
                    else if (!LeadingSetMatches(plan, input, candidate, unicode))
                    {
                        candidate = Utf16.AdvanceStringIndex(input, candidate, unicode);
                        continue;
                    }

                    if (
                        TryCandidate(
                            program,
                            input,
                            candidate,
                            captures,
                            states,
                            options,
                            ref captureTrail,
                            ref stateTrail,
                            ref budget,
                            out endPosition
                        )
                    )
                    {
                        return true;
                    }
                    candidate = Utf16.AdvanceStringIndex(input, candidate, unicode);
                }
            }

            captures[..requiredCaptureSlots].Fill(-1);
            endPosition = -1;
            return false;
        }
        finally
        {
            captureTrail.Dispose();
            stateTrail.Dispose();
            if (rentedStates is not null)
                ArrayPool<int>.Shared.Return(rentedStates, clearArray: false);
        }
    }

    [SkipLocalsInit]
    private static bool TryCandidate(
        RegexProgram program,
        ReadOnlySpan<char> input,
        int candidate,
        Span<int> captures,
        Span<int> states,
        RegExpOptions options,
        ref ValueStack<TrailEntry> captureTrail,
        ref ValueStack<TrailEntry> stateTrail,
        ref ExecutionBudget budget,
        out int endPosition
    )
    {
        captures.Fill(-1);
        states.Fill(-1);
        captureTrail.Clear();
        stateTrail.Clear();
        return RunSegment(
            program,
            segmentId: 0,
            input,
            candidate,
            captures,
            states,
            options,
            ref captureTrail,
            ref stateTrail,
            ref budget,
            assertionDepth: 0,
            out endPosition
        );
    }

    [SkipLocalsInit]
    private static bool RunSegment(
        RegexProgram program,
        int segmentId,
        ReadOnlySpan<char> input,
        int startPosition,
        Span<int> captures,
        Span<int> states,
        RegExpOptions options,
        ref ValueStack<TrailEntry> captureTrail,
        ref ValueStack<TrailEntry> stateTrail,
        ref ExecutionBudget budget,
        int assertionDepth,
        out int endPosition
    )
    {
        ProgramSegment segment = program.Segments[segmentId];
        ReadOnlySpan<Instruction> code = segment.Code;
        int baseCaptureTrail = captureTrail.Count;
        int baseStateTrail = stateTrail.Count;
        Span<BacktrackFrame> initialFrames = stackalloc BacktrackFrame[128];
        var frames = new ValueStack<BacktrackFrame>(initialFrames);
        Span<int> classCandidates = stackalloc int[128];
        Span<int> classCandidatesStack = classCandidates;
        int[]? rentedClassCandidates = null;
        int pc = 0;
        int position = startPosition;

        try
        {
            while (true)
            {
                budget.Step();
                bool failed = (uint)pc >= (uint)code.Length;
                Instruction instruction = failed ? new Instruction(OpCode.Fail) : code[pc];

                if (!failed)
                {
                    switch (instruction.Op)
                    {
                        case OpCode.Match:
                            endPosition = position;
                            return true;

                        case OpCode.Fail:
                            failed = true;
                            break;

                        case OpCode.Character:
                        {
                            NodeOptions nodeOptions = (NodeOptions)instruction.B;
                            bool unicode = (nodeOptions & NodeOptions.Unicode) != 0;
                            if (
                                !TryRead(
                                    segment.Direction,
                                    input,
                                    position,
                                    unicode,
                                    out int codePoint,
                                    out int width
                                ) || !CharacterEquals(codePoint, instruction.A, nodeOptions)
                            )
                            {
                                failed = true;
                                break;
                            }
                            position += segment.Direction * width;
                            pc++;
                            break;
                        }

                        case OpCode.CharacterClass:
                        {
                            NodeOptions nodeOptions = (NodeOptions)instruction.B;
                            bool unicode = (nodeOptions & NodeOptions.Unicode) != 0;
                            if (
                                !TryRead(
                                    segment.Direction,
                                    input,
                                    position,
                                    unicode,
                                    out int codePoint,
                                    out int width
                                )
                            )
                            {
                                failed = true;
                                break;
                            }
                            CharSet set = program.Classes[instruction.A];
                            bool matches = ClassMatches(set, codePoint, nodeOptions);
                            if ((nodeOptions & NodeOptions.InvertClass) != 0)
                                matches = !matches;
                            if (!matches)
                            {
                                failed = true;
                                break;
                            }
                            position += segment.Direction * width;
                            pc++;
                            break;
                        }

                        case OpCode.ClassSet:
                        {
                            NodeOptions nodeOptions = (NodeOptions)instruction.B;
                            if (segment.Direction < 0)
                            {
                                throw new InvalidOperationException(
                                    "String-capable classes are not supported in backward assertions."
                                );
                            }
                            bool unicode = (nodeOptions & NodeOptions.Unicode) != 0;
                            if (rentedClassCandidates is not null)
                            {
                                ArrayPool<int>.Shared.Return(
                                    rentedClassCandidates,
                                    clearArray: false
                                );
                                rentedClassCandidates = null;
                                classCandidates = classCandidatesStack;
                            }
                            int candidateCount = CollectClassSetCandidates(
                                program,
                                program.ClassSets[instruction.A],
                                input,
                                position,
                                unicode,
                                nodeOptions,
                                ref classCandidates,
                                ref rentedClassCandidates
                            );
                            if (candidateCount == 0)
                            {
                                failed = true;
                                break;
                            }
                            for (int i = candidateCount - 1; i >= 1; i--)
                            {
                                PushFrame(
                                    pc + 1,
                                    classCandidates[i],
                                    options,
                                    ref frames,
                                    captureTrail.Count,
                                    stateTrail.Count
                                );
                            }
                            position = classCandidates[0];
                            pc++;
                            break;
                        }

                        case OpCode.Any:
                        {
                            NodeOptions nodeOptions = (NodeOptions)instruction.B;
                            bool unicode = (nodeOptions & NodeOptions.Unicode) != 0;
                            if (
                                !TryRead(
                                    segment.Direction,
                                    input,
                                    position,
                                    unicode,
                                    out int codePoint,
                                    out int width
                                )
                                || (nodeOptions & NodeOptions.DotAll) == 0
                                    && Utf16.IsLineTerminator(codePoint)
                            )
                            {
                                failed = true;
                                break;
                            }
                            position += segment.Direction * width;
                            pc++;
                            break;
                        }

                        case OpCode.Scan:
                        {
                            ScanInfo scan = program.Scans[instruction.A];
                            int count = 0;
                            int scanPosition = position;

                            if (
                                scan.Greedy
                                && segment.Direction > 0
                                && TryMeasureGreedyScan(
                                    program,
                                    scan,
                                    input,
                                    scanPosition,
                                    out int availablePoints
                                )
                            )
                            {
                                int consume =
                                    scan.Maximum < 0
                                        ? availablePoints
                                        : Math.Min(availablePoints, scan.Maximum);
                                if (consume < scan.Minimum)
                                {
                                    failed = true;
                                    break;
                                }
                                budget.AddSteps(consume);
                                scanPosition += consume;
                                count = consume;
                                if (count > scan.Minimum)
                                {
                                    int fallbackPosition = RetreatScanPosition(
                                        input,
                                        scanPosition,
                                        segment.Direction,
                                        (scan.Options & NodeOptions.Unicode) != 0
                                    );
                                    PushScanFrame(
                                        BacktrackKind.GreedyScan,
                                        pc + 1,
                                        fallbackPosition,
                                        instruction.A,
                                        count - 1,
                                        options,
                                        ref frames,
                                        captureTrail.Count,
                                        stateTrail.Count
                                    );
                                }
                                position = scanPosition;
                                pc++;
                                break;
                            }

                            while (count < scan.Minimum)
                            {
                                budget.Step();
                                if (
                                    !TryConsumeScan(
                                        program,
                                        scan,
                                        segment.Direction,
                                        input,
                                        scanPosition,
                                        out int nextPosition
                                    )
                                )
                                {
                                    failed = true;
                                    break;
                                }
                                scanPosition = nextPosition;
                                count++;
                            }
                            if (failed)
                                break;

                            if (scan.Greedy)
                            {
                                while (scan.Maximum < 0 || count < scan.Maximum)
                                {
                                    budget.Step();
                                    if (
                                        !TryConsumeScan(
                                            program,
                                            scan,
                                            segment.Direction,
                                            input,
                                            scanPosition,
                                            out int nextPosition
                                        )
                                    )
                                    {
                                        break;
                                    }
                                    scanPosition = nextPosition;
                                    count++;
                                }

                                if (count > scan.Minimum)
                                {
                                    int fallbackPosition = RetreatScanPosition(
                                        input,
                                        scanPosition,
                                        segment.Direction,
                                        (scan.Options & NodeOptions.Unicode) != 0
                                    );
                                    PushScanFrame(
                                        BacktrackKind.GreedyScan,
                                        pc + 1,
                                        fallbackPosition,
                                        instruction.A,
                                        count - 1,
                                        options,
                                        ref frames,
                                        captureTrail.Count,
                                        stateTrail.Count
                                    );
                                }
                            }
                            else if (scan.Maximum < 0 || count < scan.Maximum)
                            {
                                budget.Step();
                                if (
                                    TryConsumeScan(
                                        program,
                                        scan,
                                        segment.Direction,
                                        input,
                                        scanPosition,
                                        out int nextPosition
                                    )
                                )
                                {
                                    PushScanFrame(
                                        BacktrackKind.LazyScan,
                                        pc + 1,
                                        nextPosition,
                                        instruction.A,
                                        count + 1,
                                        options,
                                        ref frames,
                                        captureTrail.Count,
                                        stateTrail.Count
                                    );
                                }
                            }

                            position = scanPosition;
                            pc++;
                            break;
                        }

                        case OpCode.Jump:
                            pc = instruction.A;
                            break;

                        case OpCode.Split:
                            PushFrame(
                                instruction.B,
                                position,
                                options,
                                ref frames,
                                captureTrail.Count,
                                stateTrail.Count
                            );
                            pc = instruction.A;
                            break;

                        case OpCode.Save:
                            SetSlot(captures, instruction.A, position, ref captureTrail);
                            pc++;
                            break;

                        case OpCode.RepeatInit:
                        {
                            RepeatInfo repeat = program.Repeats[instruction.A];
                            SetSlot(states, repeat.StateSlot, 0, ref stateTrail);
                            SetSlot(states, repeat.StateSlot + 1, -1, ref stateTrail);
                            pc++;
                            break;
                        }

                        case OpCode.RepeatDecision:
                        {
                            RepeatInfo repeat = program.Repeats[instruction.A];
                            int count = states[repeat.StateSlot];
                            if (count < repeat.Minimum)
                            {
                                pc = repeat.BodyPc;
                            }
                            else if (repeat.Maximum >= 0 && count >= repeat.Maximum)
                            {
                                pc = repeat.ExitPc;
                            }
                            else if (repeat.Greedy)
                            {
                                PushFrame(
                                    repeat.ExitPc,
                                    position,
                                    options,
                                    ref frames,
                                    captureTrail.Count,
                                    stateTrail.Count
                                );
                                pc = repeat.BodyPc;
                            }
                            else
                            {
                                PushFrame(
                                    repeat.BodyPc,
                                    position,
                                    options,
                                    ref frames,
                                    captureTrail.Count,
                                    stateTrail.Count
                                );
                                pc = repeat.ExitPc;
                            }
                            break;
                        }

                        case OpCode.RepeatBody:
                        {
                            RepeatInfo repeat = program.Repeats[instruction.A];
                            SetSlot(states, repeat.StateSlot + 1, position, ref stateTrail);
                            if (repeat.FirstCapture >= 0)
                            {
                                for (
                                    int group = repeat.FirstCapture;
                                    group <= repeat.LastCapture;
                                    group++
                                )
                                {
                                    SetSlot(captures, group * 2, -1, ref captureTrail);
                                    SetSlot(captures, group * 2 + 1, -1, ref captureTrail);
                                }
                            }
                            pc++;
                            break;
                        }

                        case OpCode.RepeatNext:
                        {
                            RepeatInfo repeat = program.Repeats[instruction.A];
                            int count = states[repeat.StateSlot];
                            int mark = states[repeat.StateSlot + 1];
                            if (position == mark && count >= repeat.Minimum)
                            {
                                failed = true;
                                break;
                            }
                            SetSlot(states, repeat.StateSlot, checked(count + 1), ref stateTrail);
                            pc = repeat.DecisionPc;
                            break;
                        }

                        case OpCode.AssertStart:
                        {
                            NodeOptions nodeOptions = (NodeOptions)instruction.B;
                            bool matches =
                                position == 0
                                || (nodeOptions & NodeOptions.Multiline) != 0
                                    && position > 0
                                    && Utf16.IsLineTerminator(input[position - 1]);
                            if (!matches)
                                failed = true;
                            else
                                pc++;
                            break;
                        }

                        case OpCode.AssertEnd:
                        {
                            NodeOptions nodeOptions = (NodeOptions)instruction.B;
                            bool matches =
                                position == input.Length
                                || (nodeOptions & NodeOptions.Multiline) != 0
                                    && position < input.Length
                                    && Utf16.IsLineTerminator(input[position]);
                            if (!matches)
                                failed = true;
                            else
                                pc++;
                            break;
                        }

                        case OpCode.WordBoundary:
                        {
                            bool boundary = IsWordBoundary(
                                input,
                                position,
                                (NodeOptions)instruction.B
                            );
                            bool negative = instruction.A != 0;
                            if (boundary == negative)
                                failed = true;
                            else
                                pc++;
                            break;
                        }

                        case OpCode.Backreference:
                            if (
                                !TryMatchBackreference(
                                    input,
                                    position,
                                    segment.Direction,
                                    captures,
                                    instruction.A,
                                    (NodeOptions)instruction.B,
                                    out position
                                )
                            )
                            {
                                failed = true;
                            }
                            else
                            {
                                pc++;
                            }
                            break;

                        case OpCode.BackreferenceSet:
                            if (
                                !TryMatchBackreferenceSet(
                                    program,
                                    input,
                                    position,
                                    segment.Direction,
                                    captures,
                                    instruction.A,
                                    (NodeOptions)instruction.B,
                                    out position
                                )
                            )
                            {
                                failed = true;
                            }
                            else
                            {
                                pc++;
                            }
                            break;

                        case OpCode.Assertion:
                        {
                            if (assertionDepth >= options.MaxAssertionDepth)
                                throw new RegExpExecutionException(
                                    RegExpExecutionLimit.AssertionDepth
                                );

                            int captureCheckpoint = captureTrail.Count;
                            int stateCheckpoint = stateTrail.Count;
                            ProgramSegment assertion = program.Segments[instruction.A];
                            ClearCaptureRange(
                                captures,
                                assertion.FirstCapture,
                                assertion.LastCapture,
                                ref captureTrail
                            );
                            bool matched = RunSegment(
                                program,
                                instruction.A,
                                input,
                                position,
                                captures,
                                states,
                                options,
                                ref captureTrail,
                                ref stateTrail,
                                ref budget,
                                assertionDepth + 1,
                                out _
                            );
                            bool positive = instruction.B != 0;

                            if (positive)
                            {
                                if (!matched)
                                {
                                    failed = true;
                                }
                                else
                                {
                                    Rollback(states, ref stateTrail, stateCheckpoint);
                                    pc++;
                                }
                            }
                            else
                            {
                                if (matched)
                                {
                                    Rollback(captures, ref captureTrail, captureCheckpoint);
                                    Rollback(states, ref stateTrail, stateCheckpoint);
                                    failed = true;
                                }
                                else
                                {
                                    pc++;
                                }
                            }
                            break;
                        }

                        default:
                            throw new InvalidOperationException(
                                $"Unknown opcode {instruction.Op}."
                            );
                    }
                }

                if (!failed)
                    continue;
                if (frames.Count == 0)
                {
                    Rollback(captures, ref captureTrail, baseCaptureTrail);
                    Rollback(states, ref stateTrail, baseStateTrail);
                    endPosition = -1;
                    return false;
                }

                budget.Backtrack();
                BacktrackFrame frame = frames.Pop();
                Rollback(captures, ref captureTrail, frame.CaptureTrail);
                Rollback(states, ref stateTrail, frame.StateTrail);
                pc = frame.ProgramCounter;
                position = frame.Position;

                if (frame.Kind == BacktrackKind.GreedyScan)
                {
                    ScanInfo scan = program.Scans[frame.ScanId];
                    if (frame.Count > scan.Minimum)
                    {
                        int fallbackPosition = RetreatScanPosition(
                            input,
                            frame.Position,
                            segment.Direction,
                            (scan.Options & NodeOptions.Unicode) != 0
                        );
                        PushScanFrame(
                            BacktrackKind.GreedyScan,
                            frame.ProgramCounter,
                            fallbackPosition,
                            frame.ScanId,
                            frame.Count - 1,
                            options,
                            ref frames,
                            frame.CaptureTrail,
                            frame.StateTrail
                        );
                    }
                }
                else if (frame.Kind == BacktrackKind.LazyScan)
                {
                    ScanInfo scan = program.Scans[frame.ScanId];
                    if (scan.Maximum < 0 || frame.Count < scan.Maximum)
                    {
                        budget.Step();
                        if (
                            TryConsumeScan(
                                program,
                                scan,
                                segment.Direction,
                                input,
                                frame.Position,
                                out int nextPosition
                            )
                        )
                        {
                            PushScanFrame(
                                BacktrackKind.LazyScan,
                                frame.ProgramCounter,
                                nextPosition,
                                frame.ScanId,
                                frame.Count + 1,
                                options,
                                ref frames,
                                frame.CaptureTrail,
                                frame.StateTrail
                            );
                        }
                    }
                }
            }
        }
        finally
        {
            frames.Dispose();
        }
    }

    internal static bool IsWordBoundary(ReadOnlySpan<char> input, int position, NodeOptions options)
    {
        bool unicode = (options & NodeOptions.Unicode) != 0;
        bool ignoreCase = (options & NodeOptions.IgnoreCase) != 0;
        bool before =
            Utf16.TryReadBackward(input, position, unicode, out int previous, out _)
            && Utf16.IsWord(previous, unicode, ignoreCase);
        bool after =
            Utf16.TryReadForward(input, position, unicode, out int next, out _)
            && Utf16.IsWord(next, unicode, ignoreCase);
        return before != after;
    }

    private static int CollectClassSetCandidates(
        RegexProgram program,
        ClassSetInfo info,
        ReadOnlySpan<char> input,
        int position,
        bool unicode,
        NodeOptions options,
        ref Span<int> candidates,
        ref int[]? rented
    )
    {
        int count = 0;
        StringTrie trie = info.Trie;
        int node = 0;
        int cursor = position;
        while (cursor < input.Length)
        {
            int codePoint = Utf16.ReadForward(input, cursor, unicode, out int width);
            int start = trie.ChildOffsets[node];
            int end =
                node + 1 < trie.NodeCount
                    ? trie.ChildOffsets[node + 1]
                    : trie.EdgeCodePoints.Length;
            int low = start;
            int high = end - 1;
            int next = -1;
            while (low <= high)
            {
                int middle = (low + high) >>> 1;
                int edge = trie.EdgeCodePoints[middle];
                if (codePoint < edge)
                    high = middle - 1;
                else if (codePoint > edge)
                    low = middle + 1;
                else
                {
                    next = trie.EdgeNexts[middle];
                    break;
                }
            }
            if (next < 0)
                break;
            node = next;
            cursor += width;
            if (trie.Terminals[node] >= 0)
                AddClassSetCandidate(ref candidates, ref rented, ref count, cursor);
        }

        int stringCount = count;
        for (int i = 0, j = stringCount - 1; i < j; i++, j--)
        {
            int temporary = candidates[i];
            candidates[i] = candidates[j];
            candidates[j] = temporary;
        }

        if (Utf16.TryReadForward(input, position, unicode, out int single, out int singleWidth))
        {
            bool matches = ClassMatches(info.CodePoints, single, options);
            if ((options & NodeOptions.InvertClass) != 0)
                matches = !matches;
            if (matches)
                AddClassSetCandidate(ref candidates, ref rented, ref count, position + singleWidth);
        }
        return count;
    }

    private static void AddClassSetCandidate(
        ref Span<int> candidates,
        ref int[]? rented,
        ref int count,
        int value
    )
    {
        if (count < candidates.Length)
        {
            candidates[count++] = value;
            return;
        }
        int[] replacement = ArrayPool<int>.Shared.Rent(count * 2);
        candidates[..count].CopyTo(replacement);
        if (rented is not null)
            ArrayPool<int>.Shared.Return(rented, clearArray: false);
        rented = replacement;
        candidates = replacement.AsSpan();
        candidates[count++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConsumeScan(
        RegexProgram program,
        ScanInfo scan,
        int direction,
        ReadOnlySpan<char> input,
        int position,
        out int newPosition
    )
    {
        bool unicode = (scan.Options & NodeOptions.Unicode) != 0;
        if (!TryRead(direction, input, position, unicode, out int codePoint, out int width))
        {
            newPosition = position;
            return false;
        }

        bool matches = scan.Kind switch
        {
            ScanAtomKind.Character => CharacterEquals(codePoint, scan.Value, scan.Options),
            ScanAtomKind.CharacterClass => ClassMatches(
                program.Classes[scan.Value],
                codePoint,
                scan.Options
            ),
            ScanAtomKind.Any => (scan.Options & NodeOptions.DotAll) != 0
                || !Utf16.IsLineTerminator(codePoint),
            _ => false,
        };
        if (
            scan.Kind == ScanAtomKind.CharacterClass
            && (scan.Options & NodeOptions.InvertClass) != 0
        )
        {
            matches = !matches;
        }

        newPosition = matches ? position + direction * width : position;
        return matches;
    }

    private static int RetreatScanPosition(
        ReadOnlySpan<char> input,
        int position,
        int direction,
        bool unicode
    )
    {
        if (!TryRead(-direction, input, position, unicode, out _, out int width))
            throw new InvalidOperationException("Cannot retreat a successful scan endpoint.");
        return position - direction * width;
    }

    /// <summary>
    /// Measures a greedy forward scan run in code points for dot and the ASCII
    /// built-in classes, jumping past matches with a vectorized lookup. Returns
    /// false when the scan shape is unknown or the run needs scalar handling.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryMeasureGreedyScan(
        RegexProgram program,
        ScanInfo scan,
        ReadOnlySpan<char> input,
        int position,
        out int codePoints
    )
    {
        switch (scan.Kind)
        {
            case ScanAtomKind.Any:
            {
                ReadOnlySpan<char> run = input[position..];
                int units;
                if ((scan.Options & NodeOptions.DotAll) != 0)
                {
                    units = run.Length;
                }
                else
                {
                    int terminator = run.IndexOfAny(s_lineTerminators);
                    units = terminator < 0 ? run.Length : terminator;
                }
                if (
                    (scan.Options & NodeOptions.Unicode) != 0
                    && run[..units].IndexOfAnyInRange('\uD800', '\uDFFF') >= 0
                )
                {
                    codePoints = 0;
                    return false;
                }
                codePoints = units;
                return true;
            }
            case ScanAtomKind.CharacterClass:
            {
                CharSet set = program.Classes[scan.Value];
                if (
                    !ReferenceEquals(set, UnicodePropertyDatabase.Digit)
                    && !ReferenceEquals(set, UnicodePropertyDatabase.Word)
                    && !ReferenceEquals(set, UnicodePropertyDatabase.WhiteSpace)
                )
                {
                    codePoints = 0;
                    return false;
                }
                ReadOnlySpan<char> run = input[position..];
                int firstOutside;
                if (ReferenceEquals(set, UnicodePropertyDatabase.Digit))
                {
                    firstOutside = run.IndexOfAnyExceptInRange('0', '9');
                }
                else if (ReferenceEquals(set, UnicodePropertyDatabase.Word))
                {
                    firstOutside = run.IndexOfAnyExcept(s_wordValues);
                }
                else
                {
                    firstOutside = run.IndexOfAnyExcept(s_spaceValues);
                }
                codePoints = firstOutside < 0 ? run.Length : firstOutside;
                return true;
            }
            default:
                codePoints = 0;
                return false;
        }
    }

    private static readonly SearchValues<char> s_lineTerminators = SearchValues.Create([
        '\n',
        '\r',
        '\u2028',
        '\u2029',
    ]);

    private static readonly SearchValues<char> s_wordValues = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_"
    );

    private static readonly SearchValues<char> s_spaceValues = SearchValues.Create(
        "\t\n\v\f\r \u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF"
    );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ClassMatches(CharSet set, int codePoint, NodeOptions options)
    {
        if ((options & NodeOptions.IgnoreCase) == 0)
            return set.Contains(codePoint);
        if ((options & NodeOptions.UnicodeSets) != 0)
            return set.Contains(UnicodeCaseFolding.CanonicalizeUnicode(codePoint));
        return set.ContainsCaseInsensitive(codePoint, (options & NodeOptions.Unicode) != 0);
    }

    private static void ClearCaptureRange(
        Span<int> captures,
        int firstGroup,
        int lastGroup,
        ref ValueStack<TrailEntry> trail
    )
    {
        if (firstGroup < 0)
            return;
        for (int group = firstGroup; group <= lastGroup; group++)
        {
            SetSlot(captures, group * 2, -1, ref trail);
            SetSlot(captures, group * 2 + 1, -1, ref trail);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryRead(
        int direction,
        ReadOnlySpan<char> input,
        int position,
        bool unicode,
        out int codePoint,
        out int width
    ) =>
        direction > 0
            ? Utf16.TryReadForward(input, position, unicode, out codePoint, out width)
            : Utf16.TryReadBackward(input, position, unicode, out codePoint, out width);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CharacterEquals(int left, int right, NodeOptions options)
    {
        if (left == right)
            return true;
        if ((options & NodeOptions.IgnoreCase) == 0)
            return false;
        return (options & NodeOptions.Unicode) != 0
            ? UnicodeCaseFolding.EqualsUnicode(left, right)
            : UnicodeCaseFolding.EqualsLegacy(left, right);
    }

    private static bool TryMatchBackreference(
        ReadOnlySpan<char> input,
        int position,
        int direction,
        ReadOnlySpan<int> captures,
        int group,
        NodeOptions options,
        out int newPosition
    )
    {
        int start = captures[group * 2];
        int end = captures[group * 2 + 1];
        if (start < 0 || end < 0)
        {
            newPosition = position;
            return true;
        }
        if (start > end)
            (start, end) = (end, start);
        return TryMatchCapturedRange(
            input,
            position,
            direction,
            start,
            end,
            options,
            out newPosition
        );
    }

    private static bool TryMatchBackreferenceSet(
        RegexProgram program,
        ReadOnlySpan<char> input,
        int position,
        int direction,
        ReadOnlySpan<int> captures,
        int setIndex,
        NodeOptions options,
        out int newPosition
    )
    {
        int[] groups = program.NameGroupSets[setIndex];
        for (int i = groups.Length - 1; i >= 0; i--)
        {
            int group = groups[i];
            int start = captures[group * 2];
            int end = captures[group * 2 + 1];
            if (start < 0 || end < 0)
                continue;
            if (start > end)
                (start, end) = (end, start);
            return TryMatchCapturedRange(
                input,
                position,
                direction,
                start,
                end,
                options,
                out newPosition
            );
        }
        newPosition = position;
        return true;
    }

    private static bool TryMatchCapturedRange(
        ReadOnlySpan<char> input,
        int position,
        int direction,
        int start,
        int end,
        NodeOptions options,
        out int newPosition
    )
    {
        bool unicode = (options & NodeOptions.Unicode) != 0;
        bool ignoreCase = (options & NodeOptions.IgnoreCase) != 0;
        if (!ignoreCase)
        {
            int length = end - start;
            int candidateStart = direction > 0 ? position : position - length;
            if (
                candidateStart < 0
                || candidateStart + length > input.Length
                || !input.Slice(start, length).SequenceEqual(input.Slice(candidateStart, length))
            )
            {
                newPosition = position;
                return false;
            }
            newPosition = direction > 0 ? position + length : position - length;
            return true;
        }

        if (direction > 0)
        {
            int captured = start;
            int candidate = position;
            while (captured < end)
            {
                if (
                    !Utf16.TryReadForward(input, captured, unicode, out int a, out int aw)
                    || !Utf16.TryReadForward(input, candidate, unicode, out int b, out int bw)
                    || !CharacterEquals(a, b, options)
                )
                {
                    newPosition = position;
                    return false;
                }
                captured += aw;
                candidate += bw;
            }
            newPosition = candidate;
            return true;
        }
        else
        {
            int captured = end;
            int candidate = position;
            while (captured > start)
            {
                if (
                    !Utf16.TryReadBackward(input, captured, unicode, out int a, out int aw)
                    || !Utf16.TryReadBackward(input, candidate, unicode, out int b, out int bw)
                    || !CharacterEquals(a, b, options)
                )
                {
                    newPosition = position;
                    return false;
                }
                captured -= aw;
                candidate -= bw;
            }
            newPosition = candidate;
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetSlot(
        Span<int> slots,
        int index,
        int value,
        ref ValueStack<TrailEntry> trail
    )
    {
        int previous = slots[index];
        if (previous == value)
            return;
        trail.Push(new TrailEntry(index, previous));
        slots[index] = value;
    }

    private static void Rollback(Span<int> slots, ref ValueStack<TrailEntry> trail, int target)
    {
        while (trail.Count > target)
        {
            TrailEntry entry = trail.Pop();
            slots[entry.Slot] = entry.PreviousValue;
        }
    }

    private static void PushFrame(
        int pc,
        int position,
        RegExpOptions options,
        ref ValueStack<BacktrackFrame> frames,
        int captureTrail,
        int stateTrail
    )
    {
        if (frames.Count >= options.MaxBacktrackDepth)
            throw new RegExpExecutionException(RegExpExecutionLimit.BacktrackDepth);
        frames.Push(
            new BacktrackFrame(BacktrackKind.Resume, pc, position, captureTrail, stateTrail)
        );
    }

    private static void PushScanFrame(
        BacktrackKind kind,
        int pc,
        int position,
        int scanId,
        int count,
        RegExpOptions options,
        ref ValueStack<BacktrackFrame> frames,
        int captureTrail,
        int stateTrail
    )
    {
        if (frames.Count >= options.MaxBacktrackDepth)
            throw new RegExpExecutionException(RegExpExecutionLimit.BacktrackDepth);
        frames.Push(
            new BacktrackFrame(kind, pc, position, captureTrail, stateTrail, scanId, count)
        );
    }

    private static bool PrefixMatches(ReadOnlySpan<char> input, int position, string? prefix) =>
        prefix is null || input[position..].StartsWith(prefix.AsSpan(), StringComparison.Ordinal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool LeadingSetMatches(
        SearchPlan plan,
        ReadOnlySpan<char> input,
        int candidate,
        bool unicode
    )
    {
        CharSet? leading = plan.LeadingSet;
        if (leading is null)
            return true;
        if (candidate >= input.Length)
            return false;
        int codePoint = unicode
            ? Utf16.ReadForward(input, candidate, unicode, out _)
            : input[candidate];
        return leading.Contains(codePoint);
    }
}
