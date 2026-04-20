namespace Okojo.Runtime;

public partial class Intrinsics
{
    private static string ToUint8ArrayBase64(JsRealm realm, JsTypedArrayObject target, string alphabet,
        bool omitPadding)
    {
        var base64Url = alphabet switch
        {
            "base64" => false,
            "base64url" => true,
            _ => throw new JsRuntimeException(JsErrorKind.TypeError,
                "Uint8Array.prototype.toBase64 alphabet must be 'base64' or 'base64url'")
        };

        var bytes = ReadUint8ArrayBytes(target);
        var result = Convert.ToBase64String(bytes);
        if (base64Url)
            result = result.Replace('+', '-').Replace('/', '_');
        if (omitPadding)
            result = result.TrimEnd('=');
        return result;
    }

    private static string ToUint8ArrayHex(JsTypedArrayObject target)
    {
        var bytes = ReadUint8ArrayBytes(target);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static byte[] ReadUint8ArrayBytes(JsTypedArrayObject target)
    {
        var length = (int)target.Length;
        if (length == 0)
            return Array.Empty<byte>();

        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
            bytes[i] = target.TryGetElement((uint)i, out var value) ? (byte)value.Int32Value : (byte)0;
        return bytes;
    }

    private static JsPlainObject CreateBase64SetResultObject(JsRealm realm, int read, int written)
    {
        var result = new JsPlainObject(realm);
        result.DefineDataPropertyAtom(realm, IdRead, JsValue.FromInt32(read), JsShapePropertyFlags.Open);
        result.DefineDataPropertyAtom(realm, IdWritten, JsValue.FromInt32(written),
            JsShapePropertyFlags.Open);
        return result;
    }

    private static (int Read, int Written) SetUint8ArrayFromBase64(
        JsRealm realm,
        JsTypedArrayObject target,
        string source,
        string alphabet,
        Base64LastChunkHandling lastChunkHandling)
    {
        var base64Url = alphabet switch
        {
            "base64" => false,
            "base64url" => true,
            _ => throw new JsRuntimeException(JsErrorKind.TypeError,
                "Uint8Array.fromBase64 alphabet must be 'base64' or 'base64url'")
        };

        var targetLength = (int)target.Length;
        if (targetLength == 0)
            return (0, 0);

        var compact = new char[source.Length];
        var originalOffsets = new int[source.Length];
        var compactLength = 0;
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (IsAsciiBase64Whitespace(c))
                continue;
            compact[compactLength] = c;
            originalOffsets[compactLength] = i + 1;
            compactLength++;
        }

        var compactIndex = 0;
        var written = 0;
        while (compactIndex < compactLength)
        {
            var remaining = compactLength - compactIndex;
            if (remaining >= 4)
            {
                var a = compact[compactIndex];
                var b = compact[compactIndex + 1];
                var c = compact[compactIndex + 2];
                var d = compact[compactIndex + 3];

                if (c == '=' || d == '=')
                {
                    if (compactIndex + 4 != compactLength)
                        throw InvalidBase64Syntax();

                    var produced = CountFinalPaddedBase64Bytes(a, b, c, d, base64Url, lastChunkHandling);
                    if (written + produced > targetLength)
                        return (GetBase64ReadCountBeforeChunk(originalOffsets, compactIndex), written);

                    WriteFinalPaddedBase64Bytes(realm, target, written, a, b, c, d, base64Url, lastChunkHandling);
                    written += produced;
                    compactIndex += 4;
                    break;
                }

                if (written + 3 > targetLength)
                {
                    ValidateBase64Triple(a, b, c, d, base64Url);
                    return (GetBase64ReadCountBeforeChunk(originalOffsets, compactIndex), written);
                }

                WriteBase64Triple(realm, target, written, a, b, c, d, base64Url);
                written += 3;
                compactIndex += 4;
                if (written == targetLength)
                    return (GetBase64ReadCountBeforeChunk(originalOffsets, compactIndex), written);
                continue;
            }

            if (lastChunkHandling == Base64LastChunkHandling.StopBeforePartial)
            {
                ValidateStopBeforePartialTail(compact, compactIndex, remaining, base64Url);
                return (GetBase64ReadCountBeforeChunk(originalOffsets, compactIndex), written);
            }

            var producedPartial =
                CountFinalUnpaddedBase64Bytes(compact, compactIndex, remaining, base64Url, lastChunkHandling);
            if (written + producedPartial > targetLength)
                return (GetBase64ReadCountBeforeChunk(originalOffsets, compactIndex), written);

            WriteFinalUnpaddedBase64Bytes(realm, target, written, compact, compactIndex, remaining, base64Url,
                lastChunkHandling);
            written += producedPartial;
            compactIndex = compactLength;
            break;
        }

        return (source.Length, written);
    }

    private static void ValidateStopBeforePartialTail(char[] compact, int compactIndex, int remaining, bool base64Url)
    {
        if (remaining == 2)
        {
            if (DecodeBase64Sextet(compact[compactIndex], base64Url) < 0 ||
                DecodeBase64Sextet(compact[compactIndex + 1], base64Url) < 0)
                throw InvalidBase64Syntax();
            return;
        }

        if (remaining == 3)
        {
            var a = compact[compactIndex];
            var b = compact[compactIndex + 1];
            var c = compact[compactIndex + 2];
            if (DecodeBase64Sextet(a, base64Url) < 0 || DecodeBase64Sextet(b, base64Url) < 0)
                throw InvalidBase64Syntax();
            if (c == '=')
            {
                if (((DecodeBase64Sextet(a, base64Url) << 2) | (DecodeBase64Sextet(b, base64Url) >> 4)) < 0)
                    throw InvalidBase64Syntax();
                return;
            }

            if (DecodeBase64Sextet(c, base64Url) < 0)
                throw InvalidBase64Syntax();
            return;
        }

        for (var i = 0; i < remaining; i++)
        {
            var c = compact[compactIndex + i];
            if (c == '=' || DecodeBase64Sextet(c, base64Url) < 0)
                throw InvalidBase64Syntax();
        }
    }

    private static (int Read, int Written) SetUint8ArrayFromHex(JsTypedArrayObject target, string source)
    {
        if ((source.Length & 1) != 0)
            throw new JsRuntimeException(JsErrorKind.SyntaxError, "Invalid hex string");

        var targetLength = (int)target.Length;
        var written = 0;
        for (var i = 0; i < source.Length; i += 2)
        {
            if (written == targetLength)
                return (i, written);

            var high = HexNibble(source[i]);
            var low = HexNibble(source[i + 1]);
            if (high < 0 || low < 0)
                throw new JsRuntimeException(JsErrorKind.SyntaxError, "Invalid hex string");

            target.SetElement((uint)written, JsValue.FromInt32((high << 4) | low));
            written++;
        }

        return (source.Length, written);
    }

    private static int GetBase64ReadCountBeforeChunk(int[] originalOffsets, int compactIndex)
    {
        return compactIndex == 0 ? 0 : originalOffsets[compactIndex - 1];
    }

    private static void WriteBase64Triple(JsRealm realm, JsTypedArrayObject target, int written, char a, char b,
        char c, char d, bool base64Url)
    {
        var sa = DecodeBase64Sextet(a, base64Url);
        var sb = DecodeBase64Sextet(b, base64Url);
        var sc = DecodeBase64Sextet(c, base64Url);
        var sd = DecodeBase64Sextet(d, base64Url);
        if ((sa | sb | sc | sd) < 0)
            throw InvalidBase64Syntax();

        target.SetElement((uint)written, JsValue.FromInt32((sa << 2) | (sb >> 4)));
        target.SetElement((uint)(written + 1), JsValue.FromInt32(((sb & 0x0F) << 4) | (sc >> 2)));
        target.SetElement((uint)(written + 2), JsValue.FromInt32(((sc & 0x03) << 6) | sd));
    }

    private static void ValidateBase64Triple(char a, char b, char c, char d, bool base64Url)
    {
        var sa = DecodeBase64Sextet(a, base64Url);
        var sb = DecodeBase64Sextet(b, base64Url);
        var sc = DecodeBase64Sextet(c, base64Url);
        var sd = DecodeBase64Sextet(d, base64Url);
        if ((sa | sb | sc | sd) < 0)
            throw InvalidBase64Syntax();
    }

    private static int CountFinalPaddedBase64Bytes(char a, char b, char c, char d, bool base64Url,
        Base64LastChunkHandling lastChunkHandling)
    {
        var sa = DecodeBase64Sextet(a, base64Url);
        var sb = DecodeBase64Sextet(b, base64Url);
        if ((sa | sb) < 0)
            throw InvalidBase64Syntax();

        if (c == '=')
        {
            if (d != '=')
                throw InvalidBase64Syntax();
            if (lastChunkHandling == Base64LastChunkHandling.Strict && (sb & 0x0F) != 0)
                throw InvalidBase64Syntax();
            return 1;
        }

        if (d == '=')
        {
            var sc = DecodeBase64Sextet(c, base64Url);
            if (sc < 0)
                throw InvalidBase64Syntax();
            if (lastChunkHandling == Base64LastChunkHandling.Strict && (sc & 0x03) != 0)
                throw InvalidBase64Syntax();
            return 2;
        }

        var sc2 = DecodeBase64Sextet(c, base64Url);
        var sd = DecodeBase64Sextet(d, base64Url);
        if ((sc2 | sd) < 0)
            throw InvalidBase64Syntax();
        return 3;
    }

    private static void WriteFinalPaddedBase64Bytes(JsRealm realm, JsTypedArrayObject target, int written, char a,
        char b, char c, char d, bool base64Url, Base64LastChunkHandling lastChunkHandling)
    {
        var sa = DecodeBase64Sextet(a, base64Url);
        var sb = DecodeBase64Sextet(b, base64Url);
        if ((sa | sb) < 0)
            throw InvalidBase64Syntax();

        target.SetElement((uint)written, JsValue.FromInt32((sa << 2) | (sb >> 4)));
        if (c == '=')
        {
            if (d != '=')
                throw InvalidBase64Syntax();
            if (lastChunkHandling == Base64LastChunkHandling.Strict && (sb & 0x0F) != 0)
                throw InvalidBase64Syntax();
            return;
        }

        var sc = DecodeBase64Sextet(c, base64Url);
        if (sc < 0)
            throw InvalidBase64Syntax();
        target.SetElement((uint)(written + 1), JsValue.FromInt32(((sb & 0x0F) << 4) | (sc >> 2)));
        if (d == '=')
        {
            if (lastChunkHandling == Base64LastChunkHandling.Strict && (sc & 0x03) != 0)
                throw InvalidBase64Syntax();
            return;
        }

        var sd = DecodeBase64Sextet(d, base64Url);
        if (sd < 0)
            throw InvalidBase64Syntax();
        target.SetElement((uint)(written + 2), JsValue.FromInt32(((sc & 0x03) << 6) | sd));
    }

    private static int CountFinalUnpaddedBase64Bytes(char[] compact, int compactIndex, int remaining, bool base64Url,
        Base64LastChunkHandling lastChunkHandling)
    {
        return remaining switch
        {
            1 => throw InvalidBase64Syntax(),
            2 => lastChunkHandling == Base64LastChunkHandling.Strict
                ? throw InvalidBase64Syntax()
                : ValidateFinalUnpaddedTwo(compact[compactIndex], compact[compactIndex + 1], base64Url),
            3 => lastChunkHandling == Base64LastChunkHandling.Strict
                ? throw InvalidBase64Syntax()
                : ValidateFinalUnpaddedThree(compact[compactIndex], compact[compactIndex + 1],
                    compact[compactIndex + 2], base64Url),
            _ => throw InvalidBase64Syntax()
        };
    }

    private static void WriteFinalUnpaddedBase64Bytes(JsRealm realm, JsTypedArrayObject target, int written,
        char[] compact, int compactIndex, int remaining, bool base64Url, Base64LastChunkHandling lastChunkHandling)
    {
        switch (remaining)
        {
            case 2:
                {
                    var sa = DecodeBase64Sextet(compact[compactIndex], base64Url);
                    var sb = DecodeBase64Sextet(compact[compactIndex + 1], base64Url);
                    if ((sa | sb) < 0)
                        throw InvalidBase64Syntax();
                    target.SetElement((uint)written, JsValue.FromInt32((sa << 2) | (sb >> 4)));
                    return;
                }
            case 3:
                {
                    var sa = DecodeBase64Sextet(compact[compactIndex], base64Url);
                    var sb = DecodeBase64Sextet(compact[compactIndex + 1], base64Url);
                    var sc = DecodeBase64Sextet(compact[compactIndex + 2], base64Url);
                    if ((sa | sb | sc) < 0)
                        throw InvalidBase64Syntax();
                    target.SetElement((uint)written, JsValue.FromInt32((sa << 2) | (sb >> 4)));
                    target.SetElement((uint)(written + 1), JsValue.FromInt32(((sb & 0x0F) << 4) | (sc >> 2)));
                    return;
                }
            default:
                throw InvalidBase64Syntax();
        }
    }

    private static int ValidateFinalUnpaddedTwo(char a, char b, bool base64Url)
    {
        var sa = DecodeBase64Sextet(a, base64Url);
        var sb = DecodeBase64Sextet(b, base64Url);
        if ((sa | sb) < 0)
            throw InvalidBase64Syntax();
        return 1;
    }

    private static int ValidateFinalUnpaddedThree(char a, char b, char c, bool base64Url)
    {
        var sa = DecodeBase64Sextet(a, base64Url);
        var sb = DecodeBase64Sextet(b, base64Url);
        var sc = DecodeBase64Sextet(c, base64Url);
        if ((sa | sb | sc) < 0)
            throw InvalidBase64Syntax();
        return 2;
    }
}
