namespace Okojo.Compiler.Experimental;

internal static class CompilerStoragePlanner
{
    public static CompilerBindingPlan Plan(CompilerBindingCollectionResult collected)
    {
        var planned = new PooledArrayBuilder<CompilerPlannedBinding>(collected.Bindings.Length == 0 ? 4 : collected.Bindings.Length);
        var nextStorageIndexByScopeId = new Dictionary<int, int>();
        var scopeById = CreateScopeById(collected.Scopes);
        var bindingsByScopeId = CreateBindingsByScopeId(collected.Bindings);
        var capturedBindingIndexes = CollectCapturedBindingIndexes(collected, scopeById, bindingsByScopeId);

        for (var bindingIndex = 0; bindingIndex < collected.Bindings.Length; bindingIndex++)
        {
            var binding = collected.Bindings[bindingIndex];
            var isCaptured = capturedBindingIndexes.Contains(bindingIndex);
            var storageKind = ClassifyStorage(binding.Kind);
            if (isCaptured && storageKind != CompilerPlannedStorageKind.ImportBinding)
                storageKind = CompilerPlannedStorageKind.ContextSlot;
            var storageIndex = storageKind == CompilerPlannedStorageKind.ImportBinding
                ? -1
                : GetNextStorageIndex(nextStorageIndexByScopeId, binding.ScopeId);
            planned.Add(new CompilerPlannedBinding(
                binding.ScopeId,
                binding.Name,
                binding.NameId,
                binding.Kind,
                storageKind,
                storageIndex,
                isCaptured,
                binding.IsConst,
                binding.Position));
        }

        return new(planned);
    }

    private static Dictionary<int, CompilerCollectedScope> CreateScopeById(ReadOnlySpan<CompilerCollectedScope> scopes)
    {
        var result = new Dictionary<int, CompilerCollectedScope>(scopes.Length);
        for (var i = 0; i < scopes.Length; i++)
            result[scopes[i].ScopeId] = scopes[i];
        return result;
    }

    private static Dictionary<int, List<int>> CreateBindingsByScopeId(ReadOnlySpan<CompilerCollectedBinding> bindings)
    {
        var result = new Dictionary<int, List<int>>();
        for (var i = 0; i < bindings.Length; i++)
        {
            var binding = bindings[i];
            if (!result.TryGetValue(binding.ScopeId, out var indexes))
            {
                indexes = [];
                result[binding.ScopeId] = indexes;
            }

            indexes.Add(i);
        }

        return result;
    }

    private static HashSet<int> CollectCapturedBindingIndexes(
        CompilerBindingCollectionResult collected,
        Dictionary<int, CompilerCollectedScope> scopeById,
        Dictionary<int, List<int>> bindingsByScopeId)
    {
        var captured = new HashSet<int>();
        for (var i = 0; i < collected.References.Length; i++)
        {
            var reference = collected.References[i];
            if (!TryResolveBindingIndex(reference, collected.Bindings, scopeById, bindingsByScopeId, out var bindingIndex))
                continue;

            var bindingScopeId = collected.Bindings[bindingIndex].ScopeId;
            if (HasInterveningFunctionScope(reference.ScopeId, bindingScopeId, scopeById))
                captured.Add(bindingIndex);
        }

        return captured;
    }

    private static bool TryResolveBindingIndex(
        CompilerCollectedReference reference,
        ReadOnlySpan<CompilerCollectedBinding> bindings,
        Dictionary<int, CompilerCollectedScope> scopeById,
        Dictionary<int, List<int>> bindingsByScopeId,
        out int bindingIndex)
    {
        for (var scopeId = reference.ScopeId; scopeId >= 0;)
        {
            if (bindingsByScopeId.TryGetValue(scopeId, out var indexes))
            {
                for (var i = indexes.Count - 1; i >= 0; i--)
                {
                    var candidateIndex = indexes[i];
                    if (!string.Equals(bindings[candidateIndex].Name, reference.Name, StringComparison.Ordinal))
                        continue;
                    bindingIndex = candidateIndex;
                    return true;
                }
            }

            if (!scopeById.TryGetValue(scopeId, out var scope))
                break;
            scopeId = scope.ParentScopeId;
        }

        bindingIndex = -1;
        return false;
    }

    private static bool HasInterveningFunctionScope(
        int referenceScopeId,
        int bindingScopeId,
        Dictionary<int, CompilerCollectedScope> scopeById)
    {
        for (var scopeId = referenceScopeId; scopeId >= 0 && scopeId != bindingScopeId;)
        {
            if (!scopeById.TryGetValue(scopeId, out var scope))
                break;
            if (scope.Kind == CompilerCollectedScopeKind.Function)
                return true;
            scopeId = scope.ParentScopeId;
        }

        return false;
    }

    private static CompilerPlannedStorageKind ClassifyStorage(CompilerCollectedBindingKind kind)
    {
        return kind switch
        {
            CompilerCollectedBindingKind.Var or CompilerCollectedBindingKind.FunctionDeclaration =>
                CompilerPlannedStorageKind.LocalRegister,
            CompilerCollectedBindingKind.Import => CompilerPlannedStorageKind.ImportBinding,
            CompilerCollectedBindingKind.Parameter or
                CompilerCollectedBindingKind.Lexical or
                CompilerCollectedBindingKind.ClassDeclaration or
                CompilerCollectedBindingKind.FunctionNameSelf or
                CompilerCollectedBindingKind.BlockAlias or
                CompilerCollectedBindingKind.LoopHeadAlias or
                CompilerCollectedBindingKind.CatchAlias or
                CompilerCollectedBindingKind.ClassLexicalAlias =>
                CompilerPlannedStorageKind.LexicalRegister,
            _ => CompilerPlannedStorageKind.LocalRegister
        };
    }

    private static int GetNextStorageIndex(Dictionary<int, int> nextStorageIndexByScopeId, int scopeId)
    {
        if (!nextStorageIndexByScopeId.TryGetValue(scopeId, out var nextIndex))
            nextIndex = 0;
        nextStorageIndexByScopeId[scopeId] = nextIndex + 1;
        return nextIndex;
    }
}
