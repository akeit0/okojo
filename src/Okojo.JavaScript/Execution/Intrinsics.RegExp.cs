using System.Text;
using Okojo.JavaScript.Internals;
using Okojo.JavaScript.Parsing;
using Okojo.JavaScript.RegExp;

namespace Okojo.JavaScript.Execution;

public partial class Intrinsics
{
    private JsHostFunction? _regExpPrototypeExec;

    /// <summary>
    ///     True when <paramref name="receiver"/> resolves 'exec' to this
    ///     realm's original RegExp.prototype.exec builtin, enabling raw-step
    ///     fast paths that would otherwise be observable overrides.
    /// </summary>
    internal bool IsDefaultRegExpExec(JsRealm realm, JsObject receiver)
    {
        if (_regExpPrototypeExec is null)
            return false;
        if (!receiver.TryGetPropertyAtom(realm, AtomTable.IdExec, out var value, out _))
            return false;
        return value.TryGetObject(out var obj) && ReferenceEquals(obj, _regExpPrototypeExec);
    }

    private JsHostFunction CreateRegExpConstructor()
    {
        return new(
            Realm,
            (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                var patternValue = args.Length > 0 ? args[0] : JsValue.Undefined;
                var flagsValue = args.Length > 1 ? args[1] : JsValue.Undefined;

                if (
                    TryGetRegExpPatternAndFlags(
                        realm,
                        in patternValue,
                        in flagsValue,
                        out var pattern,
                        out var flags,
                        out var originalPattern
                    )
                )
                {
                    if (
                        !info.IsConstruct
                        && flagsValue.IsUndefined
                        && patternValue.TryGetObject(out var patternObj)
                        && IsRegExpLike(realm, patternObj)
                        && patternObj.TryGetPropertyAtom(
                            realm,
                            IdConstructor,
                            out var ctorValue,
                            out _
                        )
                        && ctorValue.TryGetObject(out var ctorObj)
                        && ReferenceEquals(ctorObj, info.Function)
                    )
                        return originalPattern;

                    return realm.CreateRegExpObject(pattern, flags, in info);
                }

                return realm.CreateRegExpObject("(?:)", string.Empty, in info);
            },
            "RegExp",
            2,
            true
        );
    }

    private static bool IsRegExpLike(JsRealm realm, JsObject obj)
    {
        if (
            obj.TryGetPropertyAtom(realm, IdSymbolMatch, out var matchValue, out _)
            && !matchValue.IsUndefined
        )
            return matchValue.ToBoolean();

        return obj is JsRegExpObject;
    }

    private static bool TryGetRegExpPatternAndFlags(
        JsRealm realm,
        in JsValue patternValue,
        in JsValue flagsValue,
        out string pattern,
        out string flags,
        out JsValue originalPattern
    )
    {
        originalPattern = patternValue;
        if (!patternValue.TryGetObject(out var patternObj))
        {
            pattern = patternValue.IsUndefined ? "(?:)" : realm.ToJsStringSlowPath(patternValue);
            flags = flagsValue.IsUndefined ? string.Empty : realm.ToJsStringSlowPath(flagsValue);
            return true;
        }

        var isRegExpLike = IsRegExpLike(realm, patternObj);

        if (!isRegExpLike)
        {
            pattern = patternValue.IsUndefined ? "(?:)" : realm.ToJsStringSlowPath(patternValue);
            flags = flagsValue.IsUndefined ? string.Empty : realm.ToJsStringSlowPath(flagsValue);
            return true;
        }

        if (patternObj is JsRegExpObject patternRegExp)
        {
            pattern = patternRegExp.Pattern;
            flags = flagsValue.IsUndefined
                ? patternRegExp.Flags
                : realm.ToJsStringSlowPath(flagsValue);
            return true;
        }

        if (!patternObj.TryGetPropertyAtom(realm, IdSource, out var sourceValue, out _))
            sourceValue = JsValue.Undefined;
        pattern = realm.ToJsStringSlowPath(sourceValue);

        if (flagsValue.IsUndefined)
        {
            if (!patternObj.TryGetPropertyAtom(realm, IdFlags, out var objFlagsValue, out _))
                objFlagsValue = JsValue.Undefined;
            flags = realm.ToJsStringSlowPath(objFlagsValue);
        }
        else
        {
            flags = realm.ToJsStringSlowPath(flagsValue);
        }

        return true;
    }

    private void InstallRegExpConstructorBuiltins()
    {
        const int execAtom = IdExec;
        const int testAtom = IdTest;
        var hasIndicesAtom = Atoms.InternNoCheck("hasIndices");
        var unicodeSetsAtom = Atoms.InternNoCheck("unicodeSets");
        var speciesGetter = new JsHostFunction(
            Realm,
            (in info) =>
            {
                return info.ThisValue;
            },
            "get [Symbol.species]",
            0
        );

        static string ApplyReplacementTemplate(
            string matched,
            string input,
            int position,
            ReadOnlySpan<string?> captures,
            JsObject? namedCaptures,
            JsRealm realm,
            string replacementTemplate
        )
        {
            if (replacementTemplate.Length == 0)
                return string.Empty;

            var stringLength = input.Length;
            var result = new StringBuilder(replacementTemplate.Length);
            var cursor = 0;
            while (cursor < replacementTemplate.Length)
            {
                var ch = replacementTemplate[cursor];
                if (ch != '$' || cursor + 1 >= replacementTemplate.Length)
                {
                    result.Append(ch);
                    cursor++;
                    continue;
                }

                var next = replacementTemplate[cursor + 1];
                switch (next)
                {
                    case '$':
                        result.Append('$');
                        cursor += 2;
                        continue;
                    case '&':
                        result.Append(matched);
                        cursor += 2;
                        continue;
                    case '`':
                        result.Append(input, 0, position);
                        cursor += 2;
                        continue;
                    case '\'':
                    {
                        var tailPos = Math.Min(position + matched.Length, stringLength);
                        if (tailPos < stringLength)
                            result.Append(input, tailPos, stringLength - tailPos);
                        cursor += 2;
                        continue;
                    }
                }

                if (char.IsAsciiDigit(next))
                {
                    var digitCount =
                        cursor + 2 < replacementTemplate.Length
                        && char.IsAsciiDigit(replacementTemplate[cursor + 2])
                            ? 2
                            : 1;
                    var firstDigit = next - '0';
                    var secondDigit = digitCount == 2 ? replacementTemplate[cursor + 2] - '0' : 0;
                    var captureIndex = digitCount == 2 ? firstDigit * 10 + secondDigit : firstDigit;

                    if (captureIndex > captures.Length && digitCount == 2)
                    {
                        digitCount = 1;
                        captureIndex = firstDigit;
                    }

                    if ((uint)(captureIndex - 1) < (uint)captures.Length)
                    {
                        var capture = captures[captureIndex - 1];
                        if (capture is not null)
                            result.Append(capture);
                        cursor += 1 + digitCount;
                        continue;
                    }

                    result.Append(replacementTemplate, cursor, 1 + digitCount);
                    cursor += 1 + digitCount;
                    continue;
                }

                if (next == '<')
                {
                    var closeIndex = replacementTemplate.IndexOf('>', cursor + 2);
                    if (closeIndex < 0 || namedCaptures is null)
                    {
                        result.Append("$<");
                        cursor += 2;
                        continue;
                    }

                    var groupName = replacementTemplate.Substring(
                        cursor + 2,
                        closeIndex - (cursor + 2)
                    );
                    if (
                        !namedCaptures.TryGetProperty(groupName, out var captureValue)
                        || captureValue.IsUndefined
                    )
                    {
                        cursor = closeIndex + 1;
                        continue;
                    }

                    result.Append(realm.ToJsStringSlowPath(captureValue));
                    cursor = closeIndex + 1;
                    continue;
                }

                result.Append('$');
                cursor++;
            }

            return result.ToString();
        }

        static JsObject CreateNamedCapturesObject(
            JsRealm realm,
            IReadOnlyDictionary<string, string?> namedGroups
        )
        {
            JsPlainObject groups = new(realm, false) { Prototype = null };
            foreach (var pair in namedGroups)
            {
                groups.DefineDataProperty(
                    pair.Key,
                    pair.Value is null ? JsValue.Undefined : JsValue.FromString(pair.Value),
                    JsShapePropertyFlags.Open
                );
            }

            return groups;
        }

        static JsValue FromLengthValue(long value)
        {
            return value <= int.MaxValue ? JsValue.FromInt32((int)value) : new(value);
        }

        static string EscapeRegExpPattern(string pattern)
        {
            if (pattern.Length == 0)
                return "(?:)";

            var result = new StringBuilder(pattern.Length);
            var inCharClass = false;
            for (var i = 0; i < pattern.Length; )
            {
                var ch = pattern[i++];
                switch (ch)
                {
                    case '\\':
                        result.Append(ch);
                        if (i < pattern.Length)
                            result.Append(pattern[i++]);
                        continue;
                    case '[':
                        if (!inCharClass && i < pattern.Length && pattern[i] == ']')
                        {
                            result.Append("[]");
                            i++;
                            inCharClass = true;
                            continue;
                        }

                        inCharClass = true;
                        break;
                    case ']':
                        inCharClass = false;
                        break;
                    case '\n':
                        result.Append("\\n");
                        continue;
                    case '\r':
                        result.Append("\\r");
                        continue;
                    case '/':
                        if (!inCharClass)
                        {
                            result.Append("\\/");
                            continue;
                        }

                        break;
                    default:
                        if (ch is '\u2028' or '\u2029')
                        {
                            result.Append("\\u");
                            result.Append(((int)ch).ToString("x4"));
                            continue;
                        }

                        break;
                }

                result.Append(ch);
            }

            return result.ToString();
        }

        static bool IsAsciiLetterOrDigit(int codePoint)
        {
            return codePoint is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';
        }

        static bool IsRegExpSyntaxCharacter(int codePoint)
        {
            return codePoint
                is '^'
                    or '$'
                    or '\\'
                    or '.'
                    or '*'
                    or '+'
                    or '?'
                    or '('
                    or ')'
                    or '['
                    or ']'
                    or '{'
                    or '}'
                    or '|'
                    or '/';
        }

        static bool IsOtherRegExpPunctuator(int codePoint)
        {
            return codePoint
                is ','
                    or '-'
                    or '='
                    or '<'
                    or '>'
                    or '#'
                    or '&'
                    or '!'
                    or '%'
                    or ':'
                    or ';'
                    or '@'
                    or '~'
                    or '\''
                    or '`'
                    or '"';
        }

        static bool IsRegExpWhitespaceOrLineTerminator(int codePoint)
        {
            return codePoint switch
            {
                0x0009
                or 0x000A
                or 0x000B
                or 0x000C
                or 0x000D
                or 0x0020
                or 0x00A0
                or 0x1680
                or 0x2000
                or 0x2001
                or 0x2002
                or 0x2003
                or 0x2004
                or 0x2005
                or 0x2006
                or 0x2007
                or 0x2008
                or 0x2009
                or 0x200A
                or 0x2028
                or 0x2029
                or 0x202F
                or 0x205F
                or 0x3000
                or 0xFEFF => true,
                _ => false,
            };
        }

        static void AppendHexEscape(StringBuilder builder, int codeUnit)
        {
            builder.Append("\\x");
            builder.Append(codeUnit.ToString("x2"));
        }

        static void AppendUnicodeEscape(StringBuilder builder, int codeUnit)
        {
            builder.Append("\\u");
            builder.Append(codeUnit.ToString("x4"));
        }

        static void AppendRegExpEscape(
            StringBuilder builder,
            int codePoint,
            ReadOnlySpan<char> units,
            bool loneSurrogate
        )
        {
            switch (codePoint)
            {
                case '\t':
                    builder.Append("\\t");
                    return;
                case '\n':
                    builder.Append("\\n");
                    return;
                case '\v':
                    builder.Append("\\v");
                    return;
                case '\f':
                    builder.Append("\\f");
                    return;
                case '\r':
                    builder.Append("\\r");
                    return;
            }

            if (IsRegExpSyntaxCharacter(codePoint))
            {
                builder.Append('\\');
                builder.Append(units[0]);
                return;
            }

            if (
                loneSurrogate
                || IsOtherRegExpPunctuator(codePoint)
                || IsRegExpWhitespaceOrLineTerminator(codePoint)
            )
            {
                if (!loneSurrogate && codePoint <= 0xFF)
                {
                    AppendHexEscape(builder, codePoint);
                    return;
                }

                for (var i = 0; i < units.Length; i++)
                    AppendUnicodeEscape(builder, units[i]);
                return;
            }

            builder.Append(units);
        }

        static string EscapeRegExpString(string text)
        {
            if (text.Length == 0)
                return string.Empty;

            var builder = new StringBuilder(text.Length);
            var index = 0;
            var first = true;
            while (index < text.Length)
            {
                var start = index;
                var ch = text[index++];
                int codePoint = ch;
                var loneSurrogate = char.IsSurrogate(ch);
                if (
                    char.IsHighSurrogate(ch)
                    && index < text.Length
                    && char.IsLowSurrogate(text[index])
                )
                {
                    loneSurrogate = false;
                    codePoint = char.ConvertToUtf32(ch, text[index]);
                    index++;
                }

                var units = text.AsSpan(start, index - start);
                if (first && IsAsciiLetterOrDigit(codePoint))
                    AppendHexEscape(builder, units[0]);
                else
                    AppendRegExpEscape(builder, codePoint, units, loneSurrogate);

                first = false;
            }

            return builder.ToString();
        }

        var toStringFn = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var thisValue = info.ThisValue;
                if (!thisValue.TryGetObject(out var obj) || obj is not JsRegExpObject)
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "RegExp.prototype.toString called on incompatible receiver"
                    );

                var source = obj.TryGetPropertyAtom(
                    info.Realm,
                    IdSource,
                    out var sourceValue,
                    out _
                )
                    ? info.Realm.ToJsStringSlowPath(sourceValue)
                    : "(?:)";
                var flags = obj.TryGetPropertyAtom(info.Realm, IdFlags, out var flagsValue, out _)
                    ? info.Realm.ToJsStringSlowPath(flagsValue)
                    : string.Empty;
                return "/" + source + "/" + flags;
            },
            "toString",
            0
        );

        var escapeFn = new JsHostFunction(
            Realm,
            (in info) =>
            {
                if (info.Arguments.Length == 0 || !info.Arguments[0].IsString)
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "RegExp.escape requires a string"
                    );

                return JsValue.FromString(EscapeRegExpString(info.Arguments[0].AsString()));
            },
            "escape",
            1
        );

        static JsRealm GetFunctionRealm(in CallInfo info)
        {
            return info.Function.Realm;
        }

        static JsRegExpObject? ThisRegExpValueOrUndefined(in CallInfo info, string methodName)
        {
            var functionRealm = GetFunctionRealm(info);
            if (!info.ThisValue.TryGetObject(out var obj))
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    $"RegExp.prototype.{methodName} called on incompatible receiver",
                    errorRealm: functionRealm
                );
            if (ReferenceEquals(obj, functionRealm.RegExpPrototype))
                return null;
            if (obj is not JsRegExpObject rx)
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    $"RegExp.prototype.{methodName} called on incompatible receiver",
                    errorRealm: functionRealm
                );
            return rx;
        }

        static bool GetBooleanProperty(JsRealm realm, JsObject obj, int atom)
        {
            return obj.TryGetPropertyAtom(realm, atom, out var value, out _) && value.ToBoolean();
        }

        static JsValue GetFlagsValue(in CallInfo info, int hasIndicesAtom, int unicodeSetsAtom)
        {
            var functionRealm = GetFunctionRealm(info);
            if (!info.ThisValue.TryGetObject(out var obj))
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    "RegExp.prototype.flags called on incompatible receiver",
                    errorRealm: functionRealm
                );
            if (ReferenceEquals(obj, functionRealm.Intrinsics.RegExpPrototype))
                return JsValue.FromString(string.Empty);

            var builder = new StringBuilder(8);
            if (GetBooleanProperty(info.Realm, obj, hasIndicesAtom))
                builder.Append('d');
            if (GetBooleanProperty(info.Realm, obj, IdGlobal))
                builder.Append('g');
            if (GetBooleanProperty(info.Realm, obj, IdIgnoreCase))
                builder.Append('i');
            if (GetBooleanProperty(info.Realm, obj, IdMultiline))
                builder.Append('m');
            if (GetBooleanProperty(info.Realm, obj, IdDotAll))
                builder.Append('s');
            if (GetBooleanProperty(info.Realm, obj, unicodeSetsAtom))
                builder.Append('v');
            else if (GetBooleanProperty(info.Realm, obj, IdUnicode))
                builder.Append('u');
            if (GetBooleanProperty(info.Realm, obj, IdSticky))
                builder.Append('y');
            return JsValue.FromString(builder.ToString());
        }

        var sourceGetter = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var rx = ThisRegExpValueOrUndefined(info, "source");
                if (rx is null)
                    return JsValue.FromString("(?:)");
                return JsValue.FromString(EscapeRegExpPattern(rx.Pattern));
            },
            "get source",
            0
        );

        var flagsGetter = new JsHostFunction(
            Realm,
            (in info) =>
            {
                return GetFlagsValue(info, hasIndicesAtom, unicodeSetsAtom);
            },
            "get flags",
            0
        );

        var globalGetter = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var rx = ThisRegExpValueOrUndefined(info, "global");
                if (rx is null)
                    return JsValue.Undefined;
                return rx.Global ? JsValue.True : JsValue.False;
            },
            "get global",
            0
        );

        var ignoreCaseGetter = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var rx = ThisRegExpValueOrUndefined(info, "ignoreCase");
                if (rx is null)
                    return JsValue.Undefined;
                return rx.IgnoreCase ? JsValue.True : JsValue.False;
            },
            "get ignoreCase",
            0
        );

        var multilineGetter = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var rx = ThisRegExpValueOrUndefined(info, "multiline");
                if (rx is null)
                    return JsValue.Undefined;
                return rx.Multiline ? JsValue.True : JsValue.False;
            },
            "get multiline",
            0
        );

        var stickyGetter = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var rx = ThisRegExpValueOrUndefined(info, "sticky");
                if (rx is null)
                    return JsValue.Undefined;
                return rx.Sticky ? JsValue.True : JsValue.False;
            },
            "get sticky",
            0
        );

        var unicodeGetter = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var rx = ThisRegExpValueOrUndefined(info, "unicode");
                if (rx is null)
                    return JsValue.Undefined;
                return rx.Unicode ? JsValue.True : JsValue.False;
            },
            "get unicode",
            0
        );

        var dotAllGetter = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var rx = ThisRegExpValueOrUndefined(info, "dotAll");
                if (rx is null)
                    return JsValue.Undefined;
                return rx.DotAll ? JsValue.True : JsValue.False;
            },
            "get dotAll",
            0
        );

        var hasIndicesGetter = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var rx = ThisRegExpValueOrUndefined(info, "hasIndices");
                if (rx is null)
                    return JsValue.Undefined;
                return rx.CompiledPattern.ParsedFlags.HasIndices ? JsValue.True : JsValue.False;
            },
            "get hasIndices",
            0
        );

        var unicodeSetsGetter = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var rx = ThisRegExpValueOrUndefined(info, "unicodeSets");
                if (rx is null)
                    return JsValue.Undefined;
                return rx.CompiledPattern.ParsedFlags.UnicodeSets ? JsValue.True : JsValue.False;
            },
            "get unicodeSets",
            0
        );

        var execFn = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                if (!thisValue.TryGetObject(out var obj) || obj is not JsRegExpObject rx)
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "RegExp.prototype.exec called on incompatible receiver"
                    );

                var input = realm.ToJsStringSlowPath(args.Length > 0 ? args[0] : JsValue.Undefined);
                return JsRegExpRuntime.Exec(realm, rx, input);
            },
            "exec",
            1
        );
        _regExpPrototypeExec = execFn;

        var testFn = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                if (!thisValue.TryGetObject(out var obj) || obj is not JsRegExpObject rx)
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "RegExp.prototype.test called on incompatible receiver"
                    );
                var input = realm.ToJsStringSlowPath(args.Length > 0 ? args[0] : JsValue.Undefined);
                return JsRegExpRuntime.Test(realm, rx, input) ? JsValue.True : JsValue.False;
            },
            "test",
            1
        );
        testFn.LeafBodyField = TryLeafRegExpTest;

        var matchSymbolFn = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                if (!thisValue.TryGetObject(out var obj))
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "RegExp.prototype[Symbol.match] called on incompatible receiver"
                    );

                var input = args.Length > 0 ? realm.ToJsStringSlowPath(args[0]) : string.Empty;
                if (!obj.TryGetPropertyAtom(realm, IdFlags, out var flagsValue, out _))
                    flagsValue = JsValue.Undefined;
                var flags = realm.ToJsStringSlowPath(flagsValue);
                var global = flags.IndexOf('g') >= 0;
                if (!global)
                    return RegExpExecGeneric(realm, thisValue, input);

                var fullUnicode = flags.IndexOf('u') >= 0 || flags.IndexOf('v') >= 0;
                SetPropertyAtomOrThrow(
                    realm,
                    obj,
                    IdLastIndex,
                    JsValue.FromInt32(0),
                    "[Symbol.match]"
                );

                JsArray? result = null;
                uint resultIndex = 0;

                // R8-regexp fast path: when exec still resolves to the
                // intrinsic (re-checked EVERY iteration - the get is part of
                // RegExpExec and is observable), step RegExpEngine directly.
                // lastIndex is read/written through the receiver's property
                // path inside IntrinsicExecStep so accessors stay observable.
                // Any deviation falls back to RegExpExecGeneric mid-stream.
                var rx = thisValue.AsObject() as JsRegExpObject;
                var canStepFast = rx is not null;

                while (true)
                {
                    if (
                        !obj.TryGetPropertyAtom(realm, IdExec, out var iterExecValue, out _)
                        || !iterExecValue.TryGetObject(out var iterExecObj)
                        || !ReferenceEquals(iterExecObj, execFn)
                    )
                    {
                        // Custom or missing exec: use the generic algorithm
                        // for this and all remaining iterations.
                        var step = RegExpExecGeneric(realm, thisValue, input);
                        if (step.IsNull || !step.TryGetObject(out var stepObj))
                            break;

                        var matchEntry = stepObj.TryGetProperty("0", out var stepValue)
                            ? stepValue
                            : JsValue.FromString(string.Empty);
                        result ??= realm.CreateArrayObject();
                        FreshArrayOperations.DefineElement(result, resultIndex++, matchEntry);

                        var matchedStr = realm.ToJsStringSlowPath(matchEntry);
                        if (matchedStr.Length == 0)
                        {
                            var li = obj.TryGetPropertyAtom(
                                realm,
                                IdLastIndex,
                                out var lastIndexValue,
                                out _
                            )
                                ? ToLength(lastIndexValue, realm)
                                : 0;
                            SetPropertyAtomOrThrow(
                                realm,
                                obj,
                                IdLastIndex,
                                FromLengthValue(AdvanceStringIndexLong(input, li, fullUnicode)),
                                "[Symbol.match]"
                            );
                        }
                        continue;
                    }

                    // Intrinsic exec: direct engine step.
                    var engineStep = JsRegExpRuntime.IntrinsicExecStep(realm, rx!, input);
                    if (engineStep is null)
                        break;

                    var m = input.Substring(engineStep.Value.Index, engineStep.Value.Length);
                    result ??= realm.CreateArrayObject();
                    FreshArrayOperations.DefineElement(
                        result,
                        resultIndex++,
                        JsValue.FromString(m)
                    );

                    if (m.Length == 0)
                    {
                        var curLi = obj.TryGetPropertyAtom(
                            realm,
                            IdLastIndex,
                            out var curLiValue,
                            out _
                        )
                            ? ToLength(curLiValue, realm)
                            : 0;
                        SetPropertyAtomOrThrow(
                            realm,
                            obj,
                            IdLastIndex,
                            FromLengthValue(AdvanceStringIndexLong(input, curLi, fullUnicode)),
                            "[Symbol.match]"
                        );
                    }
                }

                if (resultIndex == 0)
                    return JsValue.Null;

                result!.SetLength(resultIndex);
                return result;
            },
            "[Symbol.match]",
            1
        );

        var matchAllFn = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                if (!thisValue.TryGetObject(out var obj))
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "RegExp.prototype[Symbol.matchAll] called on incompatible receiver"
                    );

                var input = args.Length > 0 ? realm.ToJsStringSlowPath(args[0]) : string.Empty;
                var constructor = GetRegExpSpeciesConstructor(realm, obj);
                if (!obj.TryGetPropertyAtom(realm, IdFlags, out var flagsValue, out _))
                    flagsValue = JsValue.Undefined;
                var flags = realm.ToJsStringSlowPath(flagsValue);

                var lastIndex = obj.TryGetPropertyAtom(
                    realm,
                    IdLastIndex,
                    out var sourceLastIndex,
                    out _
                )
                    ? ToLength(sourceLastIndex, realm)
                    : 0;

                var matcherValue = realm.ConstructWithExplicitNewTarget(
                    constructor,
                    [thisValue, JsValue.FromString(flags)],
                    JsValue.FromObject(constructor),
                    info.CallerPc
                );
                if (!matcherValue.TryGetObject(out var matcherObj))
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "RegExp.prototype[Symbol.matchAll] created an invalid matcher"
                    );

                SetPropertyAtomOrThrow(
                    realm,
                    matcherObj,
                    IdLastIndex,
                    FromLengthValue(lastIndex),
                    "[Symbol.matchAll]"
                );
                return new JsStringMatchAllIteratorObject(
                    realm,
                    input,
                    matcherObj,
                    flags.IndexOf('g') >= 0,
                    flags.IndexOf('u') >= 0 || flags.IndexOf('v') >= 0
                );
            },
            "[Symbol.matchAll]",
            1
        );

        var stringIteratorNextFn = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var thisValue = info.ThisValue;
                if (
                    !thisValue.TryGetObject(out var thisObj)
                    || thisObj is not JsStringMatchAllIteratorObject iterator
                )
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "String MatchAll Iterator next called on incompatible receiver"
                    );
                return iterator.Next();
            },
            "next",
            0
        );

        static JsFunction GetRegExpSpeciesConstructor(JsRealm realm, JsObject receiver)
        {
            JsFunction defaultCtor = realm.RegExpConstructor;
            if (!receiver.TryGetPropertyAtom(realm, IdConstructor, out var ctorValue, out _))
                return defaultCtor;
            if (ctorValue.IsUndefined)
                return defaultCtor;
            if (!ctorValue.TryGetObject(out var ctorObj))
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    "RegExp constructor property must be an object"
                );
            if (ctorObj.TryGetPropertyAtom(realm, IdSymbolSpecies, out var speciesValue, out _))
            {
                if (speciesValue.IsUndefined || speciesValue.IsNull)
                    return defaultCtor;
                if (
                    speciesValue.TryGetObject(out var speciesObj)
                    && speciesObj is JsFunction speciesCtor
                    && speciesCtor.IsConstructor
                )
                    return speciesCtor;
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    "RegExp [Symbol.species] must be a constructor"
                );
            }

            return defaultCtor;
        }

        static JsValue RegExpExecGeneric(JsRealm realm, JsValue splitterValue, string input)
        {
            if (!splitterValue.TryGetObject(out var splitterObj))
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    "RegExp exec receiver must be an object"
                );

            if (
                splitterObj.TryGetPropertyAtom(realm, IdExec, out var execValue, out _)
                && execValue.TryGetObject(out var execObj)
                && execObj is JsFunction execFn
            )
            {
                var result = realm.InvokeFunction(
                    execFn,
                    splitterValue,
                    [JsValue.FromString(input)]
                );
                if (result.IsNull || result.TryGetObject(out _))
                    return result;

                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    "RegExp exec method must return an object or null"
                );
            }

            if (splitterObj is JsRegExpObject splitterRegex)
            {
                var result = JsRegExpRuntime.Exec(realm, splitterRegex, input);
                if (splitterRegex.Global || splitterRegex.Sticky)
                    SetPropertyAtomOrThrow(
                        realm,
                        splitterObj,
                        IdLastIndex,
                        splitterRegex.GetNamedSlotUnchecked(JsRealm.RegExpOwnLastIndexSlot),
                        "exec"
                    );
                return result;
            }

            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "RegExp exec method must be callable"
            );
        }

        static int AdvanceStringIndex(string input, int index, bool unicode)
        {
            if (!unicode || index >= input.Length)
                return index + 1;

            if (
                index + 1 < input.Length
                && char.IsHighSurrogate(input[index])
                && char.IsLowSurrogate(input[index + 1])
            )
                return index + 2;

            return index + 1;
        }

        static bool IsSimpleLiteralPatternChar(char value)
        {
            return value
                is not '\\'
                    and not '^'
                    and not '$'
                    and not '.'
                    and not '*'
                    and not '+'
                    and not '?'
                    and not '('
                    and not ')'
                    and not '['
                    and not ']'
                    and not '{'
                    and not '}'
                    and not '|';
        }

        static long AdvanceStringIndexLong(string input, long index, bool unicode)
        {
            if (index >= int.MaxValue || index >= input.Length)
                return index + 1;

            return AdvanceStringIndex(input, (int)index, unicode);
        }

        static void SetPropertyAtomOrThrow(
            JsRealm realm,
            JsObject obj,
            int atom,
            in JsValue value,
            string methodName
        )
        {
            if (!obj.TrySetPropertyAtom(realm, atom, value, out _))
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    $"RegExp.prototype.{methodName} failed to set required property"
                );
        }

        static long ToLength(in JsValue value, JsRealm realm)
        {
            var number = realm.ToNumberSlowPath(value);
            if (double.IsNaN(number) || number <= 0d)
                return 0;
            const double maxSafeInteger = 9007199254740991d;
            if (double.IsPositiveInfinity(number))
                return (long)maxSafeInteger;
            return (long)Math.Min(maxSafeInteger, Math.Floor(number));
        }

        var searchSymbolFn = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                if (!thisValue.TryGetObject(out var obj))
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "RegExp.prototype[Symbol.search] called on incompatible receiver"
                    );

                var input = args.Length > 0 ? realm.ToJsStringSlowPath(args[0]) : string.Empty;

                var hasLastIndex = obj.TryGetPropertyAtom(
                    realm,
                    IdLastIndex,
                    out var oldLastIndex,
                    out _
                );
                var previousLastIndex = hasLastIndex ? oldLastIndex : JsValue.Undefined;
                if (!JsValue.SameValue(previousLastIndex, JsValue.FromInt32(0)))
                    SetPropertyAtomOrThrow(
                        realm,
                        obj,
                        IdLastIndex,
                        JsValue.FromInt32(0),
                        "[Symbol.search]"
                    );
                if (
                    !obj.TryGetPropertyAtom(realm, IdExec, out var execValue, out _)
                    || !execValue.TryGetObject(out var execObj)
                    || execObj is not JsFunction execFn
                )
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "RegExp.prototype[Symbol.search] called on incompatible receiver"
                    );

                var result = realm.InvokeFunction(
                    execFn,
                    JsValue.FromObject(obj),
                    [JsValue.FromString(input)]
                );
                var currentLastIndex = obj.TryGetPropertyAtom(
                    realm,
                    IdLastIndex,
                    out var observedLastIndex,
                    out _
                )
                    ? observedLastIndex
                    : JsValue.Undefined;
                if (!JsValue.SameValue(currentLastIndex, previousLastIndex))
                    SetPropertyAtomOrThrow(
                        realm,
                        obj,
                        IdLastIndex,
                        previousLastIndex,
                        "[Symbol.search]"
                    );

                if (result.IsNull)
                    return JsValue.FromInt32(-1);
                if (!result.TryGetObject(out var resultObj))
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "RegExp.prototype[Symbol.search] called on incompatible receiver"
                    );

                if (
                    !resultObj.TryGetPropertyByAtom(IdIndex, out var indexValue)
                    || !indexValue.IsNumber
                )
                    return JsValue.FromInt32(-1);
                return JsValue.FromInt32((int)indexValue.NumberValue);
            },
            "[Symbol.search]",
            1
        );

        var replaceFn = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                if (!thisValue.TryGetObject(out var obj))
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "RegExp.prototype[Symbol.replace] called on incompatible receiver"
                    );

                var input = args.Length > 0 ? realm.ToJsStringSlowPath(args[0]) : string.Empty;
                var replaceValue = args.Length > 1 ? args[1] : JsValue.Undefined;
                var functionalReplace =
                    replaceValue.TryGetObject(out var replaceObj) && replaceObj is JsFunction;
                var replacementTemplate = functionalReplace
                    ? string.Empty
                    : realm.ToJsStringSlowPath(replaceValue);
                var replacementNeedsSubstitution =
                    functionalReplace || replacementTemplate.IndexOf('$') >= 0;
                var inputValue = JsValue.FromString(input);
                var result = new PooledCharBuilder(
                    stackalloc char[
                        Math.Min(input.Length + Math.Max(replacementTemplate.Length, 16), 256)
                    ]
                );
                if (!obj.TryGetPropertyAtom(realm, IdFlags, out var flagsValue, out _))
                    flagsValue = JsValue.Undefined;
                var flags = realm.ToJsStringSlowPath(flagsValue);
                var isGlobal = flags.IndexOf('g') >= 0;
                var fullUnicode = flags.IndexOf('u') >= 0 || flags.IndexOf('v') >= 0;
                try
                {
                    if (isGlobal)
                        SetPropertyAtomOrThrow(
                            realm,
                            obj,
                            IdLastIndex,
                            JsValue.FromInt32(0),
                            "[Symbol.replace]"
                        );
                    var nextSourcePosition = 0;
                    while (true)
                    {
                        if (
                            !obj.TryGetPropertyAtom(realm, IdExec, out var execValue, out _)
                            || !execValue.TryGetObject(out var execObj)
                            || execObj is not JsFunction execFunction
                        )
                            throw new JsRuntimeException(
                                JsErrorKind.TypeError,
                                "RegExp.prototype[Symbol.replace] called on incompatible receiver"
                            );

                        RegExpMatchResult? rawMatch = null;
                        JsObject? matchObj = null;
                        int intrinsicStart = -1,
                            intrinsicLength = 0;
                        bool usedIntrinsicStep = false;
                        if (
                            !replacementNeedsSubstitution
                            && ReferenceEquals(execFunction, execFn)
                            && obj is JsRegExpObject rxFast
                        )
                        {
                            // R8-regexp: plain-string template needs only
                            // group-0 position; skip full match-result build.
                            var step = JsRegExpRuntime.IntrinsicExecStep(realm, rxFast, input);
                            if (step is null)
                                break;
                            intrinsicStart = step.Value.Index;
                            intrinsicLength = step.Value.Length;
                            usedIntrinsicStep = true;
                        }
                        else if (ReferenceEquals(execFunction, execFn) && obj is JsRegExpObject rx2)
                        {
                            rawMatch = JsRegExpRuntime.ExecMatchResult(realm, rx2, input);
                            if (rawMatch is null)
                                break;
                        }
                        else
                        {
                            var matchValue = realm.InvokeFunction(
                                execFunction,
                                JsValue.FromObject(obj),
                                [inputValue]
                            );
                            if (matchValue.IsNull)
                                break;
                            if (!matchValue.TryGetObject(out matchObj))
                                throw new JsRuntimeException(
                                    JsErrorKind.TypeError,
                                    "RegExp exec method must return an object or null"
                                );
                        }

                        var matchIndex =
                            usedIntrinsicStep ? intrinsicStart
                            : rawMatch is not null ? rawMatch.Index
                            : matchObj!.TryGetPropertyAtom(
                                realm,
                                IdIndex,
                                out var indexValue,
                                out _
                            )
                                ? (int)ToLength(indexValue, realm)
                            : 0;
                        var matched =
                            usedIntrinsicStep ? input.Substring(intrinsicStart, intrinsicLength)
                            : rawMatch is not null ? rawMatch.Groups[0] ?? string.Empty
                            : matchObj!.TryGetElement(0, out var matchedValue)
                                ? realm.ToJsStringSlowPath(matchedValue)
                            : "undefined";

                        if (matchIndex < nextSourcePosition)
                            continue;

                        result.Append(input, nextSourcePosition, matchIndex - nextSourcePosition);

                        string replacement;
                        if (functionalReplace)
                        {
                            var captureCount =
                                rawMatch is not null ? Math.Max(0, rawMatch.Groups.Length - 1)
                                : matchObj!.TryGetPropertyAtom(
                                    realm,
                                    IdLength,
                                    out var lengthValue,
                                    out _
                                )
                                    ? Math.Max(0, (int)ToLength(lengthValue, realm) - 1)
                                : 0;
                            var replaceFnObj = (JsFunction)replaceObj!;
                            var groupsArgValue = JsValue.Undefined;
                            var hasGroups = false;
                            if (rawMatch is not null)
                            {
                                hasGroups =
                                    rawMatch.NamedGroups is not null
                                    && rawMatch.NamedGroups.Count != 0;
                                if (hasGroups)
                                    groupsArgValue = JsValue.FromObject(
                                        CreateNamedCapturesObject(realm, rawMatch.NamedGroups!)
                                    );
                            }
                            else if (
                                matchObj!.TryGetPropertyAtom(
                                    realm,
                                    IdGroups,
                                    out var groupsArg,
                                    out _
                                ) && !groupsArg.IsUndefined
                            )
                            {
                                hasGroups = true;
                                groupsArgValue = groupsArg;
                            }

                            var replaceArgCount = captureCount + 3 + (hasGroups ? 1 : 0);
                            var replaceArgs = new JsValue[replaceArgCount];
                            replaceArgs[0] = JsValue.FromString(matched);
                            for (var captureIndex = 0; captureIndex < captureCount; captureIndex++)
                            {
                                if (rawMatch is not null)
                                {
                                    var capture = rawMatch.Groups[captureIndex + 1];
                                    replaceArgs[captureIndex + 1] = capture is null
                                        ? JsValue.Undefined
                                        : JsValue.FromString(capture);
                                    continue;
                                }

                                var hasCapture = matchObj!.TryGetElement(
                                    (uint)(captureIndex + 1),
                                    out var captureValue
                                );
                                replaceArgs[captureIndex + 1] = hasCapture
                                    ? captureValue
                                    : JsValue.Undefined;
                            }

                            replaceArgs[captureCount + 1] = JsValue.FromInt32(matchIndex);
                            replaceArgs[captureCount + 2] = inputValue;
                            if (hasGroups)
                                replaceArgs[captureCount + 3] = groupsArgValue;
                            replacement = realm.ToJsStringSlowPath(
                                realm.InvokeFunction(replaceFnObj, JsValue.Undefined, replaceArgs)
                            );
                        }
                        else
                        {
                            if (
                                !replacementNeedsSubstitution
                                && (usedIntrinsicStep || rawMatch is not null)
                            )
                            {
                                replacement = replacementTemplate;
                                result.Append(replacement);
                                nextSourcePosition = matchIndex + matched.Length;

                                if (!isGlobal)
                                    break;

                                if (matched.Length == 0)
                                {
                                    var lastIndex = obj.TryGetPropertyAtom(
                                        realm,
                                        IdLastIndex,
                                        out var lastIndexValue,
                                        out _
                                    )
                                        ? ToLength(lastIndexValue, realm)
                                        : 0;
                                    SetPropertyAtomOrThrow(
                                        realm,
                                        obj,
                                        IdLastIndex,
                                        FromLengthValue(
                                            AdvanceStringIndexLong(input, lastIndex, fullUnicode)
                                        ),
                                        "[Symbol.replace]"
                                    );
                                }

                                continue;
                            }

                            var captureCount =
                                rawMatch is not null ? Math.Max(0, rawMatch.Groups.Length - 1)
                                : matchObj!.TryGetPropertyAtom(
                                    realm,
                                    IdLength,
                                    out var lengthValue,
                                    out _
                                )
                                    ? Math.Max(0, (int)ToLength(lengthValue, realm) - 1)
                                : 0;
                            JsObject? groups = null;
                            ReadOnlySpan<string?> captures;
                            if (rawMatch is not null)
                            {
                                captures = rawMatch.Groups.AsSpan(1);
                                if (
                                    rawMatch.NamedGroups is not null
                                    && replacementTemplate.Contains("$<", StringComparison.Ordinal)
                                )
                                    groups = CreateNamedCapturesObject(realm, rawMatch.NamedGroups);
                            }
                            else
                            {
                                var capturesArray = new string?[captureCount];
                                for (
                                    var captureIndex = 0;
                                    captureIndex < captureCount;
                                    captureIndex++
                                )
                                {
                                    var captureKey = (uint)(captureIndex + 1);
                                    if (
                                        !matchObj!.TryGetElement(captureKey, out var captureValue)
                                        || captureValue.IsUndefined
                                    )
                                        continue;
                                    capturesArray[captureIndex] = realm.ToJsStringSlowPath(
                                        captureValue
                                    );
                                }

                                captures = capturesArray;
                                if (
                                    matchObj!.TryGetPropertyAtom(
                                        realm,
                                        IdGroups,
                                        out var groupsValue,
                                        out _
                                    ) && !groupsValue.IsUndefined
                                )
                                {
                                    if (!realm.TryToObject(groupsValue, out var groupsObj))
                                        throw new JsRuntimeException(
                                            JsErrorKind.TypeError,
                                            "RegExp.prototype[Symbol.replace] groups value must be coercible to object"
                                        );
                                    groups = groupsObj;
                                }
                            }

                            replacement = ApplyReplacementTemplate(
                                matched,
                                input,
                                matchIndex,
                                captures,
                                groups,
                                realm,
                                replacementTemplate
                            );
                        }

                        result.Append(replacement);
                        nextSourcePosition = matchIndex + matched.Length;

                        if (!isGlobal)
                            break;

                        if (matched.Length == 0)
                        {
                            var lastIndex = obj.TryGetPropertyAtom(
                                realm,
                                IdLastIndex,
                                out var lastIndexValue,
                                out _
                            )
                                ? ToLength(lastIndexValue, realm)
                                : 0;
                            SetPropertyAtomOrThrow(
                                realm,
                                obj,
                                IdLastIndex,
                                FromLengthValue(
                                    AdvanceStringIndexLong(input, lastIndex, fullUnicode)
                                ),
                                "[Symbol.replace]"
                            );
                        }
                    }

                    if (nextSourcePosition < input.Length)
                        result.Append(input, nextSourcePosition, input.Length - nextSourcePosition);

                    return JsValue.FromString(result.ToString());
                }
                finally
                {
                    result.Dispose();
                }
            },
            "[Symbol.replace]",
            2
        );

        var splitFn = new JsHostFunction(
            Realm,
            (in info) =>
            {
                var realm = info.Realm;
                var thisValue = info.ThisValue;
                var args = info.Arguments;
                if (!thisValue.TryGetObject(out var obj))
                    throw new JsRuntimeException(
                        JsErrorKind.TypeError,
                        "RegExp.prototype[Symbol.split] called on incompatible receiver"
                    );

                var text = args.Length > 0 ? realm.ToJsStringSlowPath(args[0]) : string.Empty;
                var limit =
                    args.Length > 1 && !args[1].IsUndefined
                        ? realm.ToUint32(args[1])
                        : uint.MaxValue;
                if (limit == 0)
                    return realm.CreateArrayObject();

                var flags = obj.TryGetPropertyAtom(realm, IdFlags, out var flagsValue, out _)
                    ? realm.ToJsStringSlowPath(flagsValue)
                    : string.Empty;
                var unicodeMatching = flags.IndexOf('u') >= 0 || flags.IndexOf('v') >= 0;
                var splitterFlags = flags.IndexOf('y') >= 0 ? flags : flags + "y";

                var ctor = GetRegExpSpeciesConstructor(realm, obj);
                var splitterValue = realm.ConstructWithExplicitNewTarget(
                    ctor,
                    [JsValue.FromObject(obj), JsValue.FromString(splitterFlags)],
                    ctor,
                    0
                );

                // R8-regexp fast path: default species construction already
                // happened above; with a plain regexp receiver still bound to
                // the builtin exec, per-position splitting can use raw engine
                // steps instead of building an observable match object at
                // every position (no user code runs between attempts).
                if (
                    splitterValue.TryGetObject(out var fastSplitterObj)
                    && fastSplitterObj is JsRegExpObject fastSplitter
                    && realm.Intrinsics.IsDefaultRegExpExec(realm, fastSplitterObj)
                )
                {
                    using var resultBuilder = new PooledManagedArrayBuilder<JsValue>(16);
                    uint fastLength = 0;
                    var fastP = 0;
                    var fastQ = 0;

                    if (text.Length == 0)
                    {
                        if (
                            JsRegExpRuntime.IntrinsicExecStepAt(realm, fastSplitter, text, 0)
                            is not null
                        )
                            return realm.CreateArrayObjectFromDense(resultBuilder.ToArray());
                        resultBuilder.Add(JsValue.FromString(text[fastP..]));
                        return realm.CreateArrayObjectFromDense(resultBuilder.ToArray());
                    }

                    // Trivially empty pattern (empty source or bare non-
                    // capturing group): zero-length match at every position,
                    // so segments are the individual code points; skip the
                    // engine entirely.
                    var splitPatternSource = fastSplitter.Pattern;
                    if (
                        splitPatternSource.Length == 1
                        && !fastSplitter.IgnoreCase
                        && IsSimpleLiteralPatternChar(splitPatternSource[0])
                    )
                    {
                        var separator = splitPatternSource[0];
                        while (fastLength < limit)
                        {
                            var match = text.IndexOf(separator, fastP);
                            if (match < 0)
                                break;

                            resultBuilder.Add(JsValue.FromString(text[fastP..match]));
                            fastLength++;
                            fastP = match + 1;
                        }

                        if (fastLength < limit)
                            resultBuilder.Add(JsValue.FromString(text[fastP..]));
                        return realm.CreateArrayObjectFromDense(resultBuilder.ToArray());
                    }

                    if (splitPatternSource.Length == 0 || splitPatternSource is "(?:)")
                    {
                        if (!unicodeMatching)
                        {
                            var dense = new JsValue[(int)Math.Min((uint)text.Length, limit)];
                            for (var i = 0; i < dense.Length; i++)
                                dense[i] = JsValue.FromLatin1Char(text[i]);
                            return realm.CreateArrayObjectFromDense(dense);
                        }

                        while (fastQ < text.Length)
                        {
                            var next = AdvanceStringIndex(text, fastQ, unicodeMatching);
                            if (fastQ != fastP)
                            {
                                resultBuilder.Add(
                                    fastQ - fastP == 1
                                        ? JsValue.FromLatin1Char(text[fastP])
                                        : JsValue.FromString(text[fastP..fastQ])
                                );
                                fastLength++;
                                if (fastLength == limit)
                                    return realm.CreateArrayObjectFromDense(
                                        resultBuilder.ToArray()
                                    );
                                fastP = fastQ;
                            }
                            fastQ = next;
                        }
                        if (fastP < text.Length && fastLength < limit)
                            resultBuilder.Add(JsValue.FromString(text[fastP..]));
                        return realm.CreateArrayObjectFromDense(resultBuilder.ToArray());
                    }

                    while (fastQ < text.Length)
                    {
                        var step = JsRegExpRuntime.IntrinsicExecStepAt(
                            realm,
                            fastSplitter,
                            text,
                            fastQ
                        );
                        if (step is null)
                        {
                            fastQ = AdvanceStringIndex(text, fastQ, unicodeMatching);
                            continue;
                        }

                        fastQ = step.Value.Index;
                        var e = step.Value.Index + step.Value.Length;
                        if (e > text.Length)
                            e = text.Length;
                        if (e == fastP)
                        {
                            fastQ = AdvanceStringIndex(text, fastQ, unicodeMatching);
                            continue;
                        }

                        resultBuilder.Add(JsValue.FromString(text[fastP..fastQ]));
                        fastLength++;
                        if (fastLength == limit)
                            return realm.CreateArrayObjectFromDense(resultBuilder.ToArray());

                        fastP = e;

                        var ranges = step.Value.Ranges;
                        for (var i = 1; i < step.Value.RangeCount; i++)
                        {
                            var capture = ranges[i];
                            resultBuilder.Add(
                                capture.Success
                                    ? JsValue.FromString(
                                        text.Substring(capture.Index, capture.Length)
                                    )
                                    : JsValue.Undefined
                            );
                            fastLength++;
                            if (fastLength == limit)
                                return realm.CreateArrayObjectFromDense(resultBuilder.ToArray());
                        }

                        fastQ = fastP;
                    }
                    if (fastLength < limit)
                        resultBuilder.Add(JsValue.FromString(text[fastP..]));
                    return realm.CreateArrayObjectFromDense(resultBuilder.ToArray());
                }

                var result = realm.CreateArrayObject();
                uint lengthA = 0;
                var p = 0;
                var q = 0;
                if (text.Length == 0)
                {
                    var z = RegExpExecGeneric(realm, splitterValue, text);
                    if (z.IsNull)
                        goto add_tail;
                    goto done;
                }

                while (q < text.Length)
                {
                    if (!splitterValue.TryGetObject(out var splitterObj))
                        throw new JsRuntimeException(
                            JsErrorKind.TypeError,
                            "RegExp split receiver must be an object"
                        );

                    SetPropertyAtomOrThrow(
                        realm,
                        splitterObj,
                        IdLastIndex,
                        JsValue.FromInt32(q),
                        "[Symbol.split]"
                    );
                    var z = RegExpExecGeneric(realm, splitterValue, text);
                    if (z.IsNull)
                    {
                        q = AdvanceStringIndex(text, q, unicodeMatching);
                        continue;
                    }

                    if (
                        !splitterObj.TryGetPropertyAtom(
                            realm,
                            IdLastIndex,
                            out var lastIndexValue,
                            out _
                        )
                    )
                        throw new JsRuntimeException(
                            JsErrorKind.TypeError,
                            "RegExp split receiver must expose lastIndex"
                        );

                    var e = (int)ToLength(lastIndexValue, realm);
                    if (e > text.Length)
                        e = text.Length;

                    if (e == p)
                    {
                        q = AdvanceStringIndex(text, q, unicodeMatching);
                        continue;
                    }

                    FreshArrayOperations.DefineElement(
                        result,
                        lengthA++,
                        JsValue.FromString(text[p..q])
                    );
                    if (lengthA == limit)
                        goto done;

                    p = e;

                    if (!z.TryGetObject(out var matchObj))
                        throw new JsRuntimeException(
                            JsErrorKind.TypeError,
                            "RegExp split result must be an object or null"
                        );

                    if (matchObj.TryGetPropertyByAtom(IdLength, out var lengthValue))
                    {
                        var numberOfCaptures = (int)ToLength(lengthValue, realm);
                        for (var i = 1; i < numberOfCaptures; i++)
                        {
                            var capture = matchObj.TryGetElement((uint)i, out var captureValue)
                                ? captureValue
                                : JsValue.Undefined;
                            FreshArrayOperations.DefineElement(result, lengthA++, capture);
                            if (lengthA == limit)
                                goto done;
                        }
                    }

                    q = p;
                }

                add_tail:
                if (p > text.Length)
                    p = text.Length;
                FreshArrayOperations.DefineElement(
                    result,
                    lengthA++,
                    JsValue.FromString(text[p..])
                );
                done:
                result.SetLength(lengthA);
                return result;
            },
            "[Symbol.split]",
            2
        );

        RegExpConstructor.InitializePrototypeProperty(RegExpPrototype);
        RegExpConstructor.DefineAccessorPropertyAtom(
            Realm,
            IdSymbolSpecies,
            speciesGetter,
            null,
            JsShapePropertyFlags.HasGetter | JsShapePropertyFlags.Configurable
        );
        RegExpConstructor.DefineNewPropertiesNoCollision(
            Realm,
            [
                PropertyDefinition.Mutable(
                    Atoms.InternNoCheck("escape"),
                    JsValue.FromObject(escapeFn)
                ),
            ]
        );

        Span<PropertyDefinition> protoDefs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(RegExpConstructor)),
            PropertyDefinition.GetterData(IdSource, sourceGetter, configurable: true),
            PropertyDefinition.GetterData(IdFlags, flagsGetter, configurable: true),
            PropertyDefinition.GetterData(IdGlobal, globalGetter, configurable: true),
            PropertyDefinition.GetterData(IdIgnoreCase, ignoreCaseGetter, configurable: true),
            PropertyDefinition.GetterData(IdMultiline, multilineGetter, configurable: true),
            PropertyDefinition.GetterData(IdSticky, stickyGetter, configurable: true),
            PropertyDefinition.GetterData(IdUnicode, unicodeGetter, configurable: true),
            PropertyDefinition.GetterData(IdDotAll, dotAllGetter, configurable: true),
            PropertyDefinition.GetterData(hasIndicesAtom, hasIndicesGetter, configurable: true),
            PropertyDefinition.GetterData(unicodeSetsAtom, unicodeSetsGetter, configurable: true),
            PropertyDefinition.Mutable(IdToString, JsValue.FromObject(toStringFn)),
            PropertyDefinition.Mutable(execAtom, JsValue.FromObject(execFn)),
            PropertyDefinition.Mutable(testAtom, JsValue.FromObject(testFn)),
            PropertyDefinition.Mutable(IdSymbolMatch, JsValue.FromObject(matchSymbolFn)),
            PropertyDefinition.Mutable(IdSymbolSearch, JsValue.FromObject(searchSymbolFn)),
            PropertyDefinition.Mutable(IdSymbolMatchAll, JsValue.FromObject(matchAllFn)),
            PropertyDefinition.Mutable(IdSymbolReplace, JsValue.FromObject(replaceFn)),
            PropertyDefinition.Mutable(IdSymbolSplit, JsValue.FromObject(splitFn)),
        ];
        RegExpPrototype.DefineNewPropertiesNoCollision(Realm, protoDefs);

        Span<PropertyDefinition> iteratorProtoDefs =
        [
            PropertyDefinition.Mutable(IdNext, JsValue.FromObject(stringIteratorNextFn)),
            PropertyDefinition.Const(
                IdSymbolToStringTag,
                JsValue.FromString("RegExp String Iterator"),
                configurable: true
            ),
        ];
        RegExpStringIteratorPrototype.DefineNewPropertiesNoCollision(Realm, iteratorProtoDefs);
    }

    private static bool TryLeafRegExpTest(
        JsRealm realm,
        JsValue thisValue,
        ReadOnlySpan<JsValue> args,
        out JsValue result
    )
    {
        if (!thisValue.TryGetObject(out var obj) || obj is not JsRegExpObject rx)
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "RegExp.prototype.test called on incompatible receiver"
            );

        var input = realm.ToJsStringSlowPath(args.Length > 0 ? args[0] : JsValue.Undefined);
        result = JsRegExpRuntime.Test(realm, rx, input) ? JsValue.True : JsValue.False;
        return true;
    }
}
