using Okojo.Parsing;

namespace Okojo.Compiler;

public sealed partial class JsCompiler
{
    private void PrecomputeDirectChildCaptures(IEnumerable<JsStatement> statements)
    {
        foreach (var stmt in statements) ScanForDirectNestedFunctionCapturesInStatement(stmt);
    }

    private void ScanForDirectNestedFunctionCapturesInStatement(JsStatement stmt)
    {
        switch (stmt)
        {
            case JsExpressionStatement es:
                ScanForDirectNestedFunctionCapturesInExpression(es.Expression);
                break;
            case JsBlockStatement block:
                PushBlockLexicalAliases(block);
                try
                {
                    foreach (var s in block.Statements) ScanForDirectNestedFunctionCapturesInStatement(s);
                    MarkBlockLexicalsCapturedByNestedFunctionFallback(block);
                }
                finally
                {
                    PopBlockLexicalAliases(block);
                }

                break;
            case JsIfStatement i:
                ScanForDirectNestedFunctionCapturesInExpression(i.Test);
                ScanForDirectNestedFunctionCapturesInStatement(i.Consequent);
                if (i.Alternate != null) ScanForDirectNestedFunctionCapturesInStatement(i.Alternate);
                break;
            case JsWhileStatement w:
                ScanForDirectNestedFunctionCapturesInExpression(w.Test);
                ScanForDirectNestedFunctionCapturesInStatement(w.Body);
                break;
            case JsDoWhileStatement d:
                ScanForDirectNestedFunctionCapturesInStatement(d.Body);
                ScanForDirectNestedFunctionCapturesInExpression(d.Test);
                break;
            case JsForStatement f:
                PushForHeadLexicalAliases(f);
                try
                {
                    if (f.Init is JsVariableDeclarationStatement initDecl)
                    {
                        foreach (var d in initDecl.Declarators)
                            if (d.Initializer != null)
                                ScanForDirectNestedFunctionCapturesInExpression(d.Initializer);
                    }
                    else if (f.Init is JsExpression initExpr)
                    {
                        ScanForDirectNestedFunctionCapturesInExpression(initExpr);
                    }

                    if (f.Test != null) ScanForDirectNestedFunctionCapturesInExpression(f.Test);
                    if (f.Update != null) ScanForDirectNestedFunctionCapturesInExpression(f.Update);
                    ScanForDirectNestedFunctionCapturesInStatement(f.Body);
                    MarkForLoopHeadBindingsCapturedByNestedFunctionFallback(f);
                }
                finally
                {
                    PopForHeadLexicalAliases(f);
                }

                break;
            case JsForInOfStatement f:
                if (f.Left is JsVariableDeclarationStatement leftDecl)
                {
                    PushForInOfHeadLexicalAliases(f);
                    try
                    {
                        foreach (var d in leftDecl.Declarators)
                            if (d.Initializer != null)
                                ScanForDirectNestedFunctionCapturesInExpression(d.Initializer);
                    }
                    finally
                    {
                        PopForInOfHeadLexicalAliases(f);
                    }
                }
                else if (f.Left is JsExpression leftExpr)
                {
                    ScanForDirectNestedFunctionCapturesInExpression(leftExpr);
                }

                PushForInOfHeadTdzLexicalAliases(f);
                try
                {
                    ScanForDirectNestedFunctionCapturesInExpression(f.Right);
                }
                finally
                {
                    PopForInOfHeadTdzLexicalAliases(f);
                }

                PushForInOfHeadLexicalAliases(f);
                try
                {
                    ScanForDirectNestedFunctionCapturesInStatement(f.Body);
                    MarkForInOfHeadBindingsCapturedByNestedFunctionFallback(f);
                }
                finally
                {
                    PopForInOfHeadLexicalAliases(f);
                }

                break;
            case JsReturnStatement r:
                if (r.Argument != null) ScanForDirectNestedFunctionCapturesInExpression(r.Argument);
                break;
            case JsThrowStatement t:
                ScanForDirectNestedFunctionCapturesInExpression(t.Argument);
                break;
            case JsBreakStatement:
            case JsContinueStatement:
                break;
            case JsLabeledStatement labeled:
                ScanForDirectNestedFunctionCapturesInStatement(labeled.Statement);
                break;
            case JsVariableDeclarationStatement v:
                if (v.BindingInitializer is not null)
                {
                    ScanForDirectNestedFunctionCapturesInExpression(v.BindingInitializer);
                    break;
                }

                foreach (var d in v.Declarators)
                    if (d.Initializer != null)
                        ScanForDirectNestedFunctionCapturesInExpression(d.Initializer);
                break;
            case JsEmptyObjectBindingDeclarationStatement emptyObjectBinding:
                ScanForDirectNestedFunctionCapturesInExpression(emptyObjectBinding.Initializer);
                break;
            case JsFunctionDeclaration f:
                if (!string.IsNullOrEmpty(f.Name))
                {
                    if (TryResolveLocalBinding(new CompilerIdentifierName(f.Name, f.NameId),
                            out var resolvedFunctionBinding))
                        MarkCapturedByChildBinding(resolvedFunctionBinding.SymbolId);
                    else if (HasLocalBinding(f.Name))
                        MarkCapturedByChildBinding(f.Name);
                }

                MarkDirectCapturesFromNestedFunction(f.ParameterInitializers, f.Body);
                break;
            case JsClassDeclaration c:
                ScanForDirectNestedFunctionCapturesInExpression(c.ClassExpression);
                break;
            case JsTryStatement t:
                ScanForDirectNestedFunctionCapturesInStatement(t.Block);
                if (t.Handler is not null)
                {
                    var catchBindings = PushCatchBindingAliases(t.Handler);
                    try
                    {
                        if (t.Handler.BindingPattern is not null)
                            ScanForDirectNestedFunctionCapturesInExpression(t.Handler.BindingPattern);
                        ScanForDirectNestedFunctionCapturesInStatement(t.Handler.Body);
                    }
                    finally
                    {
                        if (catchBindings is not null)
                            PopAliasScope();
                    }
                }

                if (t.Finalizer is not null)
                    ScanForDirectNestedFunctionCapturesInStatement(t.Finalizer);
                break;
            case JsSwitchStatement sw:
                ScanForDirectNestedFunctionCapturesInExpression(sw.Discriminant);
                PushSwitchLexicalAliases(sw);
                try
                {
                    foreach (var c in sw.Cases)
                    {
                        if (c.Test is not null)
                            ScanForDirectNestedFunctionCapturesInExpression(c.Test);
                        foreach (var s in c.Consequent)
                            ScanForDirectNestedFunctionCapturesInStatement(s);
                    }
                }
                finally
                {
                    PopSwitchLexicalAliases(sw);
                }

                break;
        }
    }

    private void ScanForDirectNestedFunctionCapturesInExpression(JsExpression expr)
    {
        switch (expr)
        {
            case JsFunctionExpression f:
                if (!string.IsNullOrEmpty(f.Name))
                {
                    if (TryResolveLocalBinding(new CompilerIdentifierName(f.Name, f.NameId),
                            out var resolvedFunctionBinding))
                        MarkCapturedByChildBinding(resolvedFunctionBinding.SymbolId);
                    else if (HasLocalBinding(f.Name))
                        MarkCapturedByChildBinding(f.Name);
                }

                MarkDirectCapturesFromNestedFunction(f.ParameterInitializers, f.Body);
                break;
            case JsAssignmentExpression a:
                ScanForDirectNestedFunctionCapturesInExpression(a.Left);
                ScanForDirectNestedFunctionCapturesInExpression(a.Right);
                if (a.Left is JsArrayExpression arrayPattern)
                    MarkSyntheticDestructuringThunkCapturesInArrayPattern(arrayPattern);
                break;
            case JsBinaryExpression b:
                ScanForDirectNestedFunctionCapturesInExpression(b.Left);
                ScanForDirectNestedFunctionCapturesInExpression(b.Right);
                break;
            case JsConditionalExpression c:
                ScanForDirectNestedFunctionCapturesInExpression(c.Test);
                ScanForDirectNestedFunctionCapturesInExpression(c.Consequent);
                ScanForDirectNestedFunctionCapturesInExpression(c.Alternate);
                break;
            case JsCallExpression c:
                ScanForDirectNestedFunctionCapturesInExpression(c.Callee);
                foreach (var arg in c.Arguments) ScanForDirectNestedFunctionCapturesInExpression(arg);
                break;
            case JsNewExpression n:
                ScanForDirectNestedFunctionCapturesInExpression(n.Callee);
                foreach (var arg in n.Arguments) ScanForDirectNestedFunctionCapturesInExpression(arg);
                break;
            case JsUpdateExpression u:
                ScanForDirectNestedFunctionCapturesInExpression(u.Argument);
                break;
            case JsUnaryExpression u:
                ScanForDirectNestedFunctionCapturesInExpression(u.Argument);
                break;
            case JsYieldExpression y:
                if (y.Argument is not null)
                    ScanForDirectNestedFunctionCapturesInExpression(y.Argument);
                break;
            case JsAwaitExpression a:
                ScanForDirectNestedFunctionCapturesInExpression(a.Argument);
                break;
            case JsSpreadExpression s:
                ScanForDirectNestedFunctionCapturesInExpression(s.Argument);
                break;
            case JsClassExpression classExpr:
                ScanForDirectNestedFunctionCapturesInClassExpression(classExpr);
                break;
            case JsMemberExpression m:
                ScanForDirectNestedFunctionCapturesInExpression(m.Object);
                if (m.IsComputed)
                    ScanForDirectNestedFunctionCapturesInExpression(m.Property);
                break;
            case JsObjectExpression o:
                foreach (var prop in o.Properties)
                {
                    if (prop.IsComputed && prop.ComputedKey is not null)
                        ScanForDirectNestedFunctionCapturesInExpression(prop.ComputedKey);
                    ScanForDirectNestedFunctionCapturesInExpression(prop.Value);
                }

                break;
            case JsArrayExpression a:
                foreach (var item in a.Elements)
                    if (item is not null)
                        ScanForDirectNestedFunctionCapturesInExpression(item);

                break;
            case JsTemplateExpression t:
                foreach (var e in t.Expressions)
                    ScanForDirectNestedFunctionCapturesInExpression(e);
                break;
            case JsTaggedTemplateExpression tt:
                ScanForDirectNestedFunctionCapturesInExpression(tt.Tag);
                foreach (var e in tt.Template.Expressions)
                    ScanForDirectNestedFunctionCapturesInExpression(e);
                break;
            case JsSequenceExpression s:
                foreach (var e in s.Expressions)
                    ScanForDirectNestedFunctionCapturesInExpression(e);
                break;
            case JsImportCallExpression importCall:
                ScanForDirectNestedFunctionCapturesInExpression(importCall.Argument);
                if (importCall.Options is not null)
                    ScanForDirectNestedFunctionCapturesInExpression(importCall.Options);
                break;
        }
    }

    private void MarkSyntheticDestructuringThunkCapturesInArrayPattern(JsArrayExpression pattern)
    {
        for (var i = 0; i < pattern.Elements.Count; i++)
        {
            var element = pattern.Elements[i];
            if (element is null)
                continue;

            var targetExpression = element is JsSpreadExpression spread ? spread.Argument : element;
            var (target, defaultExpression) = ExtractAssignmentTargetWithDefault(targetExpression);
            MarkCapturedNamesReferencedByNestedFunction(target);
            if (defaultExpression is not null)
                MarkCapturedNamesReferencedByNestedFunction(defaultExpression);
        }
    }

    private void ScanForDirectNestedFunctionCapturesInClassExpression(JsClassExpression classExpr)
    {
        string? classLexicalInternalName = null;
        if (!string.IsNullOrEmpty(classExpr.Name))
        {
            classLexicalInternalName = GetClassLexicalInternalName(classExpr.Name, classExpr.Position);
            var classLexicalSymbolId = GetOrCreateSymbolId(classLexicalInternalName);
            GetOrCreateLocal(classLexicalSymbolId);
            MarkLexicalBinding(classLexicalSymbolId, true);
            PushAliasScope(new CompilerIdentifierName(classExpr.Name, classExpr.NameId), classLexicalInternalName);
        }

        try
        {
            if (classExpr.ExtendsExpression is not null)
                ScanForDirectNestedFunctionCapturesInExpression(classExpr.ExtendsExpression);

            foreach (var element in classExpr.Elements)
            {
                if (element.IsStatic && element.Kind == JsClassElementKind.Field &&
                    element.FieldInitializer is not null)
                    MarkCapturedNamesReferencedBySyntheticClassFieldInitializer(element.FieldInitializer);

                if (!element.IsStatic && element.Kind == JsClassElementKind.Field)
                {
                    if (element.IsComputedKey && element.ComputedKey is not null)
                        MarkCapturedNamesReferencedByNestedFunction(element.ComputedKey, false);
                    if (element.FieldInitializer is not null)
                        MarkCapturedNamesReferencedByNestedFunction(element.FieldInitializer, false);
                }

                if (element.IsComputedKey && element.ComputedKey is not null)
                    ScanForDirectNestedFunctionCapturesInExpression(element.ComputedKey);

                if (element.StaticBlock is not null)
                {
                    PrecomputeDirectChildCaptures(element.StaticBlock.Statements);
                    continue;
                }

                if (element.FieldInitializer is not null)
                    ScanForDirectNestedFunctionCapturesInExpression(element.FieldInitializer);

                if (element.Value is not null)
                    MarkDirectCapturesFromNestedFunction(element.Value.ParameterInitializers, element.Value.Body);
            }
        }
        finally
        {
            if (classLexicalInternalName is not null)
                PopAliasScope();
        }
    }

    private void MarkCapturedNamesReferencedBySyntheticClassFieldInitializer(JsExpression initializer)
    {
        MarkCapturedNamesReferencedByNestedFunction(initializer, false);
    }

    private void MarkDirectCapturesFromNestedFunction(
        IReadOnlyList<JsExpression?> parameterInitializers,
        JsBlockStatement body,
        bool allowArgumentsCapture = true)
    {
        foreach (var initializer in parameterInitializers)
            if (initializer is not null)
                MarkCapturedNamesReferencedByNestedFunction(initializer, allowArgumentsCapture);

        foreach (var stmt in body.Statements) MarkCapturedNamesReferencedByNestedFunction(stmt, allowArgumentsCapture);
    }

    private void MarkCapturedNamesReferencedByNestedFunction(JsStatement stmt, bool allowArgumentsCapture = true)
    {
        switch (stmt)
        {
            case JsExpressionStatement es:
                MarkCapturedNamesReferencedByNestedFunction(es.Expression, allowArgumentsCapture);
                break;
            case JsBlockStatement block:
                foreach (var s in block.Statements)
                    MarkCapturedNamesReferencedByNestedFunction(s, allowArgumentsCapture);
                break;
            case JsIfStatement i:
                MarkCapturedNamesReferencedByNestedFunction(i.Test, allowArgumentsCapture);
                MarkCapturedNamesReferencedByNestedFunction(i.Consequent, allowArgumentsCapture);
                if (i.Alternate != null)
                    MarkCapturedNamesReferencedByNestedFunction(i.Alternate, allowArgumentsCapture);
                break;
            case JsWhileStatement w:
                MarkCapturedNamesReferencedByNestedFunction(w.Test, allowArgumentsCapture);
                MarkCapturedNamesReferencedByNestedFunction(w.Body, allowArgumentsCapture);
                break;
            case JsDoWhileStatement d:
                MarkCapturedNamesReferencedByNestedFunction(d.Body, allowArgumentsCapture);
                MarkCapturedNamesReferencedByNestedFunction(d.Test, allowArgumentsCapture);
                break;
            case JsForStatement f:
                PushForHeadLexicalAliases(f);
                try
                {
                    if (f.Init is JsVariableDeclarationStatement initDecl)
                    {
                        foreach (var d in initDecl.Declarators)
                            if (d.Initializer != null)
                                MarkCapturedNamesReferencedByNestedFunction(d.Initializer, allowArgumentsCapture);
                    }
                    else if (f.Init is JsExpression initExpr)
                    {
                        MarkCapturedNamesReferencedByNestedFunction(initExpr, allowArgumentsCapture);
                    }

                    if (f.Test != null) MarkCapturedNamesReferencedByNestedFunction(f.Test, allowArgumentsCapture);
                    if (f.Update != null) MarkCapturedNamesReferencedByNestedFunction(f.Update, allowArgumentsCapture);
                    MarkCapturedNamesReferencedByNestedFunction(f.Body, allowArgumentsCapture);
                    MarkForLoopHeadBindingsCapturedByNestedFunctionFallback(f);
                }
                finally
                {
                    PopForHeadLexicalAliases(f);
                }

                break;
            case JsForInOfStatement f:
                if (f.Left is JsVariableDeclarationStatement leftDecl)
                {
                    PushForInOfHeadLexicalAliases(f);
                    try
                    {
                        foreach (var d in leftDecl.Declarators)
                            if (d.Initializer != null)
                                MarkCapturedNamesReferencedByNestedFunction(d.Initializer, allowArgumentsCapture);
                    }
                    finally
                    {
                        PopForInOfHeadLexicalAliases(f);
                    }
                }
                else if (f.Left is JsExpression leftExpr)
                {
                    MarkCapturedNamesReferencedByNestedFunction(leftExpr, allowArgumentsCapture);
                }

                PushForInOfHeadTdzLexicalAliases(f);
                try
                {
                    MarkCapturedNamesReferencedByNestedFunction(f.Right, allowArgumentsCapture);
                }
                finally
                {
                    PopForInOfHeadTdzLexicalAliases(f);
                }

                PushForInOfHeadLexicalAliases(f);
                try
                {
                    MarkCapturedNamesReferencedByNestedFunction(f.Body, allowArgumentsCapture);
                    MarkForInOfHeadBindingsCapturedByNestedFunctionFallback(f);
                }
                finally
                {
                    PopForInOfHeadLexicalAliases(f);
                }

                break;
            case JsReturnStatement r:
                if (r.Argument != null) MarkCapturedNamesReferencedByNestedFunction(r.Argument, allowArgumentsCapture);
                break;
            case JsThrowStatement t:
                MarkCapturedNamesReferencedByNestedFunction(t.Argument, allowArgumentsCapture);
                break;
            case JsBreakStatement:
            case JsContinueStatement:
                break;
            case JsLabeledStatement labeled:
                MarkCapturedNamesReferencedByNestedFunction(labeled.Statement, allowArgumentsCapture);
                break;
            case JsVariableDeclarationStatement v:
                if (v.BindingInitializer is not null)
                {
                    MarkCapturedNamesReferencedByNestedFunction(v.BindingInitializer, allowArgumentsCapture);
                    break;
                }

                foreach (var d in v.Declarators)
                    if (d.Initializer != null)
                        MarkCapturedNamesReferencedByNestedFunction(d.Initializer, allowArgumentsCapture);
                break;
            case JsEmptyObjectBindingDeclarationStatement emptyObjectBinding:
                MarkCapturedNamesReferencedByNestedFunction(emptyObjectBinding.Initializer, allowArgumentsCapture);
                break;
            case JsFunctionDeclaration functionDeclaration:
            {
                var nested = functionDeclaration;
                MarkDirectCapturesFromNestedFunction(nested.ParameterInitializers, nested.Body,
                    false);
            }
                break;
            case JsClassDeclaration classDeclaration:
                MarkCapturedNamesReferencedByNestedClassExpression(classDeclaration.ClassExpression,
                    allowArgumentsCapture);
                break;
            case JsTryStatement t:
                MarkCapturedNamesReferencedByNestedFunction(t.Block, allowArgumentsCapture);
                if (t.Handler is not null)
                {
                    var catchBindings = PushCatchBindingAliases(t.Handler);
                    try
                    {
                        if (t.Handler.BindingPattern is not null)
                            MarkCapturedNamesReferencedByNestedFunction(t.Handler.BindingPattern,
                                allowArgumentsCapture);
                        MarkCapturedNamesReferencedByNestedFunction(t.Handler.Body, allowArgumentsCapture);
                    }
                    finally
                    {
                        if (catchBindings is not null)
                            PopAliasScope();
                    }
                }

                if (t.Finalizer is not null)
                    MarkCapturedNamesReferencedByNestedFunction(t.Finalizer, allowArgumentsCapture);
                break;
            case JsSwitchStatement sw:
                MarkCapturedNamesReferencedByNestedFunction(sw.Discriminant, allowArgumentsCapture);
                PushSwitchLexicalAliases(sw);
                try
                {
                    foreach (var c in sw.Cases)
                    {
                        if (c.Test is not null)
                            MarkCapturedNamesReferencedByNestedFunction(c.Test, allowArgumentsCapture);
                        foreach (var s in c.Consequent)
                            MarkCapturedNamesReferencedByNestedFunction(s, allowArgumentsCapture);
                    }
                }
                finally
                {
                    PopSwitchLexicalAliases(sw);
                }

                break;
        }
    }

    private void MarkCapturedNamesReferencedByNestedFunction(JsExpression expr, bool allowArgumentsCapture = true)
    {
        switch (expr)
        {
            case JsIdentifierExpression id:
            {
                var identifier = CompilerIdentifierName.From(id);
                if (!allowArgumentsCapture && string.Equals(id.Name, "arguments", StringComparison.Ordinal))
                    break;

                if (allowArgumentsCapture &&
                    string.Equals(id.Name, "arguments", StringComparison.Ordinal) &&
                    ShouldUseFunctionArgumentsBinding(id.Name))
                {
                    MarkCapturedByChildBinding(SyntheticArgumentsSymbolId);
                    break;
                }

                if (TryGetModuleVariableBinding(id.Name, out _) ||
                    (identifier.NameId >= 0 &&
                     !string.Equals(ResolveLocalAlias(identifier), id.Name, StringComparison.Ordinal) &&
                     TryGetModuleVariableBinding(ResolveLocalAlias(identifier), out _)))
                    break;

                if (TryResolveLocalBinding(identifier, out var resolvedBinding) &&
                    IsCurrentFunctionLocalVisibleForCapture(resolvedBinding.SymbolId))
                    MarkCapturedByChildBinding(resolvedBinding.SymbolId);
                else
                    MarkAncestorBindingCapturedByNestedFunction(identifier);
            }
                break;
            case JsImportMetaExpression:
                break;
            case JsImportCallExpression importCall:
                MarkCapturedNamesReferencedByNestedFunction(importCall.Argument, allowArgumentsCapture);
                if (importCall.Options is not null)
                    MarkCapturedNamesReferencedByNestedFunction(importCall.Options, allowArgumentsCapture);
                break;
            case JsAssignmentExpression a:
                MarkCapturedNamesReferencedByNestedFunction(a.Left, allowArgumentsCapture);
                MarkCapturedNamesReferencedByNestedFunction(a.Right, allowArgumentsCapture);
                break;
            case JsBinaryExpression b:
                MarkCapturedNamesReferencedByNestedFunction(b.Left, allowArgumentsCapture);
                MarkCapturedNamesReferencedByNestedFunction(b.Right, allowArgumentsCapture);
                break;
            case JsConditionalExpression c:
                MarkCapturedNamesReferencedByNestedFunction(c.Test, allowArgumentsCapture);
                MarkCapturedNamesReferencedByNestedFunction(c.Consequent, allowArgumentsCapture);
                MarkCapturedNamesReferencedByNestedFunction(c.Alternate, allowArgumentsCapture);
                break;
            case JsCallExpression c:
                MarkCapturedNamesReferencedByNestedFunction(c.Callee, allowArgumentsCapture);
                foreach (var arg in c.Arguments)
                    MarkCapturedNamesReferencedByNestedFunction(arg, allowArgumentsCapture);
                break;
            case JsNewExpression n:
                MarkCapturedNamesReferencedByNestedFunction(n.Callee, allowArgumentsCapture);
                foreach (var arg in n.Arguments)
                    MarkCapturedNamesReferencedByNestedFunction(arg, allowArgumentsCapture);
                break;
            case JsUpdateExpression u:
                MarkCapturedNamesReferencedByNestedFunction(u.Argument, allowArgumentsCapture);
                break;
            case JsUnaryExpression u:
                MarkCapturedNamesReferencedByNestedFunction(u.Argument, allowArgumentsCapture);
                break;
            case JsYieldExpression y:
                if (y.Argument is not null)
                    MarkCapturedNamesReferencedByNestedFunction(y.Argument, allowArgumentsCapture);
                break;
            case JsAwaitExpression a:
                MarkCapturedNamesReferencedByNestedFunction(a.Argument, allowArgumentsCapture);
                break;
            case JsSpreadExpression s:
                MarkCapturedNamesReferencedByNestedFunction(s.Argument, allowArgumentsCapture);
                break;
            case JsFunctionExpression f:
                MarkDirectCapturesFromNestedFunction(
                    f.ParameterInitializers,
                    f.Body,
                    f.IsArrow && allowArgumentsCapture);
                break;
            case JsClassExpression classExpr:
                MarkCapturedNamesReferencedByNestedClassExpression(classExpr, allowArgumentsCapture);
                break;
            case JsMemberExpression m:
                MarkCapturedNamesReferencedByNestedFunction(m.Object, allowArgumentsCapture);
                if (m.IsComputed)
                    MarkCapturedNamesReferencedByNestedFunction(m.Property, allowArgumentsCapture);
                break;
            case JsObjectExpression o:
                foreach (var prop in o.Properties)
                {
                    if (prop.IsComputed && prop.ComputedKey is not null)
                        MarkCapturedNamesReferencedByNestedFunction(prop.ComputedKey, allowArgumentsCapture);
                    MarkCapturedNamesReferencedByNestedFunction(prop.Value, allowArgumentsCapture);
                }

                break;
            case JsArrayExpression a:
                foreach (var item in a.Elements)
                    if (item is not null)
                        MarkCapturedNamesReferencedByNestedFunction(item, allowArgumentsCapture);

                break;
            case JsTemplateExpression t:
                foreach (var e in t.Expressions)
                    MarkCapturedNamesReferencedByNestedFunction(e, allowArgumentsCapture);
                break;
            case JsTaggedTemplateExpression tt:
                MarkCapturedNamesReferencedByNestedFunction(tt.Tag, allowArgumentsCapture);
                foreach (var e in tt.Template.Expressions)
                    MarkCapturedNamesReferencedByNestedFunction(e, allowArgumentsCapture);
                break;
            case JsSequenceExpression s:
                foreach (var e in s.Expressions)
                    MarkCapturedNamesReferencedByNestedFunction(e, allowArgumentsCapture);
                break;
        }
    }

    private void MarkCapturedNamesReferencedByNestedClassExpression(
        JsClassExpression classExpr,
        bool allowArgumentsCapture)
    {
        string? classLexicalInternalName = null;
        if (!string.IsNullOrEmpty(classExpr.Name))
        {
            classLexicalInternalName = GetClassLexicalInternalName(classExpr.Name, classExpr.Position);
            var classLexicalSymbolId = GetOrCreateSymbolId(classLexicalInternalName);
            GetOrCreateLocal(classLexicalSymbolId);
            MarkLexicalBinding(classLexicalSymbolId, true);
            PushAliasScope(new CompilerIdentifierName(classExpr.Name, classExpr.NameId), classLexicalInternalName);
        }

        try
        {
            if (classExpr.ExtendsExpression is not null)
                MarkCapturedNamesReferencedByNestedFunction(classExpr.ExtendsExpression, allowArgumentsCapture);

            foreach (var element in classExpr.Elements)
            {
                if (element.StaticBlock is not null)
                {
                    MarkCapturedNamesReferencedByNestedFunction(element.StaticBlock, allowArgumentsCapture);
                    continue;
                }

                if (!element.IsStatic && element.Kind == JsClassElementKind.Field)
                {
                    if (element.IsComputedKey && element.ComputedKey is not null)
                        MarkCapturedNamesReferencedByNestedFunction(element.ComputedKey, false);
                    if (element.FieldInitializer is not null)
                        MarkCapturedNamesReferencedByNestedFunction(element.FieldInitializer, false);
                    continue;
                }

                if (element.IsComputedKey && element.ComputedKey is not null)
                    MarkCapturedNamesReferencedByNestedFunction(element.ComputedKey, allowArgumentsCapture);

                if (element.FieldInitializer is not null)
                    MarkCapturedNamesReferencedByNestedFunction(element.FieldInitializer, allowArgumentsCapture);

                if (element.Value is not null)
                    MarkDirectCapturesFromNestedFunction(
                        element.Value.ParameterInitializers,
                        element.Value.Body,
                        element.Value.IsArrow && allowArgumentsCapture);
            }
        }
        finally
        {
            if (classLexicalInternalName is not null)
                PopAliasScope();
        }
    }

    private void MarkAncestorBindingCapturedByNestedFunction(CompilerIdentifierName identifier)
    {
        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.parent)
        {
            if (!ancestor.TryResolveLocalBinding(identifier, out var resolvedBinding))
                continue;

            if (!ancestor.IsCurrentFunctionLocalVisibleForCapture(resolvedBinding.SymbolId))
                continue;

            if (!ancestor.HasLocalBinding(resolvedBinding.SymbolId) &&
                !ancestor.TryGetCurrentContextSlot(resolvedBinding.SymbolId, out _))
                continue;

            ancestor.MarkCapturedByChildBinding(resolvedBinding.SymbolId);
            return;
        }
    }
}
