namespace Okojo.RegExp.Experimental;

internal static partial class ScratchRegExpMatcher
{
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

        if (program.MinMatchLength == 0 &&
            program.CandidateSearchPlan.AnchorKind == ScratchRegExpProgram.SearchAnchorKind.None)
            return Math.Max(0, start);

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
        if (program.MinMatchLength == 0)
            return true;

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
}
