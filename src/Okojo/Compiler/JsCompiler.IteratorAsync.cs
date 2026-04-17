using Okojo.Bytecode;
using Okojo.Parsing;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private void EmitLoadIteratorMethod(int iterableReg, int iteratorMethodReg)
    {
        EmitCallRuntime(RuntimeId.GetIteratorMethod, iterableReg, 1);
        EmitStarRegister(iteratorMethodReg);
    }

    private void EmitLoadAsyncIteratorMethod(int iterableReg, int iteratorMethodReg)
    {
        EmitCallRuntime(RuntimeId.GetAsyncIteratorMethod, iterableReg, 1);
        EmitStarRegister(iteratorMethodReg);
    }

    private void EmitCallIteratorMethod(int iterableReg, int iteratorMethodReg)
    {
        EmitRaw(JsOpCode.CallProperty, (byte)iteratorMethodReg, (byte)iterableReg, 0, 0);
    }

    private void EmitEnsureIteratorResultObject(int resultReg)
    {
        var resultObjectLabel = builder.CreateLabel();
        EmitLdaRegister(resultReg);
        builder.EmitJump(JsOpCode.JumpIfJsReceiver, resultObjectLabel);
        EmitCallRuntime(RuntimeId.ThrowIteratorResultNotObject, 0, 0);
        builder.BindLabel(resultObjectLabel);
    }

    private void EmitCreateIteratorFromAsyncOrSyncIterable(
        int iterableReg,
        int iteratorMethodReg,
        int iterReg)
    {
        var useSyncIteratorLabel = builder.CreateLabel();
        var iteratorReadyLabel = builder.CreateLabel();

        EmitLoadAsyncIteratorMethod(iterableReg, iteratorMethodReg);
        EmitLdaRegister(iteratorMethodReg);
        builder.EmitJump(JsOpCode.JumpIfNull, useSyncIteratorLabel);
        builder.EmitJump(JsOpCode.JumpIfUndefined, useSyncIteratorLabel);
        EmitCallIteratorMethod(iterableReg, iteratorMethodReg);
        EmitStarRegister(iterReg);
        builder.EmitJump(JsOpCode.Jump, iteratorReadyLabel);

        builder.BindLabel(useSyncIteratorLabel);
        EmitLoadIteratorMethod(iterableReg, iteratorMethodReg);
        EmitCallIteratorMethod(iterableReg, iteratorMethodReg);
        var rawIterReg = AllocateTemporaryRegister();
        EmitStarRegister(rawIterReg);
        EmitCallRuntime(RuntimeId.WrapSyncIteratorForAsyncDelegate, rawIterReg, 1);
        EmitStarRegister(iterReg);

        builder.BindLabel(iteratorReadyLabel);
    }

    private void EmitYieldDelegateExpression(JsExpression? argument)
    {
        var tempScope = BeginTemporaryRegisterScope();
        try
        {
            if (argument is not null)
                VisitExpression(argument);
            else
                EmitLdaUndefined();

            var iterableReg = AllocateTemporaryRegister();
            EmitStarRegister(iterableReg);

            var iteratorMethodReg = AllocateTemporaryRegister();
            if (functionKind == JsBytecodeFunctionKind.AsyncGenerator)
            {
                var iterReg = AllocateTemporaryRegister();
                EmitCreateIteratorFromAsyncOrSyncIterable(iterableReg, iteratorMethodReg, iterReg);
                EmitYieldDelegateLoop(iterReg);
            }
            else
            {
                EmitLoadIteratorMethod(iterableReg, iteratorMethodReg);
                EmitCallIteratorMethod(iterableReg, iteratorMethodReg);
                var iterReg = AllocateTemporaryRegister();
                EmitStarRegister(iterReg);
                EmitYieldDelegateLoop(iterReg);
            }
        }
        finally
        {
            EndTemporaryRegisterScope(tempScope);
        }
    }

    private void EmitYieldDelegateLoop(int iterReg)
    {
        EmitLdaUndefined();
        var sentReg = AllocateTemporaryRegister();
        EmitStarRegister(sentReg);

        var nextNameIdx = builder.AddAtomizedStringConstant("next");
        var doneNameIdx = builder.AddAtomizedStringConstant("done");
        var valueNameIdx = builder.AddAtomizedStringConstant("value");

        var nextFnReg = AllocateTemporaryRegister();
        var argReg = AllocateTemporaryRegister();
        var resultReg = AllocateTemporaryRegister();

        EmitLdaNamedPropertyByIndex(iterReg, nextNameIdx, builder.AllocateFeedbackSlot());
        EmitStarRegister(nextFnReg);

        var loopLabel = builder.CreateLabel();
        var yieldLabel = builder.CreateLabel();
        var doneLabel = builder.CreateLabel();

        builder.BindLabel(loopLabel);
        EmitMoveRegister(sentReg, argReg);
        EmitRaw(JsOpCode.CallProperty, (byte)nextFnReg, (byte)iterReg, (byte)argReg, 1);
        if (functionKind == JsBytecodeFunctionKind.AsyncGenerator)
            EmitGeneratorSuspendResume(minimizeLiveRange: true, isAwaitSuspend: true);
        EmitStarRegister(resultReg);
        EmitEnsureIteratorResultObject(resultReg);

        EmitLdaNamedPropertyByIndex(resultReg, doneNameIdx, builder.AllocateFeedbackSlot());
        builder.EmitJump(JsOpCode.JumpIfToBooleanFalse, yieldLabel);
        EmitLdaNamedPropertyByIndex(resultReg, valueNameIdx, builder.AllocateFeedbackSlot());
        builder.EmitJump(JsOpCode.Jump, doneLabel);

        builder.BindLabel(yieldLabel);
        if (functionKind == JsBytecodeFunctionKind.AsyncGenerator)
            EmitLdaNamedPropertyByIndex(resultReg, valueNameIdx, builder.AllocateFeedbackSlot());
        else
            EmitLdaRegister(resultReg);
        PinSuspendRegister(iterReg);
        PinSuspendRegister(sentReg);
        try
        {
            EmitGeneratorSuspendResume(iterReg, true, inspectActiveDelegateOnNext: true,
                delegateCompletedAsNextLabel: doneLabel);
        }
        finally
        {
            UnpinSuspendRegister(sentReg);
            UnpinSuspendRegister(iterReg);
        }

        EmitStarRegister(sentReg);
        builder.EmitJump(JsOpCode.Jump, loopLabel);

        builder.BindLabel(doneLabel);
    }

    private void EmitForAwaitOfStatement(
        JsForInOfStatement stmt,
        IReadOnlyList<string>? labels = null,
        bool needsPerIterationContext = false)
    {
        if (functionKind is not (JsBytecodeFunctionKind.Async or JsBytecodeFunctionKind.AsyncGenerator))
            throw new NotSupportedException("for await...of is only valid inside async functions.");

        if (CanUseSimpleForAwaitEmit(stmt.Body, labels, needsPerIterationContext))
        {
            EmitSimpleForAwaitOfStatement(stmt);
            return;
        }

        VisitExpression(stmt.Right);
        var iterableReg = AllocateTemporaryRegister();
        EmitStarRegister(iterableReg);

        var nextNameIdx = builder.AddAtomizedStringConstant("next");
        var returnNameIdx = builder.AddAtomizedStringConstant("return");
        var doneNameIdx = builder.AddAtomizedStringConstant("done");
        var valueNameIdx = builder.AddAtomizedStringConstant("value");

        var iteratorMethodReg = AllocateTemporaryRegister();
        var iterReg = AllocateTemporaryRegister();
        var resultReg = AllocateTemporaryRegister();
        var valueReg = AllocateTemporaryRegister();
        var closeRequestedReg = AllocateSyntheticLocal($"$forAwait.close.{finallyTempUniqueId}");
        var iteratorDoneReg = AllocateSyntheticLocal($"$forAwait.done.{finallyTempUniqueId}");
        finallyTempUniqueId++;

        var loopLabel = builder.CreateLabel();
        var continueLabel = builder.CreateLabel();
        var doneLabel = builder.CreateLabel();
        var resultReadyLabel = builder.CreateLabel();
        var stepDoneLabel = builder.CreateLabel();

        EmitCreateIteratorFromAsyncOrSyncIterable(iterableReg, iteratorMethodReg, iterReg);
        EmitSetBooleanRegister(iteratorDoneReg, false);
        builder.BindLabel(loopLabel);
        EmitLdaNamedPropertyByIndex(iterReg, nextNameIdx, builder.AllocateFeedbackSlot());
        var nextFnReg = AllocateTemporaryRegister();
        EmitStarRegister(nextFnReg);
        EmitRaw(JsOpCode.CallProperty, (byte)nextFnReg, (byte)iterReg, 0, 0);
        EmitGeneratorSuspendResume(minimizeLiveRange: true, isAwaitSuspend: true);
        EmitStarRegister(resultReg);

        builder.BindLabel(resultReadyLabel);
        EmitEnsureIteratorResultObject(resultReg);

        EmitLdaNamedPropertyByIndex(resultReg, doneNameIdx, builder.AllocateFeedbackSlot());
        builder.EmitJump(JsOpCode.JumpIfToBooleanTrue, stepDoneLabel);

        EmitLdaNamedPropertyByIndex(resultReg, valueNameIdx, builder.AllocateFeedbackSlot());
        EmitStarRegister(valueReg);

        var completionKindReg = AllocateSyntheticLocal($"$forAwait.kind.{finallyTempUniqueId}");
        var completionValueReg = AllocateSyntheticLocal($"$forAwait.value.{finallyTempUniqueId}");
        var kindCompareReg = AllocateSyntheticLocal($"$forAwait.kindcmp.{finallyTempUniqueId}");
        var routeCompareReg = AllocateSyntheticLocal($"$forAwait.routecmp.{finallyTempUniqueId}");
        var closeMethodReg = AllocateSyntheticLocal($"$forAwait.closeMethod.{finallyTempUniqueId}");
        var closeResultReg = AllocateSyntheticLocal($"$forAwait.closeResult.{finallyTempUniqueId}");
        var routeMap = new FinallyJumpRouteMap();
        finallyTempUniqueId++;

        var catchLabel = builder.CreateLabel();
        var finallyFromTryLabel = builder.CreateLabel();
        var finallyEntryLabel = builder.CreateLabel();
        var iterationEndLabel = builder.CreateLabel();
        var closeDoneLabel = builder.CreateLabel();
        var closeResultObjectLabel = builder.CreateLabel();
        var closeThrowAwaitDoneLabel = builder.CreateLabel();
        var normalContinueLabel = builder.CreateLabel();
        var returnLabel = builder.CreateLabel();
        var throwLabel = builder.CreateLabel();
        var notReturnLabel = builder.CreateLabel();
        var notThrowLabel = builder.CreateLabel();

        EmitLdaZero();
        EmitStarRegister(completionKindReg);
        EmitStarRegister(completionValueReg);
        EmitSetBooleanRegister(closeRequestedReg, false);

        builder.EmitJump(JsOpCode.PushTry, catchLabel);
        activeFinallyFlow.Push(new(
            completionKindReg,
            completionValueReg,
            finallyFromTryLabel,
            true,
            routeMap));
        activeForAwaitLoops.Push(new(doneLabel, continueLabel, closeRequestedReg));
        try
        {
            EmitLdaRegister(valueReg);
            EmitForIterationAssignLeft(stmt.Left, true);

            loopTargets.Push(new(doneLabel, continueLabel));
            breakTargets.Push(doneLabel);
            if (labels is not null && labels.Count != 0)
                PushLabeledTargets(labels, doneLabel, continueLabel, true);
            try
            {
                if (TryGetUsingLikeForInOfLeft(stmt, out var usingLikeDeclaration))
                {
                    EmitExplicitResourceScope(
                        () => VisitStatement(stmt.Body),
                        usingLikeDeclaration.Kind == JsVariableDeclarationKind.AwaitUsing,
                        _ => EmitRegisterExplicitResource(usingLikeDeclaration.Kind, valueReg));
                }
                else
                {
                    VisitStatement(stmt.Body);
                }
            }
            finally
            {
                if (labels is not null && labels.Count != 0)
                    PopLabeledTargets(labels.Count);
                breakTargets.Pop();
                loopTargets.Pop();
            }
        }
        finally
        {
            activeForAwaitLoops.Pop();
            activeFinallyFlow.Pop();
        }

        EmitRaw(JsOpCode.PopTry);
        EmitJump(finallyEntryLabel);

        builder.BindLabel(finallyFromTryLabel);
        EmitRaw(JsOpCode.PopTry);
        EmitJump(finallyEntryLabel);

        builder.BindLabel(catchLabel);
        EmitStarRegister(completionValueReg);
        EmitLda(2);
        EmitStarRegister(completionKindReg);
        EmitJump(finallyEntryLabel);

        builder.BindLabel(finallyEntryLabel);
        EmitLdaRegister(iteratorDoneReg);
        builder.EmitJump(JsOpCode.JumpIfToBooleanTrue, iterationEndLabel);

        var closeOnThrowLabel = builder.CreateLabel();
        var closeLabel = builder.CreateLabel();
        EmitLdaRegister(closeRequestedReg);
        builder.EmitJump(JsOpCode.JumpIfToBooleanTrue, closeLabel);
        EmitLda(2);
        EmitStarRegister(kindCompareReg);
        EmitLdaRegister(completionKindReg);
        EmitTestEqualStrictRegister(kindCompareReg);
        EmitJumpIfToBooleanFalse(iterationEndLabel);
        builder.BindLabel(closeOnThrowLabel);
        builder.BindLabel(closeLabel);
        EmitLdaRegister(iterReg);
        EmitLda(2);
        EmitStarRegister(kindCompareReg);
        EmitLdaRegister(completionKindReg);
        EmitTestEqualStrictRegister(kindCompareReg);
        var closeNormalPathLabel = builder.CreateLabel();
        EmitJumpIfToBooleanFalse(closeNormalPathLabel);
        EmitCallRuntime(RuntimeId.AsyncIteratorCloseBestEffort, iterReg, 1);
        EmitStarRegister(closeResultReg);
        EmitLdaTheHole();
        EmitTestEqualStrictRegister(closeResultReg);
        builder.EmitJump(JsOpCode.JumpIfTrue, closeThrowAwaitDoneLabel);
        EmitLdaRegister(closeResultReg);
        EmitGeneratorSuspendResume(minimizeLiveRange: true, isAwaitSuspend: true);
        EmitJump(closeThrowAwaitDoneLabel);
        builder.BindLabel(closeNormalPathLabel);
        EmitLdaNamedPropertyByIndex(iterReg, returnNameIdx, builder.AllocateFeedbackSlot());
        builder.EmitJump(JsOpCode.JumpIfNull, closeDoneLabel);
        builder.EmitJump(JsOpCode.JumpIfUndefined, closeDoneLabel);
        EmitStarRegister(closeMethodReg);
        EmitRaw(JsOpCode.CallProperty, (byte)closeMethodReg, (byte)iterReg, 0, 0);
        EmitStarRegister(closeResultReg);
        EmitLdaTheHole();
        EmitTestEqualStrictRegister(closeResultReg);
        builder.EmitJump(JsOpCode.JumpIfTrue, closeDoneLabel);
        EmitLdaRegister(closeResultReg);
        EmitGeneratorSuspendResume(minimizeLiveRange: true, isAwaitSuspend: true);
        builder.EmitJump(JsOpCode.JumpIfJsReceiver, closeResultObjectLabel);
        EmitCallRuntime(RuntimeId.ThrowIteratorResultNotObject, 0, 0);
        builder.BindLabel(closeResultObjectLabel);
        builder.BindLabel(closeThrowAwaitDoneLabel);
        builder.BindLabel(closeDoneLabel);

        builder.BindLabel(iterationEndLabel);
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

        BytecodeBuilder.Label breakDispatchLabel = default;
        BytecodeBuilder.Label continueDispatchLabel = default;
        if (routeMap.HasBreakRoutes)
        {
            var notBreakLabel = builder.CreateLabel();
            breakDispatchLabel = builder.CreateLabel();
            EmitLda(3);
            EmitStarRegister(kindCompareReg);
            EmitLdaRegister(completionKindReg);
            EmitTestEqualStrictRegister(kindCompareReg);
            EmitJumpIfToBooleanFalse(notBreakLabel);
            EmitJump(breakDispatchLabel);
            builder.BindLabel(notBreakLabel);
        }

        if (routeMap.HasContinueRoutes)
        {
            var notContinueLabel = builder.CreateLabel();
            continueDispatchLabel = builder.CreateLabel();
            EmitLda(4);
            EmitStarRegister(kindCompareReg);
            EmitLdaRegister(completionKindReg);
            EmitTestEqualStrictRegister(kindCompareReg);
            EmitJumpIfToBooleanFalse(notContinueLabel);
            EmitJump(continueDispatchLabel);
            builder.BindLabel(notContinueLabel);
        }

        EmitJump(normalContinueLabel);

        builder.BindLabel(returnLabel);
        EmitLdaRegister(completionValueReg);
        EmitReturnConsideringFinallyFlow();

        builder.BindLabel(throwLabel);
        EmitLdaRegister(completionValueReg);
        EmitThrowConsideringFinallyFlow();

        if (breakDispatchLabel.IsInitialized)
        {
            var noBreakRouteMatchedLabel = builder.CreateLabel();
            builder.BindLabel(breakDispatchLabel);
            EmitFinallyRouteDispatch(routeMap, false, completionValueReg, routeCompareReg,
                noBreakRouteMatchedLabel);
            builder.BindLabel(noBreakRouteMatchedLabel);
            EmitJump(normalContinueLabel);
        }

        if (continueDispatchLabel.IsInitialized)
        {
            builder.BindLabel(continueDispatchLabel);
            EmitFinallyRouteDispatch(routeMap, true, completionValueReg, routeCompareReg, continueLabel);
        }

        builder.BindLabel(normalContinueLabel);
        builder.BindLabel(continueLabel);
        if (needsPerIterationContext)
            EmitRotatePerIterationContext();
        EmitJump(loopLabel);

        builder.BindLabel(stepDoneLabel);
        EmitSetBooleanRegister(iteratorDoneReg, true);
        builder.BindLabel(doneLabel);
    }

    private void EmitSimpleForAwaitOfStatement(JsForInOfStatement stmt)
    {
        VisitExpression(stmt.Right);
        var iterableReg = AllocateTemporaryRegister();
        EmitStarRegister(iterableReg);

        var nextNameIdx = builder.AddAtomizedStringConstant("next");
        var doneNameIdx = builder.AddAtomizedStringConstant("done");
        var valueNameIdx = builder.AddAtomizedStringConstant("value");

        var iteratorMethodReg = AllocateTemporaryRegister();
        var iterReg = AllocateTemporaryRegister();
        var resultReg = AllocateTemporaryRegister();
        var valueReg = AllocateTemporaryRegister();
        var thrownValueReg = AllocateSyntheticLocal($"$forAwait.simple.throw.{finallyTempUniqueId++}");
        var closeResultReg = AllocateSyntheticLocal($"$forAwait.simple.close.{finallyTempUniqueId++}");

        var loopLabel = builder.CreateLabel();
        var continueLabel = builder.CreateLabel();
        var doneLabel = builder.CreateLabel();
        var catchLabel = builder.CreateLabel();
        var rethrowLabel = builder.CreateLabel();
        var stepDoneLabel = builder.CreateLabel();

        EmitCreateIteratorFromAsyncOrSyncIterable(iterableReg, iteratorMethodReg, iterReg);
        builder.BindLabel(loopLabel);
        EmitLdaNamedPropertyByIndex(iterReg, nextNameIdx, builder.AllocateFeedbackSlot());
        var nextFnReg = AllocateTemporaryRegister();
        EmitStarRegister(nextFnReg);
        EmitRaw(JsOpCode.CallProperty, (byte)nextFnReg, (byte)iterReg, 0, 0);
        EmitGeneratorSuspendResume(minimizeLiveRange: true, isAwaitSuspend: true);
        EmitStarRegister(resultReg);

        EmitEnsureIteratorResultObject(resultReg);

        EmitLdaNamedPropertyByIndex(resultReg, doneNameIdx, builder.AllocateFeedbackSlot());
        builder.EmitJump(JsOpCode.JumpIfToBooleanTrue, stepDoneLabel);

        EmitLdaNamedPropertyByIndex(resultReg, valueNameIdx, builder.AllocateFeedbackSlot());
        EmitStarRegister(valueReg);

        builder.EmitJump(JsOpCode.PushTry, catchLabel);
        EmitLdaRegister(valueReg);
        EmitForIterationAssignLeft(stmt.Left, true);
        if (TryGetUsingLikeForInOfLeft(stmt, out var usingLikeDeclaration))
        {
            EmitExplicitResourceScope(
                () => VisitStatement(stmt.Body),
                usingLikeDeclaration.Kind == JsVariableDeclarationKind.AwaitUsing,
                _ => EmitRegisterExplicitResource(usingLikeDeclaration.Kind, valueReg));
        }
        else
        {
            VisitStatement(stmt.Body);
        }
        EmitRaw(JsOpCode.PopTry);
        EmitJump(continueLabel);

        builder.BindLabel(catchLabel);
        EmitStarRegister(thrownValueReg);
        EmitCallRuntime(RuntimeId.AsyncIteratorCloseBestEffort, iterReg, 1);
        EmitStarRegister(closeResultReg);
        EmitLdaTheHole();
        EmitTestEqualStrictRegister(closeResultReg);
        builder.EmitJump(JsOpCode.JumpIfTrue, rethrowLabel);
        EmitLdaRegister(closeResultReg);
        EmitGeneratorSuspendResume(minimizeLiveRange: true, isAwaitSuspend: true);
        builder.BindLabel(rethrowLabel);
        EmitLdaRegister(thrownValueReg);
        EmitThrowConsideringFinallyFlow();

        builder.BindLabel(continueLabel);
        EmitJump(loopLabel);

        builder.BindLabel(stepDoneLabel);
        builder.BindLabel(doneLabel);
    }

    private static bool CanUseSimpleForAwaitEmit(
        JsStatement body,
        IReadOnlyList<string>? labels,
        bool needsPerIterationContext)
    {
        if (needsPerIterationContext)
            return false;
        if (labels is not null && labels.Count != 0)
            return false;

        return !StatementHasExplicitAbruptForAwaitControl(body);
    }

    private static bool StatementHasExplicitAbruptForAwaitControl(JsStatement stmt)
    {
        switch (stmt)
        {
            case JsReturnStatement:
            case JsBreakStatement:
            case JsContinueStatement:
            case JsThrowStatement:
                return true;
            case JsBlockStatement block:
                foreach (var child in block.Statements)
                    if (StatementHasExplicitAbruptForAwaitControl(child))
                        return true;

                return false;
            case JsIfStatement conditional:
                return StatementHasExplicitAbruptForAwaitControl(conditional.Consequent) ||
                       (conditional.Alternate is not null &&
                        StatementHasExplicitAbruptForAwaitControl(conditional.Alternate));
            case JsWhileStatement whileStatement:
                return StatementHasExplicitAbruptForAwaitControl(whileStatement.Body);
            case JsDoWhileStatement doWhileStatement:
                return StatementHasExplicitAbruptForAwaitControl(doWhileStatement.Body);
            case JsForStatement forStatement:
                return StatementHasExplicitAbruptForAwaitControl(forStatement.Body);
            case JsForInOfStatement forInOfStatement:
                return StatementHasExplicitAbruptForAwaitControl(forInOfStatement.Body);
            case JsLabeledStatement labeledStatement:
                return StatementHasExplicitAbruptForAwaitControl(labeledStatement.Statement);
            case JsSwitchStatement switchStatement:
                foreach (var @case in switchStatement.Cases)
                foreach (var consequent in @case.Consequent)
                    if (StatementHasExplicitAbruptForAwaitControl(consequent))
                        return true;

                return false;
            case JsTryStatement tryStatement:
                return StatementHasExplicitAbruptForAwaitControl(tryStatement.Block) ||
                       (tryStatement.Handler is not null &&
                        StatementHasExplicitAbruptForAwaitControl(tryStatement.Handler.Body)) ||
                       (tryStatement.Finalizer is not null &&
                        StatementHasExplicitAbruptForAwaitControl(tryStatement.Finalizer));
            case JsWithStatement withStatement:
                return StatementHasExplicitAbruptForAwaitControl(withStatement.Body);
            case JsExportDeclarationStatement exportDeclaration:
                return StatementHasExplicitAbruptForAwaitControl(exportDeclaration.Declaration);
            default:
                return false;
        }
    }
}
