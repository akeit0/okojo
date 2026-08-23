namespace Okojo.JavaScript.Compiler.Experimental;

internal abstract partial class JsPlannedCompilerBase
{
    protected int CurrentContextDepth
    {
        get
        {
            var depth = 0;
            foreach (var scope in activeScopes)
                if (scope.HasContext)
                    depth++;
            return depth;
        }
    }

    protected bool TryResolveBinding(string name, out BindingStorage binding)
    {
        return TryResolveBindingAccess(name, out binding, out _);
    }

    private bool TryResolveBindingAccess(
        string name,
        out BindingStorage binding,
        out int contextDepth
    )
    {
        contextDepth = 0;
        foreach (var scope in activeScopes)
        {
            for (var i = 0; i < scope.Bindings.Count; i++)
            {
                if (emittingInstanceFieldInitializer && scope.ScopeId == 0)
                    continue;
                if (
                    emittingParameterInitializers
                    && scope.ScopeId == 0
                    && scope.Bindings[i].Planned.Kind
                        is not (
                            CompilerCollectedBindingKind.Parameter
                            or CompilerCollectedBindingKind.FunctionNameSelf
                            or CompilerCollectedBindingKind.Arguments
                        )
                )
                    continue;
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

    private CompilerCollectedScope FindChildScope(
        int parentScopeId,
        CompilerCollectedScopeKind kind,
        int position
    )
    {
        var children = GetChildScopes(parentScopeId);
        if (children.Length == 0)
            throw new InvalidOperationException(
                $"No child scopes found for parent scope {parentScopeId}."
            );

        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            if (child.Kind == kind && child.Position == position)
                return child;
        }

        throw new InvalidOperationException(
            $"No child scope found for {kind} at position {position}."
        );
    }

    private void EnterScope(int scopeId)
    {
        var bindings = GetPlannedBindings(scopeId);
        if (bindings.Length == 0)
        {
            activeScopes.Push(new ActiveScope(scopeId, [], 0));
            return;
        }

        var contextSlotCount = 0;
        var allocated = new List<BindingStorage>(bindings.Length);
        for (var i = 0; i < bindings.Length; i++)
        {
            var binding = bindings[i];
            var register = binding.StorageKind switch
            {
                CompilerPlannedStorageKind.ImportBinding => -1,
                CompilerPlannedStorageKind.ModuleBinding => -1,
                CompilerPlannedStorageKind.ContextSlot => -1,
                CompilerPlannedStorageKind.GlobalBinding => -1,
                _ => builder.AllocateTemporaryRegister(),
            };
            if (binding.StorageKind == CompilerPlannedStorageKind.ContextSlot)
            {
                contextSlotCount = Math.Max(contextSlotCount, binding.StorageIndex + 1);
            }
            allocated.Add(new BindingStorage(binding, register));
        }

        if (contextSlotCount != 0)
            EmitCreateFunctionContextWithCells(contextSlotCount);
        activeScopes.Push(new ActiveScope(scopeId, allocated, contextSlotCount));
        EmitScopeLexicalHoleInitialization();
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
                if (
                    emittingInstanceFieldInitializer
                    && scope.ScopeId == 0
                    && binding.Planned.Kind != CompilerCollectedBindingKind.SuperBase
                )
                    continue;
                if (
                    binding.Planned.StorageKind
                    is not (
                        CompilerPlannedStorageKind.ContextSlot
                        or CompilerPlannedStorageKind.ModuleBinding
                    )
                )
                    continue;
                captures.TryAdd(
                    binding.Planned.Name,
                    new CapturedBindingAccess(
                        binding.Planned.StorageIndex,
                        currentDepth,
                        binding.Planned.IsConst,
                        binding.Planned.Kind == CompilerCollectedBindingKind.FunctionNameSelf,
                        binding.Planned.StorageKind == CompilerPlannedStorageKind.ModuleBinding
                    )
                );
            }

            if (scope.HasContext)
                currentDepth++;
        }

        foreach (var pair in ExternalCaptures)
            captures.TryAdd(
                pair.Key,
                new CapturedBindingAccess(
                    pair.Value.Slot,
                    pair.Value.Depth + currentDepth + ExternalCaptureContextDepthOffset,
                    pair.Value.IsConst,
                    pair.Value.IsImmutableFunctionName,
                    pair.Value.IsModuleVariable
                )
            );

        return captures;
    }
}
