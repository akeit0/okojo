namespace Okojo.Objects;

internal sealed class JsSetIteratorObject : JsObject
{
    private readonly IterationKind kind;

    private readonly JsSetObject set;
    private int cursor;
    private bool exhausted;

    internal JsSetIteratorObject(JsRealm realm, JsSetObject set, IterationKind kind) : base(realm)
    {
        this.set = set;
        this.kind = kind;
        Prototype = realm.SetIteratorPrototype;
    }

    internal JsValue Next()
    {
        if (exhausted)
            return Realm.CreateIteratorResultObject(JsValue.Undefined, true);
        if (!set.TryGetNextLiveValue(ref cursor, out var value))
        {
            exhausted = true;
            return Realm.CreateIteratorResultObject(JsValue.Undefined, true);
        }

        return kind switch
        {
            IterationKind.Values => JsValue.FromObject(Realm.CreateIteratorResultObject(value, false)),
            _ => JsValue.FromObject(Realm.CreateIteratorResultObject(CreateEntryPair(value), false))
        };
    }

    private JsPlainObject CreateIteratorResultObject(JsValue value, bool done)
    {
        return Realm.CreateIteratorResultObject(value, done);
    }

    private JsArray CreateEntryPair(JsValue value)
    {
        var pair = Realm.CreateArrayObject();
        pair.SetElement(0, value);
        pair.SetElement(1, value);
        return pair;
    }

    internal enum IterationKind : byte
    {
        Values,
        Entries
    }
}
