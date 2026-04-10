using System.Runtime.InteropServices;
using Okojo.Bytecode;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private ResolvedIdentifierBinding ResolveIdentifierBinding(CompilerIdentifierName identifier)
    {
        var sourceName = identifier.Name;
        var useSyntheticArgumentsBinding = ShouldUseFunctionArgumentsBinding(sourceName);
        var hasResolvedLocalBinding = TryResolveLocalBinding(identifier, out var resolvedLocalBinding);
        if (!hasResolvedLocalBinding && !useSyntheticArgumentsBinding)
        {
            var resolvedAliasName = ResolveLocalAlias(sourceName);
            if (!string.Equals(resolvedAliasName, sourceName, StringComparison.Ordinal) &&
                TryGetSymbolId(resolvedAliasName, out var resolvedAliasSymbolId))
            {
                resolvedLocalBinding = new(resolvedAliasSymbolId, resolvedAliasName);
                hasResolvedLocalBinding = true;
            }
        }

        var semanticName = hasResolvedLocalBinding ? resolvedLocalBinding.Name : sourceName;

        if (hasResolvedLocalBinding &&
            TryGetVisibleCurrentLocalBinding(
                resolvedLocalBinding.SymbolId,
                sourceName,
                out var reg,
                out var slotIdx,
                out var hasCurrentContextSlot,
                out var isLexicalRegisterLocal,
                out var isConstLocal))
        {
            if (hasCurrentContextSlot)
            {
                var perIterationDepth = GetActivePerIterationContextDepthForSymbol(resolvedLocalBinding.SymbolId);
                if (perIterationDepth != 0)
                    return new(
                        ResolvedIdentifierBindingKind.CapturedContext,
                        sourceName,
                        semanticName,
                        resolvedLocalBinding.SymbolId,
                        Slot: (short)slotIdx,
                        Depth: (short)perIterationDepth,
                        IsConst: isConstLocal,
                        IsImmutableFunctionName: IsImmutableFunctionNameBinding(resolvedLocalBinding.SymbolId));
            }

            return new(
                ResolvedIdentifierBindingKind.CurrentLocal,
                sourceName,
                semanticName,
                resolvedLocalBinding.SymbolId,
                (short)reg,
                hasCurrentContextSlot ? (short)slotIdx : (short)(isLexicalRegisterLocal ? -2 : -1),
                0,
                isLexicalRegisterLocal,
                isConstLocal,
                IsImmutableFunctionNameBinding(resolvedLocalBinding.SymbolId));
        }

        if (hasResolvedLocalBinding &&
            IsCurrentFunctionLocalVisible(resolvedLocalBinding.SymbolId, sourceName) &&
            TryGetCurrentContextSlot(resolvedLocalBinding.SymbolId, out var currentContextSlot))
        {
            var currentIsLexicalLocal = IsLexicalLocalBinding(resolvedLocalBinding.SymbolId);
            var currentIsConstLocal = IsConstLocalBinding(resolvedLocalBinding.SymbolId);
            var currentIsImmutableFunctionName = IsImmutableFunctionNameBinding(resolvedLocalBinding.SymbolId);
            var perIterationDepth = GetActivePerIterationContextDepthForSymbol(resolvedLocalBinding.SymbolId);
            if (perIterationDepth != 0)
                return new(
                    ResolvedIdentifierBindingKind.CapturedContext,
                    sourceName,
                    semanticName,
                    resolvedLocalBinding.SymbolId,
                    Slot: (short)currentContextSlot,
                    Depth: (short)perIterationDepth,
                    IsConst: currentIsConstLocal,
                    IsImmutableFunctionName: currentIsImmutableFunctionName);

            return new(
                ResolvedIdentifierBindingKind.CurrentLocal,
                sourceName,
                semanticName,
                resolvedLocalBinding.SymbolId,
                -1,
                (short)currentContextSlot,
                0,
                currentIsLexicalLocal,
                currentIsConstLocal,
                currentIsImmutableFunctionName);
        }

        if (CanUseClassLexicalBindingLoad(sourceName))
        {
            if (!useMethodEnvironmentCapture)
                throw new InvalidOperationException("Class lexical binding load requires method-environment capture.");
            var depth = currentContextSlotById.Count == 0 && !forceModuleFunctionContext ? 0 : 1;
            return new(
                ResolvedIdentifierBindingKind.ClassLexical,
                sourceName,
                sourceName,
                Slot: 1,
                Depth: (short)depth);
        }

        if (TryGetModuleVariableBinding(sourceName, out var moduleBinding))
            return new(
                ResolvedIdentifierBindingKind.ModuleVariable,
                sourceName,
                sourceName,
                Slot: moduleBinding.CellIndex,
                Depth: moduleBinding.Depth,
                IsModuleReadOnly: moduleBinding.IsReadOnly);

        if (TryResolveCapturedContextAccess(identifier, out var capturedSlot, out var capturedDepth))
            return new(
                ResolvedIdentifierBindingKind.CapturedContext,
                sourceName,
                sourceName,
                Slot: (short)capturedSlot,
                Depth: (short)capturedDepth,
                IsConst: TryResolveCapturedConst(identifier),
                IsImmutableFunctionName: TryResolveCapturedImmutableFunctionName(identifier));

        if (TryGetModuleVariableBinding(sourceName, out var moduleBindingDirect))
            return new(
                ResolvedIdentifierBindingKind.ModuleVariable,
                sourceName,
                sourceName,
                Slot: moduleBindingDirect.CellIndex,
                Depth: moduleBindingDirect.Depth,
                IsModuleReadOnly: moduleBindingDirect.IsReadOnly);

        if (hasResolvedLocalBinding &&
            !string.Equals(semanticName, sourceName, StringComparison.Ordinal) &&
            TryGetModuleVariableBinding(semanticName, out var moduleBindingResolved))
            return new(
                ResolvedIdentifierBindingKind.ModuleVariable,
                sourceName,
                semanticName,
                Slot: moduleBindingResolved.CellIndex,
                Depth: moduleBindingResolved.Depth,
                IsModuleReadOnly: moduleBindingResolved.IsReadOnly);

        if (isArrowFunction &&
            string.Equals(sourceName, "arguments", StringComparison.Ordinal) &&
            TryResolveCapturedContextAccess(SyntheticArgumentsBindingName, out capturedSlot, out capturedDepth))
            return new(
                ResolvedIdentifierBindingKind.CapturedContext,
                sourceName,
                SyntheticArgumentsBindingName,
                SyntheticArgumentsSymbolId,
                Slot: (short)capturedSlot,
                Depth: (short)capturedDepth);

        if (ShouldUseFunctionArgumentsBinding(sourceName))
            return new(ResolvedIdentifierBindingKind.Arguments, sourceName, sourceName);

        if (sourceName == "undefined")
            return new(ResolvedIdentifierBindingKind.UndefinedIntrinsic, sourceName, sourceName);

        return new(ResolvedIdentifierBindingKind.Global, sourceName, sourceName);
    }

    private IdentifierReadBinding ResolveIdentifierReadBinding(CompilerIdentifierName identifier)
    {
        var sourceName = identifier.Name;
        var useSyntheticArgumentsBinding = ShouldUseFunctionArgumentsBinding(sourceName);
        if (useSyntheticArgumentsBinding)
            return new(IdentifierReadBindingKind.Arguments);

        var resolvedName = useSyntheticArgumentsBinding ? sourceName : ResolveLocalAlias(sourceName);

        if (TryResolveTopLevelModuleVariableReadBinding(sourceName, resolvedName, out var moduleReadBinding))
            return moduleReadBinding;

        if (TryGetVisibleCurrentLocalBinding(
                resolvedName,
                sourceName,
                out var reg,
                out var slotIdx,
                out var hasCurrentContextSlot,
                out var isLexicalRegisterLocal,
                out _))
        {
            if (hasCurrentContextSlot)
            {
                var perIterationDepth = GetActivePerIterationContextDepthForSymbol(resolvedName);
                if (perIterationDepth != 0)
                    return new(IdentifierReadBindingKind.CapturedContext, -1, (short)slotIdx,
                        (short)perIterationDepth);
            }

            return hasCurrentContextSlot
                ? new(IdentifierReadBindingKind.CurrentLocal, (short)reg, (short)slotIdx)
                : new IdentifierReadBinding(IdentifierReadBindingKind.CurrentLocal, (short)reg,
                    (short)(isLexicalRegisterLocal ? -2 : -1));
        }

        if (TryGetSymbolId(resolvedName, out var resolvedSymbolId) &&
            IsCurrentFunctionLocalVisible(resolvedSymbolId, sourceName) &&
            TryGetCurrentContextSlot(resolvedSymbolId, out var currentContextSlot))
        {
            var perIterationDepth = GetActivePerIterationContextDepthForSymbol(resolvedSymbolId);
            return perIterationDepth != 0
                ? new(IdentifierReadBindingKind.CapturedContext, -1, (short)currentContextSlot,
                    (short)perIterationDepth)
                : new(IdentifierReadBindingKind.CurrentLocal, -1, (short)currentContextSlot);
        }

        if (TryResolveAnyScopeModuleVariableReadBinding(sourceName, resolvedName, out moduleReadBinding))
            return moduleReadBinding;

        if (CanUseClassLexicalBindingLoad(sourceName))
        {
            if (!useMethodEnvironmentCapture)
                throw new InvalidOperationException("Class lexical binding load requires method-environment capture.");
            var depth = currentContextSlotById.Count == 0 && !forceModuleFunctionContext ? 0 : 1;
            return new(IdentifierReadBindingKind.CapturedContext, -1, 1, (short)depth);
        }

        if (TryGetModuleVariableBinding(sourceName, out var moduleBinding))
            return new(
                IdentifierReadBindingKind.ModuleVariable,
                -1,
                moduleBinding.CellIndex,
                moduleBinding.Depth);

        if (TryResolveCapturedContextAccess(identifier, out var capturedSlot, out var capturedDepth))
            return new(IdentifierReadBindingKind.CapturedContext, -1, (short)capturedSlot,
                (short)capturedDepth);

        if (isArrowFunction &&
            string.Equals(sourceName, "arguments", StringComparison.Ordinal) &&
            TryResolveCapturedContextAccess(SyntheticArgumentsBindingName, out capturedSlot, out capturedDepth))
            return new(IdentifierReadBindingKind.CapturedContext, -1, (short)capturedSlot,
                (short)capturedDepth);

        if (sourceName == "undefined")
            return new(IdentifierReadBindingKind.UndefinedIntrinsic);

        return new(IdentifierReadBindingKind.Global);
    }

    private bool TryResolveTopLevelModuleVariableReadBinding(
        string sourceName,
        string resolvedName,
        out IdentifierReadBinding binding)
    {
        if (parent is not null || moduleVariableBindings is null)
        {
            binding = default;
            return false;
        }

        if (TryGetModuleVariableBinding(sourceName, out var directBinding))
        {
            binding = new(
                IdentifierReadBindingKind.ModuleVariable,
                -1,
                directBinding.CellIndex,
                directBinding.Depth);
            return true;
        }

        if (!string.Equals(resolvedName, sourceName, StringComparison.Ordinal) &&
            TryGetModuleVariableBinding(resolvedName, out var resolvedBinding))
        {
            binding = new(
                IdentifierReadBindingKind.ModuleVariable,
                -1,
                resolvedBinding.CellIndex,
                resolvedBinding.Depth);
            return true;
        }

        binding = default;
        return false;
    }

    private bool TryResolveAnyScopeModuleVariableReadBinding(
        string sourceName,
        string resolvedName,
        out IdentifierReadBinding binding)
    {
        if (TryGetModuleVariableBinding(sourceName, out var directBinding))
        {
            binding = new(
                IdentifierReadBindingKind.ModuleVariable,
                -1,
                directBinding.CellIndex,
                directBinding.Depth);
            return true;
        }

        if (!string.Equals(resolvedName, sourceName, StringComparison.Ordinal) &&
            TryGetModuleVariableBinding(resolvedName, out var resolvedBinding))
        {
            binding = new(
                IdentifierReadBindingKind.ModuleVariable,
                -1,
                resolvedBinding.CellIndex,
                resolvedBinding.Depth);
            return true;
        }

        binding = default;
        return false;
    }

    private IdentifierReadBinding ResolveIdentifierReadBinding(string sourceName)
    {
        return ResolveIdentifierReadBinding(new CompilerIdentifierName(sourceName));
    }

    private void EmitIdentifierRead(CompilerIdentifierName identifier)
    {
        var sourceName = identifier.Name;
        if (CanUseClassLexicalBindingLoad(sourceName))
            usesClassLexicalBinding = true;

        var binding = ResolveIdentifierReadBinding(identifier);
        if (binding.Kind == IdentifierReadBindingKind.CurrentLocal)
        {
            if (binding.Slot >= 0)
            {
                var readPc = builder.CodeLength;
                EmitLdaCurrentContextSlot(binding.Slot);
                builder.AddTdzReadDebugName(readPc, sourceName);
            }
            else if (binding.Slot == -2)
            {
                var resolvedName = ResolveLocalAlias(sourceName);
                if (IsKnownInitializedLexical(resolvedName))
                {
                    EmitLdaRegister(binding.Register);
                }
                else
                {
                    var readPc = builder.CodeLength;
                    EmitLdaRegister(binding.Register, true);
                    builder.AddTdzReadDebugName(readPc, sourceName);
                }
            }
            else
            {
                EmitLdaRegister(binding.Register);
            }

            return;
        }

        switch (binding.Kind)
        {
            case IdentifierReadBindingKind.ModuleVariable:
                builder.EmitLda(JsOpCode.LdaModuleVariable, unchecked((byte)binding.Slot), (byte)binding.Depth);
                break;
            case IdentifierReadBindingKind.CapturedContext:
            {
                var readPc = builder.CodeLength;
                EmitLdaContextSlot(0, binding.Slot, binding.Depth);
                builder.AddTdzReadDebugName(readPc, sourceName);
                requiresClosureBinding = true;
                break;
            }
            case IdentifierReadBindingKind.Arguments:
                _ = TryEmitArgumentsIdentifierLoad(sourceName);
                break;
            case IdentifierReadBindingKind.UndefinedIntrinsic:
                EmitLdaUndefined();
                break;
            case IdentifierReadBindingKind.Global:
            {
                var nameIdx = builder.AddAtomizedStringConstant(sourceName);
                EmitLdaGlobalByIndex(nameIdx, builder.GetOrAllocateGlobalBindingFeedbackSlot(sourceName));
                break;
            }
            default:
                throw new InvalidOperationException("Unexpected identifier read binding kind.");
        }
    }

    private void EmitIdentifierRead(string sourceName)
    {
        EmitIdentifierRead(new CompilerIdentifierName(sourceName));
    }

    private bool TryResolveIdentifierStoreBinding(
        string resolvedName,
        string sourceNameForDebug,
        out IdentifierStoreBinding binding)
    {
        if (CanUseClassLexicalBindingLoad(sourceNameForDebug))
        {
            if (!useMethodEnvironmentCapture)
                throw new InvalidOperationException("Class lexical binding store requires method-environment capture.");
            var depth = currentContextSlotById.Count == 0 && !forceModuleFunctionContext ? 0 : 1;
            binding = new(
                IdentifierStoreBindingKind.CapturedContext,
                -1,
                1,
                (short)depth,
                true,
                true);
            return true;
        }

        if (TryGetVisibleCurrentLocalBinding(
                resolvedName,
                sourceNameForDebug,
                out var reg,
                out var slotIdx,
                out var hasCurrentContextSlot,
                out var isLexicalRegisterLocal,
                out var isConstLocal))
        {
            var isLexicalLocal = IsLexicalLocalBinding(resolvedName);
            var isImmutableFunctionName = IsImmutableFunctionNameBinding(resolvedName);
            if (hasCurrentContextSlot)
            {
                var perIterationDepth = GetActivePerIterationContextDepthForSymbol(resolvedName);
                if (perIterationDepth != 0)
                {
                    binding = new(
                        IdentifierStoreBindingKind.CapturedContext,
                        -1,
                        (short)slotIdx,
                        (short)perIterationDepth,
                        isLexicalLocal,
                        isConstLocal,
                        isImmutableFunctionName);
                    return true;
                }
            }

            binding = hasCurrentContextSlot
                ? new(IdentifierStoreBindingKind.CurrentLocal, (short)reg, (short)slotIdx, 0,
                    isLexicalLocal, isConstLocal, isImmutableFunctionName)
                : new IdentifierStoreBinding(IdentifierStoreBindingKind.CurrentLocal, (short)reg, -1, 0, isLexicalLocal,
                    isConstLocal, isImmutableFunctionName, isLexicalRegisterLocal);
            return true;
        }

        if (TryGetSymbolId(resolvedName, out var resolvedSymbolId) &&
            IsCurrentFunctionLocalVisible(resolvedSymbolId, sourceNameForDebug) &&
            TryGetCurrentContextSlot(resolvedSymbolId, out var currentContextSlot))
        {
            var currentIsLexicalLocal = IsLexicalLocalBinding(resolvedSymbolId);
            var currentIsConstLocal = IsConstLocalBinding(resolvedSymbolId);
            var currentIsImmutableFunctionName = IsImmutableFunctionNameBinding(resolvedSymbolId);
            var perIterationDepth = GetActivePerIterationContextDepthForSymbol(resolvedSymbolId);
            binding = perIterationDepth != 0
                ? new(
                    IdentifierStoreBindingKind.CapturedContext,
                    -1,
                    (short)currentContextSlot,
                    (short)perIterationDepth,
                    currentIsLexicalLocal,
                    currentIsConstLocal,
                    currentIsImmutableFunctionName)
                : new(
                    IdentifierStoreBindingKind.CurrentLocal,
                    -1,
                    (short)currentContextSlot,
                    0,
                    currentIsLexicalLocal,
                    currentIsConstLocal,
                    currentIsImmutableFunctionName);
            return true;
        }

        if (TryGetModuleVariableBinding(sourceNameForDebug, out var sourceModuleBinding))
        {
            binding = new(
                IdentifierStoreBindingKind.ModuleVariable,
                -1,
                sourceModuleBinding.CellIndex,
                sourceModuleBinding.Depth,
                false,
                false,
                false,
                sourceModuleBinding.IsReadOnly);
            return true;
        }

        var sourceIdentifier = new CompilerIdentifierName(sourceNameForDebug);
        if (TryResolveCapturedContextAccess(sourceIdentifier, out var capturedSlot, out var capturedDepth))
        {
            var isConst = TryResolveCapturedConst(sourceIdentifier);
            var isLexical = TryResolveCapturedLexical(sourceIdentifier);
            var isImmutableFunctionName = TryResolveCapturedImmutableFunctionName(sourceIdentifier);
            binding = new(IdentifierStoreBindingKind.CapturedContext, -1, (short)capturedSlot,
                (short)capturedDepth, isLexical, isConst, isImmutableFunctionName);
            return true;
        }

        if (TryGetModuleVariableBinding(resolvedName, out var moduleBinding))
        {
            binding = new(
                IdentifierStoreBindingKind.ModuleVariable,
                -1,
                moduleBinding.CellIndex,
                moduleBinding.Depth,
                false,
                false,
                false,
                moduleBinding.IsReadOnly);
            return true;
        }

        binding = new(IdentifierStoreBindingKind.Global);
        return true;
    }

    private bool CanUseClassLexicalBindingLoad(CompilerIdentifierName identifier)
    {
        if (classLexicalNameForMethodResolution is null)
            return false;

        return classLexicalNameForMethodResolution.Value.NameId >= 0 && identifier.NameId >= 0
            ? classLexicalNameForMethodResolution.Value.NameId == identifier.NameId
            : string.Equals(classLexicalNameForMethodResolution.Value.Name, identifier.Name, StringComparison.Ordinal);
    }

    private bool CanUseClassLexicalBindingLoad(string identifierName)
    {
        if (classLexicalNameForMethodResolution is null)
            return false;
        return string.Equals(classLexicalNameForMethodResolution.Value.Name, identifierName, StringComparison.Ordinal);
    }

    private bool TryResolveCapturedContextAccess(CompilerIdentifierName identifier, out int ancestorContextSlot,
        out int depth)
    {
        ancestorContextSlot = -1;
        depth = activePerIterationContextSlots.Count;
        if (currentContextSlotById.Count != 0 || forceModuleFunctionContext)
            depth++;
        if (useMethodEnvironmentCapture)
            depth++;

        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.parent)
        {
            var resolvedInAncestor = ancestor.ResolveLocalAlias(identifier);
            if (ancestor.HasLocalBinding(resolvedInAncestor) ||
                ancestor.TryGetCurrentContextSlot(resolvedInAncestor, out _))
            {
                if (!ancestor.IsCurrentFunctionLocalVisibleForCapture(resolvedInAncestor))
                    continue;
                if (ancestor.TryGetCurrentContextSlot(resolvedInAncestor, out ancestorContextSlot))
                {
                    depth += ancestor.GetActivePerIterationContextDepthForSymbol(resolvedInAncestor);
                    return true;
                }

                throw new InvalidOperationException(
                    $"Captured binding '{resolvedInAncestor}' was not assigned a context slot.");
            }

            if (ancestor.currentContextSlotById.Count != 0 || ancestor.forceModuleFunctionContext)
                depth++;
            if (ancestor.useMethodEnvironmentCapture)
                depth++;
        }

        depth = 0;
        return false;
    }

    private bool TryResolveCapturedContextAccess(string sourceName, out int ancestorContextSlot, out int depth)
    {
        return TryResolveCapturedContextAccess(new CompilerIdentifierName(sourceName), out ancestorContextSlot,
            out depth);
    }

    private bool TryResolveDerivedThisContextAccess(out int ancestorContextSlot, out int depth)
    {
        ancestorContextSlot = -1;
        depth = 0;

        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.parent)
        {
            ancestor.EnsureDerivedThisContextSlotIfNeeded();

            if (ancestor.lexicalThisContextSlot >= 0)
            {
                ancestorContextSlot = ancestor.lexicalThisContextSlot;
                depth += ancestor.lexicalThisContextDepth;
                return true;
            }

            if (ancestor.derivedThisContextSlot >= 0)
            {
                ancestorContextSlot = ancestor.derivedThisContextSlot;
                return true;
            }

            if (ancestor.currentContextSlotById.TryGetValue(DerivedThisSymbolId, out var derivedThisSlot))
            {
                ancestorContextSlot = derivedThisSlot;
                return true;
            }

            if (ancestor.currentContextSlotById.Count != 0 || ancestor.forceModuleFunctionContext)
                depth++;
            if (ancestor.useMethodEnvironmentCapture)
                depth++;
        }

        depth = 0;
        return false;
    }

    private bool TryResolveCapturedConst(string sourceName)
    {
        return TryResolveCapturedConst(new CompilerIdentifierName(sourceName));
    }

    private bool TryResolveCapturedConst(CompilerIdentifierName identifier)
    {
        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.parent)
        {
            var resolvedInAncestor = ancestor.ResolveLocalAlias(identifier);
            if (ancestor.HasLocalBinding(resolvedInAncestor))
                return ancestor.IsConstLocalBinding(resolvedInAncestor);
        }

        return false;
    }

    private bool TryResolveCapturedLexical(string sourceName)
    {
        return TryResolveCapturedLexical(new CompilerIdentifierName(sourceName));
    }

    private bool TryResolveCapturedLexical(CompilerIdentifierName identifier)
    {
        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.parent)
        {
            var resolvedInAncestor = ancestor.ResolveLocalAlias(identifier);
            if (ancestor.HasLocalBinding(resolvedInAncestor))
                return ancestor.IsLexicalLocalBinding(resolvedInAncestor);
        }

        return false;
    }

    private bool TryResolveCapturedImmutableFunctionName(string sourceName)
    {
        return TryResolveCapturedImmutableFunctionName(new CompilerIdentifierName(sourceName));
    }

    private bool TryResolveCapturedImmutableFunctionName(CompilerIdentifierName identifier)
    {
        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.parent)
        {
            var resolvedInAncestor = ancestor.ResolveLocalAlias(identifier);
            if (ancestor.HasLocalBinding(resolvedInAncestor))
                return ancestor.IsImmutableFunctionNameBinding(resolvedInAncestor);
        }

        return false;
    }

    private enum ResolvedIdentifierBindingKind : byte
    {
        CurrentLocal,
        ModuleVariable,
        CapturedContext,
        Arguments,
        UndefinedIntrinsic,
        Global,
        ClassLexical
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ResolvedIdentifierBinding(
        ResolvedIdentifierBindingKind Kind,
        string SourceName,
        string SemanticName,
        int SymbolId = int.MinValue,
        short Register = -1,
        short Slot = -1,
        short Depth = 0,
        bool IsLexicalRegisterLocal = false,
        bool IsConst = false,
        bool IsImmutableFunctionName = false,
        bool IsModuleReadOnly = false);

    private enum IdentifierReadBindingKind : byte
    {
        CurrentLocal,
        ModuleVariable,
        CapturedContext,
        Arguments,
        UndefinedIntrinsic,
        Global
    }

    private readonly record struct IdentifierReadBinding(
        IdentifierReadBindingKind Kind,
        short Register = -1,
        short Slot = -1,
        short Depth = 0);

    private enum IdentifierStoreBindingKind : byte
    {
        ModuleVariable,
        CurrentLocal,
        CapturedContext,
        Global
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct IdentifierStoreBinding(
        IdentifierStoreBindingKind Kind,
        short Register = -1,
        short Slot = -1,
        short Depth = 0,
        bool IsLexical = false,
        bool IsConst = false,
        bool IsImmutableFunctionName = false,
        bool IsLexicalRegisterLocal = false,
        bool IsModuleReadOnly = false);
}
