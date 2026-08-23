namespace Okojo.JavaScript.Compiler.Experimental;

internal abstract partial class JsPlannedCompilerBase
{
    protected void InitializePlanIndexes(
        CompilerBindingCollectionResult collected,
        CompilerBindingPlan plan
    )
    {
        var scopeCount = collected.Scopes.Length;
        plannedBindingOffsets = new int[scopeCount];
        plannedBindingCounts = new int[scopeCount];
        childScopeOffsets = new int[scopeCount];
        childScopeCounts = new int[scopeCount];

        foreach (var binding in plan.Bindings)
        {
            if ((uint)binding.ScopeId >= (uint)scopeCount)
                throw new InvalidOperationException(
                    $"Planned binding '{binding.Name}' references invalid scope {binding.ScopeId}."
                );
            plannedBindingCounts[binding.ScopeId]++;
        }

        foreach (var scope in collected.Scopes)
            if (scope.ParentScopeId >= 0)
                childScopeCounts[scope.ParentScopeId]++;

        var bindingTotal = 0;
        var childTotal = 0;
        for (var scopeId = 0; scopeId < scopeCount; scopeId++)
        {
            plannedBindingOffsets[scopeId] = bindingTotal;
            bindingTotal += plannedBindingCounts[scopeId];
            childScopeOffsets[scopeId] = childTotal;
            childTotal += childScopeCounts[scopeId];
        }

        plannedBindings = new CompilerPlannedBinding[bindingTotal];
        childScopes = new CompilerCollectedScope[childTotal];
        var bindingCursors = (int[])plannedBindingOffsets.Clone();
        var childCursors = (int[])childScopeOffsets.Clone();
        foreach (var binding in plan.Bindings)
            plannedBindings[bindingCursors[binding.ScopeId]++] = binding;
        foreach (var scope in collected.Scopes)
            if (scope.ParentScopeId >= 0)
                childScopes[childCursors[scope.ParentScopeId]++] = scope;
    }

    protected void InitializeRootBindings(
        IReadOnlyDictionary<string, int>? preallocatedParameterRegisters = null
    )
    {
        rootContextSlotCount = 0;
        var allocated = new List<BindingStorage>();
        var rootBindings = GetPlannedBindings(0);
        for (var i = 0; i < rootBindings.Length; i++)
        {
            var binding = rootBindings[i];
            var register = binding.StorageKind switch
            {
                CompilerPlannedStorageKind.ImportBinding => -1,
                CompilerPlannedStorageKind.ContextSlot => -1,
                _ when binding.Kind == CompilerCollectedBindingKind.Parameter
                        && preallocatedParameterRegisters is not null
                        && preallocatedParameterRegisters.TryGetValue(
                            binding.Name,
                            out var parameterRegister
                        ) => parameterRegister,
                _ => builder.AllocatePinnedRegister(),
            };
            if (binding.StorageKind == CompilerPlannedStorageKind.ContextSlot)
                rootContextSlotCount = Math.Max(rootContextSlotCount, binding.StorageIndex + 1);
            allocated.Add(new BindingStorage(binding, register));
        }

        activeScopes.Clear();
        activeScopes.Push(new ActiveScope(0, allocated, rootContextSlotCount));
    }

    private ReadOnlySpan<CompilerPlannedBinding> GetPlannedBindings(int scopeId)
    {
        if ((uint)scopeId >= (uint)plannedBindingOffsets.Length)
            return [];
        return plannedBindings.AsSpan(
            plannedBindingOffsets[scopeId],
            plannedBindingCounts[scopeId]
        );
    }

    private ReadOnlySpan<CompilerCollectedScope> GetChildScopes(int parentScopeId)
    {
        if ((uint)parentScopeId >= (uint)childScopeOffsets.Length)
            return [];
        return childScopes.AsSpan(
            childScopeOffsets[parentScopeId],
            childScopeCounts[parentScopeId]
        );
    }
}
