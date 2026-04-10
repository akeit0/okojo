namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private int GetOrCreateSymbolId(string name)
    {
        if (identifierTable is not null && identifierTable.TryGetIdentifierId(name, out var identifierId))
            return CompilerSymbolId.FromSourceIdentifier(identifierId).Value;
        if (syntheticSymbolIdsByName.TryGetValue(name, out var existingSymbolId))
            return existingSymbolId;

        var symbolId = CompilerSymbolId.CreateCompilerSynthetic(nextSyntheticSymbolOrdinal++).Value;
        symbolNamesById[symbolId] = name;
        syntheticSymbolIdsByName[name] = symbolId;
        return symbolId;
    }

    private bool TryGetSymbolId(string name, out int symbolId)
    {
        if (identifierTable is not null && identifierTable.TryGetIdentifierId(name, out symbolId))
        {
            symbolId = CompilerSymbolId.FromSourceIdentifier(symbolId).Value;
            return true;
        }

        if (syntheticSymbolIdsByName.TryGetValue(name, out symbolId))
            return true;

        symbolId = default;
        return false;
    }

    private int GetOrCreateSymbolId(CompilerIdentifierName identifier)
    {
        if (identifier.NameId >= 0)
            return CompilerSymbolId.FromSourceIdentifier(identifier.NameId).Value;

        return GetOrCreateSymbolId(identifier.Name);
    }

    private string GetSymbolName(int symbolId)
    {
        if (symbolId == SyntheticArgumentsSymbolId)
            return SyntheticArgumentsBindingName;
        if (symbolId == DerivedThisSymbolId)
            return DerivedThisInternalBindingName;
        if (symbolId == SuperBaseSymbolId)
            return SuperBaseInternalBindingName;
        if (CompilerSymbolId.IsSourceIdentifier(symbolId) && identifierTable is not null)
            return identifierTable.GetIdentifierLiteral(CompilerSymbolId.GetSourceIdentifierId(symbolId));
        if (symbolNamesById.TryGetValue(symbolId, out var name))
            return name;
        return $"<symbol:{symbolId}>";
    }

    private bool TryGetLocalBindingInfo(int symbolId, out LocalBindingInfo info)
    {
        return localBindingInfoById.TryGetValue(symbolId, out info);
    }

    private bool TryGetResolvedAliasSymbolId(CompilerIdentifierName identifier, out int symbolId,
        out string resolvedName)
    {
        foreach (var scope in activeBlockLexicalAliases)
        {
            var bindings = scope.Bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (!binding.Matches(identifier))
                    continue;

                if (scope.Inherited)
                {
                    if (identifier.NameId >= 0)
                    {
                        var sourceSymbolId = CompilerSymbolId.FromSourceIdentifier(identifier.NameId).Value;
                        if (locals.ContainsKey(sourceSymbolId))
                        {
                            symbolId = sourceSymbolId;
                            resolvedName = identifier.Name;
                            return true;
                        }
                    }
                    else if (TryGetSymbolId(identifier.Name, out var directSymbolId) &&
                             locals.ContainsKey(directSymbolId))
                    {
                        symbolId = directSymbolId;
                        resolvedName = identifier.Name;
                        return true;
                    }

                    if (!TryGetSymbolId(binding.InternalName, out var inheritedInternalSymbolId) ||
                        !locals.ContainsKey(inheritedInternalSymbolId))
                        continue;

                    symbolId = inheritedInternalSymbolId;
                    resolvedName = binding.InternalName;
                    return true;
                }

                if (!TryGetSymbolId(binding.InternalName, out symbolId))
                    symbolId = binding.InternalSymbolId;
                resolvedName = binding.InternalName;
                return true;
            }
        }

        resolvedName = identifier.Name;
        if (TryGetMatchingSourceIdentifierSymbolId(identifier, out symbolId))
            return locals.ContainsKey(symbolId);

        if (TryGetSymbolId(identifier.Name, out symbolId))
            return locals.ContainsKey(symbolId);

        return false;
    }

    private bool TryGetMatchingSourceIdentifierSymbolId(CompilerIdentifierName identifier, out int symbolId)
    {
        if (identifier.NameId >= 0 &&
            identifierTable is not null &&
            identifierTable.TryGetIdentifierId(identifier.Name, out var identifierId) &&
            identifierId == identifier.NameId)
        {
            symbolId = CompilerSymbolId.FromSourceIdentifier(identifier.NameId).Value;
            return true;
        }

        symbolId = default;
        return false;
    }

    private int GetOrCreateResolvedAliasSymbolId(CompilerIdentifierName identifier, out string resolvedName)
    {
        if (TryGetResolvedAliasSymbolId(identifier, out var symbolId, out resolvedName))
            return symbolId;

        resolvedName = identifier.Name;
        return GetOrCreateSymbolId(identifier.Name);
    }

    private bool TryGetResolvedAliasSymbolId(string sourceName, out int symbolId, out string resolvedName)
    {
        return TryGetResolvedAliasSymbolId(new CompilerIdentifierName(sourceName), out symbolId, out resolvedName);
    }

    private bool TryResolveLocalBinding(CompilerIdentifierName identifier, out ResolvedLocalBinding binding)
    {
        if (TryGetResolvedAliasSymbolId(identifier, out var symbolId, out var resolvedName))
        {
            binding = new(symbolId, resolvedName);
            return true;
        }

        binding = default;
        return false;
    }

    private bool TryResolveLocalBinding(string sourceName, out ResolvedLocalBinding binding)
    {
        return TryResolveLocalBinding(new CompilerIdentifierName(sourceName), out binding);
    }

    private bool IsKnownInitializedLexical(string resolvedName)
    {
        return TryGetSymbolId(resolvedName, out var symbolId) && knownInitializedLexicals.Contains(symbolId);
    }

    private bool IsKnownInitializedLexical(int symbolId)
    {
        return knownInitializedLexicals.Contains(symbolId);
    }

    private void MarkKnownInitializedLexical(string resolvedName)
    {
        knownInitializedLexicals.Add(GetOrCreateSymbolId(resolvedName));
    }

    private void MarkKnownInitializedLexical(int symbolId)
    {
        knownInitializedLexicals.Add(symbolId);
    }

    private void UnmarkKnownInitializedLexical(string resolvedName)
    {
        if (TryGetSymbolId(resolvedName, out var symbolId))
            knownInitializedLexicals.Remove(symbolId);
    }

    private bool ShouldSkipLexicalRegisterHoleInit(string resolvedName)
    {
        return TryGetSymbolId(resolvedName, out var symbolId) && skipLexicalRegisterPrologueHoleInit.Contains(symbolId);
    }

    private bool ShouldSkipLexicalRegisterHoleInit(int symbolId)
    {
        return skipLexicalRegisterPrologueHoleInit.Contains(symbolId);
    }

    private void MarkSkipLexicalRegisterHoleInit(string resolvedName)
    {
        skipLexicalRegisterPrologueHoleInit.Add(GetOrCreateSymbolId(resolvedName));
    }

    private void MarkSkipLexicalRegisterHoleInit(int symbolId)
    {
        skipLexicalRegisterPrologueHoleInit.Add(symbolId);
    }

    private bool IsSwitchLexicalInternal(string resolvedName)
    {
        return TryGetSymbolId(resolvedName, out var symbolId) && switchLexicalInternalNames.Contains(symbolId);
    }

    private bool IsSwitchLexicalInternal(int symbolId)
    {
        return switchLexicalInternalNames.Contains(symbolId);
    }

    private void MarkSwitchLexicalInternal(string resolvedName)
    {
        switchLexicalInternalNames.Add(GetOrCreateSymbolId(resolvedName));
    }

    private void MarkSwitchLexicalInternal(int symbolId)
    {
        switchLexicalInternalNames.Add(symbolId);
    }

    private void MarkParameterBinding(string name)
    {
        AddBindingFlags(name, LocalBindingFlags.Parameter);
    }

    private void MarkParameterBinding(int symbolId)
    {
        AddBindingFlags(symbolId, LocalBindingFlags.Parameter);
    }

    private bool IsParameterLocalBinding(string name)
    {
        if (TryGetSymbolId(name, out var symbolId) && localBindingInfoById.TryGetValue(symbolId, out var info))
            return (info.Flags & LocalBindingFlags.Parameter) != 0;
        return false;
    }

    private bool IsParameterLocalBinding(int symbolId)
    {
        return TryGetLocalBindingInfo(symbolId, out var info) &&
               (info.Flags & LocalBindingFlags.Parameter) != 0;
    }

    private void ClearParameterBindingFlags()
    {
        if (localBindingInfoById.Count == 0)
            return;

        foreach (var key in localBindingInfoById.Keys.ToArray())
        {
            var info = localBindingInfoById[key];
            if ((info.Flags & LocalBindingFlags.Parameter) == 0)
                continue;
            info.Flags &= ~LocalBindingFlags.Parameter;
            localBindingInfoById[key] = info;
        }
    }

    private void MarkInitializedParameterBinding(string resolvedName)
    {
        initializedParameterBindingIds.Add(GetOrCreateSymbolId(resolvedName));
    }

    private void MarkInitializedParameterBinding(int symbolId)
    {
        initializedParameterBindingIds.Add(symbolId);
    }

    private bool IsInitializedParameterBinding(string resolvedName)
    {
        return TryGetSymbolId(resolvedName, out var symbolId) && initializedParameterBindingIds.Contains(symbolId);
    }

    private bool IsInitializedParameterBinding(int symbolId)
    {
        return initializedParameterBindingIds.Contains(symbolId);
    }

    private void ClearInitializedParameterBindings()
    {
        initializedParameterBindingIds.Clear();
    }

    private void MarkVarBinding(string name)
    {
        AddBindingFlags(name, LocalBindingFlags.Var);
    }

    private void MarkVarBinding(int symbolId)
    {
        AddBindingFlags(symbolId, LocalBindingFlags.Var);
    }

    private void MarkLexicalBinding(string name, bool isConst)
    {
        AddBindingFlags(name, LocalBindingFlags.Lexical);
        if (isConst) AddBindingFlags(name, LocalBindingFlags.Const);
    }

    private void MarkLexicalBinding(int symbolId, bool isConst)
    {
        AddBindingFlags(symbolId, LocalBindingFlags.Lexical);
        if (isConst) AddBindingFlags(symbolId, LocalBindingFlags.Const);
    }

    private void MarkCapturedByChildBinding(string name)
    {
        AddBindingFlags(name, LocalBindingFlags.CapturedByChild);
    }

    private void MarkCapturedByChildBinding(int symbolId)
    {
        if (!localBindingInfoById.TryGetValue(symbolId, out var info))
            info = new() { Register = -1, Flags = LocalBindingFlags.None };
        info.Flags |= LocalBindingFlags.CapturedByChild;
        localBindingInfoById[symbolId] = info;
    }

    private void MarkImmutableFunctionNameBinding(string name)
    {
        AddBindingFlags(name, LocalBindingFlags.Lexical | LocalBindingFlags.ImmutableFunctionName);
    }

    private void MarkImmutableFunctionNameBinding(int symbolId)
    {
        AddBindingFlags(symbolId, LocalBindingFlags.Lexical | LocalBindingFlags.ImmutableFunctionName);
    }


    private void SetLocalBindingRegister(string name, int register)
    {
        var symbolId = GetOrCreateSymbolId(name);
        SetLocalBindingRegister(symbolId, register);
    }

    private void SetLocalBindingRegister(int symbolId, int register)
    {
        if (!localBindingInfoById.TryGetValue(symbolId, out var info))
            info = default;
        info.Register = register;
        localBindingInfoById[symbolId] = info;
    }

    private void AddBindingFlags(string name, LocalBindingFlags flags)
    {
        var symbolId = GetOrCreateSymbolId(name);
        AddBindingFlags(symbolId, flags);
    }

    private void AddBindingFlags(int symbolId, LocalBindingFlags flags)
    {
        if (!localBindingInfoById.TryGetValue(symbolId, out var info))
            info = new() { Register = -1, Flags = LocalBindingFlags.None };
        info.Flags |= flags;
        localBindingInfoById[symbolId] = info;
    }

    private bool TryGetLocalRegister(string name, out int reg)
    {
        if (!TryGetSymbolId(name, out var symbolId) || !locals.TryGetValue(symbolId, out var localReg))
        {
            reg = -1;
            return false;
        }

        if (localBindingInfoById.TryGetValue(symbolId, out var info) && info.Register >= 0)
        {
            reg = info.Register;
            return true;
        }

        SetLocalBindingRegister(name, localReg);
        reg = localReg;
        return true;
    }

    private bool TryGetLocalRegister(int symbolId, out int reg)
    {
        if (!locals.TryGetValue(symbolId, out var localReg))
        {
            reg = -1;
            return false;
        }

        if (localBindingInfoById.TryGetValue(symbolId, out var info) && info.Register >= 0)
        {
            reg = info.Register;
            return true;
        }

        localBindingInfoById[symbolId] = new()
        {
            Register = localReg,
            Flags = localBindingInfoById.TryGetValue(symbolId, out info) ? info.Flags : LocalBindingFlags.None
        };
        reg = localReg;
        return true;
    }

    private bool IsConstLocalBinding(string name)
    {
        if (TryGetSymbolId(name, out var symbolId) && localBindingInfoById.TryGetValue(symbolId, out var info))
            return (info.Flags & LocalBindingFlags.Const) != 0;
        return false;
    }

    private bool IsConstLocalBinding(int symbolId)
    {
        return TryGetLocalBindingInfo(symbolId, out var info) &&
               (info.Flags & LocalBindingFlags.Const) != 0;
    }

    private bool IsVarLocalBinding(string name)
    {
        if (TryGetSymbolId(name, out var symbolId) && localBindingInfoById.TryGetValue(symbolId, out var info))
            return (info.Flags & LocalBindingFlags.Var) != 0;
        return false;
    }

    private bool IsVarLocalBinding(int symbolId)
    {
        return TryGetLocalBindingInfo(symbolId, out var info) &&
               (info.Flags & LocalBindingFlags.Var) != 0;
    }

    private bool IsLexicalLocalBinding(string name)
    {
        if (TryGetSymbolId(name, out var symbolId) && localBindingInfoById.TryGetValue(symbolId, out var info))
            return (info.Flags & LocalBindingFlags.Lexical) != 0;
        return false;
    }

    private bool IsLexicalLocalBinding(int symbolId)
    {
        return TryGetLocalBindingInfo(symbolId, out var info) &&
               (info.Flags & LocalBindingFlags.Lexical) != 0;
    }

    private bool IsCapturedByChildBinding(string name)
    {
        if (TryGetSymbolId(name, out var symbolId) && localBindingInfoById.TryGetValue(symbolId, out var info))
            return (info.Flags & LocalBindingFlags.CapturedByChild) != 0;
        return false;
    }

    private bool IsCapturedByChildBinding(int symbolId)
    {
        return TryGetLocalBindingInfo(symbolId, out var info) &&
               (info.Flags & LocalBindingFlags.CapturedByChild) != 0;
    }

    private bool IsImmutableFunctionNameBinding(string name)
    {
        if (TryGetSymbolId(name, out var symbolId) && localBindingInfoById.TryGetValue(symbolId, out var info))
            return (info.Flags & LocalBindingFlags.ImmutableFunctionName) != 0;
        return false;
    }

    private bool IsImmutableFunctionNameBinding(int symbolId)
    {
        return TryGetLocalBindingInfo(symbolId, out var info) &&
               (info.Flags & LocalBindingFlags.ImmutableFunctionName) != 0;
    }

    private bool HasCapturedByChildLocals()
    {
        foreach (var info in localBindingInfoById.Values)
            if ((info.Flags & LocalBindingFlags.CapturedByChild) != 0)
                return true;

        return false;
    }

    private void ClearCapturedByChildFlags()
    {
        if (localBindingInfoById.Count == 0)
            return;

        foreach (var key in localBindingInfoById.Keys.ToArray())
        {
            var info = localBindingInfoById[key];
            if ((info.Flags & LocalBindingFlags.CapturedByChild) == 0)
                continue;
            info.Flags &= ~LocalBindingFlags.CapturedByChild;
            localBindingInfoById[key] = info;
        }
    }

    private bool TryGetVisibleCurrentLocalBinding(
        string resolvedName,
        string sourceName,
        out int reg,
        out int slotIdx,
        out bool hasCurrentContextSlot,
        out bool isLexicalRegisterLocal,
        out bool isConstLocal)
    {
        slotIdx = -1;
        hasCurrentContextSlot = false;
        isLexicalRegisterLocal = false;
        isConstLocal = false;

        if (!TryGetLocalRegister(resolvedName, out reg))
            return false;
        if (!IsCurrentFunctionLocalVisible(resolvedName, sourceName))
            return false;

        hasCurrentContextSlot = TryGetCurrentContextSlot(resolvedName, out slotIdx);
        isLexicalRegisterLocal = IsLexicalRegisterLocal(resolvedName);
        isConstLocal = IsConstLocalBinding(resolvedName);
        return true;
    }

    private bool TryGetVisibleCurrentLocalBinding(
        int resolvedSymbolId,
        string sourceName,
        out int reg,
        out int slotIdx,
        out bool hasCurrentContextSlot,
        out bool isLexicalRegisterLocal,
        out bool isConstLocal)
    {
        slotIdx = -1;
        hasCurrentContextSlot = false;
        isLexicalRegisterLocal = false;
        isConstLocal = false;

        if (!TryGetLocalRegister(resolvedSymbolId, out reg))
            return false;
        if (!IsCurrentFunctionLocalVisible(resolvedSymbolId, sourceName))
            return false;

        hasCurrentContextSlot = TryGetCurrentContextSlot(resolvedSymbolId, out slotIdx);
        isLexicalRegisterLocal = IsLexicalRegisterLocal(resolvedSymbolId);
        isConstLocal = IsConstLocalBinding(resolvedSymbolId);
        return true;
    }

    private bool TryGetFastPathLocalRegister(
        string resolvedName,
        out int reg,
        out bool needsLexicalTdzReadCheck)
    {
        needsLexicalTdzReadCheck = false;
        if (!TryGetLocalRegister(resolvedName, out reg))
            return false;
        if (TryGetCurrentContextSlot(resolvedName, out _))
            return false;

        needsLexicalTdzReadCheck =
            IsLexicalRegisterLocal(resolvedName) && !IsKnownInitializedLexical(resolvedName);
        return true;
    }

    private bool TryGetFastPathLocalRegister(
        int resolvedSymbolId,
        out int reg,
        out bool needsLexicalTdzReadCheck)
    {
        needsLexicalTdzReadCheck = false;
        if (!TryGetLocalRegister(resolvedSymbolId, out reg))
            return false;
        if (TryGetCurrentContextSlot(resolvedSymbolId, out _))
            return false;

        needsLexicalTdzReadCheck =
            IsLexicalRegisterLocal(resolvedSymbolId) && !IsKnownInitializedLexical(resolvedSymbolId);
        return true;
    }

    private bool HasLocalBinding(string resolvedName)
    {
        return TryGetLocalRegister(resolvedName, out _);
    }

    private bool HasLocalBinding(int symbolId)
    {
        return TryGetLocalRegister(symbolId, out _);
    }

    private bool TryGetCurrentContextSlot(string resolvedName, out int slotIdx)
    {
        if (TryGetSymbolId(resolvedName, out var symbolId))
            return currentContextSlotById.TryGetValue(symbolId, out slotIdx);

        slotIdx = -1;
        return false;
    }

    private bool TryGetCurrentContextSlot(int symbolId, out int slotIdx)
    {
        return currentContextSlotById.TryGetValue(symbolId, out slotIdx);
    }

    private void EnsureCurrentContextSlotForLocal(string resolvedName)
    {
        var symbolId = GetOrCreateSymbolId(resolvedName);
        if (currentContextSlotById.ContainsKey(symbolId))
            return;
        currentContextSlotById[symbolId] = currentContextSlotById.Count;
    }

    private void EnsureCurrentContextSlotForLocal(int symbolId)
    {
        if (currentContextSlotById.ContainsKey(symbolId))
            return;
        currentContextSlotById[symbolId] = currentContextSlotById.Count;
    }

    private readonly record struct ResolvedLocalBinding(int SymbolId, string Name);

    [Flags]
    private enum LocalBindingFlags : byte
    {
        None = 0,
        Lexical = 1 << 0,
        Const = 1 << 1,
        Var = 1 << 2,
        Parameter = 1 << 3,
        CapturedByChild = 1 << 4,
        ImmutableFunctionName = 1 << 5
    }

    private struct LocalBindingInfo
    {
        public int Register;
        public LocalBindingFlags Flags;
    }
}
