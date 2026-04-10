namespace Okojo.Compiler.Experimental;

internal sealed partial class JsPlannedFunctionCompiler
{
    private bool TryResolveBinding(string name, out BindingStorage binding)
    {
        return TryResolveBindingAccess(name, out binding, out _);
    }

    private bool TryResolveBindingAccess(string name, out BindingStorage binding, out int contextDepth)
    {
        contextDepth = 0;
        foreach (var scope in activeScopes)
        {
            for (var i = 0; i < scope.Bindings.Count; i++)
            {
                if (!string.Equals(scope.Bindings[i].Planned.Name, name, StringComparison.Ordinal))
                    continue;
                binding = scope.Bindings[i];
                return true;
            }

            if (scope.HasContext)
                contextDepth++;
        }

        binding = default;
        contextDepth = 0;
        return false;
    }

    private CompilerCollectedScope FindChildScope(int parentScopeId, CompilerCollectedScopeKind kind, int position)
    {
        if (!scopesByParentScopeId.TryGetValue(parentScopeId, out var children))
            throw new InvalidOperationException($"No child scopes found for parent scope {parentScopeId}.");

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.Kind == kind && child.Position == position)
                return child;
        }

        throw new InvalidOperationException($"No child scope found for {kind} at position {position}.");
    }

    private void EnterScope(int scopeId)
    {
        if (!plannedBindingsByScopeId.TryGetValue(scopeId, out var bindings))
        {
            activeScopes.Push(new ActiveScope(scopeId, [], false));
            return;
        }

        var hasContext = false;
        var contextSlotCount = 0;
        var allocated = new List<BindingStorage>(bindings.Count);
        for (var i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            var register = binding.StorageKind switch
            {
                CompilerPlannedStorageKind.ImportBinding => -1,
                CompilerPlannedStorageKind.ContextSlot => -1,
                _ => builder.AllocateTemporaryRegister()
            };
            if (binding.StorageKind == CompilerPlannedStorageKind.ContextSlot)
            {
                hasContext = true;
                contextSlotCount = Math.Max(contextSlotCount, binding.StorageIndex + 1);
            }
            allocated.Add(new BindingStorage(binding, register));
        }

        if (hasContext)
            EmitCreateFunctionContextWithCells(contextSlotCount);
        activeScopes.Push(new ActiveScope(scopeId, allocated, hasContext));
    }

    private void LeaveScope()
    {
        var scope = activeScopes.Pop();
        if (scope.ScopeId == 0)
            throw new InvalidOperationException("Cannot leave root scope.");

        for (var i = 0; i < scope.Bindings.Count; i++)
            if (scope.Bindings[i].Register >= 0)
                builder.ReleaseTemporaryRegister(scope.Bindings[i].Register);

        if (scope.HasContext)
            EmitPopContext();
    }

    private Dictionary<string, CapturedBindingAccess> BuildChildCaptureBindings()
    {
        var captures = new Dictionary<string, CapturedBindingAccess>(StringComparer.Ordinal);
        var currentDepth = 0;
        foreach (var scope in activeScopes)
        {
            for (var i = 0; i < scope.Bindings.Count; i++)
            {
                var binding = scope.Bindings[i];
                if (binding.Planned.StorageKind != CompilerPlannedStorageKind.ContextSlot)
                    continue;
                captures.TryAdd(binding.Planned.Name,
                    new CapturedBindingAccess(binding.Planned.StorageIndex, currentDepth));
            }

            if (scope.HasContext)
                currentDepth++;
        }

        foreach (var pair in inheritedCaptures)
            captures.TryAdd(pair.Key, new CapturedBindingAccess(pair.Value.Slot, pair.Value.Depth + currentDepth));

        return captures;
    }

    private int GetInheritedCaptureDepth(CapturedBindingAccess inherited)
    {
        var currentDepth = 0;
        foreach (var scope in activeScopes)
            if (scope.HasContext)
                currentDepth++;
        return inherited.Depth + currentDepth;
    }

    private readonly record struct ActiveScope(int ScopeId, IReadOnlyList<BindingStorage> Bindings, bool HasContext);
}
