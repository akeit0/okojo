using Okojo.Bytecode;
using Okojo.Parsing;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private void EmitBindingDeclarationStatement(JsVariableDeclarationStatement declaration,
        bool emitCompletionValue = true)
    {
        if (declaration.BindingPattern is null || declaration.BindingInitializer is null)
            throw new InvalidOperationException("Binding declaration is missing pattern or initializer.");

        VisitExpression(declaration.BindingInitializer);
        var valueReg = AllocateTemporaryRegister();
        EmitStarRegister(valueReg);

        switch (declaration.BindingPattern)
        {
            case JsArrayExpression arrayPattern:
                EmitArrayDestructuringAssignmentFromRegister(arrayPattern, valueReg, initializeIdentifiers: true);
                break;
            case JsObjectExpression objectPattern:
                EmitObjectDestructuringAssignmentFromRegister(objectPattern, valueReg, initializeIdentifiers: true);
                break;
            default:
                throw new NotSupportedException("Binding declaration pattern is not supported.");
        }

        if (declaration.Kind.IsUsingLike())
            EmitRegisterExplicitResource(declaration.Kind, valueReg);

        ReleaseTemporaryRegister(valueReg);
        if (emitCompletionValue)
            EmitLdaTheHole();
    }

    private void EmitVariableDeclarationStatement(JsVariableDeclarationStatement varStmt,
        bool emitCompletionValue = true)
    {
        if (varStmt.BindingPattern is not null && varStmt.BindingInitializer is not null)
        {
            EmitBindingDeclarationStatement(varStmt, emitCompletionValue);
            return;
        }

        foreach (var decl in varStmt.Declarators)
        {
            var hasResolvedDeclBinding = TryResolveLocalBinding(decl.Name, out var resolvedDeclBinding);
            var targetName = hasResolvedDeclBinding ? resolvedDeclBinding.Name : decl.Name;
            if (decl.Initializer != null)
            {
                var isUsingLike = varStmt.Kind.IsUsingLike();
                var useInitializationStore = varStmt.Kind is not JsVariableDeclarationKind.Var;
                if (!isUsingLike &&
                    hasResolvedDeclBinding &&
                    TryEmitDirectLocalToLocalMoveForInitializer(targetName, decl.Initializer,
                        useInitializationStore, decl.Name))
                {
                    if (ShouldTrackKnownInitializedLexical(resolvedDeclBinding.SymbolId))
                        MarkKnownInitializedLexical(resolvedDeclBinding.SymbolId);
                    continue;
                }

                if (!isUsingLike &&
                    hasResolvedDeclBinding &&
                    TryEmitLiteralInitializerDirectToLocal(varStmt.Kind, targetName, decl.Initializer))
                {
                    if (ShouldTrackKnownInitializedLexical(resolvedDeclBinding.SymbolId))
                        MarkKnownInitializedLexical(resolvedDeclBinding.SymbolId);
                    continue;
                }

                VisitExpressionWithInferredName(decl.Initializer, decl.Name);
                var valueReg = -1;
                if (isUsingLike)
                {
                    valueReg = AllocateTemporaryRegister();
                    EmitStarRegister(valueReg);
                    EmitLdaRegister(valueReg);
                }
                StoreIdentifier(targetName, useInitializationStore, decl.Name);
                if (hasResolvedDeclBinding && ShouldTrackKnownInitializedLexical(resolvedDeclBinding.SymbolId))
                    MarkKnownInitializedLexical(resolvedDeclBinding.SymbolId);
                if (isUsingLike)
                {
                    EmitRegisterExplicitResource(varStmt.Kind, valueReg);
                    ReleaseTemporaryRegister(valueReg);
                }
            }
            else if (varStmt.Kind is JsVariableDeclarationKind.Let && hasResolvedDeclBinding &&
                     IsLexicalLocalBinding(resolvedDeclBinding.SymbolId))
            {
                EmitLdaUndefined();
                StoreIdentifier(targetName, true, decl.Name);
                if (ShouldTrackKnownInitializedLexical(resolvedDeclBinding.SymbolId))
                    MarkKnownInitializedLexical(resolvedDeclBinding.SymbolId);
            }
            else if (IsReplTopLevelMode() && varStmt.Kind is JsVariableDeclarationKind.Let)
            {
                EmitLdaUndefined();
                StoreIdentifier(targetName, true, decl.Name);
            }
        }

        if (emitCompletionValue)
            EmitLdaTheHole();
    }

    private void VisitStatement(JsStatement stmt, bool resultUsed = true)
    {
        if (stmt is not JsFunctionDeclaration)
            EmitSourcePosition(stmt.Position);
        var hasStructuredCompletion = activeStatementCompletionStates.Count != 0;
        var statementCompletionState = hasStructuredCompletion
            ? activeStatementCompletionStates.Peek()
            : default;
        var statementCompletionReg = hasStructuredCompletion ? statementCompletionState.Register : -1;
        var statementCompletionKnownNonHole = hasStructuredCompletion && statementCompletionState.KnownNonHole;
        var hasSwitchCompletion = activeSwitchCompletionRegisters.Count != 0;
        var switchCompletionReg = hasSwitchCompletion ? activeSwitchCompletionRegisters.Peek() : -1;
        var completionHandledInternally = false;
        switch (stmt)
        {
            case JsExpressionStatement exprStmt: VisitExpression(exprStmt.Expression); break;
            case JsBlockStatement blockStmt:
                if (BlockNeedsExplicitResourceScope(blockStmt.Statements))
                {
                    EmitExplicitResourceScope(
                        () => EmitBlockStatementCore(blockStmt, resultUsed),
                        BlockNeedsAsyncExplicitResourceScope(blockStmt.Statements));
                    completionHandledInternally = true;
                }
                else
                {
                    EmitBlockStatementCore(blockStmt, resultUsed);
                    completionHandledInternally = true;
                }
                break;
            case JsFunctionDeclaration _:
                builder.ClearPendingSourceOffset();
                if (resultUsed)
                    EmitLdaTheHole();
                break;
            case JsClassDeclaration classDecl:
                _ = TryResolveLocalBinding(classDecl.Name, out var resolvedClassBinding);
                VisitClassExpression(classDecl.ClassExpression, classDecl.Name, classDecl.Name);
                StoreIdentifier(resolvedClassBinding.Name, true, classDecl.Name);
                if (ShouldTrackKnownInitializedLexical(resolvedClassBinding.SymbolId))
                    MarkKnownInitializedLexical(resolvedClassBinding.SymbolId);
                if (resultUsed)
                    EmitLdaTheHole();
                break;
            case JsIfStatement ifStmt:
            {
                if (ifStmt.Alternate is null &&
                    StatementNeverCompletesNormally(ifStmt.Consequent) &&
                    !ContainsShortCircuitingControlFlow(ifStmt.Test) &&
                    (hasStructuredCompletion || hasSwitchCompletion))
                {
                    var abruptElseLabel = builder.CreateLabel();
                    VisitExpression(ifStmt.Test);
                    EmitJumpIfToBooleanFalse(abruptElseLabel);
                    activeAbruptEmptyNormalizations.Push(true);
                    VisitStatement(ifStmt.Consequent);
                    activeAbruptEmptyNormalizations.Pop();
                    builder.BindLabel(abruptElseLabel);
                    EmitLdaUndefined();
                    if (hasStructuredCompletion)
                    {
                        EmitStarRegister(statementCompletionReg);
                        EmitLdaRegister(statementCompletionReg);
                    }
                    else
                    {
                        EmitStarRegister(switchCompletionReg);
                        EmitLdaRegister(switchCompletionReg);
                    }

                    completionHandledInternally = true;
                    break;
                }

                var elseLabel = builder.CreateLabel();
                BytecodeBuilder.Label endLabel = default;
                VisitExpression(ifStmt.Test);
                EmitJumpIfToBooleanFalse(elseLabel);
                activeAbruptEmptyNormalizations.Push(true);
                VisitStatement(ifStmt.Consequent);
                activeAbruptEmptyNormalizations.Pop();
                var hasLabel = false;
                if (!StatementNeverCompletesNormally(ifStmt.Consequent))
                {
                    hasLabel = true;
                    endLabel = builder.CreateLabel();
                    EmitJump(endLabel);
                }

                builder.BindLabel(elseLabel);
                if (ifStmt.Alternate != null)
                {
                    activeAbruptEmptyNormalizations.Push(true);
                    VisitStatement(ifStmt.Alternate);
                    activeAbruptEmptyNormalizations.Pop();
                }
                else
                {
                    EmitLdaTheHole();
                }

                if (hasLabel)
                    builder.BindLabel(endLabel);
                EmitNormalizeAccumulatorHoleToUndefined();
            }
                break;
            case JsWhileStatement whileStmt:
                EmitWhileStatement(whileStmt);
                break;
            case JsDoWhileStatement doWhileStmt:
                EmitDoWhileStatement(doWhileStmt);
                break;
            case JsForStatement forStmt when forStmt.Init is JsVariableDeclarationStatement initDecl && initDecl.Kind.IsUsingLike():
                EmitExplicitResourceScope(() => EmitForStatement(forStmt),
                    initDecl.Kind == JsVariableDeclarationKind.AwaitUsing);
                completionHandledInternally = true;
                break;
            case JsForStatement forStmt:
                EmitForStatement(forStmt);
                break;
            case JsForInOfStatement forInOfStmt:
                EmitForInOfStatement(forInOfStmt);
                break;
            case JsSwitchStatement switchStmt:
                EmitSwitchStatement(switchStmt);
                break;
            case JsLabeledStatement labeledStmt:
                EmitLabeledStatement(labeledStmt);
                break;
            case JsVariableDeclarationStatement varStmt when varStmt.Kind.IsUsingLike() && !HasAmbientExplicitResourceScope:
                EmitExplicitResourceScope(() => EmitVariableDeclarationStatement(varStmt, resultUsed),
                    varStmt.Kind == JsVariableDeclarationKind.AwaitUsing);
                completionHandledInternally = true;
                break;
            case JsVariableDeclarationStatement varStmt:
                EmitVariableDeclarationStatement(varStmt, resultUsed);
                break;
            case JsEmptyObjectBindingDeclarationStatement emptyObjectBindingStmt
                when emptyObjectBindingStmt.Kind.IsUsingLike() && !HasAmbientExplicitResourceScope:
                EmitExplicitResourceScope(() => EmitEmptyObjectBindingDeclarationStatement(emptyObjectBindingStmt),
                    emptyObjectBindingStmt.Kind == JsVariableDeclarationKind.AwaitUsing);
                completionHandledInternally = true;
                break;
            case JsEmptyObjectBindingDeclarationStatement emptyObjectBindingStmt:
                EmitEmptyObjectBindingDeclarationStatement(emptyObjectBindingStmt);
                break;
            case JsReturnStatement returnStmt:
                if (returnStmt.Argument != null)
                {
                    VisitExpression(returnStmt.Argument, directReturn: true);
                    if (functionKind == JsBytecodeFunctionKind.AsyncGenerator)
                        EmitGeneratorSuspendResume(minimizeLiveRange: true, isAwaitSuspend: true);
                }
                else
                {
                    EmitLdaUndefined();
                }

                EmitReturnConsideringFinallyFlow();
                break;
            case JsThrowStatement throwStmt:
                VisitExpression(throwStmt.Argument);
                EmitThrowConsideringFinallyFlow();
                break;
            case JsBreakStatement br:
                if (hasStructuredCompletion)
                {
                    EmitLdaRegister(statementCompletionReg);
                    if (activeAbruptEmptyNormalizations.Count != 0 && !statementCompletionKnownNonHole)
                        EmitNormalizeAccumulatorHoleToUndefined();
                    if (hasSwitchCompletion && switchCompletionReg != statementCompletionReg)
                    {
                        EmitStarRegister(switchCompletionReg);
                        EmitLdaRegister(switchCompletionReg);
                    }
                }
                else if (hasSwitchCompletion)
                {
                    EmitLdaRegister(switchCompletionReg);
                }

                if (br.Label is null)
                {
                    if (breakTargets.Count == 0)
                        ThrowIllegalBreakSyntaxError(br.Position);
                    EmitBreakConsideringFinallyFlow(breakTargets.Peek());
                    break;
                }

                if (!TryResolveBreakTarget(br.Label, out var breakTarget))
                    ThrowUndefinedLabelSyntaxError(br.Label, br.Position);
                EmitBreakConsideringFinallyFlow(breakTarget);
                break;
            case JsContinueStatement cont:
                if (hasStructuredCompletion)
                {
                    EmitLdaRegister(statementCompletionReg);
                    if (activeAbruptEmptyNormalizations.Count != 0 && !statementCompletionKnownNonHole)
                        EmitNormalizeAccumulatorHoleToUndefined();
                    if (hasSwitchCompletion && switchCompletionReg != statementCompletionReg)
                    {
                        EmitStarRegister(switchCompletionReg);
                        EmitLdaRegister(switchCompletionReg);
                    }
                }
                else if (hasSwitchCompletion)
                {
                    EmitLdaRegister(switchCompletionReg);
                }

                if (cont.Label is null)
                {
                    if (loopTargets.Count == 0)
                        ThrowIllegalContinueNoLoopSyntaxError(cont.Position);
                    EmitContinueConsideringFinallyFlow(loopTargets.Peek().ContinueTarget);
                    break;
                }

                if (!TryResolveContinueTarget(cont.Label, out var continueTarget, out var continueError))
                {
                    if (continueError == ContinueLabelError.UndefinedLabel)
                        ThrowUndefinedLabelSyntaxError(cont.Label, cont.Position);
                    ThrowIllegalContinueLabelSyntaxError(cont.Label, cont.Position);
                }

                EmitContinueConsideringFinallyFlow(continueTarget);
                break;
            case JsTryStatement tryStmt:
            {
                EmitTryStatement(tryStmt);
            }
                break;
            case JsDebuggerStatement:
                EmitDebugger();
                if (resultUsed)
                    EmitLdaTheHole();
                break;
            case JsEmptyStatement:
                if (resultUsed)
                    EmitLdaTheHole();
                break;
            case JsWithStatement: throw new NotSupportedException("With statements are not supported in Okojo");
            default: throw new NotImplementedException($"Statement {stmt.GetType().Name}");
        }

        if (stmt is not JsBreakStatement and not JsContinueStatement and not JsReturnStatement and not JsThrowStatement
            && StatementCanProduceTrackedCompletion(stmt)
            && !completionHandledInternally)
        {
            var trackedCompletionKnownNonHole = StatementTrackedCompletionIsKnownNonHole(stmt);
            if (hasStructuredCompletion)
            {
                if (stmt is JsExpressionStatement || trackedCompletionKnownNonHole)
                    EmitStarRegister(statementCompletionReg);
                else
                    EmitStoreCompletionValueIfNotHole(statementCompletionReg);
            }
            else if (hasSwitchCompletion)
            {
                if (stmt is JsExpressionStatement || trackedCompletionKnownNonHole)
                    EmitStarRegister(switchCompletionReg);
                else
                    EmitStoreCompletionValueIfNotHole(switchCompletionReg);
            }
        }
    }

    private void EmitBlockStatementCore(JsBlockStatement blockStmt, bool resultUsed)
    {
        var hasStructuredCompletion = activeStatementCompletionStates.Count != 0;
        var reuseParentBlockCompletion =
            hasStructuredCompletion &&
            StatementListNeedsStructuredCompletionTracking(blockStmt.Statements) &&
            !StatementListNeedsCompletionIsolationFromParent(blockStmt.Statements);
        var trackBlockCompletion =
            !reuseParentBlockCompletion &&
            StatementListNeedsStructuredCompletionTracking(blockStmt.Statements);
        var blockCompletionReg = -1;
        if (trackBlockCompletion)
        {
            blockCompletionReg = AllocateTemporaryRegister();
            EmitLdaTheHole();
            EmitStarRegister(blockCompletionReg);
            PushStatementCompletionState(blockCompletionReg, false);
        }

        PushBlockLexicalAliases(blockStmt);
        try
        {
            foreach (var s in blockStmt.Statements)
                if (s is JsFunctionDeclaration decl)
                    HoistFunction(decl);

            foreach (var s in blockStmt.Statements)
            {
                VisitStatement(s, resultUsed);
                if (StatementAlwaysReturns(s))
                    break;
            }
        }
        finally
        {
            PopBlockLexicalAliases(blockStmt);
            if (trackBlockCompletion)
                PopStatementCompletionState();
        }

        if (trackBlockCompletion)
        {
            EmitLdaRegister(blockCompletionReg);
            ReleaseTemporaryRegister(blockCompletionReg);
        }
        else if (resultUsed && !StatementListLeavesDirectCompletionValue(blockStmt.Statements))
        {
            EmitLdaTheHole();
        }
    }

    private void EmitEmptyObjectBindingDeclarationStatement(JsEmptyObjectBindingDeclarationStatement statement)
    {
        VisitExpression(statement.Initializer);
        var valueReg = AllocateTemporaryRegister();
        EmitStarRegister(valueReg);
        EmitCallRuntime(RuntimeId.RequireObjectCoercible, valueReg, 1);
        if (statement.Kind.IsUsingLike())
            EmitRegisterExplicitResource(statement.Kind, valueReg);
        builder.ReleaseTemporaryRegister(valueReg);
        EmitLdaTheHole();
    }

    private static bool ContainsShortCircuitingControlFlow(JsExpression expr)
    {
        switch (expr)
        {
            case JsBinaryExpression binary:
                return binary.Operator is JsBinaryOperator.LogicalAnd or JsBinaryOperator.LogicalOr
                           or JsBinaryOperator.NullishCoalescing
                       || ContainsShortCircuitingControlFlow(binary.Left)
                       || ContainsShortCircuitingControlFlow(binary.Right);
            case JsConditionalExpression conditional:
                return true;
            case JsAssignmentExpression assignment:
                return ContainsShortCircuitingControlFlow(assignment.Left) ||
                       ContainsShortCircuitingControlFlow(assignment.Right);
            case JsCallExpression call:
                if (ContainsShortCircuitingControlFlow(call.Callee))
                    return true;
                foreach (var arg in call.Arguments)
                    if (ContainsShortCircuitingControlFlow(arg))
                        return true;

                return false;
            case JsMemberExpression member:
                return ContainsShortCircuitingControlFlow(member.Object) ||
                       (member.IsComputed && ContainsShortCircuitingControlFlow(member.Property));
            case JsUnaryExpression unary:
                return ContainsShortCircuitingControlFlow(unary.Argument);
            case JsUpdateExpression update:
                return ContainsShortCircuitingControlFlow(update.Argument);
            case JsAwaitExpression awaitExpression:
                return ContainsShortCircuitingControlFlow(awaitExpression.Argument);
            case JsYieldExpression yieldExpression:
                return yieldExpression.Argument is not null &&
                       ContainsShortCircuitingControlFlow(yieldExpression.Argument);
            case JsTemplateExpression template:
                foreach (var part in template.Expressions)
                    if (ContainsShortCircuitingControlFlow(part))
                        return true;

                return false;
            case JsTaggedTemplateExpression tagged:
                if (ContainsShortCircuitingControlFlow(tagged.Tag))
                    return true;
                foreach (var part in tagged.Template.Expressions)
                    if (ContainsShortCircuitingControlFlow(part))
                        return true;

                return false;
            case JsArrayExpression array:
                foreach (var item in array.Elements)
                    if (item is not null && ContainsShortCircuitingControlFlow(item))
                        return true;

                return false;
            case JsObjectExpression obj:
                foreach (var property in obj.Properties)
                {
                    if (property.IsComputed && property.ComputedKey is not null &&
                        ContainsShortCircuitingControlFlow(property.ComputedKey))
                        return true;

                    if (ContainsShortCircuitingControlFlow(property.Value))
                        return true;
                }

                return false;
            case JsSpreadExpression spread:
                return ContainsShortCircuitingControlFlow(spread.Argument);
            default:
                return false;
        }
    }
}
