using System.Buffers;

namespace Okojo.JavaScript.Parsing;

internal sealed class FlatJavaScriptParser
{
    private readonly FlatAst ast;
    private readonly JsLexer lexer;
    private readonly string source;
    private JsToken current;
    private int functionDepth;
    private int loopDepth;
    private int switchDepth;
    private bool strictMode;

    private FlatJavaScriptParser(string source, string? sourcePath)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        ast = new FlatAst(source, sourcePath);
        lexer = new JsLexer(source);
        current = lexer.NextToken();
    }

    public static FlatAst ParseScript(string source, string? sourcePath = null)
    {
        var parser = new FlatJavaScriptParser(source, sourcePath);
        try
        {
            parser.ast.Root = parser.ParseProgram();
            return parser.ast;
        }
        catch
        {
            parser.ast.Dispose();
            throw;
        }
    }

    private AstArena Arena => ast.Arena;

    private int ParseProgram()
    {
        Span<int> initial = stackalloc int[16];
        var statements = new NodeList(initial);
        var allowsDirectives = true;
        try
        {
            while (current.Kind != JsTokenKind.Eof)
            {
                var statement = ParseStatement();
                statements.Add(statement);
                if (allowsDirectives && IsUseStrictDirective(statement))
                {
                    ast.StrictDeclared = true;
                    strictMode = true;
                }
                else
                    allowsDirectives = false;
            }

            var children = Arena.AddChildren(statements.AsSpan());
            return Arena.Add(AstKind.Program, children.Offset, children.Count);
        }
        finally
        {
            statements.Dispose();
        }
    }

    private int ParseStatement()
    {
        var position = current.Position;
        return current.Kind switch
        {
            JsTokenKind.Semicolon => ParseEmptyStatement(position),
            JsTokenKind.LeftBrace => ParseBlock(out _),
            JsTokenKind.Var or JsTokenKind.Let or JsTokenKind.Const => ParseVariableDeclaration(
                consumeSemicolon: true
            ),
            JsTokenKind.Function => ParseFunctionDeclaration(),
            JsTokenKind.If => ParseIfStatement(),
            JsTokenKind.While => ParseWhileStatement(),
            JsTokenKind.Do => ParseDoWhileStatement(),
            JsTokenKind.For => ParseForStatement(),
            JsTokenKind.Break => ParseLoopControl(AstKind.BreakStatement),
            JsTokenKind.Continue => ParseLoopControl(AstKind.ContinueStatement),
            JsTokenKind.Return => ParseReturnStatement(),
            JsTokenKind.Throw => ParseThrowStatement(),
            JsTokenKind.Try => ParseTryStatement(),
            JsTokenKind.Switch => ParseSwitchStatement(),
            JsTokenKind.With or JsTokenKind.Debugger => throw UnsupportedStatement(current.Kind),
            _ => ParseExpressionStatement(),
        };
    }

    private int ParseEmptyStatement(int position)
    {
        Next();
        return Arena.Add(AstKind.EmptyStatement, position: position);
    }

    private int ParseBlock(
        out bool strictDeclared,
        AstKind rootKind = AstKind.BlockStatement,
        bool allowDirectives = false
    )
    {
        var position = Expect(JsTokenKind.LeftBrace).Position;
        Span<int> initial = stackalloc int[8];
        var statements = new NodeList(initial);
        var allowsDirectives = allowDirectives;
        strictDeclared = false;
        try
        {
            while (current.Kind != JsTokenKind.RightBrace)
            {
                if (current.Kind == JsTokenKind.Eof)
                    throw Error("Unterminated block", position);
                var statement = ParseStatement();
                statements.Add(statement);
                if (allowsDirectives && IsUseStrictDirective(statement))
                {
                    strictDeclared = true;
                    strictMode = true;
                }
                else
                    allowsDirectives = false;
            }

            Next();
            var children = Arena.AddChildren(statements.AsSpan());
            return Arena.Add(rootKind, children.Offset, children.Count, position: position);
        }
        finally
        {
            statements.Dispose();
        }
    }

    private int ParseVariableDeclaration(bool consumeSemicolon)
    {
        var position = current.Position;
        var kind = current.Kind switch
        {
            JsTokenKind.Var => JsVariableDeclarationKind.Var,
            JsTokenKind.Let => JsVariableDeclarationKind.Let,
            JsTokenKind.Const => JsVariableDeclarationKind.Const,
            _ => throw Error("Expected variable declaration", current.Position),
        };
        Next();

        Span<int> initial = stackalloc int[4];
        var declarators = new NodeList(initial);
        try
        {
            do
            {
                if (current.Kind is JsTokenKind.LeftBracket or JsTokenKind.LeftBrace)
                {
                    var pattern =
                        current.Kind == JsTokenKind.LeftBracket
                            ? ParseArrayBindingPattern()
                            : ParseObjectBindingPattern();
                    if (!Match(JsTokenKind.Assign))
                        throw Error(
                            "Binding declaration requires initializer",
                            Arena.GetPosition(pattern)
                        );
                    declarators.Add(
                        Arena.Add(
                            AstKind.VariableDeclaratorPattern,
                            pattern,
                            ParseAssignment(allowIn: true),
                            position: Arena.GetPosition(pattern)
                        )
                    );
                }
                else
                {
                    var identifier = ExpectIdentifier();
                    var initializer = -1;
                    if (Match(JsTokenKind.Assign))
                        initializer = ParseAssignment(allowIn: true);
                    else if (kind == JsVariableDeclarationKind.Const)
                        throw Error("Const declaration requires initializer", identifier.Position);
                    declarators.Add(
                        Arena.Add(
                            AstKind.VariableDeclarator,
                            Arena.AddString(GetIdentifierText(identifier)),
                            identifier.IdentifierId,
                            initializer,
                            identifier.Position
                        )
                    );
                }
            } while (Match(JsTokenKind.Comma));

            if (consumeSemicolon)
                ConsumeSemicolon();
            var children = Arena.AddChildren(declarators.AsSpan());
            return Arena.Add(
                AstKind.VariableDeclaration,
                children.Offset,
                children.Count,
                (int)kind,
                position
            );
        }
        finally
        {
            declarators.Dispose();
        }
    }

    private int ParseArrayBindingPattern()
    {
        var position = Expect(JsTokenKind.LeftBracket).Position;
        Span<int> initial = stackalloc int[8];
        var elements = new NodeList(initial);
        try
        {
            while (current.Kind != JsTokenKind.RightBracket)
            {
                if (Match(JsTokenKind.Comma))
                {
                    elements.Add(-1);
                    continue;
                }

                if (current.Kind == JsTokenKind.Ellipsis)
                {
                    var restPosition = current.Position;
                    Next();
                    elements.Add(
                        Arena.Add(
                            AstKind.SpreadElement,
                            ParseBindingTarget(),
                            position: restPosition
                        )
                    );
                    if (current.Kind == JsTokenKind.Comma)
                        throw Error("Rest binding must be the final element", current.Position);
                    break;
                }

                var target = ParseBindingTarget();
                target = ParseBindingDefault(target);
                elements.Add(target);
                if (!Match(JsTokenKind.Comma))
                    break;
            }

            Expect(JsTokenKind.RightBracket);
            var children = Arena.AddChildren(elements.AsSpan());
            return Arena.Add(
                AstKind.ArrayBindingPattern,
                children.Offset,
                children.Count,
                position: position
            );
        }
        finally
        {
            elements.Dispose();
        }
    }

    private int ParseBindingTarget()
    {
        if (current.Kind == JsTokenKind.LeftBracket)
            return ParseArrayBindingPattern();
        if (current.Kind == JsTokenKind.LeftBrace)
            return ParseObjectBindingPattern();
        if (current.Kind != JsTokenKind.Identifier)
            throw Error(
                "Binding target must be an identifier, array pattern, or object pattern",
                current.Position
            );

        var identifier = current;
        Next();
        return Arena.Add(
            AstKind.Identifier,
            Arena.AddString(GetIdentifierText(identifier)),
            identifier.IdentifierId,
            position: identifier.Position
        );
    }

    private int ParseBindingDefault(int target)
    {
        if (!Match(JsTokenKind.Assign))
            return target;
        return Arena.Add(
            AstKind.AssignmentExpression,
            target,
            ParseAssignment(allowIn: true),
            (int)JsAssignmentOperator.Assign,
            Arena.GetPosition(target)
        );
    }

    private int ParseObjectBindingPattern()
    {
        var position = Expect(JsTokenKind.LeftBrace).Position;
        Span<FlatObjectProperty> initial = stackalloc FlatObjectProperty[8];
        var properties = new ObjectPropertyList(initial);
        try
        {
            while (current.Kind != JsTokenKind.RightBrace)
            {
                var propertyPosition = current.Position;
                if (Match(JsTokenKind.Ellipsis))
                {
                    if (current.Kind != JsTokenKind.Identifier)
                        throw Error(
                            "Object rest binding target must be an identifier",
                            current.Position
                        );
                    properties.Add(
                        new FlatObjectProperty(
                            -1,
                            ParseBindingTarget(),
                            propertyPosition,
                            FlatObjectPropertyFlags.Rest
                        )
                    );
                    if (current.Kind == JsTokenKind.Comma)
                        throw Error("Rest binding must be the final property", current.Position);
                    break;
                }

                var computed = Match(JsTokenKind.LeftBracket);
                int key;
                JsToken shorthandToken = default;
                if (computed)
                {
                    key = ParseAssignment(allowIn: true);
                    Expect(JsTokenKind.RightBracket);
                }
                else
                {
                    shorthandToken = current;
                    key = Arena.AddString(GetObjectPropertyName(current));
                    Next();
                }

                int target;
                if (Match(JsTokenKind.Colon))
                    target = ParseBindingTarget();
                else if (!computed && shorthandToken.Kind == JsTokenKind.Identifier)
                    target = Arena.Add(
                        AstKind.Identifier,
                        Arena.AddString(GetIdentifierText(shorthandToken)),
                        shorthandToken.IdentifierId,
                        position: shorthandToken.Position
                    );
                else
                    throw Error("Expected ':' after object binding key", current.Position);

                properties.Add(
                    new FlatObjectProperty(
                        key,
                        ParseBindingDefault(target),
                        propertyPosition,
                        computed ? FlatObjectPropertyFlags.Computed : FlatObjectPropertyFlags.None
                    )
                );
                if (!Match(JsTokenKind.Comma) && current.Kind != JsTokenKind.RightBrace)
                    throw Error("Expected ',' or '}'", current.Position);
            }

            Expect(JsTokenKind.RightBrace);
            var range = ast.AddObjectProperties(properties.AsSpan());
            return Arena.Add(
                AstKind.ObjectBindingPattern,
                range.Offset,
                range.Count,
                position: position
            );
        }
        finally
        {
            properties.Dispose();
        }
    }

    private int ParseFunctionDeclaration() => ParseFunction(isDeclaration: true);

    private int ParseFunctionExpression() => ParseFunction(isDeclaration: false);

    private int ParseFunction(bool isDeclaration)
    {
        var position = Expect(JsTokenKind.Function).Position;
        if (Match(JsTokenKind.Star))
            throw Error("Generator functions are not supported by FlatJavaScriptParser", position);
        var nameId = -1;
        string name;
        if (isDeclaration || current.Kind == JsTokenKind.Identifier)
        {
            var nameToken = ExpectIdentifier();
            name = GetIdentifierText(nameToken);
            nameId = nameToken.IdentifierId;
        }
        else
            name = string.Empty;
        return ParseFunctionTail(isDeclaration, name, nameId, position, isMethod: false);
    }

    private int ParseFunctionTail(
        bool isDeclaration,
        string name,
        int nameId,
        int position,
        bool isMethod
    )
    {
        Expect(JsTokenKind.LeftParen);
        Span<FlatParameter> initialParameters = stackalloc FlatParameter[8];
        var parameterList = new ParameterList(initialParameters);
        ParameterNameTracker boundParameterNames = default;
        var functionLength = 0;
        var seenDefault = false;
        var hasSimpleParameterList = true;
        var hasDuplicateParameters = false;
        var hasRestrictedParameterName = false;
        var restParameterIndex = -1;
        try
        {
            if (current.Kind != JsTokenKind.RightParen)
            {
                while (true)
                {
                    if (Match(JsTokenKind.Ellipsis))
                    {
                        hasSimpleParameterList = false;
                        seenDefault = true;
                        restParameterIndex = parameterList.Count;
                        var restPosition = current.Position;
                        if (current.Kind is JsTokenKind.LeftBracket or JsTokenKind.LeftBrace)
                        {
                            var pattern = ParseBindingTarget();
                            TrackParameterPatternNames(
                                pattern,
                                ref boundParameterNames,
                                ref hasDuplicateParameters,
                                ref hasRestrictedParameterName
                            );
                            parameterList.Add(
                                new FlatParameter(
                                    Arena.AddString(
                                        $"$rest_pattern_{functionDepth}_{restPosition}"
                                    ),
                                    -1,
                                    -1,
                                    pattern,
                                    restPosition,
                                    JsFormalParameterBindingKind.RestPattern
                                )
                            );
                        }
                        else
                        {
                            var parameter = ExpectIdentifier();
                            var parameterName = GetIdentifierText(parameter);
                            TrackParameterName(
                                parameterName,
                                ref boundParameterNames,
                                ref hasDuplicateParameters,
                                ref hasRestrictedParameterName
                            );
                            parameterList.Add(
                                new FlatParameter(
                                    Arena.AddString(parameterName),
                                    parameter.IdentifierId,
                                    -1,
                                    -1,
                                    parameter.Position,
                                    JsFormalParameterBindingKind.Rest
                                )
                            );
                        }

                        if (current.Kind == JsTokenKind.Comma)
                            throw Error("Rest parameter must be last", current.Position);
                        break;
                    }

                    var parameterPosition = current.Position;
                    if (current.Kind is JsTokenKind.LeftBracket or JsTokenKind.LeftBrace)
                    {
                        hasSimpleParameterList = false;
                        var pattern = ParseBindingTarget();
                        TrackParameterPatternNames(
                            pattern,
                            ref boundParameterNames,
                            ref hasDuplicateParameters,
                            ref hasRestrictedParameterName
                        );
                        var initializer = Match(JsTokenKind.Assign)
                            ? ParseAssignment(allowIn: true)
                            : -1;
                        if (initializer >= 0)
                            seenDefault = true;
                        if (!seenDefault)
                            functionLength++;
                        parameterList.Add(
                            new FlatParameter(
                                Arena.AddString(
                                    $"$param_pattern_{functionDepth}_{parameterPosition}"
                                ),
                                -1,
                                initializer,
                                pattern,
                                parameterPosition,
                                JsFormalParameterBindingKind.Pattern
                            )
                        );
                    }
                    else
                    {
                        var parameter = ExpectIdentifier();
                        var parameterName = GetIdentifierText(parameter);
                        TrackParameterName(
                            parameterName,
                            ref boundParameterNames,
                            ref hasDuplicateParameters,
                            ref hasRestrictedParameterName
                        );
                        var initializer = Match(JsTokenKind.Assign)
                            ? ParseAssignment(allowIn: true)
                            : -1;
                        if (initializer >= 0)
                        {
                            hasSimpleParameterList = false;
                            seenDefault = true;
                        }
                        if (!seenDefault)
                            functionLength++;
                        parameterList.Add(
                            new FlatParameter(
                                Arena.AddString(parameterName),
                                parameter.IdentifierId,
                                initializer,
                                -1,
                                parameter.Position,
                                JsFormalParameterBindingKind.Plain
                            )
                        );
                    }

                    if (!Match(JsTokenKind.Comma))
                        break;
                    if (current.Kind == JsTokenKind.RightParen)
                        break;
                }
            }
            Expect(JsTokenKind.RightParen);
            var parameterRange = ast.AddParameters(parameterList.AsSpan());
            var strictBeforeFunction = strictMode;
            var loopDepthBeforeFunction = loopDepth;
            var switchDepthBeforeFunction = switchDepth;
            loopDepth = 0;
            switchDepth = 0;
            functionDepth++;
            int body;
            bool strictDeclared;
            try
            {
                body = ParseBlock(out strictDeclared, AstKind.Program, allowDirectives: true);
            }
            finally
            {
                functionDepth--;
                loopDepth = loopDepthBeforeFunction;
                switchDepth = switchDepthBeforeFunction;
            }
            var effectiveStrict = strictBeforeFunction || strictDeclared;
            strictMode = strictBeforeFunction;
            if (strictDeclared && !hasSimpleParameterList)
                throw Error(
                    "Illegal 'use strict' directive in function with non-simple parameters",
                    position
                );
            if (hasDuplicateParameters && (isMethod || effectiveStrict || !hasSimpleParameterList))
                throw Error("Duplicate parameter name", position);
            if (effectiveStrict && hasRestrictedParameterName)
                throw Error("Unexpected eval or arguments in strict mode", position);
            var functionIndex = ast.AddFunction(
                new FlatFunctionInfo(
                    Arena.AddString(name),
                    nameId,
                    parameterRange.Offset,
                    parameterRange.Count,
                    functionLength,
                    restParameterIndex,
                    effectiveStrict,
                    hasSimpleParameterList,
                    hasDuplicateParameters,
                    position,
                    isMethod
                )
            );
            return Arena.Add(
                isDeclaration ? AstKind.FunctionDeclaration : AstKind.FunctionExpression,
                functionIndex,
                body,
                position: position
            );
        }
        finally
        {
            parameterList.Dispose();
        }
    }

    private void TrackParameterPatternNames(
        int nodeIndex,
        ref ParameterNameTracker names,
        ref bool hasDuplicate,
        ref bool hasRestrictedName
    )
    {
        ref readonly var node = ref Arena[nodeIndex];
        switch (node.Kind)
        {
            case AstKind.Identifier:
                TrackParameterName(
                    Arena.GetString(node.Arg0),
                    ref names,
                    ref hasDuplicate,
                    ref hasRestrictedName
                );
                return;
            case AstKind.AssignmentExpression:
            case AstKind.SpreadElement:
                TrackParameterPatternNames(
                    node.Arg0,
                    ref names,
                    ref hasDuplicate,
                    ref hasRestrictedName
                );
                return;
            case AstKind.ArrayBindingPattern:
                var elements = Arena.ChildRange(node.Arg0, node.Arg1);
                for (var i = 0; i < elements.Length; i++)
                    if (elements[i] >= 0)
                        TrackParameterPatternNames(
                            elements[i],
                            ref names,
                            ref hasDuplicate,
                            ref hasRestrictedName
                        );
                return;
            case AstKind.ObjectBindingPattern:
                var properties = ast.GetObjectProperties(node.Arg0, node.Arg1);
                for (var i = 0; i < properties.Length; i++)
                    TrackParameterPatternNames(
                        properties[i].ValueNode,
                        ref names,
                        ref hasDuplicate,
                        ref hasRestrictedName
                    );
                return;
            default:
                throw Error("Invalid parameter binding pattern", Arena.GetPosition(nodeIndex));
        }
    }

    private static void TrackParameterName(
        string name,
        ref ParameterNameTracker names,
        ref bool hasDuplicate,
        ref bool hasRestrictedName
    )
    {
        if (!names.Add(name))
            hasDuplicate = true;
        if (name is "eval" or "arguments")
            hasRestrictedName = true;
    }

    private int ParseIfStatement()
    {
        var position = Expect(JsTokenKind.If).Position;
        Expect(JsTokenKind.LeftParen);
        var test = ParseExpression();
        Expect(JsTokenKind.RightParen);
        var consequent = ParseStatement();
        var alternate = Match(JsTokenKind.Else) ? ParseStatement() : -1;
        return Arena.Add(AstKind.IfStatement, test, consequent, alternate, position);
    }

    private int ParseWhileStatement()
    {
        var position = Expect(JsTokenKind.While).Position;
        Expect(JsTokenKind.LeftParen);
        var test = ParseExpression();
        Expect(JsTokenKind.RightParen);
        return Arena.Add(AstKind.WhileStatement, test, ParseLoopBody(), position: position);
    }

    private int ParseDoWhileStatement()
    {
        var position = Expect(JsTokenKind.Do).Position;
        var body = ParseLoopBody();
        Expect(JsTokenKind.While);
        Expect(JsTokenKind.LeftParen);
        var test = ParseExpression();
        Expect(JsTokenKind.RightParen);
        ConsumeSemicolon();
        return Arena.Add(AstKind.DoWhileStatement, body, test, position: position);
    }

    private int ParseForStatement()
    {
        var position = Expect(JsTokenKind.For).Position;
        Expect(JsTokenKind.LeftParen);
        var init = -1;
        if (current.Kind != JsTokenKind.Semicolon)
        {
            init = current.Kind is JsTokenKind.Var or JsTokenKind.Let or JsTokenKind.Const
                ? ParseVariableDeclaration(consumeSemicolon: false)
                : ParseExpression(allowIn: false);
        }
        if (current.Kind is JsTokenKind.In or JsTokenKind.Of)
            throw Error("for-in/of is not supported by FlatJavaScriptParser", current.Position);
        Expect(JsTokenKind.Semicolon);
        var test = current.Kind == JsTokenKind.Semicolon ? -1 : ParseExpression();
        Expect(JsTokenKind.Semicolon);
        var update = current.Kind == JsTokenKind.RightParen ? -1 : ParseExpression();
        Expect(JsTokenKind.RightParen);
        Span<int> parts = [init, test, update, ParseLoopBody()];
        var children = Arena.AddChildren(parts);
        return Arena.Add(AstKind.ForStatement, children.Offset, children.Count, position: position);
    }

    private int ParseLoopControl(AstKind kind)
    {
        var position = current.Position;
        if (kind == AstKind.ContinueStatement ? loopDepth == 0 : loopDepth == 0 && switchDepth == 0)
            throw Error($"Illegal {kind}", position);
        Next();
        if (
            !current.HasLineTerminatorBefore
            && current.Kind is JsTokenKind.Identifier or JsTokenKind.ReservedWord
        )
            throw Error("Labeled loop control is not supported by FlatJavaScriptParser", position);
        ConsumeSemicolon();
        return Arena.Add(kind, position: position);
    }

    private int ParseSwitchStatement()
    {
        var position = Expect(JsTokenKind.Switch).Position;
        Expect(JsTokenKind.LeftParen);
        var discriminant = ParseExpression();
        Expect(JsTokenKind.RightParen);
        Expect(JsTokenKind.LeftBrace);
        Span<int> initialCases = stackalloc int[8];
        var cases = new NodeList(initialCases);
        var hasDefault = false;
        switchDepth++;
        try
        {
            Span<int> initialStatements = stackalloc int[4];
            while (current.Kind != JsTokenKind.RightBrace)
            {
                if (current.Kind == JsTokenKind.Eof)
                    throw Error("Unterminated switch statement", position);
                var casePosition = current.Position;
                int test;
                if (Match(JsTokenKind.Case))
                    test = ParseExpression();
                else if (Match(JsTokenKind.Default))
                {
                    if (hasDefault)
                        throw Error(
                            "More than one default clause in switch statement",
                            casePosition
                        );
                    hasDefault = true;
                    test = -1;
                }
                else
                    throw Error("Expected case or default clause", current.Position);
                Expect(JsTokenKind.Colon);

                var statements = new NodeList(initialStatements);
                try
                {
                    while (
                        current.Kind
                            is not (
                                JsTokenKind.Case
                                or JsTokenKind.Default
                                or JsTokenKind.RightBrace
                                or JsTokenKind.Eof
                            )
                    )
                        statements.Add(ParseStatement());
                    var consequent = Arena.AddChildren(statements.AsSpan());
                    cases.Add(
                        Arena.Add(
                            AstKind.SwitchCase,
                            test,
                            consequent.Offset,
                            consequent.Count,
                            casePosition
                        )
                    );
                }
                finally
                {
                    statements.Dispose();
                }
            }
            Next();
            var range = Arena.AddChildren(cases.AsSpan());
            return Arena.Add(
                AstKind.SwitchStatement,
                discriminant,
                range.Offset,
                range.Count,
                position
            );
        }
        finally
        {
            switchDepth--;
            cases.Dispose();
        }
    }

    private int ParseReturnStatement()
    {
        var position = current.Position;
        if (functionDepth == 0)
            throw Error("Illegal return statement", position);
        Next();
        var argument =
            current.HasLineTerminatorBefore
            || current.Kind is JsTokenKind.Semicolon or JsTokenKind.RightBrace or JsTokenKind.Eof
                ? -1
                : ParseExpression();
        ConsumeSemicolon();
        return Arena.Add(AstKind.ReturnStatement, argument, position: position);
    }

    private int ParseThrowStatement()
    {
        var position = Expect(JsTokenKind.Throw).Position;
        if (current.HasLineTerminatorBefore)
            throw Error("Illegal newline after throw", position);
        var argument = ParseExpression();
        ConsumeSemicolon();
        return Arena.Add(AstKind.ThrowStatement, argument, position: position);
    }

    private int ParseTryStatement()
    {
        var position = Expect(JsTokenKind.Try).Position;
        var block = ParseBlock(out _);
        var handler = -1;
        if (current.Kind == JsTokenKind.Catch)
        {
            var catchPosition = current.Position;
            Next();
            var binding = -1;
            if (Match(JsTokenKind.LeftParen))
            {
                binding = ParseBindingTarget();
                ParameterNameTracker names = default;
                var duplicate = false;
                var restricted = false;
                TrackParameterPatternNames(binding, ref names, ref duplicate, ref restricted);
                if (duplicate)
                    throw Error("Duplicate catch binding", catchPosition);
                Expect(JsTokenKind.RightParen);
            }
            var body = ParseBlock(out _);
            handler = Arena.Add(AstKind.CatchClause, binding, body, position: catchPosition);
        }

        var finalizer = -1;
        if (Match(JsTokenKind.Finally))
            finalizer = ParseBlock(out _);
        if (handler < 0 && finalizer < 0)
            throw Error("try statement requires catch or finally", current.Position);
        return Arena.Add(AstKind.TryStatement, block, handler, finalizer, position);
    }

    private int ParseLoopBody()
    {
        loopDepth++;
        try
        {
            return ParseStatement();
        }
        finally
        {
            loopDepth--;
        }
    }

    private int ParseExpressionStatement()
    {
        var position = current.Position;
        var expression = ParseExpression();
        ConsumeSemicolon();
        return Arena.Add(AstKind.ExpressionStatement, expression, position: position);
    }

    private int ParseExpression(bool allowIn = true)
    {
        var position = current.Position;
        var first = ParseAssignment(allowIn);
        if (!Match(JsTokenKind.Comma))
            return first;

        Span<int> initial = stackalloc int[4];
        var expressions = new NodeList(initial);
        try
        {
            expressions.Add(first);
            do
            {
                expressions.Add(ParseAssignment(allowIn));
            } while (Match(JsTokenKind.Comma));
            var children = Arena.AddChildren(expressions.AsSpan());
            return Arena.Add(
                AstKind.SequenceExpression,
                children.Offset,
                children.Count,
                position: position
            );
        }
        finally
        {
            expressions.Dispose();
        }
    }

    private int ParseAssignment(bool allowIn)
    {
        var position = current.Position;
        var left = ParseConditional(allowIn);
        if (!TryGetAssignmentOperator(current.Kind, out var op))
            return left;
        if (
            Arena[left].Kind is not (AstKind.Identifier or AstKind.MemberExpression)
            && (op != JsAssignmentOperator.Assign || !IsDestructuringAssignmentTarget(left))
        )
            throw Error("Invalid assignment target", position);
        Next();
        return Arena.Add(
            AstKind.AssignmentExpression,
            left,
            ParseAssignment(allowIn),
            (int)op,
            position
        );
    }

    private int ParseConditional(bool allowIn)
    {
        var position = current.Position;
        var test = ParseBinary(allowIn, 1);
        if (!Match(JsTokenKind.Question))
            return test;
        var consequent = ParseAssignment(allowIn: true);
        Expect(JsTokenKind.Colon);
        return Arena.Add(
            AstKind.ConditionalExpression,
            test,
            consequent,
            ParseAssignment(allowIn),
            position
        );
    }

    private int ParseBinary(bool allowIn, int minimumPrecedence)
    {
        var left = ParseUnary();
        while (
            TryGetBinaryOperator(
                current.Kind,
                allowIn,
                out var op,
                out var precedence,
                out var rightAssociative
            )
            && precedence >= minimumPrecedence
        )
        {
            var position = Arena.GetPosition(left);
            Next();
            var right = ParseBinary(allowIn, rightAssociative ? precedence : precedence + 1);
            left = Arena.Add(AstKind.BinaryExpression, left, right, (int)op, position);
        }
        return left;
    }

    private int ParseUnary()
    {
        var position = current.Position;
        if (current.Kind == JsTokenKind.New)
            return ParseNewExpression();

        if (TryGetUnaryOperator(current.Kind, out var unary))
        {
            Next();
            var argument = ParseUnary();
            if (
                unary == JsUnaryOperator.Delete
                && strictMode
                && Arena[argument].Kind == AstKind.Identifier
            )
                throw Error("Delete of an unqualified identifier in strict mode", position);
            return Arena.Add(AstKind.UnaryExpression, argument, (int)unary, position: position);
        }

        if (current.Kind is JsTokenKind.PlusPlus or JsTokenKind.MinusMinus)
        {
            JsOperatorTable.TryGetUpdate(current.Kind, out var op);
            Next();
            var argument = ParseUnary();
            EnsureUpdateTarget(argument, position);
            return Arena.Add(AstKind.UpdateExpression, argument, (int)op, 1, position);
        }

        var expression = ParsePostfix();
        if (
            !current.HasLineTerminatorBefore
            && current.Kind is JsTokenKind.PlusPlus or JsTokenKind.MinusMinus
        )
        {
            JsOperatorTable.TryGetUpdate(current.Kind, out var op);
            Next();
            EnsureUpdateTarget(expression, position);
            expression = Arena.Add(AstKind.UpdateExpression, expression, (int)op, 0, position);
        }
        return expression;
    }

    private int ParsePostfix()
    {
        return ParseMemberAndCallSuffix(ParsePrimary(), allowCalls: true);
    }

    private int ParseNewExpression(bool allowCallSuffix = true)
    {
        var position = Expect(JsTokenKind.New).Position;
        var callee =
            current.Kind == JsTokenKind.New
                ? ParseNewExpression(allowCallSuffix: false)
                : ParseMemberAndCallSuffix(ParsePrimary(), allowCalls: false);
        var arguments = Match(JsTokenKind.LeftParen)
            ? ParseArgumentListAfterOpenParen()
            : (Offset: 0, Count: 0);
        var expression = Arena.Add(
            AstKind.NewExpression,
            callee,
            arguments.Offset,
            arguments.Count,
            position
        );
        return ParseMemberAndCallSuffix(expression, allowCallSuffix);
    }

    private int ParseMemberAndCallSuffix(int expression, bool allowCalls)
    {
        while (true)
        {
            var position = Arena.GetPosition(expression);
            if (Match(JsTokenKind.Dot))
            {
                if (!JsTokenFacts.IsIdentifierName(current.Kind))
                    throw Error($"Expected Identifier but found {current.Kind}", current.Position);
                var property = current;
                Next();
                expression = Arena.Add(
                    AstKind.MemberExpression,
                    expression,
                    Arena.AddString(GetIdentifierText(property)),
                    (int)AstMemberFlags.None,
                    position
                );
                continue;
            }

            if (Match(JsTokenKind.LeftBracket))
            {
                var property = ParseExpression();
                Expect(JsTokenKind.RightBracket);
                expression = Arena.Add(
                    AstKind.MemberExpression,
                    expression,
                    property,
                    (int)AstMemberFlags.Computed,
                    position
                );
                continue;
            }

            if (allowCalls && Match(JsTokenKind.LeftParen))
            {
                expression = ParseCallArguments(expression, position);
                continue;
            }

            return expression;
        }
    }

    private int ParseCallArguments(int callee, int position)
    {
        var children = ParseArgumentListAfterOpenParen();
        return Arena.Add(AstKind.CallExpression, callee, children.Offset, children.Count, position);
    }

    private (int Offset, int Count) ParseArgumentListAfterOpenParen()
    {
        Span<int> initial = stackalloc int[4];
        var arguments = new NodeList(initial);
        try
        {
            while (current.Kind != JsTokenKind.RightParen)
            {
                if (current.Kind == JsTokenKind.Ellipsis)
                {
                    var position = current.Position;
                    Next();
                    arguments.Add(
                        Arena.Add(
                            AstKind.SpreadElement,
                            ParseAssignment(allowIn: true),
                            position: position
                        )
                    );
                }
                else
                    arguments.Add(ParseAssignment(allowIn: true));
                if (!Match(JsTokenKind.Comma))
                    break;
            }
            Expect(JsTokenKind.RightParen);
            return Arena.AddChildren(arguments.AsSpan());
        }
        finally
        {
            arguments.Dispose();
        }
    }

    private int ParsePrimary()
    {
        var token = current;
        switch (token.Kind)
        {
            case JsTokenKind.Identifier:
                Next();
                return Arena.Add(
                    AstKind.Identifier,
                    Arena.AddString(GetIdentifierText(token)),
                    token.IdentifierId,
                    position: token.Position
                );
            case JsTokenKind.Number:
                Next();
                return Arena.Add(
                    AstKind.NumericLiteral,
                    Arena.AddNumber(token.NumberLiteral),
                    position: token.Position
                );
            case JsTokenKind.String:
                Next();
                return Arena.Add(
                    AstKind.StringLiteral,
                    Arena.AddString(lexer.GetStringLiteral(token)),
                    position: token.Position
                );
            case JsTokenKind.True:
            case JsTokenKind.False:
                Next();
                return Arena.Add(
                    AstKind.BooleanLiteral,
                    token.Kind == JsTokenKind.True ? 1 : 0,
                    position: token.Position
                );
            case JsTokenKind.Null:
                Next();
                return Arena.Add(AstKind.NullLiteral, position: token.Position);
            case JsTokenKind.This:
                Next();
                return Arena.Add(AstKind.ThisExpression, position: token.Position);
            case JsTokenKind.LeftParen:
                Next();
                var expression = ParseExpression();
                Expect(JsTokenKind.RightParen);
                return expression;
            case JsTokenKind.LeftBracket:
                return ParseArrayLiteral();
            case JsTokenKind.LeftBrace:
                return ParseObjectLiteral();
            case JsTokenKind.Function:
                return ParseFunctionExpression();
            default:
                throw Error(
                    $"Expression token '{token.Kind}' is not supported by FlatJavaScriptParser",
                    token.Position
                );
        }
    }

    private int ParseArrayLiteral()
    {
        var position = Expect(JsTokenKind.LeftBracket).Position;
        Span<int> initial = stackalloc int[8];
        var elements = new NodeList(initial);
        try
        {
            while (current.Kind != JsTokenKind.RightBracket)
            {
                if (Match(JsTokenKind.Comma))
                {
                    elements.Add(-1);
                    continue;
                }
                if (current.Kind == JsTokenKind.Ellipsis)
                {
                    var spreadPosition = current.Position;
                    Next();
                    elements.Add(
                        Arena.Add(
                            AstKind.SpreadElement,
                            ParseAssignment(allowIn: true),
                            position: spreadPosition
                        )
                    );
                }
                else
                    elements.Add(ParseAssignment(allowIn: true));
                if (!Match(JsTokenKind.Comma) && current.Kind != JsTokenKind.RightBracket)
                    throw Error("Expected ',' or ']'", current.Position);
            }
            Next();
            var children = Arena.AddChildren(elements.AsSpan());
            return Arena.Add(
                AstKind.ArrayExpression,
                children.Offset,
                children.Count,
                position: position
            );
        }
        finally
        {
            elements.Dispose();
        }
    }

    private int ParseObjectLiteral()
    {
        var position = Expect(JsTokenKind.LeftBrace).Position;
        Span<FlatObjectProperty> initial = stackalloc FlatObjectProperty[8];
        var properties = new ObjectPropertyList(initial);
        try
        {
            while (current.Kind != JsTokenKind.RightBrace)
            {
                var propertyPosition = current.Position;
                if (Match(JsTokenKind.Ellipsis))
                {
                    properties.Add(
                        new FlatObjectProperty(
                            -1,
                            ParseAssignment(allowIn: true),
                            propertyPosition,
                            FlatObjectPropertyFlags.Rest
                        )
                    );
                    if (!Match(JsTokenKind.Comma) && current.Kind != JsTokenKind.RightBrace)
                        throw Error("Expected ',' or '}'", current.Position);
                    continue;
                }

                var computed = Match(JsTokenKind.LeftBracket);
                int key;
                JsToken shorthandToken = default;
                if (computed)
                {
                    key = ParseAssignment(allowIn: true);
                    Expect(JsTokenKind.RightBracket);
                }
                else
                {
                    shorthandToken = current;
                    key = Arena.AddString(GetObjectPropertyName(current));
                    Next();
                }

                var staticName = computed ? null : ast.GetString(key);
                if (
                    staticName is "get" or "set"
                    && current.Kind
                        is not (
                            JsTokenKind.LeftParen
                            or JsTokenKind.Colon
                            or JsTokenKind.Comma
                            or JsTokenKind.RightBrace
                            or JsTokenKind.Assign
                        )
                )
                {
                    var isGetter = staticName == "get";
                    computed = Match(JsTokenKind.LeftBracket);
                    if (computed)
                    {
                        key = ParseAssignment(allowIn: true);
                        Expect(JsTokenKind.RightBracket);
                    }
                    else
                    {
                        key = Arena.AddString(GetObjectPropertyName(current));
                        Next();
                    }

                    var accessor = ParseFunctionTail(
                        isDeclaration: false,
                        string.Empty,
                        -1,
                        propertyPosition,
                        isMethod: true
                    );
                    var accessorFunction = ast.GetFunction(Arena[accessor].Arg0);
                    if (isGetter && accessorFunction.ParameterCount != 0)
                        throw Error("Getter must not have parameters", propertyPosition);
                    if (
                        !isGetter
                        && (
                            accessorFunction.ParameterCount != 1
                            || accessorFunction.RestParameterIndex >= 0
                        )
                    )
                        throw Error("Expected setter parameter", propertyPosition);

                    var accessorFlags = computed
                        ? FlatObjectPropertyFlags.Computed
                        : FlatObjectPropertyFlags.None;
                    accessorFlags |= isGetter
                        ? FlatObjectPropertyFlags.Getter
                        : FlatObjectPropertyFlags.Setter;
                    properties.Add(
                        new FlatObjectProperty(key, accessor, propertyPosition, accessorFlags)
                    );
                    if (!Match(JsTokenKind.Comma) && current.Kind != JsTokenKind.RightBrace)
                        throw Error("Expected ',' or '}'", current.Position);
                    continue;
                }

                if (current.Kind == JsTokenKind.LeftParen)
                {
                    var method = ParseFunctionTail(
                        isDeclaration: false,
                        string.Empty,
                        -1,
                        propertyPosition,
                        isMethod: true
                    );
                    properties.Add(
                        new FlatObjectProperty(
                            key,
                            method,
                            propertyPosition,
                            computed
                                ? FlatObjectPropertyFlags.Computed
                                : FlatObjectPropertyFlags.None
                        )
                    );
                    if (!Match(JsTokenKind.Comma) && current.Kind != JsTokenKind.RightBrace)
                        throw Error("Expected ',' or '}'", current.Position);
                    continue;
                }

                int value;
                var flags = computed
                    ? FlatObjectPropertyFlags.Computed
                    : FlatObjectPropertyFlags.None;
                if (Match(JsTokenKind.Colon))
                    value = ParseAssignment(allowIn: true);
                else if (
                    !computed
                    && shorthandToken.Kind == JsTokenKind.Identifier
                    && current.Kind != JsTokenKind.LeftParen
                )
                {
                    value = Arena.Add(
                        AstKind.Identifier,
                        Arena.AddString(GetIdentifierText(shorthandToken)),
                        shorthandToken.IdentifierId,
                        position: shorthandToken.Position
                    );
                    if (Match(JsTokenKind.Assign))
                    {
                        flags |= FlatObjectPropertyFlags.CoverInitializedName;
                        value = Arena.Add(
                            AstKind.AssignmentExpression,
                            value,
                            ParseAssignment(allowIn: true),
                            (int)JsAssignmentOperator.Assign,
                            shorthandToken.Position
                        );
                    }
                }
                else
                    throw Error(
                        "Object methods and accessors are not supported by FlatJavaScriptParser",
                        current.Position
                    );

                properties.Add(new FlatObjectProperty(key, value, propertyPosition, flags));
                if (!Match(JsTokenKind.Comma) && current.Kind != JsTokenKind.RightBrace)
                    throw Error("Expected ',' or '}'", current.Position);
            }
            Next();
            var range = ast.AddObjectProperties(properties.AsSpan());
            return Arena.Add(
                AstKind.ObjectExpression,
                range.Offset,
                range.Count,
                position: position
            );
        }
        finally
        {
            properties.Dispose();
        }
    }

    private bool IsDestructuringAssignmentTarget(int nodeIndex)
    {
        ref readonly var node = ref Arena[nodeIndex];
        switch (node.Kind)
        {
            case AstKind.Identifier:
            case AstKind.MemberExpression:
                return true;
            case AstKind.AssignmentExpression:
                return (JsAssignmentOperator)node.Arg2 == JsAssignmentOperator.Assign
                    && IsDestructuringAssignmentTarget(node.Arg0);
            case AstKind.SpreadElement:
                return Arena[node.Arg0].Kind is AstKind.Identifier or AstKind.MemberExpression;
            case AstKind.ArrayExpression:
                var elements = Arena.ChildRange(node.Arg0, node.Arg1);
                for (var i = 0; i < elements.Length; i++)
                {
                    if (elements[i] < 0)
                        continue;
                    if (!IsDestructuringAssignmentTarget(elements[i]))
                        return false;
                    if (
                        Arena[elements[i]].Kind == AstKind.SpreadElement
                        && i != elements.Length - 1
                    )
                        return false;
                }
                return true;
            case AstKind.ObjectExpression:
                var properties = ast.GetObjectProperties(node.Arg0, node.Arg1);
                for (var i = 0; i < properties.Length; i++)
                {
                    if (
                        properties[i].IsRest
                        && Arena[properties[i].ValueNode].Kind
                            is not (AstKind.Identifier or AstKind.MemberExpression)
                    )
                        return false;
                    if (!IsDestructuringAssignmentTarget(properties[i].ValueNode))
                        return false;
                    if (properties[i].IsRest && i != properties.Length - 1)
                        return false;
                }
                return true;
            default:
                return false;
        }
    }

    private string GetObjectPropertyName(in JsToken token)
    {
        if (JsTokenFacts.IsIdentifierName(token.Kind))
            return GetIdentifierText(token);
        return token.Kind switch
        {
            JsTokenKind.String => lexer.GetStringLiteral(token),
            JsTokenKind.Number => JsValue.NumberToJsString(token.NumberLiteral),
            JsTokenKind.BigInt => lexer.GetBigIntLiteral(token).Value.ToString(),
            _ => throw Error("Expected object property name", token.Position),
        };
    }

    private bool IsUseStrictDirective(int statement)
    {
        ref readonly var node = ref Arena[statement];
        if (node.Kind != AstKind.ExpressionStatement)
            return false;
        ref readonly var expression = ref Arena[node.Arg0];
        return expression.Kind == AstKind.StringLiteral
            && string.Equals(
                Arena.GetString(expression.Arg0),
                "use strict",
                StringComparison.Ordinal
            );
    }

    private void EnsureUpdateTarget(int node, int position)
    {
        if (Arena[node].Kind is not (AstKind.Identifier or AstKind.MemberExpression))
            throw Error("Invalid update target", position);
    }

    private string GetIdentifierText(in JsToken token)
    {
        return token.DataIndex >= 0
            ? lexer.GetIdentifierLiteral(token)
            : source.Substring(token.Position, token.SourceLength);
    }

    private JsToken ExpectIdentifier()
    {
        if (current.Kind != JsTokenKind.Identifier)
            throw Error($"Expected identifier but found {current.Kind}", current.Position);
        var token = current;
        Next();
        return token;
    }

    private JsToken Expect(JsTokenKind kind)
    {
        if (current.Kind != kind)
            throw Error($"Expected {kind} but found {current.Kind}", current.Position);
        var token = current;
        Next();
        return token;
    }

    private bool Match(JsTokenKind kind)
    {
        if (current.Kind != kind)
            return false;
        Next();
        return true;
    }

    private void ConsumeSemicolon()
    {
        if (Match(JsTokenKind.Semicolon))
            return;
        if (
            current.Kind is JsTokenKind.RightBrace or JsTokenKind.Eof
            || current.HasLineTerminatorBefore
        )
            return;
        throw Error("Expected semicolon", current.Position);
    }

    private void Next()
    {
        current = lexer.NextToken();
    }

    private JsParseException UnsupportedStatement(JsTokenKind kind)
    {
        return Error(
            $"Statement '{kind}' is not supported by FlatJavaScriptParser",
            current.Position
        );
    }

    private JsParseException Error(string message, int position)
    {
        return new JsParseException(message, position, source);
    }

    private static bool TryGetAssignmentOperator(JsTokenKind kind, out JsAssignmentOperator op)
    {
        return JsOperatorTable.TryGetAssignment(kind, out op);
    }

    private static bool TryGetUnaryOperator(JsTokenKind kind, out JsUnaryOperator op)
    {
        return JsOperatorTable.TryGetUnary(kind, out op);
    }

    private static bool TryGetBinaryOperator(
        JsTokenKind kind,
        bool allowIn,
        out JsBinaryOperator op,
        out int precedence,
        out bool rightAssociative
    )
    {
        if (kind == JsTokenKind.NullishCoalescing)
        {
            op = JsBinaryOperator.NullishCoalescing;
            precedence = 1;
            rightAssociative = false;
            return true;
        }

        if (!JsOperatorTable.TryGetBinary(kind, allowIn, out var info))
        {
            op = default;
            precedence = 0;
            rightAssociative = false;
            return false;
        }

        op = info.Operator;
        precedence = info.Precedence;
        rightAssociative = info.IsRightAssociative;
        return true;
    }

    private struct ParameterNameTracker
    {
        private string? first;
        private string? second;
        private HashSet<string>? names;

        public bool Add(string name)
        {
            if (first is null)
            {
                first = name;
                return true;
            }
            if (string.Equals(first, name, StringComparison.Ordinal))
                return false;
            if (second is null)
            {
                second = name;
                return true;
            }
            if (string.Equals(second, name, StringComparison.Ordinal))
                return false;
            names ??= new HashSet<string>(StringComparer.Ordinal) { first, second };
            return names.Add(name);
        }
    }

    private ref struct NodeList
    {
        private Span<int> buffer;
        private int[]? rented;

        public NodeList(Span<int> initialBuffer)
        {
            buffer = initialBuffer;
        }

        public int Count { get; private set; }

        public void Add(int node)
        {
            if (Count == buffer.Length)
                Grow();
            buffer[Count++] = node;
        }

        public ReadOnlySpan<int> AsSpan() => buffer[..Count];

        public void Dispose()
        {
            if (rented is not null)
                ArrayPool<int>.Shared.Return(rented);
            rented = null;
            buffer = [];
            Count = 0;
        }

        private void Grow()
        {
            var next = ArrayPool<int>.Shared.Rent(Math.Max(8, buffer.Length * 2));
            buffer.CopyTo(next);
            if (rented is not null)
                ArrayPool<int>.Shared.Return(rented);
            rented = next;
            buffer = next;
        }
    }

    private ref struct ParameterList
    {
        private Span<FlatParameter> buffer;
        private FlatParameter[]? rented;

        public ParameterList(Span<FlatParameter> initialBuffer)
        {
            buffer = initialBuffer;
        }

        public int Count { get; private set; }

        public void Add(FlatParameter parameter)
        {
            if (Count == buffer.Length)
                Grow();
            buffer[Count++] = parameter;
        }

        public ReadOnlySpan<FlatParameter> AsSpan() => buffer[..Count];

        public void Dispose()
        {
            if (rented is not null)
                ArrayPool<FlatParameter>.Shared.Return(rented);
            rented = null;
            buffer = [];
            Count = 0;
        }

        private void Grow()
        {
            var next = ArrayPool<FlatParameter>.Shared.Rent(Math.Max(8, buffer.Length * 2));
            buffer.CopyTo(next);
            if (rented is not null)
                ArrayPool<FlatParameter>.Shared.Return(rented);
            rented = next;
            buffer = next;
        }
    }

    private ref struct ObjectPropertyList
    {
        private Span<FlatObjectProperty> buffer;
        private FlatObjectProperty[]? rented;

        public ObjectPropertyList(Span<FlatObjectProperty> initialBuffer)
        {
            buffer = initialBuffer;
        }

        public int Count { get; private set; }

        public void Add(FlatObjectProperty property)
        {
            if (Count == buffer.Length)
                Grow();
            buffer[Count++] = property;
        }

        public ReadOnlySpan<FlatObjectProperty> AsSpan() => buffer[..Count];

        public void Dispose()
        {
            if (rented is not null)
                ArrayPool<FlatObjectProperty>.Shared.Return(rented);
            rented = null;
            buffer = [];
            Count = 0;
        }

        private void Grow()
        {
            var next = ArrayPool<FlatObjectProperty>.Shared.Rent(Math.Max(8, buffer.Length * 2));
            buffer.CopyTo(next);
            if (rented is not null)
                ArrayPool<FlatObjectProperty>.Shared.Return(rented);
            rented = next;
            buffer = next;
        }
    }
}
