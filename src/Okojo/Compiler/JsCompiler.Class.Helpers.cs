namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private static string GetClassLexicalInternalName(string className, int position)
    {
        return $"$class_name_{className}_{position}";
    }

    private IReadOnlyDictionary<string, PrivateFieldBinding> MergeVisiblePrivateBindings(
        IReadOnlyDictionary<string, PrivateFieldBinding> ownBindings)
    {
        var outerVisibleBindings = GetVisiblePrivateNameBindings();
        if (outerVisibleBindings is null || outerVisibleBindings.Count == 0)
            return ownBindings;

        var merged = new Dictionary<string, PrivateFieldBinding>(outerVisibleBindings, StringComparer.Ordinal);
        foreach (var entry in ownBindings)
            merged[entry.Key] = entry.Value;
        return merged;
    }

    private static List<int>? CollectInheritedPrivateBrandIds(
        IReadOnlyDictionary<string, PrivateFieldBinding> visibleBindings,
        int instanceBrandId,
        int staticBrandId)
    {
        List<int>? inheritedBrandIds = null;
        foreach (var binding in visibleBindings.Values)
        {
            if (binding.BrandId == instanceBrandId || binding.BrandId == staticBrandId)
                continue;

            inheritedBrandIds ??= new(2);
            if (!inheritedBrandIds.Contains(binding.BrandId))
                inheritedBrandIds.Add(binding.BrandId);
        }

        return inheritedBrandIds;
    }

    private static IReadOnlyList<int>? CombinePrivateBrandIds(
        IReadOnlyList<int>? inheritedBrandIds,
        int extraBrandId)
    {
        if (extraBrandId == 0)
            return inheritedBrandIds;

        if (inheritedBrandIds is null || inheritedBrandIds.Count == 0)
            return new[] { extraBrandId };

        var combined = new List<int>(inheritedBrandIds.Count + 1);
        combined.Add(extraBrandId);
        for (var i = 0; i < inheritedBrandIds.Count; i++)
            if (inheritedBrandIds[i] != extraBrandId)
                combined.Add(inheritedBrandIds[i]);

        return combined;
    }

    private List<PrivateBrandSourceMapping>? CollectInheritedPrivateBrandMappingsFromActiveScopes(
        int instanceBrandId,
        int staticBrandId)
    {
        if (activeClassPrivateSourceScopes.Count == 0)
            return null;

        List<PrivateBrandSourceMapping>? mappings = null;
        foreach (var scope in activeClassPrivateSourceScopes)
        {
            if (scope.InstanceBrandId != 0 &&
                scope.InstanceBrandId != instanceBrandId &&
                scope.InstanceBrandId != staticBrandId)
            {
                mappings ??= new(2);
                if (!mappings.Exists(m => m.BrandId == scope.InstanceBrandId))
                    mappings.Add(new(scope.InstanceBrandId, scope.InstanceBrandSourceReg));
            }

            if (scope.StaticBrandId != 0 &&
                scope.StaticBrandId != instanceBrandId &&
                scope.StaticBrandId != staticBrandId)
            {
                mappings ??= new(2);
                if (!mappings.Exists(m => m.BrandId == scope.StaticBrandId))
                    mappings.Add(new(scope.StaticBrandId, scope.StaticBrandSourceReg));
            }
        }

        return mappings;
    }

    private static IReadOnlyList<PrivateBrandSourceMapping>? CreateCurrentClassPrivateBrandMappings(
        int instanceBrandId,
        int instanceBrandSourceReg,
        int staticBrandId,
        int staticBrandSourceReg)
    {
        List<PrivateBrandSourceMapping>? mappings = null;
        if (instanceBrandId != 0 && instanceBrandSourceReg >= 0)
        {
            mappings ??= new(2);
            mappings.Add(new(instanceBrandId, instanceBrandSourceReg));
        }

        if (staticBrandId != 0 && staticBrandSourceReg >= 0)
        {
            mappings ??= new(2);
            mappings.Add(new(staticBrandId, staticBrandSourceReg));
        }

        return mappings;
    }

    private static IReadOnlyList<PrivateBrandSourceMapping>? CombinePrivateBrandMappings(
        IReadOnlyList<PrivateBrandSourceMapping>? inheritedMappings,
        IReadOnlyList<PrivateBrandSourceMapping>? currentClassMappings)
    {
        if (inheritedMappings is null || inheritedMappings.Count == 0)
            return currentClassMappings;
        if (currentClassMappings is null || currentClassMappings.Count == 0)
            return inheritedMappings;

        var combined = new List<PrivateBrandSourceMapping>(currentClassMappings.Count + inheritedMappings.Count);
        for (var i = 0; i < currentClassMappings.Count; i++)
            combined.Add(currentClassMappings[i]);

        for (var i = 0; i < inheritedMappings.Count; i++)
        {
            var mapping = inheritedMappings[i];
            if (!combined.Exists(m => m.BrandId == mapping.BrandId))
                combined.Add(mapping);
        }

        return combined;
    }
}
