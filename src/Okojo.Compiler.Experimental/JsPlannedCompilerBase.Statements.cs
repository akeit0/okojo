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
                EmitVariableDeclaration(
                    ast,
                    node.Arg0,
                    node.Arg1,
                    (JsVariableDeclarationKind)node.Arg2
                );
                return;
            case AstKind.FunctionDeclaration:
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
            case AstKind.ForInOfStatement:
                EmitForInOfStatement(ast, nodeIndex, node);
                return;
            case AstKind.BreakStatement:
                EmitAbruptCommand(AbruptCommand.Break);
                return;
            case AstKind.ContinueStatement:
                EmitAbruptCommand(AbruptCommand.Continue);
                return;
            case AstKind.ExpressionStatement:
                EmitExpression(ast, node.Arg0);
                return;
            case AstKind.ReturnStatement:
                if (node.Arg0 >= 0)
                    EmitExpression(ast, node.Arg0);
                else
                    builder.EmitLda(JsOpCode.LdaUndefined);
                EmitAbruptCommand(AbruptCommand.Return);
                return;
            case AstKind.ThrowStatement:
                EmitExpression(ast, node.Arg0);
                builder.Emit(JsOpCode.Throw);
                return;
            case AstKind.TryStatement:
                EmitTryStatement(ast, node);
                return;
            case AstKind.SwitchStatement:
                EmitSwitchStatement(ast, nodeIndex, node);
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
        EmitExpressionForTest(ast, test, breakTarget, jumpIfTrue: false);
        PushIterationControlScope(breakTarget, continueTarget);
        try
        {
            EmitStatement(ast, body);
        }
        finally
        {
            controlScopes.Pop();
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
        PushIterationControlScope(breakTarget, continueTarget);
        try
        {
            EmitStatement(ast, body);
        }
        finally
        {
            controlScopes.Pop();
        }
        builder.BindLabel(continueTarget);
        EmitExpressionForTest(ast, test, breakTarget, jumpIfTrue: false);
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
                EmitExpressionForTest(ast, parts[1], breakTarget, jumpIfTrue: false);
            }

            PushIterationControlScope(breakTarget, continueTarget);
            try
            {
                EmitStatement(ast, parts[3]);
            }
            finally
            {
                controlScopes.Pop();
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

    private void EmitForInOfStatement(FlatAst ast, int nodeIndex, AstNode node)
    {
        if (node.Arg2 != 0)
        {
            EmitForOfStatement(ast, nodeIndex, node);
            return;
        }

        var parts = ast.ChildRange(node.Arg0, node.Arg1);
        var left = parts[0];
        var hasLexicalScope =
            ast[left].Kind == AstKind.VariableDeclaration
            && (JsVariableDeclarationKind)ast[left].Arg2
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

        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            EmitExpression(ast, parts[1]);
            var enumerableRegister = builder.AllocateTemporaryRegister();
            EmitStar(enumerableRegister);
            EmitForInRegisterOperation(
                JsOpCode.ForInEnumerate,
                RuntimeId.ForInEnumerate,
                enumerableRegister
            );
            var enumeratorRegister = builder.AllocateTemporaryRegister();
            EmitStar(enumeratorRegister);

            var loopStart = builder.CreateLabel();
            var continueTarget = builder.CreateLabel();
            var breakTarget = builder.CreateLabel();
            var needsPerIterationContext =
                hasLexicalScope && ShouldReplaceLoopHeadContextPerIteration(activeScopes.Peek());
            builder.BindLabel(loopStart);
            EmitForInRegisterOperation(JsOpCode.ForInNext, RuntimeId.ForInNext, enumeratorRegister);
            builder.EmitJump(JsOpCode.JumpIfUndefined, breakTarget);
            EmitForIterationAssignment(ast, left);

            PushIterationControlScope(breakTarget, continueTarget);
            try
            {
                EmitStatement(ast, parts[2]);
            }
            finally
            {
                controlScopes.Pop();
            }

            builder.BindLabel(continueTarget);
            if (needsPerIterationContext)
                EmitReplaceCurrentContext(activeScopes.Peek().ContextSlotCount);
            EmitForInRegisterOperation(JsOpCode.ForInStep, RuntimeId.ForInStep, enumeratorRegister);
            EmitJump(loopStart);
            builder.BindLabel(breakTarget);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
            if (hasLexicalScope)
                LeaveScope();
        }
    }

    private void EmitForOfStatement(FlatAst ast, int nodeIndex, AstNode node)
    {
        var parts = ast.ChildRange(node.Arg0, node.Arg1);
        var left = parts[0];
        var hasLexicalScope =
            ast[left].Kind == AstKind.VariableDeclaration
            && (JsVariableDeclarationKind)ast[left].Arg2
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

        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            EmitExpression(ast, parts[1]);
            var sourceRegister = builder.AllocateTemporaryRegister();
            EmitStar(sourceRegister);
            builder.EmitCallRuntime(
                (int)RuntimeId.CreateArrayDestructureIterator,
                sourceRegister,
                1
            );
            var iteratorRegister = builder.AllocateTemporaryRegister();
            EmitStar(iteratorRegister);
            var valueRegister = builder.AllocateTemporaryRegister();
            var exceptionRegister = builder.AllocateTemporaryRegister();
            var loopStart = builder.CreateLabel();
            var continueTarget = builder.CreateLabel();
            var breakTarget = builder.CreateLabel();
            var catchTarget = builder.CreateLabel();
            var needsPerIterationContext =
                hasLexicalScope && ShouldReplaceLoopHeadContextPerIteration(activeScopes.Peek());

            builder.BindLabel(loopStart);
            EmitLdar(iteratorRegister);
            builder.EmitCallRuntime(
                (int)RuntimeId.DestructureIteratorStepValue,
                iteratorRegister,
                1
            );
            EmitStar(valueRegister);
            builder.EmitLda(JsOpCode.LdaTheHole);
            EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, valueRegister);
            EmitJumpIfToBooleanTrue(breakTarget);

            builder.EmitJump(JsOpCode.PushTry, catchTarget);
            PushForOfControlScope(breakTarget, continueTarget, iteratorRegister);
            PushTryControlScope();
            try
            {
                EmitLdar(valueRegister);
                EmitForIterationAssignment(ast, left);
                EmitStatement(ast, parts[2]);
            }
            finally
            {
                controlScopes.Pop();
                controlScopes.Pop();
            }
            builder.Emit(JsOpCode.PopTry);

            builder.BindLabel(continueTarget);
            if (needsPerIterationContext)
                EmitReplaceCurrentContext(activeScopes.Peek().ContextSlotCount);
            EmitJump(loopStart);

            builder.BindLabel(catchTarget);
            EmitStar(exceptionRegister);
            builder.EmitCallRuntime(
                (int)RuntimeId.DestructureIteratorCloseBestEffort,
                iteratorRegister,
                1
            );
            EmitLdar(exceptionRegister);
            builder.Emit(JsOpCode.Throw);
            builder.BindLabel(breakTarget);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
            if (hasLexicalScope)
                LeaveScope();
        }
    }

    private void EmitForIterationAssignment(FlatAst ast, int left)
    {
        ref readonly var node = ref ast[left];
        if (node.Kind == AstKind.VariableDeclaration)
        {
            var declarators = ast.ChildRange(node.Arg0, node.Arg1);
            ref readonly var declarator = ref ast[declarators[0]];
            if (declarator.Kind == AstKind.VariableDeclaratorPattern)
            {
                EmitStoreBindingTarget(ast, declarator.Arg0);
                return;
            }

            var name = ast.GetString(declarator.Arg0);
            if (!TryResolveBinding(name, out var binding))
                throw new InvalidOperationException($"No planned binding found for '{name}'.");
            EmitStore(
                binding,
                isInitialization: (JsVariableDeclarationKind)node.Arg2
                    != JsVariableDeclarationKind.Var
            );
            return;
        }

        var identifier = ast.GetString(node.Arg0);
        var hasLocalBinding = TryResolveBindingAccess(
            identifier,
            out var localBinding,
            out var contextDepth
        );
        var hasExternalBinding = TryResolveExternalBinding(
            identifier,
            out var externalBinding,
            out var externalDepth
        );
        EmitResolvedIdentifierStore(
            identifier,
            hasLocalBinding,
            hasExternalBinding,
            localBinding,
            contextDepth,
            externalBinding,
            externalDepth
        );
    }

    private void EmitForInRegisterOperation(JsOpCode opcode, RuntimeId runtime, int register)
    {
        if ((uint)register <= byte.MaxValue)
            builder.Emit(opcode, (byte)register);
        else
            builder.EmitCallRuntime((int)runtime, register, 1);
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

    private void PushIterationControlScope(
        BytecodeBuilder.Label breakTarget,
        BytecodeBuilder.Label continueTarget
    )
    {
        controlScopes.Push(
            new ControlScope(
                ControlScopeKind.Iteration,
                breakTarget,
                continueTarget,
                default,
                CurrentContextDepth
            )
        );
    }

    private void PushForOfControlScope(
        BytecodeBuilder.Label breakTarget,
        BytecodeBuilder.Label continueTarget,
        int iteratorRegister
    )
    {
        controlScopes.Push(
            new ControlScope(
                ControlScopeKind.ForOf,
                breakTarget,
                continueTarget,
                default,
                CurrentContextDepth,
                IteratorRegister: iteratorRegister
            )
        );
    }

    private void PushSwitchControlScope(BytecodeBuilder.Label breakTarget)
    {
        controlScopes.Push(
            new ControlScope(
                ControlScopeKind.Switch,
                breakTarget,
                default,
                default,
                CurrentContextDepth
            )
        );
    }

    private void EmitAbruptCommand(AbruptCommand command)
    {
        var contextDepth = CurrentContextDepth;
        foreach (var scope in controlScopes)
        {
            for (var depth = contextDepth; depth > scope.ContextDepth; depth--)
                EmitPopContext();
            contextDepth = Math.Min(contextDepth, scope.ContextDepth);
            if (scope.Kind == ControlScopeKind.Try)
            {
                builder.Emit(JsOpCode.PopTry);
                continue;
            }
            if (scope.Kind == ControlScopeKind.ForOf)
            {
                if (command == AbruptCommand.Continue)
                {
                    EmitJump(scope.Continue);
                    return;
                }
                var returnValueRegister =
                    command == AbruptCommand.Return ? builder.AllocateTemporaryRegister() : -1;
                if (returnValueRegister >= 0)
                    EmitStar(returnValueRegister);
                builder.EmitCallRuntime(
                    (int)RuntimeId.DestructureIteratorClose,
                    scope.IteratorRegister,
                    1
                );
                if (command == AbruptCommand.Break)
                {
                    EmitJump(scope.Break);
                    return;
                }
                EmitLdar(returnValueRegister);
                builder.ReleaseTemporaryRegister(returnValueRegister);
                continue;
            }
            if (scope.Kind == ControlScopeKind.Finally)
            {
                if (command == AbruptCommand.Return)
                    EmitStar(scope.CompletionValueRegister);
                EmitSmi(
                    command switch
                    {
                        AbruptCommand.Return => 1,
                        AbruptCommand.Break => 3,
                        AbruptCommand.Continue => 4,
                        _ => throw new ArgumentOutOfRangeException(nameof(command)),
                    }
                );
                EmitStar(scope.CompletionKindRegister);
                EmitJump(scope.Finally);
                return;
            }
            if (scope.Kind == ControlScopeKind.Switch && command == AbruptCommand.Break)
            {
                EmitJump(scope.Break);
                return;
            }
            if (
                scope.Kind == ControlScopeKind.Iteration
                && command is AbruptCommand.Break or AbruptCommand.Continue
            )
            {
                EmitJump(command == AbruptCommand.Continue ? scope.Continue : scope.Break);
                return;
            }
        }

        switch (command)
        {
            case AbruptCommand.Return:
                builder.Emit(JsOpCode.Return);
                return;
            default:
                throw new InvalidOperationException(
                    $"Abrupt command '{command}' has no active control scope."
                );
        }
    }

    private void EmitSwitchStatement(FlatAst ast, int nodeIndex, AstNode statement)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        BytecodeBuilder.Label[]? rentedLabels = null;
        try
        {
            EmitExpression(ast, statement.Arg0);
            var tagRegister = builder.AllocateTemporaryRegister();
            EmitStar(tagRegister);
            var cases = ast.ChildRange(statement.Arg1, statement.Arg2);
            rentedLabels = ArrayPool<BytecodeBuilder.Label>.Shared.Rent(cases.Length);
            var breakTarget = builder.CreateLabel();
            var defaultTarget = breakTarget;
            for (var i = 0; i < cases.Length; i++)
            {
                rentedLabels[i] = builder.CreateLabel();
                if (ast[cases[i]].Arg0 < 0)
                    defaultTarget = rentedLabels[i];
            }

            var scope = FindChildScope(
                activeScopes.Peek().ScopeId,
                CompilerCollectedScopeKind.Block,
                ast.GetPosition(nodeIndex)
            );
            EnterScope(scope.ScopeId);
            try
            {
                for (var i = 0; i < cases.Length; i++)
                {
                    ref readonly var switchCase = ref ast[cases[i]];
                    EmitDeclarationPrologue(ast, switchCase.Arg1, switchCase.Arg2);
                }
                for (var i = 0; i < cases.Length; i++)
                {
                    ref readonly var switchCase = ref ast[cases[i]];
                    if (switchCase.Arg0 < 0)
                        continue;
                    EmitExpression(ast, switchCase.Arg0);
                    EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, tagRegister);
                    EmitJumpIfToBooleanTrue(rentedLabels[i]);
                }
                EmitJump(defaultTarget);

                PushSwitchControlScope(breakTarget);
                try
                {
                    for (var i = 0; i < cases.Length; i++)
                    {
                        builder.BindLabel(rentedLabels[i]);
                        ref readonly var switchCase = ref ast[cases[i]];
                        var statements = ast.ChildRange(switchCase.Arg1, switchCase.Arg2);
                        for (var j = 0; j < statements.Length; j++)
                            EmitStatement(ast, statements[j]);
                    }
                }
                finally
                {
                    controlScopes.Pop();
                }
                builder.BindLabel(breakTarget);
            }
            finally
            {
                LeaveScope();
            }
        }
        finally
        {
            if (rentedLabels is not null)
                ArrayPool<BytecodeBuilder.Label>.Shared.Return(rentedLabels);
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitTryStatement(FlatAst ast, AstNode statement)
    {
        if (statement.Arg2 < 0)
        {
            EmitTryCatch(ast, statement.Arg0, statement.Arg1);
            return;
        }
        EmitTryFinally(ast, statement.Arg0, statement.Arg1, statement.Arg2);
    }

    private void EmitTryCatch(FlatAst ast, int body, int handler)
    {
        var catchLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        builder.EmitLda(JsOpCode.LdaUndefined);
        builder.EmitJump(JsOpCode.PushTry, catchLabel);
        PushTryControlScope();
        try
        {
            EmitStatement(ast, body);
        }
        finally
        {
            controlScopes.Pop();
        }
        builder.Emit(JsOpCode.PopTry);
        EmitJump(endLabel);
        builder.BindLabel(catchLabel);
        EmitCatchClause(ast, handler);
        builder.BindLabel(endLabel);
    }

    private void PushTryControlScope()
    {
        controlScopes.Push(
            new ControlScope(ControlScopeKind.Try, default, default, default, CurrentContextDepth)
        );
    }

    private void EmitTryFinally(FlatAst ast, int body, int handler, int finalizer)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var canCrossIteration = controlScopes.Any(static scope =>
                scope.Kind is ControlScopeKind.Iteration or ControlScopeKind.ForOf
            );
            var completionKind = builder.AllocateTemporaryRegister();
            var completionValue = builder.AllocateTemporaryRegister();
            var compare = builder.AllocateTemporaryRegister();
            var catchLabel = builder.CreateLabel();
            var finallyFromTry = builder.CreateLabel();
            var finallyEntry = builder.CreateLabel();

            EmitSmi(0);
            EmitStar(completionKind);
            builder.EmitLda(JsOpCode.LdaUndefined);
            EmitStar(completionValue);

            builder.EmitJump(JsOpCode.PushTry, catchLabel);
            PushFinallyControlScope(finallyFromTry, completionKind, completionValue);
            try
            {
                builder.EmitLda(JsOpCode.LdaUndefined);
                EmitStatement(ast, body);
                EmitStar(completionValue);
            }
            finally
            {
                controlScopes.Pop();
            }
            builder.Emit(JsOpCode.PopTry);
            EmitJump(finallyEntry);

            builder.BindLabel(finallyFromTry);
            builder.Emit(JsOpCode.PopTry);
            EmitJump(finallyEntry);

            builder.BindLabel(catchLabel);
            if (handler >= 0)
            {
                var catchThrow = builder.CreateLabel();
                var finallyFromCatch = builder.CreateLabel();
                builder.EmitJump(JsOpCode.PushTry, catchThrow);
                PushFinallyControlScope(finallyFromCatch, completionKind, completionValue);
                try
                {
                    EmitCatchClause(ast, handler);
                    EmitStar(completionValue);
                }
                finally
                {
                    controlScopes.Pop();
                }
                builder.Emit(JsOpCode.PopTry);
                EmitJump(finallyEntry);
                builder.BindLabel(finallyFromCatch);
                builder.Emit(JsOpCode.PopTry);
                EmitJump(finallyEntry);
                builder.BindLabel(catchThrow);
            }
            EmitStar(completionValue);
            EmitSmi(2);
            EmitStar(completionKind);
            EmitJump(finallyEntry);

            builder.BindLabel(finallyEntry);
            EmitStatement(ast, finalizer);

            EmitFinallyCompletionJump(completionKind, compare, 1, out var notReturn);
            EmitLdar(completionValue);
            EmitAbruptCommand(AbruptCommand.Return);
            builder.BindLabel(notReturn);

            EmitFinallyCompletionJump(completionKind, compare, 2, out var notThrow);
            EmitLdar(completionValue);
            builder.Emit(JsOpCode.Throw);
            builder.BindLabel(notThrow);

            if (canCrossIteration)
            {
                EmitFinallyCompletionJump(completionKind, compare, 3, out var notBreak);
                EmitAbruptCommand(AbruptCommand.Break);
                builder.BindLabel(notBreak);

                EmitFinallyCompletionJump(completionKind, compare, 4, out var normal);
                EmitAbruptCommand(AbruptCommand.Continue);
                builder.BindLabel(normal);
            }
            EmitLdar(completionValue);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void PushFinallyControlScope(
        BytecodeBuilder.Label target,
        int completionKind,
        int completionValue
    )
    {
        controlScopes.Push(
            new ControlScope(
                ControlScopeKind.Finally,
                default,
                default,
                target,
                CurrentContextDepth,
                completionKind,
                completionValue
            )
        );
    }

    private void EmitFinallyCompletionJump(
        int completionKind,
        int compare,
        int kind,
        out BytecodeBuilder.Label next
    )
    {
        next = builder.CreateLabel();
        EmitSmi(kind);
        EmitStar(compare);
        EmitLdar(completionKind);
        EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, compare);
        EmitJumpIfToBooleanFalse(next);
    }

    private void EmitCatchClause(FlatAst ast, int nodeIndex)
    {
        ref readonly var clause = ref ast[nodeIndex];
        var thrown = builder.AllocateTemporaryRegister();
        EmitStar(thrown);
        var scope = FindChildScope(
            activeScopes.Peek().ScopeId,
            CompilerCollectedScopeKind.Catch,
            ast.GetPosition(nodeIndex)
        );
        EnterScope(scope.ScopeId);
        try
        {
            if (clause.Arg0 >= 0)
            {
                EmitLdar(thrown);
                EmitStoreBindingTarget(ast, clause.Arg0);
            }
            builder.EmitLda(JsOpCode.LdaUndefined);
            EmitStatement(ast, clause.Arg1);
        }
        finally
        {
            LeaveScope();
            builder.ReleaseTemporaryRegister(thrown);
        }
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
            EmitDeclarationPrologue(ast, block.Arg0, block.Arg1);
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
        var elseLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        EmitExpressionForTest(ast, test, elseLabel, jumpIfTrue: false);
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

    protected void EmitDeclarationPrologue(FlatAst ast, int bodyRoot)
    {
        ref readonly var body = ref ast[bodyRoot];
        EmitDeclarationPrologue(ast, body.Arg0, body.Arg1);
    }

    private void EmitDeclarationPrologue(FlatAst ast, int offset, int count)
    {
        var scope = activeScopes.Peek();
        for (var i = 0; i < scope.Bindings.Count; i++)
            if (scope.Bindings[i].Planned.Kind == CompilerCollectedBindingKind.Var)
            {
                builder.EmitLda(JsOpCode.LdaUndefined);
                EmitStore(scope.Bindings[i], isInitialization: true);
            }

        var statements = ast.ChildRange(offset, count);
        for (var i = 0; i < statements.Length; i++)
        {
            ref readonly var statement = ref ast[statements[i]];
            if (statement.Kind == AstKind.FunctionDeclaration)
                EmitFunctionDeclaration(ast, statement.Arg0, statement.Arg1);
        }
    }

    private void EmitVariableDeclaration(
        FlatAst ast,
        int offset,
        int count,
        JsVariableDeclarationKind declarationKind
    )
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

            if (declarationKind == JsVariableDeclarationKind.Var && declarator.Arg2 < 0)
                continue;

            var name = ast.GetString(declarator.Arg0);
            if (!TryResolveBinding(name, out var binding))
                throw new InvalidOperationException($"No planned binding found for '{name}'.");

            if (declarator.Arg2 >= 0)
                EmitExpressionWithInferredName(ast, declarator.Arg2, name);
            else
                builder.EmitLda(JsOpCode.LdaUndefined);

            EmitStore(binding, isInitialization: true);
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
            if (
                emittingParameterInitializers
                && binding.Planned.Kind == CompilerCollectedBindingKind.Parameter
            )
                EmitInitializeParameterStore(binding);
            else
                EmitStore(binding, isInitialization: true);
            return;
        }

        if (
            target.Kind
            is not (
                AstKind.ArrayBindingPattern
                or AstKind.ArrayExpression
                or AstKind.ObjectBindingPattern
                or AstKind.ObjectExpression
            )
        )
            throw new NotSupportedException(
                $"{CompilerName} does not support binding target '{target.Kind}'."
            );

        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var valueRegister = builder.AllocateTemporaryRegister();
            EmitStar(valueRegister);
            if (target.Kind is AstKind.ArrayBindingPattern or AstKind.ArrayExpression)
                EmitArrayBindingPattern(ast, target, valueRegister);
            else
                EmitObjectBindingPattern(ast, target, valueRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitArrayBindingPattern(
        FlatAst ast,
        AstNode pattern,
        int sourceRegister,
        bool assignment = false
    )
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            EmitLdar(sourceRegister);
            builder.EmitCallRuntime(
                (int)RuntimeId.CreateArrayDestructureIterator,
                sourceRegister,
                1
            );
            var iteratorRegister = builder.AllocateTemporaryRegister();
            EmitStar(iteratorRegister);
            var doneRegister = builder.AllocateTemporaryRegister();
            builder.EmitLda(JsOpCode.LdaFalse);
            EmitStar(doneRegister);
            var valueRegister = builder.AllocateTemporaryRegister();
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
                var targetMarker = builder.GetTemporaryRegisterScopeMarker();
                try
                {
                    if (element.Kind == AstKind.SpreadElement)
                    {
                        PreparedMemberReference? preparedRestTarget = null;
                        if (assignment && ast[element.Arg0].Kind == AstKind.MemberExpression)
                            preparedRestTarget = PrepareMemberReference(
                                ast,
                                ast[element.Arg0],
                                normalizeComputedKey: false
                            );
                        EmitArrayBindingRest(
                            ast,
                            element.Arg0,
                            iteratorRegister,
                            doneRegister,
                            valueRegister,
                            assignment,
                            preparedRestTarget
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

                    PreparedMemberReference? preparedTarget = null;
                    if (assignment && ast[targetIndex].Kind == AstKind.MemberExpression)
                        preparedTarget = PrepareMemberReference(
                            ast,
                            ast[targetIndex],
                            normalizeComputedKey: false
                        );
                    EmitArrayBindingStep(iteratorRegister, doneRegister, valueRegister);
                    if (defaultIndex >= 0)
                        EmitBindingDefault(ast, targetIndex, defaultIndex, valueRegister);
                    EmitStoreDestructuringTarget(ast, targetIndex, assignment, preparedTarget);
                }
                finally
                {
                    builder.ReleaseTemporaryRegistersToMarker(targetMarker);
                }
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
        int valueRegister,
        bool assignment,
        PreparedMemberReference? preparedTarget
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
        EmitStoreDestructuringTarget(ast, targetIndex, assignment, preparedTarget);
    }

    private void EmitBindingDefault(
        FlatAst ast,
        int targetIndex,
        int defaultIndex,
        int valueRegister
    )
    {
        var useDefaultLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        EmitStar(valueRegister);
        EmitLdar(valueRegister);
        EmitJumpIfUndefined(useDefaultLabel);
        EmitJump(endLabel);
        builder.BindLabel(useDefaultLabel);
        if (ast[targetIndex].Kind == AstKind.Identifier)
            EmitExpressionWithInferredName(ast, defaultIndex, ast.GetString(ast[targetIndex].Arg0));
        else
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

    private void EmitObjectBindingPattern(
        FlatAst ast,
        AstNode pattern,
        int sourceRegister,
        bool assignment = false
    )
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
                        computedKeyRegisters!.AsSpan(0, i),
                        assignment
                    );
                    continue;
                }

                var sourceKeyRegister = -1;
                if (property.IsComputed)
                {
                    EmitExpression(ast, property.Key);
                    sourceKeyRegister =
                        restIndex >= 0 ? builder.AllocateTemporaryRegister()
                        : reusableKeyRegister >= 0 ? reusableKeyRegister
                        : reusableKeyRegister = builder.AllocateTemporaryRegister();
                    EmitStar(sourceKeyRegister);
                    builder.EmitCallRuntime(
                        (int)RuntimeId.NormalizePropertyKey,
                        sourceKeyRegister,
                        1
                    );
                    EmitStar(sourceKeyRegister);
                    if (computedKeyRegisters is not null)
                        computedKeyRegisters[i] = sourceKeyRegister;
                }

                var targetMarker = builder.GetTemporaryRegisterScopeMarker();
                try
                {
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
                    PreparedMemberReference? preparedTarget = null;
                    if (assignment && ast[targetIndex].Kind == AstKind.MemberExpression)
                        preparedTarget = PrepareMemberReference(
                            ast,
                            ast[targetIndex],
                            normalizeComputedKey: false
                        );

                    if (sourceKeyRegister >= 0)
                    {
                        EmitLdar(sourceKeyRegister);
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
                    if (defaultIndex >= 0)
                        EmitBindingDefault(
                            ast,
                            targetIndex,
                            defaultIndex,
                            builder.AllocateTemporaryRegister()
                        );
                    EmitStoreDestructuringTarget(ast, targetIndex, assignment, preparedTarget);
                }
                finally
                {
                    builder.ReleaseTemporaryRegistersToMarker(targetMarker);
                }
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
        ReadOnlySpan<int> computedKeyRegisters,
        bool assignment
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
        EmitStoreDestructuringTarget(ast, targetIndex, assignment);
    }

    private void EmitStoreDestructuringTarget(
        FlatAst ast,
        int targetIndex,
        bool assignment,
        PreparedMemberReference? preparedTarget = null
    )
    {
        if (assignment)
            EmitStoreAssignmentTarget(ast, targetIndex, preparedTarget);
        else
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
        EmitStore(binding, isInitialization: true, isFunctionDeclaration: true);
    }
}
