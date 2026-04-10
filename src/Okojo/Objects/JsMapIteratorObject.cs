namespace Okojo.Objects;

internal sealed class JsMapIteratorObject : JsObject
{
    private readonly IterationKind kind;

    private readonly JsMapObject map;
    private int cursor;
    private bool exhausted;

    internal JsMapIteratorObject(JsRealm realm, JsMapObject map, IterationKind kind) : base(realm)
    {
        this.map = map;
        this.kind = kind;
        Prototype = realm.MapIteratorPrototype;
    }

    internal JsValue Next()
    {
        if (exhausted)
            return JsValue.FromObject(Realm.CreateIteratorResultObject(JsValue.Undefined, true));

        if (!map.TryGetNextLiveEntry(ref cursor, out var key, out var value))
        {
            exhausted = true;
            return JsValue.FromObject(Realm.CreateIteratorResultObject(JsValue.Undefined, true));
        }

        return kind switch
        {
            IterationKind.Keys => JsValue.FromObject(Realm.CreateIteratorResultObject(key, false)),
            IterationKind.Values => JsValue.FromObject(Realm.CreateIteratorResultObject(value, false)),
            _ => JsValue.FromObject(Realm.CreateIteratorResultObject(CreateEntryPair(key, value),
                false))
        };
    }

    private JsArray CreateEntryPair(JsValue valueKey, JsValue entryValue)
    {
        var pair = Realm.CreateArrayObject();
        pair.SetElement(0, valueKey);
        pair.SetElement(1, entryValue);
        return pair;
    }

    internal enum IterationKind : byte
    {
        Keys,
        Values,
        Entries
    }
}
