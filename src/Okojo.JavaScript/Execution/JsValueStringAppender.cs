using System.Globalization;
using System.Runtime.CompilerServices;

namespace Okojo.JavaScript.Execution;

/// <summary>
/// Appends ECMAScript ToString(value) characters into an interpolated string
/// handler without intermediate materialization where possible: slice and
/// flat-rope strings are appended from their existing char window, and
/// integral doubles (indices, counts) format into a stack buffer via
/// long.TryFormat instead of allocating a decimal string. Everything else
/// falls back to <see cref="RealmExtensions.ToJsStringSlowPath" />.
/// </summary>
internal static class JsValueStringAppender
{
    internal static void Append(
        JsRealm realm,
        ref DefaultInterpolatedStringHandler handler,
        in JsValue value
    )
    {
        if (value.IsString)
        {
            var jsString = value.AsJsString();
            if (jsString.TryGetFlatSpanChars(out var chars))
                handler.AppendFormatted(chars);
            else
                handler.AppendFormatted(jsString.Flatten());
            return;
        }

        if (value.IsNumber)
        {
            AppendNumber(ref handler, value.NumberValue);
            return;
        }

        handler.AppendFormatted(realm.ToJsStringSlowPath(value));
    }

    private static void AppendNumber(ref DefaultInterpolatedStringHandler handler, double number)
    {
        if (number == 0d)
        {
            // ECMAScript keeps the sign of negative zero.
            handler.AppendLiteral(double.IsNegative(number) ? "-0" : "0");
            return;
        }

        if (NumberFormatting.IsIntegralSafe(number))
        {
            Span<char> buffer = stackalloc char[20];
            ((long)number).TryFormat(buffer, out var written, null, CultureInfo.InvariantCulture);
            handler.AppendFormatted(buffer[..written]);
            return;
        }

        handler.AppendFormatted(NumberFormatting.ToString(number));
    }
}
