namespace Okojo.JavaScript.Compiler;

internal abstract partial class JsCompilerBase
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
            BindingStorage? selfShadowCandidate = null;
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
                if (scope.Bindings[i].Planned.Kind == CompilerCollectedBindingKind.FunctionNameSelf)
                {
                    selfShadowCandidate ??= scope.Bindings[i];
                    continue;
                }
                binding = scope.Bindings[i];
                return true;
            }

            if (selfShadowCandidate is { } shadowed)
            {
                binding = shadowed;
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

    private readonly Stack<List<BindingStorage>> bindingStorageListPool = new();

    private List<BindingStorage> RentBindingStorageList(int capacity)
    {
        if (!bindingStorageListPool.TryPop(out var list))
            return new List<BindingStorage>(capacity);
        if (list.Capacity < capacity)
            list.Capacity = capacity;
        return list;
    }

    private void ReturnBindingStorageList(List<BindingStorage> list)
    {
        list.Clear();
        bindingStorageListPool.Push(list);
    }

    private void EnterScope(int scopeId)
    {
        var debugStartPc = builder.CodeLength;
        var bindings = GetPlannedBindings(scopeId);
        if (bindings.Length == 0)
        {
            activeScopes.Push(new ActiveScope(scopeId, [], 0, debugStartPc));
            return;
        }

        var contextSlotCount = 0;
        var allocated = RentBindingStorageList(bindings.Length);
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
        activeScopes.Push(new ActiveScope(scopeId, allocated, contextSlotCount, debugStartPc));
        EmitScopeLexicalHoleInitialization();
    }

    private void LeaveScope()
    {
        var scope = activeScopes.Pop();
        if (scope.ScopeId == 0)
            throw new InvalidOperationException("Cannot leave root scope.");

        EmitLocalDebugInfos(scope.Bindings, scope.DebugStartPc, builder.CodeLength);
        RemoveKnownInitializedLexicals(scope.Bindings);

        for (var i = 0; i < scope.Bindings.Count; i++)
            if (scope.Bindings[i].Register >= 0)
                builder.ReleaseTemporaryRegister(scope.Bindings[i].Register);

        if (scope.Bindings is List<BindingStorage> pooledList)
            ReturnBindingStorageList(pooledList);

        if (scope.HasContext)
            EmitPopContext();
    }

    private Dictionary<string, CapturedBindingAccess> childCaptureScratch = new(
        StringComparer.Ordinal
    );

    private Dictionary<string, CapturedBindingAccess> BuildChildCaptureBindings()
    {
        // Sequential child compiles consume this synchronously and copy what they
        // need, so one reusable dictionary avoids a fresh allocation per closure.
        var captures = childCaptureScratch;
        captures.Clear();
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
                    emittingParameterInitializers
                    && scope.ScopeId == 0
                    && binding.Planned.Kind
                        is not (
                            CompilerCollectedBindingKind.Parameter
                            or CompilerCollectedBindingKind.FunctionNameSelf
                            or CompilerCollectedBindingKind.Arguments
                        )
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
                var access = new CapturedBindingAccess(
                    binding.Planned.StorageIndex,
                    currentDepth,
                    binding.Planned.IsConst,
                    binding.Planned.Kind == CompilerCollectedBindingKind.FunctionNameSelf,
                    binding.Planned.StorageKind == CompilerPlannedStorageKind.ModuleBinding,
                    NeedsTdzWriteCheck(binding.Planned.Kind)
                );
                if (
                    captures.TryGetValue(binding.Planned.Name, out var existing)
                    && existing.IsImmutableFunctionName
                    && !access.IsImmutableFunctionName
                )
                    captures[binding.Planned.Name] = access;
                else
                    captures.TryAdd(binding.Planned.Name, access);
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
                    pair.Value.IsModuleVariable,
                    pair.Value.NeedsTdzWriteCheck
                )
            );

        if (derivedThisContextSlot >= 0)
            captures.TryAdd(
                DerivedThisBindingName,
                new CapturedBindingAccess(derivedThisContextSlot, 0, true, false, false, false)
            );

        return captures;
    }

    private static bool NeedsTdzWriteCheck(CompilerCollectedBindingKind kind) =>
        kind
            is CompilerCollectedBindingKind.Lexical
                or CompilerCollectedBindingKind.ClassDeclaration
                or CompilerCollectedBindingKind.BlockAlias
                or CompilerCollectedBindingKind.LoopHeadAlias
                or CompilerCollectedBindingKind.CatchAlias
                or CompilerCollectedBindingKind.ClassLexicalAlias;
}
