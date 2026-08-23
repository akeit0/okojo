using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Compiler;
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

        try
        {
            if (init >= 0)
            {
                if (ast[init].Kind == AstKind.VariableDeclaration)
                    EmitStatement(ast, init);
                else
                    EmitExpression(ast, init);
            }

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
