using System.Diagnostics;
using System.Globalization;
using System.Text;
using Okojo.Diagnostics;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeUtilBuiltIn(NodeRuntime runtime)
{
    private const int ModuleFormatSlot = 0;
    private const int ModuleInspectSlot = 1;

    private int atomFormat = -1;
    private int atomInspect = -1;
    private JsPlainObject? moduleObject;
    private StaticNamedPropertyLayout? moduleShape;

    public JsPlainObject GetModule()
    {
        if (moduleObject is not null)
            return moduleObject;

        var realm = runtime.MainRealm;
        var shape = moduleShape ??= CreateModuleShape(realm);
        var module = new JsPlainObject(shape);
        module.SetNamedSlotUnchecked(ModuleFormatSlot, JsValue.FromObject(CreateFormatFunction(realm)));
        module.SetNamedSlotUnchecked(ModuleInspectSlot, JsValue.FromObject(CreateInspectFunction(realm)));
        moduleObject = module;
        return module;
    }

    private StaticNamedPropertyLayout CreateModuleShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomFormat, JsShapePropertyFlags.Open, out var formatInfo);
        shape = shape.GetOrAddTransition(atomInspect, JsShapePropertyFlags.Open, out var inspectInfo);
        Debug.Assert(formatInfo.Slot == ModuleFormatSlot);
        Debug.Assert(inspectInfo.Slot == ModuleInspectSlot);
        return shape;
    }

    private void EnsureAtoms(JsRealm realm)
    {
        atomFormat = EnsureAtom(realm, atomFormat, "format");
        atomInspect = EnsureAtom(realm, atomInspect, "inspect");
    }

    private static int EnsureAtom(JsRealm realm, int atom, string text)
    {
        return atom >= 0 ? atom : realm.Atoms.InternNoCheck(text);
    }

    private static JsHostFunction CreateFormatFunction(JsRealm realm)
    {
        return new(realm, "format", 1, static (in info) =>
        {
            var text = FormatValues(info);
            return JsValue.FromString(text);
        }, false);
    }

    private static JsHostFunction CreateInspectFunction(JsRealm realm)
    {
        return new(realm, "inspect", 1, static (in info) =>
        {
            var value = info.Arguments.Length == 0 ? JsValue.Undefined : info.Arguments[0];
            return JsValue.FromString(FormatInspectable(info.Realm, value));
        }, false);
    }

    private static string FormatValues(in CallInfo info)
    {
        if (info.Arguments.Length == 0)
            return string.Empty;

        var first = info.Arguments[0];
        if (!first.IsString) return JoinRenderedValues(info.Realm, info.Arguments);

        var format = first.AsString();
        var builder = new StringBuilder(format.Length + 16);
        var argIndex = 1;
        for (var i = 0; i < format.Length; i++)
        {
            var ch = format[i];
            if (ch != '%' || i + 1 >= format.Length)
            {
                builder.Append(ch);
                continue;
            }

            var spec = format[++i];
            switch (spec)
            {
                case '%':
                    builder.Append('%');
                    break;
                case 's':
                    builder.Append(argIndex < info.Arguments.Length
                        ? RenderForString(info.Realm, info.Arguments[argIndex++])
                        : "%s");
                    break;
                case 'd':
                case 'i':
                    builder.Append(argIndex < info.Arguments.Length
                        ? ((long)info.Realm.ToIntegerOrInfinity(info.Arguments[argIndex++])).ToString(CultureInfo
                            .InvariantCulture)
                        : "%" + spec);
                    break;
                case 'f':
                    builder.Append(argIndex < info.Arguments.Length
                        ? info.Realm.ToNumber(info.Arguments[argIndex++]).ToString(CultureInfo.InvariantCulture)
                        : "%f");
                    break;
                case 'j':
                    builder.Append(argIndex < info.Arguments.Length
                        ? RenderForJsonLike(info.Realm, info.Arguments[argIndex++])
                        : "%j");
                    break;
                case 'o':
                case 'O':
                    builder.Append(argIndex < info.Arguments.Length
                        ? FormatInspectable(info.Realm, info.Arguments[argIndex++])
                        : "%" + spec);
                    break;
                default:
                    builder.Append('%').Append(spec);
                    break;
            }
        }

        if (argIndex < info.Arguments.Length)
        {
            if (builder.Length != 0)
                builder.Append(' ');
            builder.Append(JoinRenderedValues(info.Realm, info.Arguments[argIndex..]));
        }

        return builder.ToString();
    }

    private static string JoinRenderedValues(JsRealm realm, ReadOnlySpan<JsValue> values)
    {
        if (values.Length == 0)
            return string.Empty;

        var builder = new StringBuilder();
        for (var i = 0; i < values.Length; i++)
        {
            if (i != 0)
                builder.Append(' ');
            builder.Append(FormatInspectable(realm, values[i]));
        }

        return builder.ToString();
    }

    private static string RenderForString(JsRealm realm, in JsValue value)
    {
        return value.IsString ? value.AsString() : realm.ToJsStringSlowPath(value);
    }

    private static string RenderForJsonLike(JsRealm realm, in JsValue value)
    {
        if (value.IsString)
            return "\"" + value.AsString().Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        if (value.IsNumber || value.IsBool || value.IsNull)
            return FormatInspectable(realm, value);
        if (!value.TryGetObject(out var obj))
            return "null";

        if (obj is JsArray array)
        {
            var parts = new string[array.Length];
            for (uint i = 0; i < array.Length; i++)
                parts[i] = array.TryGetProperty(i.ToString(CultureInfo.InvariantCulture), out var element)
                    ? RenderForJsonLike(realm, element)
                    : "null";
            return "[" + string.Join(",", parts) + "]";
        }

        var keys = GetOwnEnumerableStringKeys(realm, obj);
        var builder = new StringBuilder();
        builder.Append('{');
        for (var i = 0; i < keys.Count; i++)
        {
            if (i != 0)
                builder.Append(',');
            var key = keys[i];
            _ = obj.TryGetProperty(key, out var propertyValue);
            builder.Append('"').Append(key.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            builder.Append(':');
            builder.Append(RenderForJsonLike(realm, propertyValue));
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static string FormatInspectable(JsRealm realm, in JsValue value)
    {
        return new ReplFormatter(realm).Format(value);
    }

    private static List<string> GetOwnEnumerableStringKeys(JsRealm realm, JsObject obj)
    {
        var keysValue = realm.InvokeObjectConstructorMethod("keys", [JsValue.FromObject(obj)]);
        if (!keysValue.TryGetObject(out var keysObject))
            return [];

        var length = realm.GetArrayLikeLengthLong(keysObject);
        var keys = new List<string>((int)Math.Max(0, length));
        for (var i = 0; i < length; i++)
            if (keysObject.TryGetProperty(i.ToString(CultureInfo.InvariantCulture), out var keyValue))
                keys.Add(keyValue.IsString ? keyValue.AsString() : realm.ToJsStringSlowPath(keyValue));

        return keys;
    }
}
