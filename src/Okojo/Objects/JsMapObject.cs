namespace Okojo.Objects;

public sealed class JsMapObject : JsObject
{
    private readonly List<Entry> entries = new(4);
    private readonly Dictionary<JsValue, int> entryIndexByKey = new(JsValueSameValueZeroComparer.Instance);

    public JsMapObject(JsRealm realm, JsObject? prototype = null) : base(realm)
    {
        Prototype = prototype ?? realm.MapPrototype;
    }

    public int Count { get; private set; }

    public bool TryGetValue(in JsValue key, out JsValue value)
    {
        if (entryIndexByKey.TryGetValue(key, out var entryIndex))
        {
            value = entries[entryIndex].Value;
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    public bool HasKey(in JsValue key)
    {
        return entryIndexByKey.ContainsKey(key);
    }

    public void SetValue(in JsValue key, in JsValue value)
    {
        if (entryIndexByKey.TryGetValue(key, out var entryIndex))
        {
            var entry = entries[entryIndex];
            entry.Value = value;
            entries[entryIndex] = entry;
            return;
        }

        entryIndexByKey[key] = entries.Count;
        entries.Add(new() { Key = key, Value = value, IsDeleted = false });
        Count++;
    }

    public bool DeleteKey(in JsValue key)
    {
        if (!entryIndexByKey.Remove(key, out var entryIndex))
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
        entryIndexByKey.Clear();
        Count = 0;
    }

    public bool TryGetNextLiveEntry(ref int cursor, out JsValue key, out JsValue value)
    {
        while ((uint)cursor < (uint)entries.Count)
        {
            var entry = entries[cursor++];
            if (entry.IsDeleted)
                continue;

            key = entry.Key;
            value = entry.Value;
            return true;
        }

        key = JsValue.Undefined;
        value = JsValue.Undefined;
        return false;
    }

    private struct Entry
    {
        public JsValue Key;
        public JsValue Value;
        public bool IsDeleted;
    }
}
