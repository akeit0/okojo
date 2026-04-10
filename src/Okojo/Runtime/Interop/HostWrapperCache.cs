using System.Runtime.CompilerServices;

namespace Okojo.Runtime.Interop;

internal sealed class HostWrapperCache
{
    private readonly ConditionalWeakTable<object, Entry> entries = new();

    internal bool TryGet(object target, out JsHostObject wrapper)
    {
        if (entries.TryGetValue(target, out var entry))
            return entry.TryGet(out wrapper);

        wrapper = null!;
        return false;
    }

    internal JsHostObject GetOrAdd<TState>(object target, TState state, Func<object, TState, JsHostObject> factory)
    {
        var entry = entries.GetValue(target, static _ => new());
        lock (entry.Sync)
        {
            if (entry.TryGet(out var existing))
                return existing;

            var created = factory(target, state);
            entry.Set(created);
            return created;
        }
    }

    private sealed class Entry
    {
        private WeakReference<JsHostObject>? wrapper;

        internal object Sync { get; } = new();

        internal bool TryGet(out JsHostObject value)
        {
            if (wrapper is not null && wrapper.TryGetTarget(out value!))
                return true;

            value = null!;
            return false;
        }

        internal void Set(JsHostObject value)
        {
            wrapper = new(value);
        }
    }
}
