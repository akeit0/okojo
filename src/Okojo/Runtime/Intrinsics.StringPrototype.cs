using System.Buffers;
using System.Globalization;
using System.Text;
using Okojo.Parsing;

namespace Okojo.Runtime;

public partial class Intrinsics
{
    private void InstallStringPrototypeBuiltins()
    {
        static JsString ToCoercibleJsString(JsRealm realm, in JsValue thisValue, string methodName)
        {
            if (thisValue.IsNullOrUndefined)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    $"String.prototype.{methodName} called on null or undefined");

            return realm.ToJsStringValueSlowPath(thisValue);
        }

        static int ClampStringPosition(double position, int length)
        {
            if (double.IsNegativeInfinity(position) || position <= 0d)
                return 0;
            if (double.IsPositiveInfinity(position) || position >= length)
                return length;
            return (int)position;
        }

        static JsString GetSubstringByCodeUnit(JsString value, int start, int end)
        {
            if (end <= start)
                return JsString.Empty;
            return value.Slice(start, end - start);
        }


        static JsString BuildPaddingString(long fillLength, JsString fillString)
        {
            if (fillLength <= 0 || fillString.Length == 0)
                return JsString.Empty;

            var repeatCount = (fillLength + fillString.Length - 1) / fillString.Length;
            var repeated = fillString.Repeat(repeatCount);
            return repeated.Length == fillLength ? repeated : repeated.Slice(0, (int)fillLength);
        }


        static void AppendSubstitution(
            ref PooledCharBuilder result,
            JsString matched,
            JsString str,
            int position,
            string replacementTemplate)
        {
            if (replacementTemplate.Length == 0)
                return;

            var stringLength = str.Length;
            JsString template = replacementTemplate;
            var cursor = 0;
            var literalStart = 0;
            while (cursor < replacementTemplate.Length)
            {
                var ch = replacementTemplate[cursor];
                if (ch != '$' || cursor + 1 >= replacementTemplate.Length)
                {
                    cursor++;
                    continue;
                }

                if (cursor > literalStart)
                    result.Append(template, literalStart, cursor - literalStart);

                var next = replacementTemplate[cursor + 1];
                switch (next)
                {
                    case '$':
                        result.Append('$');
                        cursor += 2;
                        literalStart = cursor;
                        continue;
                    case '&':
                        result.Append(matched);
                        cursor += 2;
                        literalStart = cursor;
                        continue;
                    case '`':
                        if (position > 0)
                            result.Append(str, 0, position);
                        cursor += 2;
                        literalStart = cursor;
                        continue;
                    case '\'':
                        {
                            var tailPos = Math.Min(position + matched.Length, stringLength);
                            if (tailPos < stringLength)
                                result.Append(str, tailPos, stringLength - tailPos);
                            cursor += 2;
                            literalStart = cursor;
                            continue;
                        }
                }

                if (next is >= '0' and <= '9')
                {
                    var digitCount = cursor + 2 < replacementTemplate.Length &&
                                     replacementTemplate[cursor + 2] is >= '0' and <= '9'
                        ? 2
                        : 1;
                    var index = replacementTemplate[cursor + 1] - '0';
                    if (digitCount == 2)
                    {
                        var twoDigitIndex = index * 10 + (replacementTemplate[cursor + 2] - '0');
                        if (twoDigitIndex == 0)
                        {
                            result.Append(template, cursor, 3);
                            cursor += 3;
                            literalStart = cursor;
                            continue;
                        }

                        digitCount = 1;
                    }

                    if (index == 0)
                    {
                        result.Append(template, cursor, 2);
                        cursor += 2;
                        literalStart = cursor;
                        continue;
                    }

                    result.Append('$');
                    result.Append(next);
                    cursor += 2;
                    literalStart = cursor;
                    continue;
                }

                if (next == '<')
                {
                    result.Append("$<");
                    cursor += 2;
                    literalStart = cursor;
                    continue;
                }

                result.Append('$');
                cursor++;
                literalStart = cursor;
            }

            if (cursor > literalStart)
                result.Append(template, literalStart, cursor - literalStart);
        }

        static JsValue CallReplaceMethodIfPresent(
            JsRealm realm,
            in JsValue searchValue,
            in JsValue originalValue,
            in JsValue replaceValue)
        {
            if (!searchValue.TryGetObject(out var searchObj) ||
                !searchObj.TryGetPropertyAtom(realm, IdSymbolReplace, out var replacerValue, out _) ||
                replacerValue.IsUndefined || replacerValue.IsNull)
                return JsValue.Undefined;

            if (!(replacerValue.Obj is JsFunction replacerFn))
                throw new JsRuntimeException(JsErrorKind.TypeError, "Symbol.replace method must be callable");

            Span<JsValue> replaceArgs =
            [
                originalValue,
                replaceValue
            ];
            return realm.InvokeFunction(replacerFn, searchValue, replaceArgs);
        }

        static bool IsRegExpForStringMethod(JsRealm realm, in JsValue value)
        {
            if (!value.TryGetObject(out var obj))
                return false;

            JsValue matchValue;
            if (obj.TryGetPropertyAtom(realm, IdSymbolMatch, out matchValue, out _) &&
                !matchValue.IsUndefined)
                return JsRealm.ToBoolean(matchValue);

            return obj is JsRegExpObject;
        }

        static JsValue GetFlagsForRegExpStringMethod(JsRealm realm, JsObject obj)
        {
            return obj.TryGetPropertyAtom(realm, IdFlags, out var flagsValue, out _)
                ? flagsValue
                : JsValue.Undefined;
        }

        static bool IsHighSurrogate(char ch)
        {
            return ch is >= '\uD800' and <= '\uDBFF';
        }

        static bool IsLowSurrogate(char ch)
        {
            return ch is >= '\uDC00' and <= '\uDFFF';
        }

        static bool IsEcmaTrimCodeUnit(char ch)
        {
            return ch switch
            {
                '\u0009' or '\u000A' or '\u000B' or '\u000C' or '\u000D' or '\u0020' or '\u00A0' or '\u1680' or
                    '\u2000' or '\u2001' or '\u2002' or '\u2003' or '\u2004' or '\u2005' or '\u2006' or '\u2007' or
                    '\u2008' or '\u2009' or '\u200A' or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' or
                    '\uFEFF' => true,
                _ => false
            };
        }

        static JsString TrimEcmaWhitespace(JsString value, bool trimStart, bool trimEnd)
        {
            if (value.Length == 0)
                return value;

            char[]? pooledChars = null;
            var chars = value.Flatten(out pooledChars);
            try
            {
                var start = 0;
                var end = chars.Length - 1;

                if (trimStart)
                    while (start < chars.Length && IsEcmaTrimCodeUnit(chars[start]))
                        start++;

                if (trimEnd)
                    while (end >= start && IsEcmaTrimCodeUnit(chars[end]))
                        end--;

                if (start == 0 && end == chars.Length - 1)
                    return value;
                if (start > end)
                    return JsString.Empty;
                return value.Slice(start, end - start + 1);
            }
            finally
            {
                if (pooledChars is not null)
                    ArrayPool<char>.Shared.Return(pooledChars);
            }
        }

        static bool IsStringWellFormed(JsString value)
        {
            char[]? pooledChars = null;
            var chars = value.Flatten(out pooledChars);
            try
            {
                for (var i = 0; i < chars.Length; i++)
                {
                    var ch = chars[i];
                    if (IsHighSurrogate(ch))
                    {
                        if (i + 1 >= chars.Length || !IsLowSurrogate(chars[i + 1]))
                            return false;
                        i++;
                        continue;
                    }

                    if (IsLowSurrogate(ch))
                        return false;
                }

                return true;
            }
            finally
            {
                if (pooledChars is not null)
                    ArrayPool<char>.Shared.Return(pooledChars);
            }
        }

        static JsString ToWellFormedString(JsString value)
        {
            if (IsStringWellFormed(value))
                return value;

            char[]? pooledChars = null;
            var chars = value.Flatten(out pooledChars);
            try
            {
                using var builder = new PooledCharBuilder(stackalloc char[Math.Min(chars.Length + 8, 256)]);
                for (var i = 0; i < chars.Length; i++)
                {
                    var ch = chars[i];
                    if (IsHighSurrogate(ch))
                    {
                        if (i + 1 < chars.Length && IsLowSurrogate(chars[i + 1]))
                        {
                            builder.Append(ch);
                            builder.Append(chars[i + 1]);
                            i++;
                        }
                        else
                        {
                            builder.Append('\uFFFD');
                        }

                        continue;
                    }

                    if (IsLowSurrogate(ch))
                    {
                        builder.Append('\uFFFD');
                        continue;
                    }

                    builder.Append(ch);
                }

                return builder.ToString();
            }
            finally
            {
                if (pooledChars is not null)
                    ArrayPool<char>.Shared.Return(pooledChars);
            }
        }

        var valueOfFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var thisValue = info.ThisValue;
                if (thisValue.IsString)
                    return thisValue;
                if (thisValue.TryGetObject(out var obj) && obj is JsStringObject boxed)
                    return boxed.Value;
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "String.prototype.valueOf requires that 'this' be a String", errorRealm: info.Function.Realm);
            }, "valueOf", 0);

        var toStringFn = new JsHostFunction(Realm,
            static (in info) =>
            {
                var thisValue = info.ThisValue;
                if (thisValue.IsString)
                    return thisValue;
                if (thisValue.TryGetObject(out var obj) && obj is JsStringObject boxed)
                    return boxed.Value;
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "String.prototype.toString requires that 'this' be a String", errorRealm: info.Function.Realm);
            }, "toString", 0);

        var iteratorFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var text = ToCoercibleJsString(realm, thisValue, "[Symbol.iterator]");
            return new JsStringIteratorObject(realm, text);
        }, "[Symbol.iterator]", 0);

        var atFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "at");
            var len = text.Length;
            var relativeIndex = args.Length == 0 ? 0d : realm.ToIntegerOrInfinity(args[0]);
            if (double.IsPositiveInfinity(relativeIndex) || double.IsNegativeInfinity(relativeIndex))
                return JsValue.Undefined;
            var index = relativeIndex >= 0d ? (int)relativeIndex : len + (int)relativeIndex;
            if (index < 0 || index >= len)
                return JsValue.Undefined;
            return JsValue.FromString(text[index].ToString());
        }, "at", 1);

        var charAtFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "charAt");
            var position = args.Length == 0 ? 0d : realm.ToIntegerOrInfinity(args[0]);
            if (double.IsInfinity(position))
                return JsValue.FromString(string.Empty);
            var index = (int)position;
            return JsValue.FromString(index < 0 || index >= text.Length ? string.Empty : text[index].ToString());
        }, "charAt", 1);

        var charCodeAtFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "charCodeAt");
            var position = args.Length == 0 ? 0d : realm.ToIntegerOrInfinity(args[0]);
            if (double.IsInfinity(position))
                return JsValue.NaN;
            var index = (int)position;
            return index < 0 || index >= text.Length ? JsValue.NaN : new((double)text[index]);
        }, "charCodeAt", 1);

        var codePointAtFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "codePointAt");
            var position = args.Length == 0 ? 0d : realm.ToIntegerOrInfinity(args[0]);
            if (double.IsInfinity(position))
                return JsValue.Undefined;
            var index = (int)position;
            if (index < 0 || index >= text.Length)
                return JsValue.Undefined;
            var first = text[index];
            if (!char.IsHighSurrogate(first) || index + 1 >= text.Length)
                return new((double)first);

            var second = text[index + 1];
            if (!char.IsLowSurrogate(second))
                return new((double)first);

            return new(char.ConvertToUtf32(first, second));
        }, "codePointAt", 1);

        var concatFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var result = ToCoercibleJsString(realm, thisValue, "concat");
            if (args.Length == 0)
                return JsValue.FromString(result);
            for (var i = 0; i < args.Length; i++)
                result = JsString.Concat(result, realm.ToJsStringValueSlowPath(args[i]));
            return JsValue.FromString(result);
        }, "concat", 1);

        var indexOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "indexOf");
            var search = args.Length != 0 ? realm.ToJsStringValueSlowPath(args[0]) : "undefined";
            var start = ClampStringPosition(args.Length > 1 ? realm.ToIntegerOrInfinity(args[1]) : 0d, text.Length);
            return JsValue.FromInt32(text.IndexOf(search, start));
        }, "indexOf", 1);

        var lastIndexOfFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "lastIndexOf");
            var search = args.Length != 0 ? realm.ToJsStringValueSlowPath(args[0]) : "undefined";
            var numPos = args.Length > 1 ? realm.ToNumberSlowPath(args[1]) : double.NaN;
            var pos = double.IsNaN(numPos)
                ? double.PositiveInfinity
                : realm.ToIntegerOrInfinity(new(numPos));
            var start = ClampStringPosition(pos, text.Length);
            return JsValue.FromInt32(text.LastIndexOf(search, start));
        }, "lastIndexOf", 1);

        var sliceFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "slice");
            long length = text.Length;
            var start = NormalizeRelativeIndex(args.Length > 0 ? realm.ToIntegerOrInfinity(args[0]) : 0d, length);
            var end = args.Length > 1 && !args[1].IsUndefined
                ? NormalizeRelativeIndex(realm.ToIntegerOrInfinity(args[1]), length)
                : length;
            return JsValue.FromString(GetSubstringByCodeUnit(text, (int)start, (int)Math.Max(start, end)));
        }, "slice", 2);

        var substringFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "substring");
            var len = text.Length;
            var intStart = args.Length > 0 ? realm.ToIntegerOrInfinity(args[0]) : 0d;
            var intEnd = args.Length > 1 && !args[1].IsUndefined ? realm.ToIntegerOrInfinity(args[1]) : len;
            var finalStart = ClampStringPosition(intStart, len);
            var finalEnd = ClampStringPosition(intEnd, len);
            var from = Math.Min(finalStart, finalEnd);
            var to = Math.Max(finalStart, finalEnd);
            return JsValue.FromString(GetSubstringByCodeUnit(text, from, to));
        }, "substring", 2);

        var substrFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "substr");
            var len = text.Length;

            var startNumber = args.Length > 0 ? realm.ToIntegerOrInfinity(args[0]) : 0d;
            var start = double.IsNegativeInfinity(startNumber)
                ? 0
                : startNumber < 0
                    ? Math.Max(len + (int)startNumber, 0)
                    : Math.Min((int)startNumber, len);

            int size;
            if (args.Length < 2 || args[1].IsUndefined)
            {
                size = len - start;
            }
            else
            {
                var lengthNumber = realm.ToIntegerOrInfinity(args[1]);
                size = double.IsPositiveInfinity(lengthNumber)
                    ? len - start
                    : Math.Max(0, (int)lengthNumber);
            }

            var end = Math.Min(start + size, len);
            return JsValue.FromString(GetSubstringByCodeUnit(text, start, end));
        }, "substr", 2);

        var startsWithFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "startsWith");
            if (args.Length > 0 && IsRegExpForStringMethod(realm, args[0]))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "First argument to String.prototype.startsWith must not be a RegExp");
            var search = args.Length != 0 ? realm.ToJsStringValueSlowPath(args[0]) : "undefined";
            var start = ClampStringPosition(args.Length > 1 ? realm.ToIntegerOrInfinity(args[1]) : 0d, text.Length);
            return search.Length > text.Length - start || !text.Slice(start, text.Length - start).StartsWith(search)
                ? JsValue.False
                : JsValue.True;
        }, "startsWith", 1);

        var endsWithFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "endsWith");
            if (args.Length > 0 && IsRegExpForStringMethod(realm, args[0]))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "First argument to String.prototype.endsWith must not be a RegExp");
            var search = args.Length != 0 ? realm.ToJsStringValueSlowPath(args[0]) : "undefined";
            var len = text.Length;
            var end = ClampStringPosition(
                args.Length > 1 && !args[1].IsUndefined ? realm.ToIntegerOrInfinity(args[1]) : len, len);
            return search.Length > end || !text.Slice(0, end).EndsWith(search) ? JsValue.False : JsValue.True;
        }, "endsWith", 1);

        var includesFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "includes");
            if (args.Length > 0 && IsRegExpForStringMethod(realm, args[0]))
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "First argument to String.prototype.includes must not be a RegExp");
            var search = args.Length != 0 ? realm.ToJsStringValueSlowPath(args[0]) : "undefined";
            var start = ClampStringPosition(args.Length > 1 ? realm.ToIntegerOrInfinity(args[1]) : 0d, text.Length);
            return text.IndexOf(search, start) >= 0 ? JsValue.True : JsValue.False;
        }, "includes", 1);

        var padStartFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "padStart");
            var maxLength = args.Length == 0 ? 0 : realm.ToLength(args[0]);
            if (maxLength <= text.Length)
                return JsValue.FromString(text);
            var fillString = args.Length < 2 || args[1].IsUndefined ? " " : realm.ToJsStringValueSlowPath(args[1]);
            if (fillString.Length == 0)
                return JsValue.FromString(text);
            var padding = BuildPaddingString(maxLength - text.Length, fillString);
            return JsValue.FromString(JsString.Concat(padding, text));
        }, "padStart", 1);

        var padEndFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "padEnd");
            var maxLength = args.Length == 0 ? 0 : realm.ToLength(args[0]);
            if (maxLength <= text.Length)
                return JsValue.FromString(text);
            var fillString = args.Length < 2 || args[1].IsUndefined ? " " : realm.ToJsStringValueSlowPath(args[1]);
            if (fillString.Length == 0)
                return JsValue.FromString(text);
            var padding = BuildPaddingString(maxLength - text.Length, fillString);
            return JsValue.FromString(JsString.Concat(text, padding));
        }, "padEnd", 1);

        var repeatFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "repeat");
            var countNumber = args.Length == 0 ? 0d : realm.ToIntegerOrInfinity(args[0]);
            if (countNumber < 0d || double.IsPositiveInfinity(countNumber))
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid count value");
            var count = double.IsNegativeInfinity(countNumber) ? 0 : (long)countNumber;
            if (count == 0 || text.Length == 0)
                return JsValue.FromString(string.Empty);
            if (count > int.MaxValue / Math.Max(text.Length, 1))
                throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid string length");
            return JsValue.FromString(text.Repeat(count));
        }, "repeat", 1);

        var replaceFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var searchValue = args.Length > 0 ? args[0] : JsValue.Undefined;
            var replaceValue = args.Length > 1 ? args[1] : JsValue.Undefined;
            var replacedByMethod = CallReplaceMethodIfPresent(realm, searchValue, thisValue, replaceValue);
            if (!replacedByMethod.IsUndefined)
                return replacedByMethod;

            var text = ToCoercibleJsString(realm, thisValue, "replace");
            var searchString = realm.ToJsStringValueSlowPath(searchValue);
            var functionalReplace = replaceValue.TryGetObject(out var replaceObj) && replaceObj is JsFunction;
            var replacementTemplate = functionalReplace ? string.Empty : realm.ToJsStringSlowPath(replaceValue);
            var position = text.IndexOf(searchString);
            if (position < 0)
                return JsValue.FromString(text);

            var builder =
                new PooledCharBuilder(stackalloc char[Math.Min(text.Length + Math.Max(replacementTemplate.Length, 16),
                    256)]);
            try
            {
                if (position > 0)
                    builder.Append(text, 0, position);

                if (functionalReplace)
                {
                    var replaceFnObj = (JsFunction)replaceObj!;
                    Span<JsValue> replaceArgs =
                    [
                        JsValue.FromString(searchString),
                        JsValue.FromInt32(position),
                        JsValue.FromString(text)
                    ];
                    builder.Append(
                        realm.ToJsStringValueSlowPath(
                            realm.InvokeFunction(replaceFnObj, JsValue.Undefined, replaceArgs)));
                }
                else
                {
                    AppendSubstitution(ref builder, searchString, text, position, replacementTemplate);
                }

                var followingStart = position + searchString.Length;
                if (followingStart < text.Length)
                    builder.Append(text, followingStart, text.Length - followingStart);

                return JsValue.FromString(builder.ToString());
            }
            finally
            {
                builder.Dispose();
            }
        }, "replace", 2);

        var replaceAllFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (thisValue.IsNullOrUndefined)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "String.prototype.replaceAll called on null or undefined");

            var searchValue = args.Length > 0 ? args[0] : JsValue.Undefined;
            var replaceValue = args.Length > 1 ? args[1] : JsValue.Undefined;

            if (searchValue.TryGetObject(out var searchObj))
                if (IsRegExpForStringMethod(realm, searchValue))
                {
                    var flagsValue = GetFlagsForRegExpStringMethod(realm, searchObj);
                    if (flagsValue.IsNullOrUndefined)
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "String.prototype.replaceAll called with non-coercible flags");
                    var flags = realm.ToJsStringSlowPath(flagsValue);
                    if (!flags.Contains('g'))
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "String.prototype.replaceAll called with a non-global RegExp argument");
                }

            var replacedByMethod = CallReplaceMethodIfPresent(realm, searchValue, thisValue, replaceValue);
            if (!replacedByMethod.IsUndefined)
                return replacedByMethod;

            var text = ToCoercibleJsString(realm, thisValue, "replaceAll");
            var searchString = realm.ToJsStringValueSlowPath(searchValue);
            var functionalReplace = replaceValue.TryGetObject(out var replaceObj) && replaceObj is JsFunction;
            var replacementTemplate = functionalReplace ? string.Empty : realm.ToJsStringSlowPath(replaceValue);
            var searchLength = searchString.Length;
            var advanceBy = Math.Max(1, searchLength);
            var position = text.IndexOf(searchString);
            if (position < 0)
                return JsValue.FromString(text);

            var builder =
                new PooledCharBuilder(stackalloc char[Math.Min(text.Length + Math.Max(replacementTemplate.Length, 16),
                    256)]);
            try
            {
                var endOfLastMatch = 0;
                while (position >= 0)
                {
                    if (position > endOfLastMatch)
                        builder.Append(text, endOfLastMatch, position - endOfLastMatch);

                    if (functionalReplace)
                    {
                        var replaceFnObj = (JsFunction)replaceObj!;
                        Span<JsValue> replaceArgs =
                        [
                            JsValue.FromString(searchString),
                            JsValue.FromInt32(position),
                            JsValue.FromString(text)
                        ];
                        builder.Append(realm.ToJsStringValueSlowPath(
                            realm.InvokeFunction(replaceFnObj, JsValue.Undefined, replaceArgs)));
                    }
                    else
                    {
                        AppendSubstitution(ref builder, searchString, text, position, replacementTemplate);
                    }

                    endOfLastMatch = position + searchLength;
                    var nextSearchStart = position + advanceBy;
                    position = nextSearchStart > text.Length
                        ? -1
                        : text.IndexOf(searchString, nextSearchStart);
                }

                if (endOfLastMatch < text.Length)
                    builder.Append(text, endOfLastMatch, text.Length - endOfLastMatch);

                return JsValue.FromString(builder.ToString());
            }
            finally
            {
                builder.Dispose();
            }
        }, "replaceAll", 2);

        var splitFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            if (thisValue.IsNullOrUndefined)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "String.prototype.split called on null or undefined");

            var separatorValue = args.Length > 0 ? args[0] : JsValue.Undefined;
            var limitValue = args.Length > 1 ? args[1] : JsValue.Undefined;

            if (!separatorValue.IsUndefined && !separatorValue.IsNull &&
                separatorValue.TryGetObject(out var separatorHookObj) &&
                separatorHookObj.TryGetPropertyAtom(realm, IdSymbolSplit, out var splitterValue,
                    out _) &&
                !splitterValue.IsUndefined && !splitterValue.IsNull)
            {
                if (!splitterValue.TryGetObject(out var splitterObj) || splitterObj is not JsFunction splitterFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Symbol.split method must be callable");
                Span<JsValue> splitterArgs = [thisValue, limitValue];
                return realm.InvokeFunction(splitterFn, separatorValue, splitterArgs);
            }

            var text = ToCoercibleJsString(realm, thisValue, "split");
            var limit = !limitValue.IsUndefined ? realm.ToUint32(limitValue) : uint.MaxValue;
            JsString separatorString = default;
            var hasStringSeparator = false;

            if (!separatorValue.IsUndefined && (!separatorValue.TryGetObject(out var separatorObj) ||
                                                separatorObj is not JsRegExpObject))
            {
                separatorString = realm.ToJsStringValueSlowPath(separatorValue);
                hasStringSeparator = true;
            }

            var result = realm.CreateArrayObject();
            if (limit == 0)
                return result;

            if (separatorValue.IsUndefined)
            {
                FreshArrayOperations.DefineElement(result, 0, JsValue.FromString(text));
                result.SetLength(1);
                return result;
            }

            if (separatorValue.TryGetObject(out var separatorObject) &&
                separatorObject is JsRegExpObject separatorRegex)
            {
                var flatText = text.Flatten();
                if (separatorRegex.Pattern.Length == 0)
                {
                    uint count = 0;
                    for (var i = 0; i < flatText.Length && count < limit; i++, count++)
                        FreshArrayOperations.DefineElement(result, count, JsValue.FromString(flatText[i].ToString()));
                    result.SetLength(count);
                    return result;
                }

                if (text.Length == 0)
                {
                    if (JsRegExpRuntime.Exec(realm,
                            new(realm, separatorRegex.Pattern,
                                separatorRegex.Flags.Contains('g') ? separatorRegex.Flags : separatorRegex.Flags + "g",
                                true,
                                separatorRegex.IgnoreCase,
                                separatorRegex.Multiline,
                                separatorRegex.Sticky,
                                separatorRegex.Unicode,
                                separatorRegex.DotAll,
                                separatorRegex.Engine),
                            flatText).IsNull)
                    {
                        FreshArrayOperations.DefineElement(result, 0, JsValue.FromString(string.Empty));
                        result.SetLength(1);
                    }

                    return result;
                }

                var splitterFlags = separatorRegex.Flags.Contains('g')
                    ? separatorRegex.Flags
                    : separatorRegex.Flags + "g";
                var splitter = new JsRegExpObject(realm, separatorRegex.Pattern, splitterFlags,
                    true,
                    separatorRegex.IgnoreCase,
                    separatorRegex.Multiline,
                    separatorRegex.Sticky,
                    separatorRegex.Unicode,
                    separatorRegex.DotAll,
                    separatorRegex.Engine);

                uint resultIndex = 0;
                var splitStart = 0;
                while (resultIndex < limit)
                {
                    var matchValue = JsRegExpRuntime.Exec(realm, splitter, flatText);
                    if (matchValue.IsNull || !matchValue.TryGetObject(out var matchObj))
                        break;

                    if (!matchObj.TryGetPropertyByAtom(IdIndex, out var matchIndexValue) || !matchIndexValue.IsNumber)
                        break;

                    var matchIndex = (int)matchIndexValue.NumberValue;
                    var matched = matchObj.TryGetElement(0, out var matchedValue)
                        ? realm.ToJsStringSlowPath(matchedValue)
                        : string.Empty;

                    if (matched.Length == 0)
                    {
                        if (matchIndex == splitStart)
                        {
                            if (matchIndex >= text.Length)
                                break;
                            splitter.SetNamedSlotUnchecked(RegExpOwnLastIndexSlot, JsValue.FromInt32(matchIndex + 1));
                            continue;
                        }

                        if (matchIndex == text.Length)
                            break;
                    }

                    FreshArrayOperations.DefineElement(result, resultIndex++,
                        JsValue.FromString(text.Slice(splitStart, matchIndex - splitStart)));
                    if (resultIndex == limit)
                    {
                        result.SetLength(resultIndex);
                        return result;
                    }

                    var endIndex = matchIndex + matched.Length;
                    splitStart = endIndex;

                    if (matched.Length == 0)
                    {
                        if (endIndex >= text.Length)
                            break;
                        splitter.SetNamedSlotUnchecked(RegExpOwnLastIndexSlot, JsValue.FromInt32(endIndex + 1));
                    }
                }

                if (resultIndex < limit)
                    FreshArrayOperations.DefineElement(result, resultIndex++,
                        JsValue.FromString(text.Slice(splitStart, text.Length - splitStart)));
                result.SetLength(resultIndex);
                return result;
            }

            if (hasStringSeparator && separatorString.Length == 0)
            {
                char[]? pooledChars = null;
                var chars = text.Flatten(out pooledChars);
                try
                {
                    uint count = 0;
                    for (var i = 0; i < chars.Length && count < limit; i++, count++)
                        FreshArrayOperations.DefineElement(result, count, JsValue.FromString(chars[i].ToString()));
                    result.SetLength(count);
                    return result;
                }
                finally
                {
                    if (pooledChars is not null)
                        ArrayPool<char>.Shared.Return(pooledChars);
                }
            }

            uint index = 0;
            var start = 0;
            while (index < limit)
            {
                var match = text.IndexOf(separatorString, start);
                if (match < 0)
                    break;

                FreshArrayOperations.DefineElement(result, index++,
                    JsValue.FromString(text.Slice(start, match - start)));
                start = match + separatorString.Length;
                if (index == limit)
                {
                    result.SetLength(index);
                    return result;
                }
            }

            if (index < limit)
                FreshArrayOperations.DefineElement(result, index++,
                    JsValue.FromString(text.Slice(start, text.Length - start)));
            result.SetLength(index);
            return result;
        }, "split", 2);

        var normalizeFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "normalize");
            var form = args.Length == 0 || args[0].IsUndefined ? "NFC" : realm.ToJsStringSlowPath(args[0]);
            var normalizationForm = form switch
            {
                "NFC" => NormalizationForm.FormC,
                "NFD" => NormalizationForm.FormD,
                "NFKC" => NormalizationForm.FormKC,
                "NFKD" => NormalizationForm.FormKD,
                _ => throw new JsRuntimeException(JsErrorKind.RangeError,
                    "The normalization form should be one of NFC, NFD, NFKC, NFKD")
            };
            return JsValue.FromString(text.Flatten().Normalize(normalizationForm));
        }, "normalize", 0);

        var matchFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var inputJs = ToCoercibleJsString(realm, thisValue, "match");
            var matcherValue = args.Length > 0 ? args[0] : JsValue.Undefined;
            if (matcherValue.TryGetObject(out var matcherObj) &&
                matcherObj.TryGetPropertyAtom(realm, IdSymbolMatch, out var matchMethodValue,
                    out _) &&
                !matchMethodValue.IsUndefined && !matchMethodValue.IsNull)
            {
                if (!matchMethodValue.TryGetObject(out var matchMethodObj) ||
                    matchMethodObj is not JsFunction matchMethod)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Symbol.match method must be callable");
                Span<JsValue> matchArgs = [thisValue];
                return realm.InvokeFunction(matchMethod, matcherValue, matchArgs);
            }

            var created = args.Length == 0
                ? realm.CreateRegExpObject("(?:)", string.Empty)
                : args[0].TryGetObject(out var reObj) && reObj is JsRegExpObject
                    ? args[0]
                    : realm.CreateRegExpObject(args[0].IsUndefined ? "(?:)" : realm.ToJsStringSlowPath(args[0]),
                        string.Empty);
            if (!created.TryGetObject(out var createdObj) ||
                !createdObj.TryGetPropertyAtom(realm, IdSymbolMatch, out var createdMatchValue,
                    out _) ||
                !createdMatchValue.TryGetObject(out var createdMatchObj) ||
                createdMatchObj is not JsFunction createdMatchFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Symbol.match method must be callable");

            var input = inputJs.Flatten();
            Span<JsValue> createdArgs = [JsValue.FromString(input)];
            return realm.InvokeFunction(createdMatchFn, created, createdArgs);
        }, "match", 1);

        var matchAllFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var inputJs = ToCoercibleJsString(realm, thisValue, "matchAll");
            var regexp = args.Length > 0 ? args[0] : JsValue.Undefined;
            if (regexp.TryGetObject(out var regexpObj))
            {
                if (IsRegExpForStringMethod(realm, regexp))
                {
                    var flagsValue = GetFlagsForRegExpStringMethod(realm, regexpObj);
                    if (flagsValue.IsNullOrUndefined)
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "String.prototype.matchAll called with non-coercible flags");
                    var flags = realm.ToJsStringSlowPath(flagsValue);
                    if (!flags.Contains('g'))
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "String.prototype.matchAll called with a non-global RegExp argument");
                }

                if (regexpObj.TryGetPropertyAtom(realm, IdSymbolMatchAll, out var matcherValue,
                        out _) &&
                    !matcherValue.IsUndefined && !matcherValue.IsNull)
                {
                    if (!matcherValue.TryGetObject(out var matcherObj) || matcherObj is not JsFunction matcherFn)
                        throw new JsRuntimeException(JsErrorKind.TypeError,
                            "Symbol.matchAll method must be callable");
                    Span<JsValue> matcherArgs = [JsValue.FromString(inputJs)];
                    return realm.InvokeFunction(matcherFn, regexp, matcherArgs);
                }
            }


            var pattern = args.Length == 0 || regexp.IsUndefined ? "(?:)" : realm.ToJsStringSlowPath(regexp);
            var created = realm.CreateRegExpObject(pattern, "g");
            created.TryGetObject(out var createdObj);
            var createdRegex = (JsRegExpObject)createdObj!;
            if (!createdRegex.TryGetPropertyAtom(realm, IdSymbolMatchAll, out var createdMatcherValue,
                    out _) ||
                !createdMatcherValue.TryGetObject(out var createdMatcherObj) ||
                createdMatcherObj is not JsFunction createdMatcherFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Symbol.matchAll method must be callable");

            var input = inputJs.Flatten();
            Span<JsValue> createdArgs = [JsValue.FromString(input)];
            return realm.InvokeFunction(createdMatcherFn, JsValue.FromObject(createdRegex), createdArgs);
        }, "matchAll", 1);

        var searchFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var inputJs = ToCoercibleJsString(realm, thisValue, "search");
            var regexp = args.Length > 0 ? args[0] : JsValue.Undefined;
            if (!regexp.IsUndefined && !regexp.IsNull &&
                regexp.TryGetObject(out var regexpObj) &&
                regexpObj.TryGetPropertyAtom(realm, IdSymbolSearch, out var searcherValue, out _) &&
                !searcherValue.IsUndefined && !searcherValue.IsNull)
            {
                if (!searcherValue.TryGetObject(out var searcherObj) || searcherObj is not JsFunction searcherFn)
                    throw new JsRuntimeException(JsErrorKind.TypeError, "Symbol.search method must be callable");
                Span<JsValue> searcherArgs = [thisValue];
                return realm.InvokeFunction(searcherFn, regexp, searcherArgs);
            }


            var pattern = args.Length != 0 ? realm.ToJsStringSlowPath(args[0]) : string.Empty;
            var created = realm.CreateRegExpObject(pattern, string.Empty);
            if (!created.TryGetObject(out var createdObj) ||
                !createdObj.TryGetPropertyAtom(realm, IdSymbolSearch, out var createdSearcherValue,
                    out _) ||
                !createdSearcherValue.TryGetObject(out var createdSearcherObj) ||
                createdSearcherObj is not JsFunction createdSearcherFn)
                throw new JsRuntimeException(JsErrorKind.TypeError, "Symbol.search method must be callable");

            var input = inputJs.Flatten();
            Span<JsValue> createdSearcherArgs = [JsValue.FromString(input)];
            return realm.InvokeFunction(createdSearcherFn, created, createdSearcherArgs);
        }, "search", 1);

        var toLowerCaseFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromString(JsStringCaseOperations.ToLowerCaseUnicodeDefault(
                ToCoercibleJsString(realm, thisValue, "toLowerCase")));
        }, "toLowerCase", 0);

        var toLocaleLowerCaseFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromString(JsStringLocaleCaseOperations.ToLocaleLowerCase(
                realm,
                ToCoercibleJsString(realm, thisValue, "toLocaleLowerCase"),
                info.Arguments));
        }, "toLocaleLowerCase", 0);

        var toUpperCaseFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromString(JsStringCaseOperations.ToUpperCaseUnicodeDefault(
                ToCoercibleJsString(realm, thisValue, "toUpperCase")));
        }, "toUpperCase", 0);

        var toLocaleUpperCaseFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromString(JsStringLocaleCaseOperations.ToLocaleUpperCase(
                realm,
                ToCoercibleJsString(realm, thisValue, "toLocaleUpperCase"),
                info.Arguments));
        }, "toLocaleUpperCase", 0);

        var localeCompareFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            var args = info.Arguments;
            var text = ToCoercibleJsString(realm, thisValue, "localeCompare");
            var that = args.Length == 0 ? "undefined" : realm.ToJsStringValueSlowPath(args[0]);
            if (realm.GlobalObject.TryGetPropertyAtom(realm, realm.Atoms.InternNoCheck("Intl"), out var intlValue,
                    out _) &&
                intlValue.TryGetObject(out var intlObject) &&
                intlObject.TryGetPropertyAtom(realm, realm.Atoms.InternNoCheck("Collator"), out var ctorValue, out _) &&
                ctorValue.TryGetObject(out var ctorObject) &&
                ctorObject is JsFunction ctorFn)
            {
                var locales = args.Length > 1 ? args[1] : JsValue.Undefined;
                var options = args.Length > 2 ? args[2] : JsValue.Undefined;
                var collatorValue =
                    realm.ConstructWithExplicitNewTarget(ctorFn, [locales, options], JsValue.FromObject(ctorFn), -1);
                if (collatorValue.TryGetObject(out var collatorObject))
                {
                    if (collatorObject is JsCollatorObject collator)
                        return JsValue.FromInt32(collator.Compare(text.Flatten(), that.Flatten()));

                    if (collatorObject.TryGetPropertyByAtom(IdCompare, out var compareValue) &&
                        compareValue.TryGetObject(out var compareObject) &&
                        compareObject is JsFunction compareFn)
                    {
                        Span<JsValue> compareArgs = [JsValue.FromString(text), JsValue.FromString(that)];
                        return realm.InvokeFunction(compareFn, JsValue.FromObject(collatorObject), compareArgs);
                    }
                }
            }

            var normalizedText = text.Flatten().Normalize(NormalizationForm.FormC);
            var normalizedThat = that.Flatten().Normalize(NormalizationForm.FormC);
            var comparison = string.Compare(normalizedText, normalizedThat, CultureInfo.CurrentCulture,
                CompareOptions.None);
            return comparison == 0 ? new(0d) : new JsValue(comparison);
        }, "localeCompare", 1);

        var trimFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromString(TrimEcmaWhitespace(ToCoercibleJsString(realm, thisValue, "trim"), true, true));
        }, "trim", 0);

        var trimStartFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromString(TrimEcmaWhitespace(ToCoercibleJsString(realm, thisValue, "trimStart"), true,
                false));
        }, "trimStart", 0);

        var trimEndFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromString(TrimEcmaWhitespace(ToCoercibleJsString(realm, thisValue, "trimEnd"), false,
                true));
        }, "trimEnd", 0);

        var isWellFormedFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return IsStringWellFormed(ToCoercibleJsString(realm, thisValue, "isWellFormed"))
                ? JsValue.True
                : JsValue.False;
        }, "isWellFormed", 0);

        var toWellFormedFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var thisValue = info.ThisValue;
            return JsValue.FromString(ToWellFormedString(ToCoercibleJsString(realm, thisValue, "toWellFormed")));
        }, "toWellFormed", 0);

        var stringIteratorNextFn = new JsHostFunction(Realm, (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var obj) || obj is not JsStringIteratorObject iterator)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "String Iterator.prototype.next called on incompatible receiver");
            return iterator.Next();
        }, "next", 0);

        var stringIteratorSelfFn = new JsHostFunction(Realm, (in info) =>
        {
            var thisValue = info.ThisValue;
            if (!thisValue.TryGetObject(out var obj) || obj is not JsStringIteratorObject)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "String Iterator [Symbol.iterator] called on incompatible receiver");
            return thisValue;
        }, "[Symbol.iterator]", 0);

        var atomTable = Realm.Atoms;
        var atomSubstr = atomTable.InternNoCheck("substr");
        Span<PropertyDefinition> defs =
        [
            PropertyDefinition.Mutable(IdConstructor, JsValue.FromObject(StringConstructor)),
            PropertyDefinition.Mutable(IdValueOf, JsValue.FromObject(valueOfFn)),
            PropertyDefinition.Mutable(IdToString, JsValue.FromObject(toStringFn)),
            PropertyDefinition.Mutable(IdAt, JsValue.FromObject(atFn)),
            PropertyDefinition.Mutable(IdCharAt, JsValue.FromObject(charAtFn)),
            PropertyDefinition.Mutable(IdCharCodeAt, JsValue.FromObject(charCodeAtFn)),
            PropertyDefinition.Mutable(IdCodePointAt, JsValue.FromObject(codePointAtFn)),
            PropertyDefinition.Mutable(IdConcat, JsValue.FromObject(concatFn)),
            PropertyDefinition.Mutable(IdIndexOf, JsValue.FromObject(indexOfFn)),
            PropertyDefinition.Mutable(IdLastIndexOf, JsValue.FromObject(lastIndexOfFn)),
            PropertyDefinition.Mutable(IdSlice, JsValue.FromObject(sliceFn)),
            PropertyDefinition.Mutable(IdSubstring, JsValue.FromObject(substringFn)),
            PropertyDefinition.Mutable(atomSubstr, JsValue.FromObject(substrFn)),
            PropertyDefinition.Mutable(IdStartsWith, JsValue.FromObject(startsWithFn)),
            PropertyDefinition.Mutable(IdEndsWith, JsValue.FromObject(endsWithFn)),
            PropertyDefinition.Mutable(IdIncludes, JsValue.FromObject(includesFn)),
            PropertyDefinition.Mutable(IdPadStart, JsValue.FromObject(padStartFn)),
            PropertyDefinition.Mutable(IdPadEnd, JsValue.FromObject(padEndFn)),
            PropertyDefinition.Mutable(IdRepeat, JsValue.FromObject(repeatFn)),
            PropertyDefinition.Mutable(IdReplace, JsValue.FromObject(replaceFn)),
            PropertyDefinition.Mutable(IdReplaceAll, JsValue.FromObject(replaceAllFn)),
            PropertyDefinition.Mutable(IdSplit, JsValue.FromObject(splitFn)),
            PropertyDefinition.Mutable(IdNormalize, JsValue.FromObject(normalizeFn)),
            PropertyDefinition.Mutable(IdMatch, JsValue.FromObject(matchFn)),
            PropertyDefinition.Mutable(IdMatchAll, JsValue.FromObject(matchAllFn)),
            PropertyDefinition.Mutable(IdSearch, JsValue.FromObject(searchFn)),
            PropertyDefinition.Mutable(IdToLowerCase, JsValue.FromObject(toLowerCaseFn)),
            PropertyDefinition.Mutable(IdToLocaleLowerCase, JsValue.FromObject(toLocaleLowerCaseFn)),
            PropertyDefinition.Mutable(IdLocaleCompare, JsValue.FromObject(localeCompareFn)),
            PropertyDefinition.Mutable(IdToUpperCase, JsValue.FromObject(toUpperCaseFn)),
            PropertyDefinition.Mutable(IdToLocaleUpperCase, JsValue.FromObject(toLocaleUpperCaseFn)),
            PropertyDefinition.Mutable(IdTrim, JsValue.FromObject(trimFn)),
            PropertyDefinition.Mutable(IdTrimStart, JsValue.FromObject(trimStartFn)),
            PropertyDefinition.Mutable(IdTrimEnd, JsValue.FromObject(trimEndFn)),
            PropertyDefinition.Mutable(IdIsWellFormed, JsValue.FromObject(isWellFormedFn)),
            PropertyDefinition.Mutable(IdToWellFormed, JsValue.FromObject(toWellFormedFn)),
            PropertyDefinition.Mutable(IdSymbolIterator, JsValue.FromObject(iteratorFn)),
            PropertyDefinition.Mutable(atomTable.InternNoCheck("trimLeft"), JsValue.FromObject(trimStartFn)),
            PropertyDefinition.Mutable(atomTable.InternNoCheck("trimRight"), JsValue.FromObject(trimEndFn))
        ];
        StringPrototype.DefineNewPropertiesNoCollision(Realm, defs);

        Span<PropertyDefinition> iteratorProtoDefs =
        [
            PropertyDefinition.Mutable(IdNext, JsValue.FromObject(stringIteratorNextFn)),
            PropertyDefinition.Mutable(IdSymbolIterator, JsValue.FromObject(stringIteratorSelfFn)),
            PropertyDefinition.Const(IdSymbolToStringTag, JsValue.FromString("String Iterator"),
                configurable: true)
        ];
        StringIteratorPrototype.DefineNewPropertiesNoCollision(Realm, iteratorProtoDefs);

        var fromCharCodeFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0)
                return JsValue.FromString(string.Empty);
            Span<char> chars = stackalloc char[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                var n = realm.ToNumber(args[i]);
                ushort code;
                if (double.IsNaN(n) || double.IsInfinity(n) || n == 0d)
                {
                    code = 0;
                }
                else
                {
                    var intPart = Math.Truncate(n);
                    var mod = intPart % 65536d;
                    if (mod < 0d)
                        mod += 65536d;
                    code = (ushort)mod;
                }

                chars[i] = (char)code;
            }

            return JsValue.FromString(chars.ToString());
        }, "fromCharCode", 1);

        var fromCodePointFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0)
                return JsValue.FromString(string.Empty);

            var rented = ArrayPool<char>.Shared.Rent(args.Length * 2);
            try
            {
                var length = 0;
                for (var i = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    var n = arg.IsNumber ? arg.NumberValue : realm.ToNumber(arg);
                    if (double.IsNaN(n) || double.IsInfinity(n) || n != Math.Truncate(n) || n < 0d || n > 0x10FFFF)
                        throw new JsRuntimeException(JsErrorKind.RangeError, "Invalid code point");

                    var codePoint = (int)n;
                    if (codePoint <= char.MaxValue)
                    {
                        rented[length++] = (char)codePoint;
                        continue;
                    }

                    codePoint -= 0x10000;
                    rented[length++] = (char)((codePoint >> 10) + 0xD800);
                    rented[length++] = (char)((codePoint & 0x3FF) + 0xDC00);
                }

                return JsValue.FromString(new string(rented, 0, length));
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }, "fromCodePoint", 1);
        StringFromCodePointFunction = fromCodePointFn;

        var rawFn = new JsHostFunction(Realm, (in info) =>
        {
            var realm = info.Realm;
            var args = info.Arguments;
            if (args.Length == 0 || !realm.TryToObject(args[0], out var templateObj))
                throw new JsRuntimeException(JsErrorKind.TypeError, "String.raw requires an object template");
            if (!templateObj.TryGetPropertyAtom(realm, IdRaw, out var rawValue, out _) ||
                !realm.TryToObject(rawValue, out var rawObj))
                throw new JsRuntimeException(JsErrorKind.TypeError, "String.raw template.raw must be an object");

            if (!rawObj.TryGetPropertyAtom(realm, IdLength, out var lengthValue, out _))
                lengthValue = JsValue.Undefined;
            var lengthNumber = realm.ToNumberSlowPath(lengthValue);
            var literalCount = double.IsNaN(lengthNumber) || lengthNumber <= 0d
                ? 0
                : lengthNumber >= 9007199254740991d
                    ? 9007199254740991L
                    : (long)Math.Truncate(lengthNumber);
            if (literalCount <= 0)
                return JsValue.FromString(string.Empty);

            var substitutionCount = Math.Max(0, args.Length - 1);
            using var builder = new PooledCharBuilder(stackalloc char[128]);
            for (long i = 0; i < literalCount; i++)
            {
                JsValue literalValue;
                if (i <= uint.MaxValue)
                {
                    rawObj.TryGetElement((uint)i, out literalValue);
                }
                else
                {
                    var atomIndex = realm.Atoms.InternNoCheck(i.ToString(CultureInfo.InvariantCulture));
                    _ = rawObj.TryGetPropertyAtom(realm, atomIndex, out literalValue, out _);
                }

                builder.Append(realm.ToJsStringValueSlowPath(literalValue));
                if (i + 1 == literalCount)
                    break;
                if (i < substitutionCount)
                    builder.Append(realm.ToJsStringValueSlowPath(args[(int)i + 1]));
            }

            return JsValue.FromString(builder.ToString());
        }, "raw", 1);

        Span<PropertyDefinition> ctorDefs =
        [
            PropertyDefinition.Mutable(IdFromCharCode, JsValue.FromObject(fromCharCodeFn)),
            PropertyDefinition.Mutable(IdFromCodePoint, JsValue.FromObject(fromCodePointFn)),
            PropertyDefinition.Mutable(IdRaw, JsValue.FromObject(rawFn))
        ];
        StringConstructor.InitializePrototypeProperty(StringPrototype);
        StringConstructor.DefineNewPropertiesNoCollision(Realm, ctorDefs);
    }
}
