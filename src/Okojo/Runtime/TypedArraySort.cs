namespace Okojo.Runtime;

internal static class TypedArraySort
{
    internal static void Sort(JsRealm realm, JsTypedArrayObject typedArray, JsFunction? compareFn)
    {
        var initialLength = typedArray.Length;
        if (initialLength <= 1)
            return;

        var values = new List<JsValue>((int)initialLength);
        for (uint index = 0; index < initialLength; index++)
        {
            typedArray.TryGetElement(index, out var value);
            values.Add(value);
        }

        StableSort(realm, values, compareFn);

        if (typedArray.IsOutOfBounds)
            return;

        var writeLength = Math.Min(initialLength, typedArray.Length);
        for (uint index = 0; index < writeLength; index++)
            typedArray.TrySetElement(index, values[(int)index]);
    }

    private static void StableSort(JsRealm realm, List<JsValue> values, JsFunction? compareFn)
    {
        if (values.Count <= 1)
            return;

        var buffer = new JsValue[values.Count];
        StableMergeSort(values, buffer, 0, values.Count, static (left, right, state) =>
            Compare(state.realm, state.compareFn, left, right), (realm, compareFn));
    }

    private static int Compare(JsRealm realm, JsFunction? compareFn, in JsValue left, in JsValue right)
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

        if (left.IsBigInt && right.IsBigInt)
            return left.AsBigInt().Value.CompareTo(right.AsBigInt().Value);

        var x = left.NumberValue;
        var y = right.NumberValue;
        var xNaN = double.IsNaN(x);
        var yNaN = double.IsNaN(y);
        if (xNaN)
            return yNaN ? 0 : 1;
        if (yNaN)
            return -1;

        if (x == 0d && y == 0d)
        {
            var xNegZero = IsNegativeZero(x);
            var yNegZero = IsNegativeZero(y);
            if (xNegZero == yNegZero)
                return 0;
            return xNegZero ? -1 : 1;
        }

        if (x < y)
            return -1;
        if (x > y)
            return 1;
        return 0;
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

    private static bool IsNegativeZero(double value)
    {
        return value == 0d && BitConverter.DoubleToInt64Bits(value) == unchecked((long)0x8000000000000000UL);
    }
}
