namespace Okojo.Compiler.Experimental;

internal sealed partial class JsPlannedScriptCompiler
{
    private void InitializePlanIndexes(CompilerBindingCollectionResult collected, CompilerBindingPlan plan)
    {
        plannedBindingsByScopeId.Clear();
        scopesByParentScopeId.Clear();

        foreach (var scope in collected.Scopes)
        {
            if (!scopesByParentScopeId.TryGetValue(scope.ParentScopeId, out var children))
            {
                children = new();
                scopesByParentScopeId[scope.ParentScopeId] = children;
            }

            children.Add(scope);
        }

        foreach (var binding in plan.Bindings)
        {
            if (!plannedBindingsByScopeId.TryGetValue(binding.ScopeId, out var bindings))
            {
                bindings = new();
                plannedBindingsByScopeId[binding.ScopeId] = bindings;
            }

            bindings.Add(binding);
        }
    }

    private void InitializeRootBindings()
    {
        rootBindingsByName.Clear();
        rootContextSlotCount = 0;
        if (!plannedBindingsByScopeId.TryGetValue(0, out var rootBindings))
        {
            activeScopes.Clear();
            activeScopes.Push(new ActiveScope(0, [], false));
            return;
        }

        var allocated = new List<RootBindingStorage>(rootBindings.Count);
        foreach (var binding in rootBindings)
        {
            var register = binding.StorageKind switch
            {
                CompilerPlannedStorageKind.ImportBinding => -1,
                CompilerPlannedStorageKind.ContextSlot => -1,
                _ => builder.AllocatePinnedRegister()
            };
            if (binding.StorageKind == CompilerPlannedStorageKind.ContextSlot)
                rootContextSlotCount = Math.Max(rootContextSlotCount, binding.StorageIndex + 1);
            var storage = new RootBindingStorage(binding, register);
            allocated.Add(storage);
            rootBindingsByName.TryAdd(binding.Name, storage);
        }

        activeScopes.Clear();
        activeScopes.Push(new ActiveScope(0, allocated, rootContextSlotCount != 0));
    }
}
