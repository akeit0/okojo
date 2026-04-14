namespace Okojo.RegExp;

internal static partial class ScratchRegExpMatcher
{
    internal static bool TryReadCodePointForVm(string input, int pos, bool unicode, int endLimit, out int nextPos,
        out int codePoint)
    {
        if (pos >= endLimit)
        {
            nextPos = default;
            codePoint = default;
            return false;
        }

        return TryReadCodePoint(input, pos, unicode, out nextPos, out codePoint);
    }

    internal static bool CodePointEqualsForVm(int left, int right, bool ignoreCase)
    {
        return CodePointEquals(left, right, ignoreCase);
    }

    internal static bool TryMatchLiteralSetForVm(string input, int pos, int[] codePoints, bool ignoreCase, bool unicode,
        int endLimit, out int nextPos)
    {
        if (!TryReadCodePointForVm(input, pos, unicode, endLimit, out nextPos, out var codePoint))
            return false;

        for (var i = 0; i < codePoints.Length; i++)
            if (CodePointEquals(codePoint, codePoints[i], ignoreCase))
                return true;

        nextPos = default;
        return false;
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

    internal static bool TryMatchCharacterSetForVm(string input, int pos, RegExpCharacterSet characterSet,
        RegExpRuntimeFlags flags, int endLimit, out int nextPos)
    {
        if (pos >= endLimit)
        {
            nextPos = default;
            return false;
        }

        if (characterSet.SimpleClass is not null)
            return TryMatchSimpleClassForward(characterSet.SimpleClass, input, pos, flags, out nextPos);

        return TryMatchClassForward(input, pos, characterSet.ComplexClass!, flags, out nextPos);
    }

    internal static bool TryMatchPropertyEscapeForVm(string input, int pos,
        RegExpPropertyEscape propertyEscape, RegExpRuntimeFlags flags, int endLimit, out int nextPos)
    {
        if (pos >= endLimit)
        {
            nextPos = default;
            return false;
        }

        return TryMatchPropertyEscapeForward(input, pos, propertyEscape, flags, out nextPos);
    }

    internal static bool TryMatchBackReferenceForVm(string input, int pos, int captureIndex, RegExpRuntimeFlags flags,
        bool ignoreCase, RegExpCaptureState? captureState, out int endIndex)
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
        RegExpRuntimeFlags flags, bool ignoreCase, RegExpCaptureState? captureState, out int endIndex)
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

    internal static bool TryMatchLookbehindForVm(CompiledProgram compiledProgram,
        ScratchRegExpProgram.Node child,
        string input, int pos, RegExpRuntimeFlags flags, int minMatchLength, int maxMatchLength,
        RegExpCaptureState? captureState)
    {
        var program = compiledProgram.TreeProgram;
        if (minMatchLength >= 0 && pos < minMatchLength)
            return false;

        var startLimit = maxMatchLength >= 0 ? Math.Max(0, pos - maxMatchLength) : 0;
        using var stateArena = program.CaptureCount == 0 ? null : new ScratchMatchStateArena(program.CaptureCount);
        var state = stateArena is null ? ScratchMatchState.Empty : stateArena.Root;
        if (captureState is not null)
            CopyVmCapturesToScratchState(captureState, state);

        if (!TryMatchLookbehindAssertionForVm(compiledProgram, child, input, pos, flags, state, out _,
                startLimit))
            return false;

        if (captureState is not null)
            CopyScratchStateToVmCaptures(state, captureState, program.CaptureCount);

        return true;
    }

    internal static bool TryMatchLookbehindForwardProgramForVm(CompiledProgram compiledProgram,
        RegExpBytecodeProgram lookbehindProgram, string input, int pos, RegExpRuntimeFlags flags,
        int minMatchLength, int maxMatchLength, RegExpCaptureState? captureState)
    {
        var program = compiledProgram.TreeProgram;
        if (minMatchLength >= 0 && pos < minMatchLength)
            return false;

        var earliestStart = 0;
        if (maxMatchLength >= 0)
            earliestStart = Math.Max(0, pos - maxMatchLength);

        var checkpoint = captureState?.Checkpoint ?? 0;
        var latestStart = minMatchLength >= 0 ? pos - minMatchLength : pos;
        for (var candidateStart = earliestStart; candidateStart <= latestStart; candidateStart++)
        {
            captureState?.Restore(checkpoint);
            if (RegExpVm.TryMatch(compiledProgram, lookbehindProgram, input, candidateStart, flags,
                    captureState,
                    pos, out var endIndex) &&
                endIndex == pos)
                return true;
        }

        captureState?.Restore(checkpoint);

        return false;
    }

    internal static bool TryMatchLookaheadAssertionForVm(CompiledProgram compiledProgram,
        ScratchRegExpProgram.Node child, string input, int pos, RegExpRuntimeFlags flags, ScratchMatchState state)
    {
        if (!compiledProgram.TryGetLookaheadAssertionProgram(child, flags, out var lookaheadProgram))
            throw new InvalidOperationException(" lookahead assertion program was not precompiled.");

        using var captureState = compiledProgram.TreeProgram.CaptureCount == 0
            ? null
            : new RegExpCaptureState(compiledProgram.TreeProgram.CaptureCount);
        if (captureState is not null)
            CopyScratchStateToVmCaptures(state, captureState, compiledProgram.TreeProgram.CaptureCount);

        var matched = RegExpVm.TryMatch(compiledProgram, lookaheadProgram, input, pos, flags, captureState,
            out _);
        if (matched && captureState is not null)
            CopyVmCapturesToScratchState(captureState, state);

        return matched;
    }

    internal static int ScanDotToEndForVm(string input, int pos, bool unicode, int endLimit)
    {
        var currentPos = pos;
        while (TryReadCodePointForVm(input, currentPos, unicode, endLimit, out var nextPos, out var codePoint) &&
               !IsLineTerminator(codePoint))
            currentPos = nextPos;

        return currentPos;
    }

    private static void CopyVmCapturesToScratchState(RegExpCaptureState source, ScratchMatchState destination)
    {
        var destStarts = destination.Starts.AsSpan();
        var destEnds = destination.Ends.AsSpan();
        var destMatched = destination.Matched.AsSpan();
        for (var i = 0; i <= source.CaptureCount; i++)
        {
            destStarts[i] = source.Starts[i];
            destEnds[i] = source.Ends[i];
            destMatched[i] = source.Matched[i] ? 1 : 0;
        }
    }

    private static void CopyScratchStateToVmCaptures(ScratchMatchState source, RegExpCaptureState destination,
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

    internal static int ScanCharacterSetToEndForVm(string input, int pos, RegExpCharacterSet characterSet,
        RegExpRuntimeFlags flags, int endLimit)
    {
        var currentPos = pos;
        while (TryMatchCharacterSetForVm(input, currentPos, characterSet, flags, endLimit, out var nextPos))
            currentPos = nextPos;

        return currentPos;
    }

    internal static bool TryMatchAsciiClassForVm(string input, int pos, ulong lowBitmap, ulong highBitmap, int endLimit,
        out int nextPos)
    {
        if (pos < endLimit && (uint)pos < (uint)input.Length && input[pos] <= 0x7F &&
            MatchesAsciiBitmap(input[pos], lowBitmap, highBitmap))
        {
            nextPos = pos + 1;
            return true;
        }

        nextPos = default;
        return false;
    }

    internal static int ScanAsciiClassToEndForVm(string input, int pos, ulong lowBitmap, ulong highBitmap, int endLimit)
    {
        var currentPos = pos;
        while (currentPos < endLimit &&
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
}
