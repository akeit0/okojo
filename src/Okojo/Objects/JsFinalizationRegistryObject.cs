using Okojo.Internals;

namespace Okojo.Objects;

internal sealed class JsFinalizationRegistryObject : JsObject
{
    private readonly List<CellRecord> cells = new();
    private bool cleanupJobActive;

    internal JsFinalizationRegistryObject(JsRealm realm, JsObject prototype) : base(realm)
    {
        Prototype = prototype;
        CleanupCallback = null!;
        realm.Agent.TrackFinalizationRegistry(this);
    }

    internal JsFinalizationRegistryObject(JsRealm realm, JsObject prototype, JsFunction cleanupCallback) :
        base(realm)
    {
        Prototype = prototype;
        CleanupCallback = cleanupCallback;
        realm.Agent.TrackFinalizationRegistry(this);
    }

    internal JsFunction CleanupCallback { get; }

    internal int CellCount => cells.Count;

    internal void RegisterTarget(in JsValue target, in JsValue heldValue, in JsValue unregisterToken)
    {
        cells.Add(new(target, heldValue, unregisterToken));
    }

    internal bool Unregister(in JsValue unregisterToken)
    {
        var removed = false;
        for (var i = cells.Count - 1; i >= 0; i--)
        {
            if (!cells[i].HasUnregisterToken)
                continue;
            if (!JsValue.SameValue(cells[i].UnregisterToken, unregisterToken))
                continue;
            cells.RemoveAt(i);
            removed = true;
        }

        return removed;
    }

    internal bool MarkTargetCollected(in JsValue target)
    {
        var changed = false;
        for (var i = 0; i < cells.Count; i++)
            changed |= cells[i].MarkCollectedIfMatches(target);
        return changed;
    }

    internal bool TryActivateCleanupJob()
    {
        if (cleanupJobActive)
            return false;
        cleanupJobActive = true;
        return true;
    }

    internal void ResetCleanupJobActive()
    {
        cleanupJobActive = false;
    }

    internal void RunCleanupJob()
    {
        while (TryTakeClearedCell(out var heldValue))
        {
            var args = new InlineJsValueArray1 { Item0 = heldValue };
            Realm.InvokeFunction(CleanupCallback, JsValue.Undefined, args.AsSpan());
        }
    }

    private bool TryTakeClearedCell(out JsValue heldValue)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (!cells[i].IsTargetCleared)
                continue;
            heldValue = cells[i].HeldValue;
            cells.RemoveAt(i);
            return true;
        }

        heldValue = JsValue.Undefined;
        return false;
    }

    private sealed class CellRecord
    {
        private readonly WeakReference<JsObject>? objectTarget;
        private bool cleared;
        private Symbol? symbolTarget;

        internal CellRecord(in JsValue target, in JsValue heldValue, in JsValue unregisterToken)
        {
            if (target.TryGetObject(out var obj))
                objectTarget = new(obj);
            else if (target.IsSymbol)
                symbolTarget = target.AsSymbol();
            else
                throw new InvalidOperationException("FinalizationRegistry target must be weakly holdable.");

            HeldValue = heldValue;
            UnregisterToken = unregisterToken;
            HasUnregisterToken = !unregisterToken.IsUndefined;
        }

        internal JsValue HeldValue { get; }
        internal JsValue UnregisterToken { get; }
        internal bool HasUnregisterToken { get; }

        internal bool IsTargetCleared
        {
            get
            {
                if (cleared)
                    return true;
                if (objectTarget is not null)
                    return !objectTarget.TryGetTarget(out _);
                return symbolTarget is null;
            }
        }

        internal bool MarkCollectedIfMatches(in JsValue target)
        {
            if (cleared)
                return false;
            if (target.TryGetObject(out var obj))
            {
                if (objectTarget is null || !objectTarget.TryGetTarget(out var current) ||
                    !ReferenceEquals(current, obj))
                    return false;
                cleared = true;
                return true;
            }

            if (!target.IsSymbol || symbolTarget is null || !ReferenceEquals(symbolTarget, target.AsSymbol()))
                return false;

            symbolTarget = null;
            cleared = true;
            return true;
        }
    }
}
