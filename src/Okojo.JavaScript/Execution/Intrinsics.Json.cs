using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Okojo.JavaScript.Internals;

namespace Okojo.JavaScript.Execution;

public partial class Intrinsics
{
    private void InstallJsonBuiltins()
    {
        // JSON is installed as a plain object on globalThis.
    }

    private JsPlainObject CreateJsonObject()
    {
        var json = new JsPlainObject(Realm, false) { Prototype = ObjectPrototype };

        const int parseAtom = IdParse;
        const int stringifyAtom = IdStringify;
        const int rawJsonAtom = IdRawJson;
        var isRawJsonAtom = Atoms.InternNoCheck("isRawJSON");

        var parseFn = new JsHostFunction(
            Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                if (args.Length == 0)
                    throw new JsRuntimeException(
                        JsErrorKind.SyntaxError,
                        "Unexpected end of JSON input"
                    );
                var text = realm.ToJsStringSlowPath(args[0]);
                JsFunction? reviver = null;
                if (
                    args.Length > 1
                    && args[1].TryGetObject(out var reviverObj)
                    && reviverObj is JsFunction reviverFn
                )
                    reviver = reviverFn;
                try
                {
                    if (reviver is null)
                        return ParseJsonText(realm, text);

                    using var doc = JsonDocument.Parse(text);

                    var holder = new JsPlainObject(realm);
                    holder.DefineDataProperty(
                        string.Empty,
                        ConvertJsonElement(realm, doc.RootElement),
                        JsShapePropertyFlags.Open
                    );
                    return InternalizeJsonProperty(
                        realm,
                        holder,
                        string.Empty,
                        doc.RootElement,
                        reviver
                    );
                }
                catch (JsonException ex)
                {
                    throw new JsRuntimeException(
                        JsErrorKind.SyntaxError,
                        ex.Message,
                        innerException: ex
                    );
                }
            },
            "parse",
            2
        );

        var stringifyFn = new JsHostFunction(
            Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                var value = args.Length > 0 ? args[0] : JsValue.Undefined;
                JsFunction? replacer = null;
                string[]? propertyList = null;
                if (args.Length > 1 && args[1].TryGetObject(out var replacerObj))
                {
                    if (replacerObj is JsFunction replacerFn)
                        replacer = replacerFn;
                    else if (IsArrayObject(realm, replacerObj))
                        propertyList = JsonStringifyBuildPropertyList(realm, replacerObj);
                }

                var gap = args.Length > 2 ? JsonStringifyComputeGap(realm, args[2]) : string.Empty;

                var visited = new HashSet<JsObject>();
                var serialized = JsonStringifyTopLevel(
                    realm,
                    value,
                    replacer,
                    propertyList,
                    gap,
                    visited
                );
                return serialized is null ? JsValue.Undefined : serialized;
            },
            "stringify",
            3
        );

        var rawJsonFn = new JsHostFunction(
            Realm,
            static (in info) =>
            {
                var realm = info.Realm;
                var args = info.Arguments;
                var text = args.Length == 0 ? string.Empty : realm.ToJsStringSlowPath(args[0]);
                ValidateRawJsonText(text);
                return new JsRawJsonObject(realm, text);
            },
            "rawJSON",
            1
        );

        var isRawJsonFn = new JsHostFunction(
            Realm,
            static (in info) =>
            {
                var args = info.Arguments;
                return
                    args.Length > 0 && args[0].TryGetObject(out var obj) && obj is JsRawJsonObject
                    ? JsValue.True
                    : JsValue.False;
            },
            "isRawJSON",
            1
        );

        var builtinFlags = JsShapePropertyFlags.Writable | JsShapePropertyFlags.Configurable;
        json.DefineDataPropertyAtom(Realm, parseAtom, parseFn, builtinFlags);
        json.DefineDataPropertyAtom(Realm, stringifyAtom, stringifyFn, builtinFlags);
        json.DefineDataPropertyAtom(Realm, rawJsonAtom, rawJsonFn, builtinFlags);
        json.DefineDataPropertyAtom(Realm, isRawJsonAtom, isRawJsonFn, builtinFlags);
        json.DefineDataPropertyAtom(
            Realm,
            IdSymbolToStringTag,
            "JSON",
            JsShapePropertyFlags.Configurable
        );
        return json;
    }

    private static JsValue ConvertJsonElement(JsRealm realm, JsonElement element)
    {
        var buffers = new JsonConversionBuffers();
        try
        {
            return ConvertJsonElement(realm, element, buffers);
        }
        finally
        {
            buffers.Dispose();
        }
    }

    private static JsValue ParseJsonText(JsRealm realm, string text)
    {
        var parser = new JsonTextParser(realm, text);
        return parser.Parse();
    }

    private ref struct JsonTextParser
    {
        private const int MaxDepth = 64;

        private readonly JsRealm realm;
        private readonly string source;
        private readonly JsonConversionBuffers buffers;
        private int offset;
        private int depth;

        public JsonTextParser(JsRealm realm, string source)
        {
            this.realm = realm;
            this.source = source;
            buffers = new JsonConversionBuffers();
        }

        public JsValue Parse()
        {
            try
            {
                SkipWhitespace();
                if (offset == source.Length)
                    throw new JsonException("The JSON payload is empty.");

                var value = ParseValue();
                SkipWhitespace();
                if (offset != source.Length)
                    throw new JsonException("The JSON payload contains multiple values.");

                return value;
            }
            finally
            {
                buffers.Dispose();
            }
        }

        private JsValue ParseValue()
        {
            SkipWhitespace();
            if (offset == source.Length)
                throw new JsonException("Unexpected end of JSON input.");

            return source[offset] switch
            {
                'n' when ConsumeLiteral("null") => JsValue.Null,
                't' when ConsumeLiteral("true") => JsValue.True,
                'f' when ConsumeLiteral("false") => JsValue.False,
                '"' => JsValue.FromString(ParseString()),
                '[' => ParseArray(),
                '{' => ParseObject(),
                '-' or >= '0' and <= '9' => ParseNumber(),
                _ => throw new JsonException("Unexpected JSON token."),
            };
        }

        private JsValue ParseArray()
        {
            EnterContainer();
            try
            {
                offset++;
                var array = realm.CreateArrayObject();
                SkipWhitespace();
                if (Consume(']'))
                    return array;

                var index = 0;
                while (true)
                {
                    var value = ParseValue();
                    var dense = array.Dense!;
                    if (index >= dense.Length)
                    {
                        if (!array.TryEnsureDenseCapacity((uint)index))
                            throw new JsonException("Unable to allocate JSON array.");
                        dense = array.Dense!;
                    }

                    dense[index++] = value;
                    SkipWhitespace();
                    if (Consume(']'))
                        break;
                    Expect(',');
                    SkipWhitespace();
                    if (Peek(']'))
                        throw new JsonException("Trailing commas are not allowed in JSON arrays.");
                }

                array.SetLength((uint)index);
                return array;
            }
            finally
            {
                depth--;
            }
        }

        private JsValue ParseObject()
        {
            EnterContainer();
            try
            {
                offset++;
                var obj = new JsPlainObject(realm, useDictionaryMode: true);
                var staging = buffers.AcquireObjectStaging();
                var usedGenericNamedPath = false;
                try
                {
                    SkipWhitespace();
                    if (Consume('}'))
                        return obj;

                    while (true)
                    {
                        if (!Peek('"'))
                            throw new JsonException("Expected a JSON property name.");

                        var name = ParseString();
                        SkipWhitespace();
                        Expect(':');
                        var propValue = ParseValue();
                        if (TryGetArrayIndexFromCanonicalString(name, out var index))
                        {
                            obj.SetElement(index, propValue);
                        }
                        else
                        {
                            var atom = realm.Atoms.InternNoCheck(name);
                            if (!usedGenericNamedPath && !staging.ContainsAtom(atom))
                            {
                                staging.Add(atom, propValue);
                            }
                            else
                            {
                                if (!usedGenericNamedPath && staging.Count != 0)
                                {
                                    obj.InitializeDynamicOpenDataPropertiesNoCollision(
                                        realm,
                                        staging.Atoms,
                                        staging.Values
                                    );
                                    usedGenericNamedPath = true;
                                }

                                obj.SetPropertyAtom(realm, atom, propValue, out _);
                            }
                        }

                        SkipWhitespace();
                        if (Consume('}'))
                            break;
                        Expect(',');
                        SkipWhitespace();
                        if (Peek('}'))
                            throw new JsonException(
                                "Trailing commas are not allowed in JSON objects."
                            );
                    }

                    if (!usedGenericNamedPath && staging.Count != 0)
                        obj.InitializeDynamicOpenDataPropertiesNoCollision(
                            realm,
                            staging.Atoms,
                            staging.Values
                        );

                    return obj;
                }
                finally
                {
                    staging.Release();
                }
            }
            finally
            {
                depth--;
            }
        }

        private JsValue ParseNumber()
        {
            var start = offset;
            var negative = Consume('-');
            if (negative && !IsDigit(PeekCharacter()))
                throw new JsonException("A JSON number requires digits.");

            long integerValue = 0;
            var integerOverflow = false;
            if (Consume('0'))
            {
                if (IsDigit(PeekCharacter()))
                    throw new JsonException("JSON numbers cannot have leading zeroes.");
            }
            else
            {
                if (!IsDigitOneToNine(PeekCharacter()))
                    throw new JsonException("A JSON number requires digits.");
                while (IsDigit(PeekCharacter()))
                {
                    var digit = source[offset++] - '0';
                    if (!integerOverflow)
                    {
                        if (integerValue > (2_147_483_648 - digit) / 10)
                            integerOverflow = true;
                        else
                            integerValue = integerValue * 10 + digit;
                    }
                }
            }

            var hasFraction = false;
            if (Consume('.'))
            {
                hasFraction = true;
                if (!IsDigit(PeekCharacter()))
                    throw new JsonException("A JSON fraction requires digits.");
                while (IsDigit(PeekCharacter()))
                    offset++;
            }

            var hasExponent = false;
            if (Peek('e') || Peek('E'))
            {
                hasExponent = true;
                offset++;
                if (Peek('+') || Peek('-'))
                    offset++;
                if (!IsDigit(PeekCharacter()))
                    throw new JsonException("A JSON exponent requires digits.");
                while (IsDigit(PeekCharacter()))
                    offset++;
            }

            var number = source.AsSpan(start, offset - start);
            if (!hasFraction && !hasExponent && !integerOverflow)
            {
                var signedValue = negative ? -integerValue : integerValue;
                if (
                    signedValue >= int.MinValue
                    && signedValue <= int.MaxValue
                    && !(negative && signedValue == 0)
                )
                    return JsValue.FromInt32((int)signedValue);
            }

            if (
                !double.TryParse(
                    number,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value
                )
            )
                throw new JsonException("Invalid JSON number.");
            if (
                double.IsFinite(value)
                && value >= int.MinValue
                && value <= int.MaxValue
                && value == Math.Truncate(value)
                && !(negative && value == 0)
            )
                return JsValue.FromInt32((int)value);
            return new(value);
        }

        private string ParseString()
        {
            Expect('"');
            var start = offset;
            while (offset < source.Length)
            {
                var specialOffset = source.AsSpan(offset).IndexOfAny('"', '\\');
                if (specialOffset < 0)
                    throw new JsonException("Unterminated JSON string.");

                var special = offset + specialOffset;
                for (var i = offset; i < special; i++)
                    if (source[i] < ' ')
                        throw new JsonException("A JSON string cannot contain control characters.");

                if (source[special] == '"')
                {
                    offset = special + 1;
                    return source.Substring(start, special - start);
                }

                var builder = new StringBuilder(special - start + 4);
                builder.Append(source, start, special - start);
                offset = special;
                while (offset < source.Length)
                {
                    var c = source[offset++];
                    if (c == '"')
                        return builder.ToString();
                    if (c == '\\')
                    {
                        if (offset == source.Length)
                            throw new JsonException("Unterminated JSON escape.");
                        AppendEscape(builder, source[offset++]);
                        continue;
                    }

                    if (c < ' ')
                        throw new JsonException("A JSON string cannot contain control characters.");
                    builder.Append(c);
                }

                throw new JsonException("Unterminated JSON string.");
            }

            throw new JsonException("Unterminated JSON string.");
        }

        private void AppendEscape(StringBuilder builder, char escape)
        {
            switch (escape)
            {
                case '"':
                case '\\':
                case '/':
                    builder.Append(escape);
                    return;
                case 'b':
                    builder.Append('\b');
                    return;
                case 'f':
                    builder.Append('\f');
                    return;
                case 'n':
                    builder.Append('\n');
                    return;
                case 'r':
                    builder.Append('\r');
                    return;
                case 't':
                    builder.Append('\t');
                    return;
                case 'u':
                {
                    if (offset + 4 > source.Length)
                        throw new JsonException("Incomplete JSON unicode escape.");
                    var value = 0;
                    for (var i = 0; i < 4; i++)
                    {
                        var digit = HexValue(source[offset++]);
                        if (digit < 0)
                            throw new JsonException("Invalid JSON unicode escape.");
                        value = (value << 4) | digit;
                    }

                    builder.Append((char)value);
                    return;
                }
                default:
                    throw new JsonException("Invalid JSON escape.");
            }
        }

        private void EnterContainer()
        {
            if (++depth > MaxDepth)
            {
                depth--;
                throw new JsonException("The JSON payload exceeds the maximum depth.");
            }
        }

        private void SkipWhitespace()
        {
            while (offset < source.Length && source[offset] is ' ' or '\t' or '\r' or '\n')
                offset++;
        }

        private bool Consume(char expected)
        {
            if (!Peek(expected))
                return false;
            offset++;
            return true;
        }

        private void Expect(char expected)
        {
            if (!Consume(expected))
                throw new JsonException($"Expected JSON character '{expected}'.");
        }

        private bool ConsumeLiteral(string literal)
        {
            if (!source.AsSpan(offset).StartsWith(literal, StringComparison.Ordinal))
                return false;
            offset += literal.Length;
            return true;
        }

        private bool Peek(char expected) => offset < source.Length && source[offset] == expected;

        private char PeekCharacter() => offset < source.Length ? source[offset] : '\0';

        private static bool IsDigit(char c) => c is >= '0' and <= '9';

        private static bool IsDigitOneToNine(char c) => c is >= '1' and <= '9';

        private static int HexValue(char c)
        {
            return c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };
        }
    }

    private static JsValue ConvertJsonElement(
        JsRealm realm,
        JsonElement element,
        JsonConversionBuffers buffers
    )
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return JsValue.Null;
            case JsonValueKind.True:
                return JsValue.True;
            case JsonValueKind.False:
                return JsValue.False;
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var i32))
                {
                    var rawNumber = element.GetRawText();
                    if (!string.Equals(rawNumber, "-0", StringComparison.Ordinal))
                        return JsValue.FromInt32(i32);
                }

                return new(element.GetDouble());
            case JsonValueKind.String:
                return element.GetString() ?? string.Empty;
            case JsonValueKind.Array:
            {
                var array = realm.CreateArrayObject();
                var length = element.GetArrayLength();
                if (length != 0)
                {
                    var dense = array.InitializeDenseElementsNoCollision(length);
                    var idx = 0;
                    foreach (var item in element.EnumerateArray())
                        dense[idx++] = ConvertJsonElement(realm, item, buffers);
                }

                return array;
            }
            case JsonValueKind.Object:
            {
                var obj = new JsPlainObject(realm, useDictionaryMode: true);
                var staging = buffers.AcquireObjectStaging();
                var usedGenericNamedPath = false;
                try
                {
                    foreach (var prop in element.EnumerateObject())
                    {
                        var propValue = ConvertJsonElement(realm, prop.Value, buffers);
                        if (TryGetArrayIndexFromCanonicalString(prop.Name, out var index))
                        {
                            obj.SetElement(index, propValue);
                            continue;
                        }

                        var atom = realm.Atoms.InternNoCheck(prop.Name);
                        if (!usedGenericNamedPath && !staging.ContainsAtom(atom))
                        {
                            staging.Add(atom, propValue);
                            continue;
                        }

                        if (!usedGenericNamedPath && staging.Count != 0)
                        {
                            obj.InitializeDynamicOpenDataPropertiesNoCollision(
                                realm,
                                staging.Atoms,
                                staging.Values
                            );
                            usedGenericNamedPath = true;
                        }

                        obj.SetPropertyAtom(realm, atom, propValue, out _);
                    }

                    if (!usedGenericNamedPath && staging.Count != 0)
                        obj.InitializeDynamicOpenDataPropertiesNoCollision(
                            realm,
                            staging.Atoms,
                            staging.Values
                        );
                }
                finally
                {
                    staging.Release();
                }

                return obj;
            }
            default:
                return JsValue.Undefined;
        }
    }

    internal JsValue ConvertJsonElementForBenchmark(JsonElement element)
    {
        return ConvertJsonElement(Realm, element);
    }

    internal JsValue ParseJsonModuleSource(string source)
    {
        try
        {
            return ParseJsonText(Realm, source);
        }
        catch (JsonException ex)
        {
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                ex.Message,
                "MODULE_JSON_PARSE_FAILED",
                innerException: ex
            );
        }
    }

    private static JsValue InternalizeJsonProperty(
        JsRealm realm,
        JsObject holder,
        string key,
        JsonElement? sourceElement,
        JsFunction reviver
    )
    {
        if (!TryGetJsonPropertyByStringKey(realm, holder, key, out var value))
            value = JsValue.Undefined;

        if (value.TryGetObject(out var obj))
        {
            if (IsArrayObject(realm, obj))
            {
                var length = GetArrayLikeLengthLong(realm, obj);
                for (long i = 0; i < length; i++)
                {
                    var indexKey = i.ToString(CultureInfo.InvariantCulture);
                    var revived = InternalizeJsonProperty(
                        realm,
                        obj,
                        indexKey,
                        TryGetJsonArraySourceElement(sourceElement, i, out var childArrayElement)
                            ? childArrayElement
                            : null,
                        reviver
                    );
                    if (revived.IsUndefined)
                        DeleteJsonPropertyForReviver(realm, obj, indexKey);
                    else
                        CreateJsonDataPropertyForReviver(realm, obj, indexKey, revived);
                }
            }
            else
            {
                var keys = CollectEnumerableOwnStringKeysForJsonInternalize(realm, obj);
                for (var i = 0; i < keys.Length; i++)
                {
                    var propertyKey = keys[i];
                    var revived = InternalizeJsonProperty(
                        realm,
                        obj,
                        propertyKey,
                        TryGetJsonObjectSourceElement(
                            sourceElement,
                            propertyKey,
                            out var childObjectElement
                        )
                            ? childObjectElement
                            : null,
                        reviver
                    );
                    if (revived.IsUndefined)
                        DeleteJsonPropertyForReviver(realm, obj, propertyKey);
                    else
                        CreateJsonDataPropertyForReviver(realm, obj, propertyKey, revived);
                }
            }
        }

        var sourceContext = new JsPlainObject(realm);
        if (ShouldExposeJsonSourceContext(realm, value, sourceElement))
            sourceContext.DefineDataPropertyAtom(
                realm,
                IdSource,
                sourceElement!.Value.GetRawText(),
                JsShapePropertyFlags.Open
            );

        var args = new InlineJsValueArray3
        {
            Item0 = key,
            Item1 = value,
            Item2 = sourceContext,
        };
        return realm.InvokeFunction(reviver, holder, args.AsSpan());
    }

    private static string[] CollectEnumerableOwnStringKeysForJsonInternalize(
        JsRealm realm,
        JsObject target
    )
    {
        var indexNames = realm.RentScratchList<(uint Index, string Name)>(4);
        var stringNames = realm.RentScratchList<string>(8);
        var seenNames = realm.RentScratchHashSet<string>(comparer: StringComparer.Ordinal);
        var ownElementIndices = realm.RentScratchList<uint>(8);
        var enumerableNamedAtoms = realm.RentScratchList<int>(8);
        JsObject? proxyTarget = null;
        try
        {
            var isProxy =
                target.TryGetOwnKeysTrapKeys(realm, out var trapKeys)
                || target.TryGetProxyTarget(out proxyTarget);
            if (isProxy)
            {
                var proxyKeys = trapKeys ?? OwnKeysHelpers.CollectForProxy(realm, proxyTarget!);
                for (var i = 0; i < proxyKeys.Count; i++)
                {
                    var key = proxyKeys[i];
                    if (!key.IsString)
                        continue;
                    if (
                        !TryGetOwnEnumerableForJsonInternalize(
                            realm,
                            target,
                            key,
                            out var enumerable
                        ) || !enumerable
                    )
                        continue;

                    var name = key.AsString();
                    if (!seenNames.Add(name))
                        continue;

                    if (TryGetArrayIndexFromCanonicalString(name, out var index))
                        indexNames.Add((index, name));
                    else
                        stringNames.Add(name);
                }
            }
            else
            {
                target.CollectOwnElementIndices(ownElementIndices, true);
                for (var i = 0; i < ownElementIndices.Count; i++)
                {
                    var key = ownElementIndices[i].ToString(CultureInfo.InvariantCulture);
                    if (seenNames.Add(key))
                        indexNames.Add((ownElementIndices[i], key));
                }

                target.CollectOwnNamedPropertyAtoms(realm, enumerableNamedAtoms, true);
                for (var i = 0; i < enumerableNamedAtoms.Count; i++)
                {
                    var atom = enumerableNamedAtoms[i];
                    if (atom < 0)
                        continue;

                    var name = realm.Atoms.AtomToString(atom);
                    if (!seenNames.Add(name))
                        continue;
                    if (TryGetArrayIndexFromCanonicalString(name, out var index))
                        indexNames.Add((index, name));
                    else
                        stringNames.Add(name);
                }
            }

            indexNames.Sort(static (left, right) => left.Index.CompareTo(right.Index));
            var result = new string[indexNames.Count + stringNames.Count];
            var offset = 0;
            for (var i = 0; i < indexNames.Count; i++)
                result[offset++] = indexNames[i].Name;
            for (var i = 0; i < stringNames.Count; i++)
                result[offset++] = stringNames[i];
            return result;
        }
        finally
        {
            realm.ReturnScratchList(enumerableNamedAtoms);
            realm.ReturnScratchList(ownElementIndices);
            realm.ReturnScratchHashSet(seenNames);
            realm.ReturnScratchList(stringNames);
            realm.ReturnScratchList(indexNames);
        }
    }

    private static bool TryGetOwnEnumerableForJsonInternalize(
        JsRealm realm,
        JsObject source,
        in JsValue key,
        out bool enumerable
    )
    {
        var trapUsed = source.TryGetOwnEnumerableDescriptorViaTrap(
            realm,
            key,
            out var trapHasDescriptor,
            out var trapEnumerable
        );
        if (trapUsed)
        {
            enumerable = trapHasDescriptor && trapEnumerable;
            return trapHasDescriptor;
        }

        if (source.TryGetProxyTarget(out var proxyTarget))
            return TryGetOwnEnumerableForJsonInternalize(realm, proxyTarget, key, out enumerable);

        var isIndex = realm.TryResolvePropertyKey(key, out var index, out var atom);
        if (isIndex && source.TryGetOwnElementDescriptor(index, out var elementDescriptor))
        {
            enumerable = elementDescriptor.Enumerable;
            return true;
        }

        if (source.TryGetOwnNamedPropertyDescriptorAtom(realm, atom, out var namedDescriptor))
        {
            enumerable = namedDescriptor.Enumerable;
            return true;
        }

        if (
            source is JsGlobalObject globalObject
            && globalObject.TryGetNamedGlobalDescriptorAtom(atom, out var globalDescriptor)
        )
        {
            enumerable = globalDescriptor.Enumerable;
            return true;
        }

        enumerable = false;
        return false;
    }

    private static bool TryGetJsonObjectSourceElement(
        JsonElement? parent,
        string key,
        out JsonElement child
    )
    {
        if (!parent.HasValue || parent.Value.ValueKind != JsonValueKind.Object)
        {
            child = default;
            return false;
        }

        var found = false;
        child = default;
        foreach (var property in parent.Value.EnumerateObject())
        {
            if (!string.Equals(property.Name, key, StringComparison.Ordinal))
                continue;
            child = property.Value;
            found = true;
        }

        return found;
    }

    private static bool TryGetJsonArraySourceElement(
        JsonElement? parent,
        long index,
        out JsonElement child
    )
    {
        if (!parent.HasValue || parent.Value.ValueKind != JsonValueKind.Array || index < 0)
        {
            child = default;
            return false;
        }

        long current = 0;
        foreach (var item in parent.Value.EnumerateArray())
        {
            if (current == index)
            {
                child = item;
                return true;
            }

            current++;
        }

        child = default;
        return false;
    }

    private static bool ShouldExposeJsonSourceContext(
        JsRealm realm,
        in JsValue currentValue,
        JsonElement? sourceElement
    )
    {
        if (
            !sourceElement.HasValue
            || sourceElement.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
        )
            return false;

        var originalValue = ConvertJsonElement(realm, sourceElement.Value);
        return JsValue.SameValue(currentValue, originalValue);
    }

    private static void DeleteJsonPropertyForReviver(JsRealm realm, JsObject target, string key)
    {
        if (TryGetArrayIndexFromCanonicalString(key, out var index))
        {
            _ = target.DeleteElement(index);
            return;
        }

        var atom = realm.Atoms.InternNoCheck(key);
        _ = target.DeletePropertyAtom(realm, atom);
    }

    private static void CreateJsonDataPropertyForReviver(
        JsRealm realm,
        JsObject target,
        string key,
        JsValue value
    )
    {
        var descriptor = new JsPlainObject(realm);
        descriptor.DefineNewPropertiesNoCollision(
            realm,
            [
                PropertyDefinition.OpenData(IdValue, value),
                PropertyDefinition.OpenData(IdWritable, JsValue.True),
                PropertyDefinition.OpenData(IdEnumerable, JsValue.True),
                PropertyDefinition.OpenData(IdConfigurable, JsValue.True),
            ]
        );

        Span<JsValue> defineArgs =
        [
            JsValue.FromObject(target),
            JsValue.FromString(key),
            JsValue.FromObject(descriptor),
        ];

        try
        {
            _ = realm.InvokeObjectConstructorMethod("defineProperty", defineArgs);
        }
        catch (JsRuntimeException ex) when (ex.Kind == JsErrorKind.TypeError) { }
    }

    private static string? JsonStringifyTopLevel(
        JsRealm realm,
        in JsValue value,
        JsFunction? replacer,
        string[]? propertyList,
        string gap,
        HashSet<JsObject> visited
    )
    {
        var holder = new JsPlainObject(realm);
        holder.DefineDataProperty(string.Empty, value, JsShapePropertyFlags.Open);
        return JsonStringifyProperty(
            realm,
            holder,
            string.Empty,
            value,
            replacer,
            propertyList,
            gap,
            visited,
            string.Empty
        );
    }

    private static string? JsonStringifyProperty(
        JsRealm realm,
        JsObject holder,
        string keyString,
        in JsValue inputValue,
        JsFunction? replacer,
        string[]? propertyList,
        string gap,
        HashSet<JsObject> visited,
        string currentIndent
    )
    {
        var value = ApplyJsonToJson(realm, keyString, inputValue);
        if (replacer is not null)
        {
            var args = new InlineJsValueArray2 { Item0 = keyString, Item1 = value };
            value = realm.InvokeFunction(replacer, holder, args.AsSpan());
        }

        return JsonStringifyCore(realm, value, replacer, propertyList, gap, visited, currentIndent);
    }

    private static JsValue ApplyJsonToJson(JsRealm realm, string keyString, in JsValue inputValue)
    {
        var value = inputValue;
        var receiver = value;
        JsFunction? toJson = null;
        const int atomToJson = IdToJson;

        if (value.TryGetObject(out var objectValue))
        {
            if (
                objectValue.TryGetPropertyAtom(realm, atomToJson, out var toJsonValue, out _)
                && toJsonValue.TryGetObject(out var toJsonObject)
                && toJsonObject is JsFunction toJsonFunction
            )
                toJson = toJsonFunction;
        }
        else if (
            value.IsBigInt
            && TryGetJsonMethodOnBigIntPrimitive(realm, value, atomToJson, out var primitiveToJson)
        )
        {
            toJson = primitiveToJson;
        }

        if (toJson is null)
            return value;

        var args = new InlineJsValueArray1 { Item0 = keyString };
        return realm.InvokeFunction(toJson, receiver, args.AsSpan());
    }

    private static bool TryGetJsonMethodOnBigIntPrimitive(
        JsRealm realm,
        in JsValue receiver,
        int atomToJson,
        out JsFunction function
    )
    {
        for (
            JsObject? current = realm.Intrinsics.BigIntPrototype;
            current is not null;
            current = current.Prototype
        )
            if (current.TryGetOwnNamedPropertyDescriptorAtom(realm, atomToJson, out var descriptor))
            {
                if (descriptor.IsAccessor)
                {
                    if (descriptor.Getter is null)
                        break;
                    var getterResult = realm.InvokeFunction(
                        descriptor.Getter,
                        receiver,
                        ReadOnlySpan<JsValue>.Empty
                    );
                    if (
                        getterResult.TryGetObject(out var getterObject)
                        && getterObject is JsFunction getterFunction
                    )
                    {
                        function = getterFunction;
                        return true;
                    }

                    break;
                }

                if (
                    descriptor.Value.TryGetObject(out var functionObject)
                    && functionObject is JsFunction directFunction
                )
                {
                    function = directFunction;
                    return true;
                }

                break;
            }

        function = null!;
        return false;
    }

    private static string? JsonStringifyCore(
        JsRealm realm,
        in JsValue value,
        JsFunction? replacer,
        string[]? propertyList,
        string gap,
        HashSet<JsObject> visited,
        string currentIndent
    )
    {
        if (value.IsUndefined || value.IsSymbol)
            return null;
        if (value.IsBigInt)
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Do not know how to serialize a BigInt"
            );
        if (value.IsNull)
            return "null";
        if (value.IsTrue)
            return "true";
        if (value.IsFalse)
            return "false";
        if (value.IsString)
            return JsonStringEncoding.Escape(value.AsString());
        if (value.IsNumber)
        {
            var n = value.NumberValue;
            if (double.IsNaN(n) || double.IsInfinity(n))
                return "null";
            if (n == 0d)
                return "0";
            return n.ToString(CultureInfo.InvariantCulture);
        }

        if (!value.TryGetObject(out var obj))
            return JsonStringEncoding.Escape(value.ToString() ?? string.Empty);

        if (obj is JsFunction)
            return null;
        if (obj is JsNumberObject)
            return JsonStringifyCore(
                realm,
                new(realm.ToNumberSlowPath(value)),
                replacer,
                propertyList,
                gap,
                visited,
                currentIndent
            );
        if (obj is JsStringObject)
            return JsonStringifyCore(
                realm,
                JsValue.FromString(realm.ToJsStringSlowPath(value)),
                replacer,
                propertyList,
                gap,
                visited,
                currentIndent
            );
        if (obj is JsBooleanObject boxedBoolean)
            return JsonStringifyCore(
                realm,
                boxedBoolean.Value ? JsValue.True : JsValue.False,
                replacer,
                propertyList,
                gap,
                visited,
                currentIndent
            );
        if (obj is JsBigIntObject)
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Do not know how to serialize a BigInt"
            );

        if (obj is JsRawJsonObject rawJsonObject)
            return rawJsonObject.RawJson;

        if (!visited.Add(obj))
            throw new JsRuntimeException(
                JsErrorKind.TypeError,
                "Converting circular structure to JSON"
            );

        try
        {
            var nextIndent = gap.Length == 0 ? string.Empty : currentIndent + gap;
            if (IsArrayObject(realm, obj))
            {
                var length = GetArrayLikeLengthLong(realm, obj);
                using var parts = new PooledList<string>((int)Math.Min(length, int.MaxValue));
                for (long i = 0; i < length; i++)
                {
                    if (
                        !TryGetJsonPropertyByStringKey(
                            realm,
                            obj,
                            i.ToString(CultureInfo.InvariantCulture),
                            out var elementValue
                        )
                    )
                        elementValue = JsValue.Undefined;

                    var part = JsonStringifyProperty(
                        realm,
                        obj,
                        i.ToString(CultureInfo.InvariantCulture),
                        elementValue,
                        replacer,
                        propertyList,
                        gap,
                        visited,
                        nextIndent
                    );
                    parts.Add(part ?? "null");
                }

                return BuildJsonArray(parts.AsSpan(), gap, currentIndent, nextIndent);
            }

            using var members = new PooledList<string>(8);
            if (propertyList is not null)
            {
                foreach (var key in propertyList)
                {
                    if (!TryGetJsonPropertyByStringKey(realm, obj, key, out var propertyValue))
                        propertyValue = JsValue.Undefined;
                    var serialized = JsonStringifyProperty(
                        realm,
                        obj,
                        key,
                        propertyValue,
                        replacer,
                        propertyList,
                        gap,
                        visited,
                        nextIndent
                    );
                    if (serialized is null)
                        continue;
                    members.Add(BuildJsonMember(key, serialized, gap));
                }
            }
            else
            {
                var keys = CollectEnumerableOwnStringKeysForJsonInternalize(realm, obj);
                for (var i = 0; i < keys.Length; i++)
                {
                    var key = keys[i];
                    if (!TryGetJsonPropertyByStringKey(realm, obj, key, out var propertyValue))
                        propertyValue = JsValue.Undefined;
                    var serialized = JsonStringifyProperty(
                        realm,
                        obj,
                        key,
                        propertyValue,
                        replacer,
                        propertyList,
                        gap,
                        visited,
                        nextIndent
                    );
                    if (serialized is null)
                        continue;
                    members.Add(BuildJsonMember(key, serialized, gap));
                }
            }

            return BuildJsonObject(members.AsSpan(), gap, currentIndent, nextIndent);
        }
        finally
        {
            visited.Remove(obj);
        }
    }

    private static void AppendCommaSeparated(StringBuilder builder, ReadOnlySpan<string> parts)
    {
        if (parts.IsEmpty)
            return;

        builder.Append(parts[0]);
        for (var i = 1; i < parts.Length; i++)
        {
            builder.Append(',');
            builder.Append(parts[i]);
        }
    }

    private static int EstimateDelimitedLength(ReadOnlySpan<string> parts, int wrapperLength)
    {
        var totalLength = wrapperLength;
        if (parts.IsEmpty)
            return totalLength;

        totalLength += parts.Length - 1;
        for (var i = 0; i < parts.Length; i++)
            totalLength += parts[i].Length;
        return totalLength;
    }

    private static string[] JsonStringifyBuildPropertyList(JsRealm realm, JsObject replacerObj)
    {
        var length = GetArrayLikeLengthLong(realm, replacerObj);
        if (length <= 0)
            return [];

        var list = new List<string>((int)Math.Min(length, 16));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (long i = 0; i < length; i++)
        {
            if (!TryGetArrayLikeIndex(realm, replacerObj, i, out var item))
                continue;

            string? propertyName = null;
            if (item.IsString)
            {
                propertyName = item.AsString();
            }
            else if (item.IsNumber)
            {
                propertyName = realm.ToJsStringSlowPath(item);
            }
            else if (item.TryGetObject(out var itemObj))
            {
                if (itemObj is JsStringObject)
                    propertyName = realm.ToJsStringSlowPath(item);
                else if (itemObj is JsNumberObject)
                    propertyName = realm.ToJsStringSlowPath(item);
            }

            if (propertyName is null || !seen.Add(propertyName))
                continue;
            list.Add(propertyName);
        }

        return list.ToArray();
    }

    private static string JsonStringifyComputeGap(JsRealm realm, in JsValue space)
    {
        var normalized = space;
        if (space.TryGetObject(out var obj))
        {
            if (obj is JsNumberObject)
                normalized = new(realm.ToNumberSlowPath(normalized));
            else if (obj is JsStringObject)
                normalized = JsValue.FromString(realm.ToJsStringSlowPath(normalized));
        }

        if (normalized.IsNumber)
        {
            var count = (int)Math.Clamp(realm.ToIntegerOrInfinity(normalized), 0d, 10d);
            return count == 0 ? string.Empty : new(' ', count);
        }

        if (normalized.IsString)
        {
            var text = normalized.AsString();
            return text.Length <= 10 ? text : text[..10];
        }

        return string.Empty;
    }

    private static bool TryGetJsonPropertyByStringKey(
        JsRealm realm,
        JsObject obj,
        string key,
        out JsValue value
    )
    {
        if (key.Length == 0)
        {
            if (obj.TryGetOwnNamedPropertyDescriptorAtom(realm, IdEmpty, out var emptyDescriptor))
            {
                value = emptyDescriptor.Value;
                return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        if (TryGetArrayIndexFromCanonicalString(key, out var index))
            return obj.TryGetElement(index, out value);

        var atom = realm.Atoms.InternNoCheck(key);
        return obj.TryGetPropertyAtom(realm, atom, out value, out _);
    }

    private static string BuildJsonMember(string key, string serializedValue, string gap)
    {
        if (gap.Length == 0)
            return JsonStringEncoding.Escape(key) + ":" + serializedValue;
        return JsonStringEncoding.Escape(key) + ": " + serializedValue;
    }

    private static string BuildJsonArray(
        ReadOnlySpan<string> parts,
        string gap,
        string currentIndent,
        string nextIndent
    )
    {
        if (parts.IsEmpty)
            return "[]";
        if (gap.Length == 0)
        {
            var builder = new StringBuilder(EstimateDelimitedLength(parts, 2));
            builder.Append('[');
            AppendCommaSeparated(builder, parts);
            builder.Append(']');
            return builder.ToString();
        }

        var builderIndented = new StringBuilder();
        builderIndented.Append('[');
        builderIndented.Append('\n');
        for (var i = 0; i < parts.Length; i++)
        {
            builderIndented.Append(nextIndent);
            builderIndented.Append(parts[i]);
            if (i + 1 < parts.Length)
                builderIndented.Append(',');
            builderIndented.Append('\n');
        }

        builderIndented.Append(currentIndent);
        builderIndented.Append(']');
        return builderIndented.ToString();
    }

    private static string BuildJsonObject(
        ReadOnlySpan<string> members,
        string gap,
        string currentIndent,
        string nextIndent
    )
    {
        if (members.IsEmpty)
            return "{}";
        if (gap.Length == 0)
        {
            var builder = new StringBuilder(EstimateDelimitedLength(members, 2));
            builder.Append('{');
            AppendCommaSeparated(builder, members);
            builder.Append('}');
            return builder.ToString();
        }

        var builderIndented = new StringBuilder();
        builderIndented.Append('{');
        builderIndented.Append('\n');
        for (var i = 0; i < members.Length; i++)
        {
            builderIndented.Append(nextIndent);
            builderIndented.Append(members[i]);
            if (i + 1 < members.Length)
                builderIndented.Append(',');
            builderIndented.Append('\n');
        }

        builderIndented.Append(currentIndent);
        builderIndented.Append('}');
        return builderIndented.ToString();
    }

    private static void ValidateRawJsonText(string text)
    {
        if (
            text.Length == 0
            || IsJsonRawBoundaryWhitespace(text[0])
            || IsJsonRawBoundaryWhitespace(text[^1])
        )
            throw new JsRuntimeException(JsErrorKind.SyntaxError, "Invalid raw JSON text");

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                throw new JsRuntimeException(JsErrorKind.SyntaxError, "Invalid raw JSON text");
        }
        catch (JsonException ex)
        {
            throw new JsRuntimeException(JsErrorKind.SyntaxError, ex.Message, innerException: ex);
        }
    }

    private static bool IsJsonRawBoundaryWhitespace(char c)
    {
        return c is '\t' or '\n' or '\r' or ' ';
    }

    private sealed class JsonConversionBuffers : IDisposable
    {
        private int[] atoms = Array.Empty<int>();
        private int count;
        private JsValue[] values = Array.Empty<JsValue>();

        public void Dispose()
        {
            if (atoms.Length != 0)
                ArrayPool<int>.Shared.Return(atoms);
            if (values.Length != 0)
            {
                ClearValueRange(0, count);
                ArrayPool<JsValue>.Shared.Return(values);
            }

            atoms = Array.Empty<int>();
            values = Array.Empty<JsValue>();
            count = 0;
        }

        public ObjectStaging AcquireObjectStaging(int minimumCapacity = 4)
        {
            EnsureCapacity(count + minimumCapacity);
            return new(this, count);
        }

        private void EnsureCapacity(int minimumCapacity)
        {
            if (atoms.Length >= minimumCapacity)
                return;

            int newCapacity;
            if (atoms.Length == 0)
            {
                newCapacity = minimumCapacity <= 8 ? 8 : minimumCapacity;
            }
            else
            {
                newCapacity = atoms.Length + Math.Max(atoms.Length >> 1, 8);
                if (newCapacity < minimumCapacity)
                    newCapacity = minimumCapacity;
            }

            var newAtoms = ArrayPool<int>.Shared.Rent(newCapacity);
            var newValues = ArrayPool<JsValue>.Shared.Rent(newCapacity);
            if (count != 0)
            {
                Array.Copy(atoms, newAtoms, count);
                Array.Copy(values, newValues, count);
            }

            if (atoms.Length != 0)
                ArrayPool<int>.Shared.Return(atoms);
            if (values.Length != 0)
            {
                ClearValueRange(0, count);
                ArrayPool<JsValue>.Shared.Return(values);
            }

            atoms = newAtoms;
            values = newValues;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearValueRange(int start, int length)
        {
            if (length > 0)
                Array.Clear(values, start, length);
        }

        internal struct ObjectStaging(JsonConversionBuffers owner, int start)
        {
            public int Count => owner.count - start;
            public ReadOnlySpan<int> Atoms => owner.atoms.AsSpan(start, Count);
            public ReadOnlySpan<JsValue> Values => owner.values.AsSpan(start, Count);

            public bool ContainsAtom(int atom)
            {
                for (var i = start; i < owner.count; i++)
                    if (owner.atoms[i] == atom)
                        return true;

                return false;
            }

            public void Add(int atom, in JsValue value)
            {
                owner.EnsureCapacity(owner.count + 1);
                owner.atoms[owner.count] = atom;
                owner.values[owner.count] = value;
                owner.count++;
            }

            public void Release()
            {
                owner.ClearValueRange(start, owner.count - start);
                owner.count = start;
            }
        }
    }
}
