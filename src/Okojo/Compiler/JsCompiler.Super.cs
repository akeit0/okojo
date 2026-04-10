using Okojo.Parsing;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private static bool FunctionUsesSuper(IReadOnlyList<JsExpression?> parameterInitializers, JsBlockStatement body)
    {
        for (var i = 0; i < parameterInitializers.Count; i++)
        {
            var init = parameterInitializers[i];
            if (init is not null && ExpressionContainsSuperInCurrentFunction(init))
                return true;
        }

        foreach (var statement in body.Statements)
            if (StatementContainsSuperInCurrentFunction(statement))
                return true;

        return false;
    }

    private static bool FunctionUsesSuperInNestedArrows(IReadOnlyList<JsExpression?> parameterInitializers,
        JsBlockStatement body)
    {
        for (var i = 0; i < parameterInitializers.Count; i++)
        {
            var init = parameterInitializers[i];
            if (init is not null && ExpressionContainsNestedArrowWithSuper(init))
                return true;
        }

        foreach (var statement in body.Statements)
            if (StatementContainsNestedArrowWithSuper(statement))
                return true;

        return false;
    }

    private static bool StatementContainsNestedArrowWithSuper(JsStatement statement)
    {
        switch (statement)
        {
            case JsExpressionStatement expr:
                return ExpressionContainsNestedArrowWithSuper(expr.Expression);
            case JsBlockStatement block:
                return block.Statements.Any(StatementContainsNestedArrowWithSuper);
            case JsIfStatement ifStmt:
                return ExpressionContainsNestedArrowWithSuper(ifStmt.Test) ||
                       StatementContainsNestedArrowWithSuper(ifStmt.Consequent) ||
                       (ifStmt.Alternate is not null && StatementContainsNestedArrowWithSuper(ifStmt.Alternate));
            case JsWhileStatement whileStmt:
                return ExpressionContainsNestedArrowWithSuper(whileStmt.Test) ||
                       StatementContainsNestedArrowWithSuper(whileStmt.Body);
            case JsForStatement forStmt:
                return (forStmt.Init is JsExpression initExpr && ExpressionContainsNestedArrowWithSuper(initExpr)) ||
                       (forStmt.Init is JsVariableDeclarationStatement initDecl &&
                        initDecl.Declarators.Any(d =>
                            d.Initializer is not null && ExpressionContainsNestedArrowWithSuper(d.Initializer))) ||
                       (forStmt.Test is not null && ExpressionContainsNestedArrowWithSuper(forStmt.Test)) ||
                       (forStmt.Update is not null && ExpressionContainsNestedArrowWithSuper(forStmt.Update)) ||
                       StatementContainsNestedArrowWithSuper(forStmt.Body);
            case JsForInOfStatement forInOfStmt:
                return (forInOfStmt.Left is JsExpression leftExpr &&
                        ExpressionContainsNestedArrowWithSuper(leftExpr)) ||
                       (forInOfStmt.Left is JsVariableDeclarationStatement leftDecl &&
                        leftDecl.Declarators.Any(d =>
                            d.Initializer is not null && ExpressionContainsNestedArrowWithSuper(d.Initializer))) ||
                       ExpressionContainsNestedArrowWithSuper(forInOfStmt.Right) ||
                       StatementContainsNestedArrowWithSuper(forInOfStmt.Body);
            case JsReturnStatement ret:
                return ret.Argument is not null && ExpressionContainsNestedArrowWithSuper(ret.Argument);
            case JsThrowStatement thr:
                return ExpressionContainsNestedArrowWithSuper(thr.Argument);
            case JsVariableDeclarationStatement decl:
                return (decl.BindingInitializer is not null &&
                        ExpressionContainsNestedArrowWithSuper(decl.BindingInitializer)) ||
                       decl.Declarators.Any(d =>
                           d.Initializer is not null && ExpressionContainsNestedArrowWithSuper(d.Initializer));
            case JsEmptyObjectBindingDeclarationStatement emptyObjectBinding:
                return ExpressionContainsNestedArrowWithSuper(emptyObjectBinding.Initializer);
            case JsTryStatement tryStmt:
                return StatementContainsNestedArrowWithSuper(tryStmt.Block) ||
                       (tryStmt.Handler?.BindingPattern is not null &&
                        ExpressionContainsNestedArrowWithSuper(tryStmt.Handler.BindingPattern)) ||
                       (tryStmt.Handler is not null && StatementContainsNestedArrowWithSuper(tryStmt.Handler.Body)) ||
                       (tryStmt.Finalizer is not null && StatementContainsNestedArrowWithSuper(tryStmt.Finalizer));
            case JsSwitchStatement sw:
                return ExpressionContainsNestedArrowWithSuper(sw.Discriminant) ||
                       sw.Cases.Any(c => (c.Test is not null && ExpressionContainsNestedArrowWithSuper(c.Test)) ||
                                         c.Consequent.Any(StatementContainsNestedArrowWithSuper));
            case JsLabeledStatement labeled:
                return StatementContainsNestedArrowWithSuper(labeled.Statement);
            case JsFunctionDeclaration:
            case JsClassDeclaration:
                return false;
            default:
                return false;
        }
    }

    private static bool ExpressionContainsNestedArrowWithSuper(JsExpression expr)
    {
        switch (expr)
        {
            case JsFunctionExpression { IsArrow: true } arrow:
                return arrow.HasSuperBindingHint ||
                       FunctionUsesSuper(arrow.ParameterInitializers, arrow.Body) ||
                       FunctionUsesSuperInNestedArrows(arrow.ParameterInitializers, arrow.Body);
            case JsAssignmentExpression a:
                return ExpressionContainsNestedArrowWithSuper(a.Left) ||
                       ExpressionContainsNestedArrowWithSuper(a.Right);
            case JsBinaryExpression b:
                return ExpressionContainsNestedArrowWithSuper(b.Left) ||
                       ExpressionContainsNestedArrowWithSuper(b.Right);
            case JsConditionalExpression c:
                return ExpressionContainsNestedArrowWithSuper(c.Test) ||
                       ExpressionContainsNestedArrowWithSuper(c.Consequent) ||
                       ExpressionContainsNestedArrowWithSuper(c.Alternate);
            case JsCallExpression c:
                return ExpressionContainsNestedArrowWithSuper(c.Callee) ||
                       c.Arguments.Any(ExpressionContainsNestedArrowWithSuper);
            case JsNewExpression n:
                return ExpressionContainsNestedArrowWithSuper(n.Callee) ||
                       n.Arguments.Any(ExpressionContainsNestedArrowWithSuper);
            case JsMemberExpression m:
                return ExpressionContainsNestedArrowWithSuper(m.Object) ||
                       (m.IsComputed && ExpressionContainsNestedArrowWithSuper(m.Property));
            case JsSequenceExpression s:
                return s.Expressions.Any(ExpressionContainsNestedArrowWithSuper);
            case JsSpreadExpression s:
                return ExpressionContainsNestedArrowWithSuper(s.Argument);
            case JsIntrinsicCallExpression i:
                return i.Arguments.Any(ExpressionContainsNestedArrowWithSuper);
            case JsParameterInitializerExpression p:
                return ExpressionContainsNestedArrowWithSuper(p.Expression);
            case JsArrayExpression a:
                return a.Elements.Any(e => e is not null && ExpressionContainsNestedArrowWithSuper(e));
            case JsTemplateExpression t:
                return t.Expressions.Any(ExpressionContainsNestedArrowWithSuper);
            case JsTaggedTemplateExpression tt:
                return ExpressionContainsNestedArrowWithSuper(tt.Tag) ||
                       tt.Template.Expressions.Any(ExpressionContainsNestedArrowWithSuper);
            case JsObjectExpression o:
                return o.Properties.Any(p =>
                    (p.IsComputed && p.ComputedKey is not null &&
                     ExpressionContainsNestedArrowWithSuper(p.ComputedKey)) ||
                    ExpressionContainsNestedArrowWithSuper(p.Value));
            case JsUnaryExpression u:
                return ExpressionContainsNestedArrowWithSuper(u.Argument);
            case JsUpdateExpression u:
                return ExpressionContainsNestedArrowWithSuper(u.Argument);
            case JsYieldExpression y:
                return y.Argument is not null && ExpressionContainsNestedArrowWithSuper(y.Argument);
            case JsAwaitExpression a:
                return ExpressionContainsNestedArrowWithSuper(a.Argument);
            case JsImportCallExpression importCall:
                return ExpressionContainsNestedArrowWithSuper(importCall.Argument) ||
                       (importCall.Options is not null && ExpressionContainsNestedArrowWithSuper(importCall.Options));
            default:
                return false;
        }
    }

    private static bool StatementContainsSuperInCurrentFunction(JsStatement statement)
    {
        switch (statement)
        {
            case JsExpressionStatement expr:
                return ExpressionContainsSuperInCurrentFunction(expr.Expression);
            case JsBlockStatement block:
                return block.Statements.Any(StatementContainsSuperInCurrentFunction);
            case JsIfStatement ifStmt:
                return ExpressionContainsSuperInCurrentFunction(ifStmt.Test) ||
                       StatementContainsSuperInCurrentFunction(ifStmt.Consequent) ||
                       (ifStmt.Alternate is not null && StatementContainsSuperInCurrentFunction(ifStmt.Alternate));
            case JsWhileStatement whileStmt:
                return ExpressionContainsSuperInCurrentFunction(whileStmt.Test) ||
                       StatementContainsSuperInCurrentFunction(whileStmt.Body);
            case JsForStatement forStmt:
                return (forStmt.Init is JsExpression initExpr && ExpressionContainsSuperInCurrentFunction(initExpr)) ||
                       (forStmt.Init is JsVariableDeclarationStatement initDecl &&
                        initDecl.Declarators.Any(d =>
                            d.Initializer is not null && ExpressionContainsSuperInCurrentFunction(d.Initializer))) ||
                       (forStmt.Test is not null && ExpressionContainsSuperInCurrentFunction(forStmt.Test)) ||
                       (forStmt.Update is not null && ExpressionContainsSuperInCurrentFunction(forStmt.Update)) ||
                       StatementContainsSuperInCurrentFunction(forStmt.Body);
            case JsForInOfStatement forInOfStmt:
                return (forInOfStmt.Left is JsExpression leftExpr &&
                        ExpressionContainsSuperInCurrentFunction(leftExpr)) ||
                       (forInOfStmt.Left is JsVariableDeclarationStatement leftDecl &&
                        leftDecl.Declarators.Any(d =>
                            d.Initializer is not null && ExpressionContainsSuperInCurrentFunction(d.Initializer))) ||
                       ExpressionContainsSuperInCurrentFunction(forInOfStmt.Right) ||
                       StatementContainsSuperInCurrentFunction(forInOfStmt.Body);
            case JsReturnStatement ret:
                return ret.Argument is not null && ExpressionContainsSuperInCurrentFunction(ret.Argument);
            case JsThrowStatement thr:
                return ExpressionContainsSuperInCurrentFunction(thr.Argument);
            case JsVariableDeclarationStatement decl:
                return (decl.BindingInitializer is not null &&
                        ExpressionContainsSuperInCurrentFunction(decl.BindingInitializer)) ||
                       decl.Declarators.Any(d =>
                           d.Initializer is not null && ExpressionContainsSuperInCurrentFunction(d.Initializer));
            case JsEmptyObjectBindingDeclarationStatement emptyObjectBinding:
                return ExpressionContainsSuperInCurrentFunction(emptyObjectBinding.Initializer);
            case JsTryStatement tryStmt:
                return StatementContainsSuperInCurrentFunction(tryStmt.Block) ||
                       (tryStmt.Handler?.BindingPattern is not null &&
                        ExpressionContainsSuperInCurrentFunction(tryStmt.Handler.BindingPattern)) ||
                       (tryStmt.Handler is not null && StatementContainsSuperInCurrentFunction(tryStmt.Handler.Body)) ||
                       (tryStmt.Finalizer is not null && StatementContainsSuperInCurrentFunction(tryStmt.Finalizer));
            case JsSwitchStatement sw:
                return ExpressionContainsSuperInCurrentFunction(sw.Discriminant) ||
                       sw.Cases.Any(c => (c.Test is not null && ExpressionContainsSuperInCurrentFunction(c.Test)) ||
                                         c.Consequent.Any(StatementContainsSuperInCurrentFunction));
            case JsLabeledStatement labeled:
                return StatementContainsSuperInCurrentFunction(labeled.Statement);
            case JsFunctionDeclaration:
            case JsClassDeclaration:
                return false;
            default:
                return false;
        }
    }

    private static bool ExpressionContainsSuperInCurrentFunction(JsExpression expr)
    {
        switch (expr)
        {
            case JsSuperExpression:
                return true;
            case JsAssignmentExpression a:
                return ExpressionContainsSuperInCurrentFunction(a.Left) ||
                       ExpressionContainsSuperInCurrentFunction(a.Right);
            case JsBinaryExpression b:
                return ExpressionContainsSuperInCurrentFunction(b.Left) ||
                       ExpressionContainsSuperInCurrentFunction(b.Right);
            case JsConditionalExpression c:
                return ExpressionContainsSuperInCurrentFunction(c.Test) ||
                       ExpressionContainsSuperInCurrentFunction(c.Consequent) ||
                       ExpressionContainsSuperInCurrentFunction(c.Alternate);
            case JsCallExpression c:
                return (c.Callee is not JsSuperExpression && ExpressionContainsSuperInCurrentFunction(c.Callee)) ||
                       c.Arguments.Any(ExpressionContainsSuperInCurrentFunction);
            case JsNewExpression n:
                return ExpressionContainsSuperInCurrentFunction(n.Callee) ||
                       n.Arguments.Any(ExpressionContainsSuperInCurrentFunction);
            case JsMemberExpression m:
                return ExpressionContainsSuperInCurrentFunction(m.Object) ||
                       (m.IsComputed && ExpressionContainsSuperInCurrentFunction(m.Property));
            case JsSequenceExpression s:
                return s.Expressions.Any(ExpressionContainsSuperInCurrentFunction);
            case JsSpreadExpression s:
                return ExpressionContainsSuperInCurrentFunction(s.Argument);
            case JsIntrinsicCallExpression i:
                return i.Arguments.Any(ExpressionContainsSuperInCurrentFunction);
            case JsParameterInitializerExpression p:
                return ExpressionContainsSuperInCurrentFunction(p.Expression);
            case JsArrayExpression a:
                return a.Elements.Any(e => e is not null && ExpressionContainsSuperInCurrentFunction(e));
            case JsTemplateExpression t:
                return t.Expressions.Any(ExpressionContainsSuperInCurrentFunction);
            case JsTaggedTemplateExpression tt:
                return ExpressionContainsSuperInCurrentFunction(tt.Tag) ||
                       tt.Template.Expressions.Any(ExpressionContainsSuperInCurrentFunction);
            case JsObjectExpression o:
                return o.Properties.Any(PropertyContainsSuperInCurrentFunction);
            case JsClassExpression:
            case JsFunctionExpression:
                return false;
            case JsUnaryExpression u:
                return ExpressionContainsSuperInCurrentFunction(u.Argument);
            case JsUpdateExpression u:
                return ExpressionContainsSuperInCurrentFunction(u.Argument);
            case JsYieldExpression y:
                return y.Argument is not null && ExpressionContainsSuperInCurrentFunction(y.Argument);
            case JsAwaitExpression a:
                return ExpressionContainsSuperInCurrentFunction(a.Argument);
            default:
                return false;
        }
    }

    private static bool PropertyContainsSuperInCurrentFunction(JsObjectProperty property)
    {
        if (property.IsComputed && property.ComputedKey is not null &&
            ExpressionContainsSuperInCurrentFunction(property.ComputedKey))
            return true;

        return property.Value is not null && ExpressionContainsSuperInCurrentFunction(property.Value);
    }
}
