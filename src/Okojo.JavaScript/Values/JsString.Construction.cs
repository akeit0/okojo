namespace Okojo.Values;

public readonly partial struct JsString
{
    public static JsString Repeat(JsString value, long count)
    {
        if (count <= 0 || value.Length == 0)
            return Empty;

        var result = Empty;
        var current = value;
        var remaining = count;

        while (remaining > 0)
        {
            if ((remaining & 1) != 0)
                result = Concat(result, current);

            remaining >>= 1;
            if (remaining != 0)
                current = Concat(current, current);
        }

        return result;
    }
}
