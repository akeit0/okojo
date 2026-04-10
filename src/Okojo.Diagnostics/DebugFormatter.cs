using Okojo.Runtime;

namespace Okojo.Diagnostics;

public static class DebugFormatter
{
    public static string FormatForRepl(JsRealm realm, in JsValue value, int? indentSize = 2)
    {
        var formatter = new ReplFormatter(realm, indentSize);
        return formatter.Format(value);
    }

    public static string FormatValue(in JsValue value)
    {
        return JsValueDebugString.FormatValue(value);
    }
}
