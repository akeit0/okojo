using System.Text;
using Okojo.JavaScript.RegExp;
using Okojo.Text.RegularExpressions;

namespace Okojo.JavaScript.Execution;

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

internal static JsValue Exec(JsRealm realm, JsRegExpObject rx, string input)
{
    var match = ExecMatchResult(realm, rx, input);
    return match is null ? JsValue.Null : BuildExecResult(realm, rx, match, input);
}

/// <summary>
///     One intrinsic RegExpExec step (R8-regexp): mirrors
///     RegExpBuiltinExec exactly - lastIndex read/written through the
///     receiver's property path so accessors stay observable - but returns
///     only raw ranges (no result object, no capture substrings).
///     Returns null when there is no match.
/// </summary>
internal static RegExpEngineStep? IntrinsicExecStep(
    JsRealm realm,
    JsRegExpObject rx,
    string input
)
{
    var global = rx.Global;
    var sticky = rx.Sticky;
    var useLastIndex = global || sticky;
    var lastIndex = GetLastIndex(realm, rx);
    var startIndex = useLastIndex ? (int)Math.Min(lastIndex, int.MaxValue) : 0;

    if (useLastIndex && lastIndex > input.Length)
    {
        SetLastIndex(realm, rx, 0);
        return null;
    }

    if (
        !RegExpEngine.Default.TryMatchRanges(
            rx.CompiledPattern,
            input,
            startIndex,
            out var index,
            out var length,
            out var ranges,
            out var rangeCount
        )
    )
    {
        if (useLastIndex)
            SetLastIndex(realm, rx, 0);
        return null;
    }

    if (useLastIndex)
        SetLastIndex(realm, rx, index + length);

    return new RegExpEngineStep(index, length, ranges, rangeCount);
}

internal readonly record struct RegExpEngineStep(
    int Index,
    int Length,
    CaptureRange[] Ranges,
    int RangeCount
);

    internal static RegExpMatchResult? ExecMatchResult(
        JsRealm realm,
        JsRegExpObject rx,
        string input
    )
    {
        var lastIndex = GetLastIndex(realm, rx);
        var global = rx.Global;
        var sticky = rx.Sticky;
        var useLastIndex = global || sticky;
        var startIndex = useLastIndex ? (int)Math.Min(lastIndex, int.MaxValue) : 0;

        if (useLastIndex && lastIndex > input.Length)
        {
            SetLastIndex(realm, rx, 0);
            return null;
        }

        var engineMatch = RegExpEngine.Default.Exec(rx.CompiledPattern, input, startIndex);
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

    private static JsValue BuildExecResult(
        JsRealm realm,
        JsRegExpObject rx,
        RegExpMatchResult match,
        string input
    )
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
                groups.DefineDataProperty(
                    groupName,
                    groupValue is null ? JsValue.Undefined : JsValue.FromString(groupValue),
                    JsShapePropertyFlags.Open
                );
            }

            groupsValue = JsValue.FromObject(groups);
        }

        array.DefineDataPropertyAtom(
            realm,
            AtomTable.IdGroups,
            groupsValue,
            JsShapePropertyFlags.Open
        );
        if (rx.CompiledPattern.ParsedFlags.HasIndices)
        {
            var indices = CreateMatchIndicesArray(
                realm,
                match.GroupIndices ?? new RegExpMatchRange?[match.Groups.Length]
            );
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
                    groups.DefineDataProperty(
                        groupName,
                        range.HasValue
                            ? CreateMatchIndexPairArray(realm, range.Value)
                            : JsValue.Undefined,
                        JsShapePropertyFlags.Open
                    );
                }

                indexGroupsValue = JsValue.FromObject(groups);
            }

            indices.DefineDataPropertyAtom(
                realm,
                AtomTable.IdGroups,
                indexGroupsValue,
                JsShapePropertyFlags.Open
            );
            array.DefineDataProperty(
                "indices",
                JsValue.FromObject(indices),
                JsShapePropertyFlags.Open
            );
        }

        array.DefineDataProperty(
            "index",
            JsValue.FromInt32(match.Index),
            JsShapePropertyFlags.Open
        );
        array.DefineDataProperty("input", JsValue.FromString(input), JsShapePropertyFlags.Open);
        return array;
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

    private static long GetLastIndex(JsRealm realm, JsRegExpObject rx)
    {
        if (!rx.TryGetPropertyAtom(realm, IdLastIndex, out var lastIndexValue, out _))
            return 0;
        return realm.ToLength(lastIndexValue);
    }

    private static void SetLastIndex(JsRealm realm, JsRegExpObject rx, int value)
    {
        if (!rx.TrySetPropertyAtom(realm, IdLastIndex, JsValue.FromInt32(value), out _))
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "RegExp.prototype.exec failed to set required property"
            );
    }

    private static void ThrowInvalidFlags()
    {
        throw new JsRuntimeException(
            JsErrorKind.SyntaxError,
            "Invalid regular expression flags",
            "REGEXP_INVALID_FLAGS"
        );
    }

    internal static int Search(JsRealm realm, JsRegExpObject rx, string input)
    {
        // String.prototype.search should not persist lastIndex side effects.
        var oldLastIndex = rx.TryGetPropertyAtom(
            realm,
            IdLastIndex,
            out var currentLastIndex,
            out _
        )
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
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    "RegExp.prototype.search failed to restore lastIndex"
                );
        }
    }

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

        internal Flags(
            bool global,
            bool ignoreCase,
            bool multiline,
            bool hasIndices,
            bool sticky,
            bool unicode,
            bool unicodeSets,
            bool dotAll
        )
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
}
