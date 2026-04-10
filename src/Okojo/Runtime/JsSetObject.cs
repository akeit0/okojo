namespace Okojo.Runtime;

public sealed class JsSetObject : JsObject
{
    private readonly List<Entry> entries = new(4);
    private readonly Dictionary<JsValue, int> entryIndexByValue = new(JsValueSameValueZeroComparer.Instance);

    public JsSetObject(JsRealm realm, JsObject? prototype = null) : base(realm)
    {
        Prototype = prototype ?? realm.SetPrototype;
    }

    public int Count { get; private set; }

    public bool HasValue(in JsValue value)
    {
        return entryIndexByValue.ContainsKey(CanonicalizeKey(value));
    }

    public void AddValue(in JsValue value)
    {
        var normalized = CanonicalizeKey(value);
        if (entryIndexByValue.ContainsKey(normalized))
            return;

        entryIndexByValue[normalized] = entries.Count;
        entries.Add(new() { Value = normalized });
        Count++;
    }

    public bool DeleteValue(in JsValue value)
    {
        if (!entryIndexByValue.Remove(CanonicalizeKey(value), out var entryIndex))
            return false;

        var entry = entries[entryIndex];
        entry.IsDeleted = true;
        entries[entryIndex] = entry;
        Count--;
        return true;
    }

    public void ClearEntries()
    {
        entries.Clear();
        entryIndexByValue.Clear();
        Count = 0;
    }

    public bool TryGetNextLiveValue(ref int cursor, out JsValue value)
    {
        while ((uint)cursor < (uint)entries.Count)
        {
            var entry = entries[cursor++];
            if (entry.IsDeleted)
                continue;

            value = entry.Value;
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    private static JsValue CanonicalizeKey(in JsValue value)
    {
        if (value.IsNumber && value.NumberValue == 0d)
            return new(0d);
        return value;
    }

    private struct Entry
    {
        public JsValue Value;
        public bool IsDeleted;
    }
}
