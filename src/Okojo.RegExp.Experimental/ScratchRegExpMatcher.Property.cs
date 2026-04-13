namespace Okojo.RegExp.Experimental;

internal static partial class ScratchRegExpMatcher
{
    private static void ConsumePropertyEscapeRun(
        string input,
        int pos,
        ScratchRegExpProgram.PropertyEscapeNode propertyEscape,
        RegExpRuntimeFlags flags,
        int maxCount,
        out int endPos,
        out int consumed)
    {
        ConsumePropertyEscapeRunForVm(input, pos,
            new(propertyEscape.Kind, propertyEscape.Negated, propertyEscape.Categories, propertyEscape.PropertyValue),
            flags, input.Length, maxCount, out endPos, out consumed);
    }

    private static bool FastPropertyEscapeMatches(ScratchRegExpProgram.PropertyEscapeNode propertyEscape, int codePoint,
        RegExpRuntimeFlags flags)
    {
        return FastPropertyEscapeMatches(propertyEscape.Kind, propertyEscape.Negated, propertyEscape.Categories,
            propertyEscape.PropertyValue, codePoint, flags);
    }

    private static bool FastPropertyEscapeMatches(ExperimentalRegExpPropertyEscape propertyEscape, int codePoint,
        RegExpRuntimeFlags flags)
    {
        return FastPropertyEscapeMatches(propertyEscape.Kind, propertyEscape.Negated, propertyEscape.Categories,
            propertyEscape.PropertyValue, codePoint, flags);
    }

    private static bool FastPropertyEscapeMatches(
        ScratchRegExpProgram.PropertyEscapeKind kind,
        bool negated,
        ScratchRegExpProgram.GeneralCategoryMask categories,
        string? propertyValue,
        int codePoint,
        RegExpRuntimeFlags flags)
    {
        bool matched;
        switch (kind)
        {
            case ScratchRegExpProgram.PropertyEscapeKind.GeneralCategory:
                matched = ScratchUnicodeGeneralCategoryTables.Contains(categories, codePoint);
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
                matched = propertyValue is not null &&
                          ScratchUnicodeScriptTables.Contains(ScratchUnicodeScriptTables.GetRanges(propertyValue),
                              codePoint);
                break;
            case ScratchRegExpProgram.PropertyEscapeKind.ScriptExtensions:
                matched = propertyValue is not null &&
                          ScratchUnicodeScriptExtensionsTables.Contains(
                              ScratchUnicodeScriptExtensionsTables.GetRanges(propertyValue), codePoint);
                break;
            case ScratchRegExpProgram.PropertyEscapeKind.UppercaseLetter
                when negated && flags.IgnoreCase:
                return true;
            default:
                return PropertyEscapeMatches(kind, negated, categories, propertyValue, codePoint, flags);
        }

        return negated ? !matched : matched;
    }

    internal static int ScanPropertyEscapeToEndForVm(string input, int pos,
        ExperimentalRegExpPropertyEscape propertyEscape, RegExpRuntimeFlags flags, int endLimit)
    {
        ConsumePropertyEscapeRunForVm(input, pos, propertyEscape, flags, endLimit, int.MaxValue, out var endPos,
            out _);
        return endPos;
    }

    internal static void ConsumePropertyEscapeRunForVm(string input, int pos,
        ExperimentalRegExpPropertyEscape propertyEscape, RegExpRuntimeFlags flags, int endLimit, int maxCount,
        out int endPos, out int consumed)
    {
        if (TryConsumeSpecializedPropertyEscapeRun(input, pos, propertyEscape, flags, endLimit, maxCount, out endPos,
                out consumed))
            return;

        var currentPos = pos;
        consumed = 0;

        while (consumed < maxCount && (uint)currentPos < (uint)endLimit && (uint)currentPos < (uint)input.Length)
        {
            int codePoint;
            int nextPos;

            if (propertyEscape.Kind == ScratchRegExpProgram.PropertyEscapeKind.StringProperty &&
                propertyEscape.PropertyValue is not null)
            {
                if (!ScratchUnicodeStringPropertyTables.TryMatchAt(propertyEscape.PropertyValue, input, currentPos,
                        out nextPos) ||
                    nextPos == currentPos ||
                    nextPos > endLimit)
                    break;
            }
            else if (flags.Unicode &&
                     currentPos + 1 < endLimit &&
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

    private static bool TryConsumeSpecializedPropertyEscapeRun(string input, int pos,
        ExperimentalRegExpPropertyEscape propertyEscape, RegExpRuntimeFlags flags, int endLimit, int maxCount,
        out int endPos, out int consumed)
    {
        switch (propertyEscape.Kind)
        {
            case ScratchRegExpProgram.PropertyEscapeKind.StringProperty when propertyEscape.PropertyValue is not null:
                return ConsumeStringPropertyRun(input, pos, propertyEscape.PropertyValue, endLimit, maxCount, out endPos,
                    out consumed);
            case ScratchRegExpProgram.PropertyEscapeKind.GeneralCategory:
                return ConsumeGeneralCategoryPropertyRun(input, pos, propertyEscape, flags, endLimit, maxCount,
                    out endPos, out consumed);
            case ScratchRegExpProgram.PropertyEscapeKind.Ascii:
                return ConsumeAsciiPropertyRun(input, pos, propertyEscape.Negated, flags, endLimit, maxCount,
                    out endPos, out consumed);
            case ScratchRegExpProgram.PropertyEscapeKind.Any:
                return ConsumeAnyPropertyRun(input, pos, propertyEscape.Negated, flags, endLimit, maxCount,
                    out endPos, out consumed);
            case ScratchRegExpProgram.PropertyEscapeKind.Assigned:
                return ConsumeAssignedPropertyRun(input, pos, propertyEscape.Negated, flags, endLimit, maxCount,
                    out endPos, out consumed);
            case ScratchRegExpProgram.PropertyEscapeKind.Script when propertyEscape.PropertyValue is not null:
                return ConsumeScriptPropertyRun(input, pos, propertyEscape.PropertyValue, propertyEscape.Negated, flags,
                    endLimit, maxCount, out endPos, out consumed);
            case ScratchRegExpProgram.PropertyEscapeKind.ScriptExtensions when propertyEscape.PropertyValue is not null:
                return ConsumeScriptExtensionsPropertyRun(input, pos, propertyEscape.PropertyValue,
                    propertyEscape.Negated, flags, endLimit, maxCount, out endPos, out consumed);
            case ScratchRegExpProgram.PropertyEscapeKind.UppercaseLetter when propertyEscape.Negated && flags.IgnoreCase:
                return ConsumeAnyPropertyRun(input, pos, negated: false, flags, endLimit, maxCount, out endPos,
                    out consumed);
            default:
                endPos = default;
                consumed = default;
                return false;
        }
    }

    private static bool ConsumeStringPropertyRun(string input, int pos, string propertyValue, int endLimit, int maxCount,
        out int endPos, out int consumed)
    {
        var currentPos = pos;
        consumed = 0;
        while (consumed < maxCount &&
               ScratchUnicodeStringPropertyTables.TryMatchAt(propertyValue, input, currentPos, out var nextPos) &&
               nextPos != currentPos &&
               nextPos <= endLimit)
        {
            consumed++;
            currentPos = nextPos;
        }

        endPos = currentPos;
        return true;
    }

    private static bool ConsumeGeneralCategoryPropertyRun(string input, int pos,
        ExperimentalRegExpPropertyEscape propertyEscape, RegExpRuntimeFlags flags, int endLimit, int maxCount,
        out int endPos, out int consumed)
    {
        var currentPos = pos;
        consumed = 0;
        while (consumed < maxCount &&
               TryReadCodePointForVm(input, currentPos, flags.Unicode, endLimit, out var nextPos, out var codePoint) &&
               (ScratchUnicodeGeneralCategoryTables.Contains(propertyEscape.Categories, codePoint) ^
                propertyEscape.Negated))
        {
            consumed++;
            currentPos = nextPos;
        }

        endPos = currentPos;
        return true;
    }

    private static bool ConsumeAsciiPropertyRun(string input, int pos, bool negated, RegExpRuntimeFlags flags,
        int endLimit, int maxCount, out int endPos, out int consumed)
    {
        var currentPos = pos;
        consumed = 0;
        while (consumed < maxCount &&
               TryReadCodePointForVm(input, currentPos, flags.Unicode, endLimit, out var nextPos, out var codePoint) &&
               ((codePoint <= 0x7F) ^ negated))
        {
            consumed++;
            currentPos = nextPos;
        }

        endPos = currentPos;
        return true;
    }

    private static bool ConsumeAnyPropertyRun(string input, int pos, bool negated, RegExpRuntimeFlags flags,
        int endLimit, int maxCount, out int endPos, out int consumed)
    {
        var currentPos = pos;
        consumed = 0;
        if (negated)
        {
            endPos = currentPos;
            return true;
        }

        while (consumed < maxCount &&
               TryReadCodePointForVm(input, currentPos, flags.Unicode, endLimit, out var nextPos, out _))
        {
            consumed++;
            currentPos = nextPos;
        }

        endPos = currentPos;
        return true;
    }

    private static bool ConsumeAssignedPropertyRun(string input, int pos, bool negated, RegExpRuntimeFlags flags,
        int endLimit, int maxCount, out int endPos, out int consumed)
    {
        var currentPos = pos;
        consumed = 0;
        while (consumed < maxCount &&
               TryReadCodePointForVm(input, currentPos, flags.Unicode, endLimit, out var nextPos, out var codePoint) &&
               (IsAssignedCodePoint(codePoint) ^ negated))
        {
            consumed++;
            currentPos = nextPos;
        }

        endPos = currentPos;
        return true;
    }

    private static bool ConsumeScriptPropertyRun(string input, int pos, string propertyValue, bool negated,
        RegExpRuntimeFlags flags, int endLimit, int maxCount, out int endPos, out int consumed)
    {
        var ranges = ScratchUnicodeScriptTables.GetRanges(propertyValue);
        var currentPos = pos;
        consumed = 0;
        while (consumed < maxCount &&
               TryReadCodePointForVm(input, currentPos, flags.Unicode, endLimit, out var nextPos, out var codePoint) &&
               (ScratchUnicodeScriptTables.Contains(ranges, codePoint) ^ negated))
        {
            consumed++;
            currentPos = nextPos;
        }

        endPos = currentPos;
        return true;
    }

    private static bool ConsumeScriptExtensionsPropertyRun(string input, int pos, string propertyValue, bool negated,
        RegExpRuntimeFlags flags, int endLimit, int maxCount, out int endPos, out int consumed)
    {
        var ranges = ScratchUnicodeScriptExtensionsTables.GetRanges(propertyValue);
        var currentPos = pos;
        consumed = 0;
        while (consumed < maxCount &&
               TryReadCodePointForVm(input, currentPos, flags.Unicode, endLimit, out var nextPos, out var codePoint) &&
               (ScratchUnicodeScriptExtensionsTables.Contains(ranges, codePoint) ^ negated))
        {
            consumed++;
            currentPos = nextPos;
        }

        endPos = currentPos;
        return true;
    }

    private static bool TryMatchPropertyEscapeForward(string input, int pos,
        ExperimentalRegExpPropertyEscape propertyEscape, RegExpRuntimeFlags flags, out int endIndex)
    {
        if (propertyEscape.Kind == ScratchRegExpProgram.PropertyEscapeKind.StringProperty &&
            propertyEscape.PropertyValue is not null)
            return ScratchUnicodeStringPropertyTables.TryMatchAt(propertyEscape.PropertyValue, input, pos,
                out endIndex);

        if (TryReadCodePoint(input, pos, flags.Unicode, out var nextPos, out var cp) &&
            FastPropertyEscapeMatches(propertyEscape, cp, flags))
        {
            endIndex = nextPos;
            return true;
        }

        endIndex = default;
        return false;
    }

    private static bool TryMatchPropertyEscapeForward(string input, int pos,
        ScratchRegExpProgram.PropertyEscapeNode propertyEscape, RegExpRuntimeFlags flags, out int endIndex)
    {
        return TryMatchPropertyEscapeForward(input, pos,
            new ExperimentalRegExpPropertyEscape(propertyEscape.Kind, propertyEscape.Negated, propertyEscape.Categories,
                propertyEscape.PropertyValue), flags, out endIndex);
    }

    private static bool TryMatchPropertyEscapeBackward(string input, int pos,
        ExperimentalRegExpPropertyEscape propertyEscape, RegExpRuntimeFlags flags, out int startIndex, int startLimit = 0)
    {
        if (propertyEscape.Kind == ScratchRegExpProgram.PropertyEscapeKind.StringProperty &&
            propertyEscape.PropertyValue is not null)
        {
            if (ScratchUnicodeStringPropertyTables.TryMatchBackward(propertyEscape.PropertyValue, input, pos,
                    out startIndex) &&
                startIndex >= startLimit)
                return true;

            startIndex = default;
            return false;
        }

        if (TryReadCodePointBackward(input, pos, flags.Unicode, out var prevPos, out var cp) &&
            prevPos >= startLimit &&
            FastPropertyEscapeMatches(propertyEscape, cp, flags))
        {
            startIndex = prevPos;
            return true;
        }

        startIndex = default;
        return false;
    }

    private static bool TryMatchPropertyEscapeBackward(string input, int pos,
        ScratchRegExpProgram.PropertyEscapeNode propertyEscape, RegExpRuntimeFlags flags, out int startIndex,
        int startLimit = 0)
    {
        return TryMatchPropertyEscapeBackward(input, pos,
            new ExperimentalRegExpPropertyEscape(propertyEscape.Kind, propertyEscape.Negated, propertyEscape.Categories,
                propertyEscape.PropertyValue), flags, out startIndex, startLimit);
    }

    private static bool PropertyEscapeMatches(ScratchRegExpProgram.PropertyEscapeNode propertyEscape, int codePoint,
        RegExpRuntimeFlags flags)
    {
        return PropertyEscapeMatches(propertyEscape.Kind, propertyEscape.Negated, propertyEscape.Categories,
            propertyEscape.PropertyValue, codePoint, flags);
    }

    private static bool PropertyEscapeMatches(ExperimentalRegExpPropertyEscape propertyEscape, int codePoint,
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
