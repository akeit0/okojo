namespace Okojo.Runtime;

internal sealed class TypedArrayIteratorObject : JsObject
{
    private readonly IterationKind kind;

    private readonly JsTypedArrayObject typedArray;
    private uint index;

    internal TypedArrayIteratorObject(JsRealm realm, JsTypedArrayObject typedArray, IterationKind kind) :
        base(realm)
    {
        this.typedArray = typedArray;
        this.kind = kind;
        Prototype = realm.TypedArrayIteratorPrototype;
    }

    internal JsValue Next()
    {
        if (typedArray.Buffer.IsDetached)
            throw new JsRuntimeException(JsErrorKind.TypeError,
                "Cannot perform Array Iterator.prototype.next on a detached TypedArray");

        if (index >= typedArray.Length)
            return Realm.CreateIteratorResultObject(JsValue.Undefined, true);

        var currentIndex = index++;
        _ = typedArray.TryGetElement(currentIndex, out var value);

        return kind switch
        {
            IterationKind.Keys => JsValue.FromObject(Realm.CreateIteratorResultObject(
                JsValue.FromInt32((int)currentIndex),
                false)),
            IterationKind.Values => JsValue.FromObject(Realm.CreateIteratorResultObject(value, false)),
            _ => JsValue.FromObject(Realm.CreateIteratorResultObject(CreateEntryPair(currentIndex, value), false))
        };
    }

    private JsArray CreateEntryPair(uint indexValue, JsValue entryValue)
    {
        var pair = Realm.CreateArrayObject();
        pair.SetElement(0, JsValue.FromInt32((int)indexValue));
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
