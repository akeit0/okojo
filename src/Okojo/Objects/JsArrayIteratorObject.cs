namespace Okojo.Objects;

internal sealed class JsArrayIteratorObject : JsObject
{
    private readonly JsObject arrayLike;
    private readonly IterationKind kind;
    private bool done;
    private uint index;

    internal JsArrayIteratorObject(JsRealm realm, JsObject arrayLike, IterationKind kind) : base(realm)
    {
        this.arrayLike = arrayLike;
        this.kind = kind;
        Prototype = realm.ArrayIteratorPrototype;
    }

    internal JsValue Next()
    {
        if (done)
            return CreateIteratorResultObject(JsValue.Undefined, true);

        if (arrayLike is JsTypedArrayObject typedArray)
            if (typedArray.IsOutOfBounds)
                throw new JsRuntimeException(JsErrorKind.TypeError,
                    "Cannot perform Array Iterator.prototype.next on an out-of-bounds TypedArray");

        var length = (uint)Realm.GetArrayLikeLengthLong(arrayLike);
        if (index >= length)
        {
            done = true;
            return CreateIteratorResultObject(JsValue.Undefined, true);
        }

        var currentIndex = index++;
        var value = Intrinsics.TryGetArrayLikeIndex(Realm, arrayLike, currentIndex, out var elementValue)
            ? elementValue
            : JsValue.Undefined;

        return kind switch
        {
            IterationKind.Keys => JsValue.FromObject(CreateIteratorResultObject(JsValue.FromInt32((int)currentIndex),
                false)),
            IterationKind.Values => JsValue.FromObject(CreateIteratorResultObject(value, false)),
            _ => JsValue.FromObject(CreateIteratorResultObject(CreateEntryPair(currentIndex, value), false))
        };
    }

    private JsPlainObject CreateIteratorResultObject(JsValue value, bool done)
    {
        return Realm.CreateIteratorResultObject(value, done);
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
