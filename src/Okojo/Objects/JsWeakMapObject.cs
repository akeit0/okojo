using System.Runtime.CompilerServices;

namespace Okojo.Objects;

internal sealed class JsWeakMapObject : JsObject
{
    private readonly Dictionary<Symbol, JsValue> symbolTable = new();

    private readonly ConditionalWeakTable<JsObject, ValueBox> table = new();

    internal JsWeakMapObject(JsRealm realm, JsObject prototype) : base(realm)
    {
        Prototype = prototype;
        realm.Agent.TrackWeakMap(this);
    }

    internal void SetValue(JsObject key, JsValue value)
    {
        table.Remove(key);
        table.Add(key, new(value));
    }

    internal bool TryGetValue(JsObject key, out JsValue value)
    {
        if (table.TryGetValue(key, out var box))
        {
            value = box.Value;
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    internal bool HasKey(JsObject key)
    {
        return table.TryGetValue(key, out _);
    }

    internal bool DeleteKey(JsObject key)
    {
        return table.Remove(key);
    }

    internal void SetValue(Symbol key, JsValue value)
    {
        symbolTable[key] = value;
    }

    internal bool TryGetValue(Symbol key, out JsValue value)
    {
        if (symbolTable.TryGetValue(key, out value))
            return true;

        value = JsValue.Undefined;
        return false;
    }

    internal bool HasKey(Symbol key)
    {
        return symbolTable.ContainsKey(key);
    }

    internal bool DeleteKey(Symbol key)
    {
        return symbolTable.Remove(key);
    }

    internal bool DeleteCollectedTarget(in JsValue target)
    {
        if (target.TryGetObject(out var objectKey))
            return DeleteKey(objectKey);
        if (target.IsSymbol)
            return DeleteKey(target.AsSymbol());
        return false;
    }

    private sealed class ValueBox(JsValue value)
    {
        public readonly JsValue Value = value;
    }
}
