namespace Okojo.JavaScript.Objects;

internal sealed class JsArrayIteratorObject : JsObject
{
    private readonly JsObject arrayLike;
    private readonly IterationKind kind;
    private bool done;
    private long index;

    internal JsArrayIteratorObject(
        JsRealm realm,
        JsObject arrayLike,
        IterationKind kind,
        long index = 0
    )
        : base(realm)
    {
        this.arrayLike = arrayLike;
        this.kind = kind;
        this.index = index;
        Prototype = realm.ArrayIteratorPrototype;
    }

    internal JsValue Next()
    {
        var value = NextValue(out var finished);
        return Realm.CreateIteratorResultObject(value, finished);
    }

    internal JsValue NextValue(out bool finished)
    {
        finished = true;
        if (done)
            return JsValue.Undefined;

        if (arrayLike is JsTypedArrayObject typedArray)
            if (typedArray.IsOutOfBounds)
                throw new JsRuntimeException(
                    JsErrorKind.TypeError,
                    "Cannot perform Array Iterator.prototype.next on an out-of-bounds TypedArray"
                );

        var array = arrayLike as JsArray;
        var length = array is null ? Realm.GetArrayLikeLengthLong(arrayLike) : array.Length;
        if (index >= length)
        {
            done = true;
            return JsValue.Undefined;
        }

        var currentIndex = index++;
        finished = false;
        if (kind == IterationKind.Keys)
            return IndexValue(currentIndex);

        if (
            (array is null || !array.TryGetDenseElement((uint)currentIndex, out var value))
            && !Intrinsics.TryGetArrayLikeIndex(Realm, arrayLike, currentIndex, out value)
        )
            value = JsValue.Undefined;

        return kind == IterationKind.Values ? value : CreateEntryPair(currentIndex, value);
    }

    private static JsValue IndexValue(long index) =>
        index <= int.MaxValue ? JsValue.FromInt32((int)index) : new JsValue(index);

    private JsArray CreateEntryPair(long index, JsValue entryValue)
    {
        var pair = Realm.CreateArrayObject();
        pair.InitializeLiteralElement(0, IndexValue(index));
        pair.InitializeLiteralElement(1, entryValue);
        return pair;
    }

    internal enum IterationKind : byte
    {
        Keys,
        Values,
        Entries,
    }
}
