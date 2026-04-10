namespace Okojo.Compiler.Experimental;

internal sealed partial class JsPlannedFunctionCompiler
{
    private void InitializePlanIndexes(CompilerBindingCollectionResult collected, CompilerBindingPlan plan)
    {
        plannedBindingsByScopeId.Clear();
        scopesByParentScopeId.Clear();

        foreach (var scope in collected.Scopes)
        {
            if (!scopesByParentScopeId.TryGetValue(scope.ParentScopeId, out var children))
            {
                children = [];
                scopesByParentScopeId[scope.ParentScopeId] = children;
            }

            children.Add(scope);
        }

        foreach (var binding in plan.Bindings)
        {
            if (!plannedBindingsByScopeId.TryGetValue(binding.ScopeId, out var bindings))
            {
                bindings = [];
                plannedBindingsByScopeId[binding.ScopeId] = bindings;
            }

            bindings.Add(binding);
        }
    }

    private void InitializeRootBindings()
    {
        rootContextSlotCount = 0;
        var allocated = new List<BindingStorage>();
        if (plannedBindingsByScopeId.TryGetValue(0, out var rootBindings))
        {
            for (var i = 0; i < rootBindings.Count; i++)
            {
                var binding = rootBindings[i];
                var register = binding.StorageKind switch
                {
                    CompilerPlannedStorageKind.ImportBinding => -1,
                    CompilerPlannedStorageKind.ContextSlot => -1,
                    _ => builder.AllocatePinnedRegister()
                };
                if (binding.StorageKind == CompilerPlannedStorageKind.ContextSlot)
                    rootContextSlotCount = Math.Max(rootContextSlotCount, binding.StorageIndex + 1);
                allocated.Add(new BindingStorage(binding, register));
            }
        }

        activeScopes.Clear();
        activeScopes.Push(new ActiveScope(0, allocated, rootContextSlotCount != 0));
    }
}
