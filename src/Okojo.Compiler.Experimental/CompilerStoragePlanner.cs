using System.Buffers;

namespace Okojo.JavaScript.Compiler.Experimental;

internal static class CompilerStoragePlanner
{
    public static CompilerBindingPlan Plan(CompilerBindingCollectionResult collected)
    {
        var scopes = collected.Scopes;
        var bindings = collected.Bindings;
        ValidateDenseScopeIds(scopes);

        var scopeCount = Math.Max(1, scopes.Length);
        var bindingCount = bindings.Length;
        var firstBindingByScope = ArrayPool<int>.Shared.Rent(scopeCount);
        var nextBinding = ArrayPool<int>.Shared.Rent(Math.Max(1, bindingCount));
        var nextContextSlotByScope = ArrayPool<int>.Shared.Rent(scopeCount);
        var captured = ArrayPool<bool>.Shared.Rent(Math.Max(1, bindingCount));
        var hasArgumentsBinding = ArrayPool<bool>.Shared.Rent(scopeCount);

        Array.Fill(firstBindingByScope, -1, 0, scopeCount);
        Array.Clear(nextContextSlotByScope, 0, scopeCount);
        Array.Clear(captured, 0, bindingCount);
        Array.Clear(hasArgumentsBinding, 0, scopeCount);

        try
        {
            IndexBindings(bindings, firstBindingByScope, nextBinding, scopeCount);
            for (var i = 0; i < bindingCount; i++)
                if (bindings[i].Kind == CompilerCollectedBindingKind.Arguments)
                    hasArgumentsBinding[bindings[i].ScopeId] = true;
            MarkCapturedBindings(
                collected.References,
                scopes,
                bindings,
                firstBindingByScope,
                nextBinding,
                captured
            );

            var planned = new PooledArrayBuilder<CompilerPlannedBinding>(
                bindingCount == 0 ? 4 : bindingCount
            );
            for (var bindingIndex = 0; bindingIndex < bindingCount; bindingIndex++)
            {
                var binding = bindings[bindingIndex];
                var storageKind =
                    binding.Kind == CompilerCollectedBindingKind.Parameter
                    && hasArgumentsBinding[binding.ScopeId]
                        ? CompilerPlannedStorageKind.ContextSlot
                        : ClassifyStorage(binding, scopes[binding.ScopeId].Kind);
                if (
                    captured[bindingIndex]
                    && storageKind
                        is not (
                            CompilerPlannedStorageKind.ImportBinding
                            or CompilerPlannedStorageKind.GlobalBinding
                        )
                )
                    storageKind = CompilerPlannedStorageKind.ContextSlot;
                var storageIndex =
                    storageKind == CompilerPlannedStorageKind.ContextSlot
                        ? nextContextSlotByScope[binding.ScopeId]++
                        : -1;
                planned.Add(
                    new CompilerPlannedBinding(
                        binding.ScopeId,
                        binding.Name,
                        binding.NameId,
                        binding.Kind,
                        storageKind,
                        storageIndex,
                        captured[bindingIndex],
                        binding.IsConst,
                        binding.Position
                    )
                );
            }

            return new(planned);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(firstBindingByScope);
            ArrayPool<int>.Shared.Return(nextBinding);
            ArrayPool<int>.Shared.Return(nextContextSlotByScope);
            ArrayPool<bool>.Shared.Return(captured);
            ArrayPool<bool>.Shared.Return(hasArgumentsBinding);
        }
    }

    private static void ValidateDenseScopeIds(ReadOnlySpan<CompilerCollectedScope> scopes)
    {
        for (var i = 0; i < scopes.Length; i++)
            if (scopes[i].ScopeId != i)
                throw new InvalidOperationException(
                    $"Compiler scope IDs must be dense; expected {i}, found {scopes[i].ScopeId}."
                );
    }

    private static void IndexBindings(
        ReadOnlySpan<CompilerCollectedBinding> bindings,
        int[] firstBindingByScope,
        int[] nextBinding,
        int scopeCount
    )
    {
        for (var i = 0; i < bindings.Length; i++)
        {
            var scopeId = bindings[i].ScopeId;
            if ((uint)scopeId >= (uint)scopeCount)
                throw new InvalidOperationException(
                    $"Binding '{bindings[i].Name}' references invalid scope {scopeId}."
                );
            nextBinding[i] = firstBindingByScope[scopeId];
            firstBindingByScope[scopeId] = i;
        }
    }

    private static void MarkCapturedBindings(
        ReadOnlySpan<CompilerCollectedReference> references,
        ReadOnlySpan<CompilerCollectedScope> scopes,
        ReadOnlySpan<CompilerCollectedBinding> bindings,
        int[] firstBindingByScope,
        int[] nextBinding,
        bool[] captured
    )
    {
        for (var i = 0; i < references.Length; i++)
        {
            var reference = references[i];
            if (
                !TryResolveBindingIndex(
                    reference,
                    scopes,
                    bindings,
                    firstBindingByScope,
                    nextBinding,
                    out var bindingIndex
                )
            )
                continue;

            if (
                HasInterveningFunctionScope(
                    reference.ScopeId,
                    bindings[bindingIndex].ScopeId,
                    scopes
                )
            )
                captured[bindingIndex] = true;
        }
    }

    private static bool TryResolveBindingIndex(
        CompilerCollectedReference reference,
        ReadOnlySpan<CompilerCollectedScope> scopes,
        ReadOnlySpan<CompilerCollectedBinding> bindings,
        int[] firstBindingByScope,
        int[] nextBinding,
        out int bindingIndex
    )
    {
        for (var scopeId = reference.ScopeId; scopeId >= 0; scopeId = scopes[scopeId].ParentScopeId)
        {
            if ((uint)scopeId >= (uint)scopes.Length)
                break;
            for (
                var candidate = firstBindingByScope[scopeId];
                candidate >= 0;
                candidate = nextBinding[candidate]
            )
            {
                if (
                    scopeId == reference.ExcludedBodyScopeId
                    && bindings[candidate].Kind
                        is not (
                            CompilerCollectedBindingKind.Parameter
                            or CompilerCollectedBindingKind.FunctionNameSelf
                        )
                )
                    continue;
                if (
                    string.Equals(
                        bindings[candidate].Name,
                        reference.Name,
                        StringComparison.Ordinal
                    )
                )
                {
                    bindingIndex = candidate;
                    return true;
                }
            }
        }

        bindingIndex = -1;
        return false;
    }

    private static bool HasInterveningFunctionScope(
        int referenceScopeId,
        int bindingScopeId,
        ReadOnlySpan<CompilerCollectedScope> scopes
    )
    {
        for (
            var scopeId = referenceScopeId;
            scopeId >= 0 && scopeId != bindingScopeId;
            scopeId = scopes[scopeId].ParentScopeId
        )
        {
            if ((uint)scopeId >= (uint)scopes.Length)
                break;
            if (scopes[scopeId].Kind == CompilerCollectedScopeKind.Function)
                return true;
        }

        return false;
    }

    private static CompilerPlannedStorageKind ClassifyStorage(
        CompilerCollectedBinding binding,
        CompilerCollectedScopeKind scopeKind
    )
    {
        if (scopeKind == CompilerCollectedScopeKind.Program)
            return binding.Kind switch
            {
                CompilerCollectedBindingKind.Var
                or CompilerCollectedBindingKind.FunctionDeclaration =>
                    CompilerPlannedStorageKind.GlobalBinding,
                CompilerCollectedBindingKind.Lexical
                or CompilerCollectedBindingKind.ClassDeclaration =>
                    CompilerPlannedStorageKind.ContextSlot,
                _ => ClassifyLocalStorage(binding.Kind),
            };
        return ClassifyLocalStorage(binding.Kind);
    }

    private static CompilerPlannedStorageKind ClassifyLocalStorage(
        CompilerCollectedBindingKind kind
    )
    {
        return kind switch
        {
            CompilerCollectedBindingKind.Var or CompilerCollectedBindingKind.FunctionDeclaration =>
                CompilerPlannedStorageKind.LocalRegister,
            CompilerCollectedBindingKind.Arguments => CompilerPlannedStorageKind.LocalRegister,
            CompilerCollectedBindingKind.Import => CompilerPlannedStorageKind.ImportBinding,
            CompilerCollectedBindingKind.Parameter
            or CompilerCollectedBindingKind.Lexical
            or CompilerCollectedBindingKind.ClassDeclaration
            or CompilerCollectedBindingKind.FunctionNameSelf
            or CompilerCollectedBindingKind.BlockAlias
            or CompilerCollectedBindingKind.LoopHeadAlias
            or CompilerCollectedBindingKind.CatchAlias
            or CompilerCollectedBindingKind.ClassLexicalAlias =>
                CompilerPlannedStorageKind.LexicalRegister,
            _ => CompilerPlannedStorageKind.LocalRegister,
        };
    }
}
