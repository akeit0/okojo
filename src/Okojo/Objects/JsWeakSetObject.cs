using System.Runtime.CompilerServices;

namespace Okojo.Objects;

internal sealed class JsWeakSetObject : JsObject
{
    private readonly HashSet<Symbol> symbolTable = new();

    private readonly ConditionalWeakTable<JsObject, Sentinel> table = new();

    internal JsWeakSetObject(JsRealm realm, JsObject prototype) : base(realm)
    {
        Prototype = prototype;
        realm.Agent.TrackWeakSet(this);
    }

    internal void AddValue(JsObject key)
    {
        table.Remove(key);
        table.Add(key, new());
    }

    internal bool HasValue(JsObject key)
    {
        return table.TryGetValue(key, out _);
    }

    internal bool DeleteValue(JsObject key)
    {
        return table.Remove(key);
    }

    internal void AddValue(Symbol key)
    {
        symbolTable.Add(key);
    }

    internal bool HasValue(Symbol key)
    {
        return symbolTable.Contains(key);
    }

    internal bool DeleteValue(Symbol key)
    {
        return symbolTable.Remove(key);
    }

    internal bool DeleteCollectedTarget(in JsValue target)
    {
        if (target.TryGetObject(out var objectKey))
            return DeleteValue(objectKey);
        if (target.IsSymbol)
            return DeleteValue(target.AsSymbol());
        return false;
    }

    private sealed class Sentinel;
}
