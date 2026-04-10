using System.Text;

namespace Okojo.Runtime;

internal static class JsonStringEncoding
{
    public static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        AppendEscaped(builder, value);
        return builder.ToString();
    }

    public static void AppendEscaped(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    builder.Append(ch);
                    builder.Append(value[++i]);
                    continue;
                }

                AppendUnicodeEscape(builder, ch);
                continue;
            }

            if (char.IsLowSurrogate(ch))
            {
                AppendUnicodeEscape(builder, ch);
                continue;
            }

            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (ch < 0x20)
                        AppendUnicodeEscape(builder, ch);
                    else
                        builder.Append(ch);

                    break;
            }
        }

        builder.Append('"');
    }

    private static void AppendUnicodeEscape(StringBuilder builder, char ch)
    {
        builder.Append("\\u");
        builder.Append(((int)ch).ToString("x4"));
    }
}
