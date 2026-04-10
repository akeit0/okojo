namespace Okojo.Objects;

internal sealed class JsStringIteratorObject : JsObject
{
    private readonly int flatStart;
    private readonly JsString text;
    private string? flatText;
    private int index;

    internal JsStringIteratorObject(JsRealm realm, JsString text) : base(realm)
    {
        this.text = text;
        if (text.TryGetFlatWindow(out var flat, out var start, out _))
        {
            flatText = flat;
            flatStart = start;
        }

        Prototype = realm.StringIteratorPrototype;
    }

    internal JsValue Next()
    {
        if (index >= text.Length)
            return JsValue.FromObject(Realm.CreateIteratorResultObject(JsValue.Undefined, true));

        var flat = flatText ??= text.Flatten();
        var absoluteIndex = flatStart + index;
        var first = flat[absoluteIndex];
        if (char.IsHighSurrogate(first) &&
            index + 1 < text.Length &&
            char.IsLowSurrogate(flat[absoluteIndex + 1]))
        {
            var second = flat[absoluteIndex + 1];
            index += 2;
            return JsValue.FromObject(Realm.CreateIteratorResultObject(
                JsValue.FromString(string.Create(2, (first, second), static (chars, state) =>
                {
                    chars[0] = state.first;
                    chars[1] = state.second;
                })),
                false));
        }

        index++;
        return JsValue.FromObject(Realm.CreateIteratorResultObject(
            JsValue.FromString(first.ToString()),
            false));
    }
}
