namespace Okojo.Runtime;

public sealed partial class JsAgent
{
    private static readonly Action<object?> SFinalizationRegistryCleanupTask = static state =>
    {
        var finalizationRegistry = (JsFinalizationRegistryObject)state!;
        try
        {
            finalizationRegistry.RunCleanupJob();
        }
        catch (JsRuntimeException ex)
        {
            finalizationRegistry.Realm.ReportFinalizationRegistryCleanupError(ex);
        }
        finally
        {
            finalizationRegistry.ResetCleanupJobActive();
        }
    };

    private readonly List<WeakReference<JsFinalizationRegistryObject>> trackedFinalizationRegistries = new();
    private readonly List<WeakReference<JsWeakMapObject>> trackedWeakMaps = new();
    private readonly List<WeakReference<JsWeakRefObject>> trackedWeakRefs = new();
    private readonly List<WeakReference<JsWeakSetObject>> trackedWeakSets = new();

    private readonly object weakTrackingGate = new();

    internal void TrackWeakRef(JsWeakRefObject weakRef)
    {
        lock (weakTrackingGate)
        {
            PruneDeadWeakTracking_NoLock();
            trackedWeakRefs.Add(new(weakRef));
        }
    }

    internal void TrackFinalizationRegistry(JsFinalizationRegistryObject registry)
    {
        lock (weakTrackingGate)
        {
            PruneDeadWeakTracking_NoLock();
            trackedFinalizationRegistries.Add(new(registry));
        }
    }

    internal void TrackWeakMap(JsWeakMapObject weakMap)
    {
        lock (weakTrackingGate)
        {
            PruneDeadWeakTracking_NoLock();
            trackedWeakMaps.Add(new(weakMap));
        }
    }

    internal void TrackWeakSet(JsWeakSetObject weakSet)
    {
        lock (weakTrackingGate)
        {
            PruneDeadWeakTracking_NoLock();
            trackedWeakSets.Add(new(weakSet));
        }
    }

    internal bool NotifyWeakTargetCollected(in JsValue target)
    {
        if (!CanProcessCollectedTarget(target))
            return false;

        List<JsFinalizationRegistryObject>? registriesNeedingCleanup = null;
        var changed = false;

        lock (weakTrackingGate)
        {
            PruneDeadWeakTracking_NoLock();

            foreach (var entry in trackedWeakRefs)
            {
                if (!entry.TryGetTarget(out var weakRef))
                    continue;
                if (!weakRef.MatchesTarget(target))
                    continue;
                weakRef.ClearTarget();
                changed = true;
            }

            foreach (var entry in trackedFinalizationRegistries)
            {
                if (!entry.TryGetTarget(out var registry))
                    continue;
                if (!registry.MarkTargetCollected(target))
                    continue;
                changed = true;
                if (registry.TryActivateCleanupJob())
                {
                    registriesNeedingCleanup ??= new();
                    registriesNeedingCleanup.Add(registry);
                }
            }

            foreach (var entry in trackedWeakMaps)
            {
                if (!entry.TryGetTarget(out var weakMap))
                    continue;
                changed |= weakMap.DeleteCollectedTarget(target);
            }

            foreach (var entry in trackedWeakSets)
            {
                if (!entry.TryGetTarget(out var weakSet))
                    continue;
                changed |= weakSet.DeleteCollectedTarget(target);
            }
        }

        if (registriesNeedingCleanup is not null)
            for (var i = 0; i < registriesNeedingCleanup.Count; i++)
                HostEnqueueFinalizationRegistryCleanupJob(registriesNeedingCleanup[i]);

        return changed;
    }

    internal void HostEnqueueFinalizationRegistryCleanupJob(JsFinalizationRegistryObject finalizationRegistry)
    {
        if (IsTerminated)
            return;

        EnqueueHostTask(SFinalizationRegistryCleanupTask, finalizationRegistry);
    }

    private bool CanProcessCollectedTarget(in JsValue target)
    {
        if (target.TryGetObject(out var obj))
            return !IsKeptAlive(obj);
        if (target.IsSymbol)
            return !IsKeptAlive(target.AsSymbol());
        return false;
    }

    private void PruneDeadWeakTracking_NoLock()
    {
        trackedWeakRefs.RemoveAll(static entry => !entry.TryGetTarget(out _));
        trackedFinalizationRegistries.RemoveAll(static entry => !entry.TryGetTarget(out _));
        trackedWeakMaps.RemoveAll(static entry => !entry.TryGetTarget(out _));
        trackedWeakSets.RemoveAll(static entry => !entry.TryGetTarget(out _));
    }
}
