using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Parsing;

namespace Okojo.Compiler.Experimental;

internal sealed partial class JsPlannedScriptCompiler
{
    private void EmitStatement(JsStatement statement)
    {
        switch (statement)
        {
            case JsVariableDeclarationStatement declaration:
                EmitVariableDeclaration(declaration);
                return;
            case JsFunctionDeclaration functionDeclaration:
                EmitFunctionDeclaration(functionDeclaration);
                return;
            case JsBlockStatement block:
                EmitBlockStatement(block);
                return;
            case JsIfStatement ifStatement:
                EmitIfStatement(ifStatement);
                return;
            case JsExpressionStatement expressionStatement:
                EmitExpression(expressionStatement.Expression);
                return;
            case JsEmptyStatement:
                builder.EmitLda(JsOpCode.LdaUndefined);
                return;
            default:
                throw new NotSupportedException($"JsPlannedScriptCompiler does not support statement '{statement.GetType().Name}'.");
        }
    }

    private void EmitBlockStatement(JsBlockStatement block)
    {
        var childScope = FindChildScope(activeScopes.Peek().ScopeId, CompilerCollectedScopeKind.Block, block.Position);
        EnterScope(childScope.ScopeId);
        try
        {
            for (var i = 0; i < block.Statements.Count; i++)
                EmitStatement(block.Statements[i]);
        }
        finally
        {
            LeaveScope();
        }
    }

    private void EmitIfStatement(JsIfStatement ifStatement)
    {
        EmitExpression(ifStatement.Test);
        var elseLabel = builder.CreateLabel();
        var endLabel = builder.CreateLabel();
        EmitJumpIfToBooleanFalse(elseLabel);
        EmitStatement(ifStatement.Consequent);
        if (ifStatement.Alternate is not null)
        {
            EmitJump(endLabel);
            builder.BindLabel(elseLabel);
            EmitStatement(ifStatement.Alternate);
            builder.BindLabel(endLabel);
        }
        else
        {
            builder.BindLabel(elseLabel);
        }
    }

    private void EmitVariableDeclaration(JsVariableDeclarationStatement declaration)
    {
        if (declaration.BindingPattern is not null)
            throw new NotSupportedException("Binding patterns are not supported by JsPlannedScriptCompiler.");

        for (var i = 0; i < declaration.Declarators.Count; i++)
        {
            var declarator = declaration.Declarators[i];
            if (!TryResolveBinding(declarator.Name, out var binding))
                throw new InvalidOperationException($"No planned binding found for '{declarator.Name}'.");

            if (declarator.Initializer is not null)
                EmitExpression(declarator.Initializer);
            else
                builder.EmitLda(JsOpCode.LdaUndefined);

            EmitStore(binding);
        }
    }

    private void EmitFunctionDeclaration(JsFunctionDeclaration functionDeclaration)
    {
        if (!TryResolveBinding(functionDeclaration.Name, out var binding))
            throw new InvalidOperationException($"No planned binding found for function '{functionDeclaration.Name}'.");

        var functionCompiler = new JsPlannedFunctionCompiler(Vm, BuildChildCaptureBindings());
        var functionObject = functionCompiler.CompileFunction(
            functionDeclaration.Name,
            FunctionParameterPlan.FromFunction(functionDeclaration),
            functionDeclaration.Body);
        var idx = builder.AddObjectConstant(functionObject);
        EmitCreateClosureByIndex(idx);
        EmitStore(binding);
    }
}
