using Okojo.Parsing;

namespace Okojo.Runtime;

internal static class EvalEarlyErrors
{
    public static void ThrowIfInvalidIndirectEvalScript(JsProgram program)
    {
        if (TryFindInvalidNewTarget(program.Statements, false, out var position))
            throw new JsRuntimeException(JsErrorKind.SyntaxError,
                $"new.target is not allowed in indirect eval script at position {position}.",
                "EVAL_NEW_TARGET");
    }

    private static bool TryFindInvalidNewTarget(
        IReadOnlyList<JsStatement> statements,
        bool allowNewTarget,
        out int position)
    {
        for (var i = 0; i < statements.Count; i++)
            if (TryFindInvalidNewTarget(statements[i], allowNewTarget, out position))
                return true;

        position = -1;
        return false;
    }

    private static bool TryFindInvalidNewTarget(JsStatement statement, bool allowNewTarget, out int position)
    {
        switch (statement)
        {
            case JsExpressionStatement expressionStatement:
                return TryFindInvalidNewTarget(expressionStatement.Expression, allowNewTarget, out position);
            case JsBlockStatement blockStatement:
                return TryFindInvalidNewTarget(blockStatement.Statements, allowNewTarget, out position);
            case JsIfStatement ifStatement:
                if (TryFindInvalidNewTarget(ifStatement.Test, allowNewTarget, out position) ||
                    TryFindInvalidNewTarget(ifStatement.Consequent, allowNewTarget, out position))
                    return true;
                if (ifStatement.Alternate is not null)
                    return TryFindInvalidNewTarget(ifStatement.Alternate, allowNewTarget, out position);
                break;
            case JsWhileStatement whileStatement:
                if (TryFindInvalidNewTarget(whileStatement.Test, allowNewTarget, out position))
                    return true;
                return TryFindInvalidNewTarget(whileStatement.Body, allowNewTarget, out position);
            case JsForStatement forStatement:
                if (forStatement.Init is JsExpression initExpression &&
                    TryFindInvalidNewTarget(initExpression, allowNewTarget, out position))
                    return true;
                if (forStatement.Init is JsVariableDeclarationStatement initDeclaration)
                    for (var i = 0; i < initDeclaration.Declarators.Count; i++)
                    {
                        var initializer = initDeclaration.Declarators[i].Initializer;
                        if (initializer is not null &&
                            TryFindInvalidNewTarget(initializer, allowNewTarget, out position))
                            return true;
                    }

                if (forStatement.Test is not null &&
                    TryFindInvalidNewTarget(forStatement.Test, allowNewTarget, out position))
                    return true;
                if (forStatement.Update is not null &&
                    TryFindInvalidNewTarget(forStatement.Update, allowNewTarget, out position))
                    return true;
                return TryFindInvalidNewTarget(forStatement.Body, allowNewTarget, out position);
            case JsForInOfStatement forInOfStatement:
                if (forInOfStatement.Left is JsExpression leftExpression &&
                    TryFindInvalidNewTarget(leftExpression, allowNewTarget, out position))
                    return true;
                if (forInOfStatement.Left is JsVariableDeclarationStatement leftDeclaration)
                    for (var i = 0; i < leftDeclaration.Declarators.Count; i++)
                    {
                        var initializer = leftDeclaration.Declarators[i].Initializer;
                        if (initializer is not null &&
                            TryFindInvalidNewTarget(initializer, allowNewTarget, out position))
                            return true;
                    }

                if (TryFindInvalidNewTarget(forInOfStatement.Right, allowNewTarget, out position))
                    return true;
                return TryFindInvalidNewTarget(forInOfStatement.Body, allowNewTarget, out position);
            case JsReturnStatement returnStatement when returnStatement.Argument is not null:
                return TryFindInvalidNewTarget(returnStatement.Argument, allowNewTarget, out position);
            case JsThrowStatement throwStatement:
                return TryFindInvalidNewTarget(throwStatement.Argument, allowNewTarget, out position);
            case JsVariableDeclarationStatement variableDeclaration:
                if (variableDeclaration.BindingInitializer is not null &&
                    TryFindInvalidNewTarget(variableDeclaration.BindingInitializer, allowNewTarget, out position))
                    return true;

                for (var i = 0; i < variableDeclaration.Declarators.Count; i++)
                {
                    var initializer = variableDeclaration.Declarators[i].Initializer;
                    if (initializer is not null && TryFindInvalidNewTarget(initializer, allowNewTarget, out position))
                        return true;
                }

                break;
            case JsEmptyObjectBindingDeclarationStatement emptyObjectBinding:
                return TryFindInvalidNewTarget(emptyObjectBinding.Initializer, allowNewTarget, out position);
            case JsFunctionDeclaration functionDeclaration:
                return TryFindInvalidNewTargetInFunction(functionDeclaration.ParameterInitializers,
                    functionDeclaration.Body, false, allowNewTarget, out position);
            case JsClassDeclaration classDeclaration:
                return TryFindInvalidNewTarget(classDeclaration.ClassExpression, allowNewTarget, out position);
            case JsTryStatement tryStatement:
                if (TryFindInvalidNewTarget(tryStatement.Block, allowNewTarget, out position))
                    return true;
                if (tryStatement.Handler?.BindingPattern is not null &&
                    TryFindInvalidNewTarget(tryStatement.Handler.BindingPattern, allowNewTarget, out position))
                    return true;
                if (tryStatement.Handler is not null &&
                    TryFindInvalidNewTarget(tryStatement.Handler.Body, allowNewTarget, out position))
                    return true;
                if (tryStatement.Finalizer is not null)
                    return TryFindInvalidNewTarget(tryStatement.Finalizer, allowNewTarget, out position);
                break;
            case JsSwitchStatement switchStatement:
                if (TryFindInvalidNewTarget(switchStatement.Discriminant, allowNewTarget, out position))
                    return true;
                for (var i = 0; i < switchStatement.Cases.Count; i++)
                {
                    var switchCase = switchStatement.Cases[i];
                    if (switchCase.Test is not null &&
                        TryFindInvalidNewTarget(switchCase.Test, allowNewTarget, out position))
                        return true;
                    if (TryFindInvalidNewTarget(switchCase.Consequent, allowNewTarget, out position))
                        return true;
                }

                break;
        }

        position = -1;
        return false;
    }

    private static bool TryFindInvalidNewTarget(
        IReadOnlyList<JsExpression?> expressions,
        bool allowNewTarget,
        out int position)
    {
        for (var i = 0; i < expressions.Count; i++)
        {
            var expression = expressions[i];
            if (expression is not null && TryFindInvalidNewTarget(expression, allowNewTarget, out position))
                return true;
        }

        position = -1;
        return false;
    }

    private static bool TryFindInvalidNewTarget(JsExpression expression, bool allowNewTarget, out int position)
    {
        switch (expression)
        {
            case JsNewTargetExpression newTargetExpression:
                if (!allowNewTarget)
                {
                    position = newTargetExpression.Position;
                    return true;
                }

                break;
            case JsFunctionExpression functionExpression:
                return TryFindInvalidNewTargetInFunction(
                    functionExpression.ParameterInitializers,
                    functionExpression.Body,
                    functionExpression.IsArrow,
                    allowNewTarget,
                    out position);
            case JsAssignmentExpression assignmentExpression:
                if (TryFindInvalidNewTarget(assignmentExpression.Left, allowNewTarget, out position))
                    return true;
                return TryFindInvalidNewTarget(assignmentExpression.Right, allowNewTarget, out position);
            case JsBinaryExpression binaryExpression:
                if (TryFindInvalidNewTarget(binaryExpression.Left, allowNewTarget, out position))
                    return true;
                return TryFindInvalidNewTarget(binaryExpression.Right, allowNewTarget, out position);
            case JsConditionalExpression conditionalExpression:
                if (TryFindInvalidNewTarget(conditionalExpression.Test, allowNewTarget, out position) ||
                    TryFindInvalidNewTarget(conditionalExpression.Consequent, allowNewTarget, out position))
                    return true;
                return TryFindInvalidNewTarget(conditionalExpression.Alternate, allowNewTarget, out position);
            case JsCallExpression callExpression:
                if (TryFindInvalidNewTarget(callExpression.Callee, allowNewTarget, out position))
                    return true;
                return TryFindInvalidNewTarget(callExpression.Arguments, allowNewTarget, out position);
            case JsNewExpression newExpression:
                if (TryFindInvalidNewTarget(newExpression.Callee, allowNewTarget, out position))
                    return true;
                return TryFindInvalidNewTarget(newExpression.Arguments, allowNewTarget, out position);
            case JsUpdateExpression updateExpression:
                return TryFindInvalidNewTarget(updateExpression.Argument, allowNewTarget, out position);
            case JsUnaryExpression unaryExpression:
                return TryFindInvalidNewTarget(unaryExpression.Argument, allowNewTarget, out position);
            case JsYieldExpression yieldExpression when yieldExpression.Argument is not null:
                return TryFindInvalidNewTarget(yieldExpression.Argument, allowNewTarget, out position);
            case JsAwaitExpression awaitExpression:
                return TryFindInvalidNewTarget(awaitExpression.Argument, allowNewTarget, out position);
            case JsSpreadExpression spreadExpression:
                return TryFindInvalidNewTarget(spreadExpression.Argument, allowNewTarget, out position);
            case JsClassExpression classExpression:
                return TryFindInvalidNewTarget(classExpression, allowNewTarget, out position);
            case JsMemberExpression memberExpression:
                if (TryFindInvalidNewTarget(memberExpression.Object, allowNewTarget, out position))
                    return true;
                if (memberExpression.IsComputed)
                    return TryFindInvalidNewTarget(memberExpression.Property, allowNewTarget, out position);
                break;
            case JsObjectExpression objectExpression:
                for (var i = 0; i < objectExpression.Properties.Count; i++)
                {
                    var property = objectExpression.Properties[i];
                    if (property.IsComputed && property.ComputedKey is not null &&
                        TryFindInvalidNewTarget(property.ComputedKey, allowNewTarget, out position))
                        return true;
                    if (TryFindInvalidNewTarget(property.Value, allowNewTarget, out position))
                        return true;
                }

                break;
            case JsArrayExpression arrayExpression:
                return TryFindInvalidNewTarget(arrayExpression.Elements, allowNewTarget, out position);
            case JsTemplateExpression templateExpression:
                return TryFindInvalidNewTarget(templateExpression.Expressions, allowNewTarget, out position);
            case JsTaggedTemplateExpression taggedTemplateExpression:
                if (TryFindInvalidNewTarget(taggedTemplateExpression.Tag, allowNewTarget, out position))
                    return true;
                return TryFindInvalidNewTarget(taggedTemplateExpression.Template.Expressions, allowNewTarget,
                    out position);
            case JsSequenceExpression sequenceExpression:
                return TryFindInvalidNewTarget(sequenceExpression.Expressions, allowNewTarget, out position);
            case JsImportCallExpression importCallExpression:
                if (TryFindInvalidNewTarget(importCallExpression.Argument, allowNewTarget, out position))
                    return true;
                if (importCallExpression.Options is not null)
                    return TryFindInvalidNewTarget(importCallExpression.Options, allowNewTarget, out position);
                break;
        }

        position = -1;
        return false;
    }

    private static bool TryFindInvalidNewTarget(
        JsClassExpression classExpression,
        bool allowNewTarget,
        out int position)
    {
        if (classExpression.ExtendsExpression is not null &&
            TryFindInvalidNewTarget(classExpression.ExtendsExpression, allowNewTarget, out position))
            return true;

        for (var i = 0; i < classExpression.Elements.Count; i++)
        {
            var element = classExpression.Elements[i];
            if (element.IsComputedKey && element.ComputedKey is not null &&
                TryFindInvalidNewTarget(element.ComputedKey, allowNewTarget, out position))
                return true;

            if (element.FieldInitializer is not null &&
                TryFindInvalidNewTarget(element.FieldInitializer, true, out position))
                return true;

            if (element.StaticBlock is not null &&
                TryFindInvalidNewTarget(element.StaticBlock.Statements, allowNewTarget, out position))
                return true;

            if (element.Value is not null &&
                TryFindInvalidNewTargetInFunction(
                    element.Value.ParameterInitializers,
                    element.Value.Body,
                    element.Value.IsArrow,
                    allowNewTarget,
                    out position))
                return true;
        }

        position = -1;
        return false;
    }

    private static bool TryFindInvalidNewTargetInFunction(
        IReadOnlyList<JsExpression?> parameterInitializers,
        JsBlockStatement body,
        bool isArrow,
        bool inheritedAllowNewTarget,
        out int position)
    {
        var allowNewTarget = isArrow ? inheritedAllowNewTarget : true;
        if (TryFindInvalidNewTarget(parameterInitializers, allowNewTarget, out position))
            return true;
        return TryFindInvalidNewTarget(body.Statements, allowNewTarget, out position);
    }
}
