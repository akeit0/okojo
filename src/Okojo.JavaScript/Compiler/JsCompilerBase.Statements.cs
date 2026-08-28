using System.Buffers;
using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler;

internal abstract partial class JsCompilerBase
{
    private readonly struct ExplicitResourceScope(int stackRegister, bool isAsync)
    {
        public readonly int StackRegister = stackRegister;
        public readonly bool IsAsync = isAsync;
    }

    private readonly Stack<ExplicitResourceScope> activeExplicitResourceScopes = new();

    protected bool hasActiveModuleTopLevelExplicitResourceScope;
    protected bool moduleTopLevelExplicitResourceScopeIsAsync;

    private bool HasAmbientExplicitResourceScope =>
        activeExplicitResourceScopes.Count != 0 || hasActiveModuleTopLevelExplicitResourceScope;

    protected void EmitStatement(JsAst ast, int nodeIndex)
    {
        ref readonly var node = ref ast[nodeIndex];
        if (node.Kind != AstKind.FunctionDeclaration)
            EmitSourcePosition(ast.GetPosition(nodeIndex));

        // Completion-value tracking (script roots only): constructs whose spec
        // completion starts empty reset the sink before emitting, mirroring V8
        // rewriter.cc's AssignUndefinedBefore.
        if (CompletionSinkActive && StatementNeedsCompletionReset(ast, nodeIndex))
        {
            builder.EmitLda(JsOpCode.LdaUndefined);
            CaptureCompletionValue();
        }

        switch (node.Kind)
        {
            case AstKind.ImportDeclaration:
                return;
            case AstKind.ExportDeclaration:
                EmitExportDeclaration(ast, node);
                return;
            case AstKind.VariableDeclaration:
                EmitVariableDeclarationStatement(ast, nodeIndex);
                return;
            case AstKind.FunctionDeclaration:
                return;
            case AstKind.ClassDeclaration:
                EmitClassDeclaration(ast, node);
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
                EmitForStatementEntry(ast, nodeIndex, node);
                return;
            case AstKind.ForInOfStatement:
                EmitForInOfStatement(ast, nodeIndex, node);
                return;
            case AstKind.LabeledStatement:
                EmitLabeledStatement(ast, node);
                return;
            case AstKind.BreakStatement:
                EmitAbruptCommand(
                    AbruptCommand.Break,
                    node.Arg0 < 0 ? null : ast.GetString(node.Arg0)
                );
                return;
            case AstKind.ContinueStatement:
                EmitAbruptCommand(
                    AbruptCommand.Continue,
                    node.Arg0 < 0 ? null : ast.GetString(node.Arg0)
                );
                return;
            case AstKind.ExpressionStatement:
                if (CompletionSinkActive)
                    EmitExpression(ast, node.Arg0);
                else
                    EmitExpressionForEffect(ast, node.Arg0);
                CaptureCompletionValue();
                return;
            case AstKind.ReturnStatement:
                if (node.Arg0 >= 0)
                {
                    if (returnInferredNameStringIndex >= 0)
                        EmitExpressionWithInferredName(
                            ast,
                            node.Arg0,
                            ast.GetString(returnInferredNameStringIndex)
                        );
                    else if (returnInferredNameFromFirstParameter)
                        EmitExpressionWithComputedName(ast, node.Arg0, 0);
                    else
                        EmitExpression(ast, node.Arg0);
                    if (isGenerator && isAsync)
                        EmitAwaitSuspension();
                }
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
            case AstKind.DebuggerStatement:
                builder.Emit(JsOpCode.Debugger);
                return;
            default:
                throw new NotSupportedException(
                    $"{CompilerName} does not support statement '{node.Kind}'."
                );
        }
    }

    private void EmitSourcePosition(int sourceOffset)
    {
        if (sourceOffset >= 0)
            builder.SetPendingSourceOffset(sourceOffset);
    }

    private void EmitExportDeclaration(JsAst ast, AstNode node)
    {
        if (node.Arg0 < 0)
            return;

        var entries = ast.GetExportEntries(node);
        if (entries.Length == 0)
            return;
        if (entries[0].Kind == JsExportKind.Local)
        {
            EmitStatement(ast, node.Arg0);
            return;
        }

        ref readonly var value = ref ast[node.Arg0];
        if (
            entries[0].Kind == JsExportKind.DefaultDeclaration
            && value.Kind == AstKind.FunctionExpression
        )
            return;

        var localName = ast.GetString(entries[0].LocalNameStringIndex);
        EmitExpressionWithInferredName(ast, node.Arg0, "default");
        if (!TryResolveBinding(localName, out var binding))
            throw new InvalidOperationException(
                $"No planned binding found for default export '{localName}'."
            );
        EmitStore(binding, isInitialization: true);
    }

    private void EmitWhileStatement(JsAst ast, int test, int body, string[]? labels = null)
    {
        var incoming = CaptureKnownInitializedLexicals();
        var continueTarget = builder.CreateLabel();
        var breakTarget = builder.CreateLabel();
        builder.BindLabel(continueTarget);
        EmitExpressionForTest(ast, test, breakTarget, jumpIfTrue: false);
        PushIterationControlScope(breakTarget, continueTarget, labels);
        EnterBreakableContext();
        try
        {
            EmitStatement(ast, body);
        }
        finally
        {
            ExitBreakableContext();
            controlScopes.Pop();
        }
        EmitJump(continueTarget);
        builder.BindLabel(breakTarget);
        RestoreKnownInitializedLexicals(incoming);
    }

    private void EmitDoWhileStatement(JsAst ast, int body, int test, string[]? labels = null)
    {
        var incoming = CaptureKnownInitializedLexicals();
        var loopStart = builder.CreateLabel();
        var continueTarget = builder.CreateLabel();
        var breakTarget = builder.CreateLabel();
        builder.BindLabel(loopStart);
        PushIterationControlScope(breakTarget, continueTarget, labels);
        EnterBreakableContext();
        try
        {
            EmitStatement(ast, body);
        }
        finally
        {
            ExitBreakableContext();
            controlScopes.Pop();
        }
        builder.BindLabel(continueTarget);
        EmitExpressionForTest(ast, test, breakTarget, jumpIfTrue: false);
        EmitJump(loopStart);
        builder.BindLabel(breakTarget);
        RestoreKnownInitializedLexicals(incoming);
    }

    private void EmitForStatementEntry(JsAst ast, int nodeIndex, AstNode node)
    {
        var parts = ast.ChildRange(node.Arg0, node.Arg1);
        var init = parts[0];
        if (
            init < 0
            || ast[init].Kind != AstKind.VariableDeclaration
            || !((JsVariableDeclarationKind)ast[init].Arg2).IsUsingLike()
        )
        {
            EmitForStatement(ast, nodeIndex, node);
            return;
        }

        var kind = (JsVariableDeclarationKind)ast[init].Arg2;
        EmitExplicitResourceScope(
            () => EmitForStatement(ast, nodeIndex, node),
            kind == JsVariableDeclarationKind.AwaitUsing
        );
    }

    private void EmitForStatement(JsAst ast, int nodeIndex, AstNode node, string[]? labels = null)
    {
        var parts = ast.ChildRange(node.Arg0, node.Arg1);
        var init = parts[0];
        var hasLexicalScope =
            init >= 0
            && ast[init].Kind == AstKind.VariableDeclaration
            && (JsVariableDeclarationKind)ast[init].Arg2
                is JsVariableDeclarationKind.Let
                    or JsVariableDeclarationKind.Const
                    or JsVariableDeclarationKind.Using
                    or JsVariableDeclarationKind.AwaitUsing;
        if (hasLexicalScope)
        {
            var scope = FindChildScope(
                activeScopes.Peek().ScopeId,
                CompilerCollectedScopeKind.Block,
                ast.GetPosition(nodeIndex)
            );
            PrepareLoopLexicalHoleInitializationSkip(ast, init, scope.ScopeId);
            EnterScope(scope.ScopeId);
        }

        HashSet<CompilerPlannedBinding>? afterInit = null;
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

            afterInit = CaptureKnownInitializedLexicals();

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

            PushIterationControlScope(breakTarget, continueTarget, labels);
            EnterBreakableContext();
            try
            {
                EmitStatement(ast, parts[3]);
            }
            finally
            {
                ExitBreakableContext();
                controlScopes.Pop();
            }

            builder.BindLabel(continueTarget);
            if (needsPerIterationContext)
                EmitReplaceCurrentContext(activeScopes.Peek().ContextSlotCount);
            if (parts[2] >= 0)
                EmitExpressionForEffect(ast, parts[2]);
            EmitJump(loopStart);
            builder.BindLabel(breakTarget);
        }
        finally
        {
            if (afterInit is not null)
                RestoreKnownInitializedLexicals(afterInit);
            if (hasLexicalScope)
                LeaveScope();
        }
    }

    private void EmitForInOfStatement(
        JsAst ast,
        int nodeIndex,
        AstNode node,
        string[]? labels = null
    )
    {
        if (node.Arg2 == 2)
        {
            EmitForAwaitOfStatement(ast, nodeIndex, node, labels);
            return;
        }
        if (node.Arg2 == 1)
        {
            EmitForOfStatement(ast, nodeIndex, node, labels);
            return;
        }

        var parts = ast.ChildRange(node.Arg0, node.Arg1);
        var left = parts[0];
        var hasLexicalScope =
            ast[left].Kind == AstKind.VariableDeclaration
            && (JsVariableDeclarationKind)ast[left].Arg2
                is JsVariableDeclarationKind.Let
                    or JsVariableDeclarationKind.Const
                    or JsVariableDeclarationKind.Using
                    or JsVariableDeclarationKind.AwaitUsing;
        if (hasLexicalScope)
        {
            var scope = FindChildScope(
                activeScopes.Peek().ScopeId,
                CompilerCollectedScopeKind.Block,
                ast.GetPosition(nodeIndex)
            );
            EnterScope(scope.ScopeId);
        }

        var incoming = CaptureKnownInitializedLexicals();
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
            if (needsPerIterationContext)
                EmitReplaceCurrentContext(activeScopes.Peek().ContextSlotCount);
            EmitForInRegisterOperation(JsOpCode.ForInNext, RuntimeId.ForInNext, enumeratorRegister);
            builder.EmitJump(JsOpCode.JumpIfUndefined, breakTarget);
            EmitForIterationAssignment(ast, left);

            PushIterationControlScope(breakTarget, continueTarget, labels);
            EnterBreakableContext();
            try
            {
                EmitStatement(ast, parts[2]);
            }
            finally
            {
                ExitBreakableContext();
                controlScopes.Pop();
            }

            builder.BindLabel(continueTarget);
            EmitForInRegisterOperation(JsOpCode.ForInStep, RuntimeId.ForInStep, enumeratorRegister);
            EmitJump(loopStart);
            builder.BindLabel(breakTarget);
        }
        finally
        {
            RestoreKnownInitializedLexicals(incoming);
            builder.ReleaseTemporaryRegistersToMarker(marker);
            if (hasLexicalScope)
                LeaveScope();
        }
    }

    private void EmitForOfStatement(JsAst ast, int nodeIndex, AstNode node, string[]? labels)
    {
        var parts = ast.ChildRange(node.Arg0, node.Arg1);
        var left = parts[0];
        var hasLexicalScope =
            ast[left].Kind == AstKind.VariableDeclaration
            && (JsVariableDeclarationKind)ast[left].Arg2
                is JsVariableDeclarationKind.Let
                    or JsVariableDeclarationKind.Const
                    or JsVariableDeclarationKind.Using
                    or JsVariableDeclarationKind.AwaitUsing;
        if (hasLexicalScope)
        {
            var scope = FindChildScope(
                activeScopes.Peek().ScopeId,
                CompilerCollectedScopeKind.Block,
                ast.GetPosition(nodeIndex)
            );
            EnterScope(scope.ScopeId);
        }

        var incoming = CaptureKnownInitializedLexicals();
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            EmitExpression(ast, parts[1]);
            var sourceRegister = builder.AllocateTemporaryRegister();
            EmitStar(sourceRegister);
            var needsPerIterationContext =
                hasLexicalScope && ShouldReplaceLoopHeadContextPerIteration(activeScopes.Peek());
            var arrayLengthRegister = builder.AllocateTemporaryRegister();
            builder.EmitCallRuntime((int)RuntimeId.ForOfFastPathLength, sourceRegister, 1);
            EmitStar(arrayLengthRegister);

            var fastArrayLabel = builder.CreateLabel();
            var fallbackLabel = builder.CreateLabel();
            var doneLabel = builder.CreateLabel();

            EmitLdar(arrayLengthRegister);
            builder.Emit(JsOpCode.TestLessThanSmi, 0, 0);
            EmitJumpIfToBooleanFalse(fastArrayLabel);
            EmitJump(fallbackLabel);

            EmitForOfFastArrayPath(
                ast,
                left,
                parts[2],
                sourceRegister,
                arrayLengthRegister,
                fastArrayLabel,
                doneLabel,
                labels,
                needsPerIterationContext
            );
            EmitForOfIteratorFallbackPath(
                ast,
                left,
                parts[2],
                sourceRegister,
                fallbackLabel,
                doneLabel,
                labels,
                needsPerIterationContext
            );

            builder.BindLabel(doneLabel);
        }
        finally
        {
            RestoreKnownInitializedLexicals(incoming);
            builder.ReleaseTemporaryRegistersToMarker(marker);
            if (hasLexicalScope)
                LeaveScope();
        }
    }

    private void EmitForOfFastArrayPath(
        JsAst ast,
        int left,
        int body,
        int sourceRegister,
        int arrayLengthRegister,
        BytecodeBuilder.Label fastArrayLabel,
        BytecodeBuilder.Label doneLabel,
        string[]? labels,
        bool needsPerIterationContext
    )
    {
        builder.BindLabel(fastArrayLabel);
        var indexRegister = builder.AllocateTemporaryRegister();
        var valueRegister = builder.AllocateTemporaryRegister();
        EmitSmi(0);
        EmitStar(indexRegister);

        var loopStart = builder.CreateLabel();
        var continueTarget = builder.CreateLabel();
        builder.BindLabel(loopStart);
        if (needsPerIterationContext)
            EmitReplaceCurrentContext(activeScopes.Peek().ContextSlotCount);

        builder.EmitCallRuntime((int)RuntimeId.ForOfFastPathLength, sourceRegister, 1);
        EmitStar(arrayLengthRegister);
        EmitLdar(arrayLengthRegister);
        builder.Emit(JsOpCode.TestLessThan, (byte)indexRegister, 0);
        EmitJumpIfToBooleanFalse(doneLabel);

        EmitLdar(indexRegister);
        builder.EmitLdaKeyedProperty(sourceRegister);
        EmitStar(valueRegister);

        PushIterationControlScope(doneLabel, continueTarget, labels);
        EnterBreakableContext();
        try
        {
            EmitLdar(valueRegister);
            EmitForIterationAssignment(ast, left);
            EmitForOfIterationBodyWithResources(ast, left, valueRegister, body, false);
        }
        finally
        {
            ExitBreakableContext();
            controlScopes.Pop();
        }

        builder.BindLabel(continueTarget);
        EmitLdar(indexRegister);
        builder.Emit(JsOpCode.Inc);
        EmitStar(indexRegister);
        EmitJump(loopStart);
    }

    private void EmitForOfIteratorFallbackPath(
        JsAst ast,
        int left,
        int body,
        int sourceRegister,
        BytecodeBuilder.Label fallbackLabel,
        BytecodeBuilder.Label doneLabel,
        string[]? labels,
        bool needsPerIterationContext
    )
    {
        builder.BindLabel(fallbackLabel);

        var methodRegister = builder.AllocateTemporaryRegister();
        builder.EmitCallRuntime((int)RuntimeId.GetIteratorMethod, sourceRegister, 1);
        EmitStar(methodRegister);
        builder.EmitCallProperty(methodRegister, sourceRegister, 0, 0);
        var iteratorRegister = builder.AllocateTemporaryRegister();
        EmitStar(iteratorRegister);

        var nextName = builder.AddAtomizedStringConstant("next");
        var doneName = builder.AddAtomizedStringConstant("done");
        var valueName = builder.AddAtomizedStringConstant("value");
        var nextFunctionRegister = builder.AllocateTemporaryRegister();
        var resultRegister = builder.AllocateTemporaryRegister();
        builder.EmitLdaNamedProperty(iteratorRegister, nextName, builder.AllocateFeedbackSlot());
        EmitStar(nextFunctionRegister);
        var valueRegister = builder.AllocateTemporaryRegister();
        var exceptionRegister = builder.AllocateTemporaryRegister();
        var loopStart = builder.CreateLabel();
        var continueTarget = builder.CreateLabel();
        var catchTarget = builder.CreateLabel();
        var resultIsObject = builder.CreateLabel();

        builder.BindLabel(loopStart);
        if (needsPerIterationContext)
            EmitReplaceCurrentContext(activeScopes.Peek().ContextSlotCount);
        builder.EmitCallProperty(nextFunctionRegister, iteratorRegister, 0, 0);
        EmitStar(resultRegister);
        builder.EmitJump(JsOpCode.JumpIfJsReceiver, resultIsObject);
        builder.EmitCallRuntime((int)RuntimeId.ThrowIteratorResultNotObject, 0, 0);
        builder.BindLabel(resultIsObject);
        builder.EmitLdaNamedProperty(resultRegister, doneName, builder.AllocateFeedbackSlot());
        EmitJumpIfToBooleanTrue(doneLabel);
        builder.EmitLdaNamedProperty(resultRegister, valueName, builder.AllocateFeedbackSlot());
        EmitStar(valueRegister);

        builder.EmitJump(JsOpCode.PushTry, catchTarget);
        PushForOfControlScope(doneLabel, continueTarget, iteratorRegister, labels);
        PushTryControlScope();
        EnterBreakableContext();
        try
        {
            EmitLdar(valueRegister);
            EmitForIterationAssignment(ast, left);
            EmitForOfIterationBodyWithResources(ast, left, valueRegister, body, false);
        }
        finally
        {
            ExitBreakableContext();
            controlScopes.Pop();
            controlScopes.Pop();
        }
        builder.Emit(JsOpCode.PopTry);

        builder.BindLabel(continueTarget);
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
    }

    private void EmitSimpleForAwaitOfStatement(JsAst ast, ReadOnlySpan<int> parts)
    {
        EmitExpression(ast, parts[1]);
        var sourceRegister = builder.AllocateTemporaryRegister();
        EmitStar(sourceRegister);
        var methodRegister = builder.AllocateTemporaryRegister();
        var iteratorRegister = builder.AllocateTemporaryRegister();
        EmitCreateAsyncOrSyncIterator(sourceRegister, methodRegister, iteratorRegister);
        var nextFunctionRegister = builder.AllocateTemporaryRegister();
        var resultRegister = builder.AllocateTemporaryRegister();

        var nextName = builder.AddAtomizedStringConstant("next");
        var doneName = builder.AddAtomizedStringConstant("done");
        var valueName = builder.AddAtomizedStringConstant("value");
        var loopStart = builder.CreateLabel();
        var iterationDone = builder.CreateLabel();
        var resultIsObject = builder.CreateLabel();

        builder.BindLabel(loopStart);
        builder.EmitLdaNamedProperty(iteratorRegister, nextName, builder.AllocateFeedbackSlot());
        EmitStar(nextFunctionRegister);
        builder.EmitCallProperty(nextFunctionRegister, iteratorRegister, 0, 0);
        EmitAwaitSuspension();
        EmitStar(resultRegister);
        builder.EmitJump(JsOpCode.JumpIfJsReceiver, resultIsObject);
        builder.EmitCallRuntime((int)RuntimeId.ThrowIteratorResultNotObject, 0, 0);
        builder.BindLabel(resultIsObject);
        builder.EmitLdaNamedProperty(resultRegister, doneName, builder.AllocateFeedbackSlot());
        EmitJumpIfToBooleanTrue(iterationDone);
        builder.EmitLdaNamedProperty(resultRegister, valueName, builder.AllocateFeedbackSlot());
        EmitForIterationAssignment(ast, parts[0]);
        EmitJump(loopStart);

        builder.BindLabel(iterationDone);
    }

    private void EmitForAwaitOfStatement(JsAst ast, int nodeIndex, AstNode node, string[]? labels)
    {
        if (!isAsync)
            throw new InvalidOperationException("for await...of requires an async function.");

        var parts = ast.ChildRange(node.Arg0, node.Arg1);
        var left = parts[0];
        var hasLexicalScope =
            ast[left].Kind == AstKind.VariableDeclaration
            && (JsVariableDeclarationKind)ast[left].Arg2
                is JsVariableDeclarationKind.Let
                    or JsVariableDeclarationKind.Const
                    or JsVariableDeclarationKind.Using
                    or JsVariableDeclarationKind.AwaitUsing;
        if (hasLexicalScope)
        {
            var scope = FindChildScope(
                activeScopes.Peek().ScopeId,
                CompilerCollectedScopeKind.Block,
                ast.GetPosition(nodeIndex)
            );
            EnterScope(scope.ScopeId);
        }

        var incoming = CaptureKnownInitializedLexicals();
        var canUseSimpleEmit =
            !hasLexicalScope
            && ast[parts[2]].Kind == AstKind.EmptyStatement
            && (labels is null || labels.Length == 0);

        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            if (canUseSimpleEmit)
            {
                EmitSimpleForAwaitOfStatement(ast, parts);
                return;
            }
            EmitExpression(ast, parts[1]);
            var sourceRegister = builder.AllocateTemporaryRegister();
            EmitStar(sourceRegister);
            var methodRegister = builder.AllocateTemporaryRegister();
            var iteratorRegister = builder.AllocateTemporaryRegister();
            EmitCreateAsyncOrSyncIterator(sourceRegister, methodRegister, iteratorRegister);
            var resultRegister = builder.AllocateTemporaryRegister();
            var valueRegister = builder.AllocateTemporaryRegister();
            var nextFunctionRegister = builder.AllocateTemporaryRegister();
            var completionKindRegister = builder.AllocateTemporaryRegister();
            var completionValueRegister = builder.AllocateTemporaryRegister();
            var completionCompareRegister = builder.AllocateTemporaryRegister();
            var abruptRoutes = new List<FinallyAbruptRoute>();
            var nextName = builder.AddAtomizedStringConstant("next");
            var doneName = builder.AddAtomizedStringConstant("done");
            var valueName = builder.AddAtomizedStringConstant("value");
            var loopStart = builder.CreateLabel();
            var continueTarget = builder.CreateLabel();
            var iterationDone = builder.CreateLabel();
            var breakTarget = builder.CreateLabel();
            var catchTarget = builder.CreateLabel();
            var closeTarget = builder.CreateLabel();
            var resultIsObject = builder.CreateLabel();
            var needsPerIterationContext =
                hasLexicalScope && ShouldReplaceLoopHeadContextPerIteration(activeScopes.Peek());

            PushForAwaitOfControlScope(
                breakTarget,
                continueTarget,
                iteratorRegister,
                labels,
                closeTarget,
                completionKindRegister,
                completionValueRegister,
                abruptRoutes
            );
            PushTryControlScope();
            try
            {
                builder.BindLabel(loopStart);
                if (needsPerIterationContext)
                    EmitReplaceCurrentContext(activeScopes.Peek().ContextSlotCount);
                builder.EmitLdaNamedProperty(
                    iteratorRegister,
                    nextName,
                    builder.AllocateFeedbackSlot()
                );
                EmitStar(nextFunctionRegister);
                builder.EmitCallProperty(nextFunctionRegister, iteratorRegister, 0, 0);
                EmitAwaitSuspension();
                EmitStar(resultRegister);
                EmitLdar(resultRegister);
                builder.EmitJump(JsOpCode.JumpIfJsReceiver, resultIsObject);
                builder.EmitCallRuntime((int)RuntimeId.ThrowIteratorResultNotObject, 0, 0);
                builder.BindLabel(resultIsObject);
                builder.EmitLdaNamedProperty(
                    resultRegister,
                    doneName,
                    builder.AllocateFeedbackSlot()
                );
                EmitJumpIfToBooleanTrue(iterationDone);
                builder.EmitLdaNamedProperty(
                    resultRegister,
                    valueName,
                    builder.AllocateFeedbackSlot()
                );
                EmitStar(valueRegister);
                EmitLdar(valueRegister);

                // The close-dispatching region covers only the per-iteration body:
                // a rejection from the awaited next() itself must not run the loop
                // close because AsyncFromSyncIteratorContinuation already closed
                // the sync iterator via its onRejected step.
                builder.EmitJump(JsOpCode.PushTry, catchTarget);
                EmitForIterationAssignment(ast, left);
                EmitForOfIterationBodyWithResources(ast, left, valueRegister, parts[2], true);
                builder.Emit(JsOpCode.PopTry);

                builder.BindLabel(continueTarget);
                EmitJump(loopStart);

                builder.BindLabel(iterationDone);
                EmitJump(breakTarget);
            }
            finally
            {
                controlScopes.Pop();
                controlScopes.Pop();
            }

            builder.BindLabel(catchTarget);
            EmitStar(completionValueRegister);
            EmitSmi(2);
            EmitStar(completionKindRegister);
            EmitJump(closeTarget);

            builder.BindLabel(closeTarget);
            EmitForAwaitIteratorClose(
                iteratorRegister,
                completionKindRegister,
                completionCompareRegister
            );
            EmitFinallyCompletionJump(
                completionKindRegister,
                completionCompareRegister,
                1,
                out var notReturn
            );
            EmitLdar(completionValueRegister);
            EmitAbruptCommand(AbruptCommand.Return);
            builder.BindLabel(notReturn);
            EmitFinallyCompletionJump(
                completionKindRegister,
                completionCompareRegister,
                2,
                out var notThrow
            );
            EmitLdar(completionValueRegister);
            builder.Emit(JsOpCode.Throw);
            builder.BindLabel(notThrow);
            for (var i = 0; i < abruptRoutes.Count; i++)
            {
                var route = abruptRoutes[i];
                EmitFinallyCompletionJump(
                    completionKindRegister,
                    completionCompareRegister,
                    route.CompletionKind,
                    out var nextRoute
                );
                if (
                    route.Command == AbruptCommand.Break
                    && (
                        route.Label is null
                        || labels is not null && Array.IndexOf(labels, route.Label) >= 0
                    )
                )
                    EmitJump(breakTarget);
                else
                    EmitAbruptCommand(route.Command, route.Label);
                builder.BindLabel(nextRoute);
            }
            builder.BindLabel(breakTarget);
        }
        finally
        {
            RestoreKnownInitializedLexicals(incoming);
            builder.ReleaseTemporaryRegistersToMarker(marker);
            if (hasLexicalScope)
                LeaveScope();
        }
    }

    private void EmitForIterationAssignment(JsAst ast, int left)
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

        if (node.Kind is AstKind.ArrayExpression or AstKind.ObjectExpression)
        {
            var marker = builder.GetTemporaryRegisterScopeMarker();
            try
            {
                var sourceRegister = builder.AllocateTemporaryRegister();
                EmitStar(sourceRegister);
                if (node.Kind == AstKind.ArrayExpression)
                    EmitArrayBindingPattern(ast, node, sourceRegister, assignment: true);
                else
                    EmitObjectBindingPattern(ast, node, sourceRegister, assignment: true);
            }
            finally
            {
                builder.ReleaseTemporaryRegistersToMarker(marker);
            }
            return;
        }

        if (node.Kind == AstKind.MemberExpression)
        {
            var marker = builder.GetTemporaryRegisterScopeMarker();
            try
            {
                var valueRegister = builder.AllocateTemporaryRegister();
                EmitStar(valueRegister);
                var reference = PrepareMemberReference(ast, node, normalizeComputedKey: true);
                EmitLdar(valueRegister);
                EmitPreparedMemberStore(reference);
            }
            finally
            {
                builder.ReleaseTemporaryRegistersToMarker(marker);
            }
            return;
        }

        var identifier = ast.GetString(node.Arg0);
        var hasLocalBinding = TryResolveBindingAccess(
            node.Arg1,
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

    private void EmitLabeledStatement(JsAst ast, AstNode statement)
    {
        var labels = new List<string>(2);
        var body = statement.Arg1;
        labels.Add(ast.GetString(statement.Arg0));
        while (ast[body].Kind == AstKind.LabeledStatement)
        {
            labels.Add(ast.GetString(ast[body].Arg0));
            body = ast[body].Arg1;
        }

        var names = labels.ToArray();
        ref readonly var target = ref ast[body];
        switch (target.Kind)
        {
            case AstKind.WhileStatement:
                EmitWhileStatement(ast, target.Arg0, target.Arg1, names);
                return;
            case AstKind.DoWhileStatement:
                EmitDoWhileStatement(ast, target.Arg0, target.Arg1, names);
                return;
            case AstKind.ForStatement:
                EmitForStatement(ast, body, target, names);
                return;
            case AstKind.ForInOfStatement:
                EmitForInOfStatement(ast, body, target, names);
                return;
        }

        var breakTarget = builder.CreateLabel();
        controlScopes.Push(
            new ControlScope(
                ControlScopeKind.Label,
                breakTarget,
                default,
                default,
                CurrentContextDepth,
                Labels: names
            )
        );
        EnterBreakableContext();
        try
        {
            EmitStatement(ast, body);
        }
        finally
        {
            ExitBreakableContext();
            controlScopes.Pop();
        }
        builder.BindLabel(breakTarget);
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
        BytecodeBuilder.Label continueTarget,
        string[]? labels = null
    )
    {
        controlScopes.Push(
            new ControlScope(
                ControlScopeKind.Iteration,
                breakTarget,
                continueTarget,
                default,
                CurrentContextDepth,
                Labels: labels
            )
        );
    }

    private void PushForOfControlScope(
        BytecodeBuilder.Label breakTarget,
        BytecodeBuilder.Label continueTarget,
        int iteratorRegister,
        string[]? labels
    )
    {
        controlScopes.Push(
            new ControlScope(
                ControlScopeKind.ForOf,
                breakTarget,
                continueTarget,
                default,
                CurrentContextDepth,
                IteratorRegister: iteratorRegister,
                Labels: labels
            )
        );
    }

    private void PushForAwaitOfControlScope(
        BytecodeBuilder.Label breakTarget,
        BytecodeBuilder.Label continueTarget,
        int iteratorRegister,
        string[]? labels,
        BytecodeBuilder.Label closeTarget,
        int completionKindRegister,
        int completionValueRegister,
        List<FinallyAbruptRoute> abruptRoutes
    )
    {
        controlScopes.Push(
            new ControlScope(
                ControlScopeKind.ForOf,
                breakTarget,
                continueTarget,
                closeTarget,
                CurrentContextDepth,
                completionKindRegister,
                completionValueRegister,
                iteratorRegister,
                IsAsyncIterator: true,
                Labels: labels,
                FinallyRoutes: abruptRoutes
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

    private void EmitAbruptCommand(AbruptCommand command, string? label = null)
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
                var isTarget = label is null || ScopeHasLabel(scope, label);
                if (command == AbruptCommand.Continue && isTarget)
                {
                    EmitJump(scope.Continue);
                    return;
                }
                if (scope.IsAsyncIterator)
                {
                    if (command == AbruptCommand.Return)
                        EmitStar(scope.CompletionValueRegister);
                    var completionKind =
                        command == AbruptCommand.Return
                            ? 1
                            : GetOrAddFinallyAbruptRoute(scope.FinallyRoutes!, command, label);
                    EmitSmi(completionKind);
                    EmitStar(scope.CompletionKindRegister);
                    EmitJump(scope.Finally);
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
                if (command == AbruptCommand.Break && isTarget)
                {
                    EmitJump(scope.Break);
                    return;
                }
                if (returnValueRegister >= 0)
                {
                    EmitLdar(returnValueRegister);
                    builder.ReleaseTemporaryRegister(returnValueRegister);
                }
                continue;
            }
            if (scope.Kind == ControlScopeKind.Destructuring)
            {
                if (command == AbruptCommand.Return)
                {
                    EmitStar(scope.CompletionValueRegister);
                    EmitSmi(1);
                    EmitStar(scope.CompletionKindRegister);
                    EmitJump(scope.Finally);
                    return;
                }
                continue;
            }
            if (scope.Kind == ControlScopeKind.Finally)
            {
                if (command == AbruptCommand.Return)
                    EmitStar(scope.CompletionValueRegister);
                var completionKind =
                    command == AbruptCommand.Return
                        ? 1
                        : GetOrAddFinallyAbruptRoute(scope.FinallyRoutes!, command, label);
                EmitSmi(completionKind);
                EmitStar(scope.CompletionKindRegister);
                EmitJump(scope.Finally);
                return;
            }
            if (
                scope.Kind == ControlScopeKind.Label
                && command == AbruptCommand.Break
                && label is not null
                && ScopeHasLabel(scope, label)
            )
            {
                EmitJump(scope.Break);
                return;
            }
            if (
                scope.Kind == ControlScopeKind.Switch
                && command == AbruptCommand.Break
                && label is null
            )
            {
                EmitJump(scope.Break);
                return;
            }
            if (
                scope.Kind == ControlScopeKind.Iteration
                && command is AbruptCommand.Break or AbruptCommand.Continue
                && (label is null || ScopeHasLabel(scope, label))
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

    private static bool ScopeHasLabel(in ControlScope scope, string label) =>
        scope.Labels is not null && Array.IndexOf(scope.Labels, label) >= 0;

    private static int GetOrAddFinallyAbruptRoute(
        List<FinallyAbruptRoute> routes,
        AbruptCommand command,
        string? label
    )
    {
        for (var i = 0; i < routes.Count; i++)
            if (routes[i].Command == command && routes[i].Label == label)
                return routes[i].CompletionKind;

        var completionKind = routes.Count + 3;
        routes.Add(new(completionKind, command, label));
        return completionKind;
    }

    private void EmitForAwaitIteratorClose(
        int iteratorRegister,
        int completionKindRegister,
        int compareRegister
    )
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var bestEffort = builder.AllocateTemporaryRegister();
            var resultRegister = builder.AllocateTemporaryRegister();
            var normalClose = builder.CreateLabel();
            var resultReady = builder.CreateLabel();
            EmitSmi(2);
            EmitStar(compareRegister);
            EmitLdar(completionKindRegister);
            EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, compareRegister);
            EmitJumpIfToBooleanFalse(normalClose);
            builder.EmitLda(JsOpCode.LdaTrue);
            EmitStar(bestEffort);
            builder.EmitCallRuntime(
                (int)RuntimeId.AsyncIteratorCloseBestEffort,
                iteratorRegister,
                1
            );
            EmitJump(resultReady);
            builder.BindLabel(normalClose);
            builder.EmitLda(JsOpCode.LdaFalse);
            EmitStar(bestEffort);
            builder.EmitCallRuntime((int)RuntimeId.AsyncIteratorClose, iteratorRegister, 1);
            builder.BindLabel(resultReady);
            EmitStar(resultRegister);
            var done = builder.CreateLabel();
            builder.EmitLda(JsOpCode.LdaTheHole);
            EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, resultRegister);
            EmitJumpIfToBooleanTrue(done);
            EmitLdar(resultRegister);
            EmitAwaitSuspension(returnAsNext: true);
            EmitLdar(bestEffort);
            EmitJumpIfToBooleanTrue(done);
            EmitLdar(generatorResumeValueRegister);
            var resultIsObject = builder.CreateLabel();
            builder.EmitJump(JsOpCode.JumpIfJsReceiver, resultIsObject);
            builder.EmitCallRuntime((int)RuntimeId.ThrowIteratorResultNotObject, 0, 0);
            builder.BindLabel(resultIsObject);
            builder.BindLabel(done);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitSwitchStatement(JsAst ast, int nodeIndex, AstNode statement)
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
            var previousTrackingSuppression = PushSuppressKnownInitializedLexicalTracking();
            try
            {
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
                    EnterBreakableContext();
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
                        ExitBreakableContext();
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
                RestoreKnownInitializedLexicalTracking(previousTrackingSuppression);
            }
        }
        finally
        {
            if (rentedLabels is not null)
                ArrayPool<BytecodeBuilder.Label>.Shared.Return(rentedLabels);
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitTryStatement(JsAst ast, AstNode statement)
    {
        if (statement.Arg2 < 0)
        {
            EmitTryCatch(ast, statement.Arg0, statement.Arg1);
            return;
        }
        EmitTryFinally(ast, statement.Arg0, statement.Arg1, statement.Arg2);
    }

    private void EmitTryCatch(JsAst ast, int body, int handler)
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

    private void EmitTryFinally(JsAst ast, int body, int handler, int finalizer)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var abruptRoutes = new List<FinallyAbruptRoute>();
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
            PushFinallyControlScope(finallyFromTry, completionKind, completionValue, abruptRoutes);
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
                PushFinallyControlScope(
                    finallyFromCatch,
                    completionKind,
                    completionValue,
                    abruptRoutes
                );
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
            // Completion semantics for finally, mirroring V8's positional
            // save/restore rewrite inside breakable constructs: save the carried
            // value, reset to undefined, emit the finalizer (its expressions
            // capture normally), then restore the saved value. An abrupt exit
            // (break/continue) jumps past the restore, keeping whatever the
            // finally assigned - exactly matching the spec's UpdateEmpty rules.
            var suppressCompletion = !InBreakableContext;
            var savedCompletionSink = -1;
            var backupRegister = -1;
            if (suppressCompletion)
            {
                savedCompletionSink = TakeCompletionSink();
            }
            else
            {
                backupRegister = builder.AllocatePinnedRegister();
                EmitLdar(completionSinkRegister);
                EmitStar(backupRegister);
                builder.EmitLda(JsOpCode.LdaUndefined);
                CaptureCompletionValue();
            }
            EmitStatement(ast, finalizer);
            if (suppressCompletion)
            {
                RestoreCompletionSink(savedCompletionSink);
            }
            else
            {
                EmitLdar(backupRegister);
                CaptureCompletionValue();
            }

            EmitFinallyCompletionJump(completionKind, compare, 1, out var notReturn);
            EmitLdar(completionValue);
            EmitAbruptCommand(AbruptCommand.Return);
            builder.BindLabel(notReturn);

            EmitFinallyCompletionJump(completionKind, compare, 2, out var notThrow);
            EmitLdar(completionValue);
            builder.Emit(JsOpCode.Throw);
            builder.BindLabel(notThrow);

            for (var i = 0; i < abruptRoutes.Count; i++)
            {
                var route = abruptRoutes[i];
                EmitFinallyCompletionJump(
                    completionKind,
                    compare,
                    route.CompletionKind,
                    out var next
                );
                EmitAbruptCommand(route.Command, route.Label);
                builder.BindLabel(next);
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
        int completionValue,
        List<FinallyAbruptRoute> abruptRoutes
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
                completionValue,
                FinallyRoutes: abruptRoutes
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

    private void EmitCatchClause(JsAst ast, int nodeIndex)
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

    private void EmitBlockStatement(JsAst ast, int nodeIndex)
    {
        ref readonly var block = ref ast[nodeIndex];
        var statements = ast.ChildRange(block.Arg0, block.Arg1);
        if (BlockNeedsExplicitResourceScope(ast, block.Arg0, block.Arg1))
        {
            EmitExplicitResourceScope(
                () => EmitBlockStatementCore(ast, nodeIndex),
                BlockNeedsAsyncExplicitResourceScope(ast, block.Arg0, block.Arg1)
            );
            return;
        }
        EmitBlockStatementCore(ast, nodeIndex);
    }

    private void EmitBlockStatementCore(JsAst ast, int nodeIndex)
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

    private void EmitVariableDeclarationStatement(JsAst ast, int nodeIndex)
    {
        ref readonly var node = ref ast[nodeIndex];
        var kind = (JsVariableDeclarationKind)node.Arg2;
        var offset = node.Arg0;
        var count = node.Arg1;
        if (!kind.IsUsingLike() || HasAmbientExplicitResourceScope)
        {
            EmitVariableDeclaration(ast, offset, count, kind);
            return;
        }

        EmitExplicitResourceScope(
            () => EmitVariableDeclaration(ast, offset, count, kind),
            kind == JsVariableDeclarationKind.AwaitUsing
        );
    }

    private void EmitExplicitResourceScope(
        Action emitBody,
        bool isAsyncScope,
        Action<int>? emitEnter = null
    )
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            var abruptRoutes = new List<FinallyAbruptRoute>();
            var completionKind = builder.AllocateTemporaryRegister();
            var completionValue = builder.AllocateTemporaryRegister();
            var compare = builder.AllocateTemporaryRegister();
            var stackRegister = builder.AllocateTemporaryRegister();
            var catchLabel = builder.CreateLabel();
            var finallyEntry = builder.CreateLabel();

            builder.EmitCallRuntime(
                (int)(
                    isAsyncScope
                        ? RuntimeId.CreateAsyncDisposableResourceStack
                        : RuntimeId.CreateDisposableResourceStack
                ),
                0,
                0
            );
            EmitStar(stackRegister);

            EmitSmi(0);
            EmitStar(completionKind);
            builder.EmitLda(JsOpCode.LdaUndefined);
            EmitStar(completionValue);

            builder.EmitJump(JsOpCode.PushTry, catchLabel);
            PushFinallyControlScope(finallyEntry, completionKind, completionValue, abruptRoutes);
            try
            {
                builder.EmitLda(JsOpCode.LdaUndefined);
                activeExplicitResourceScopes.Push(new(stackRegister, isAsyncScope));
                try
                {
                    emitEnter?.Invoke(stackRegister);
                    emitBody();
                    EmitStar(completionValue);
                }
                finally
                {
                    activeExplicitResourceScopes.Pop();
                }
            }
            finally
            {
                controlScopes.Pop();
            }
            builder.Emit(JsOpCode.PopTry);
            EmitJump(finallyEntry);

            builder.BindLabel(catchLabel);
            EmitStar(completionValue);
            EmitSmi(2);
            EmitStar(completionKind);

            builder.BindLabel(finallyEntry);
            {
                var finalizerArgs = builder.AllocateTemporaryRegisterBlock(3);
                EmitLdar(stackRegister);
                EmitStar(finalizerArgs);
                EmitLdar(completionKind);
                EmitStar(finalizerArgs + 1);
                EmitLdar(completionValue);
                EmitStar(finalizerArgs + 2);
                builder.EmitCallRuntime(
                    (int)(
                        isAsyncScope
                            ? RuntimeId.DisposeAsyncDisposableResourceStack
                            : RuntimeId.DisposeDisposableResourceStack
                    ),
                    finalizerArgs,
                    3
                );
                if (isAsyncScope)
                {
                    var resumeDone = builder.CreateLabel();
                    builder.EmitJump(JsOpCode.JumpIfUndefined, resumeDone);
                    EmitAwaitSuspension();
                    builder.BindLabel(resumeDone);
                }
            }

            EmitFinallyCompletionJump(completionKind, compare, 1, out var notReturn);
            EmitLdar(completionValue);
            EmitAbruptCommand(AbruptCommand.Return);
            builder.BindLabel(notReturn);

            EmitFinallyCompletionJump(completionKind, compare, 2, out var notThrow);
            EmitLdar(completionValue);
            builder.Emit(JsOpCode.Throw);
            builder.BindLabel(notThrow);

            for (var i = 0; i < abruptRoutes.Count; i++)
            {
                var route = abruptRoutes[i];
                EmitFinallyCompletionJump(
                    completionKind,
                    compare,
                    route.CompletionKind,
                    out var next
                );
                EmitAbruptCommand(route.Command, route.Label);
                builder.BindLabel(next);
            }
            EmitLdar(completionValue);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitForOfIterationBodyWithResources(
        JsAst ast,
        int left,
        int valueRegister,
        int body,
        bool isAsyncIteration
    )
    {
        if (!IsUsingLikeDeclarationNode(ast, left))
        {
            EmitStatement(ast, body);
            return;
        }

        var kind = (JsVariableDeclarationKind)ast[left].Arg2;
        EmitExplicitResourceScope(
            () => EmitStatement(ast, body),
            kind == JsVariableDeclarationKind.AwaitUsing || isAsyncIteration,
            _ => EmitAddDisposableResource(kind, valueRegister)
        );
    }

    private void EmitAddDisposableResource(JsVariableDeclarationKind kind, int valueRegister)
    {
        if (activeExplicitResourceScopes.Count == 0)
        {
            if (!hasActiveModuleTopLevelExplicitResourceScope)
                throw new InvalidOperationException(
                    "using declaration requires an active explicit resource scope."
                );
            if (
                kind == JsVariableDeclarationKind.AwaitUsing
                && !moduleTopLevelExplicitResourceScopeIsAsync
            )
                throw new InvalidOperationException(
                    "await using declaration requires an async explicit resource scope."
                );

            var moduleArgs = builder.AllocateTemporaryRegisterBlock(1);
            EmitLdar(valueRegister);
            EmitStar(moduleArgs);
            builder.EmitCallRuntime(
                (int)(
                    kind == JsVariableDeclarationKind.AwaitUsing
                        ? RuntimeId.AddCurrentModuleAsyncDisposableResource
                        : RuntimeId.AddCurrentModuleDisposableResource
                ),
                moduleArgs,
                1
            );
            return;
        }

        var scope = activeExplicitResourceScopes.Peek();
        var args = builder.AllocateTemporaryRegisterBlock(2);
        EmitLdar(scope.StackRegister);
        EmitStar(args);
        EmitLdar(valueRegister);
        EmitStar(args + 1);
        builder.EmitCallRuntime(
            (int)(
                kind == JsVariableDeclarationKind.AwaitUsing
                    ? RuntimeId.AddAsyncDisposableResource
                    : RuntimeId.AddDisposableResource
            ),
            args,
            2
        );
    }

    protected void EmitBodyStatementListWithResources(
        JsAst ast,
        int offset,
        int count,
        Action emitStatements
    )
    {
        if (BlockNeedsExplicitResourceScope(ast, offset, count))
            EmitExplicitResourceScope(
                emitStatements,
                BlockNeedsAsyncExplicitResourceScope(ast, offset, count)
            );
        else
            emitStatements();
    }

    protected void EmitRootStatementList(JsAst ast, int offset, int count)
    {
        var statements = ast.ChildRange(offset, count);
        for (var i = 0; i < statements.Length; i++)
            EmitStatement(ast, statements[i]);
    }

    private bool BlockNeedsExplicitResourceScope(JsAst ast, int offset, int count)
    {
        var statements = ast.ChildRange(offset, count);
        for (var i = 0; i < statements.Length; i++)
            if (IsUsingLikeDeclarationNode(ast, statements[i]))
                return true;
        return false;
    }

    private bool BlockNeedsAsyncExplicitResourceScope(JsAst ast, int offset, int count)
    {
        var statements = ast.ChildRange(offset, count);
        for (var i = 0; i < statements.Length; i++)
        {
            ref readonly var statement = ref ast[statements[i]];
            if (
                statement.Kind == AstKind.VariableDeclaration
                && (JsVariableDeclarationKind)statement.Arg2 == JsVariableDeclarationKind.AwaitUsing
            )
                return true;
        }
        return false;
    }

    private static bool IsUsingLikeDeclarationNode(JsAst ast, int nodeIndex)
    {
        ref readonly var statement = ref ast[nodeIndex];
        return statement.Kind == AstKind.VariableDeclaration
            && ((JsVariableDeclarationKind)statement.Arg2).IsUsingLike();
    }

    private void EmitIfStatement(JsAst ast, int test, int consequent, int alternate)
    {
        var elseLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        var incoming = CaptureKnownInitializedLexicals();
        EmitExpressionForTest(ast, test, elseLabel, jumpIfTrue: false);
        EmitStatement(ast, consequent);
        var consequentState = CaptureKnownInitializedLexicals();
        RestoreKnownInitializedLexicals(incoming);
        if (alternate >= 0)
        {
            EmitJump(endLabel);
            builder.BindLabel(elseLabel);
            EmitStatement(ast, alternate);
            var alternateState = CaptureKnownInitializedLexicals();
            MergeKnownInitializedLexicals(consequentState, alternateState);
            builder.BindLabel(endLabel);
        }
        else
        {
            builder.BindLabel(elseLabel);
            RestoreKnownInitializedLexicals(incoming);
        }
    }

    protected void EmitDeclarationPrologue(JsAst ast, int bodyRoot)
    {
        ref readonly var body = ref ast[bodyRoot];
        EmitDeclarationPrologue(ast, body.Arg0, body.Arg1);
    }

    private void EmitDeclarationPrologue(JsAst ast, int offset, int count)
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
            else if (statement.Kind == AstKind.ExportDeclaration && statement.Arg0 >= 0)
                EmitExportDeclarationPrologue(ast, statement);
        }
    }

    private void EmitExportDeclarationPrologue(JsAst ast, AstNode export)
    {
        ref readonly var value = ref ast[export.Arg0];
        if (value.Kind == AstKind.FunctionDeclaration)
        {
            EmitFunctionDeclaration(ast, value.Arg0, value.Arg1);
            return;
        }
        if (value.Kind != AstKind.FunctionExpression)
            return;

        var entries = ast.GetExportEntries(export);
        if (entries.Length == 0 || entries[0].Kind != JsExportKind.DefaultDeclaration)
            return;
        var localName = ast.GetString(entries[0].LocalNameStringIndex);
        if (!TryResolveBinding(localName, out var binding))
            throw new InvalidOperationException(
                $"No planned binding found for default export '{localName}'."
            );
        if (
            EmitFunctionExpression(
                ast,
                value.Arg0,
                value.Arg1,
                inferredName: "default",
                deferredBinding: binding,
                suppressSelfBinding: true
            )
        )
            return;
        EmitStore(binding, isInitialization: true, isFunctionDeclaration: true);
    }

    private void EmitVariableDeclaration(
        JsAst ast,
        int offset,
        int count,
        JsVariableDeclarationKind declarationKind
    )
    {
        var addResource =
            declarationKind
            is JsVariableDeclarationKind.Using
                or JsVariableDeclarationKind.AwaitUsing;
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

            // A global `var x = v` body statement is an assignment against the
            // binding the declaration prologue already created: StaGlobalInit would
            // be a no-op for undefined values on existing globals.
            var isInitStore =
                declarationKind != JsVariableDeclarationKind.Var
                || binding.Planned.StorageKind != CompilerPlannedStorageKind.GlobalBinding;

            if (
                !addResource
                && isInitStore
                && declarator.Arg2 >= 0
                && TryGetDirectLocalRegister(ast, declarator.Arg2, out var sourceRegister)
                && binding.Planned.StorageKind
                    is CompilerPlannedStorageKind.LocalRegister
                        or CompilerPlannedStorageKind.LexicalRegister
                && binding.Register >= 0
            )
            {
                EmitMove(sourceRegister, binding.Register);
                if (binding.Planned.StorageKind == CompilerPlannedStorageKind.LexicalRegister)
                    MarkKnownInitializedLexical(binding);
                continue;
            }

            if (
                !addResource
                && binding.Planned.StorageKind
                    is CompilerPlannedStorageKind.LocalRegister
                        or CompilerPlannedStorageKind.LexicalRegister
                && binding.Register >= 0
                && declarator.Arg2 >= 0
                && ast[declarator.Arg2].Kind is AstKind.ArrayExpression or AstKind.ObjectExpression
                && CanInitializeWithoutUserCode(ast, declarator.Arg2)
                && !ExpressionReferencesIdentifier(ast, declarator.Arg2, name)
            )
            {
                if (ast[declarator.Arg2].Kind == AstKind.ArrayExpression)
                    EmitArrayExpression(ast, ast[declarator.Arg2], binding.Register);
                else
                    EmitObjectExpression(ast, ast[declarator.Arg2], binding.Register);
                if (isInitStore)
                    MarkKnownInitializedLexical(binding);
                continue;
            }

            int valueRegister = -1;
            var resourceMarker = builder.GetTemporaryRegisterScopeMarker();
            try
            {
                if (declarator.Arg2 >= 0)
                    EmitExpressionWithInferredName(ast, declarator.Arg2, name);
                else
                    builder.EmitLda(JsOpCode.LdaUndefined);

                if (addResource)
                {
                    valueRegister = builder.AllocateTemporaryRegister();
                    EmitStar(valueRegister);
                    EmitLdar(valueRegister);
                }

                EmitStore(binding, isInitialization: isInitStore);
                if (valueRegister >= 0)
                    EmitAddDisposableResource(declarationKind, valueRegister);
            }
            finally
            {
                builder.ReleaseTemporaryRegistersToMarker(resourceMarker);
            }
        }
    }

    private void EmitStoreBindingTarget(JsAst ast, int targetIndex)
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
                EmitInitializeParameterStore(binding, markKnownInitialized: true);
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
        JsAst ast,
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
            var inStepRegister = builder.AllocateTemporaryRegister();
            builder.EmitLda(JsOpCode.LdaFalse);
            EmitStar(inStepRegister);
            var kindRegister = builder.AllocateTemporaryRegister();
            builder.EmitLda(JsOpCode.LdaZero);
            EmitStar(kindRegister);
            var catchLabel = builder.CreateLabel();
            var abruptExitLabel = builder.CreateLabel();
            var closeEntryLabel = builder.CreateLabel();
            var endLabel = builder.CreateLabel();

            builder.EmitJump(JsOpCode.PushTry, catchLabel);
            controlScopes.Push(
                new ControlScope(
                    ControlScopeKind.Destructuring,
                    default,
                    default,
                    abruptExitLabel,
                    CurrentContextDepth,
                    kindRegister,
                    valueRegister
                )
            );
            var elements = ast.ChildRange(pattern.Arg0, pattern.Arg1);
            for (var i = 0; i < elements.Length; i++)
            {
                if (elements[i] < 0)
                {
                    EmitArrayBindingElision(
                        iteratorRegister,
                        doneRegister,
                        valueRegister,
                        inStepRegister
                    );
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
                            preparedRestTarget,
                            inStepRegister
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
                    EmitArrayBindingStep(
                        iteratorRegister,
                        doneRegister,
                        valueRegister,
                        inStepRegister
                    );
                    if (defaultIndex >= 0)
                        EmitBindingDefault(ast, targetIndex, defaultIndex, valueRegister);
                    EmitStoreDestructuringTarget(ast, targetIndex, assignment, preparedTarget);
                }
                finally
                {
                    builder.ReleaseTemporaryRegistersToMarker(targetMarker);
                }
            }
            controlScopes.Pop();
            builder.Emit(JsOpCode.PopTry);
            EmitJump(closeEntryLabel);

            builder.BindLabel(abruptExitLabel);
            builder.Emit(JsOpCode.PopTry);
            EmitJump(closeEntryLabel);

            builder.BindLabel(catchLabel);
            EmitStar(valueRegister);
            EmitSmi(2);
            EmitStar(kindRegister);
            EmitJump(closeEntryLabel);

            builder.BindLabel(closeEntryLabel);
            var throwCloseLabel = builder.CreateLabel();
            var dispatchLabel = builder.CreateLabel();
            var returnDispatchLabel = builder.CreateLabel();
            var throwDispatchLabel = builder.CreateLabel();
            var rethrowLabel = builder.CreateLabel();
            EmitSmi(2);
            EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, kindRegister);
            EmitJumpIfToBooleanTrue(throwCloseLabel);
            EmitCloseArrayBindingIterator(iteratorRegister, doneRegister);
            EmitJump(dispatchLabel);

            builder.BindLabel(throwCloseLabel);
            EmitLdar(doneRegister);
            EmitJumpIfToBooleanTrue(rethrowLabel);
            EmitLdar(inStepRegister);
            EmitJumpIfToBooleanTrue(rethrowLabel);
            builder.EmitCallRuntime(
                (int)RuntimeId.DestructureIteratorCloseBestEffort,
                iteratorRegister,
                1
            );
            builder.BindLabel(rethrowLabel);

            builder.BindLabel(dispatchLabel);
            EmitSmi(1);
            EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, kindRegister);
            EmitJumpIfToBooleanTrue(returnDispatchLabel);
            EmitSmi(2);
            EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, kindRegister);
            EmitJumpIfToBooleanTrue(throwDispatchLabel);
            EmitJump(endLabel);

            builder.BindLabel(returnDispatchLabel);
            EmitLdar(valueRegister);
            EmitAbruptCommand(AbruptCommand.Return);

            builder.BindLabel(throwDispatchLabel);
            EmitLdar(valueRegister);
            builder.Emit(JsOpCode.Throw);
            builder.BindLabel(endLabel);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitArrayBindingStep(
        int iteratorRegister,
        int doneRegister,
        int valueRegister,
        int inStepRegister
    )
    {
        var doneLabel = builder.CreateLabel();
        var hasValueLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        EmitLdar(doneRegister);
        EmitJumpIfToBooleanTrue(doneLabel);
        builder.EmitLda(JsOpCode.LdaTrue);
        EmitStar(inStepRegister);
        builder.EmitCallRuntime((int)RuntimeId.DestructureIteratorStepValue, iteratorRegister, 1);
        EmitStar(valueRegister);
        builder.EmitLda(JsOpCode.LdaFalse);
        EmitStar(inStepRegister);
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

    private void EmitArrayBindingElision(
        int iteratorRegister,
        int doneRegister,
        int valueRegister,
        int inStepRegister
    )
    {
        var endLabel = builder.CreateLabel();
        EmitLdar(doneRegister);
        EmitJumpIfToBooleanTrue(endLabel);
        builder.EmitLda(JsOpCode.LdaTrue);
        EmitStar(inStepRegister);
        builder.EmitCallRuntime((int)RuntimeId.DestructureIteratorStepValue, iteratorRegister, 1);
        EmitStar(valueRegister);
        builder.EmitLda(JsOpCode.LdaFalse);
        EmitStar(inStepRegister);
        builder.EmitLda(JsOpCode.LdaTheHole);
        EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, valueRegister);
        EmitJumpIfToBooleanFalse(endLabel);
        builder.EmitLda(JsOpCode.LdaTrue);
        EmitStar(doneRegister);
        builder.BindLabel(endLabel);
    }

    private void EmitArrayBindingRest(
        JsAst ast,
        int targetIndex,
        int iteratorRegister,
        int doneRegister,
        int valueRegister,
        bool assignment,
        PreparedMemberReference? preparedTarget,
        int inStepRegister
    )
    {
        var emptyLabel = builder.CreateLabel();
        var storeLabel = builder.CreateLabel();
        EmitLdar(doneRegister);
        EmitJumpIfToBooleanTrue(emptyLabel);
        builder.EmitLda(JsOpCode.LdaTrue);
        EmitStar(inStepRegister);
        builder.EmitCallRuntime((int)RuntimeId.DestructureIteratorRestArray, iteratorRegister, 1);
        EmitStar(valueRegister);
        builder.EmitLda(JsOpCode.LdaFalse);
        EmitStar(inStepRegister);
        builder.EmitLda(JsOpCode.LdaTrue);
        EmitStar(doneRegister);
        EmitLdar(valueRegister);
        EmitJump(storeLabel);
        builder.BindLabel(emptyLabel);
        builder.Emit(JsOpCode.CreateEmptyArrayLiteral);
        builder.BindLabel(storeLabel);
        EmitStoreDestructuringTarget(ast, targetIndex, assignment, preparedTarget);
    }

    private void EmitBindingDefault(JsAst ast, int targetIndex, int defaultIndex, int valueRegister)
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
        builder.EmitCallRuntime((int)RuntimeId.DestructureIteratorClose, iteratorRegister, 1);
        builder.BindLabel(endLabel);
    }

    private void EmitObjectBindingPattern(
        JsAst ast,
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
        JsAst ast,
        int targetIndex,
        int sourceRegister,
        ReadOnlySpan<JsObjectProperty> excludedProperties,
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
        JsAst ast,
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

    private void EmitFunctionDeclaration(JsAst ast, int functionIndex, int bodyRoot)
    {
        var function = ast.GetFunction(functionIndex);
        var name = ast.GetString(function.NameStringIndex);
        if (!TryResolveBinding(name, out var binding))
            throw new InvalidOperationException($"No planned binding found for function '{name}'.");

        var functionCompiler = new JsFunctionCompiler(
            Vm,
            BuildChildCaptureBindings(),
            visiblePrivateBindings
        );
        var functionObject = functionCompiler.CompileFunction(ast, function, bodyRoot);
        if (DeferHoistedFunction(binding, functionObject))
            return;
        var idx = builder.AddObjectConstant(functionObject);
        EmitCreateClosureByIndex(idx);
        EmitPrivateBrandMappingsForClosure();
        EmitStore(binding, isInitialization: true, isFunctionDeclaration: true);
    }

    protected virtual bool DeferHoistedFunction(
        in BindingStorage binding,
        JsBytecodeFunction function
    ) => false;
}
