using System.Globalization;
using Okojo.Bytecode;
using Okojo.Parsing;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private const int ArrayAssignmentElementFlagElision = 1;
    private const int ArrayAssignmentElementFlagRest = 2;
    private const int ArrayAssignmentTargetFlagHasTarget = 1;
    private const int ArrayAssignmentTargetFlagComputed = 2;
    private const int ArrayAssignmentTargetFlagReceiverIsThunk = 4;
    private const int ArrayAssignmentTargetFlagKeyIsThunk = 8;
    private const int ArrayAssignmentTargetFlagSetterThunk = 16;
    private const int ArrayAssignmentTargetFlagElisionRuntime = 32;
    private const int ArrayAssignmentTargetFlagRestRuntime = 64;

    private void EmitCreateClosureForMethodWithEnvironment(
        JsBytecodeFunction functionObj,
        int homeObjectReg,
        int classLexicalReg = -1,
        int privateBrandSourceReg = -1,
        IReadOnlyList<int>? inheritedPrivateBrandIds = null,
        int inheritedPrivateBrandSourceReg = -1,
        IReadOnlyList<PrivateBrandSourceMapping>? explicitPrivateBrandMappings = null)
    {
        var needsMethodEnvironment = functionObj.UsesMethodEnvironmentCapture;
        if (!needsMethodEnvironment)
        {
            var idx = builder.AddObjectConstant(functionObj);
            EmitCreateClosureByIndex(idx);
            if (privateBrandSourceReg >= 0)
                EmitSetFunctionPrivateBrandTokenFromAccumulator(privateBrandSourceReg);
            if (inheritedPrivateBrandSourceReg >= 0 && inheritedPrivateBrandIds is not null)
                EmitSetFunctionPrivateBrandMappingsFromAccumulator(inheritedPrivateBrandSourceReg,
                    inheritedPrivateBrandIds);
            if (explicitPrivateBrandMappings is not null)
                EmitSetFunctionPrivateBrandMappingsFromAccumulatorExact(explicitPrivateBrandMappings);
            return;
        }

        var slotCount = classLexicalReg >= 0 ? 2 : 1;
        EmitCreateFunctionContextWithCells(slotCount);
        EmitPushContextAcc();
        EmitLdaRegister(homeObjectReg);
        EmitStaCurrentContextSlot(0);
        if (classLexicalReg >= 0)
        {
            EmitLdaRegister(classLexicalReg);
            EmitStaCurrentContextSlot(1);
        }

        var methodIdx = builder.AddObjectConstant(functionObj);
        EmitCreateClosureByIndex(methodIdx);
        if (privateBrandSourceReg >= 0)
            EmitSetFunctionPrivateBrandTokenFromAccumulator(privateBrandSourceReg);
        if (inheritedPrivateBrandSourceReg >= 0 && inheritedPrivateBrandIds is not null)
            EmitSetFunctionPrivateBrandMappingsFromAccumulator(inheritedPrivateBrandSourceReg,
                inheritedPrivateBrandIds);
        if (explicitPrivateBrandMappings is not null)
            EmitSetFunctionPrivateBrandMappingsFromAccumulatorExact(explicitPrivateBrandMappings);
        EmitPopContext();
    }

    private void EmitSetFunctionPrivateBrandTokenFromAccumulator(int brandSourceReg)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var argStart = AllocateTemporaryRegisterBlock(2);
            EmitStarRegister(argStart);
            EmitMoveRegister(brandSourceReg, argStart + 1);
            EmitCallRuntime(RuntimeId.SetFunctionPrivateBrandToken, argStart, 2);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitSetFunctionPrivateMethodValueFromAccumulator(int targetReg, int index)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var argStart = AllocateTemporaryRegisterBlock(3);
            EmitStarRegister(argStart + 2);
            EmitMoveRegister(targetReg, argStart);
            EmitLda(index);
            EmitStarRegister(argStart + 1);
            EmitCallRuntime(RuntimeId.SetFunctionPrivateMethodValue, argStart, 3);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitSetFunctionMethodEnvironment(int targetReg, int homeObjectReg, int classLexicalReg = -1)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var argStart = AllocateTemporaryRegisterBlock(3);
            EmitMoveRegister(targetReg, argStart);
            EmitMoveRegister(homeObjectReg, argStart + 1);
            if (classLexicalReg >= 0)
                EmitMoveRegister(classLexicalReg, argStart + 2);
            else
                EmitLdaUndefined();
            if (classLexicalReg < 0)
                EmitStarRegister(argStart + 2);
            EmitCallRuntime(RuntimeId.SetFunctionMethodEnvironment, argStart, 3);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitSetFunctionPrivateBrandMappingsFromAccumulator(
        int brandSourceReg,
        IReadOnlyList<int> brandIds)
    {
        if (brandIds.Count == 0)
            return;

        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var argStart = AllocateTemporaryRegisterBlock(3);
            for (var i = 0; i < brandIds.Count; i++)
            {
                EmitStarRegister(argStart);
                EmitLda(brandIds[i]);
                EmitStarRegister(argStart + 1);
                EmitMoveRegister(brandSourceReg, argStart + 2);
                EmitCallRuntime(RuntimeId.SetFunctionPrivateBrandMapping, argStart, 3);
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitSetFunctionPrivateBrandMappingsFromAccumulator(
        IReadOnlyList<PrivateBrandSourceMapping> brandMappings)
    {
        EmitSetFunctionPrivateBrandMappingsFromAccumulator(brandMappings, false);
    }

    private void EmitSetFunctionPrivateBrandMappingsFromAccumulatorExact(
        IReadOnlyList<PrivateBrandSourceMapping> brandMappings)
    {
        EmitSetFunctionPrivateBrandMappingsFromAccumulator(brandMappings, true);
    }

    private void EmitSetFunctionPrivateBrandMappingsFromAccumulatorExact(
        int brandSourceReg,
        IReadOnlyList<int> brandIds)
    {
        if (brandIds.Count == 0)
            return;

        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var argStart = AllocateTemporaryRegisterBlock(3);
            for (var i = 0; i < brandIds.Count; i++)
            {
                EmitStarRegister(argStart);
                EmitLda(brandIds[i]);
                EmitStarRegister(argStart + 1);
                EmitMoveRegister(brandSourceReg, argStart + 2);
                EmitCallRuntime(RuntimeId.SetFunctionPrivateBrandMappingExact, argStart, 3);
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitSetFunctionPrivateBrandMappingsFromAccumulator(
        IReadOnlyList<PrivateBrandSourceMapping> brandMappings,
        bool exactSource)
    {
        if (brandMappings.Count == 0)
            return;

        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var argStart = AllocateTemporaryRegisterBlock(3);
            for (var i = 0; i < brandMappings.Count; i++)
            {
                EmitStarRegister(argStart);
                EmitLda(brandMappings[i].BrandId);
                EmitStarRegister(argStart + 1);
                EmitMoveRegister(brandMappings[i].SourceReg, argStart + 2);
                EmitCallRuntime(
                    exactSource
                        ? RuntimeId.SetFunctionPrivateBrandMappingExact
                        : RuntimeId.SetFunctionPrivateBrandMapping,
                    argStart,
                    3);
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitInheritCurrentFunctionPrivateBrandStateIfNeeded()
    {
        var visiblePrivateBrandIds = CollectVisiblePrivateBrandIds();
        if (visiblePrivateBrandIds is null)
            return;

        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var closureReg = AllocateTemporaryRegisterBlock(2);
            var sourceReg = closureReg + 1;
            EmitStarRegister(closureReg);
            builder.EmitLda(JsOpCode.LdaCurrentFunction);
            EmitStarRegister(sourceReg);
            EmitLdaRegister(closureReg);
            EmitSetFunctionPrivateBrandTokenFromAccumulator(sourceReg);
            EmitSetFunctionPrivateBrandMappingsFromAccumulator(sourceReg, visiblePrivateBrandIds);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitLoadCurrentFunctionPrivateMethodValue(int index)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var argStart = AllocateTemporaryRegister();
            EmitLda(index);
            EmitStarRegister(argStart);
            EmitCallRuntime(RuntimeId.LoadCurrentFunctionPrivateMethodValue, argStart, 1);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitOptionalChainShortCircuitLoad(int objectReg, Action loadWhenPresent)
    {
        var shortCircuitLabel = builder.CreateLabel();
        var doneLabel = builder.CreateLabel();

        EmitLdaRegister(objectReg);
        builder.EmitJump(JsOpCode.JumpIfNull, shortCircuitLabel);
        EmitLdaRegister(objectReg);
        builder.EmitJump(JsOpCode.JumpIfUndefined, shortCircuitLabel);

        loadWhenPresent();
        builder.EmitJump(doneLabel);

        builder.BindLabel(shortCircuitLabel);
        EmitLdaUndefined();
        builder.BindLabel(doneLabel);
    }

    private void EmitObjectLiteralDataValue(JsObjectProperty property, int objectRegister)
    {
        if (property.Value is JsFunctionExpression { HasSuperBindingHint: true } methodExpr)
        {
            var parameterPlan = FunctionParameterPlan.FromFunction(methodExpr);
            var methodObj = CompileFunctionObject(
                !property.IsComputed && string.IsNullOrEmpty(methodExpr.Name) ? property.Key : methodExpr.Name,
                parameterPlan,
                methodExpr.Body,
                CreateFunctionShape(methodExpr.IsGenerator, methodExpr.IsAsync, methodExpr.IsArrow,
                    true),
                sourceStartPosition: methodExpr.Position,
                useMethodEnvironmentCapture: true);
            if (methodObj.RequiresClosureBinding)
                requiresClosureBinding = true;
            EmitCreateClosureForMethodWithEnvironment(methodObj, objectRegister);
            return;
        }

        if (!property.IsComputed &&
            property.Value is JsFunctionExpression { Name: null } or JsClassExpression { Name: null })
        {
            VisitExpressionWithInferredName(property.Value, property.Key);
            return;
        }

        VisitExpression(property.Value);
    }

    private void EmitObjectLiteralSpread(int targetReg, JsExpression sourceExpression)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            VisitExpression(sourceExpression);
            var sourceReg = AllocateTemporaryRegister();
            EmitStarRegister(sourceReg);

            var argStart = AllocateTemporaryRegisterBlock(2);
            EmitMoveRegister(targetReg, argStart);
            EmitMoveRegister(sourceReg, argStart + 1);
            EmitCallRuntime(RuntimeId.CopyDataProperties, argStart, 2);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitPrepareSuperReceiverAndKey(
        JsMemberExpression superMember,
        int thisReg,
        int keyReg,
        string invalidKeyMessage,
        out int namedSuperNameIdx)
    {
        builder.EmitLda(JsOpCode.LdaThis);
        EmitStarRegister(thisReg);

        if (superMember.IsComputed)
        {
            VisitExpression(superMember.Property);
        }
        else
        {
            if (!TryGetNamedMemberKey(superMember, out var superName))
                throw new NotSupportedException(invalidKeyMessage);
            var keyIdx = builder.AddObjectConstant(superName);
            EmitLdaStringConstantByIndex(keyIdx);
        }

        EmitStarRegister(keyReg);

        if (superMember.IsComputed)
        {
            namedSuperNameIdx = -1;
            return;
        }

        if (!TryGetNamedMemberKey(superMember, out var namedSuperName))
            throw new NotSupportedException(invalidKeyMessage);
        namedSuperNameIdx = builder.AddAtomizedStringConstant(namedSuperName);
    }

    private void EmitLoadSuperPropertyFromPrepared(JsMemberExpression superMember, int thisReg, int namedSuperNameIdx)
    {
        if (superMember.IsComputed)
            EmitCallRuntime(RuntimeId.LoadKeyedFromSuper, thisReg, 2);
        else
            EmitGetNamedPropertyFromSuperByIndex(namedSuperNameIdx, 0);
    }

    private void EmitStoreSuperPropertyFromPrepared(int thisReg)
    {
        EmitCallRuntime(RuntimeId.SuperSet, thisReg, 3);
    }

    private void EmitArrayDestructuringAssignment(JsArrayExpression pattern, JsExpression right)
    {
        EmitArrayDestructuringAssignment(pattern, right, false);
    }

    private void EmitArrayDestructuringAssignment(JsArrayExpression pattern, JsExpression right,
        bool initializeIdentifiers)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var elements = ExtractArrayAssignmentElements(pattern);
            VisitExpression(right);
            var rightReg = AllocateTemporaryRegister();
            EmitStarRegister(rightReg);
            EmitArrayDestructuringAssignmentFromRegister(pattern, rightReg, elements, initializeIdentifiers);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitObjectDestructuringAssignment(JsObjectExpression pattern, JsExpression right)
    {
        EmitObjectDestructuringAssignment(pattern, right, false);
    }

    private void EmitObjectDestructuringAssignment(JsObjectExpression pattern, JsExpression right,
        bool initializeIdentifiers)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            var elements = ExtractObjectAssignmentElements(pattern);

            VisitExpression(right);
            var rightReg = AllocateTemporaryRegister();
            EmitStarRegister(rightReg);
            EmitObjectDestructuringAssignmentFromRegister(pattern, rightReg, elements, initializeIdentifiers);
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private ArrayAssignmentElement[] ExtractArrayAssignmentElements(JsArrayExpression pattern)
    {
        var elements = new ArrayAssignmentElement[pattern.Elements.Count];
        for (var i = 0; i < pattern.Elements.Count; i++)
        {
            var element = pattern.Elements[i];
            if (element is null)
            {
                elements[i] = new(null, null, true, false);
                continue;
            }

            if (element is JsSpreadExpression spread)
            {
                var (restTarget, restDefaultExpression) = ExtractAssignmentTargetWithDefault(spread.Argument);
                if (restDefaultExpression is not null)
                    throw new NotSupportedException("Array rest destructuring does not support defaults.");
                elements[i] = new(restTarget, null, false, true);
                continue;
            }

            var (target, defaultExpression) = ExtractAssignmentTargetWithDefault(element);
            elements[i] = new(target, defaultExpression, false, false);
        }

        return elements;
    }

    private ObjectAssignmentElement[] ExtractObjectAssignmentElements(JsObjectExpression pattern)
    {
        var elements = new ObjectAssignmentElement[pattern.Properties.Count];
        for (var i = 0; i < pattern.Properties.Count; i++)
        {
            var property = pattern.Properties[i];
            if (property.Kind == JsObjectPropertyKind.Spread)
            {
                var (restTarget, restDefaultExpression) = ExtractAssignmentTargetWithDefault(property.Value);
                if (restDefaultExpression is not null)
                    throw new NotSupportedException("Object rest destructuring does not support defaults.");

                elements[i] = new(
                    null,
                    null,
                    restTarget,
                    null,
                    true);
                continue;
            }

            if (property.Kind != JsObjectPropertyKind.Data)
                throw new NotSupportedException(
                    "Object destructuring assignment currently supports data properties and rest.");

            var (target, defaultExpression) = ExtractAssignmentTargetWithDefault(property.Value);
            elements[i] = new(
                property.IsComputed ? null : property.Key,
                property.ComputedKey,
                target,
                defaultExpression,
                false);
        }

        return elements;
    }

    private static (JsExpression Target, JsExpression? DefaultExpression) ExtractAssignmentTargetWithDefault(
        JsExpression expression)
    {
        return expression switch
        {
            JsIdentifierExpression or JsMemberExpression or JsArrayExpression or JsObjectExpression => (expression,
                null),
            JsAssignmentExpression
            {
                Operator: JsAssignmentOperator.Assign,
                Left: JsIdentifierExpression or JsMemberExpression or JsArrayExpression or JsObjectExpression
            } assign => (
                assign.Left, assign.Right),
            _ => throw new NotSupportedException(
                "Destructuring assignment currently supports identifier/member/pattern targets and simple defaults only.")
        };
    }

    private PreparedDestructuringTarget PrepareDestructuringTarget(JsExpression target)
    {
        if (target is JsIdentifierExpression id)
            return new(new CompilerIdentifierName(id.Name, id.NameId), -1, -1, null);

        if (target is not JsMemberExpression member)
            throw new NotSupportedException("Destructuring assignment target is not supported.");

        if (member.Object is JsSuperExpression)
            throw new NotSupportedException(
                "Super destructuring assignment targets are not supported in Okojo Phase 1.");
        int objectReg;
        if (!TryGetPlainLocalReadRegister(member.Object, out objectReg))
        {
            VisitExpression(member.Object);
            objectReg = AllocateTemporaryRegister();
            EmitStarRegister(objectReg);
        }

        if (member.IsPrivate)
        {
            if (!TryResolvePrivateMemberBinding(member, out var privateBinding))
                throw new NotSupportedException(
                    "Private destructuring assignment target shape is not supported in Okojo Phase 1.");

            return new(null, objectReg, -1, null, privateBinding);
        }

        if (member.IsComputed)
        {
            VisitExpression(member.Property);
            var keyReg = AllocateTemporaryRegister();
            EmitStarRegister(keyReg);
            return new(null, objectReg, keyReg, null);
        }

        if (!TryGetNamedMemberKey(member, out var memberName))
            throw new NotSupportedException("Only named or computed destructuring member targets are supported.");

        return new(null, objectReg, -1, memberName);
    }

    private void EmitDestructuringDefaultIfNeeded(JsExpression? defaultExpression, string? inferredName = null)
    {
        if (defaultExpression is null)
            return;

        var valueReg = AllocateTemporaryRegister();
        EmitStarRegister(valueReg);
        var useDefaultLabel = builder.CreateLabel();
        var doneLabel = builder.CreateLabel();
        builder.EmitJump(JsOpCode.JumpIfUndefined, useDefaultLabel);
        builder.EmitJump(doneLabel);
        builder.BindLabel(useDefaultLabel);
        VisitExpressionWithInferredName(defaultExpression, inferredName);
        builder.BindLabel(doneLabel);
    }

    private void EmitStoreOrDestructureAssignmentTarget(JsExpression target, bool initializeIdentifiers = false)
    {
        if (target is JsIdentifierExpression or JsMemberExpression)
        {
            EmitStorePreparedDestructuringTarget(PrepareDestructuringTarget(target), initializeIdentifiers);
            return;
        }

        var tempReg = AllocateTemporaryRegister();
        EmitStarRegister(tempReg);
        switch (target)
        {
            case JsObjectExpression objectPattern:
                EmitObjectDestructuringAssignmentFromRegister(objectPattern, tempReg,
                    initializeIdentifiers: initializeIdentifiers);
                break;
            case JsArrayExpression arrayPattern:
                EmitArrayDestructuringAssignmentFromRegister(arrayPattern, tempReg,
                    initializeIdentifiers: initializeIdentifiers);
                break;
            default:
                throw new NotSupportedException("Destructuring assignment target is not supported.");
        }
    }

    private void EmitStoreOrDestructureAssignmentTargetPreservingValue(JsExpression target,
        bool initializeIdentifiers = false)
    {
        if (target is JsIdentifierExpression or JsMemberExpression)
        {
            var valueReg = AllocateTemporaryRegister();
            EmitStarRegister(valueReg);
            var preparedTarget = PrepareDestructuringTarget(target);
            EmitLdaRegister(valueReg);
            EmitStorePreparedDestructuringTarget(preparedTarget, initializeIdentifiers);
            return;
        }

        EmitStoreOrDestructureAssignmentTarget(target, initializeIdentifiers);
    }

    private void EmitStoreIdentifierPreservingValue(CompilerIdentifierName identifier)
    {
        if (TryEmitStoreIdentifierByReloadingValue(identifier))
            return;

        var valueReg = AllocateTemporaryRegister();
        EmitStarRegister(valueReg);
        EmitLdaRegister(valueReg);
        StoreIdentifier(identifier);
        EmitLdaRegister(valueReg);
    }

    private bool TryEmitStoreIdentifierByReloadingValue(CompilerIdentifierName identifier)
    {
        if (ShouldUseFunctionArgumentsBinding(identifier.Name))
            return false;

        var resolvedName = ResolveLocalAlias(identifier.Name);
        _ = TryResolveIdentifierStoreBinding(resolvedName, identifier.Name, out var binding);
        if (binding.IsConst || binding.IsImmutableFunctionName || binding.IsModuleReadOnly)
            return false;

        switch (binding.Kind)
        {
            case IdentifierStoreBindingKind.ModuleVariable:
                StoreIdentifier(identifier);
                builder.EmitLda(JsOpCode.LdaModuleVariable, unchecked((byte)binding.Slot), (byte)binding.Depth);
                return true;
            case IdentifierStoreBindingKind.CurrentLocal:
                StoreIdentifier(identifier);
                if (binding.Slot >= 0)
                    EmitLdaCurrentContextSlot(binding.Slot, true);
                else
                    EmitLdaRegister(binding.Register);
                return true;
            case IdentifierStoreBindingKind.CapturedContext:
                StoreIdentifier(identifier);
                EmitLdaContextSlot(0, binding.Slot, binding.Depth, true);
                return true;
            default:
                return false;
        }
    }

    private void EmitArrayDestructuringAssignmentFromRegister(
        JsArrayExpression pattern,
        int rightReg,
        ArrayAssignmentElement[]? precomputedElements = null,
        bool initializeIdentifiers = false)
    {
        var elements = precomputedElements ?? ExtractArrayAssignmentElements(pattern);
        if (initializeIdentifiers)
        {
            EmitArrayDestructuringAssignmentFromRegisterGeneratorAware(rightReg, elements, true);
            EmitLdaRegister(rightReg);
            return;
        }

        if (RequiresGeneratorAwareArrayAssignment(elements))
        {
            EmitArrayDestructuringAssignmentFromRegisterGeneratorAware(rightReg, elements);
            EmitLdaRegister(rightReg);
            return;
        }

        if (!RequiresSinglePhaseArrayAssignment(elements))
        {
            EmitArrayDestructuringAssignmentFromRegisterFastPath(rightReg, elements);
            return;
        }

        EmitArrayDestructuringAssignmentFromRegisterSinglePhase(rightReg, elements);
        EmitLdaRegister(rightReg);
    }

    private static bool RequiresGeneratorAwareArrayAssignment(
        IReadOnlyList<ArrayAssignmentElement> elements)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            if (element.Target is JsIdentifierExpression { Name: "arguments" })
                return true;

            if (element.DefaultExpression is not null &&
                ExpressionRequiresCurrentGeneratorExecution(element.DefaultExpression))
                return true;

            if (element.Target is JsMemberExpression memberTarget &&
                (ExpressionRequiresCurrentGeneratorExecution(memberTarget.Object) ||
                 (memberTarget.IsComputed && ExpressionRequiresCurrentGeneratorExecution(memberTarget.Property))))
                return true;
        }

        return false;
    }

    private static bool RequiresSinglePhaseArrayAssignment(
        IReadOnlyList<ArrayAssignmentElement> elements)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            if (element.DefaultExpression is not null)
                return true;
            if (element.Target is JsMemberExpression)
                return true;
        }

        return false;
    }

    private void EmitArrayDestructuringAssignmentFromRegisterFastPath(
        int rightReg,
        IReadOnlyList<ArrayAssignmentElement> elements)
    {
        var argStart = AllocateTemporaryRegisterBlock(1 + elements.Count);
        EmitMoveRegister(rightReg, argStart);

        for (var i = 0; i < elements.Count; i++)
        {
            var flags = elements[i].IsElision ? ArrayAssignmentElementFlagElision : 0;
            if (elements[i].IsRest)
                flags |= ArrayAssignmentElementFlagRest;
            EmitLda(flags);
            EmitStarRegister(argStart + 1 + i);
        }

        EmitCallRuntime(RuntimeId.DestructureArrayAssignment, argStart, 1 + elements.Count);

        var resultReg = AllocateTemporaryRegister();
        EmitStarRegister(resultReg);

        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            if (element.IsElision || element.Target is null)
                continue;

            EmitLda(i);
            EmitLdaKeyedProperty(resultReg);
            EmitStoreOrDestructureAssignmentTarget(element.Target);
        }

        EmitLdaRegister(rightReg);
    }

    private void EmitArrayDestructuringAssignmentFromRegisterGeneratorAware(
        int rightReg,
        IReadOnlyList<ArrayAssignmentElement> elements,
        bool initializeIdentifiers = false)
    {
        var iteratorReg = AllocateTemporaryRegister();
        var doneReg = AllocateSyntheticLocal($"$destr.iter.done.{finallyTempUniqueId}");
        var completionKindReg = AllocateSyntheticLocal($"$destr.iter.kind.{finallyTempUniqueId}");
        var completionValueReg = AllocateSyntheticLocal($"$destr.iter.value.{finallyTempUniqueId}");
        var kindCompareReg = AllocateSyntheticLocal($"$destr.iter.kindcmp.{finallyTempUniqueId}");
        finallyTempUniqueId++;

        EmitLdaRegister(rightReg);
        EmitCallRuntime(RuntimeId.CreateArrayDestructureIterator, rightReg, 1);
        EmitStarRegister(iteratorReg);
        EmitSetBooleanRegister(doneReg, false);

        var catchLabel = builder.CreateLabel();
        var finallyFromTryLabel = builder.CreateLabel();
        var finallyEntryLabel = builder.CreateLabel();
        var afterCloseLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        var returnLabel = builder.CreateLabel();
        var throwLabel = builder.CreateLabel();
        var notReturnLabel = builder.CreateLabel();
        var notThrowLabel = builder.CreateLabel();

        EmitLdaZero();
        EmitStarRegister(completionKindReg);
        EmitLdaUndefined();
        EmitStarRegister(completionValueReg);

        var routeMap = new FinallyJumpRouteMap();
        builder.EmitJump(JsOpCode.PushTry, catchLabel);
        activeFinallyFlow.Push(new(
            completionKindReg,
            completionValueReg,
            finallyFromTryLabel,
            true,
            routeMap));
        try
        {
            var elementValueReg = AllocateTemporaryRegister();
            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                PreparedDestructuringTarget? preparedTarget = null;
                if (element.Target is JsMemberExpression memberTarget)
                    preparedTarget = PrepareDestructuringTarget(memberTarget);

                if (element.IsElision)
                {
                    EmitLdaRegister(iteratorReg);
                    EmitCallRuntime(RuntimeId.DestructureIteratorStepValue, iteratorReg, 1);
                    EmitStarRegister(elementValueReg);
                    EmitLdaTheHole();
                    EmitTestEqualStrictRegister(elementValueReg);
                    var afterElisionLabel = builder.CreateLabel();
                    EmitJumpIfToBooleanFalse(afterElisionLabel);
                    EmitSetBooleanRegister(doneReg, true);
                    builder.BindLabel(afterElisionLabel);
                    continue;
                }

                if (element.IsRest)
                {
                    EmitLdaRegister(iteratorReg);
                    EmitCallRuntime(RuntimeId.DestructureIteratorRestArray, iteratorReg, 1);
                    var restValueReg = AllocateTemporaryRegister();
                    EmitStarRegister(restValueReg);
                    EmitSetBooleanRegister(doneReg, true);
                    if (element.Target is not null)
                    {
                        EmitLdaRegister(restValueReg);
                        if (preparedTarget is { } preparedRestTarget)
                            EmitStorePreparedDestructuringTarget(preparedRestTarget, initializeIdentifiers);
                        else
                            EmitStoreOrDestructureAssignmentTargetPreservingValue(element.Target,
                                initializeIdentifiers);
                    }

                    continue;
                }

                EmitLdaRegister(iteratorReg);
                EmitCallRuntime(RuntimeId.DestructureIteratorStepValue, iteratorReg, 1);
                EmitStarRegister(elementValueReg);

                var hasValueLabel = builder.CreateLabel();
                var afterValueLabel = builder.CreateLabel();
                EmitLdaTheHole();
                EmitTestEqualStrictRegister(elementValueReg);
                EmitJumpIfToBooleanFalse(hasValueLabel);
                EmitSetBooleanRegister(doneReg, true);
                EmitLdaUndefined();
                EmitJump(afterValueLabel);
                builder.BindLabel(hasValueLabel);
                EmitLdaRegister(elementValueReg);
                builder.BindLabel(afterValueLabel);

                EmitDestructuringDefaultIfNeeded(element.DefaultExpression,
                    TryGetIdentifierDestructuringTargetName(element.Target!, out var inferredName)
                        ? inferredName
                        : null);
                if (element.Target is not null)
                {
                    if (preparedTarget is { } preparedElementTarget)
                        EmitStorePreparedDestructuringTarget(preparedElementTarget, initializeIdentifiers);
                    else
                        EmitStoreOrDestructureAssignmentTargetPreservingValue(element.Target, initializeIdentifiers);
                }
            }
        }
        finally
        {
            activeFinallyFlow.Pop();
        }

        EmitPopTry();
        EmitJump(finallyEntryLabel);

        builder.BindLabel(finallyFromTryLabel);
        EmitPopTry();
        EmitJump(finallyEntryLabel);

        builder.BindLabel(catchLabel);
        EmitStarRegister(completionValueReg);
        EmitLda(2);
        EmitStarRegister(completionKindReg);
        EmitJump(finallyEntryLabel);

        builder.BindLabel(finallyEntryLabel);
        EmitLdaRegister(doneReg);
        builder.EmitJump(JsOpCode.JumpIfTrue, afterCloseLabel);

        EmitLda(2);
        EmitStarRegister(kindCompareReg);
        EmitLdaRegister(completionKindReg);
        EmitTestEqualStrictRegister(kindCompareReg);
        var fullCloseLabel = builder.CreateLabel();
        EmitJumpIfToBooleanFalse(fullCloseLabel);
        EmitLdaRegister(iteratorReg);
        EmitCallRuntime(RuntimeId.DestructureIteratorCloseBestEffort, iteratorReg, 1);
        EmitJump(afterCloseLabel);

        builder.BindLabel(fullCloseLabel);
        EmitLdaRegister(iteratorReg);
        EmitCallRuntime(RuntimeId.DestructureIteratorClose, iteratorReg, 1);

        builder.BindLabel(afterCloseLabel);
        EmitLda(1);
        EmitStarRegister(kindCompareReg);
        EmitLdaRegister(completionKindReg);
        EmitTestEqualStrictRegister(kindCompareReg);
        EmitJumpIfToBooleanFalse(notReturnLabel);
        EmitJump(returnLabel);

        builder.BindLabel(notReturnLabel);
        EmitLda(2);
        EmitStarRegister(kindCompareReg);
        EmitLdaRegister(completionKindReg);
        EmitTestEqualStrictRegister(kindCompareReg);
        EmitJumpIfToBooleanFalse(notThrowLabel);
        EmitJump(throwLabel);
        builder.BindLabel(notThrowLabel);
        EmitJump(endLabel);

        builder.BindLabel(returnLabel);
        EmitLdaRegister(completionValueReg);
        EmitReturnConsideringFinallyFlow();

        builder.BindLabel(throwLabel);
        EmitLdaRegister(completionValueReg);
        EmitThrowConsideringFinallyFlow();

        builder.BindLabel(endLabel);
    }

    private void EmitArrayDestructuringAssignmentFromRegisterSinglePhase(
        int rightReg,
        IReadOnlyList<ArrayAssignmentElement> elements)
    {
        var argStart = AllocateTemporaryRegisterBlock(1 + elements.Count * 4);
        EmitMoveRegister(rightReg, argStart);

        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            var specStart = argStart + 1 + i * 4;
            var flags = 0;

            if (element.IsElision)
                flags |= ArrayAssignmentTargetFlagElisionRuntime;
            if (element.IsRest)
                flags |= ArrayAssignmentTargetFlagRestRuntime;

            if (element.Target is null)
            {
                EmitLdaUndefined();
                EmitStarRegister(specStart);
                EmitStarRegister(specStart + 1);
            }
            else if (element.Target is JsMemberExpression { IsPrivate: false } memberTarget)
            {
                flags |= ArrayAssignmentTargetFlagHasTarget;
                if (ExpressionRequiresCurrentGeneratorExecution(memberTarget.Object))
                {
                    VisitExpression(memberTarget.Object);
                    EmitStarRegister(specStart);
                }
                else
                {
                    EmitZeroArgArrowThunk(memberTarget.Object);
                    EmitStarRegister(specStart);
                    flags |= ArrayAssignmentTargetFlagReceiverIsThunk;
                }

                if (memberTarget.IsComputed)
                {
                    if (ExpressionRequiresCurrentGeneratorExecution(memberTarget.Property))
                    {
                        VisitExpression(memberTarget.Property);
                        EmitStarRegister(specStart + 1);
                        flags |= ArrayAssignmentTargetFlagComputed;
                    }
                    else
                    {
                        EmitZeroArgArrowThunk(memberTarget.Property);
                        EmitStarRegister(specStart + 1);
                        flags |= ArrayAssignmentTargetFlagComputed | ArrayAssignmentTargetFlagKeyIsThunk;
                    }
                }
                else
                {
                    if (!TryGetNamedMemberKey(memberTarget, out var memberName))
                        throw new NotSupportedException(
                            "Only named or computed destructuring member targets are supported.");
                    var keyIdx = builder.AddObjectConstant(memberName);
                    EmitLdaStringConstantByIndex(keyIdx);
                    EmitStarRegister(specStart + 1);
                }
            }
            else
            {
                flags |= ArrayAssignmentTargetFlagHasTarget | ArrayAssignmentTargetFlagSetterThunk;
                EmitSingleArgAssignmentSetterThunk(element.Target);
                EmitStarRegister(specStart);
                EmitLdaUndefined();
                EmitStarRegister(specStart + 1);
            }

            EmitLda(flags);
            EmitStarRegister(specStart + 2);

            if (element.DefaultExpression is not null)
            {
                var inferredName = TryGetIdentifierDestructuringTargetName(element.Target!, out var targetName)
                    ? targetName
                    : null;
                EmitZeroArgArrowThunk(element.DefaultExpression, inferredName);
            }
            else
            {
                EmitLdaUndefined();
            }

            EmitStarRegister(specStart + 3);
        }

        EmitCallRuntime(RuntimeId.DestructureArrayAssignmentMemberTargets, argStart, 1 + elements.Count * 4);
    }

    private void EmitObjectDestructuringAssignmentFromRegister(
        JsObjectExpression pattern,
        int rightReg,
        ObjectAssignmentElement[]? precomputedElements = null,
        bool initializeIdentifiers = false)
    {
        var elements = precomputedElements ?? ExtractObjectAssignmentElements(pattern);
        EmitCallRuntime(RuntimeId.RequireObjectCoercible, rightReg, 1);

        var excludedKeys = new List<ObjectRestExcludedKey>(elements.Length);
        var preparedTargets = new PreparedDestructuringTarget?[elements.Length];

        for (var i = 0; i < elements.Length; i++)
        {
            var element = elements[i];

            var sourceKeyReg = -1;
            if (element.ComputedSourceKey is not null)
            {
                VisitExpression(element.ComputedSourceKey);
                sourceKeyReg = AllocateTemporaryRegister();
                EmitStarRegister(sourceKeyReg);
                EmitCallRuntime(RuntimeId.NormalizePropertyKey, sourceKeyReg, 1);
                EmitStarRegister(sourceKeyReg);
            }

            if (element.IsRest)
            {
                EmitObjectRestDestructuringAssignment(element.Target, rightReg, excludedKeys, initializeIdentifiers);
                continue;
            }

            if (element.Target is JsMemberExpression)
                preparedTargets[i] = PrepareDestructuringTarget(element.Target);

            if (sourceKeyReg >= 0)
            {
                EmitLdaRegister(sourceKeyReg);
                EmitLdaKeyedProperty(rightReg);
            }
            else
            {
                if (TryGetNumericStaticDestructuringIndex(element.StaticSourceKey!, out var numericIndex))
                {
                    EmitLda(numericIndex);
                    EmitLdaKeyedProperty(rightReg);
                }
                else
                {
                    var nameIdx = builder.AddAtomizedStringConstant(element.StaticSourceKey!);
                    var feedbackSlot = builder.AllocateFeedbackSlot();
                    EmitLdaNamedPropertyByIndex(rightReg, nameIdx, feedbackSlot);
                }
            }

            EmitDestructuringDefaultIfNeeded(element.DefaultExpression,
                TryGetIdentifierDestructuringTargetName(element.Target, out var inferredName)
                    ? inferredName
                    : null);
            if (preparedTargets[i] is { } preparedTarget)
                EmitStorePreparedDestructuringTarget(preparedTarget, initializeIdentifiers);
            else
                EmitStoreOrDestructureAssignmentTarget(element.Target, initializeIdentifiers);

            excludedKeys.Add(new(element.StaticSourceKey, sourceKeyReg));
        }

        EmitLdaRegister(rightReg);
    }

    private void EmitObjectRestDestructuringAssignment(
        JsExpression target,
        int sourceObjectRegister,
        List<ObjectRestExcludedKey> excludedKeys,
        bool initializeIdentifiers = false)
    {
        var restObjectReg = AllocateTemporaryRegister();
        EmitCreateEmptyObjectLiteral();
        EmitStarRegister(restObjectReg);

        var argStart = AllocateTemporaryRegisterBlock(2 + excludedKeys.Count);
        EmitMoveRegister(restObjectReg, argStart);
        EmitMoveRegister(sourceObjectRegister, argStart + 1);
        for (var i = 0; i < excludedKeys.Count; i++)
        {
            var targetReg = argStart + 2 + i;
            var excludedKey = excludedKeys[i];
            if (excludedKey.ComputedKeyRegister >= 0)
            {
                EmitMoveRegister(excludedKey.ComputedKeyRegister, targetReg);
                continue;
            }

            var keyIdx = builder.AddObjectConstant(excludedKey.StaticSourceKey!);
            EmitLdaStringConstantByIndex(keyIdx);
            EmitStarRegister(targetReg);
        }

        EmitCallRuntime(RuntimeId.CopyDataPropertiesExcluding, argStart, 2 + excludedKeys.Count);
        EmitLdaRegister(restObjectReg);
        EmitStoreOrDestructureAssignmentTargetPreservingValue(target, initializeIdentifiers);
    }

    private void VisitExpressionWithInferredName(JsExpression expression, string? inferredName)
    {
        if (!string.IsNullOrEmpty(inferredName) &&
            expression is JsFunctionExpression
            {
                Name: null,
                Parameters: var parameters,
                Body: var body,
                IsGenerator: var isGenerator,
                IsAsync: var isAsync,
                IsArrow: var isArrow,
                ParameterInitializers: var parameterInitializers,
                ParameterPatterns: var parameterPatterns,
                ParameterPositions: var parameterPositions,
                ParameterBindingKinds: var parameterBindingKinds,
                FunctionLength: var functionLength,
                HasSimpleParameterList: var hasSimpleParameterList,
                HasSuperBindingHint: var hasSuperBindingHint,
                HasDuplicateParameters: var hasDuplicateParameters,
                RestParameterIndex: var restParameterIndex
            })
        {
            EmitFunctionExpression(new(
                inferredName,
                parameters,
                body,
                isGenerator,
                isAsync,
                isArrow,
                parameterInitializers,
                parameterPatterns,
                parameterPositions,
                parameterBindingKinds,
                functionLength,
                hasSimpleParameterList,
                hasSuperBindingHint,
                hasDuplicateParameters,
                restParameterIndex), false);
            return;
        }

        if (!string.IsNullOrEmpty(inferredName) &&
            expression is JsClassExpression { Name: null, Elements: var classElements } classExpr &&
            ShouldInferAnonymousClassName(classElements))
        {
            VisitClassExpression(classExpr, inferredName);
            return;
        }

        VisitExpression(expression);
    }

    private void EmitFunctionExpression(JsFunctionExpression funcExpr, bool immutableSelfBinding = true)
    {
        var parameterPlan = FunctionParameterPlan.FromFunction(funcExpr);
        var funcObj = CompileFunctionObject(funcExpr.Name, parameterPlan,
            funcExpr.Body,
            CreateFunctionShape(funcExpr.IsGenerator, funcExpr.IsAsync, funcExpr.IsArrow),
            immutableSelfBinding,
            sourceStartPosition: funcExpr.Position,
            useMethodEnvironmentCapture: funcExpr.HasSuperBindingHint && useMethodEnvironmentCapture);
        if (funcObj.RequiresClosureBinding) requiresClosureBinding = true;
        if (funcExpr.HasSuperBindingHint && useMethodEnvironmentCapture)
        {
            var tempScope = BeginTemporaryRegisterScope();
            try
            {
                var depth = currentContextSlotById.Count == 0 && !forceModuleFunctionContext ? 0 : 1;
                var homeObjectReg = AllocateTemporaryRegister();
                builder.EmitLda(JsOpCode.LdaContextSlot, 0, (byte)depth);
                EmitStarRegister(homeObjectReg);

                var classLexicalReg = -1;
                if (classLexicalNameForMethodResolution is not null)
                {
                    classLexicalReg = AllocateTemporaryRegister();
                    builder.EmitLda(JsOpCode.LdaContextSlot, 1, (byte)depth);
                    EmitStarRegister(classLexicalReg);
                }

                EmitCreateClosureForMethodWithEnvironment(funcObj, homeObjectReg, classLexicalReg);
            }
            finally
            {
                EndTemporaryRegisterScope(tempScope);
            }
        }
        else
        {
            var idx = builder.AddObjectConstant(funcObj);
            EmitCreateClosureByIndex(idx);
        }

        EmitInheritCurrentFunctionPrivateBrandStateIfNeeded();
    }

    private bool TryGetIdentifierDestructuringTargetName(JsExpression target, out string name)
    {
        if (target is JsIdentifierExpression id)
        {
            name = id.Name;
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static bool ShouldInferAnonymousClassName(IReadOnlyList<JsClassElement> elements)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            if (!element.IsStatic || element.IsComputedKey)
                continue;
            if (string.Equals(element.Key, "name", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool TryGetNumericStaticDestructuringIndex(string key, out uint index)
    {
        index = 0;
        if (key.Length == 0)
            return false;
        if (!uint.TryParse(key, NumberStyles.None,
                CultureInfo.InvariantCulture, out index))
            return false;

        return string.Equals(key, index.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    private void EmitStorePreparedDestructuringTarget(in PreparedDestructuringTarget target,
        bool initializeIdentifiers = false)
    {
        if (target.Identifier is not null)
        {
            StoreIdentifier(
                TryResolveLocalBinding(target.Identifier.Value, out var resolvedTarget)
                    ? resolvedTarget.Name
                    : target.Identifier.Value.Name,
                initializeIdentifiers,
                target.Identifier.Value.Name);
            return;
        }

        if (target.RawKeyRegister >= 0)
        {
            var valueReg = AllocateTemporaryRegister();
            EmitStarRegister(valueReg);
            EmitLdaRegister(valueReg);
            EmitStaKeyedProperty(target.ObjectRegister, target.RawKeyRegister);
            return;
        }

        if (target.PrivateBinding is { } privateBinding)
        {
            var valueReg = AllocateTemporaryRegister();
            EmitStarRegister(valueReg);
            EmitPrivateFieldOp(JsOpCode.SetPrivateField, target.ObjectRegister, valueReg, privateBinding.BrandId,
                privateBinding.SlotIndex);
            return;
        }

        var nameIdx = builder.AddAtomizedStringConstant(target.StaticMemberKey!);
        var feedbackSlot = builder.AllocateFeedbackSlot();
        EmitStaNamedPropertyByIndex(target.ObjectRegister, nameIdx, feedbackSlot);
    }

    private void EmitZeroArgArrowThunk(JsExpression expression, string? inferredName = null)
    {
        var thunkExpression = expression;
        if (!string.IsNullOrEmpty(inferredName) &&
            expression is JsFunctionExpression
            {
                Name: null,
                Parameters: var parameters,
                Body: var body,
                IsGenerator: var isGenerator,
                IsAsync: var isAsync,
                IsArrow: var isArrow,
                ParameterInitializers: var parameterInitializers,
                ParameterPatterns: var parameterPatterns,
                ParameterPositions: var parameterPositions,
                ParameterBindingKinds: var parameterBindingKinds,
                FunctionLength: var functionLength,
                HasSimpleParameterList: var hasSimpleParameterList,
                HasSuperBindingHint: var hasSuperBindingHint,
                HasDuplicateParameters: var hasDuplicateParameters,
                RestParameterIndex: var restParameterIndex,
                ParameterIds: var parameterIds
            })
            thunkExpression = new JsFunctionExpression(
                inferredName,
                parameters,
                body,
                isGenerator,
                isAsync,
                isArrow,
                parameterInitializers,
                parameterPatterns,
                parameterPositions,
                parameterBindingKinds,
                functionLength,
                hasSimpleParameterList,
                hasSuperBindingHint,
                hasDuplicateParameters,
                restParameterIndex,
                -1,
                parameterIds);
        else if (!string.IsNullOrEmpty(inferredName) &&
                 expression is JsClassExpression { Name: null, Elements: var classElements } classExpr &&
                 ShouldInferAnonymousClassName(classElements))
            thunkExpression = new JsClassExpression(
                inferredName,
                classExpr.Elements,
                classExpr.Decorators,
                classExpr.HasExtends,
                classExpr.ExtendsExpression)
            {
                Position = classExpr.Position,
                EndPosition = classExpr.EndPosition
            };

        var thunkBody = new JsBlockStatement(new JsStatement[]
        {
            new JsReturnStatement(thunkExpression)
        }, false);

        MarkCapturedNamesReferencedByNestedFunction(thunkExpression);

        var parameterPlan = FunctionParameterPlan.Empty();
        var thunkObj = CompileFunctionObject(
            null,
            parameterPlan,
            thunkBody,
            CreateFunctionShape(false, false, true));
        if (thunkObj.RequiresClosureBinding)
            requiresClosureBinding = true;
        var idx = builder.AddObjectConstant(thunkObj);
        EmitCreateClosureByIndex(idx);
        EmitInheritCurrentFunctionPrivateBrandStateIfNeeded();
    }

    private void EmitSingleArgAssignmentSetterThunk(JsExpression target)
    {
        if (target is JsIdentifierExpression id)
        {
            if (ShouldUseFunctionArgumentsBinding(id.Name))
                target = new JsIdentifierExpression(SyntheticArgumentsBindingName);
            else if (TryResolveLocalBinding(id.Name, out var resolvedIdentifier) &&
                     !string.Equals(resolvedIdentifier.Name, id.Name, StringComparison.Ordinal))
                target = new JsIdentifierExpression(resolvedIdentifier.Name);
        }

        MarkCapturedNamesReferencedByNestedFunction(target);
        EnsureContextSlotsForNestedAssignmentTarget(target);

        var valueName = $"$destr_value_{finallyTempUniqueId++}";
        var valueRef = new JsIdentifierExpression(valueName);
        var body = new JsBlockStatement(new JsStatement[]
        {
            new JsExpressionStatement(
                new JsAssignmentExpression(JsAssignmentOperator.Assign, target, valueRef))
        }, false);

        var parameterPlan = FunctionParameterPlan.FromCompilerInputs(
            new[] { valueName },
            null,
            new JsExpression?[] { null },
            -1);
        var thunkObj = CompileFunctionObject(
            null,
            parameterPlan,
            body,
            CreateFunctionShape(false, false, true));
        if (thunkObj.RequiresClosureBinding)
            requiresClosureBinding = true;
        var idx = builder.AddObjectConstant(thunkObj);
        EmitCreateClosureByIndex(idx);
        EmitInheritCurrentFunctionPrivateBrandStateIfNeeded();
    }

    private void EnsureContextSlotsForNestedAssignmentTarget(JsExpression target)
    {
        switch (target)
        {
            case JsIdentifierExpression id
                when TryResolveLocalBinding(new CompilerIdentifierName(id.Name, id.NameId), out var resolvedIdentifier):
                EnsureCurrentContextSlotForLocal(resolvedIdentifier.SymbolId);
                break;
            case JsArrayExpression arrayPattern:
                foreach (var element in arrayPattern.Elements)
                    if (element is not null)
                        EnsureContextSlotsForNestedAssignmentTarget(element);

                break;
            case JsObjectExpression objectPattern:
                foreach (var prop in objectPattern.Properties)
                    if (prop.Value is not null)
                        EnsureContextSlotsForNestedAssignmentTarget(prop.Value);

                break;
        }
    }

    private void VisitExpression(JsExpression expr, bool resultUsed = true, bool directReturn = false)
    {
        EmitSourcePosition(expr.Position);
        switch (expr)
        {
            case JsImportMetaExpression:
                EmitCallRuntime(RuntimeId.GetCurrentModuleImportMeta, 0, 0);
                break;
            case JsImportCallExpression importCall:
            {
                var tempScope = BeginTemporaryRegisterScope();
                try
                {
                    var argCount = importCall.Options is null ? 1 : 2;
                    var argReg = AllocateTemporaryRegister();
                    var optionsReg = importCall.Options is null ? -1 : AllocateTemporaryRegister();

                    VisitExpression(importCall.Argument);
                    EmitStarRegister(argReg);
                    if (importCall.Options is not null)
                    {
                        VisitExpression(importCall.Options);
                        EmitStarRegister(optionsReg);
                    }

                    EmitCallRuntime(RuntimeId.DynamicImport, argReg, argCount);
                }
                finally
                {
                    EndTemporaryRegisterScope(tempScope);
                }
            }
                break;
            case JsLiteralExpression lit:
                if (lit.Value is double d)
                {
                    if (!IsNegativeZero(d) && d % 1 == 0 && d >= int.MinValue && d <= int.MaxValue)
                        EmitLda((long)d);
                    else
                        EmitLdaNumericConstantByIndex(builder.AddNumericConstant(d));
                }
                else if (lit.Value is string s)
                {
                    EmitLdaStringConstantByIndex(builder.AddObjectConstant(s));
                }
                else if (lit.Value == null)
                {
                    EmitLdaNull();
                }
                else if (lit.Value is JsValue undefinedValue && undefinedValue.IsUndefined)
                {
                    EmitLdaUndefined();
                }
                else if (lit.Value is bool b)
                {
                    EmitLda(b);
                }
                else if (lit.Value is JsBigInt bigInt)
                {
                    var idx = builder.AddObjectConstant(bigInt);
                    EmitLdaTypedConstByIndex(Tag.JsTagBigInt, idx);
                }

                break;
            case JsRegExpLiteralExpression regexLiteral:
            {
                var tempScope = BeginTemporaryRegisterScope();
                try
                {
                    var argStart = AllocateTemporaryRegisterBlock(2);
                    var patternIdx = builder.AddObjectConstant(regexLiteral.Pattern);
                    var flagsIdx = builder.AddObjectConstant(regexLiteral.Flags);
                    EmitLdaStringConstantByIndex(patternIdx);
                    EmitStarRegister(argStart);
                    EmitLdaStringConstantByIndex(flagsIdx);
                    EmitStarRegister(argStart + 1);
                    EmitCallRuntime(RuntimeId.CreateRegExpLiteral, argStart, 2);
                }
                finally
                {
                    EndTemporaryRegisterScope(tempScope);
                }
            }
                break;
            case JsThisExpression:
                builder.EmitLda(JsOpCode.LdaThis);
                break;

            case JsIdentifierExpression id:
            {
                var identifier = CompilerIdentifierName.From(id);
                if (ShouldThrowParameterInitializerTdz(identifier))
                {
                    EmitCallRuntime(RuntimeId.ThrowParameterInitializerTdz, 0, 0);
                    break;
                }

                if (CanUseClassLexicalBindingLoad(identifier))
                    usesClassLexicalBinding = true;

                var binding = ResolveIdentifierReadBinding(identifier);
                if (binding.Kind == IdentifierReadBindingKind.CurrentLocal)
                {
                    if (binding.Slot >= 0)
                    {
                        var readPc = builder.CodeLength;
                        EmitLdaCurrentContextSlot(binding.Slot);
                        builder.AddTdzReadDebugName(readPc, id.Name);
                    }
                    else if (binding.Slot == -2)
                    {
                        if (TryResolveLocalBinding(identifier, out var resolvedBinding) &&
                            IsKnownInitializedLexical(resolvedBinding.SymbolId))
                        {
                            EmitLdaRegister(binding.Register);
                        }
                        else
                        {
                            var readPc = builder.CodeLength;
                            EmitLdaRegister(binding.Register, true);
                            builder.AddTdzReadDebugName(readPc, id.Name);
                        }
                    }
                    else
                    {
                        EmitLdaRegister(binding.Register);
                    }
                }
                else
                {
                    switch (binding.Kind)
                    {
                        case IdentifierReadBindingKind.ModuleVariable:
                            builder.EmitLda(JsOpCode.LdaModuleVariable, unchecked((byte)binding.Slot),
                                (byte)binding.Depth);
                            break;
                        case IdentifierReadBindingKind.CapturedContext:
                        {
                            var readPc = builder.CodeLength;
                            EmitLdaContextSlot(0, binding.Slot, binding.Depth);
                            builder.AddTdzReadDebugName(readPc, id.Name);
                            requiresClosureBinding = true;
                            break;
                        }
                        case IdentifierReadBindingKind.Arguments:
                            _ = TryEmitArgumentsIdentifierLoad(id.Name);
                            break;
                        case IdentifierReadBindingKind.UndefinedIntrinsic:
                            EmitLdaUndefined();
                            break;
                        case IdentifierReadBindingKind.Global:
                        {
                            var nameIdx = builder.AddAtomizedStringConstant(id.Name);
                            EmitLdaGlobalByIndex(nameIdx, builder.GetOrAllocateGlobalBindingFeedbackSlot(id.Name));
                            break;
                        }
                        default:
                            throw new InvalidOperationException("Unexpected identifier read binding kind.");
                    }
                }
            }
                break;

            case JsAssignmentExpression assign:
                if (assign.Left is JsIdentifierExpression idLeft)
                {
                    var leftIdentifier = CompilerIdentifierName.From(idLeft);
                    var resolvedLeft = TryResolveLocalBinding(leftIdentifier, out var resolvedLeftBinding)
                        ? resolvedLeftBinding.Name
                        : leftIdentifier.Name;
                    if (assign.Operator == JsAssignmentOperator.Assign)
                    {
                        if (!TryEmitSelfBinaryAssignmentFastPath(resolvedLeft, idLeft.Name, assign.Right))
                        {
                            if (!assign.IsParenthesizedLeftHandSide)
                                VisitExpressionWithInferredName(assign.Right, leftIdentifier.Name);
                            else
                                VisitExpression(assign.Right);

                            EmitStoreIdentifierPreservingValue(leftIdentifier);
                        }
                    }
                    else
                    {
                        if (IsLogicalAssignmentOperator(assign.Operator))
                        {
                            VisitExpression(idLeft);
                            var endLabel = builder.CreateLabel();
                            var evalLabel =
                                EmitLogicalAssignmentShortCircuitJump(assign.Operator, endLabel);
                            if (evalLabel is not null)
                                builder.BindLabel(evalLabel.Value);

                            VisitExpressionWithInferredName(assign.Right,
                                assign.IsParenthesizedLeftHandSide ? null : leftIdentifier.Name);
                            EmitStoreIdentifierPreservingValue(leftIdentifier);
                            builder.BindLabel(endLabel);
                            break;
                        }

                        if (!TryEmitCompoundAssignmentFastPath(resolvedLeft, idLeft.Name, assign.Operator,
                                assign.Right))
                        {
                            if (!TryMapCompoundAssignmentOperatorToOkojoOpCode(assign.Operator, out var compoundOp))
                                throw new NotImplementedException($"Assignment operator {assign.Operator}");

                            VisitExpression(idLeft);
                            var lhsValueReg = AllocateTemporaryRegister();
                            EmitStarRegister(lhsValueReg);
                            VisitExpressionWithInferredName(assign.Right, leftIdentifier.Name);
                            EmitRegisterSlotOp(compoundOp, lhsValueReg);
                            EmitStoreIdentifierPreservingValue(leftIdentifier);
                        }
                    }
                }
                else if (assign.Left is JsMemberExpression memberLeft)
                {
                    var tempScope = BeginTemporaryRegisterScope();
                    try
                    {
                        var isLogicalAssignment = IsLogicalAssignmentOperator(assign.Operator);
                        var isCompound = assign.Operator != JsAssignmentOperator.Assign;
                        JsOpCode compoundOp = default;
                        if (isCompound && !isLogicalAssignment &&
                            !TryMapCompoundAssignmentOperatorToOkojoOpCode(assign.Operator, out compoundOp))
                            throw new NotImplementedException($"Assignment operator {assign.Operator}");

                        if (memberLeft.Object is JsSuperExpression)
                        {
                            if (memberLeft.IsPrivate)
                                ThrowUnexpectedPrivateFieldSyntaxError(memberLeft.Position);

                            var superSetArgsStart = AllocateTemporaryRegisterBlock(3);
                            var thisReg = superSetArgsStart;
                            var keyReg = superSetArgsStart + 1;
                            var valueReg = superSetArgsStart + 2;
                            EmitPrepareSuperReceiverAndKey(
                                memberLeft,
                                thisReg,
                                keyReg,
                                "Super assignment requires named or computed property key.",
                                out var superNameIdx);

                            if (isCompound)
                            {
                                EmitLoadSuperPropertyFromPrepared(memberLeft, thisReg, superNameIdx);
                                if (isLogicalAssignment)
                                {
                                    var endLabel = builder.CreateLabel();
                                    var evalLabel =
                                        EmitLogicalAssignmentShortCircuitJump(assign.Operator, endLabel);
                                    if (evalLabel is not null)
                                        builder.BindLabel(evalLabel.Value);

                                    VisitExpression(assign.Right);
                                    EmitStarRegister(valueReg);
                                    EmitStoreSuperPropertyFromPrepared(thisReg);
                                    builder.BindLabel(endLabel);
                                }
                                else
                                {
                                    var lhsValueReg = AllocateTemporaryRegister();
                                    EmitStarRegister(lhsValueReg);
                                    VisitExpression(assign.Right);
                                    EmitRegisterSlotOp(compoundOp, lhsValueReg);
                                    EmitStarRegister(valueReg);
                                    EmitStoreSuperPropertyFromPrepared(thisReg);
                                }
                            }
                            else
                            {
                                VisitExpression(assign.Right);
                                EmitStarRegister(valueReg);
                                EmitStoreSuperPropertyFromPrepared(thisReg);
                            }

                            break;
                        }

                        int objReg;
                        if (!TryGetPlainLocalReadRegister(memberLeft.Object, out objReg))
                        {
                            VisitExpression(memberLeft.Object);
                            objReg = AllocateTemporaryRegister();
                            EmitStarRegister(objReg);
                        }

                        if (memberLeft.IsPrivate)
                        {
                            if (!TryResolvePrivateMemberBinding(memberLeft, out var privateBinding))
                                throw new NotSupportedException(
                                    "Private member assignment shape is not supported in Okojo Phase 1.");

                            if (isCompound)
                            {
                                EmitPrivateFieldOp(JsOpCode.GetPrivateField, objReg, privateBinding.BrandId,
                                    privateBinding.SlotIndex);
                                if (isLogicalAssignment)
                                {
                                    var endLabel = builder.CreateLabel();
                                    var evalLabel =
                                        EmitLogicalAssignmentShortCircuitJump(assign.Operator, endLabel);
                                    if (evalLabel is not null)
                                        builder.BindLabel(evalLabel.Value);

                                    VisitExpression(assign.Right);
                                    var valueReg = AllocateTemporaryRegister();
                                    EmitStarRegister(valueReg);
                                    EmitPrivateFieldOp(JsOpCode.SetPrivateField, objReg, valueReg,
                                        privateBinding.BrandId,
                                        privateBinding.SlotIndex);
                                    builder.BindLabel(endLabel);
                                }
                                else
                                {
                                    var lhsValueReg = AllocateTemporaryRegister();
                                    EmitStarRegister(lhsValueReg);
                                    VisitExpression(assign.Right);
                                    EmitRegisterSlotOp(compoundOp, lhsValueReg);
                                    var valueReg = AllocateTemporaryRegister();
                                    EmitStarRegister(valueReg);
                                    EmitPrivateFieldOp(JsOpCode.SetPrivateField, objReg, valueReg,
                                        privateBinding.BrandId,
                                        privateBinding.SlotIndex);
                                }
                            }
                            else
                            {
                                VisitExpression(assign.Right);
                                var valueReg = AllocateTemporaryRegister();
                                EmitStarRegister(valueReg);
                                EmitPrivateFieldOp(JsOpCode.SetPrivateField, objReg, valueReg, privateBinding.BrandId,
                                    privateBinding.SlotIndex);
                            }
                        }
                        else if (memberLeft.IsComputed)
                        {
                            VisitExpression(memberLeft.Property);
                            var keyReg = AllocateTemporaryRegister();
                            EmitStarRegister(keyReg);
                            if (isCompound)
                            {
                                EmitLdaRegister(objReg);
                                EmitCallRuntime(RuntimeId.RequireObjectCoercible, objReg, 1);
                                EmitLdaRegister(keyReg);
                                EmitCallRuntime(RuntimeId.NormalizePropertyKey, keyReg, 1);
                                EmitStarRegister(keyReg);
                                EmitLdaKeyedProperty(objReg);
                                if (isLogicalAssignment)
                                {
                                    var endLabel = builder.CreateLabel();
                                    var evalLabel =
                                        EmitLogicalAssignmentShortCircuitJump(assign.Operator, endLabel);
                                    if (evalLabel is not null)
                                        builder.BindLabel(evalLabel.Value);

                                    VisitExpression(assign.Right);
                                    EmitStaKeyedProperty(objReg, keyReg);
                                    builder.BindLabel(endLabel);
                                }
                                else
                                {
                                    var lhsValueReg = AllocateTemporaryRegister();
                                    EmitStarRegister(lhsValueReg);
                                    VisitExpression(assign.Right);
                                    EmitRegisterSlotOp(compoundOp, lhsValueReg);
                                    EmitStaKeyedProperty(objReg, keyReg);
                                }
                            }
                            else
                            {
                                VisitExpression(assign.Right);
                                EmitStaKeyedProperty(objReg, keyReg);
                            }
                        }
                        else
                        {
                            if (!TryGetNamedMemberKey(memberLeft, out var memberName))
                                throw new NotImplementedException(
                                    "Only non-computed named member assignment is supported in Okojo Phase 1.");
                            var nameIdx = builder.AddAtomizedStringConstant(memberName);
                            var feedbackSlot = builder.AllocateFeedbackSlot();
                            if (isCompound)
                            {
                                EmitLdaNamedPropertyByIndex(objReg, nameIdx, feedbackSlot);
                                if (isLogicalAssignment)
                                {
                                    var endLabel = builder.CreateLabel();
                                    var evalLabel =
                                        EmitLogicalAssignmentShortCircuitJump(assign.Operator, endLabel);
                                    if (evalLabel is not null)
                                        builder.BindLabel(evalLabel.Value);

                                    VisitExpression(assign.Right);
                                    EmitStaNamedPropertyByIndex(objReg, nameIdx, feedbackSlot);
                                    builder.BindLabel(endLabel);
                                }
                                else
                                {
                                    var lhsValueReg = AllocateTemporaryRegister();
                                    EmitStarRegister(lhsValueReg);
                                    VisitExpression(assign.Right);
                                    EmitRegisterSlotOp(compoundOp, lhsValueReg);
                                    EmitStaNamedPropertyByIndex(objReg, nameIdx, feedbackSlot);
                                }
                            }
                            else
                            {
                                VisitExpression(assign.Right);
                                EmitStaNamedPropertyByIndex(objReg, nameIdx, feedbackSlot);
                            }
                        }
                    }
                    finally
                    {
                        EndTemporaryRegisterScope(tempScope);
                    }
                }
                else if (assign.Left is JsArrayExpression arrayLeft && assign.Operator == JsAssignmentOperator.Assign)
                {
                    EmitArrayDestructuringAssignment(arrayLeft, assign.Right,
                        ShouldInitializeSyntheticForPatternAssignment(assign.Right));
                }
                else if (assign.Left is JsObjectExpression objectLeft && assign.Operator == JsAssignmentOperator.Assign)
                {
                    EmitObjectDestructuringAssignment(objectLeft, assign.Right,
                        ShouldInitializeSyntheticForPatternAssignment(assign.Right));
                }
                else
                {
                    throw new NotImplementedException();
                }

                break;

                static bool ShouldInitializeSyntheticForPatternAssignment(JsExpression right)
                {
                    return right is JsIdentifierExpression { Name: var name } &&
                           name.StartsWith("$forpat_", StringComparison.Ordinal);
                }

            case JsCallExpression call:
            {
                var tempScope = BeginTemporaryRegisterScope();
                try
                {
                    var argsMaySuspend = ArgumentsMaySuspendInCurrentFunction(call.Arguments);
                    var preserveCallState = ArgumentsRequireStableCallState(call.Arguments);
                    if (call.Callee is JsSuperExpression)
                    {
                        if (TryEmitExplicitSuperForwardAllArguments(call.Arguments))
                            break;

                        if (HasSpreadArgument(call.Arguments))
                        {
                            var argStart =
                                EmitSpreadAwareArgumentsIntoContiguousTemporaryRegisters(call.Arguments,
                                    out var flagsReg);
                            var runtimeArgStart = AllocateTemporaryRegisterBlock(1 + call.Arguments.Count);
                            EmitLdaRegister(flagsReg);
                            EmitStarRegister(runtimeArgStart);
                            for (var i = 0; i < call.Arguments.Count; i++)
                            {
                                EmitLdaRegister(argStart + i);
                                EmitStarRegister(runtimeArgStart + 1 + i);
                            }

                            EmitCallRuntime(RuntimeId.CallSuperConstructorWithSpread, runtimeArgStart,
                                1 + call.Arguments.Count);
                        }
                        else
                        {
                            var argStart = EmitArgumentsIntoContiguousTemporaryRegisters(call.Arguments);
                            EmitCallRuntime(RuntimeId.CallSuperConstructor, argStart == -1 ? 0 : argStart,
                                call.Arguments.Count);
                        }

                        if (isDerivedConstructor &&
                            !hasEmittedDeferredInstanceInitializers &&
                            HasPendingInstanceInitializers())
                        {
                            hasEmittedDeferredInstanceInitializers = true;
                            EmitPendingInstanceInitializers();
                        }

                        break;
                    }

                    if (call.Callee is JsMemberExpression { Object: JsSuperExpression } superMember &&
                        !superMember.IsPrivate)
                    {
                        var superGetArgsStart = AllocateTemporaryRegisterBlock(2);
                        var thisReg = superGetArgsStart;
                        var keyReg = superGetArgsStart + 1;
                        EmitPrepareSuperReceiverAndKey(
                            superMember,
                            thisReg,
                            keyReg,
                            "Only named/computed super member calls are supported.",
                            out var superNameIdx);
                        EmitLoadSuperPropertyFromPrepared(superMember, thisReg, superNameIdx);
                        var funcReg = AllocateCallStateRegister("$call.func", preserveCallState);
                        EmitStarRegister(funcReg);

                        if (HasSpreadArgument(call.Arguments))
                        {
                            var argStart =
                                EmitSpreadAwareArgumentsIntoContiguousTemporaryRegisters(call.Arguments,
                                    out var flagsReg);
                            var runtimeArgStart = AllocateTemporaryRegisterBlock(3 + call.Arguments.Count);
                            EmitMoveRegister(funcReg, runtimeArgStart);
                            EmitMoveRegister(thisReg, runtimeArgStart + 1);
                            EmitMoveRegister(flagsReg, runtimeArgStart + 2);
                            for (var i = 0; i < call.Arguments.Count; i++)
                                EmitMoveRegister(argStart + i, runtimeArgStart + 3 + i);

                            EmitCallRuntime(RuntimeId.CallWithSpread, runtimeArgStart, 3 + call.Arguments.Count);
                        }
                        else
                        {
                            var argStart = GetCallArgumentStart(call.Arguments);
                            EmitCallProperty(funcReg, thisReg, argStart == -1 ? 0 : argStart, call.Arguments.Count);
                        }

                        break;
                    }

                    if (call.Callee is JsMemberExpression
                        {
                            Object: JsSuperExpression, IsPrivate: true
                        } superPrivateMember)
                        ThrowUnexpectedPrivateFieldSyntaxError(superPrivateMember.Position);

                    if (call.Callee is JsMemberExpression memberCallee)
                    {
                        int objReg;
                        if (!TryGetPlainLocalReadRegister(memberCallee.Object, out objReg))
                        {
                            VisitExpression(memberCallee.Object);
                            objReg = AllocateCallStateRegister("$call.obj", preserveCallState);
                            EmitStarRegister(objReg);
                        }
                        else if (argsMaySuspend)
                        {
                            objReg = PreserveRegisterForCallState(objReg, "$call.obj", preserveCallState);
                        }

                        void EmitMemberCalleeLoad()
                        {
                            if (memberCallee.IsPrivate)
                            {
                                if (!TryResolvePrivateMemberBinding(memberCallee, out var privateBinding))
                                    throw new NotSupportedException(
                                        "Private member call shape is not supported in Okojo Phase 2.");
                                EmitPrivateFieldOp(JsOpCode.GetPrivateField, objReg, privateBinding.BrandId,
                                    privateBinding.SlotIndex);
                            }
                            else if (memberCallee.IsComputed)
                            {
                                VisitExpression(memberCallee.Property);
                                EmitLdaKeyedProperty(objReg);
                            }
                            else
                            {
                                if (!TryGetNamedMemberKey(memberCallee, out var memberName))
                                    throw new NotImplementedException(
                                        "Only non-private member calls are supported in Okojo Phase 1.");
                                var nameIdx = builder.AddAtomizedStringConstant(memberName);
                                var feedbackSlot = builder.AllocateFeedbackSlot();
                                EmitLdaNamedPropertyByIndex(objReg, nameIdx, feedbackSlot);
                            }
                        }

                        var loadedFuncReg = AllocateCallStateRegister("$call.func", preserveCallState);

                        void EmitLoadedMemberCall()
                        {
                            if (HasSpreadArgument(call.Arguments))
                            {
                                var argStart =
                                    EmitSpreadAwareArgumentsIntoContiguousTemporaryRegisters(call.Arguments,
                                        out var flagsReg);
                                var runtimeArgStart = AllocateTemporaryRegisterBlock(3 + call.Arguments.Count);
                                EmitMoveRegister(loadedFuncReg, runtimeArgStart);
                                EmitMoveRegister(objReg, runtimeArgStart + 1);
                                EmitMoveRegister(flagsReg, runtimeArgStart + 2);
                                for (var i = 0; i < call.Arguments.Count; i++)
                                    EmitMoveRegister(argStart + i, runtimeArgStart + 3 + i);

                                EmitCallRuntime(RuntimeId.CallWithSpread, runtimeArgStart, 3 + call.Arguments.Count);
                            }
                            else
                            {
                                var argStart = GetCallArgumentStart(call.Arguments);
                                EmitCallProperty(loadedFuncReg, objReg, argStart == -1 ? 0 : argStart,
                                    call.Arguments.Count);
                            }
                        }

                        void EmitMemberCall()
                        {
                            EmitMemberCalleeLoad();
                            EmitStarRegister(loadedFuncReg);

                            if (call.IsOptionalChainSegment)
                                EmitOptionalChainShortCircuitLoad(loadedFuncReg, EmitLoadedMemberCall);
                            else
                                EmitLoadedMemberCall();
                        }

                        if (memberCallee.IsOptionalChainSegment)
                            EmitOptionalChainShortCircuitLoad(objReg, EmitMemberCall);
                        else
                            EmitMemberCall();
                    }
                    else
                    {
                        int funcReg;
                        if (!TryGetPlainLocalReadRegister(call.Callee, out funcReg))
                        {
                            VisitExpression(call.Callee);
                            funcReg = AllocateCallStateRegister("$call.func", preserveCallState);
                            EmitStarRegister(funcReg);
                        }
                        else if (argsMaySuspend)
                        {
                            funcReg = PreserveRegisterForCallState(funcReg, "$call.func", preserveCallState);
                        }

                        void EmitDirectCall()
                        {
                            if (HasSpreadArgument(call.Arguments))
                            {
                                var argStart =
                                    EmitSpreadAwareArgumentsIntoContiguousTemporaryRegisters(call.Arguments,
                                        out var flagsReg);
                                var runtimeArgStart = AllocateTemporaryRegisterBlock(3 + call.Arguments.Count);
                                EmitMoveRegister(funcReg, runtimeArgStart);
                                EmitLdaUndefined();
                                EmitStarRegister(runtimeArgStart + 1);
                                EmitMoveRegister(flagsReg, runtimeArgStart + 2);
                                for (var i = 0; i < call.Arguments.Count; i++)
                                    EmitMoveRegister(argStart + i, runtimeArgStart + 3 + i);

                                EmitCallRuntime(RuntimeId.CallWithSpread, runtimeArgStart, 3 + call.Arguments.Count);
                            }
                            else
                            {
                                var argStart = GetCallArgumentStart(call.Arguments);
                                EmitCallUndefinedReceiver(funcReg, argStart == -1 ? 0 : argStart, call.Arguments.Count);
                            }
                        }

                        if (call.IsOptionalChainSegment)
                            EmitOptionalChainShortCircuitLoad(funcReg, EmitDirectCall);
                        else
                            EmitDirectCall();
                    }
                }
                finally
                {
                    EndTemporaryRegisterScope(tempScope);
                }
            }
                break;
            case JsTemplateExpression templateExpr:
                EmitTemplateStringExpression(templateExpr);
                break;
            case JsTaggedTemplateExpression taggedTemplate:
                EmitTaggedTemplateCallExpression(taggedTemplate);
                break;
            case JsNewExpression @new:
            {
                var tempScope = BeginTemporaryRegisterScope();
                try
                {
                    VisitExpression(@new.Callee);
                    var funcReg = AllocateTemporaryRegister();
                    EmitStarRegister(funcReg);

                    if (HasSpreadArgument(@new.Arguments))
                    {
                        var argStart =
                            EmitSpreadAwareArgumentsIntoContiguousTemporaryRegisters(@new.Arguments, out var flagsReg);
                        var runtimeArgStart = AllocateTemporaryRegisterBlock(2 + @new.Arguments.Count);
                        EmitLdaRegister(funcReg);
                        EmitStarRegister(runtimeArgStart);
                        EmitLdaRegister(flagsReg);
                        EmitStarRegister(runtimeArgStart + 1);
                        for (var i = 0; i < @new.Arguments.Count; i++)
                        {
                            EmitLdaRegister(argStart + i);
                            EmitStarRegister(runtimeArgStart + 2 + i);
                        }

                        EmitCallRuntime(RuntimeId.ConstructWithSpread, runtimeArgStart, 2 + @new.Arguments.Count);
                    }
                    else
                    {
                        var argStart = GetCallArgumentStart(@new.Arguments);
                        EmitConstruct(funcReg, argStart == -1 ? 0 : argStart, @new.Arguments.Count);
                    }
                }
                finally
                {
                    EndTemporaryRegisterScope(tempScope);
                }
            }
                break;
            case JsNewTargetExpression:
                if (cachedNewTargetRegister >= 0)
                    EmitLdaRegister(cachedNewTargetRegister);
                else
                    builder.EmitLda(JsOpCode.LdaNewTarget);
                break;
            case JsYieldExpression yield:
            {
                if (functionKind is not (JsBytecodeFunctionKind.Generator or JsBytecodeFunctionKind.AsyncGenerator))
                    throw new NotSupportedException("yield is only valid inside generator functions.");
                if (yield.IsDelegate)
                {
                    EmitYieldDelegateExpression(yield.Argument);
                    break;
                }

                if (yield.Argument is not null)
                    VisitExpression(yield.Argument);
                else
                    EmitLdaUndefined();

                EmitGeneratorSuspendResume(minimizeLiveRange: !resultUsed);
            }
                break;
            case JsAwaitExpression awaitExpr:
            {
                if (functionKind is not (JsBytecodeFunctionKind.Async or JsBytecodeFunctionKind.AsyncGenerator))
                    throw new NotSupportedException("await is only valid inside async functions.");

                VisitExpression(awaitExpr.Argument);
                var guaranteedNextOnly = IsGuaranteedFulfilledAwait(awaitExpr.Argument);
                EmitGeneratorSuspendResume(minimizeLiveRange: true, guaranteedNextOnly: guaranteedNextOnly,
                    isAwaitSuspend: true);
            }
                break;

            case JsFunctionExpression funcExpr:
            {
                EmitFunctionExpression(funcExpr);
            }
                break;
            case JsClassExpression classExpr:
                VisitClassExpression(classExpr);
                break;
            case JsArrayExpression arrExpr:
            {
                var tempScope = BeginTemporaryRegisterScope();
                try
                {
                    var hasSpread = false;
                    for (var i = 0; i < arrExpr.Elements.Count; i++)
                        if (arrExpr.Elements[i] is JsSpreadExpression)
                        {
                            hasSpread = true;
                            break;
                        }

                    if (hasSpread)
                    {
                        var spreadArrReg = AllocateTemporaryRegister();
                        EmitArrayLiteralWithSpreadIntoRegister(arrExpr, spreadArrReg);
                        EmitLdaRegister(spreadArrReg);
                        break;
                    }

                    var arrReg = AllocateTemporaryRegister();
                    EmitArrayLiteralIntoRegister(arrExpr, arrReg);
                    EmitLdaRegister(arrReg);
                }
                finally
                {
                    EndTemporaryRegisterScope(tempScope);
                }
            }
                break;

            case JsObjectExpression objExpr:
            {
                var tempScope = BeginTemporaryRegisterScope();
                try
                {
                    if (objExpr.Properties.Count == 0)
                    {
                        EmitCreateEmptyObjectLiteral();
                        break;
                    }

                    var atomTable = Vm.Atoms;
                    var shapePrefixEnd = objExpr.Properties.Count;
                    var prefixAccessorKindByAtom = new Dictionary<int, bool>();
                    for (var i = 0; i < objExpr.Properties.Count; i++)
                    {
                        var p = objExpr.Properties[i];
                        if (p.Kind == JsObjectPropertyKind.Spread || p.IsComputed ||
                            TryGetCanonicalArrayIndexObjectLiteralKey(p, out _))
                        {
                            shapePrefixEnd = i;
                            break;
                        }

                        var atom = atomTable.InternNoCheck(p.Key);
                        var isAccessor = p.Kind is JsObjectPropertyKind.Getter or JsObjectPropertyKind.Setter;
                        if (prefixAccessorKindByAtom.TryGetValue(atom, out var existingIsAccessor) &&
                            existingIsAccessor != isAccessor)
                        {
                            shapePrefixEnd = i;
                            break;
                        }

                        prefixAccessorKindByAtom[atom] = isAccessor;
                    }

                    var namePlanByProperty = new NamedLiteralPropertyPlan[objExpr.Properties.Count];
                    var orderedUniqueNamedAtoms = new List<int>(objExpr.Properties.Count);
                    var finalFlagsByAtom = new Dictionary<int, JsShapePropertyFlags>();
                    var firstSeen = new HashSet<int>();
                    for (var i = 0; i < shapePrefixEnd; i++)
                    {
                        var prop = objExpr.Properties[i];
                        var atom = atomTable.InternNoCheck(prop.Key);
                        if (firstSeen.Add(atom))
                            orderedUniqueNamedAtoms.Add(atom);

                        var initFlags = prop.Kind switch
                        {
                            JsObjectPropertyKind.Data => JsShapePropertyFlags.Open,
                            JsObjectPropertyKind.Getter => JsShapePropertyFlags.HasGetter,
                            JsObjectPropertyKind.Setter => JsShapePropertyFlags.HasSetter,
                            _ => throw new NotImplementedException(
                                $"Object property kind {prop.Kind} is not supported in Okojo Phase 1.")
                        };

                        if (prop.Kind is JsObjectPropertyKind.Getter or JsObjectPropertyKind.Setter)
                            initFlags |= JsShapePropertyFlags.Enumerable | JsShapePropertyFlags.Configurable;

                        if (!finalFlagsByAtom.TryGetValue(atom, out var currentFlags))
                            finalFlagsByAtom[atom] = NormalizeObjectLiteralFinalFlags(initFlags);
                        else
                            finalFlagsByAtom[atom] = MergeObjectLiteralPropertyFlags(currentFlags, initFlags, prop.Key);

                        namePlanByProperty[i] = new(atom, initFlags);
                    }

                    var shape = Vm.EmptyShape;
                    for (var i = 0; i < orderedUniqueNamedAtoms.Count; i++)
                    {
                        var atom = orderedUniqueNamedAtoms[i];
                        shape = shape.GetOrAddTransition(atom, finalFlagsByAtom[atom], out _);
                    }

                    var literalBoilerplateIdx = builder.AddObjectConstant(shape);
                    EmitCreateObjectLiteralByIndex(literalBoilerplateIdx);

                    var objReg = AllocateTemporaryRegister();
                    EmitStarRegister(objReg);
                    var keyReg = -1;
                    for (var i = 0; i < objExpr.Properties.Count; i++)
                    {
                        var prop = objExpr.Properties[i];
                        if (prop.Kind == JsObjectPropertyKind.Spread)
                        {
                            EmitObjectLiteralSpread(objReg, prop.Value);
                            continue;
                        }

                        if (TryGetCanonicalArrayIndexObjectLiteralKey(prop, out var index))
                        {
                            EmitObjectLiteralIndexedKey(index);
                            if (keyReg == -1)
                                keyReg = AllocateTemporaryRegister();
                            EmitStarRegister(keyReg);
                            if (prop.Kind is JsObjectPropertyKind.Data)
                            {
                                EmitObjectLiteralDataValue(prop, objReg);
                                EmitDefineOwnKeyedProperty(objReg, keyReg);
                            }
                            else if (prop.Kind is JsObjectPropertyKind.Getter or JsObjectPropertyKind.Setter)
                            {
                                EmitDefineObjectLiteralAccessor(objReg, keyReg, prop);
                            }
                            else
                            {
                                throw new NotSupportedException(
                                    $"Object literal property kind {prop.Kind} is not supported in Okojo Phase 2.");
                            }

                            continue;
                        }

                        if (prop.IsComputed)
                        {
                            if (prop.ComputedKey is null)
                                throw new InvalidOperationException(
                                    "Computed object literal key expression is missing.");
                            VisitExpression(prop.ComputedKey);
                            if (keyReg == -1)
                                keyReg = AllocateTemporaryRegister();
                            EmitStarRegister(keyReg);
                            EmitCallRuntime(RuntimeId.NormalizePropertyKey, keyReg, 1);
                            EmitStarRegister(keyReg);
                            if (prop.Kind is JsObjectPropertyKind.Data)
                            {
                                EmitObjectLiteralDataValue(prop, objReg);
                                EmitDefineOwnKeyedProperty(objReg, keyReg);
                            }
                            else if (prop.Kind is JsObjectPropertyKind.Getter or JsObjectPropertyKind.Setter)
                            {
                                EmitDefineObjectLiteralAccessor(objReg, keyReg, prop);
                            }
                            else
                            {
                                throw new NotSupportedException(
                                    $"Object literal property kind {prop.Kind} is not supported in Okojo Phase 2.");
                            }

                            continue;
                        }

                        if (i < shapePrefixEnd)
                        {
                            if (!prop.IsComputed) EmitObjectLiteralDataValue(prop, objReg);

                            if (prop.Kind is JsObjectPropertyKind.Getter or JsObjectPropertyKind.Setter)
                            {
                                if (keyReg == -1)
                                    keyReg = AllocateTemporaryRegister();
                                var keyIdx = builder.AddObjectConstant(prop.Key);
                                EmitLdaStringConstantByIndex(keyIdx);
                                EmitStarRegister(keyReg);
                                EmitDefineObjectLiteralAccessor(objReg, keyReg, prop);
                                continue;
                            }

                            var plan = namePlanByProperty[i];
                            if (!shape.TryGetSlotInfo(plan.Atom, out var slotInfo))
                                throw new InvalidOperationException("Missing precomputed object literal shape slot.");
                            var slot = prop.Kind == JsObjectPropertyKind.Setter &&
                                       (slotInfo.Flags & JsShapePropertyFlags.BothAccessor) ==
                                       JsShapePropertyFlags.BothAccessor
                                ? slotInfo.AccessorSetterSlot
                                : slotInfo.Slot;
                            EmitInitializeNamedProperty(objReg, slot);
                        }
                        else
                        {
                            if (prop.Kind is JsObjectPropertyKind.Data)
                            {
                                if (keyReg == -1)
                                    keyReg = AllocateTemporaryRegister();
                                var keyIdx = builder.AddObjectConstant(prop.Key);
                                EmitLdaStringConstantByIndex(keyIdx);
                                EmitStarRegister(keyReg);
                                EmitObjectLiteralDataValue(prop, objReg);
                                EmitDefineOwnKeyedProperty(objReg, keyReg);
                            }
                            else if (prop.Kind is JsObjectPropertyKind.Getter or JsObjectPropertyKind.Setter)
                            {
                                if (keyReg == -1)
                                    keyReg = AllocateTemporaryRegister();
                                var keyIdx = builder.AddObjectConstant(prop.Key);
                                EmitLdaStringConstantByIndex(keyIdx);
                                EmitStarRegister(keyReg);
                                EmitDefineObjectLiteralAccessor(objReg, keyReg, prop);
                            }
                            else
                            {
                                throw new NotSupportedException(
                                    $"Object literal property kind {prop.Kind} is not supported in Okojo Phase 2.");
                            }
                        }
                    }

                    if (keyReg != -1)
                        ReleaseTemporaryRegister(keyReg);
                    EmitLdaRegister(objReg);
                }
                finally
                {
                    EndTemporaryRegisterScope(tempScope);
                }
            }
                break;

            case JsMemberExpression member:
            {
                var tempScope = BeginTemporaryRegisterScope();
                try
                {
                    if (member.Object is JsSuperExpression)
                    {
                        if (member.IsPrivate)
                            ThrowUnexpectedPrivateFieldSyntaxError(member.Position);

                        var superGetArgsStart = AllocateTemporaryRegisterBlock(2);
                        var thisReg = superGetArgsStart;
                        var keyReg = superGetArgsStart + 1;
                        EmitPrepareSuperReceiverAndKey(
                            member,
                            thisReg,
                            keyReg,
                            "Only named/computed super member access is supported.",
                            out var superNameIdx);
                        EmitLoadSuperPropertyFromPrepared(member, thisReg, superNameIdx);
                        break;
                    }

                    int objReg;
                    if (!TryGetPlainLocalReadRegister(member.Object, out objReg))
                    {
                        VisitExpression(member.Object);
                        objReg = AllocateTemporaryRegister();
                        EmitStarRegister(objReg);
                    }

                    void EmitMemberLoad()
                    {
                        if (member.IsPrivate)
                        {
                            if (!TryResolvePrivateMemberBinding(member, out var privateBinding))
                                throw new NotSupportedException(
                                    "Private member access shape is not supported in Okojo Phase 1.");
                            EmitPrivateFieldOp(JsOpCode.GetPrivateField, objReg, privateBinding.BrandId,
                                privateBinding.SlotIndex);
                            return;
                        }

                        if (member.IsComputed)
                        {
                            VisitExpression(member.Property);
                            EmitLdaKeyedProperty(objReg);
                            return;
                        }

                        if (!TryGetNamedMemberKey(member, out var memberName))
                            throw new NotImplementedException(
                                "Only non-computed named member access is supported in Okojo Phase 1.");
                        var nameIdx = builder.AddAtomizedStringConstant(memberName);
                        var feedbackSlot = builder.AllocateFeedbackSlot();
                        EmitLdaNamedPropertyByIndex(objReg, nameIdx, feedbackSlot);
                    }

                    if (member.IsOptionalChainSegment)
                        EmitOptionalChainShortCircuitLoad(objReg, EmitMemberLoad);
                    else
                        EmitMemberLoad();
                }
                finally
                {
                    EndTemporaryRegisterScope(tempScope);
                }
            }
                break;

            case JsUnaryExpression unary:
                if (TryEmitFoldedUnaryLiteral(unary))
                    break;

                if (unary.Operator == JsUnaryOperator.Delete)
                {
                    EmitDeleteExpression(unary.Argument);
                    break;
                }

                if (unary.Operator == JsUnaryOperator.Typeof)
                {
                    if (unary.Argument is JsIdentifierExpression idArg)
                    {
                        var identifier = CompilerIdentifierName.From(idArg);
                        var binding = ResolveIdentifierReadBinding(identifier);
                        if (binding.Kind != IdentifierReadBindingKind.Global)
                        {
                            VisitExpression(idArg);
                            EmitRaw(JsOpCode.TypeOf);
                        }
                        else
                        {
                            var nameIdx = builder.AddAtomizedStringConstant(idArg.Name);
                            EmitTypeOfGlobalByIndex(nameIdx,
                                builder.GetOrAllocateGlobalBindingFeedbackSlot(idArg.Name));
                        }
                    }
                    else
                    {
                        VisitExpression(unary.Argument);
                        EmitRaw(JsOpCode.TypeOf);
                    }

                    break;
                }

                VisitExpression(unary.Argument);
                switch (unary.Operator)
                {
                    case JsUnaryOperator.Minus:
                        EmitRaw(JsOpCode.ToNumeric);
                        EmitRaw(JsOpCode.Negate);
                        break;
                    case JsUnaryOperator.Plus: EmitRaw(JsOpCode.ToNumber); break;
                    case JsUnaryOperator.LogicalNot: EmitRaw(JsOpCode.LogicalNot); break;
                    case JsUnaryOperator.BitwiseNot:
                        EmitRaw(JsOpCode.ToNumeric);
                        EmitRaw(JsOpCode.BitwiseNot);
                        break;
                    case JsUnaryOperator.Void:
                        EmitLdaUndefined();
                        break;
                    default: throw new NotImplementedException($"Unary {unary.Operator}");
                }

                break;

            case JsUpdateExpression update:
            {
                var tempScope = BeginTemporaryRegisterScope();
                try
                {
                    switch (update.Operator)
                    {
                        case JsUpdateOperator.Increment:
                            break;
                        case JsUpdateOperator.Decrement:
                            break;
                        default:
                            throw new NotImplementedException(update.Operator.ToString());
                    }

                    var isIncrement = update.Operator == JsUpdateOperator.Increment;
                    if (update.Argument is JsIdentifierExpression idArg)
                    {
                        var identifier = CompilerIdentifierName.From(idArg);
                        VisitExpression(idArg);
                        EmitToNumeric();

                        var oldValueReg = -1;
                        if (!update.IsPrefix && resultUsed)
                        {
                            oldValueReg = AllocateTemporaryRegister();
                            EmitStarRegister(oldValueReg);
                        }

                        EmitIncOrDec(isIncrement);
                        StoreIdentifier(identifier);

                        if (!update.IsPrefix && resultUsed)
                            EmitLdaRegister(oldValueReg);
                    }
                    else if (update.Argument is JsMemberExpression memberArg)
                    {
                        if (memberArg.Object is JsSuperExpression)
                        {
                            if (memberArg.IsPrivate)
                                ThrowUnexpectedPrivateFieldSyntaxError(memberArg.Position);

                            var superArgsStart = AllocateTemporaryRegisterBlock(3);
                            var thisReg = superArgsStart;
                            var keyReg = superArgsStart + 1;
                            var valueReg = superArgsStart + 2;

                            EmitPrepareSuperReceiverAndKey(
                                memberArg,
                                thisReg,
                                keyReg,
                                "Super member update requires named or computed property key.",
                                out var superNameIdx);
                            EmitLoadSuperPropertyFromPrepared(memberArg, thisReg, superNameIdx);
                            EmitToNumeric();
                            var oldValueRegSuper = -1;
                            if (!update.IsPrefix && resultUsed)
                            {
                                oldValueRegSuper = AllocateTemporaryRegister();
                                EmitStarRegister(oldValueRegSuper);
                            }

                            EmitIncOrDec(isIncrement);
                            EmitStarRegister(valueReg);
                            EmitStoreSuperPropertyFromPrepared(thisReg);

                            if (!update.IsPrefix && resultUsed)
                                EmitLdaRegister(oldValueRegSuper);
                            break;
                        }

                        int objReg;
                        if (!TryGetPlainLocalReadRegister(memberArg.Object, out objReg))
                        {
                            VisitExpression(memberArg.Object);
                            objReg = AllocateTemporaryRegister();
                            EmitStarRegister(objReg);
                        }

                        var oldValueReg = -1;
                        if (memberArg.IsPrivate)
                        {
                            if (!TryResolvePrivateMemberBinding(memberArg, out var privateBinding))
                                throw new NotSupportedException(
                                    "Private member update shape is not supported in Okojo Phase 1.");

                            EmitPrivateFieldOp(JsOpCode.GetPrivateField, objReg, privateBinding.BrandId,
                                privateBinding.SlotIndex);
                            EmitToNumeric();

                            if (!update.IsPrefix && resultUsed)
                            {
                                oldValueReg = AllocateTemporaryRegister();
                                EmitStarRegister(oldValueReg);
                            }

                            EmitIncOrDec(isIncrement);
                            var valueReg = AllocateTemporaryRegister();
                            EmitStarRegister(valueReg);
                            EmitPrivateFieldOp(JsOpCode.SetPrivateField, objReg, valueReg, privateBinding.BrandId,
                                privateBinding.SlotIndex);
                        }
                        else if (memberArg.IsComputed)
                        {
                            VisitExpression(memberArg.Property);
                            var keyReg = AllocateTemporaryRegister();
                            EmitStarRegister(keyReg);
                            EmitLdaRegister(objReg);
                            EmitCallRuntime(RuntimeId.RequireObjectCoercible, objReg, 1);
                            EmitLdaRegister(keyReg);
                            EmitCallRuntime(RuntimeId.NormalizePropertyKey, keyReg, 1);
                            EmitStarRegister(keyReg);
                            EmitLdaRegister(keyReg);
                            EmitLdaKeyedProperty(objReg);
                            EmitToNumeric();

                            if (!update.IsPrefix && resultUsed)
                            {
                                oldValueReg = AllocateTemporaryRegister();
                                EmitStarRegister(oldValueReg);
                            }

                            EmitIncOrDec(isIncrement);
                            EmitStaKeyedProperty(objReg, keyReg);
                        }
                        else
                        {
                            if (!TryGetNamedMemberKey(memberArg, out var memberName))
                                throw new NotImplementedException(
                                    "Only non-computed named member update is supported in Okojo Phase 1.");

                            var nameIdx = builder.AddAtomizedStringConstant(memberName);
                            var feedbackSlot = builder.AllocateFeedbackSlot();
                            EmitLdaNamedPropertyByIndex(objReg, nameIdx, feedbackSlot);
                            EmitToNumeric();

                            if (!update.IsPrefix && resultUsed)
                            {
                                oldValueReg = AllocateTemporaryRegister();
                                EmitStarRegister(oldValueReg);
                            }

                            EmitIncOrDec(isIncrement);
                            EmitStaNamedPropertyByIndex(objReg, nameIdx, feedbackSlot);
                        }

                        if (!update.IsPrefix && resultUsed)
                            EmitLdaRegister(oldValueReg);
                    }
                    else
                    {
                        throw new NotImplementedException(
                            "Update expressions support identifier/member operands only in Okojo Phase 2.");
                    }
                }
                finally
                {
                    EndTemporaryRegisterScope(tempScope);
                }
            }
                break;

            case JsBinaryExpression bin:
            {
                var tempScope = BeginTemporaryRegisterScope();
                try
                {
                    if (bin.Operator == JsBinaryOperator.In &&
                        bin.Left is JsPrivateIdentifierExpression privateIdentifier)
                    {
                        if (!TryResolvePrivateIdentifierBinding(privateIdentifier, out var privateBinding))
                            throw new NotSupportedException("Private identifier `in` binding could not be resolved.");

                        VisitExpression(bin.Right);
                        var argStart = AllocateTemporaryRegisterBlock(3);
                        EmitStarRegister(argStart);
                        EmitLda(privateBinding.BrandId);
                        EmitStarRegister(argStart + 1);
                        EmitLda(privateBinding.SlotIndex);
                        EmitStarRegister(argStart + 2);
                        EmitCallRuntime(RuntimeId.HasPrivateField, argStart, 3);
                        break;
                    }

                    if (bin.Operator is JsBinaryOperator.LogicalAnd or JsBinaryOperator.LogicalOr)
                    {
                        var endLabel = builder.CreateLabel();
                        VisitExpression(bin.Left);
                        if (bin.Operator == JsBinaryOperator.LogicalAnd)
                            builder.EmitJump(JsOpCode.JumpIfToBooleanFalse, endLabel);
                        else
                            builder.EmitJump(JsOpCode.JumpIfToBooleanTrue, endLabel);
                        VisitExpression(bin.Right);
                        builder.BindLabel(endLabel);
                        break;
                    }

                    if (bin.Operator == JsBinaryOperator.NullishCoalescing)
                    {
                        VisitExpression(bin.Left);
                        var leftReg = AllocateTemporaryRegister();
                        EmitStarRegister(leftReg);
                        var rightLabel = builder.CreateLabel();
                        var endLabel = builder.CreateLabel();
                        builder.EmitJump(JsOpCode.JumpIfNull, rightLabel);
                        EmitLdaRegister(leftReg);
                        builder.EmitJump(JsOpCode.JumpIfUndefined, rightLabel);
                        builder.EmitJump(JsOpCode.Jump, endLabel);
                        builder.BindLabel(rightLabel);
                        VisitExpression(bin.Right);
                        builder.BindLabel(endLabel);
                        break;
                    }

                    if (TryGetSmiImmediate(bin.Right, out var rhsSmi) && IsSmiSpecializableBinaryOperator(bin.Operator))
                    {
                        VisitExpression(bin.Left);
                        switch (bin.Operator)
                        {
                            case JsBinaryOperator.Add: EmitRaw(JsOpCode.AddSmi, (byte)rhsSmi, 0); break;
                            case JsBinaryOperator.Subtract: EmitRaw(JsOpCode.SubSmi, (byte)rhsSmi, 0); break;
                            case JsBinaryOperator.Multiply: EmitRaw(JsOpCode.MulSmi, (byte)rhsSmi, 0); break;
                            case JsBinaryOperator.Modulo: EmitRaw(JsOpCode.ModSmi, (byte)rhsSmi, 0); break;
                            case JsBinaryOperator.Exponentiate: EmitRaw(JsOpCode.ExpSmi, (byte)rhsSmi, 0); break;
                            case JsBinaryOperator.LessThan: EmitRaw(JsOpCode.TestLessThanSmi, (byte)rhsSmi, 0); break;
                            case JsBinaryOperator.GreaterThan:
                                EmitRaw(JsOpCode.TestGreaterThanSmi, (byte)rhsSmi, 0); break;
                            case JsBinaryOperator.LessThanOrEqual:
                                EmitRaw(JsOpCode.TestLessThanOrEqualSmi, (byte)rhsSmi, 0); break;
                            case JsBinaryOperator.GreaterThanOrEqual:
                                EmitRaw(JsOpCode.TestGreaterThanOrEqualSmi, (byte)rhsSmi, 0); break;
                        }

                        break;
                    }

                    if (TryGetPlainLocalReadRegister(bin.Left, out var lhsDirectReg) &&
                        TryGetPlainLocalReadRegister(bin.Right, out var rhsDirectReg) &&
                        TryMapBinaryOperatorToOkojoOpCode(bin.Operator, out var directOp))
                    {
                        EmitLdaRegister(rhsDirectReg);
                        EmitRegisterSlotOp(directOp, lhsDirectReg);
                        break;
                    }

                    if (TryGetPlainLocalReadRegister(bin.Left, out lhsDirectReg) &&
                        bin.Right is JsLiteralExpression &&
                        TryMapBinaryOperatorToOkojoOpCode(bin.Operator, out directOp))
                    {
                        VisitExpression(bin.Right);
                        EmitRegisterSlotOp(directOp, lhsDirectReg);
                        break;
                    }

                    VisitExpression(bin.Left);
                    var lhsReg = AllocateTemporaryRegister();
                    EmitStarRegister(lhsReg);

                    VisitExpression(bin.Right);
                    switch (bin.Operator)
                    {
                        case JsBinaryOperator.Add: EmitRegisterSlotOp(JsOpCode.Add, lhsReg); break;
                        case JsBinaryOperator.Subtract: EmitRegisterSlotOp(JsOpCode.Sub, lhsReg); break;
                        case JsBinaryOperator.Multiply: EmitRegisterSlotOp(JsOpCode.Mul, lhsReg); break;
                        case JsBinaryOperator.Divide: EmitRegisterSlotOp(JsOpCode.Div, lhsReg); break;
                        case JsBinaryOperator.Modulo: EmitRegisterSlotOp(JsOpCode.Mod, lhsReg); break;
                        case JsBinaryOperator.Exponentiate: EmitRegisterSlotOp(JsOpCode.Exp, lhsReg); break;
                        case JsBinaryOperator.BitwiseAnd: EmitRegisterSlotOp(JsOpCode.BitwiseAnd, lhsReg); break;
                        case JsBinaryOperator.BitwiseOr: EmitRegisterSlotOp(JsOpCode.BitwiseOr, lhsReg); break;
                        case JsBinaryOperator.BitwiseXor: EmitRegisterSlotOp(JsOpCode.BitwiseXor, lhsReg); break;
                        case JsBinaryOperator.ShiftLeft: EmitRegisterSlotOp(JsOpCode.ShiftLeft, lhsReg); break;
                        case JsBinaryOperator.ShiftRight: EmitRegisterSlotOp(JsOpCode.ShiftRight, lhsReg); break;
                        case JsBinaryOperator.ShiftRightLogical:
                            EmitRegisterSlotOp(JsOpCode.ShiftRightLogical, lhsReg); break;
                        case JsBinaryOperator.LessThan: EmitRegisterSlotOp(JsOpCode.TestLessThan, lhsReg); break;
                        case JsBinaryOperator.GreaterThan: EmitRegisterSlotOp(JsOpCode.TestGreaterThan, lhsReg); break;
                        case JsBinaryOperator.LessThanOrEqual:
                            EmitRegisterSlotOp(JsOpCode.TestLessThanOrEqual, lhsReg); break;
                        case JsBinaryOperator.GreaterThanOrEqual:
                            EmitRegisterSlotOp(JsOpCode.TestGreaterThanOrEqual, lhsReg); break;
                        case JsBinaryOperator.Equal: EmitRegisterSlotOp(JsOpCode.TestEqual, lhsReg); break;
                        case JsBinaryOperator.NotEqual: EmitRegisterSlotOp(JsOpCode.TestNotEqual, lhsReg); break;
                        case JsBinaryOperator.StrictEqual: EmitTestEqualStrictRegister(lhsReg); break;
                        case JsBinaryOperator.In: EmitRegisterSlotOp(JsOpCode.TestIn, lhsReg); break;
                        case JsBinaryOperator.Instanceof: EmitRegisterSlotOp(JsOpCode.TestInstanceOf, lhsReg); break;
                        case JsBinaryOperator.StrictNotEqual:
                            EmitTestEqualStrictRegister(lhsReg);
                            EmitRaw(JsOpCode.LogicalNot);
                            break;
                        default: throw new NotImplementedException(bin.Operator.ToString());
                    }

                    break;
                }
                finally
                {
                    EndTemporaryRegisterScope(tempScope);
                }
            }
            case JsConditionalExpression conditional:
            {
                var elseLabel = builder.CreateLabel();
                var endLabel = builder.CreateLabel();
                VisitExpression(conditional.Test);
                builder.EmitJump(JsOpCode.JumpIfToBooleanFalse, elseLabel);
                VisitExpression(conditional.Consequent);
                if (directReturn && CanReturnNormally)
                    EmitRaw(JsOpCode.Return);
                else
                    builder.EmitJump(JsOpCode.Jump, endLabel);
                builder.BindLabel(elseLabel);
                VisitExpression(conditional.Alternate);
                builder.BindLabel(endLabel);
            }
                break;
            case JsSequenceExpression sequence:
            {
                if (sequence.Expressions.Count == 0)
                {
                    EmitLdaUndefined();
                    break;
                }

                for (var i = 0; i < sequence.Expressions.Count; i++)
                    VisitExpression(sequence.Expressions[i],
                        i == sequence.Expressions.Count - 1 && resultUsed);
            }
                break;
            default: throw new NotImplementedException(expr.GetType().Name);
        }
    }

    private BytecodeBuilder.Label? EmitLogicalAssignmentShortCircuitJump(JsAssignmentOperator op,
        BytecodeBuilder.Label endLabel)
    {
        switch (op)
        {
            case JsAssignmentOperator.LogicalAndAssign:
                builder.EmitJump(JsOpCode.JumpIfToBooleanFalse, endLabel);
                return null;
            case JsAssignmentOperator.LogicalOrAssign:
                builder.EmitJump(JsOpCode.JumpIfToBooleanTrue, endLabel);
                return null;
            case JsAssignmentOperator.NullishCoalescingAssign:
            {
                var evalLabel = builder.CreateLabel();
                builder.EmitJump(JsOpCode.JumpIfNull, evalLabel);
                builder.EmitJump(JsOpCode.JumpIfUndefined, evalLabel);
                builder.EmitJump(JsOpCode.Jump, endLabel);
                return evalLabel;
            }
            default:
                throw new NotImplementedException($"Assignment operator {op}");
        }
    }

    private readonly record struct ArrayAssignmentElement(
        JsExpression? Target,
        JsExpression? DefaultExpression,
        bool IsElision,
        bool IsRest);

    private readonly record struct ObjectAssignmentElement(
        string? StaticSourceKey,
        JsExpression? ComputedSourceKey,
        JsExpression Target,
        JsExpression? DefaultExpression,
        bool IsRest);

    private readonly record struct PreparedDestructuringTarget(
        CompilerIdentifierName? Identifier,
        int ObjectRegister,
        int RawKeyRegister,
        string? StaticMemberKey,
        PrivateFieldBinding? PrivateBinding = null);

    private readonly record struct ObjectRestExcludedKey(string? StaticSourceKey, int ComputedKeyRegister);
}
