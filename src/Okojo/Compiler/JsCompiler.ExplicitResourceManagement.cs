using Okojo.Bytecode;
using Okojo.Parsing;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private readonly record struct ExplicitResourceScope(int StackRegister, bool IsAsync);

    private readonly Stack<ExplicitResourceScope> activeExplicitResourceScopes = new();

    private bool HasActiveExplicitResourceScope => activeExplicitResourceScopes.Count != 0;

    private void EmitExplicitResourceScope(Action emitBody, bool isAsyncScope, Action<int>? emitEnter = null)
    {
        var stackReg = AllocateSyntheticLocal($"$erm.stack.{finallyTempUniqueId}");
        EmitCallRuntime(
            isAsyncScope
                ? RuntimeId.CreateAsyncDisposableResourceStack
                : RuntimeId.CreateDisposableResourceStack,
            0,
            0);
        EmitStarRegister(stackReg);

        EmitFinallyFlowScope(
            () =>
            {
                activeExplicitResourceScopes.Push(new(stackReg, isAsyncScope));
                try
                {
                    emitEnter?.Invoke(stackReg);
                    emitBody();
                }
                finally
                {
                    activeExplicitResourceScopes.Pop();
                }
            },
            (completionKindReg, completionValueReg) =>
            {
                var finalizerArgStart = AllocateTemporaryRegisterBlock(3);
                EmitLdaRegister(stackReg);
                EmitStarRegister(finalizerArgStart);
                EmitLdaRegister(completionKindReg);
                EmitStarRegister(finalizerArgStart + 1);
                EmitLdaRegister(completionValueReg);
                EmitStarRegister(finalizerArgStart + 2);
                EmitCallRuntime(
                    isAsyncScope
                        ? RuntimeId.DisposeAsyncDisposableResourceStack
                        : RuntimeId.DisposeDisposableResourceStack,
                    finalizerArgStart,
                    3);
                if (isAsyncScope)
                {
                    var doneLabel = builder.CreateLabel();
                    builder.EmitJump(JsOpCode.JumpIfUndefined, doneLabel);
                    EmitGeneratorSuspendResume(minimizeLiveRange: true, isAwaitSuspend: true);
                    builder.BindLabel(doneLabel);
                }
            });
    }

    private bool BlockNeedsExplicitResourceScope(IReadOnlyList<JsStatement> statements)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            switch (statements[i])
            {
                case JsVariableDeclarationStatement decl when decl.Kind.IsUsingLike():
                case JsEmptyObjectBindingDeclarationStatement emptyDecl when emptyDecl.Kind.IsUsingLike():
                    return true;
            }
        }

        return false;
    }

    private bool BlockNeedsAsyncExplicitResourceScope(IReadOnlyList<JsStatement> statements)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            switch (statements[i])
            {
                case JsVariableDeclarationStatement { Kind: JsVariableDeclarationKind.AwaitUsing }:
                case JsEmptyObjectBindingDeclarationStatement { Kind: JsVariableDeclarationKind.AwaitUsing }:
                    return true;
            }
        }

        return false;
    }

    private void EmitRegisterExplicitResource(JsVariableDeclarationKind kind, int valueRegister)
    {
        if (activeExplicitResourceScopes.Count == 0)
            throw new InvalidOperationException("using declaration requires an active explicit resource scope.");

        var scope = activeExplicitResourceScopes.Peek();
        EmitLdaRegister(scope.StackRegister);
        var argStart = AllocateTemporaryRegisterBlock(2);
        EmitStarRegister(argStart);
        EmitLdaRegister(valueRegister);
        EmitStarRegister(argStart + 1);
        EmitCallRuntime(
            kind == JsVariableDeclarationKind.AwaitUsing
                ? RuntimeId.AddAsyncDisposableResource
                : RuntimeId.AddDisposableResource,
            argStart,
            2);
    }

    private static bool TryGetUsingLikeForInOfLeft(JsForInOfStatement statement,
        out JsVariableDeclarationStatement declaration)
    {
        if (statement.Left is JsVariableDeclarationStatement leftDecl && leftDecl.Kind.IsUsingLike())
        {
            declaration = leftDecl;
            return true;
        }

        declaration = null!;
        return false;
    }
}
