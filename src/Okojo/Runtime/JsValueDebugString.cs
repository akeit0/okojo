using System.Globalization;
using System.Runtime.CompilerServices;

namespace Okojo.Runtime;

internal static class JsValueDebugString
{
    public static string FormatValue(in JsValue value)
    {
        if (value.IsUndefined) return "undefined";
        if (value.IsTheHole) return "the hole";
        if (value.IsNull) return "null";
        if (value.IsBool) return value.U == JsValue.True.U ? "true" : "false";
        if (value.IsInt32) return $"Number({value.Int32Value})";
        if (value.IsFloat64)
        {
            var v = value.Float64Value;
            if (Unsafe.BitCast<double, ulong>(v) == Unsafe.BitCast<double, ulong>(-0)) return "Number(-0)";
            return $"Number({v.ToString(CultureInfo.InvariantCulture)})";
        }

        if (value.IsString) return $"String(\"{Escape(value.AsString())}\")";
        if (value.IsObject)
        {
            var obj = value.AsObject();
            return obj switch
            {
                JsFunction fn => $"Function({fn.Name ?? "<anonymous>"})",
                JsGeneratorObject gen =>
                    $"Generator(state={gen.State}, resume={gen.PendingResumeMode}, suspend={gen.SuspendId})",
                _ => obj.ToDisplayString()
            };
        }

        if (value.Obj is JsContext)
            return "Context";

        return value.Tag.ToString();
    }

    private static string Escape(string s)
    {
        const int max = 40;
        var escaped = s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        return escaped.Length <= max ? escaped : escaped[..max] + "...<truncated>";
    }
}
