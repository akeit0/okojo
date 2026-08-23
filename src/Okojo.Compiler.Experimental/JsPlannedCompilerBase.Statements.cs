using System.Buffers;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal abstract partial class JsPlannedCompilerBase
{
    protected void EmitStatement(FlatAst ast, int nodeIndex)
    {
        ref readonly var node = ref ast[nodeIndex];
        switch (node.Kind)
        {
            case AstKind.VariableDeclaration:
                EmitVariableDeclaration(ast, node.Arg0, node.Arg1);
                return;
            case AstKind.FunctionDeclaration:
                EmitFunctionDeclaration(ast, node.Arg0, node.Arg1);
                return;
            case AstKind.BlockStatement:
                EmitBlockStatement(ast, nodeIndex);
                return;
            case AstKind.IfStatement:
                EmitIfStatement(ast, node.Arg0, node.Arg1, node.Arg2);
                return;
            case AstKind.WhileStatement:
                EmitWhileStatement(ast, node.Arg0, node.Arg1);
                return;
            case AstKind.DoWhileStatement:
                EmitDoWhileStatement(ast, node.Arg0, node.Arg1);
                return;
            case AstKind.ForStatement:
                EmitForStatement(ast, nodeIndex, node);
                return;
            case AstKind.BreakStatement:
                EmitLoopControl(isContinue: false);
                return;
            case AstKind.ContinueStatement:
                EmitLoopControl(isContinue: true);
                return;
            case AstKind.ExpressionStatement:
                EmitExpression(ast, node.Arg0);
                return;
            case AstKind.ReturnStatement:
                if (node.Arg0 >= 0)
                    EmitExpression(ast, node.Arg0);
                else
                    builder.EmitLda(JsOpCode.LdaUndefined);
                builder.Emit(JsOpCode.Return);
                return;
            case AstKind.EmptyStatement:
                builder.EmitLda(JsOpCode.LdaUndefined);
                return;
            default:
                throw new NotSupportedException(
                    $"{CompilerName} does not support flat statement '{node.Kind}'."
                );
        }
    }

    private void EmitWhileStatement(FlatAst ast, int test, int body)
    {
        var continueTarget = builder.CreateLabel();
        var breakTarget = builder.CreateLabel();
        builder.BindLabel(continueTarget);
        EmitExpression(ast, test);
        EmitJumpIfToBooleanFalse(breakTarget);
        PushLoopTargets(breakTarget, continueTarget);
        try
        {
            EmitStatement(ast, body);
        }
        finally
        {
            loopTargets.Pop();
        }
        EmitJump(continueTarget);
        builder.BindLabel(breakTarget);
    }

    private void EmitDoWhileStatement(FlatAst ast, int body, int test)
    {
        var loopStart = builder.CreateLabel();
        var continueTarget = builder.CreateLabel();
        var breakTarget = builder.CreateLabel();
        builder.BindLabel(loopStart);
        PushLoopTargets(breakTarget, continueTarget);
        try
        {
            EmitStatement(ast, body);
        }
        finally
        {
            loopTargets.Pop();
        }
        builder.BindLabel(continueTarget);
        EmitExpression(ast, test);
        EmitJumpIfToBooleanFalse(breakTarget);
        EmitJump(loopStart);
        builder.BindLabel(breakTarget);
    }

    private void EmitForStatement(FlatAst ast, int nodeIndex, AstNode node)
    {
        var parts = ast.ChildRange(node.Arg0, node.Arg1);
        var init = parts[0];
        var hasLexicalScope =
            init >= 0
            && ast[init].Kind == AstKind.VariableDeclaration
            && (JsVariableDeclarationKind)ast[init].Arg2
                is JsVariableDeclarationKind.Let
                    or JsVariableDeclarationKind.Const;
        if (hasLexicalScope)
        {
            var scope = FindChildScope(
                activeScopes.Peek().ScopeId,
                CompilerCollectedScopeKind.Block,
                ast.GetPosition(nodeIndex)
            );
            EnterScope(scope.ScopeId);
        }

        var needsPerIterationContext =
            hasLexicalScope && ShouldReplaceLoopHeadContextPerIteration(activeScopes.Peek());

        try
        {
            if (init >= 0)
            {
                if (ast[init].Kind == AstKind.VariableDeclaration)
                    EmitStatement(ast, init);
                else
                    EmitExpression(ast, init);
            }

            if (needsPerIterationContext)
                EmitReplaceCurrentContext(activeScopes.Peek().ContextSlotCount);

            var loopStart = builder.CreateLabel();
            var continueTarget = builder.CreateLabel();
            var breakTarget = builder.CreateLabel();
            builder.BindLabel(loopStart);
            if (parts[1] >= 0)
            {
                EmitExpression(ast, parts[1]);
                EmitJumpIfToBooleanFalse(breakTarget);
            }

            PushLoopTargets(breakTarget, continueTarget);
            try
            {
                EmitStatement(ast, parts[3]);
            }
            finally
            {
                loopTargets.Pop();
            }

            builder.BindLabel(continueTarget);
            if (needsPerIterationContext)
                EmitReplaceCurrentContext(activeScopes.Peek().ContextSlotCount);
            if (parts[2] >= 0)
                EmitExpression(ast, parts[2]);
            EmitJump(loopStart);
            builder.BindLabel(breakTarget);
        }
        finally
        {
            if (hasLexicalScope)
                LeaveScope();
        }
    }

    private static bool ShouldReplaceLoopHeadContextPerIteration(in ActiveScope scope)
    {
        for (var i = 0; i < scope.Bindings.Count; i++)
        {
            var binding = scope.Bindings[i].Planned;
            if (
                binding.Kind == CompilerCollectedBindingKind.LoopHeadAlias
                && binding.IsCaptured
                && binding.StorageKind == CompilerPlannedStorageKind.ContextSlot
            )
                return true;
        }

        return false;
    }

    private void EmitReplaceCurrentContext(int slotCount)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var copyStart = builder.AllocateTemporaryRegisterBlock(slotCount);
            for (var slot = 0; slot < slotCount; slot++)
            {
                EmitLdaCurrentContextSlot(slot, skipTdz: true);
                EmitStar(copyStart + slot);
            }

            EmitPopContext();
            EmitCreateFunctionContextWithCells(slotCount);
            for (var slot = 0; slot < slotCount; slot++)
            {
                EmitLdar(copyStart + slot);
                EmitStaCurrentContextSlot(slot);
            }
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void PushLoopTargets(
        BytecodeBuilder.Label breakTarget,
        BytecodeBuilder.Label continueTarget
    )
    {
        loopTargets.Push(new LoopTargets(breakTarget, continueTarget, CurrentContextDepth));
    }

    private void EmitLoopControl(bool isContinue)
    {
        if (loopTargets.Count == 0)
            throw new InvalidOperationException("Loop control emitted without an active loop.");
        var target = loopTargets.Peek();
        for (var depth = CurrentContextDepth; depth > target.ContextDepth; depth--)
            EmitPopContext();
        EmitJump(isContinue ? target.Continue : target.Break);
    }

    private void EmitBlockStatement(FlatAst ast, int nodeIndex)
    {
        ref readonly var block = ref ast[nodeIndex];
        var childScope = FindChildScope(
            activeScopes.Peek().ScopeId,
            CompilerCollectedScopeKind.Block,
            ast.GetPosition(nodeIndex)
        );
        EnterScope(childScope.ScopeId);
        try
        {
            var statements = ast.ChildRange(block.Arg0, block.Arg1);
            for (var i = 0; i < statements.Length; i++)
                EmitStatement(ast, statements[i]);
        }
        finally
        {
            LeaveScope();
        }
    }

    private void EmitIfStatement(FlatAst ast, int test, int consequent, int alternate)
    {
        EmitExpression(ast, test);
        var elseLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        EmitJumpIfToBooleanFalse(elseLabel);
        EmitStatement(ast, consequent);
        if (alternate >= 0)
        {
            EmitJump(endLabel);
            builder.BindLabel(elseLabel);
            EmitStatement(ast, alternate);
            builder.BindLabel(endLabel);
        }
        else
        {
            builder.BindLabel(elseLabel);
        }
    }

    private void EmitVariableDeclaration(FlatAst ast, int offset, int count)
    {
        var declarators = ast.ChildRange(offset, count);
        for (var i = 0; i < declarators.Length; i++)
        {
            ref readonly var declarator = ref ast[declarators[i]];
            if (declarator.Kind == AstKind.VariableDeclaratorPattern)
            {
                var marker = builder.GetTemporaryRegisterScopeMarker();
                try
                {
                    EmitExpression(ast, declarator.Arg1);
                    EmitStoreBindingTarget(ast, declarator.Arg0);
                }
                finally
                {
                    builder.ReleaseTemporaryRegistersToMarker(marker);
                }
                continue;
            }

            var name = ast.GetString(declarator.Arg0);
            if (!TryResolveBinding(name, out var binding))
                throw new InvalidOperationException($"No planned binding found for '{name}'.");

            if (declarator.Arg2 >= 0)
                EmitExpression(ast, declarator.Arg2);
            else
                builder.EmitLda(JsOpCode.LdaUndefined);

            EmitStore(binding);
        }
    }

    private void EmitStoreBindingTarget(FlatAst ast, int targetIndex)
    {
        ref readonly var target = ref ast[targetIndex];
        if (target.Kind == AstKind.Identifier)
        {
            var name = ast.GetString(target.Arg0);
            if (!TryResolveBinding(name, out var binding))
                throw new InvalidOperationException($"No planned binding found for '{name}'.");
            EmitStore(binding);
            return;
        }

        if (target.Kind is not (AstKind.ArrayBindingPattern or AstKind.ObjectBindingPattern))
            throw new NotSupportedException(
                $"{CompilerName} does not support binding target '{target.Kind}'."
            );

        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var valueRegister = builder.AllocateTemporaryRegister();
            EmitStar(valueRegister);
            if (target.Kind == AstKind.ArrayBindingPattern)
                EmitArrayBindingPattern(ast, target, valueRegister);
            else
                EmitObjectBindingPattern(ast, target, valueRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitArrayBindingPattern(FlatAst ast, AstNode pattern, int valueRegister)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            EmitLdar(valueRegister);
            builder.EmitCallRuntime(
                (int)RuntimeId.CreateArrayDestructureIterator,
                valueRegister,
                1
            );
            var iteratorRegister = builder.AllocateTemporaryRegister();
            EmitStar(iteratorRegister);
            var doneRegister = builder.AllocateTemporaryRegister();
            builder.EmitLda(JsOpCode.LdaFalse);
            EmitStar(doneRegister);
            var catchLabel = builder.CreateLabel();
            var endLabel = builder.CreateLabel();

            builder.EmitJump(JsOpCode.PushTry, catchLabel);
            var elements = ast.ChildRange(pattern.Arg0, pattern.Arg1);
            for (var i = 0; i < elements.Length; i++)
            {
                if (elements[i] < 0)
                {
                    EmitArrayBindingElision(iteratorRegister, doneRegister, valueRegister);
                    continue;
                }

                ref readonly var element = ref ast[elements[i]];
                if (element.Kind == AstKind.SpreadElement)
                {
                    EmitArrayBindingRest(
                        ast,
                        element.Arg0,
                        iteratorRegister,
                        doneRegister,
                        valueRegister
                    );
                    continue;
                }

                var targetIndex = elements[i];
                var defaultIndex = -1;
                if (
                    element.Kind == AstKind.AssignmentExpression
                    && (JsAssignmentOperator)element.Arg2 == JsAssignmentOperator.Assign
                )
                {
                    targetIndex = element.Arg0;
                    defaultIndex = element.Arg1;
                }

                EmitArrayBindingStep(iteratorRegister, doneRegister, valueRegister);
                if (defaultIndex >= 0)
                    EmitBindingDefault(ast, defaultIndex, valueRegister);
                EmitStoreBindingTarget(ast, targetIndex);
            }
            builder.Emit(JsOpCode.PopTry);
            EmitCloseArrayBindingIterator(iteratorRegister, doneRegister);
            EmitJump(endLabel);

            builder.BindLabel(catchLabel);
            EmitStar(valueRegister);
            var rethrowLabel = builder.CreateLabel();
            EmitLdar(doneRegister);
            EmitJumpIfToBooleanTrue(rethrowLabel);
            EmitLdar(iteratorRegister);
            builder.EmitCallRuntime(
                (int)RuntimeId.DestructureIteratorCloseBestEffort,
                iteratorRegister,
                1
            );
            builder.BindLabel(rethrowLabel);
            EmitLdar(valueRegister);
            builder.Emit(JsOpCode.Throw);
            builder.BindLabel(endLabel);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitArrayBindingStep(int iteratorRegister, int doneRegister, int valueRegister)
    {
        var doneLabel = builder.CreateLabel();
        var hasValueLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        EmitLdar(doneRegister);
        EmitJumpIfToBooleanTrue(doneLabel);
        EmitLdar(iteratorRegister);
        builder.EmitCallRuntime((int)RuntimeId.DestructureIteratorStepValue, iteratorRegister, 1);
        EmitStar(valueRegister);
        builder.EmitLda(JsOpCode.LdaTheHole);
        EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, valueRegister);
        EmitJumpIfToBooleanFalse(hasValueLabel);
        builder.EmitLda(JsOpCode.LdaTrue);
        EmitStar(doneRegister);
        builder.BindLabel(doneLabel);
        builder.EmitLda(JsOpCode.LdaUndefined);
        EmitJump(endLabel);
        builder.BindLabel(hasValueLabel);
        EmitLdar(valueRegister);
        builder.BindLabel(endLabel);
    }

    private void EmitArrayBindingElision(int iteratorRegister, int doneRegister, int valueRegister)
    {
        var endLabel = builder.CreateLabel();
        EmitLdar(doneRegister);
        EmitJumpIfToBooleanTrue(endLabel);
        EmitLdar(iteratorRegister);
        builder.EmitCallRuntime((int)RuntimeId.DestructureIteratorStepValue, iteratorRegister, 1);
        EmitStar(valueRegister);
        builder.EmitLda(JsOpCode.LdaTheHole);
        EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, valueRegister);
        EmitJumpIfToBooleanFalse(endLabel);
        builder.EmitLda(JsOpCode.LdaTrue);
        EmitStar(doneRegister);
        builder.BindLabel(endLabel);
    }

    private void EmitArrayBindingRest(
        FlatAst ast,
        int targetIndex,
        int iteratorRegister,
        int doneRegister,
        int valueRegister
    )
    {
        var emptyLabel = builder.CreateLabel();
        var storeLabel = builder.CreateLabel();
        EmitLdar(doneRegister);
        EmitJumpIfToBooleanTrue(emptyLabel);
        EmitLdar(iteratorRegister);
        builder.EmitCallRuntime((int)RuntimeId.DestructureIteratorRestArray, iteratorRegister, 1);
        EmitStar(valueRegister);
        builder.EmitLda(JsOpCode.LdaTrue);
        EmitStar(doneRegister);
        EmitLdar(valueRegister);
        EmitJump(storeLabel);
        builder.BindLabel(emptyLabel);
        builder.EmitCreateArrayLiteral(builder.AddObjectConstant(0));
        builder.BindLabel(storeLabel);
        EmitStoreBindingTarget(ast, targetIndex);
    }

    private void EmitBindingDefault(FlatAst ast, int defaultIndex, int valueRegister)
    {
        var useDefaultLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        EmitStar(valueRegister);
        EmitLdar(valueRegister);
        EmitJumpIfUndefined(useDefaultLabel);
        EmitJump(endLabel);
        builder.BindLabel(useDefaultLabel);
        EmitExpression(ast, defaultIndex);
        builder.BindLabel(endLabel);
    }

    private void EmitCloseArrayBindingIterator(int iteratorRegister, int doneRegister)
    {
        var endLabel = builder.CreateLabel();
        EmitLdar(doneRegister);
        EmitJumpIfToBooleanTrue(endLabel);
        EmitLdar(iteratorRegister);
        builder.EmitCallRuntime((int)RuntimeId.DestructureIteratorClose, iteratorRegister, 1);
        builder.BindLabel(endLabel);
    }

    private void EmitObjectBindingPattern(FlatAst ast, AstNode pattern, int sourceRegister)
    {
        var properties = ast.GetObjectProperties(pattern.Arg0, pattern.Arg1);
        var restIndex = -1;
        for (var i = 0; i < properties.Length; i++)
            if (properties[i].IsRest)
            {
                restIndex = i;
                break;
            }
        var computedKeyRegisters = restIndex >= 0 ? ArrayPool<int>.Shared.Rent(restIndex) : null;
        if (computedKeyRegisters is not null)
            Array.Fill(computedKeyRegisters, -1, 0, restIndex);
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            EmitLdar(sourceRegister);
            builder.EmitCallRuntime((int)RuntimeId.RequireObjectCoercible, sourceRegister, 1);
            var propertyValueRegister = -1;
            var reusableKeyRegister = -1;
            for (var i = 0; i < properties.Length; i++)
            {
                ref readonly var property = ref properties[i];
                if (property.IsRest)
                {
                    EmitObjectBindingRest(
                        ast,
                        property.ValueNode,
                        sourceRegister,
                        properties[..i],
                        computedKeyRegisters!.AsSpan(0, i)
                    );
                    continue;
                }

                if (property.IsComputed)
                {
                    EmitExpression(ast, property.Key);
                    var keyRegister =
                        restIndex >= 0 ? builder.AllocateTemporaryRegister()
                        : reusableKeyRegister >= 0 ? reusableKeyRegister
                        : reusableKeyRegister = builder.AllocateTemporaryRegister();
                    EmitStar(keyRegister);
                    builder.EmitCallRuntime((int)RuntimeId.NormalizePropertyKey, keyRegister, 1);
                    EmitStar(keyRegister);
                    if (computedKeyRegisters is not null)
                        computedKeyRegisters[i] = keyRegister;
                    EmitLdar(keyRegister);
                    builder.EmitLdaKeyedProperty(sourceRegister);
                }
                else
                {
                    var name = ast.GetString(property.Key);
                    if (AtomTable.TryGetArrayIndexFromCanonicalString(name, out var index))
                    {
                        EmitNumericLiteral(index);
                        builder.EmitLdaKeyedProperty(sourceRegister);
                    }
                    else
                    {
                        builder.EmitLdaNamedProperty(
                            sourceRegister,
                            builder.AddAtomizedStringConstant(name),
                            builder.AllocateFeedbackSlot()
                        );
                    }
                }

                var targetIndex = property.ValueNode;
                var defaultIndex = -1;
                ref readonly var target = ref ast[targetIndex];
                if (
                    target.Kind == AstKind.AssignmentExpression
                    && (JsAssignmentOperator)target.Arg2 == JsAssignmentOperator.Assign
                )
                {
                    targetIndex = target.Arg0;
                    defaultIndex = target.Arg1;
                }
                if (defaultIndex >= 0)
                {
                    if (propertyValueRegister < 0)
                        propertyValueRegister = builder.AllocateTemporaryRegister();
                    EmitBindingDefault(ast, defaultIndex, propertyValueRegister);
                }
                EmitStoreBindingTarget(ast, targetIndex);
            }
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
            if (computedKeyRegisters is not null)
                ArrayPool<int>.Shared.Return(computedKeyRegisters);
        }
    }

    private void EmitObjectBindingRest(
        FlatAst ast,
        int targetIndex,
        int sourceRegister,
        ReadOnlySpan<FlatObjectProperty> excludedProperties,
        ReadOnlySpan<int> computedKeyRegisters
    )
    {
        builder.Emit(JsOpCode.CreateEmptyObjectLiteral);
        var restRegister = builder.AllocateTemporaryRegister();
        EmitStar(restRegister);
        var argumentStart = builder.AllocateTemporaryRegisterBlock(2 + excludedProperties.Length);
        EmitLdar(restRegister);
        EmitStar(argumentStart);
        EmitLdar(sourceRegister);
        EmitStar(argumentStart + 1);
        for (var i = 0; i < excludedProperties.Length; i++)
        {
            if (computedKeyRegisters[i] >= 0)
                EmitLdar(computedKeyRegisters[i]);
            else
                EmitStringLiteral(ast.GetString(excludedProperties[i].Key));
            EmitStar(argumentStart + 2 + i);
        }
        builder.EmitCallRuntime(
            (int)RuntimeId.CopyDataPropertiesExcluding,
            argumentStart,
            2 + excludedProperties.Length
        );
        EmitLdar(restRegister);
        EmitStoreBindingTarget(ast, targetIndex);
    }

    private void EmitFunctionDeclaration(FlatAst ast, int functionIndex, int bodyRoot)
    {
        var function = ast.GetFunction(functionIndex);
        var name = ast.GetString(function.NameStringIndex);
        if (!TryResolveBinding(name, out var binding))
            throw new InvalidOperationException($"No planned binding found for function '{name}'.");

        var functionCompiler = new JsPlannedFunctionCompiler(Vm, BuildChildCaptureBindings());
        var functionObject = functionCompiler.CompileFunction(ast, function, bodyRoot);
        var idx = builder.AddObjectConstant(functionObject);
        EmitCreateClosureByIndex(idx);
        EmitStore(binding);
    }
}
