using System.Buffers;

namespace Okojo.JavaScript.Compiler;

internal abstract partial class JsCompilerBase
{
    protected void InitializePlanIndexes(
        CompilerBindingCollectionResult collected,
        CompilerBindingPlan plan
    )
    {
        ReleasePlanStorage();
        var scopeCount = collected.Scopes.Length;
        planScopeCount = scopeCount;
        plannedBindingOffsets = ArrayPool<int>.Shared.Rent(Math.Max(1, scopeCount));
        plannedBindingCounts = ArrayPool<int>.Shared.Rent(Math.Max(1, scopeCount));
        childScopeOffsets = ArrayPool<int>.Shared.Rent(Math.Max(1, scopeCount));
        childScopeCounts = ArrayPool<int>.Shared.Rent(Math.Max(1, scopeCount));
        Array.Clear(plannedBindingOffsets, 0, scopeCount);
        Array.Clear(plannedBindingCounts, 0, scopeCount);
        Array.Clear(childScopeOffsets, 0, scopeCount);
        Array.Clear(childScopeCounts, 0, scopeCount);

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

        plannedBindings = ArrayPool<CompilerPlannedBinding>.Shared.Rent(Math.Max(1, bindingTotal));
        childScopes = ArrayPool<CompilerCollectedScope>.Shared.Rent(Math.Max(1, childTotal));
        foreach (var binding in plan.Bindings)
            plannedBindings[plannedBindingOffsets[binding.ScopeId]++] = binding;
        foreach (var scope in collected.Scopes)
            if (scope.ParentScopeId >= 0)
                childScopes[childScopeOffsets[scope.ParentScopeId]++] = scope;

        for (var scopeId = 0; scopeId < scopeCount; scopeId++)
        {
            plannedBindingOffsets[scopeId] -= plannedBindingCounts[scopeId];
            childScopeOffsets[scopeId] -= childScopeCounts[scopeId];
        }
    }

    protected void InitializeRootBindings(
        IReadOnlyDictionary<string, int>? preallocatedParameterRegisters = null
    )
    {
        rootContextSlotCount = 0;
        var rootBindings = GetPlannedBindings(0);
        var allocated = new BindingStorage[rootBindings.Length];
        for (var i = 0; i < rootBindings.Length; i++)
        {
            var binding = rootBindings[i];
            var register = binding.StorageKind switch
            {
                CompilerPlannedStorageKind.ImportBinding => -1,
                CompilerPlannedStorageKind.ModuleBinding => -1,
                CompilerPlannedStorageKind.ContextSlot => -1,
                CompilerPlannedStorageKind.GlobalBinding => -1,
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
            allocated[i] = new BindingStorage(binding, register);
        }

        activeScopes.Clear();
        activeScopes.Push(new ActiveScope(0, allocated, rootContextSlotCount));
    }

    protected ReadOnlySpan<CompilerPlannedBinding> GetPlannedBindings(int scopeId)
    {
        if ((uint)scopeId >= (uint)planScopeCount)
            return [];
        return plannedBindings.AsSpan(
            plannedBindingOffsets[scopeId],
            plannedBindingCounts[scopeId]
        );
    }

    private ReadOnlySpan<CompilerCollectedScope> GetChildScopes(int parentScopeId)
    {
        if ((uint)parentScopeId >= (uint)planScopeCount)
            return [];
        return childScopes.AsSpan(
            childScopeOffsets[parentScopeId],
            childScopeCounts[parentScopeId]
        );
    }

    protected void ReleasePlanStorage()
    {
        if (plannedBindings.Length != 0)
            ArrayPool<CompilerPlannedBinding>.Shared.Return(plannedBindings, clearArray: true);
        if (childScopes.Length != 0)
            ArrayPool<CompilerCollectedScope>.Shared.Return(childScopes);
        if (plannedBindingOffsets.Length != 0)
            ArrayPool<int>.Shared.Return(plannedBindingOffsets);
        if (plannedBindingCounts.Length != 0)
            ArrayPool<int>.Shared.Return(plannedBindingCounts);
        if (childScopeOffsets.Length != 0)
            ArrayPool<int>.Shared.Return(childScopeOffsets);
        if (childScopeCounts.Length != 0)
            ArrayPool<int>.Shared.Return(childScopeCounts);

        plannedBindings = [];
        childScopes = [];
        plannedBindingOffsets = [];
        plannedBindingCounts = [];
        childScopeOffsets = [];
        childScopeCounts = [];
        planScopeCount = 0;
    }

    protected void ReleaseCompilerStorage()
    {
        ReleasePlanStorage();
        Vm.ReturnCompileStack(activeScopes);
        Vm.ReturnCompileStack(controlScopes);
    }
}
