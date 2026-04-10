namespace Okojo.Runtime;

internal static class ArraySortHelpers
{
    internal static void SortArrayLike(
        JsRealm realm,
        JsObject obj,
        long length,
        JsFunction? compareFn,
        Func<JsRealm, JsObject, long, bool> hasIndex,
        GetIndexDelegate getIndex,
        SetIndexDelegate setIndexOrThrow,
        Action<JsRealm, JsObject, long> deleteIndexOrThrow)
    {
        var definedElements = new List<JsValue>();
        long undefinedCount = 0;

        for (long k = 0; k < length; k++)
        {
            if (!hasIndex(realm, obj, k))
                continue;

            getIndex(realm, obj, k, out var value);
            if (value.IsUndefined)
            {
                undefinedCount++;
                continue;
            }

            definedElements.Add(value);
        }

        StableSortDefinedElements(realm, definedElements, compareFn);

        long writeIndex = 0;
        for (var i = 0; i < definedElements.Count; i++, writeIndex++)
            setIndexOrThrow(realm, obj, writeIndex, definedElements[i]);

        for (long i = 0; i < undefinedCount; i++, writeIndex++)
            setIndexOrThrow(realm, obj, writeIndex, JsValue.Undefined);

        for (; writeIndex < length; writeIndex++)
            deleteIndexOrThrow(realm, obj, writeIndex);
    }

    private static void StableSortDefinedElements(JsRealm realm, List<JsValue> values, JsFunction? compareFn)
    {
        if (values.Count <= 1)
            return;

        var buffer = new JsValue[values.Count];
        StableMergeSort(values, buffer, 0, values.Count, static (left, right, state) =>
            CompareArraySortValues(state.realm, state.compareFn, left, right), (realm, compareFn));
    }

    private static void StableMergeSort<T, TState>(
        List<T> values,
        T[] buffer,
        int start,
        int end,
        Func<T, T, TState, int> compare,
        TState state)
    {
        if (end - start <= 1)
            return;

        var middle = start + (end - start) / 2;
        StableMergeSort(values, buffer, start, middle, compare, state);
        StableMergeSort(values, buffer, middle, end, compare, state);

        var left = start;
        var right = middle;
        var target = start;

        while (left < middle && right < end)
            if (compare(values[left], values[right], state) <= 0)
                buffer[target++] = values[left++];
            else
                buffer[target++] = values[right++];

        while (left < middle)
            buffer[target++] = values[left++];

        while (right < end)
            buffer[target++] = values[right++];

        for (var i = start; i < end; i++)
            values[i] = buffer[i];
    }

    private static int CompareArraySortValues(JsRealm realm, JsFunction? compareFn, in JsValue left,
        in JsValue right)
    {
        if (compareFn is not null)
        {
            Span<JsValue> args = [left, right];
            var compareResult =
                realm.ToNumberSlowPath(realm.InvokeFunction(compareFn, JsValue.Undefined, args));
            if (double.IsNaN(compareResult) || compareResult == 0)
                return 0;
            return compareResult < 0 ? -1 : 1;
        }

        var leftString = realm.ToJsStringSlowPath(left);
        var rightString = realm.ToJsStringSlowPath(right);
        return string.CompareOrdinal(leftString, rightString);
    }

    internal delegate void GetIndexDelegate(JsRealm realm, JsObject obj, long index, out JsValue value);

    internal delegate void SetIndexDelegate(JsRealm realm, JsObject obj, long index, JsValue value);
}
