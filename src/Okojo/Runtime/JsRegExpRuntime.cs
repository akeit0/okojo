using System.Text;
using System.Text.RegularExpressions;
using Okojo.RegExp;

namespace Okojo.Runtime;

internal static class JsRegExpRuntime
{
    internal static Flags ParseFlags(string flags)
    {
        var global = false;
        var ignoreCase = false;
        var multiline = false;
        var hasIndices = false;
        var sticky = false;
        var unicode = false;
        var unicodeSets = false;
        var dotAll = false;

        for (var i = 0; i < flags.Length; i++)
            switch (flags[i])
            {
                case 'g':
                    if (global)
                        ThrowInvalidFlags();
                    global = true;
                    break;
                case 'i':
                    if (ignoreCase)
                        ThrowInvalidFlags();
                    ignoreCase = true;
                    break;
                case 'm':
                    if (multiline)
                        ThrowInvalidFlags();
                    multiline = true;
                    break;
                case 'd':
                    if (hasIndices)
                        ThrowInvalidFlags();
                    hasIndices = true;
                    break;
                case 'y':
                    if (sticky)
                        ThrowInvalidFlags();
                    sticky = true;
                    break;
                case 'u':
                    if (unicode)
                        ThrowInvalidFlags();
                    unicode = true;
                    break;
                case 'v':
                    if (unicode || unicodeSets)
                        ThrowInvalidFlags();
                    unicode = true;
                    unicodeSets = true;
                    break;
                case 's':
                    if (dotAll)
                        ThrowInvalidFlags();
                    dotAll = true;
                    break;
                default:
                    ThrowInvalidFlags();
                    break;
            }

        return new(global, ignoreCase, multiline, hasIndices, sticky, unicode, unicodeSets, dotAll);
    }

    internal static string CanonicalizeFlags(in Flags flags)
    {
        var sb = new StringBuilder(8);
        if (flags.HasIndices)
            sb.Append('d');
        if (flags.Global)
            sb.Append('g');
        if (flags.IgnoreCase)
            sb.Append('i');
        if (flags.Multiline)
            sb.Append('m');
        if (flags.DotAll)
            sb.Append('s');
        if (flags.UnicodeSets)
            sb.Append('v');
        else if (flags.Unicode)
            sb.Append('u');
        if (flags.Sticky)
            sb.Append('y');
        return sb.ToString();
    }

    internal static RegExpCompiledPattern CompilePattern(string pattern, string flags)
    {
        var parsedFlags = ParseFlags(flags);
        var canonicalFlags = CanonicalizeFlags(parsedFlags);
        var prepared = PreparePatternForExecution(pattern, parsedFlags.Unicode, parsedFlags.DotAll,
            parsedFlags.Multiline, parsedFlags.IgnoreCase);
        return new(pattern, canonicalFlags, prepared.ExecutionPattern, prepared.NamedGroupNames,
            new(parsedFlags.Global, parsedFlags.IgnoreCase, parsedFlags.Multiline,
                parsedFlags.HasIndices, parsedFlags.Sticky, parsedFlags.Unicode, parsedFlags.UnicodeSets,
                parsedFlags.DotAll));
    }

    internal static JsValue Exec(JsRealm realm, JsRegExpObject rx, string input)
    {
        var match = ExecMatchResult(realm, rx, input);
        return match is null ? JsValue.Null : BuildExecResult(realm, rx, match, input);
    }

    internal static RegExpMatchResult? ExecMatchResult(JsRealm realm, JsRegExpObject rx, string input)
    {
        var lastIndex = GetLastIndex(realm, rx);
        var global = rx.Global;
        var sticky = rx.Sticky;
        var useLastIndex = global || sticky;
        var startIndex = useLastIndex ? (int)Math.Min(lastIndex, int.MaxValue) : 0;

        if (rx.Engine is not null)
        {
            if (useLastIndex && lastIndex > input.Length)
            {
                SetLastIndex(realm, rx, 0);
                return null;
            }

            var engineMatch = rx.Engine.Exec(rx.CompiledPattern, input, startIndex);
            if (engineMatch is null)
            {
                if (useLastIndex)
                    SetLastIndex(realm, rx, 0);
                return null;
            }

            if (useLastIndex)
                SetLastIndex(realm, rx, engineMatch.Index + engineMatch.Length);

            return engineMatch;
        }

        if (useLastIndex && lastIndex > input.Length)
        {
            SetLastIndex(realm, rx, 0);
            return null;
        }

        var match = BuildRegexForExecution(rx).Match(input, startIndex);
        var success = match.Success && (!sticky || match.Index == startIndex);
        if (!success)
        {
            if (useLastIndex)
                SetLastIndex(realm, rx, 0);
            return null;
        }

        if (useLastIndex)
            SetLastIndex(realm, rx, match.Index + match.Length);

        return CreateMatchResult(rx, match);
    }

    private static JsValue BuildExecResult(JsRealm realm, JsRegExpObject rx, RegExpMatchResult match, string input)
    {
        var array = realm.CreateArrayObject();
        var values = array.InitializeDenseElementsNoCollision(match.Groups.Length);
        for (var i = 0; i < match.Groups.Length; i++)
            values[i] = match.Groups[i] is null
                ? JsValue.Undefined
                : JsValue.FromString(match.Groups[i]!);

        var groupsValue = JsValue.Undefined;
        if (rx.NamedGroupNames.Length != 0)
        {
            JsPlainObject groups = new(realm, false) { Prototype = null };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var groupName in rx.NamedGroupNames)
            {
                if (!seen.Add(groupName))
                    continue;

                string? groupValue = null;
                if (match.NamedGroups is not null)
                    match.NamedGroups.TryGetValue(groupName, out groupValue);
                groups.DefineDataProperty(groupName,
                    groupValue is null ? JsValue.Undefined : JsValue.FromString(groupValue),
                    JsShapePropertyFlags.Open);
            }

            groupsValue = JsValue.FromObject(groups);
        }

        array.DefineDataPropertyAtom(realm, AtomTable.IdGroups, groupsValue, JsShapePropertyFlags.Open);
        if (rx.CompiledPattern.ParsedFlags.HasIndices)
        {
            var indices = CreateMatchIndicesArray(realm,
                match.GroupIndices ?? new RegExpMatchRange?[match.Groups.Length]);
            var indexGroupsValue = JsValue.Undefined;

            if (rx.NamedGroupNames.Length != 0)
            {
                JsPlainObject groups = new(realm, false) { Prototype = null };
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var groupName in rx.NamedGroupNames)
                {
                    if (!seen.Add(groupName))
                        continue;

                    RegExpMatchRange? range = null;
                    if (match.NamedGroupIndices is not null)
                        match.NamedGroupIndices.TryGetValue(groupName, out range);
                    groups.DefineDataProperty(groupName,
                        range.HasValue
                            ? CreateMatchIndexPairArray(realm, range.Value)
                            : JsValue.Undefined,
                        JsShapePropertyFlags.Open);
                }

                indexGroupsValue = JsValue.FromObject(groups);
            }

            indices.DefineDataPropertyAtom(realm, AtomTable.IdGroups, indexGroupsValue, JsShapePropertyFlags.Open);
            array.DefineDataProperty("indices", JsValue.FromObject(indices), JsShapePropertyFlags.Open);
        }

        array.DefineDataProperty("index", JsValue.FromInt32(match.Index), JsShapePropertyFlags.Open);
        array.DefineDataProperty("input", JsValue.FromString(input), JsShapePropertyFlags.Open);
        return array;
    }

    private static RegExpMatchResult CreateMatchResult(JsRegExpObject rx, Match match)
    {
        var groups = new string?[match.Groups.Count];
        RegExpMatchRange?[]? groupIndices = rx.CompiledPattern.ParsedFlags.HasIndices
            ? new RegExpMatchRange?[match.Groups.Count]
            : null;
        for (var i = 0; i < match.Groups.Count; i++)
        {
            var group = match.Groups[i];
            if (i == 0 || group.Success)
                groups[i] = group.Value;

            if (groupIndices is not null && (i == 0 || group.Success))
                groupIndices[i] = new(group.Index, group.Index + group.Length);
        }

        Dictionary<string, string?>? namedGroups = null;
        Dictionary<string, RegExpMatchRange?>? namedGroupIndices = null;
        if (rx.NamedGroupNames.Length != 0)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var groupName in rx.NamedGroupNames)
            {
                if (!seen.Add(groupName))
                    continue;

                var group = match.Groups[groupName];
                (namedGroups ??= new(StringComparer.Ordinal))[groupName] = group.Success ? group.Value : null;
                if (groupIndices is not null)
                {
                    (namedGroupIndices ??= new(StringComparer.Ordinal))[groupName] = group.Success
                        ? new(group.Index, group.Index + group.Length)
                        : null;
                }
            }
        }

        return new(match.Index, match.Length, groups, namedGroups, groupIndices, namedGroupIndices);
    }

    private static JsArray CreateMatchIndicesArray(JsRealm realm, RegExpMatchRange?[] ranges)
    {
        var indices = realm.CreateArrayObject();
        for (var i = 0; i < ranges.Length; i++)
        {
            var value = ranges[i].HasValue
                ? CreateMatchIndexPairArray(realm, ranges[i]!.Value)
                : JsValue.Undefined;
            FreshArrayOperations.DefineElement(indices, (uint)i, value);
        }

        return indices;
    }

    private static JsValue CreateMatchIndexPairArray(JsRealm realm, in RegExpMatchRange range)
    {
        var pair = realm.CreateArrayObject();
        FreshArrayOperations.DefineElement(pair, 0, JsValue.FromInt32(range.Start));
        FreshArrayOperations.DefineElement(pair, 1, JsValue.FromInt32(range.End));
        return JsValue.FromObject(pair);
    }

    internal static bool Test(JsRealm realm, JsRegExpObject rx, string input)
    {
        return !Exec(realm, rx, input).IsNull;
    }

    private static Regex BuildRegexForExecution(JsRegExpObject rx)
    {
        var options = RegexOptions.CultureInvariant;
        if (rx.IgnoreCase)
            options |= RegexOptions.IgnoreCase;
        if (rx.IgnoreCase && !rx.Unicode)
            options |= RegexOptions.ECMAScript;
        try
        {
            if (rx.Pattern == "[]")
                return new("(?!)", options);
            if (rx.Pattern == "[^]")
                return new(@"[\s\S]", options);
            return new(rx.ExecutionPattern, options);
        }
        catch (ArgumentException ex)
        {
            throw new JsRuntimeException(JsErrorKind.SyntaxError, ex.Message, "REGEXP_INVALID_PATTERN");
        }
    }

    internal static PreparedPattern PreparePatternForExecution(string pattern, bool unicode, bool dotAll,
        bool multiline, bool ignoreCase)
    {
        var namedGroupNames = ExtractNamedGroupNames(pattern, unicode, out var definitionStarts, out var references);
        var executionPattern = RewriteForwardNamedBackreferences(pattern, definitionStarts, references);
        executionPattern = NormalizeAssertionsAndDotForDotNet(executionPattern, unicode, dotAll, multiline, ignoreCase);
        executionPattern = NormalizePatternForDotNet(executionPattern, unicode, dotAll,
            hasNamedCapturingGroups: definitionStarts.Count != 0);

        try
        {
            if (pattern != "[]" && pattern != "[^]")
                _ = new Regex(executionPattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            throw new JsRuntimeException(JsErrorKind.SyntaxError, ex.Message, "REGEXP_INVALID_PATTERN");
        }

        return new(executionPattern, namedGroupNames);
    }

    private static string NormalizeAssertionsAndDotForDotNet(string pattern, bool unicode, bool dotAll, bool multiline,
        bool ignoreCase)
    {
        var firstRelevant = pattern.IndexOfAny(['.', '^', '$', '(']);
        if (firstRelevant < 0)
            return pattern;

        var sb = new StringBuilder(pattern.Length + 32);
        var modifierStack = new Stack<EffectiveModifiers>();
        modifierStack.Push(new(dotAll, multiline, ignoreCase));

        var escaped = false;
        var inCharacterClass = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (escaped)
            {
                sb.Append(ch);
                escaped = false;
                continue;
            }

            if (inCharacterClass)
            {
                sb.Append(ch);
                if (ch == '\\')
                    escaped = true;
                else if (ch == ']')
                    inCharacterClass = false;
                continue;
            }

            if (ch == '\\')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] is 'b' or 'B')
                {
                    var boundaryModifiers = modifierStack.Peek();
                    sb.Append(pattern[i + 1] == 'b'
                        ? BuildWordBoundaryAtom(unicode, boundaryModifiers.IgnoreCase)
                        : BuildNotWordBoundaryAtom(unicode, boundaryModifiers.IgnoreCase));
                    i++;
                    continue;
                }

                sb.Append(ch);
                escaped = true;
                continue;
            }

            if (ch == '[')
            {
                sb.Append(ch);
                inCharacterClass = true;
                continue;
            }

            if (ch == '(')
            {
                if (TryParseScopedModifierGroup(pattern, i, modifierStack.Peek(), out var headerLength,
                        out var scopedModifiers))
                {
                    modifierStack.Push(scopedModifiers);
                    sb.Append(pattern, i, headerLength);
                    i += headerLength - 1;
                    continue;
                }

                modifierStack.Push(modifierStack.Peek());
                sb.Append(ch);
                continue;
            }

            if (ch == ')')
            {
                if (modifierStack.Count > 1)
                    modifierStack.Pop();
                sb.Append(ch);
                continue;
            }

            var currentModifiers = modifierStack.Peek();
            switch (ch)
            {
                case '.':
                    sb.Append(BuildDotMatcherAtom(unicode, currentModifiers.DotAll));
                    continue;
                case '^':
                    sb.Append(currentModifiers.Multiline
                        ? @"(?:\A|(?<=[\n\r\u2028\u2029]))"
                        : @"\A");
                    continue;
                case '$':
                    sb.Append(currentModifiers.Multiline
                        ? @"(?=\z|[\n\r\u2028\u2029])"
                        : @"\z");
                    continue;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }

    private static bool TryParseScopedModifierGroup(
        string pattern,
        int groupStart,
        EffectiveModifiers currentModifiers,
        out int headerLength,
        out EffectiveModifiers scopedModifiers)
    {
        headerLength = 0;
        scopedModifiers = currentModifiers;

        if (groupStart + 3 >= pattern.Length || pattern[groupStart] != '(' || pattern[groupStart + 1] != '?')
            return false;

        var pos = groupStart + 2;
        var addIgnoreCase = false;
        var addMultiline = false;
        var addDotAll = false;
        var removeIgnoreCase = false;
        var removeMultiline = false;
        var removeDotAll = false;
        var sawAnyModifier = false;

        while (pos < pattern.Length && TryConsumeScopedModifierFlag(pattern[pos], out var addFlag))
        {
            sawAnyModifier = true;
            ApplyScopedModifier(addFlag, true, ref addIgnoreCase, ref addMultiline, ref addDotAll,
                ref removeIgnoreCase, ref removeMultiline, ref removeDotAll);
            pos++;
        }

        if (pos < pattern.Length && pattern[pos] == '-')
        {
            pos++;
            while (pos < pattern.Length && TryConsumeScopedModifierFlag(pattern[pos], out var removeFlag))
            {
                sawAnyModifier = true;
                ApplyScopedModifier(removeFlag, false, ref addIgnoreCase, ref addMultiline, ref addDotAll,
                    ref removeIgnoreCase, ref removeMultiline, ref removeDotAll);
                pos++;
            }
        }

        if (!sawAnyModifier || pos >= pattern.Length || pattern[pos] != ':')
            return false;

        scopedModifiers = new(
            removeDotAll ? false : addDotAll ? true : currentModifiers.DotAll,
            removeMultiline ? false : addMultiline ? true : currentModifiers.Multiline,
            removeIgnoreCase ? false : addIgnoreCase ? true : currentModifiers.IgnoreCase);
        headerLength = pos - groupStart + 1;
        return true;
    }

    private static bool TryConsumeScopedModifierFlag(char ch, out char flag)
    {
        flag = ch;
        return ch is 'i' or 'm' or 's';
    }

    private static void ApplyScopedModifier(
        char flag,
        bool isAdd,
        ref bool addIgnoreCase,
        ref bool addMultiline,
        ref bool addDotAll,
        ref bool removeIgnoreCase,
        ref bool removeMultiline,
        ref bool removeDotAll)
    {
        switch (flag)
        {
            case 'i':
                if (isAdd) addIgnoreCase = true;
                else removeIgnoreCase = true;
                break;
            case 'm':
                if (isAdd) addMultiline = true;
                else removeMultiline = true;
                break;
            case 's':
                if (isAdd) addDotAll = true;
                else removeDotAll = true;
                break;
        }
    }

    private static string BuildDotMatcherAtom(bool unicode, bool dotAll)
    {
        if (!unicode)
            return dotAll ? @"[\s\S]" : @"[^\n\r\u2028\u2029]";

        return dotAll
            ? @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\uD800-\uDFFF])"
            : @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\n\r\u2028\u2029\uD800-\uDFFF])";
    }

    private static string BuildWordBoundaryAtom(bool unicode, bool ignoreCase)
    {
        var word = BuildWordCharacterClass(unicode, ignoreCase);
        return $@"(?:(?<={word})(?!{word})|(?<!{word})(?={word}))";
    }

    private static string BuildNotWordBoundaryAtom(bool unicode, bool ignoreCase)
    {
        var word = BuildWordCharacterClass(unicode, ignoreCase);
        return $@"(?:(?<={word})(?={word})|(?<!{word})(?!{word}))";
    }

    private static string BuildWordCharacterClass(bool unicode, bool ignoreCase)
    {
        if (unicode && ignoreCase)
            return @"[A-Za-z0-9_\u017F\u212A]";
        return @"[A-Za-z0-9_]";
    }

    private static long GetLastIndex(JsRealm realm, JsRegExpObject rx)
    {
        if (!rx.TryGetPropertyAtom(realm, IdLastIndex, out var lastIndexValue, out _))
            return 0;
        return realm.ToLength(lastIndexValue);
    }

    private static void SetLastIndex(JsRealm realm, JsRegExpObject rx, int value)
    {
        if (!rx.TrySetPropertyAtom(realm, IdLastIndex, JsValue.FromInt32(value), out _))
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "RegExp.prototype.exec failed to set required property");
    }

    private static long ToLength(JsRealm realm, in JsValue value)
    {
        var number = realm.ToNumberSlowPath(value);
        if (double.IsNaN(number) || number <= 0d)
            return 0;
        const double maxSafeInteger = 9007199254740991d;
        if (double.IsPositiveInfinity(number))
            return (long)maxSafeInteger;
        return (long)Math.Min(maxSafeInteger, Math.Floor(number));
    }

    private static void ThrowInvalidFlags()
    {
        throw new JsRuntimeException(JsErrorKind.SyntaxError, "Invalid regular expression flags",
            "REGEXP_INVALID_FLAGS");
    }

    // Phase-2 bridge: .NET regex does not support JS /\u{...}/ syntax directly.
    internal static int Search(JsRealm realm, JsRegExpObject rx, string input)
    {
        // String.prototype.search should not persist lastIndex side effects.
        var oldLastIndex = rx.TryGetPropertyAtom(realm, IdLastIndex, out var currentLastIndex, out _)
            ? currentLastIndex
            : JsValue.Undefined;
        try
        {
            SetLastIndex(realm, rx, 0);
            var result = Exec(realm, rx, input);
            if (result.IsNull || !result.TryGetObject(out var matchObj))
                return -1;
            return matchObj.TryGetPropertyByAtom(IdIndex, out var indexValue) && indexValue.IsNumber
                ? (int)indexValue.NumberValue
                : -1;
        }
        finally
        {
            if (!rx.TrySetPropertyAtom(realm, IdLastIndex, oldLastIndex, out _))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "RegExp.prototype.search failed to restore lastIndex");
        }
    }

    private static string NormalizePatternForDotNet(string pattern, bool unicode, bool dotAll,
        bool hasNamedCapturingGroups = false)
    {
        if (!unicode)
            return NormalizeNonUnicodeIdentityEscapesForDotNet(pattern, hasNamedCapturingGroups);

        var unicodeBraceStart = pattern.IndexOf(@"\u{", StringComparison.Ordinal);
        var unicodeEscapeStart = pattern.IndexOf(@"\u", StringComparison.Ordinal);
        var surrogateStart = FindFirstSurrogatePairStart(pattern);
        var dotStart = pattern.IndexOf('.', StringComparison.Ordinal);
        var nonSpaceEscapeStart = pattern.IndexOf(@"\S", StringComparison.Ordinal);
        if (unicodeBraceStart < 0 && unicodeEscapeStart < 0 && surrogateStart < 0 && dotStart < 0 &&
            nonSpaceEscapeStart < 0)
            return pattern;

        var start = int.MaxValue;
        if (unicodeBraceStart >= 0 && unicodeBraceStart < start)
            start = unicodeBraceStart;
        if (unicodeEscapeStart >= 0 && unicodeEscapeStart < start)
            start = unicodeEscapeStart;
        if (surrogateStart >= 0 && surrogateStart < start)
            start = surrogateStart;
        if (dotStart >= 0 && dotStart < start)
            start = dotStart;
        if (nonSpaceEscapeStart >= 0 && nonSpaceEscapeStart < start)
            start = nonSpaceEscapeStart;
        var containingClassStart = FindContainingCharacterClassStart(pattern, start);
        if (containingClassStart >= 0)
            start = containingClassStart;

        var sb = new StringBuilder(pattern.Length);
        if (start > 0)
            sb.Append(pattern, 0, start);

        for (var i = start; i < pattern.Length; i++)
        {
            var ch = pattern[i];

            if (ch == '[')
            {
                var classEnd = FindCharacterClassEnd(pattern, i);
                if (classEnd > i)
                {
                    if (TryRewriteAstralCharacterClass(pattern, i + 1, classEnd, out var classReplacement))
                        sb.Append(classReplacement);
                    else
                        sb.Append(pattern, i, classEnd - i + 1);

                    i = classEnd;
                    continue;
                }
            }

            if (ch == '.')
            {
                sb.Append(dotAll
                    ? @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\uD800-\uDFFF])"
                    : @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\n\r\u2028\u2029\uD800-\uDFFF])");
                continue;
            }

            if (char.IsHighSurrogate(ch) && i + 1 < pattern.Length && char.IsLowSurrogate(pattern[i + 1]))
            {
                AppendSurrogatePairAsRegexAtom(sb, ch, pattern[i + 1]);
                i++;
                continue;
            }

            if (ch == '\\' && i + 3 < pattern.Length && pattern[i + 1] == 'u' && pattern[i + 2] == '{')
            {
                var close = i + 3;
                while (close < pattern.Length && pattern[close] != '}')
                    close++;
                if (close < pattern.Length && TryParseUnicodeCodePoint(pattern, i + 3, close, out var scalar))
                {
                    AppendEscapedRegexAtom(sb, scalar);
                    i = close;
                    continue;
                }
            }
            else if (ch == '\\')
            {
                if (i + 1 < pattern.Length)
                {
                    var next = pattern[i + 1];
                    if (next == 'S')
                    {
                        sb.Append(@"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\s\uD800-\uDFFF])");
                        i++;
                        continue;
                    }

                    sb.Append(ch);
                    sb.Append(next);
                    i++;
                    continue;
                }
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string NormalizeNonUnicodeIdentityEscapesForDotNet(string pattern, bool hasNamedCapturingGroups)
    {
        if (string.IsNullOrEmpty(pattern))
            return pattern;

        StringBuilder? sb = null;
        var copyStart = 0;
        var inCharacterClass = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (ch == '\\' && i + 1 < pattern.Length)
            {
                var next = pattern[i + 1];
                if (ShouldRewriteNonUnicodeIdentityEscape(pattern, i, next, inCharacterClass, hasNamedCapturingGroups))
                {
                    sb ??= new(pattern.Length);
                    if (i > copyStart)
                        sb.Append(pattern, copyStart, i - copyStart);
                    sb.Append(next);
                    i++;
                    copyStart = i + 1;
                    continue;
                }

                i++;
                continue;
            }

            if (!inCharacterClass)
            {
                if (ch == '[')
                    inCharacterClass = true;
            }
            else if (ch == ']')
            {
                inCharacterClass = false;
            }
        }

        if (sb is null)
            return pattern;

        if (copyStart < pattern.Length)
            sb.Append(pattern, copyStart, pattern.Length - copyStart);
        return sb.ToString();
    }

    private static bool ShouldRewriteNonUnicodeIdentityEscape(
        string pattern,
        int escapeStart,
        char escapedChar,
        bool inCharacterClass,
        bool hasNamedCapturingGroups)
    {
        switch (escapedChar)
        {
            case 'd':
            case 'D':
            case 's':
            case 'S':
            case 'w':
            case 'W':
            case 'f':
            case 'n':
            case 'r':
            case 't':
            case 'v':
                return false;
            case 'b':
                return false;
            case 'B':
                return !inCharacterClass ? false : true;
            case 'c':
                return escapeStart + 2 >= pattern.Length || !char.IsAsciiLetter(pattern[escapeStart + 2]);
            case 'x':
                return escapeStart + 3 >= pattern.Length ||
                       HexToInt(pattern[escapeStart + 2]) < 0 ||
                       HexToInt(pattern[escapeStart + 3]) < 0;
            case 'p':
            case 'P':
                return true;
            case 'k':
                return !hasNamedCapturingGroups && escapeStart + 2 < pattern.Length && pattern[escapeStart + 2] == '<';
            case 'u':
                return false;
            case '0':
                return false;
        }

        return char.IsAsciiLetter(escapedChar);
    }

    private static bool TryParseUnicodeCodePoint(string pattern, int start, int endExclusive, out int scalar)
    {
        scalar = 0;
        if (endExclusive <= start)
            return false;

        long value = 0;
        for (var i = start; i < endExclusive; i++)
        {
            var hex = HexToInt(pattern[i]);
            if (hex < 0)
                return false;
            value = (value << 4) | (uint)hex;
            if (value > 0x10FFFF)
                return false;
        }

        scalar = (int)value;

        return true;
    }

    private static int HexToInt(char ch)
    {
        if (ch is >= '0' and <= '9')
            return ch - '0';
        if (ch is >= 'a' and <= 'f')
            return ch - 'a' + 10;
        if (ch is >= 'A' and <= 'F')
            return ch - 'A' + 10;
        return -1;
    }

    private static void AppendEscapedRegexAtom(StringBuilder sb, int scalar)
    {
        if (scalar <= 0xFFFF && IsRegexMetaChar((char)scalar))
            sb.Append('\\');
        sb.Append(char.ConvertFromUtf32(scalar));
    }

    private static bool IsRegexMetaChar(char ch)
    {
        return ch is '\\' or '^' or '$' or '.' or '|' or '?' or '*' or '+' or '(' or ')' or '[' or ']' or '{' or '}';
    }

    private static int FindFirstSurrogatePairStart(string pattern)
    {
        for (var i = 0; i + 1 < pattern.Length; i++)
            if (char.IsHighSurrogate(pattern[i]) && char.IsLowSurrogate(pattern[i + 1]))
                return i;

        return -1;
    }

    private static int FindCharacterClassEnd(string pattern, int classStart)
    {
        var escaped = false;
        for (var i = classStart + 1; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == ']')
                return i;
        }

        return -1;
    }

    private static int FindContainingCharacterClassStart(string pattern, int index)
    {
        var escaped = false;
        var openClass = -1;
        for (var i = 0; i < index && i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '[')
            {
                openClass = i;
                continue;
            }

            if (ch == ']')
                openClass = -1;
        }

        return openClass;
    }

    private static bool TryRewriteAstralCharacterClass(string pattern, int contentStart, int classEnd,
        out string? replacement)
    {
        replacement = null;
        if (contentStart >= classEnd)
            return false;
        var negated = pattern[contentStart] == '^';
        if (negated)
            contentStart++;
        if (contentStart >= classEnd)
            return false;

        var pos = contentStart;
        if (!TryParseAstralClassAtom(pattern, ref pos, classEnd, out var high1, out var low1))
            return false;

        if (negated)
        {
            if (pos != classEnd)
                return false;
            replacement = BuildNegatedSingleSurrogatePairCodePointAtom(high1, low1);
            return true;
        }

        if (pos == classEnd)
        {
            replacement = BuildSurrogatePairRegexAtom(high1, low1);
            return true;
        }

        if (pattern[pos] != '-')
            return false;
        pos++;
        if (!TryParseAstralClassAtom(pattern, ref pos, classEnd, out var high2, out var low2))
            return false;
        if (pos != classEnd)
            return false;

        // Common case needed for test262 u-astral range: [💩-💫] (same high surrogate).
        if (high1 == high2 && low1 <= low2)
        {
            replacement = $"(?:\\u{(int)high1:X4}[\\u{(int)low1:X4}-\\u{(int)low2:X4}])";
            return true;
        }

        return false;
    }

    private static bool TryParseAstralClassAtom(string pattern, ref int pos, int classEnd, out char high, out char low)
    {
        high = '\0';
        low = '\0';
        if (pos >= classEnd)
            return false;

        if (pos + 1 < classEnd &&
            char.IsHighSurrogate(pattern[pos]) &&
            char.IsLowSurrogate(pattern[pos + 1]))
        {
            high = pattern[pos];
            low = pattern[pos + 1];
            pos += 2;
            return true;
        }

        if (pos + 12 <= classEnd &&
            pattern[pos] == '\\' && pattern[pos + 1] == 'u' &&
            pattern[pos + 6] == '\\' && pattern[pos + 7] == 'u' &&
            TryParseHex16(pattern, pos + 2, out var highInt) &&
            TryParseHex16(pattern, pos + 8, out var lowInt) &&
            char.IsHighSurrogate((char)highInt) &&
            char.IsLowSurrogate((char)lowInt))
        {
            high = (char)highInt;
            low = (char)lowInt;
            pos += 12;
            return true;
        }

        return false;
    }

    private static bool TryParseHex16(string text, int start, out int value)
    {
        value = 0;
        if (start + 4 > text.Length)
            return false;
        for (var i = start; i < start + 4; i++)
        {
            var hex = HexToInt(text[i]);
            if (hex < 0)
                return false;
            value = (value << 4) | hex;
        }

        return true;
    }

    private static void AppendSurrogatePairAsRegexAtom(StringBuilder sb, char high, char low)
    {
        sb.Append(BuildSurrogatePairRegexAtom(high, low));
    }

    private static string BuildSurrogatePairRegexAtom(char high, char low)
    {
        return $"(?:\\u{(int)high:X4}\\u{(int)low:X4})";
    }

    private static string BuildNegatedSingleSurrogatePairCodePointAtom(char high, char low)
    {
        var pair = $"\\u{(int)high:X4}\\u{(int)low:X4}";
        return
            $"(?:(?!{pair})[\\uD800-\\uDBFF][\\uDC00-\\uDFFF]|[\\uD800-\\uDBFF](?![\\uDC00-\\uDFFF])|(?<![\\uD800-\\uDBFF])[\\uDC00-\\uDFFF]|[^\\uD800-\\uDFFF])";
    }

    internal static string[] ExtractNamedGroupNames(string pattern, bool unicode = false)
    {
        return ExtractNamedGroupNames(pattern, unicode, out _, out _);
    }

    private static string[] ExtractNamedGroupNames(
        string pattern,
        bool unicode,
        out Dictionary<string, int> definitionStarts,
        out List<NamedBackreference> references)
    {
        definitionStarts = new(StringComparer.Ordinal);
        references = [];
        if (string.IsNullOrEmpty(pattern))
            return [];

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var escaped = false;
        var inCharacterClass = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                if (!inCharacterClass &&
                    i + 3 < pattern.Length &&
                    pattern[i + 1] == 'k' &&
                    pattern[i + 2] == '<')
                {
                    var referenceNameStart = i + 3;
                    var referenceNameEnd = referenceNameStart;
                    while (referenceNameEnd < pattern.Length && pattern[referenceNameEnd] != '>')
                        referenceNameEnd++;
                    if (referenceNameEnd < pattern.Length && referenceNameEnd > referenceNameStart)
                    {
                        var referenceName =
                            pattern.Substring(referenceNameStart, referenceNameEnd - referenceNameStart);
                        ValidateNamedCaptureIdentifier(referenceName);
                        references.Add(new(referenceName, i, referenceNameEnd + 1));
                        i = referenceNameEnd;
                        continue;
                    }
                }

                escaped = true;
                continue;
            }

            if (inCharacterClass)
            {
                if (ch == ']')
                    inCharacterClass = false;
                continue;
            }

            if (ch == '[')
            {
                inCharacterClass = true;
                continue;
            }

            if (ch != '(' || i + 3 >= pattern.Length || pattern[i + 1] != '?' || pattern[i + 2] != '<')
                continue;

            var discriminator = pattern[i + 3];
            if (discriminator is '=' or '!')
                continue;

            var nameStart = i + 3;
            var nameEnd = nameStart;
            while (nameEnd < pattern.Length && pattern[nameEnd] != '>')
                nameEnd++;
            if (nameEnd >= pattern.Length || nameEnd == nameStart)
                continue;

            var name = pattern.Substring(nameStart, nameEnd - nameStart);
            ValidateNamedCaptureIdentifier(name);
            if (seen.Add(name))
                names.Add(name);
            definitionStarts.TryAdd(name, i);
        }

        if (!unicode && definitionStarts.Count == 0)
        {
            references.Clear();
            return names.ToArray();
        }

        foreach (var reference in references)
            if (!definitionStarts.ContainsKey(reference.Name))
                throw new JsRuntimeException(
                    JsErrorKind.SyntaxError,
                    "Invalid named capture referenced",
                    "REGEXP_INVALID_PATTERN");

        return names.ToArray();
    }

    private static string RewriteForwardNamedBackreferences(
        string pattern,
        Dictionary<string, int> definitionStarts,
        List<NamedBackreference> references)
    {
        StringBuilder? sb = null;
        var copyStart = 0;

        foreach (var reference in references)
        {
            if (!definitionStarts.TryGetValue(reference.Name, out var definitionStart) ||
                reference.Start >= definitionStart)
                continue;

            sb ??= new(pattern.Length);
            if (reference.Start > copyStart)
                sb.Append(pattern, copyStart, reference.Start - copyStart);
            sb.Append("(?:)");
            copyStart = reference.EndExclusive;
        }

        if (sb is null)
            return pattern;

        if (copyStart < pattern.Length)
            sb.Append(pattern, copyStart, pattern.Length - copyStart);

        return sb.ToString();
    }

    private static void ValidateNamedCaptureIdentifier(string name)
    {
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (!char.IsSurrogate(ch))
                continue;

            if (char.IsHighSurrogate(ch) &&
                i + 1 < name.Length &&
                char.IsLowSurrogate(name[i + 1]))
            {
                i++;
                continue;
            }

            throw new JsRuntimeException(
                JsErrorKind.SyntaxError,
                "Invalid capture group name",
                "REGEXP_INVALID_PATTERN");
        }
    }

    internal readonly record struct PreparedPattern(string ExecutionPattern, string[] NamedGroupNames);

    private readonly record struct NamedBackreference(string Name, int Start, int EndExclusive);

    internal readonly struct Flags
    {
        internal readonly bool Global;
        internal readonly bool IgnoreCase;
        internal readonly bool Multiline;
        internal readonly bool HasIndices;
        internal readonly bool Sticky;
        internal readonly bool Unicode;
        internal readonly bool UnicodeSets;
        internal readonly bool DotAll;

        internal Flags(bool global, bool ignoreCase, bool multiline, bool hasIndices, bool sticky, bool unicode,
            bool unicodeSets, bool dotAll)
        {
            Global = global;
            IgnoreCase = ignoreCase;
            Multiline = multiline;
            HasIndices = hasIndices;
            Sticky = sticky;
            Unicode = unicode;
            UnicodeSets = unicodeSets;
            DotAll = dotAll;
        }
    }

    private readonly record struct EffectiveModifiers(bool DotAll, bool Multiline, bool IgnoreCase);
}
