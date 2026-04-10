using Okojo.Internals;

namespace Okojo.SourceMaps;

internal static class Base64Vlq
{
    private const string Digits = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    public static int[] Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
            return [];

        using var values = new PooledArrayBuilder<int>(stackalloc int[16]);
        var value = 0;
        var shift = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var digit = Digits.IndexOf(text[i]);
            if (digit < 0)
                throw new FormatException($"Invalid base64 VLQ digit '{text[i]}'.");

            var hasContinuation = (digit & 32) != 0;
            digit &= 31;
            value += digit << shift;

            if (hasContinuation)
            {
                shift += 5;
                continue;
            }

            var isNegative = (value & 1) != 0;
            var decoded = value >> 1;
            values.Add(isNegative ? -decoded : decoded);
            value = 0;
            shift = 0;
        }

        if (shift != 0)
            throw new FormatException("Invalid base64 VLQ sequence.");

        return values.ToArray();
    }
}
