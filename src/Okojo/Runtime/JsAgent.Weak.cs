namespace Okojo.Runtime;

public sealed partial class JsAgent
{
    private readonly HashSet<object> keptAlive = new(ReferenceEqualityComparer.Instance);
    private readonly object weakLifecycleGate = new();

    internal void AddToKeptObjects(JsObject value)
    {
        lock (weakLifecycleGate)
        {
            keptAlive.Add(value);
        }
    }

    internal void AddToKeptObjects(Symbol value)
    {
        lock (weakLifecycleGate)
        {
            keptAlive.Add(value);
        }
    }

    internal void AddToKeptObjects(in JsValue value)
    {
        if (value.TryGetObject(out var obj))
            AddToKeptObjects(obj);
        else if (value.IsSymbol) AddToKeptObjects(value.AsSymbol());
    }

    internal void ClearKeptObjects()
    {
        lock (weakLifecycleGate)
        {
            keptAlive.Clear();
        }
    }

    internal bool IsKeptAlive(JsObject value)
    {
        lock (weakLifecycleGate)
        {
            return keptAlive.Contains(value);
        }
    }

    internal bool IsKeptAlive(Symbol value)
    {
        lock (weakLifecycleGate)
        {
            return keptAlive.Contains(value);
        }
    }
}
