using System.Text;

namespace Okojo.Parsing;

public static class JavaScriptParser
{
    public static JsProgram ParseScript(string source, string? sourcePath = null)
    {
        return new JsParser(source, sourcePath).ParseProgram();
    }

    public static JsProgram ParseScript(
        string source,
        string? sourcePath,
        int basePosition,
        string? locationSource)
    {
        return new JsParser(
            source,
            false,
            false,
            false,
            false,
            sourcePath,
            basePosition,
            locationSource).ParseProgram();
    }

    public static JsProgram ParseScript(string source, bool allowSuperProperty, bool allowSuperCall,
        string? sourcePath = null)
    {
        return new JsParser(source, allowSuperProperty, allowSuperCall, sourcePath).ParseProgram();
    }

    public static JsProgram ParseScript(string source, bool allowSuperProperty, bool allowSuperCall,
        bool allowTopLevelAwait, string? sourcePath = null)
    {
        return new JsParser(source, allowSuperProperty, allowSuperCall, allowTopLevelAwait, sourcePath: sourcePath)
            .ParseProgram();
    }

    public static JsProgram ParseModule(string source, string? sourcePath = null)
    {
        return new JsParser(source, false, false, true,
            true, sourcePath).ParseProgram();
    }
}

internal sealed partial class JsParser
{
    private static readonly HashSet<string> StrictModeReservedWords = new(StringComparer.Ordinal)
    {
        "implements",
        "interface",
        "package",
        "private",
        "protected",
        "public",
        "static",
        "yield",
        "eval",
        "arguments"
    };

    private readonly bool allowTopLevelAwait;
    private readonly int basePosition;
    private readonly bool isModule;
    private readonly JsLexer lexer;
    private readonly string? locationSource;

    private readonly HashSet<int> nameCrashIdSet = new();
    private readonly HashSet<string> nameCrashTextSet = new(StringComparer.Ordinal);
    private readonly string source;
    private readonly string? sourcePath;
    private bool allowSuperCall;
    private bool allowSuperProperty;
    private int argumentsIdentifierId = -1;
    private int asyncFunctionLevel;
    private JsToken current;
    private int evalIdentifierId = -1;
    private int generatorFunctionLevel;
    private bool hasPeek;
    private int level;
    private JsToken peek;
    private bool sawTopLevelAwait;
    private bool strictMode;

    public JsParser(string source, string? sourcePath = null)
        : this(source, false, false, sourcePath)
    {
    }

    public JsParser(string source, bool allowSuperProperty, bool allowSuperCall, string? sourcePath = null)
        : this(source, allowSuperProperty, allowSuperCall, false, sourcePath: sourcePath)
    {
    }

    public JsParser(string source, bool allowSuperProperty, bool allowSuperCall, bool allowTopLevelAwait,
        bool isModule = false, string? sourcePath = null, int basePosition = 0, string? locationSource = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.locationSource = locationSource ?? source;
        this.sourcePath = sourcePath;
        this.basePosition = basePosition;
        lexer = new(source);
        current = lexer.NextToken();
        this.allowSuperProperty = allowSuperProperty;
        this.allowSuperCall = allowSuperCall;
        this.allowTopLevelAwait = allowTopLevelAwait;
        this.isModule = isModule;
        if (this.isModule)
            strictMode = true;
    }

    public JsProgram ParseProgram()
    {
        var start = current.Position;
        var statements = new List<JsStatement>(8);
        var lexicalNames = new HashSet<string>(StringComparer.Ordinal);
        var allowsDirectives = true;
        while (current.Kind != JsTokenKind.Eof)
        {
            var statement = isModule && level == 0 ? ParseModuleItem() : ParseStatement();
            RegisterDirectLexicalDeclarations(statement, lexicalNames);
            if (allowsDirectives)
            {
                if (statement is JsExpressionStatement { Expression: JsLiteralExpression { Text: var text } })
                {
                    if (text is "\"use strict\"" or "\'use strict\'") strictMode = true;
                }
                else
                {
                    allowsDirectives = false;
                }
            }

            statements.Add(statement);
        }

        return At(new JsProgram(statements, strictMode, lexicalNames.ToArray(), sawTopLevelAwait, source, sourcePath,
            lexer.IdentifierTable), start);
    }

    private JsStatement ParseModuleItem()
    {
        if (current.Kind == JsTokenKind.ReservedWord && CurrentSourceTextEquals("import") &&
            Peek().Kind is not (JsTokenKind.LeftParen or JsTokenKind.Dot))
            return ParseImportDeclaration();
        if (current.Kind == JsTokenKind.ReservedWord && CurrentSourceTextEquals("export"))
            return ParseExportDeclaration();
        return ParseStatement();
    }

    private JsStatement ParseStatement()
    {
        var start = current.Position;
        if (current.Kind == JsTokenKind.Identifier &&
            CurrentSourceTextEquals("using") &&
            Peek().Kind == JsTokenKind.Identifier)
            return At(ParseUsingDeclarationStatement(), start);

        if (current.Kind == JsTokenKind.At)
        {
            var decorators = ParseDecoratorList();
            if (current.Kind == JsTokenKind.ReservedWord && CurrentSourceTextEquals("class"))
                return At(ParseClassDeclaration(decorators, start), start);
            throw Error("Expected class declaration", current.Position);
        }

        if (current.Kind == JsTokenKind.ReservedWord && CurrentSourceTextEquals("class"))
            return At(ParseClassDeclaration(), start);

        if (current.Kind == JsTokenKind.Identifier &&
            CurrentSourceTextEquals("async") &&
            Peek().Kind == JsTokenKind.Function &&
            !Peek().HasLineTerminatorBefore)
            return At(ParseFunctionDeclaration(true), start);

        if (current.Kind == JsTokenKind.Identifier && Peek().Kind == JsTokenKind.Colon)
            return At(ParseLabeledStatement(), start);

        return At(current.Kind switch
        {
            JsTokenKind.Semicolon => ParseEmptyStatement(),
            JsTokenKind.LeftBrace => ParseBlockStatement(false),
            JsTokenKind.Var or JsTokenKind.Const => Peek().Kind is JsTokenKind.LeftBrace or JsTokenKind.LeftBracket
                ? ParseBindingDeclarationStatement(true)
                : ParseVariableDeclarationStatement(true),
            JsTokenKind.Let => ShouldParseLetDeclarationStatement()
                ? Peek().Kind is JsTokenKind.LeftBrace or JsTokenKind.LeftBracket
                    ? Peek().Kind == JsTokenKind.LeftBrace && Peek().HasLineTerminatorBefore
                        ? ParseExpressionStatement()
                        : ParseBindingDeclarationStatement(true)
                    : ParseVariableDeclarationStatement(true)
                : ParseExpressionStatement(),
            JsTokenKind.If => ParseIfStatement(),
            JsTokenKind.Return => ParseReturnStatement(),
            JsTokenKind.Function => ParseFunctionDeclaration(),
            JsTokenKind.While => ParseWhileStatement(),
            JsTokenKind.Do => ParseDoWhileStatement(),
            JsTokenKind.For => ParseForStatement(),
            JsTokenKind.Break => ParseBreakStatement(),
            JsTokenKind.Continue => ParseContinueStatement(),
            JsTokenKind.Debugger => ParseDebuggerStatement(),
            JsTokenKind.Throw => ParseThrowStatement(),
            JsTokenKind.Switch => ParseSwitchStatement(),
            JsTokenKind.Try => ParseTryStatement(),
            JsTokenKind.With => ParseWithStatement(),
            _ => ParseExpressionStatement()
        }, start);
    }

    private JsVariableDeclarationStatement ParseUsingDeclarationStatement()
    {
        var start = current.Position;
        // Treat `using` declaration as lexical declaration for parser compatibility.
        // Full explicit-resource-management semantics are handled separately.
        Next(); // consume `using` identifier token

        var declarators = new List<JsVariableDeclarator>();
        do
        {
            var identifier = ParseCheckedIdentifierName(Expect(JsTokenKind.Identifier));
            JsExpression? initializer = null;
            if (Match(JsTokenKind.Assign)) initializer = ParseAssignment(true);

            declarators.Add(At(new JsVariableDeclarator(identifier.Name, initializer, identifier.NameId),
                identifier.Position));
        } while (Match(JsTokenKind.Comma));

        ConsumeOptionalSemicolon();
        return At(new JsVariableDeclarationStatement(JsVariableDeclarationKind.Let, declarators), start);
    }

    private JsEmptyStatement ParseEmptyStatement()
    {
        var start = current.Position;
        Expect(JsTokenKind.Semicolon);
        return At(new JsEmptyStatement(), start);
    }

    private JsBlockStatement ParseBlockStatement(bool isFunctionBody)
    {
        var strictBeforeBlock = strictMode;
        if (isFunctionBody)
            level++;
        var nestedFunctionTrackingMarker = BeginNestedFunctionSyntaxTracking();
        var start = current.Position;
        Expect(JsTokenKind.LeftBrace);

        var allowsDirectives = isFunctionBody;
        var statements = new List<JsStatement>();
        var lexicalNames = new HashSet<string>(StringComparer.Ordinal);
        while (current.Kind != JsTokenKind.RightBrace && current.Kind != JsTokenKind.Eof)
        {
            var statement = ParseStatement();
            RegisterDirectLexicalDeclarations(statement, lexicalNames);
            if (allowsDirectives)
            {
                if (statement is JsExpressionStatement { Expression: JsLiteralExpression { Text: var text } })
                {
                    if (text is "\"use strict\"" or "\'use strict\'") strictMode = true;
                }
                else
                {
                    allowsDirectives = false;
                }
            }

            statements.Add(statement);
        }

        var endPosition = current.Position + 1;
        Expect(JsTokenKind.RightBrace);
        if (isFunctionBody)
            level--;
        var bodyMayCreateNestedFunction = EndNestedFunctionSyntaxTracking(nestedFunctionTrackingMarker);
        var block = At(new JsBlockStatement(
            statements,
            !strictBeforeBlock && strictMode,
            bodyMayCreateNestedFunction), start);
        block.EndPosition = endPosition;
        return block;
    }

    private JsVariableDeclarationStatement ParseVariableDeclarationStatement(bool requireSemicolon,
        bool allowConstWithoutInitializer = false, bool allowInInitializer = true)
    {
        var start = current.Position;
        var kind = current.Kind switch
        {
            JsTokenKind.Var => JsVariableDeclarationKind.Var,
            JsTokenKind.Let => JsVariableDeclarationKind.Let,
            JsTokenKind.Const => JsVariableDeclarationKind.Const,
            _ => throw Error("Expected variable declaration", current.Position)
        };
        Next();

        var declarators = new List<JsVariableDeclarator>();
        var lexicalDeclaratorIds =
            kind is JsVariableDeclarationKind.Let or JsVariableDeclarationKind.Const
                ? new HashSet<int>()
                : null;
        var lexicalDeclaratorNames =
            kind is JsVariableDeclarationKind.Let or JsVariableDeclarationKind.Const
                ? new HashSet<string>(StringComparer.Ordinal)
                : null;
        do
        {
            var nameTok =
                ParseBindingIdentifierToken(
                    kind == JsVariableDeclarationKind.Var && !strictMode);
            var identifier = ParseCheckedIdentifierName(nameTok);
            if (!TryAddIdentifierKey(lexicalDeclaratorIds, lexicalDeclaratorNames, identifier.NameId, identifier.Name))
                throw Error($"Unexpected identifier '{identifier.Name}'", nameTok.Position);

            JsExpression? initializer = null;
            if (Match(JsTokenKind.Assign))
                // var/let/const declarator initializer is AssignmentExpression (not full Expression),
                // so comma separates declarators instead of becoming sequence inside one initializer.
                initializer = ParseAssignment(allowInInitializer);

            if (kind == JsVariableDeclarationKind.Const && initializer is null && !allowConstWithoutInitializer)
                throw Error("const declaration requires initializer", nameTok.Position);

            declarators.Add(At(new JsVariableDeclarator(identifier.Name, initializer, identifier.NameId),
                nameTok.Position));
        } while (Match(JsTokenKind.Comma));

        if (requireSemicolon) ConsumeOptionalSemicolon();

        return At(new JsVariableDeclarationStatement(kind, declarators), start);
    }

    private JsStatement ParseBindingDeclarationStatement(bool requireSemicolon)
    {
        var start = current.Position;
        var kind = current.Kind switch
        {
            JsTokenKind.Var => JsVariableDeclarationKind.Var,
            JsTokenKind.Let => JsVariableDeclarationKind.Let,
            JsTokenKind.Const => JsVariableDeclarationKind.Const,
            _ => throw Error("Expected variable declaration", current.Position)
        };

        Next();
        JsExpression pattern = current.Kind switch
        {
            JsTokenKind.LeftBrace => ParseObjectBindingPattern(),
            JsTokenKind.LeftBracket => ParseArrayPatternExpression(),
            _ => throw Error("Expected binding pattern", current.Position)
        };
        Expect(JsTokenKind.Assign);
        var initializer = ParseAssignment(true);

        if (requireSemicolon) ConsumeOptionalSemicolon();

        var declarators = CollectBindingPatternDeclarators(pattern, kind);
        return At(new JsVariableDeclarationStatement(kind, declarators, pattern, initializer), start);
    }

    private JsToken ParseBindingIdentifierToken(bool allowKeywordLetToken = false)
    {
        if (current.Kind == JsTokenKind.Identifier ||
            current.Kind == JsTokenKind.Of ||
            (allowKeywordLetToken && current.Kind == JsTokenKind.Let))
        {
            var tok = current;
            Next();
            return tok;
        }

        throw Error($"Expected Identifier but found {current.Kind}", current.Position);
    }

    private void RegisterDirectLexicalDeclarations(JsStatement statement, HashSet<string> lexicalNames)
    {
        if (statement is JsExportDeclarationStatement exportDecl)
        {
            RegisterDirectLexicalDeclarations(exportDecl.Declaration, lexicalNames);
            return;
        }

        if (statement is JsClassDeclaration classDeclaration)
        {
            if (!lexicalNames.Add(classDeclaration.Name))
                throw Error($"Unexpected identifier '{classDeclaration.Name}'", classDeclaration.Position);

            return;
        }

        if (statement is not JsVariableDeclarationStatement decl ||
            decl.Kind is not (JsVariableDeclarationKind.Let or JsVariableDeclarationKind.Const))
            return;

        for (var i = 0; i < decl.Declarators.Count; i++)
        {
            var declarator = decl.Declarators[i];
            if (!lexicalNames.Add(declarator.Name))
                throw Error($"Unexpected identifier '{declarator.Name}'", declarator.Position);
        }
    }

    private JsImportDeclaration ParseImportDeclaration()
    {
        var start = current.Position;
        Expect(JsTokenKind.ReservedWord); // import

        if (current.Kind == JsTokenKind.String)
        {
            var sourceToken = current;
            Next();
            var attributes = ParseOptionalImportAttributesClause();
            ConsumeOptionalSemicolon();
            return At(new JsImportDeclaration(null, null, Array.Empty<JsImportSpecifier>(),
                GetStringLiteralText(sourceToken), true, attributes), start);
        }

        string? defaultBinding = null;
        string? namespaceBinding = null;
        var named = new List<JsImportSpecifier>(4);

        if (current.Kind == JsTokenKind.Identifier)
        {
            defaultBinding = ExpectCheckedIdentifierName().Name;
            if (Match(JsTokenKind.Comma))
            {
                if (Match(JsTokenKind.Star))
                {
                    Expect(JsTokenKind.Identifier); // as
                    namespaceBinding = ExpectCheckedIdentifierName().Name;
                }
                else if (Match(JsTokenKind.LeftBrace))
                {
                    ParseImportSpecifierList(named);
                    Expect(JsTokenKind.RightBrace);
                }
                else
                {
                    throw Error("Unsupported import clause", current.Position);
                }
            }
        }
        else if (Match(JsTokenKind.Star))
        {
            Expect(JsTokenKind.Identifier); // as
            namespaceBinding = ExpectCheckedIdentifierName().Name;
        }
        else if (Match(JsTokenKind.LeftBrace))
        {
            ParseImportSpecifierList(named);
            Expect(JsTokenKind.RightBrace);
        }
        else
        {
            throw Error("Unsupported import clause", current.Position);
        }

        Expect(JsTokenKind.Identifier); // from
        var source = Expect(JsTokenKind.String);
        var importAttributes = ParseOptionalImportAttributesClause();
        ConsumeOptionalSemicolon();
        return At(new JsImportDeclaration(defaultBinding, namespaceBinding, named,
            GetStringLiteralText(source), false, importAttributes), start);
    }

    private void ParseImportSpecifierList(List<JsImportSpecifier> target)
    {
        while (current.Kind != JsTokenKind.RightBrace && current.Kind != JsTokenKind.Eof)
        {
            var importedWasString = current.Kind == JsTokenKind.String;
            var imported = ParseModuleExportName();
            string local;
            if (current.Kind == JsTokenKind.Identifier && CurrentSourceTextEquals("as"))
            {
                Next();
                local = ParseIdentifierName();
            }
            else
            {
                if (importedWasString)
                    throw Error("String import names require an alias", current.Position);
                local = imported;
            }

            target.Add(new(imported, local));
            if (!Match(JsTokenKind.Comma))
                break;
        }
    }

    private JsStatement ParseExportDeclaration()
    {
        var start = current.Position;
        Expect(JsTokenKind.ReservedWord); // export

        if (Match(JsTokenKind.Star))
        {
            string? exportedName = null;
            if (current.Kind == JsTokenKind.Identifier && CurrentSourceTextEquals("as"))
            {
                Next();
                exportedName = ParseModuleExportName();
            }

            Expect(JsTokenKind.Identifier); // from
            var source = Expect(JsTokenKind.String);
            var exportAttributes = ParseOptionalImportAttributesClause();
            ConsumeOptionalSemicolon();
            return At(new JsExportAllDeclaration(GetStringLiteralText(source), exportedName, exportAttributes), start);
        }

        if (Match(JsTokenKind.LeftBrace))
        {
            var specifiers = new List<JsExportSpecifier>();
            while (current.Kind != JsTokenKind.RightBrace && current.Kind != JsTokenKind.Eof)
            {
                var local = ParseModuleExportName();
                var exported = local;
                if (current.Kind == JsTokenKind.Identifier && CurrentSourceTextEquals("as"))
                {
                    Next();
                    exported = ParseModuleExportName();
                }

                specifiers.Add(new(local, exported));
                if (!Match(JsTokenKind.Comma))
                    break;
            }

            Expect(JsTokenKind.RightBrace);
            string? source = null;
            if (current.Kind == JsTokenKind.Identifier && CurrentSourceTextEquals("from"))
            {
                Next();
                source = GetStringLiteralText(Expect(JsTokenKind.String));
            }

            var exportAttributes =
                source is null ? Array.Empty<JsImportAttribute>() : ParseOptionalImportAttributesClause();
            ConsumeOptionalSemicolon();
            return At(new JsExportNamedDeclaration(specifiers, source, exportAttributes), start);
        }

        if (current.Kind == JsTokenKind.Default ||
            (current.Kind == JsTokenKind.Identifier && CurrentSourceTextEquals("default")))
        {
            Next();
            // Grammar split:
            // export default ClassDeclaration / HoistableDeclaration
            // export default AssignmentExpression ;
            // Class/function declaration forms do not require a trailing semicolon.
            if (current.Kind == JsTokenKind.ReservedWord && CurrentSourceTextEquals("class"))
            {
                var classExpr = ParseClassExpression(true);
                return At(new JsExportDefaultDeclaration(classExpr, true), start);
            }

            if (current.Kind == JsTokenKind.Function)
            {
                var fnExpr = ParseFunctionExpression();
                return At(new JsExportDefaultDeclaration(fnExpr, true), start);
            }

            if (current.Kind == JsTokenKind.Identifier &&
                CurrentSourceTextEquals("async") &&
                Peek().Kind == JsTokenKind.Function &&
                !Peek().HasLineTerminatorBefore)
            {
                var asyncFnExpr = ParseFunctionExpression(true);
                return At(new JsExportDefaultDeclaration(asyncFnExpr, true), start);
            }

            var expr = ParseAssignment(true);
            ConsumeOptionalSemicolon();
            return At(new JsExportDefaultDeclaration(expr), start);
        }

        if (current.Kind is JsTokenKind.Var or JsTokenKind.Let or JsTokenKind.Const)
        {
            var decl = current.Kind switch
            {
                JsTokenKind.Var or JsTokenKind.Const when Peek().Kind is JsTokenKind.LeftBrace
                        or JsTokenKind.LeftBracket
                    => ParseBindingDeclarationStatement(true),
                JsTokenKind.Let when Peek().Kind is JsTokenKind.LeftBrace or JsTokenKind.LeftBracket
                    => ParseBindingDeclarationStatement(true),
                _ => ParseVariableDeclarationStatement(true)
            };
            return At(new JsExportDeclarationStatement(decl), start);
        }

        if (current.Kind == JsTokenKind.Function)
        {
            var decl = ParseFunctionDeclaration();
            return At(new JsExportDeclarationStatement(decl), start);
        }

        if (current.Kind == JsTokenKind.ReservedWord && CurrentSourceTextEquals("class"))
        {
            var decl = ParseClassDeclaration();
            return At(new JsExportDeclarationStatement(decl), start);
        }

        throw Error("Unsupported export declaration", current.Position);
    }

    private IReadOnlyList<JsImportAttribute> ParseOptionalImportAttributesClause()
    {
        if (!(current.Kind == JsTokenKind.With ||
              (current.Kind == JsTokenKind.Identifier && CurrentSourceTextEquals("with"))))
            return Array.Empty<JsImportAttribute>();

        Next();
        Expect(JsTokenKind.LeftBrace);
        if (Match(JsTokenKind.RightBrace))
            return Array.Empty<JsImportAttribute>();

        var attributes = new List<JsImportAttribute>(4);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var key = current.Kind == JsTokenKind.String
                ? GetStringLiteralText(Expect(JsTokenKind.String))
                : ParseIdentifierName();
            Expect(JsTokenKind.Colon);
            var value = GetStringLiteralText(Expect(JsTokenKind.String));

            if (!seenKeys.Add(key))
                throw Error($"Duplicate import attribute key '{key}'", current.Position);

            attributes.Add(new(key, value));

            if (!Match(JsTokenKind.Comma))
                break;
            if (current.Kind == JsTokenKind.RightBrace)
                break;
        }

        Expect(JsTokenKind.RightBrace);
        return attributes;
    }

    private JsIfStatement ParseIfStatement()
    {
        var start = current.Position;
        Expect(JsTokenKind.If);
        Expect(JsTokenKind.LeftParen);
        var test = ParseExpression();
        Expect(JsTokenKind.RightParen);
        var consequent = ParseStatement();
        JsStatement? alternate = null;
        if (Match(JsTokenKind.Else)) alternate = ParseStatement();

        return At(new JsIfStatement(test, consequent, alternate), start);
    }

    private JsReturnStatement ParseReturnStatement()
    {
        var start = current.Position;
        if (level == 0)
            throw Error("'return' outside of function.", start);
        Expect(JsTokenKind.Return);
        if (current.Kind is JsTokenKind.Semicolon or JsTokenKind.RightBrace or JsTokenKind.Eof ||
            current.HasLineTerminatorBefore)
        {
            ConsumeOptionalSemicolon();
            return At(new JsReturnStatement(null), start);
        }

        var arg = ParseExpression();
        ConsumeOptionalSemicolon();
        return At(new JsReturnStatement(arg), start);
    }

    private JsFunctionDeclaration ParseFunctionDeclaration(bool isAsyncPrefix = false)
    {
        MarkNestedFunctionSyntaxSeen();
        var start = current.Position;
        var isAsync = false;
        if (isAsyncPrefix)
        {
            isAsync = true;
            Expect(JsTokenKind.Identifier); // async
        }

        Expect(JsTokenKind.Function);
        var isGenerator = Match(JsTokenKind.Star);

        var identifier = ParseCheckedIdentifierName(Expect(JsTokenKind.Identifier));
        Expect(JsTokenKind.LeftParen);
        var generatorLevelBeforeParams = generatorFunctionLevel;
        if (!isGenerator) generatorFunctionLevel = 0;

        var parsedParams = ParseFormalParameterList();
        generatorFunctionLevel = generatorLevelBeforeParams;
        var parameters = parsedParams.Parameters;
        var parameterIds = parsedParams.ParameterIds;
        var parameterInitializers = parsedParams.Initializers;
        var parameterPatterns = parsedParams.ParameterPatterns;
        var parameterPositions = parsedParams.ParameterPositions;
        var functionLength = parsedParams.FunctionLength;
        var hasSimpleParameterList = parsedParams.HasSimpleParameterList;
        var hasDuplicateParameters = parsedParams.HasDuplicateParameters;
        var restParameterIndex = parsedParams.RestParameterIndex;
        var strictBeforeBody = strictMode;
        var generatorLevelBeforeBody = generatorFunctionLevel;
        var asyncLevelBeforeBody = asyncFunctionLevel;
        var allowSuperPropertyBeforeBody = allowSuperProperty;
        var allowSuperCallBeforeBody = allowSuperCall;
        generatorFunctionLevel = isGenerator ? generatorLevelBeforeBody + 1 : 0;
        asyncFunctionLevel = isAsync ? asyncLevelBeforeBody + 1 : 0;

        allowSuperProperty = false;
        allowSuperCall = false;
        var body = ParseBlockStatement(true);
        if (!strictBeforeBody && strictMode)
            for (var i = 0; i < parameters.Count; i++)
                if (IsEvalOrArguments(parameters[i], parameterIds[i]))
                    throw Error("Unexpected eval or arguments in strict mode", parameterPositions[i]);

        strictMode = strictBeforeBody;
        generatorFunctionLevel = generatorLevelBeforeBody;
        asyncFunctionLevel = asyncLevelBeforeBody;
        allowSuperProperty = allowSuperPropertyBeforeBody;
        allowSuperCall = allowSuperCallBeforeBody;

        return At(new JsFunctionDeclaration(identifier.Name, parameters, body, isGenerator, isAsync,
                parameterInitializers, parameterPatterns, parameterPositions, parsedParams.ParameterBindingKinds,
                functionLength, hasSimpleParameterList, hasDuplicateParameters,
                restParameterIndex, identifier.NameId, parameterIds),
            start);
    }

    private JsWhileStatement ParseWhileStatement()
    {
        var start = current.Position;
        Expect(JsTokenKind.While);
        Expect(JsTokenKind.LeftParen);
        var test = ParseExpression();
        Expect(JsTokenKind.RightParen);
        var body = ParseStatement();
        return At(new JsWhileStatement(test, body), start);
    }

    private JsDoWhileStatement ParseDoWhileStatement()
    {
        var start = current.Position;
        Expect(JsTokenKind.Do);
        var body = ParseStatement();
        Expect(JsTokenKind.While);
        Expect(JsTokenKind.LeftParen);
        var test = ParseExpression();
        Expect(JsTokenKind.RightParen);
        _ = Match(JsTokenKind.Semicolon);
        return At(new JsDoWhileStatement(body, test), start);
    }

    private JsStatement ParseForStatement()
    {
        var start = current.Position;
        Expect(JsTokenKind.For);
        var isAwait = false;
        if ((asyncFunctionLevel > 0 || (allowTopLevelAwait && asyncFunctionLevel == 0)) &&
            (current.Kind == JsTokenKind.Identifier || current.Kind == JsTokenKind.ReservedWord) &&
            CurrentSourceTextEquals("await"))
        {
            isAwait = true;
            Next();
        }

        Expect(JsTokenKind.LeftParen);

        JsNode? init = null;
        var initIsVarDecl = false;
        JsExpression? forHeadBindingPattern = null;
        JsVariableDeclarationKind? forHeadBindingKind = null;
        string? forHeadSyntheticName = null;
        if (current.Kind != JsTokenKind.Semicolon)
        {
            if ((current.Kind == JsTokenKind.Var || current.Kind == JsTokenKind.Let ||
                 current.Kind == JsTokenKind.Const) &&
                Peek().Kind == JsTokenKind.LeftBracket)
            {
                initIsVarDecl = true;
                var parsed = ParseForHeadBindingDeclaration(false);
                init = parsed.Declaration;
                forHeadBindingKind = parsed.Kind;
                forHeadSyntheticName = parsed.SyntheticName;
                forHeadBindingPattern = parsed.Pattern;
            }
            else if ((current.Kind == JsTokenKind.Var || current.Kind == JsTokenKind.Let ||
                      current.Kind == JsTokenKind.Const) &&
                     Peek().Kind == JsTokenKind.LeftBrace)
            {
                initIsVarDecl = true;
                var parsed = ParseForHeadBindingDeclaration(true);
                init = parsed.Declaration;
                forHeadBindingKind = parsed.Kind;
                forHeadSyntheticName = parsed.SyntheticName;
                forHeadBindingPattern = parsed.Pattern;
            }
            else if (current.Kind is JsTokenKind.Var or JsTokenKind.Const ||
                     (current.Kind == JsTokenKind.Let && ShouldParseForHeadLetDeclaration()))
            {
                initIsVarDecl = true;
                init = ParseVariableDeclarationStatement(false, true,
                    false);
            }
            else if (current.Kind == JsTokenKind.LeftBrace)
            {
                if (!TryParseForInOfAssignmentHeadPattern(true, out var pattern))
                    init = ParseExpression(false);
                else
                    init = pattern;
            }
            else if (current.Kind == JsTokenKind.LeftBracket)
            {
                if (!TryParseForInOfAssignmentHeadPattern(false, out var pattern))
                    init = ParseExpression(false);
                else
                    init = pattern;
            }
            else
            {
                init = ParseExpression(false);
            }
        }

        if (current.Kind is JsTokenKind.In or JsTokenKind.Of)
        {
            if (init is null) throw Error("Expected left side in for-in/of", current.Position);

            if (initIsVarDecl &&
                init is JsVariableDeclarationStatement { Declarators.Count: not 1 })
                throw Error("for-in/of variable declaration must contain a single declarator", current.Position);

            var isOf = current.Kind == JsTokenKind.Of;
            if (isAwait && !isOf)
                throw Error("for await loops must use 'of'", current.Position);
            Next();
            var right = ParseExpression();
            Expect(JsTokenKind.RightParen);
            if (forHeadBindingPattern is not null &&
                init is JsVariableDeclarationStatement forInOfBindingDecl &&
                forInOfBindingDecl.Declarators.Count == 1)
            {
                init = At(new JsVariableDeclarationStatement(
                        forHeadBindingKind!.Value,
                        CollectBindingPatternDeclarators(forHeadBindingPattern, forHeadBindingKind.Value),
                        forHeadBindingPattern),
                    forInOfBindingDecl.Position);
                forHeadBindingPattern = null;
                forHeadBindingKind = null;
                forHeadSyntheticName = null;
            }

            var bodyForInOf = ParseStatementTrackingNestedFunctionSyntax(out var bodyForInOfMayCreateNestedFunction);
            if (forHeadBindingPattern is not null)
                bodyForInOf = WrapForHeadBindingBody(forHeadSyntheticName!, forHeadBindingPattern,
                    forHeadBindingKind!.Value, bodyForInOf);
            return At(new JsForInOfStatement(
                init,
                right,
                isOf,
                bodyForInOf,
                isAwait,
                bodyForInOfMayCreateNestedFunction), start);
        }

        if (isAwait)
            throw Error("for await loops must use 'of'", current.Position);

        if (forHeadBindingPattern is not null &&
            init is JsVariableDeclarationStatement bindingDecl &&
            bindingDecl.Declarators.Count == 1)
        {
            init = At(new JsVariableDeclarationStatement(
                    forHeadBindingKind!.Value,
                    CollectBindingPatternDeclarators(forHeadBindingPattern, forHeadBindingKind.Value),
                    forHeadBindingPattern,
                    bindingDecl.Declarators[0].Initializer),
                bindingDecl.Position);
            forHeadBindingPattern = null;
            forHeadBindingKind = null;
            forHeadSyntheticName = null;
        }

        Expect(JsTokenKind.Semicolon);

        JsExpression? test = null;
        if (current.Kind != JsTokenKind.Semicolon) test = ParseExpression();

        Expect(JsTokenKind.Semicolon);

        JsExpression? update = null;
        if (current.Kind != JsTokenKind.RightParen) update = ParseExpression();

        Expect(JsTokenKind.RightParen);
        var body = ParseStatementTrackingNestedFunctionSyntax(out var bodyMayCreateNestedFunction);
        if (forHeadBindingPattern is not null)
            body = WrapForHeadBindingBody(forHeadSyntheticName!, forHeadBindingPattern, forHeadBindingKind!.Value,
                body);
        return At(new JsForStatement(
            init,
            test,
            update,
            body,
            bodyMayCreateNestedFunction), start);
    }

    private bool TryParseForInOfAssignmentHeadPattern(bool isObjectPattern, out JsExpression pattern)
    {
        var snapshot = CaptureSnapshot();
        try
        {
            pattern = isObjectPattern
                ? At(ParseObjectBindingPattern(), current.Position)
                : At(ParseArrayPatternExpression(), current.Position);
            if (current.Kind is JsTokenKind.In or JsTokenKind.Of)
                return true;
        }
        catch (JsParseException)
        {
        }

        RestoreSnapshot(snapshot);
        pattern = null!;
        return false;
    }

    private bool ShouldParseForHeadLetDeclaration()
    {
        if (strictMode)
            return true;

        return Peek().Kind is JsTokenKind.Identifier or JsTokenKind.ReservedWord or JsTokenKind.PrivateIdentifier;
    }

    private bool ShouldParseLetDeclarationStatement()
    {
        if (strictMode)
            return true;

        return Peek().Kind is JsTokenKind.Identifier or JsTokenKind.ReservedWord or JsTokenKind.PrivateIdentifier
            or JsTokenKind.LeftBrace or JsTokenKind.LeftBracket;
    }

    private (JsVariableDeclarationStatement Declaration, JsExpression Pattern, JsVariableDeclarationKind Kind, string
        SyntheticName)
        ParseForHeadBindingDeclaration(bool isObjectPattern)
    {
        var start = current.Position;
        var kind = current.Kind switch
        {
            JsTokenKind.Var => JsVariableDeclarationKind.Var,
            JsTokenKind.Let => JsVariableDeclarationKind.Let,
            JsTokenKind.Const => JsVariableDeclarationKind.Const,
            _ => throw Error("Expected var, let, or const", current.Position)
        };
        Next();
        JsExpression pattern = isObjectPattern
            ? At(ParseObjectBindingPattern(), current.Position)
            : At(ParseArrayPatternExpression(), current.Position);
        var syntheticName = $"$forpat_{level}_{pattern.Position}";
        JsExpression? initializer = null;
        if (Match(JsTokenKind.Assign))
            initializer = ParseAssignment(false);
        var declarator = At(CreateVariableDeclarator(syntheticName, initializer), pattern.Position);
        return (At(new JsVariableDeclarationStatement(kind, new[] { declarator }), start),
            pattern, kind, syntheticName);
    }

    private JsStatement WrapForHeadBindingBody(string syntheticName, JsExpression pattern,
        JsVariableDeclarationKind kind, JsStatement body)
    {
        return pattern switch
        {
            JsArrayExpression arrayPattern => WrapForHeadArrayBindingBody(syntheticName, arrayPattern, kind, body),
            JsObjectExpression objectPattern => WrapForHeadObjectBindingBody(syntheticName, objectPattern, kind, body),
            _ => throw Error("Unsupported for-head binding pattern.", pattern.Position)
        };
    }

    private JsStatement WrapForHeadArrayBindingBody(string syntheticName, JsArrayExpression pattern,
        JsVariableDeclarationKind kind, JsStatement body)
    {
        var statements = new List<JsStatement>();
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);
        var declarationKind = kind == JsVariableDeclarationKind.Const ? JsVariableDeclarationKind.Let : kind;
        CollectArrayPatternBindingDeclarations(pattern, declaredNames, declarationKind, statements);
        statements.Add(new JsExpressionStatement(
            new JsAssignmentExpression(JsAssignmentOperator.Assign, pattern,
                CreateIdentifierExpression(syntheticName))));

        statements.Add(body);
        return new JsBlockStatement(statements, false);
    }

    private JsStatement WrapForHeadObjectBindingBody(string syntheticName, JsObjectExpression pattern,
        JsVariableDeclarationKind kind, JsStatement body)
    {
        var statements = new List<JsStatement>();
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);
        var declarationKind = kind == JsVariableDeclarationKind.Const ? JsVariableDeclarationKind.Let : kind;
        CollectObjectPatternBindingDeclarations(pattern, declaredNames, declarationKind, statements);
        statements.Add(new JsExpressionStatement(
            new JsAssignmentExpression(JsAssignmentOperator.Assign, pattern,
                CreateIdentifierExpression(syntheticName))));

        statements.Add(body);
        return new JsBlockStatement(statements, false);
    }

    private bool TryBuildSimpleForHeadArrayBindingDeclarations(string syntheticName, JsArrayExpression pattern,
        JsVariableDeclarationKind kind,
        List<JsStatement> statements)
    {
        return TryBuildSimpleArrayBindingDeclarationsFromSource(
            CreateIdentifierExpression(syntheticName),
            pattern,
            kind,
            statements);
    }

    private bool TryBuildSimpleArrayBindingDeclarationsFromSource(JsExpression source, JsArrayExpression pattern,
        JsVariableDeclarationKind kind,
        List<JsStatement> statements)
    {
        for (var i = 0; i < pattern.Elements.Count; i++)
        {
            var element = pattern.Elements[i];
            if (element is null)
                continue;
            switch (element)
            {
                case JsIdentifierExpression:
                case JsArrayExpression:
                case JsObjectExpression:
                case JsSpreadExpression { Argument: JsIdentifierExpression or JsArrayExpression or JsObjectExpression }:
                case JsAssignmentExpression { Operator: JsAssignmentOperator.Assign, Left: JsIdentifierExpression }:
                case JsAssignmentExpression { Operator: JsAssignmentOperator.Assign, Left: JsArrayExpression }:
                case JsAssignmentExpression { Operator: JsAssignmentOperator.Assign, Left: JsObjectExpression }:
                    break;
                default:
                    return false;
            }
        }

        for (var i = 0; i < pattern.Elements.Count; i++)
        {
            var element = pattern.Elements[i];
            if (element is null)
                continue;

            JsExpression memberSource = new JsMemberExpression(
                source,
                new JsLiteralExpression((double)i, i.ToString()),
                true);

            switch (element)
            {
                case JsIdentifierExpression id:
                    statements.Add(new JsVariableDeclarationStatement(
                        kind,
                        new[] { CreateVariableDeclarator(id, memberSource) }));
                    break;
                case JsArrayExpression nestedArray:
                    if (!TryBuildSimpleArrayBindingDeclarationsFromSource(memberSource, nestedArray, kind, statements))
                        return false;
                    break;
                case JsObjectExpression nestedObject:
                    if (!TryBuildSimpleObjectBindingDeclarationsFromSource(memberSource, nestedObject, kind,
                            statements))
                        return false;
                    break;
                case JsSpreadExpression { Argument: var restTarget }:
                    var restSource = new JsArrayExpression(new JsExpression[] { new JsSpreadExpression(source) });
                    switch (restTarget)
                    {
                        case JsIdentifierExpression restId:
                            statements.Add(new JsVariableDeclarationStatement(
                                kind,
                                new[] { CreateVariableDeclarator(restId, restSource) }));
                            break;
                        case JsArrayExpression nestedRestArray:
                            if (!TryBuildSimpleArrayBindingDeclarationsFromSource(restSource, nestedRestArray, kind,
                                    statements))
                                return false;
                            break;
                        case JsObjectExpression nestedRestObject:
                            if (!TryBuildSimpleObjectBindingDeclarationsFromSource(restSource, nestedRestObject, kind,
                                    statements))
                                return false;
                            break;
                        default:
                            return false;
                    }

                    break;
                case JsAssignmentExpression
                {
                    Operator: JsAssignmentOperator.Assign, Left: JsIdentifierExpression leftId,
                    Right: var defaultValue
                }:
                    var identifierInitializer = new JsConditionalExpression(
                        new JsBinaryExpression(JsBinaryOperator.StrictEqual,
                            memberSource,
                            new JsUnaryExpression(JsUnaryOperator.Void, new JsLiteralExpression(0, "0"))),
                        defaultValue,
                        memberSource);
                    statements.Add(new JsVariableDeclarationStatement(
                        kind,
                        new[] { CreateVariableDeclarator(leftId, identifierInitializer) }));
                    break;
                case JsAssignmentExpression
                {
                    Operator: JsAssignmentOperator.Assign, Left: JsArrayExpression nestedArrayWithDefault,
                    Right: var defaultArrayValue
                }:
                    var arrayDefaultSource = new JsConditionalExpression(
                        new JsBinaryExpression(JsBinaryOperator.StrictEqual,
                            memberSource,
                            new JsUnaryExpression(JsUnaryOperator.Void, new JsLiteralExpression(0, "0"))),
                        defaultArrayValue,
                        memberSource);
                    if (!TryBuildSimpleArrayBindingDeclarationsFromSource(arrayDefaultSource, nestedArrayWithDefault,
                            kind, statements))
                        return false;
                    break;
                case JsAssignmentExpression
                {
                    Operator: JsAssignmentOperator.Assign, Left: JsObjectExpression nestedObjectWithDefault,
                    Right: var defaultObjectValue
                }:
                    var objectDefaultSource = new JsConditionalExpression(
                        new JsBinaryExpression(JsBinaryOperator.StrictEqual,
                            memberSource,
                            new JsUnaryExpression(JsUnaryOperator.Void, new JsLiteralExpression(0, "0"))),
                        defaultObjectValue,
                        memberSource);
                    if (!TryBuildSimpleObjectBindingDeclarationsFromSource(objectDefaultSource, nestedObjectWithDefault,
                            kind, statements))
                        return false;
                    break;
            }
        }

        return true;
    }

    private bool TryBuildSimpleObjectBindingDeclarations(string syntheticName, JsObjectExpression pattern,
        JsVariableDeclarationKind kind,
        List<JsStatement> statements)
    {
        return TryBuildSimpleObjectBindingDeclarationsFromSource(
            CreateIdentifierExpression(syntheticName),
            pattern,
            kind,
            statements);
    }

    private bool TryBuildSimpleObjectBindingDeclarationsFromSource(JsExpression source, JsObjectExpression pattern,
        JsVariableDeclarationKind kind,
        List<JsStatement> statements)
    {
        var declarators = new List<JsVariableDeclarator>();
        var nestedStatements = new List<JsStatement>();
        if (!TryBuildSimpleObjectBindingDeclaratorsFromSource(source, pattern, declarators, kind, nestedStatements))
            return false;

        for (var i = 0; i < declarators.Count; i++)
            statements.Add(new JsVariableDeclarationStatement(
                kind,
                new[] { declarators[i] }));

        statements.AddRange(nestedStatements);
        return true;
    }

    private bool TryBuildSimpleObjectBindingDeclarators(string syntheticName, JsObjectExpression pattern,
        List<JsVariableDeclarator> declarators)
    {
        return TryBuildSimpleObjectBindingDeclaratorsFromSource(
            CreateIdentifierExpression(syntheticName),
            pattern,
            declarators,
            JsVariableDeclarationKind.Const,
            new());
    }

    private bool TryBuildSimpleObjectBindingDeclaratorsFromSource(JsExpression source, JsObjectExpression pattern,
        List<JsVariableDeclarator> declarators,
        JsVariableDeclarationKind kind,
        List<JsStatement> nestedStatements)
    {
        for (var i = 0; i < pattern.Properties.Count; i++)
        {
            var property = pattern.Properties[i];
            if (property.Kind != JsObjectPropertyKind.Data || property.IsComputed)
                return false;

            switch (property.Value)
            {
                case JsIdentifierExpression:
                case JsArrayExpression:
                case JsObjectExpression:
                case JsAssignmentExpression { Operator: JsAssignmentOperator.Assign, Left: JsIdentifierExpression }:
                case JsAssignmentExpression { Operator: JsAssignmentOperator.Assign, Left: JsArrayExpression }:
                case JsAssignmentExpression { Operator: JsAssignmentOperator.Assign, Left: JsObjectExpression }:
                    break;
                default:
                    return false;
            }
        }

        for (var i = 0; i < pattern.Properties.Count; i++)
        {
            var property = pattern.Properties[i];
            JsExpression memberSource = new JsMemberExpression(
                source,
                CreateIdentifierExpression(property.Key),
                false);

            switch (property.Value)
            {
                case JsIdentifierExpression id:
                    declarators.Add(CreateVariableDeclarator(id, memberSource));
                    break;
                case JsArrayExpression nestedArray:
                    if (!TryBuildSimpleArrayBindingDeclarationsFromSource(memberSource, nestedArray, kind,
                            nestedStatements))
                        return false;
                    break;
                case JsObjectExpression nestedObject:
                    if (!TryBuildSimpleObjectBindingDeclaratorsFromSource(memberSource, nestedObject, declarators, kind,
                            nestedStatements))
                        return false;
                    break;
                case JsAssignmentExpression
                {
                    Operator: JsAssignmentOperator.Assign, Left: JsIdentifierExpression leftId,
                    Right: var defaultValue
                }:
                    var initializer = new JsConditionalExpression(
                        new JsBinaryExpression(JsBinaryOperator.StrictEqual,
                            memberSource,
                            new JsUnaryExpression(JsUnaryOperator.Void, new JsLiteralExpression(0, "0"))),
                        defaultValue,
                        memberSource);
                    declarators.Add(CreateVariableDeclarator(leftId, initializer));
                    break;
                case JsAssignmentExpression
                {
                    Operator: JsAssignmentOperator.Assign, Left: JsArrayExpression nestedArrayWithDefault,
                    Right: var defaultArrayValue
                }:
                    var arrayInitializerSource = new JsConditionalExpression(
                        new JsBinaryExpression(JsBinaryOperator.StrictEqual,
                            memberSource,
                            new JsUnaryExpression(JsUnaryOperator.Void, new JsLiteralExpression(0, "0"))),
                        defaultArrayValue,
                        memberSource);
                    if (!TryBuildSimpleArrayBindingDeclarationsFromSource(arrayInitializerSource,
                            nestedArrayWithDefault, kind, nestedStatements))
                        return false;
                    break;
                case JsAssignmentExpression
                {
                    Operator: JsAssignmentOperator.Assign, Left: JsObjectExpression nestedObjectWithDefault,
                    Right: var defaultObjectValue
                }:
                    var objectInitializerSource = new JsConditionalExpression(
                        new JsBinaryExpression(JsBinaryOperator.StrictEqual,
                            memberSource,
                            new JsUnaryExpression(JsUnaryOperator.Void, new JsLiteralExpression(0, "0"))),
                        defaultObjectValue,
                        memberSource);
                    if (!TryBuildSimpleObjectBindingDeclaratorsFromSource(objectInitializerSource,
                            nestedObjectWithDefault, declarators, kind, nestedStatements))
                        return false;
                    break;
            }
        }

        return true;
    }

    private void CollectArrayPatternBindingDeclarations(JsArrayExpression pattern, HashSet<string> declaredNames,
        JsVariableDeclarationKind kind,
        List<JsStatement> statements)
    {
        for (var i = 0; i < pattern.Elements.Count; i++)
        {
            var element = pattern.Elements[i];
            if (element is null)
                continue;
            CollectPatternBindingDeclarations(element, declaredNames, kind, statements);
        }
    }

    private JsBreakStatement ParseBreakStatement()
    {
        var start = current.Position;
        Expect(JsTokenKind.Break);
        string? label = null;
        if (current.Kind == JsTokenKind.Identifier && !current.HasLineTerminatorBefore) label = ConsumeIdentifierText();

        ConsumeOptionalSemicolon();
        return At(new JsBreakStatement(label), start);
    }

    private JsContinueStatement ParseContinueStatement()
    {
        var start = current.Position;
        Expect(JsTokenKind.Continue);
        string? label = null;
        if (current.Kind == JsTokenKind.Identifier && !current.HasLineTerminatorBefore) label = ConsumeIdentifierText();

        ConsumeOptionalSemicolon();
        return At(new JsContinueStatement(label), start);
    }

    private JsLabeledStatement ParseLabeledStatement()
    {
        var start = current.Position;
        var label = ConsumeIdentifierText();
        Expect(JsTokenKind.Colon);
        var statement = ParseStatement();
        return At(new JsLabeledStatement(label, statement), start);
    }

    private JsThrowStatement ParseThrowStatement()
    {
        var start = current.Position;
        Expect(JsTokenKind.Throw);
        if (current.HasLineTerminatorBefore) throw Error("Illegal newline after throw", start);

        var argument = ParseExpression();
        ConsumeOptionalSemicolon();
        return At(new JsThrowStatement(argument), start);
    }

    private JsDebuggerStatement ParseDebuggerStatement()
    {
        var start = current.Position;
        Expect(JsTokenKind.Debugger);
        ConsumeOptionalSemicolon();
        return At(new JsDebuggerStatement(), start);
    }

    private JsSwitchStatement ParseSwitchStatement()
    {
        var start = current.Position;
        Expect(JsTokenKind.Switch);
        Expect(JsTokenKind.LeftParen);
        var discriminant = ParseExpression();
        Expect(JsTokenKind.RightParen);
        Expect(JsTokenKind.LeftBrace);

        var cases = new List<JsSwitchCase>();
        var defaultCaseFound = false;
        while (current.Kind != JsTokenKind.RightBrace && current.Kind != JsTokenKind.Eof)
        {
            JsExpression? test;
            if (Match(JsTokenKind.Case))
            {
                test = ParseExpression();
            }
            else if (Match(JsTokenKind.Default))
            {
                if (defaultCaseFound) throw Error("Multiple default cases in switch statement", current.Position);

                defaultCaseFound = true;
                test = null;
            }
            else
            {
                throw Error("Expected 'case' or 'default' in switch statement", current.Position);
            }

            Expect(JsTokenKind.Colon);
            var consequent = new List<JsStatement>();
            while (current.Kind is not (JsTokenKind.Case or JsTokenKind.Default or JsTokenKind.RightBrace
                   or JsTokenKind.Eof))
                consequent.Add(ParseStatement());

            cases.Add(At(new JsSwitchCase(test, consequent), test?.Position ?? current.Position));
        }

        Expect(JsTokenKind.RightBrace);
        return At(new JsSwitchStatement(discriminant, cases), start);
    }

    private JsTryStatement ParseTryStatement()
    {
        var start = current.Position;
        Expect(JsTokenKind.Try);
        var block = ParseBlockStatement(false);

        JsCatchClause? handler = null;
        if (Match(JsTokenKind.Catch))
        {
            string? paramName = null;
            JsExpression? bindingPattern = null;
            IReadOnlyList<JsVariableDeclarator>? catchDeclarators = null;
            if (Match(JsTokenKind.LeftParen))
            {
                if (current.Kind is JsTokenKind.LeftBrace or JsTokenKind.LeftBracket)
                {
                    bindingPattern = current.Kind == JsTokenKind.LeftBrace
                        ? ParseObjectBindingPattern()
                        : ParseArrayPatternExpression();
                    catchDeclarators = CollectBindingPatternDeclarators(bindingPattern, JsVariableDeclarationKind.Let);
                }
                else
                {
                    paramName = ExpectCheckedIdentifierName(true).Name;
                }

                Expect(JsTokenKind.RightParen);
            }

            handler = At(new JsCatchClause(paramName, ParseBlockStatement(false), bindingPattern, catchDeclarators),
                current.Position);
        }

        JsBlockStatement? finalizer = null;
        if (Match(JsTokenKind.Finally)) finalizer = ParseBlockStatement(false);

        if (handler is null && finalizer is null)
            throw Error("try statement requires catch or finally", current.Position);

        return At(new JsTryStatement(block, handler, finalizer), start);
    }

    private JsWithStatement ParseWithStatement()
    {
        var start = current.Position;
        Expect(JsTokenKind.With);
        Expect(JsTokenKind.LeftParen);
        var @object = ParseExpression();
        Expect(JsTokenKind.RightParen);
        var body = ParseStatement();
        return At(new JsWithStatement(@object, body), start);
    }

    private JsExpressionStatement ParseExpressionStatement()
    {
        var start = current.Position;
        var expr = ParseExpression();
        ConsumeOptionalSemicolon();
        return At(new JsExpressionStatement(expr), start);
    }

    private JsExpression ParseExpression(bool allowIn = true)
    {
        var start = current.Position;
        return At(ParseSequence(allowIn), start);
    }

    private JsExpression ParseSequence(bool allowIn)
    {
        var start = current.Position;
        var expr = ParseAssignment(allowIn);
        if (!Match(JsTokenKind.Comma)) return expr;

        var list = new List<JsExpression> { expr };
        do
        {
            list.Add(ParseAssignment(allowIn));
        } while (Match(JsTokenKind.Comma));

        return At(new JsSequenceExpression(list), start);
    }

    private JsExpression ParseAssignment(bool allowIn)
    {
        var start = current.Position;
        if (TryParseArrowFunctionExpression(start, out var arrowExpr)) return arrowExpr;

        if (current.Kind == JsTokenKind.LeftParen && Peek().Kind == JsTokenKind.RightParen)
        {
            // Special-case empty parameter arrow head: () => ...
            Next(); // (
            Expect(JsTokenKind.RightParen);
            if (Match(JsTokenKind.Arrow))
                return At(
                    ParseArrowFunctionExpressionCore(CreateSimpleArrowParameters(Array.Empty<string>(),
                        Array.Empty<int>())), start);

            throw Error("Unexpected token ')'", current.Position);
        }

        if (generatorFunctionLevel > 0 &&
            (current.Kind == JsTokenKind.Identifier || current.Kind == JsTokenKind.ReservedWord) &&
            CurrentSourceTextEquals("yield"))
            return At(ParseYieldExpression(allowIn), start);

        var lhsStartsWithParen = current.Kind == JsTokenKind.LeftParen;
        JsExpression left;
        if (current.Kind is JsTokenKind.LeftBrace or JsTokenKind.LeftBracket)
        {
            var snapshot = CaptureSnapshot();
            try
            {
                left = current.Kind == JsTokenKind.LeftBrace
                    ? ParseObjectBindingPattern()
                    : ParseArrayPatternExpression();

                if (current.Kind != JsTokenKind.Assign)
                {
                    RestoreSnapshot(snapshot);
                    left = ParseConditional(allowIn);
                }
            }
            catch
            {
                RestoreSnapshot(snapshot);
                left = ParseConditional(allowIn);
            }
        }
        else
        {
            left = ParseConditional(allowIn);
        }

        if (Match(JsTokenKind.Arrow))
        {
            if (!TryExtractArrowParameters(left, out var arrowParameters))
                throw Error("Invalid arrow function parameter list", start);

            var arrowParameterIds = new int[arrowParameters.Count];
            Array.Fill(arrowParameterIds, -1);
            return At(ParseArrowFunctionExpressionCore(CreateSimpleArrowParameters(arrowParameters, arrowParameterIds)),
                start);
        }

        if (current.Kind is JsTokenKind.Assign or
            JsTokenKind.PlusAssign or JsTokenKind.MinusAssign or JsTokenKind.StarAssign or JsTokenKind.SlashAssign
            or JsTokenKind.PercentAssign or JsTokenKind.PowAssign or
            JsTokenKind.ShlAssign or JsTokenKind.SarAssign or JsTokenKind.ShrAssign or
            JsTokenKind.AmpersandAssign or JsTokenKind.PipeAssign or JsTokenKind.CaretAssign or
            JsTokenKind.AndAndAssign or JsTokenKind.OrOrAssign or JsTokenKind.NullishCoalescingAssign)
        {
            var op = GetAssignmentOperator(current);
            EnsureStrictAssignmentTargetAllowed(left);
            Next();
            var right = ParseAssignment(allowIn);
            return At(new JsAssignmentExpression(op, left, right, lhsStartsWithParen), start);
        }

        return At(left, start);
    }

    private ParserSnapshot CaptureSnapshot()
    {
        return new(
            lexer.GetIndex(),
            current,
            hasPeek,
            peek,
            asyncFunctionLevel,
            generatorFunctionLevel,
            strictMode,
            allowSuperProperty,
            allowSuperCall,
            nestedFunctionTrackingDepth,
            nestedFunctionTrackingMask);
    }

    private void RestoreSnapshot(ParserSnapshot snapshot)
    {
        lexer.SetIndex(snapshot.LexerIndex);
        current = snapshot.Current;
        hasPeek = snapshot.HasPeek;
        peek = snapshot.Peek;
        asyncFunctionLevel = snapshot.AsyncFunctionLevel;
        generatorFunctionLevel = snapshot.GeneratorFunctionLevel;
        strictMode = snapshot.StrictMode;
        allowSuperProperty = snapshot.AllowSuperProperty;
        allowSuperCall = snapshot.AllowSuperCall;
        nestedFunctionTrackingDepth = snapshot.NestedFunctionTrackingDepth;
        nestedFunctionTrackingMask = snapshot.NestedFunctionTrackingMask;
    }

    private void CollectObjectPatternBindingDeclarations(JsObjectExpression pattern, HashSet<string> declaredNames,
        JsVariableDeclarationKind kind,
        List<JsStatement> statements)
    {
        for (var i = 0; i < pattern.Properties.Count; i++)
        {
            var property = pattern.Properties[i];
            if (property.Kind is not (JsObjectPropertyKind.Data or JsObjectPropertyKind.Spread))
                continue;
            CollectPatternBindingDeclarations(property.Value, declaredNames, kind, statements);
        }
    }

    private void CollectPatternBindingDeclarations(JsExpression expression, HashSet<string> declaredNames,
        JsVariableDeclarationKind kind,
        List<JsStatement> statements)
    {
        switch (expression)
        {
            case JsIdentifierExpression id:
                if (declaredNames.Add(id.Name))
                    statements.Add(new JsVariableDeclarationStatement(kind,
                        new[] { CreateVariableDeclarator(id, null) }));
                break;
            case JsAssignmentExpression { Operator: JsAssignmentOperator.Assign, Left: var left }:
                CollectPatternBindingDeclarations(left, declaredNames, kind, statements);
                break;
            case JsSpreadExpression spread:
                CollectPatternBindingDeclarations(spread.Argument, declaredNames, kind, statements);
                break;
            case JsArrayExpression arrayPattern:
                CollectArrayPatternBindingDeclarations(arrayPattern, declaredNames, kind, statements);
                break;
            case JsObjectExpression objectPattern:
                CollectObjectPatternBindingDeclarations(objectPattern, declaredNames, kind, statements);
                break;
        }
    }

    private IReadOnlyList<JsVariableDeclarator> CollectBindingPatternDeclarators(
        JsExpression pattern,
        JsVariableDeclarationKind kind)
    {
        var statements = new List<JsStatement>();
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);
        CollectPatternBindingDeclarations(pattern, declaredNames, kind, statements);

        var declarators = new List<JsVariableDeclarator>(declaredNames.Count);
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not JsVariableDeclarationStatement declaration)
                continue;

            for (var j = 0; j < declaration.Declarators.Count; j++)
                declarators.Add(declaration.Declarators[j]);
        }

        return declarators;
    }

    private JsExpression ParseYieldExpression(bool allowIn)
    {
        var start = current.Position;
        if ((current.Kind != JsTokenKind.Identifier && current.Kind != JsTokenKind.ReservedWord) ||
            !CurrentSourceTextEquals("yield"))
            throw Error("Expected yield", current.Position);

        Next();
        var isDelegate = Match(JsTokenKind.Star);

        if (!isDelegate &&
            (current.Kind is JsTokenKind.Semicolon or JsTokenKind.Comma or JsTokenKind.Colon or JsTokenKind.RightParen
                 or JsTokenKind.RightBrace or JsTokenKind.RightBracket or JsTokenKind.Eof ||
             current.HasLineTerminatorBefore))
            return At(new JsYieldExpression(null, isDelegate), start);

        var argument = ParseAssignment(allowIn);
        return At(new JsYieldExpression(argument, isDelegate), start);
    }

    private JsExpression ParseAwaitExpression()
    {
        var start = current.Position;
        if (allowTopLevelAwait && asyncFunctionLevel == 0) sawTopLevelAwait = true;

        Next(); // await
        var argument = ParseUnary();
        return At(new JsAwaitExpression(argument), start);
    }

    private JsExpression ParseConditional(bool allowIn)
    {
        var start = current.Position;
        var test = ParseCoalesce(allowIn);
        if (Match(JsTokenKind.Question))
        {
            var consequent = ParseAssignment(true);
            Expect(JsTokenKind.Colon);
            var alternate = ParseAssignment(allowIn);
            return At(new JsConditionalExpression(test, consequent, alternate), start);
        }

        return At(test, start);
    }

    private JsExpression ParseCoalesce(bool allowIn)
    {
        var start = current.Position;
        var lhsStartsWithParen = current.Kind == JsTokenKind.LeftParen;
        var left = ParseLogicalOrInfo(allowIn);
        var expr = left.Expression;
        var lhsHasLogicalAndOr = left.HasLogicalAndOr;
        while (current.Kind == JsTokenKind.NullishCoalescing)
        {
            if (lhsHasLogicalAndOr && !lhsStartsWithParen)
                throw Error("Cannot mix '??' with '&&' or '||' without parentheses.", start);

            var op = JsBinaryOperator.NullishCoalescing;
            Next();
            var rhsStartsWithParen = current.Kind == JsTokenKind.LeftParen;
            var right = ParseLogicalOrInfo(allowIn);
            if (right.HasLogicalAndOr && !rhsStartsWithParen)
                throw Error("Cannot mix '??' with '&&' or '||' without parentheses.", start);
            expr = At(new JsBinaryExpression(op, expr, right.Expression), start);
            lhsStartsWithParen = false;
            lhsHasLogicalAndOr = false;
        }

        return At(expr, start);
    }

    private LogicalParseInfo ParseLogicalOrInfo(bool allowIn)
    {
        var start = current.Position;
        var left = ParseLogicalAndInfo(allowIn);
        var expr = left.Expression;
        var hasLogical = left.HasLogicalAndOr;
        while (current.Kind == JsTokenKind.OrOr)
        {
            var op = JsBinaryOperator.LogicalOr;
            Next();
            var right = ParseLogicalAndInfo(allowIn);
            expr = At(new JsBinaryExpression(op, expr, right.Expression), start);
            hasLogical = true;
        }

        return new(At(expr, start), hasLogical);
    }

    private LogicalParseInfo ParseLogicalAndInfo(bool allowIn)
    {
        var start = current.Position;
        var expr = ParseBitwiseOr(allowIn);
        var hasLogical = false;
        while (current.Kind == JsTokenKind.AndAnd)
        {
            var op = JsBinaryOperator.LogicalAnd;
            Next();
            expr = At(new JsBinaryExpression(op, expr, ParseBitwiseOr(allowIn)), start);
            hasLogical = true;
        }

        return new(At(expr, start), hasLogical);
    }

    private JsExpression ParseBitwiseOr(bool allowIn)
    {
        var start = current.Position;
        var expr = ParseBitwiseXor(allowIn);
        while (current.Kind == JsTokenKind.Pipe)
        {
            var op = JsBinaryOperator.BitwiseOr;
            Next();
            expr = At(new JsBinaryExpression(op, expr, ParseBitwiseXor(allowIn)), start);
        }

        return At(expr, start);
    }

    private JsExpression ParseBitwiseXor(bool allowIn)
    {
        var start = current.Position;
        var expr = ParseBitwiseAnd(allowIn);
        while (current.Kind == JsTokenKind.Caret)
        {
            var op = JsBinaryOperator.BitwiseXor;
            Next();
            expr = At(new JsBinaryExpression(op, expr, ParseBitwiseAnd(allowIn)), start);
        }

        return At(expr, start);
    }

    private JsExpression ParseBitwiseAnd(bool allowIn)
    {
        var start = current.Position;
        var expr = ParseEquality(allowIn);
        while (current.Kind == JsTokenKind.Ampersand)
        {
            var op = JsBinaryOperator.BitwiseAnd;
            Next();
            expr = At(new JsBinaryExpression(op, expr, ParseEquality(allowIn)), start);
        }

        return At(expr, start);
    }

    private JsExpression ParseEquality(bool allowIn)
    {
        var start = current.Position;
        var expr = ParseRelational(allowIn);
        while (current.Kind is JsTokenKind.Eq or JsTokenKind.Neq or JsTokenKind.StrictEq or JsTokenKind.StrictNeq)
        {
            var op = GetBinaryOperator(current);
            Next();
            expr = At(new JsBinaryExpression(op, expr, ParseRelational(allowIn)), start);
        }

        return At(expr, start);
    }

    private JsExpression ParseRelational(bool allowIn)
    {
        var start = current.Position;
        var expr = ParseShift();
        while (current.Kind is JsTokenKind.Lt or JsTokenKind.Lte or JsTokenKind.Gt or JsTokenKind.Gte ||
               (allowIn && current.Kind == JsTokenKind.In) ||
               current.Kind == JsTokenKind.Instanceof)
        {
            var op = GetBinaryOperator(current);
            Next();
            expr = At(new JsBinaryExpression(op, expr, ParseShift()), start);
        }

        return At(expr, start);
    }

    private JsExpression ParseShift()
    {
        var start = current.Position;
        var expr = ParseAdditive();
        while (current.Kind is JsTokenKind.Shl or JsTokenKind.Sar or JsTokenKind.Shr)
        {
            var op = GetBinaryOperator(current);
            Next();
            expr = At(new JsBinaryExpression(op, expr, ParseAdditive()), start);
        }

        return At(expr, start);
    }

    private JsExpression ParseAdditive()
    {
        var start = current.Position;
        var expr = ParseMultiplicative();
        while (current.Kind is JsTokenKind.Plus or JsTokenKind.Minus)
        {
            var op = GetBinaryOperator(current);
            Next();
            expr = At(new JsBinaryExpression(op, expr, ParseMultiplicative()), start);
        }

        return At(expr, start);
    }

    private JsExpression ParseMultiplicative()
    {
        var start = current.Position;
        var expr = ParseExponentiation();
        while (current.Kind is JsTokenKind.Star or JsTokenKind.Slash or JsTokenKind.Percent)
        {
            var op = GetBinaryOperator(current);
            Next();
            expr = At(new JsBinaryExpression(op, expr, ParseExponentiation()), start);
        }

        return At(expr, start);
    }

    private JsExpression ParseExponentiation()
    {
        var start = current.Position;
        var left = ParseUnary();
        if (current.Kind == JsTokenKind.Pow)
        {
            var op = JsBinaryOperator.Exponentiate;
            Next();
            var right = ParseExponentiation();
            return At(new JsBinaryExpression(op, left, right), start);
        }

        return At(left, start);
    }

    private JsExpression ParseUnary()
    {
        var start = current.Position;
        if (IsAwaitKeywordInCurrentScope()) return At(ParseAwaitExpression(), start);

        if (current.Kind is JsTokenKind.PlusPlus or JsTokenKind.MinusMinus)
        {
            var op = GetUpdateOperator(current);
            Next();
            var arg = ParseUnary();
            EnsureUpdateTargetAllowed(arg, start);
            EnsureStrictAssignmentTargetAllowed(arg);
            return At(new JsUpdateExpression(op, arg, true), start);
        }

        if (current.Kind == JsTokenKind.New) return ParseNewExpression();

        if (current.Kind is JsTokenKind.Bang or JsTokenKind.Plus or JsTokenKind.Minus or JsTokenKind.Tilde
            or JsTokenKind.Typeof or JsTokenKind.Void or JsTokenKind.Delete)
        {
            var op = GetUnaryOperator(current);
            Next();
            return At(new JsUnaryExpression(op, ParseUnary()), start);
        }

        return At(ParsePostfix(), start);
    }

    private bool IsAwaitKeywordInCurrentScope()
    {
        return (asyncFunctionLevel > 0 || (allowTopLevelAwait && asyncFunctionLevel == 0)) &&
               (current.Kind == JsTokenKind.Identifier || current.Kind == JsTokenKind.ReservedWord) &&
               CurrentSourceTextEquals("await");
    }

    private static JsUnaryOperator GetUnaryOperator(in JsToken token)
    {
        return token.Kind switch
        {
            JsTokenKind.Plus => JsUnaryOperator.Plus,
            JsTokenKind.Minus => JsUnaryOperator.Minus,
            JsTokenKind.Bang => JsUnaryOperator.LogicalNot,
            JsTokenKind.Tilde => JsUnaryOperator.BitwiseNot,
            JsTokenKind.Typeof => JsUnaryOperator.Typeof,
            JsTokenKind.Void => JsUnaryOperator.Void,
            JsTokenKind.Delete => JsUnaryOperator.Delete,
            _ => throw new InvalidOperationException($"Unexpected unary operator token {token.Kind}.")
        };
    }

    private static JsUpdateOperator GetUpdateOperator(in JsToken token)
    {
        return token.Kind switch
        {
            JsTokenKind.PlusPlus => JsUpdateOperator.Increment,
            JsTokenKind.MinusMinus => JsUpdateOperator.Decrement,
            _ => throw new InvalidOperationException($"Unexpected update operator token {token.Kind}.")
        };
    }

    private static JsBinaryOperator GetBinaryOperator(in JsToken token)
    {
        return token.Kind switch
        {
            JsTokenKind.Eq => JsBinaryOperator.Equal,
            JsTokenKind.Neq => JsBinaryOperator.NotEqual,
            JsTokenKind.StrictEq => JsBinaryOperator.StrictEqual,
            JsTokenKind.StrictNeq => JsBinaryOperator.StrictNotEqual,
            JsTokenKind.Lt => JsBinaryOperator.LessThan,
            JsTokenKind.Lte => JsBinaryOperator.LessThanOrEqual,
            JsTokenKind.Gt => JsBinaryOperator.GreaterThan,
            JsTokenKind.Gte => JsBinaryOperator.GreaterThanOrEqual,
            JsTokenKind.In => JsBinaryOperator.In,
            JsTokenKind.Instanceof => JsBinaryOperator.Instanceof,
            JsTokenKind.Shl => JsBinaryOperator.ShiftLeft,
            JsTokenKind.Sar => JsBinaryOperator.ShiftRight,
            JsTokenKind.Shr => JsBinaryOperator.ShiftRightLogical,
            JsTokenKind.Plus => JsBinaryOperator.Add,
            JsTokenKind.Minus => JsBinaryOperator.Subtract,
            JsTokenKind.Star => JsBinaryOperator.Multiply,
            JsTokenKind.Slash => JsBinaryOperator.Divide,
            JsTokenKind.Percent => JsBinaryOperator.Modulo,
            JsTokenKind.Pow => JsBinaryOperator.Exponentiate,
            _ => throw new InvalidOperationException($"Unexpected binary operator token {token.Kind}.")
        };
    }

    private static JsAssignmentOperator GetAssignmentOperator(in JsToken token)
    {
        return token.Kind switch
        {
            JsTokenKind.Assign => JsAssignmentOperator.Assign,
            JsTokenKind.PlusAssign => JsAssignmentOperator.AddAssign,
            JsTokenKind.MinusAssign => JsAssignmentOperator.SubtractAssign,
            JsTokenKind.StarAssign => JsAssignmentOperator.MultiplyAssign,
            JsTokenKind.PowAssign => JsAssignmentOperator.ExponentiateAssign,
            JsTokenKind.SlashAssign => JsAssignmentOperator.DivideAssign,
            JsTokenKind.PercentAssign => JsAssignmentOperator.ModuloAssign,
            JsTokenKind.ShlAssign => JsAssignmentOperator.ShiftLeftAssign,
            JsTokenKind.SarAssign => JsAssignmentOperator.ShiftRightAssign,
            JsTokenKind.ShrAssign => JsAssignmentOperator.ShiftRightLogicalAssign,
            JsTokenKind.AmpersandAssign => JsAssignmentOperator.BitwiseAndAssign,
            JsTokenKind.PipeAssign => JsAssignmentOperator.BitwiseOrAssign,
            JsTokenKind.CaretAssign => JsAssignmentOperator.BitwiseXorAssign,
            JsTokenKind.AndAndAssign => JsAssignmentOperator.LogicalAndAssign,
            JsTokenKind.OrOrAssign => JsAssignmentOperator.LogicalOrAssign,
            JsTokenKind.NullishCoalescingAssign => JsAssignmentOperator.NullishCoalescingAssign,
            _ => throw new InvalidOperationException($"Unexpected assignment operator token {token.Kind}.")
        };
    }

    private JsExpression ParseNewExpression()
    {
        var start = current.Position;
        Expect(JsTokenKind.New);
        JsExpression expr;
        if (Match(JsTokenKind.Dot))
        {
            if (!(current.Kind == JsTokenKind.Identifier &&
                  CurrentSourceTextEquals("target")))
                throw Error($"Expected Identifier but found {current.Kind}", current.Position);

            Next();
            expr = At(new JsNewTargetExpression(), start);
        }
        else
        {
            expr = current.Kind == JsTokenKind.New
                ? ParseNewExpression()
                : ParseMemberNoCall();
            if (current.Kind == JsTokenKind.Template)
            {
                var templateToken = current;
                Next();
                expr = At(new JsTaggedTemplateExpression(expr,
                    ParseTemplateLiteralAsTemplate(templateToken, true)), expr.Position);
            }

            if (Match(JsTokenKind.LeftParen))
            {
                var args = ParseArgumentListAfterOpenParen();
                expr = At(new JsNewExpression(expr, args), start);
            }
            else
            {
                expr = At(new JsNewExpression(expr, Array.Empty<JsExpression>()), start);
            }
        }

        var inOptionalChain = false;
        while (true)
        {
            if (current.Kind == JsTokenKind.Question && !Peek().HasLineTerminatorBefore &&
                Peek().Kind == JsTokenKind.Dot)
            {
                Next();
                Expect(JsTokenKind.Dot);
                inOptionalChain = true;

                if (Match(JsTokenKind.LeftBracket))
                {
                    var propExpr = ParseExpression();
                    Expect(JsTokenKind.RightBracket);
                    expr = new JsMemberExpression(expr, propExpr, true, false,
                        true);
                    continue;
                }

                if (Match(JsTokenKind.LeftParen))
                {
                    var args = ParseArgumentListAfterOpenParen();
                    expr = new JsCallExpression(expr, args, true);
                    continue;
                }

                if (current.Kind == JsTokenKind.PrivateIdentifier)
                {
                    var privateName = GetPrivateIdentifierText(current);
                    Next();
                    expr = new JsMemberExpression(expr, new JsLiteralExpression(privateName), false,
                        true, true);
                    continue;
                }

                var optionalProp = ParseIdentifierName();
                expr = new JsMemberExpression(expr, new JsLiteralExpression(optionalProp), false,
                    false, true);
                continue;
            }

            if (Match(JsTokenKind.Dot))
            {
                if (current.Kind == JsTokenKind.PrivateIdentifier)
                {
                    var privateName = GetPrivateIdentifierText(current);
                    Next();
                    expr = new JsMemberExpression(expr, new JsLiteralExpression(privateName), false,
                        true, inOptionalChain);
                    continue;
                }

                var prop = ParseIdentifierName();
                expr = new JsMemberExpression(expr, new JsLiteralExpression(prop), false,
                    false, inOptionalChain);
                continue;
            }

            if (Match(JsTokenKind.LeftBracket))
            {
                var propExpr = ParseExpression();
                Expect(JsTokenKind.RightBracket);
                expr = new JsMemberExpression(expr, propExpr, true, false,
                    inOptionalChain);
                continue;
            }

            if (Match(JsTokenKind.LeftParen))
            {
                var args = ParseArgumentListAfterOpenParen();
                expr = new JsCallExpression(expr, args, inOptionalChain);
                continue;
            }

            break;
        }

        return expr;
    }

    private JsExpression ParseMemberNoCall()
    {
        var expr = ParsePrimary();
        var inOptionalChain = false;
        while (true)
        {
            if (current.Kind == JsTokenKind.Question && !Peek().HasLineTerminatorBefore &&
                Peek().Kind == JsTokenKind.Dot)
            {
                Next();
                Expect(JsTokenKind.Dot);
                inOptionalChain = true;

                if (current.Kind == JsTokenKind.PrivateIdentifier)
                {
                    var privateName = GetPrivateIdentifierText(current);
                    Next();
                    expr = new JsMemberExpression(expr, new JsLiteralExpression(privateName), false,
                        true, true);
                    continue;
                }

                if (Match(JsTokenKind.LeftBracket))
                {
                    var propExpr = ParseExpression();
                    Expect(JsTokenKind.RightBracket);
                    expr = new JsMemberExpression(expr, propExpr, true, false,
                        true);
                    continue;
                }

                var optionalProp = ParseIdentifierName();
                expr = new JsMemberExpression(expr, new JsLiteralExpression(optionalProp), false,
                    false, true);
                continue;
            }

            if (Match(JsTokenKind.Dot))
            {
                if (current.Kind == JsTokenKind.PrivateIdentifier)
                {
                    var privateName = GetPrivateIdentifierText(current);
                    Next();
                    expr = new JsMemberExpression(expr, new JsLiteralExpression(privateName), false,
                        true, inOptionalChain);
                    continue;
                }

                var prop = ParseIdentifierName();
                expr = new JsMemberExpression(expr, new JsLiteralExpression(prop), false,
                    false, inOptionalChain);
                continue;
            }

            if (Match(JsTokenKind.LeftBracket))
            {
                var propExpr = ParseExpression();
                Expect(JsTokenKind.RightBracket);
                expr = new JsMemberExpression(expr, propExpr, true, false,
                    inOptionalChain);
                continue;
            }

            break;
        }

        return expr;
    }

    private JsExpression ParsePostfix()
    {
        var expr = ParseMemberAndCall();
        if (current.Kind is JsTokenKind.PlusPlus or JsTokenKind.MinusMinus &&
            !current.HasLineTerminatorBefore)
        {
            var op = GetUpdateOperator(current);
            EnsureUpdateTargetAllowed(expr, current.Position);
            EnsureStrictAssignmentTargetAllowed(expr);
            Next();
            return new JsUpdateExpression(op, expr, false);
        }

        return expr;
    }

    private void EnsureStrictAssignmentTargetAllowed(JsExpression target)
    {
        if (!strictMode) return;

        if (target is JsIdentifierExpression id) EnsureIdentifierAllowedInCurrentMode(id.Name, id.NameId, id.Position);
    }

    private void EnsureUpdateTargetAllowed(JsExpression target, int position)
    {
        if (target is JsIdentifierExpression or JsMemberExpression)
            return;
        throw Error("Invalid left-hand side expression in update operation", position);
    }

    private JsExpression ParseMemberAndCall()
    {
        var expr = ParsePrimary();
        var inOptionalChain = false;
        while (true)
        {
            if (current.Kind == JsTokenKind.Question && !Peek().HasLineTerminatorBefore &&
                Peek().Kind == JsTokenKind.Dot)
            {
                Next();
                Expect(JsTokenKind.Dot);
                inOptionalChain = true;

                if (current.Kind == JsTokenKind.PrivateIdentifier)
                {
                    var privateName = GetPrivateIdentifierText(current);
                    Next();
                    expr = new JsMemberExpression(expr, new JsLiteralExpression(privateName), false,
                        true, true);
                    continue;
                }

                if (Match(JsTokenKind.LeftBracket))
                {
                    if (expr is JsSuperExpression && !allowSuperProperty)
                        throw Error("Invalid use of super property access", current.Position);

                    var propExpr = ParseExpression();
                    Expect(JsTokenKind.RightBracket);
                    expr = new JsMemberExpression(expr, propExpr, true, false,
                        true);
                    continue;
                }

                if (Match(JsTokenKind.LeftParen))
                {
                    var args = ParseArgumentListAfterOpenParen();
                    expr = new JsCallExpression(expr, args, true);
                    continue;
                }

                var optionalProp = ParseIdentifierName();
                expr = new JsMemberExpression(expr, new JsLiteralExpression(optionalProp), false,
                    false, true);
                continue;
            }

            if (Match(JsTokenKind.Dot))
            {
                if (expr is JsSuperExpression && !allowSuperProperty)
                    throw Error("Invalid use of super property access", current.Position);

                if (current.Kind == JsTokenKind.PrivateIdentifier)
                {
                    var privateName = GetPrivateIdentifierText(current);
                    Next();
                    expr = new JsMemberExpression(expr, new JsLiteralExpression(privateName), false,
                        true, inOptionalChain);
                    continue;
                }

                var prop = ParseIdentifierName();
                expr = new JsMemberExpression(expr, new JsLiteralExpression(prop), false,
                    false, inOptionalChain);
                continue;
            }

            if (Match(JsTokenKind.LeftBracket))
            {
                if (expr is JsSuperExpression && !allowSuperProperty)
                    throw Error("Invalid use of super property access", current.Position);

                var propExpr = ParseExpression();
                Expect(JsTokenKind.RightBracket);
                expr = new JsMemberExpression(expr, propExpr, true, false,
                    inOptionalChain);
                continue;
            }

            if (Match(JsTokenKind.LeftParen))
            {
                if (expr is JsSuperExpression && !allowSuperCall)
                    throw Error("Invalid use of super() call", current.Position);

                var args = ParseArgumentListAfterOpenParen();
                expr = new JsCallExpression(expr, args, inOptionalChain);
                continue;
            }

            if (current.Kind == JsTokenKind.Template)
            {
                var templateToken = current;
                Next();
                var templateExpr = ParseTemplateLiteralAsTemplate(templateToken, true);
                expr = At(new JsTaggedTemplateExpression(expr, templateExpr), expr.Position);
                continue;
            }

            break;
        }

        if (expr is JsSuperExpression) throw Error("Invalid use of super", current.Position);

        return expr;
    }

    private IReadOnlyList<JsExpression> ParseArgumentListAfterOpenParen()
    {
        var args = new List<JsExpression>();
        if (current.Kind != JsTokenKind.RightParen)
            while (true)
            {
                if (Match(JsTokenKind.Ellipsis))
                    args.Add(new JsSpreadExpression(ParseAssignment(true)));
                else
                    args.Add(ParseAssignment(true));

                if (!Match(JsTokenKind.Comma)) break;

                // Allow trailing comma in call/new argument lists.
                if (current.Kind == JsTokenKind.RightParen) break;
            }

        Expect(JsTokenKind.RightParen);
        return args;
    }

    private JsExpression ParsePrimary()
    {
        var start = current.Position;
        if (current.Kind == JsTokenKind.At)
        {
            var decorators = ParseDecoratorList();
            return At(ParseClassExpression(true, decorators, start), start);
        }

        if (current.Kind == JsTokenKind.ReservedWord && CurrentSourceTextEquals("import"))
            return ParseImportPrimaryExpression(start);

        if (current.Kind == JsTokenKind.ReservedWord &&
            CurrentSourceTextEquals("class"))
            return At(ParseClassExpression(true), start);

        if (current.Kind is JsTokenKind.Slash or JsTokenKind.SlashAssign) return ParseRegExpLiteral();

        if (Match(JsTokenKind.LeftParen))
        {
            var expr = ParseExpression();
            Expect(JsTokenKind.RightParen);
            return At(expr, start);
        }

        if (current.Kind == JsTokenKind.LeftBracket) return At(ParseArrayExpression(), start);

        if (current.Kind == JsTokenKind.LeftBrace) return At(ParseObjectExpression(), start);

        if (current.Kind == JsTokenKind.Function) return At(ParseFunctionExpression(), start);

        if (current.Kind == JsTokenKind.Identifier &&
            CurrentSourceTextEquals("async") &&
            Peek().Kind == JsTokenKind.Function &&
            !Peek().HasLineTerminatorBefore)
            return At(ParseFunctionExpression(true), start);

        var tok = current;
        if (tok.Kind == JsTokenKind.String) EnsureStringLiteralAllowedInCurrentMode(tok);

        Next();
        return At<JsExpression>(tok.Kind switch
        {
            JsTokenKind.Identifier => CreateIdentifierExpression(tok),
            JsTokenKind.Of => CreateIdentifierExpression(tok),
            JsTokenKind.Let when !strictMode => CreateIdentifierExpression(tok),
            JsTokenKind.PrivateIdentifier => new JsPrivateIdentifierExpression(GetPrivateIdentifierText(tok),
                GetIdentifierId(tok)),
            JsTokenKind.This => new JsThisExpression(),
            JsTokenKind.ReservedWord when tok.SourceLength == 5 &&
                                          GetTokenSourceSpan(tok).SequenceEqual("super".AsSpan()) =>
                new JsSuperExpression(),
            JsTokenKind.Number => new JsLiteralExpression(GetLiteralValue(tok), GetTokenSourceText(tok)),
            JsTokenKind.BigInt => new JsLiteralExpression(GetLiteralValue(tok), GetTokenDisplayText(tok)),
            JsTokenKind.String => new JsLiteralExpression(GetLiteralValue(tok), GetTokenDisplayText(tok)),
            JsTokenKind.Template => ParseTemplateLiteral(tok),
            JsTokenKind.True => new JsLiteralExpression(true, GetTokenDisplayText(tok)),
            JsTokenKind.False => new JsLiteralExpression(false, GetTokenDisplayText(tok)),
            JsTokenKind.Null => new JsLiteralExpression(null, GetTokenDisplayText(tok)),
            JsTokenKind.Undefined => new JsLiteralExpression(JsValue.Undefined, GetTokenDisplayText(tok)),
            JsTokenKind.NaN => new JsLiteralExpression(double.NaN, GetTokenDisplayText(tok)),
            JsTokenKind.Infinity => new JsLiteralExpression(double.PositiveInfinity, GetTokenDisplayText(tok)),
            _ => throw Error($"Unexpected token '{GetTokenDisplayText(tok)}'", tok.Position)
        }, tok.Position);
    }

    private JsExpression CreateIdentifierExpression(JsToken tok)
    {
        //EnsureIdentifierAllowedInCurrentMode(tok.Text, tok.Position);
        return new JsIdentifierExpression(GetIdentifierText(tok), GetIdentifierId(tok));
    }

    private JsExpression ParseRegExpLiteral()
    {
        var start = current.Position;
        var i = start + 1;
        var inClass = false;
        var escaped = false;
        while (i < source.Length)
        {
            var ch = source[i];
            if (ch is '\n' or '\r' or '\u2028' or '\u2029')
                throw Error("Unterminated regular expression literal", start);

            if (!escaped)
            {
                if (ch == '[')
                    inClass = true;
                else if (ch == ']' && inClass)
                    inClass = false;
                else if (ch == '/' && !inClass) break;
            }

            escaped = !escaped && ch == '\\';
            i++;
        }

        if (i >= source.Length || source[i] != '/') throw Error("Unterminated regular expression literal", start);

        var pattern = source.Substring(start + 1, i - start - 1);
        i++;
        var flagsBuilder = new StringBuilder();
        while (i < source.Length)
        {
            var ch = source[i];
            if (IsIdentifierPartForRegExpFlag(ch))
            {
                flagsBuilder.Append(ch);
                i++;
                continue;
            }

            if (ch == '\\' &&
                i + 5 < source.Length &&
                source[i + 1] == 'u' &&
                IsHexDigit(source[i + 2]) &&
                IsHexDigit(source[i + 3]) &&
                IsHexDigit(source[i + 4]) &&
                IsHexDigit(source[i + 5]))
            {
                var codeUnit =
                    (HexToInt(source[i + 2]) << 12) |
                    (HexToInt(source[i + 3]) << 8) |
                    (HexToInt(source[i + 4]) << 4) |
                    HexToInt(source[i + 5]);
                flagsBuilder.Append((char)codeUnit);
                i += 6;
                continue;
            }

            break;
        }

        var flags = flagsBuilder.ToString();
        lexer.SetIndex(i);
        hasPeek = false;
        peek = default;
        current = lexer.NextToken();
        return At(new JsRegExpLiteralExpression(pattern, flags), start);
    }

    private JsArrayExpression ParseArrayPatternExpression()
    {
        return ParseArrayLiteralOrPattern(true);
    }

    private JsArrayExpression ParseArrayExpression()
    {
        return ParseArrayLiteralOrPattern(false);
    }

    private JsArrayExpression ParseArrayLiteralOrPattern(bool isPattern)
    {
        var start = current.Position;
        Expect(JsTokenKind.LeftBracket);
        var elements = new List<JsExpression?>(4);
        while (current.Kind != JsTokenKind.RightBracket)
        {
            if (Match(JsTokenKind.Comma))
            {
                elements.Add(null);
                continue;
            }

            if (Match(JsTokenKind.Ellipsis))
                elements.Add(new JsSpreadExpression(isPattern
                    ? ParsePatternExpressionElement()
                    : ParseAssignment(true)));
            else
                elements.Add(isPattern ? ParsePatternExpressionElement() : ParseAssignment(true));

            if (Match(JsTokenKind.Comma))
                continue;
            if (current.Kind != JsTokenKind.RightBracket)
                throw Error("Expected ',' or ']'", current.Position);
        }

        Expect(JsTokenKind.RightBracket);
        return At(new JsArrayExpression(elements), start);
    }

    private JsExpression ParsePatternExpressionElement()
    {
        var element = current.Kind switch
        {
            JsTokenKind.LeftBrace => ParseObjectBindingPattern(),
            JsTokenKind.LeftBracket => ParseArrayPatternExpression(),
            _ => ParseAssignment(true)
        };

        if (element is JsObjectExpression or JsArrayExpression && Match(JsTokenKind.Assign))
        {
            var defaultValue = ParseAssignment(true);
            element = At(new JsAssignmentExpression(JsAssignmentOperator.Assign, element, defaultValue),
                element.Position);
        }

        return element;
    }

    private JsObjectExpression ParseObjectExpression()
    {
        var start = current.Position;
        Expect(JsTokenKind.LeftBrace);
        var properties = new List<JsObjectProperty>(4);
        ObjectPropertyDuplicateTracker propertyKinds = default;
        while (current.Kind != JsTokenKind.RightBrace)
        {
            var keyStart = current.Position;
            if (Match(JsTokenKind.Ellipsis))
            {
                var spreadValue = ParseAssignment(true);
                properties.Add(At(new JsObjectProperty(string.Empty, spreadValue, JsObjectPropertyKind.Spread),
                    keyStart));
                _ = Match(JsTokenKind.Comma);
                if (current.Kind == JsTokenKind.RightBrace) break;

                continue;
            }

            var isAsyncMethod = false;
            if (current.Kind == JsTokenKind.Identifier &&
                CurrentSourceTextEquals("async") &&
                !Peek().HasLineTerminatorBefore &&
                Peek().Kind is not (JsTokenKind.LeftParen or JsTokenKind.Colon or JsTokenKind.Comma
                    or JsTokenKind.RightBrace or JsTokenKind.Assign))
            {
                isAsyncMethod = true;
                Next();
            }

            var isGeneratorMethod = Match(JsTokenKind.Star);
            var parsedKey = ParsePropertyName(false, false);
            var key = parsedKey.Key ?? string.Empty;
            var computedKey = parsedKey.ComputedKey;
            var isComputedKey = parsedKey.IsComputed;
            var shorthandAllowed = parsedKey.ShorthandAllowed;

            if (!isGeneratorMethod &&
                !isComputedKey &&
                (key == "get" || key == "set") &&
                current.Kind != JsTokenKind.LeftParen &&
                current.Kind != JsTokenKind.Colon &&
                current.Kind != JsTokenKind.Comma &&
                current.Kind != JsTokenKind.RightBrace)
            {
                var parsedAccessorKey = ParsePropertyName(false, false);
                var accessorKey = parsedAccessorKey.Key ?? string.Empty;
                var accessorComputedKey = parsedAccessorKey.ComputedKey;

                Expect(JsTokenKind.LeftParen);
                IReadOnlyList<string> accessorParameters;
                IReadOnlyList<int> accessorParameterIds;
                IReadOnlyList<JsExpression?> accessorInitializers;
                IReadOnlyList<JsExpression?> accessorParameterPatterns;
                IReadOnlyList<int> accessorParameterPositions;
                IReadOnlyList<JsFormalParameterBindingKind> accessorParameterBindingKinds;
                int accessorFunctionLength;
                bool accessorHasSimpleParameterList;
                bool accessorHasDuplicateParameters;
                if (key == "get")
                {
                    var parsedAccessorParams = ParseFormalParameterList();
                    accessorParameters = parsedAccessorParams.Parameters;
                    accessorParameterIds = parsedAccessorParams.ParameterIds;
                    accessorInitializers = parsedAccessorParams.Initializers;
                    accessorParameterPatterns = parsedAccessorParams.ParameterPatterns;
                    accessorParameterPositions = parsedAccessorParams.ParameterPositions;
                    accessorParameterBindingKinds = parsedAccessorParams.ParameterBindingKinds;
                    accessorFunctionLength = parsedAccessorParams.FunctionLength;
                    accessorHasSimpleParameterList = parsedAccessorParams.HasSimpleParameterList;
                    accessorHasDuplicateParameters = parsedAccessorParams.HasDuplicateParameters;
                    if (accessorParameters.Count != 0) throw Error("Getter must not have parameters", current.Position);
                }
                else
                {
                    var parsedAccessorParams = ParseFormalParameterList();
                    accessorParameters = parsedAccessorParams.Parameters;
                    accessorParameterIds = parsedAccessorParams.ParameterIds;
                    accessorInitializers = parsedAccessorParams.Initializers;
                    accessorParameterPatterns = parsedAccessorParams.ParameterPatterns;
                    accessorParameterPositions = parsedAccessorParams.ParameterPositions;
                    accessorParameterBindingKinds = parsedAccessorParams.ParameterBindingKinds;
                    accessorFunctionLength = parsedAccessorParams.FunctionLength;
                    accessorHasSimpleParameterList = parsedAccessorParams.HasSimpleParameterList;
                    accessorHasDuplicateParameters = parsedAccessorParams.HasDuplicateParameters;
                    if (accessorParameters.Count != 1) throw Error("Expected setter parameter", current.Position);

                    var setterParamName = accessorParameters[0];
                    var setterParamId = accessorParameterIds[0];
                    var setterParamPosition = accessorParameterPositions[0];
                    EnsureIdentifierAllowedInCurrentMode(setterParamName, setterParamId, setterParamPosition);
                    if (strictMode && IsEvalOrArguments(setterParamName, setterParamId))
                        throw Error("Unexpected eval or arguments in strict mode", setterParamPosition);

                    var strictBeforeAccessorBody = strictMode;
                    var allowSuperPropertyBeforeBody = allowSuperProperty;
                    var allowSuperCallBeforeBody = allowSuperCall;
                    allowSuperProperty = true;
                    allowSuperCall = false;
                    var setterBody = ParseBlockStatement(true);
                    allowSuperProperty = allowSuperPropertyBeforeBody;
                    allowSuperCall = allowSuperCallBeforeBody;
                    if (!strictBeforeAccessorBody &&
                        FunctionBodyHasUseStrictDirective(setterBody.Statements) &&
                        IsEvalOrArguments(setterParamName, setterParamId))
                        throw Error("Unexpected eval or arguments in strict mode", setterParamPosition);

                    var setterFunction = At(new JsFunctionExpression(
                        null,
                        accessorParameters,
                        setterBody,
                        parameterInitializers: accessorInitializers,
                        parameterPatterns: accessorParameterPatterns,
                        parameterPositions: accessorParameterPositions,
                        parameterBindingKinds: accessorParameterBindingKinds,
                        functionLength: accessorFunctionLength,
                        hasSimpleParameterList: accessorHasSimpleParameterList,
                        hasDuplicateParameters: accessorHasDuplicateParameters,
                        hasSuperBindingHint: true,
                        parameterIds: accessorParameterIds), keyStart);
                    if (accessorComputedKey is null)
                        ApplyObjectLiteralDuplicateChecks(
                            ref propertyKinds,
                            accessorKey,
                            JsObjectPropertyKind.Setter,
                            keyStart,
                            strictMode);

                    properties.Add(At(new JsObjectProperty(accessorKey, setterFunction, JsObjectPropertyKind.Setter,
                            accessorComputedKey),
                        keyStart));
                    _ = Match(JsTokenKind.Comma);
                    if (current.Kind == JsTokenKind.RightBrace) break;

                    continue;
                }

                var allowSuperPropertyBeforeGetterBody = allowSuperProperty;
                var allowSuperCallBeforeGetterBody = allowSuperCall;
                allowSuperProperty = true;
                allowSuperCall = false;
                var getterBody = ParseBlockStatement(true);
                allowSuperProperty = allowSuperPropertyBeforeGetterBody;
                allowSuperCall = allowSuperCallBeforeGetterBody;
                var getterFunction = At(new JsFunctionExpression(
                    null,
                    accessorParameters,
                    getterBody,
                    parameterInitializers: accessorInitializers,
                    parameterPatterns: accessorParameterPatterns,
                    parameterPositions: accessorParameterPositions,
                    parameterBindingKinds: accessorParameterBindingKinds,
                    functionLength: accessorFunctionLength,
                    hasSimpleParameterList: accessorHasSimpleParameterList,
                    hasDuplicateParameters: accessorHasDuplicateParameters,
                    hasSuperBindingHint: true), keyStart);
                if (accessorComputedKey is null)
                    ApplyObjectLiteralDuplicateChecks(
                        ref propertyKinds,
                        accessorKey,
                        JsObjectPropertyKind.Getter,
                        keyStart,
                        strictMode);

                properties.Add(At(new JsObjectProperty(accessorKey, getterFunction, JsObjectPropertyKind.Getter,
                        accessorComputedKey),
                    keyStart));
                _ = Match(JsTokenKind.Comma);
                if (current.Kind == JsTokenKind.RightBrace) break;

                continue;
            }

            if (current.Kind == JsTokenKind.LeftParen)
            {
                Expect(JsTokenKind.LeftParen);
                var allowSuperPropertyBeforeMethodBody = allowSuperProperty;
                var allowSuperCallBeforeMethodBody = allowSuperCall;
                allowSuperProperty = true;
                allowSuperCall = false;
                var parsedParams = ParseFormalParameterList();
                var parameters = parsedParams.Parameters;
                var parameterIds = parsedParams.ParameterIds;
                var parameterInitializers = parsedParams.Initializers;
                var parameterPatterns = parsedParams.ParameterPatterns;
                var functionLength = parsedParams.FunctionLength;
                var hasSimpleParameterList = parsedParams.HasSimpleParameterList;
                var hasDuplicateParameters = parsedParams.HasDuplicateParameters;
                var restParameterIndex = parsedParams.RestParameterIndex;
                var generatorLevelBeforeMethodBody = generatorFunctionLevel;
                var asyncLevelBeforeMethodBody = asyncFunctionLevel;
                generatorFunctionLevel = isGeneratorMethod ? generatorLevelBeforeMethodBody + 1 : 0;
                asyncFunctionLevel = isAsyncMethod ? asyncLevelBeforeMethodBody + 1 : 0;
                var body = ParseBlockStatement(true);
                allowSuperProperty = allowSuperPropertyBeforeMethodBody;
                allowSuperCall = allowSuperCallBeforeMethodBody;
                generatorFunctionLevel = generatorLevelBeforeMethodBody;
                asyncFunctionLevel = asyncLevelBeforeMethodBody;
                var methodFunction = At(new JsFunctionExpression(
                    null,
                    parameters,
                    body,
                    isGeneratorMethod,
                    isAsyncMethod,
                    parameterInitializers: parameterInitializers,
                    parameterPatterns: parameterPatterns,
                    parameterPositions: parsedParams.ParameterPositions,
                    parameterBindingKinds: parsedParams.ParameterBindingKinds,
                    functionLength: functionLength,
                    hasSimpleParameterList: hasSimpleParameterList,
                    hasSuperBindingHint: true,
                    hasDuplicateParameters: hasDuplicateParameters,
                    restParameterIndex: restParameterIndex,
                    parameterIds: parameterIds), keyStart);

                if (!isComputedKey)
                    ApplyObjectLiteralDuplicateChecks(
                        ref propertyKinds,
                        key,
                        JsObjectPropertyKind.Data,
                        keyStart,
                        strictMode);

                properties.Add(At(new JsObjectProperty(key, methodFunction, JsObjectPropertyKind.Data, computedKey),
                    keyStart));
                _ = Match(JsTokenKind.Comma);
                if (current.Kind == JsTokenKind.RightBrace) break;

                continue;
            }

            JsExpression value;
            if (Match(JsTokenKind.Colon))
            {
                value = ParseAssignment(true);
            }
            else
            {
                if (!shorthandAllowed) throw Error("Expected ':' after object property key", current.Position);

                var shorthandId = parsedKey.IdentifierId >= 0
                    ? parsedKey.IdentifierId
                    : lexer.AddIdentifierLiteral(key);
                value = At(new JsIdentifierExpression(key, shorthandId), current.Position);
            }

            if (!isComputedKey)
                ApplyObjectLiteralDuplicateChecks(
                    ref propertyKinds,
                    key,
                    JsObjectPropertyKind.Data,
                    keyStart,
                    strictMode);

            properties.Add(At(new JsObjectProperty(
                    key,
                    value,
                    JsObjectPropertyKind.Data,
                    computedKey),
                value.Position));
            _ = Match(JsTokenKind.Comma);
            if (current.Kind == JsTokenKind.RightBrace) break;
        }

        Expect(JsTokenKind.RightBrace);
        return At(new JsObjectExpression(properties), start);
    }

    private JsObjectExpression ParseObjectBindingPattern()
    {
        var start = current.Position;
        Expect(JsTokenKind.LeftBrace);
        var properties = new List<JsObjectProperty>(4);

        while (current.Kind != JsTokenKind.RightBrace)
        {
            var keyStart = current.Position;
            if (Match(JsTokenKind.Ellipsis))
            {
                var spreadValue = ParseAssignment(true);
                properties.Add(At(new JsObjectProperty(string.Empty, spreadValue, JsObjectPropertyKind.Spread),
                    keyStart));
                _ = Match(JsTokenKind.Comma);
                if (current.Kind == JsTokenKind.RightBrace)
                    break;
                continue;
            }

            var parsedKey = ParsePropertyName(false, false);
            var key = parsedKey.Key ?? string.Empty;
            var computedKey = parsedKey.ComputedKey;
            var shorthandAllowed = parsedKey.ShorthandAllowed;

            JsExpression value;
            if (Match(JsTokenKind.Colon))
            {
                value = current.Kind switch
                {
                    JsTokenKind.LeftBrace => ParseObjectBindingPattern(),
                    JsTokenKind.LeftBracket => ParseArrayPatternExpression(),
                    _ => ParseAssignment(true)
                };
                if (Match(JsTokenKind.Assign))
                {
                    var defaultValue = ParseAssignment(true);
                    value = At(new JsAssignmentExpression(JsAssignmentOperator.Assign, value, defaultValue),
                        value.Position);
                }
            }
            else
            {
                if (!shorthandAllowed)
                    throw Error("Expected ':' after object property key", current.Position);

                var shorthandId = parsedKey.IdentifierId >= 0
                    ? parsedKey.IdentifierId
                    : lexer.AddIdentifierLiteral(key);
                value = At(new JsIdentifierExpression(key, shorthandId), keyStart);
                if (Match(JsTokenKind.Assign))
                {
                    var defaultValue = ParseAssignment(true);
                    value = At(new JsAssignmentExpression(JsAssignmentOperator.Assign, value, defaultValue),
                        value.Position);
                }
            }

            properties.Add(At(new JsObjectProperty(
                    key,
                    value,
                    JsObjectPropertyKind.Data,
                    computedKey),
                value.Position));
            _ = Match(JsTokenKind.Comma);
            if (current.Kind == JsTokenKind.RightBrace)
                break;
        }

        Expect(JsTokenKind.RightBrace);
        return At(new JsObjectExpression(properties), start);
    }

    private string ParseIdentifierName()
    {
        if (!IsIdentifierNameToken(current.Kind))
            throw Error($"Expected Identifier but found {current.Kind}", current.Position);

        return ConsumeIdentifierText();
    }

    private string ParseModuleExportName()
    {
        if (current.Kind == JsTokenKind.String)
        {
            var token = current;
            Next();
            return GetStringLiteralText(token);
        }

        return ParseIdentifierName();
    }

    private JsParsedFormalParameters ParseFormalParameterList()
    {
        if (current.Kind == JsTokenKind.RightParen)
        {
            Expect(JsTokenKind.RightParen);
            return JsParsedFormalParameters.Empty;
        }

        var parameters = new List<string>(4);
        var parameterIds = new List<int>(4);
        var initializers = new List<JsExpression?>(4);
        var parameterPatterns = new List<JsExpression?>(4);
        var parameterPositions = new List<int>(4);
        var parameterBindingKinds = new List<JsFormalParameterBindingKind>(4);
        var nameCrashIdCheckSet = strictMode ? nameCrashIdSet : null;
        var nameCrashTextCheckSet = strictMode ? nameCrashTextSet : null;
        IdentifierDuplicateTracker duplicateTracker = default;
        var functionLength = 0;
        var seenDefault = false;
        var hasSimpleParameterList = true;
        var hasDuplicateParameters = false;
        var restParameterIndex = -1;

        while (true)
        {
            if (Match(JsTokenKind.Ellipsis))
            {
                hasSimpleParameterList = false;

                if (current.Kind is JsTokenKind.LeftBracket or JsTokenKind.LeftBrace)
                {
                    var patternPos = current.Position;
                    JsExpression pattern = current.Kind == JsTokenKind.LeftBrace
                        ? ParseObjectBindingPattern()
                        : ParseArrayPatternExpression();
                    var syntheticPatternName = $"$rest_pattern_{level}_{patternPos}";
                    parameters.Add(syntheticPatternName);
                    parameterIds.Add(-1);
                    parameterPatterns.Add(pattern);
                    parameterPositions.Add(patternPos);
                    parameterBindingKinds.Add(JsFormalParameterBindingKind.RestPattern);
                    initializers.Add(null);
                    restParameterIndex = parameters.Count - 1;
                    seenDefault = true;
                    if (current.Kind == JsTokenKind.Comma)
                        throw Error("Rest parameter must be last formal parameter", current.Position);
                    break;
                }

                var restParam = ExpectCheckedIdentifierName(true);
                parameters.Add(restParam.Name);
                parameterIds.Add(restParam.NameId);
                parameterPatterns.Add(null);
                parameterPositions.Add(restParam.Position);
                parameterBindingKinds.Add(JsFormalParameterBindingKind.Rest);
                initializers.Add(null);
                restParameterIndex = parameters.Count - 1;
                if (!strictMode && !duplicateTracker.Add(restParam.NameId, restParam.Name))
                    hasDuplicateParameters = true;
                if (current.Kind == JsTokenKind.Comma)
                    throw Error("Rest parameter must be last formal parameter", current.Position);
                break;
            }

            if (current.Kind is JsTokenKind.LeftBracket or JsTokenKind.LeftBrace)
            {
                hasSimpleParameterList = false;
                var patternPos = current.Position;
                JsExpression pattern = current.Kind == JsTokenKind.LeftBrace
                    ? ParseObjectBindingPattern()
                    : ParseArrayPatternExpression();
                var syntheticPatternName = $"$param_pattern_{level}_{patternPos}";
                parameters.Add(syntheticPatternName);
                parameterIds.Add(lexer.AddIdentifierLiteral(syntheticPatternName));
                parameterPatterns.Add(pattern);
                parameterPositions.Add(patternPos);
                parameterBindingKinds.Add(JsFormalParameterBindingKind.Pattern);

                JsExpression? patternInitializer = null;
                if (Match(JsTokenKind.Assign))
                {
                    patternInitializer = ParseAssignment(true);
                    seenDefault = true;
                }

                initializers.Add(patternInitializer);
                if (!seenDefault)
                    functionLength++;

                if (!Match(JsTokenKind.Comma))
                    break;
                if (current.Kind == JsTokenKind.RightParen)
                    break;
                continue;
            }

            var param = ExpectCheckedIdentifierName(true);
            var paramName = param.Name;
            var paramId = param.NameId;
            if (nameCrashIdCheckSet is not null)
                if (!TryAddIdentifierKey(nameCrashIdCheckSet, nameCrashTextCheckSet, paramId, paramName))
                {
                    nameCrashIdCheckSet.Clear();
                    nameCrashTextCheckSet!.Clear();
                    throw Error("Argument name clash.", param.Position);
                }

            parameters.Add(paramName);
            parameterIds.Add(paramId);
            parameterPatterns.Add(null);
            parameterPositions.Add(param.Position);
            parameterBindingKinds.Add(JsFormalParameterBindingKind.Plain);
            if (!strictMode && !duplicateTracker.Add(paramId, paramName)) hasDuplicateParameters = true;

            JsExpression? initializer = null;
            if (Match(JsTokenKind.Assign))
            {
                initializer = ParseAssignment(true);
                seenDefault = true;
                hasSimpleParameterList = false;
            }

            initializers.Add(initializer);
            if (!seenDefault) functionLength++;

            if (!Match(JsTokenKind.Comma)) break;

            if (current.Kind == JsTokenKind.RightParen) break;
        }

        nameCrashIdCheckSet?.Clear();
        nameCrashTextCheckSet?.Clear();
        Expect(JsTokenKind.RightParen);
        return new(
            parameters.Count == 0 ? Array.Empty<string>() : parameters.ToArray(),
            parameterIds.Count == 0 ? Array.Empty<int>() : parameterIds.ToArray(),
            initializers.Count == 0 ? Array.Empty<JsExpression?>() : initializers.ToArray(),
            parameterPatterns.Count == 0 ? Array.Empty<JsExpression?>() : parameterPatterns.ToArray(),
            parameterPositions.Count == 0 ? Array.Empty<int>() : parameterPositions.ToArray(),
            parameterBindingKinds.Count == 0
                ? Array.Empty<JsFormalParameterBindingKind>()
                : parameterBindingKinds.ToArray(),
            functionLength,
            hasSimpleParameterList,
            hasDuplicateParameters,
            restParameterIndex);
    }

    private void SkipBindingPatternTokens()
    {
        if (current.Kind is not (JsTokenKind.LeftBrace or JsTokenKind.LeftBracket))
            throw Error("Expected binding pattern", current.Position);

        var stack = new PooledTokenKindStack(stackalloc JsTokenKind[8]);
        try
        {
            stack.Push(current.Kind == JsTokenKind.LeftBrace ? JsTokenKind.RightBrace : JsTokenKind.RightBracket);
            Next();

            while (stack.Count > 0)
            {
                if (current.Kind == JsTokenKind.Eof)
                    throw Error("Unterminated binding pattern", current.Position);

                switch (current.Kind)
                {
                    case JsTokenKind.LeftBrace:
                        stack.Push(JsTokenKind.RightBrace);
                        Next();
                        break;
                    case JsTokenKind.LeftBracket:
                        stack.Push(JsTokenKind.RightBracket);
                        Next();
                        break;
                    case JsTokenKind.RightBrace:
                    case JsTokenKind.RightBracket:
                    {
                        var expected = stack.Peek();
                        if (current.Kind != expected)
                            throw Error("Invalid binding pattern", current.Position);
                        stack.Pop();
                        Next();
                        break;
                    }
                    default:
                        Next();
                        break;
                }
            }
        }
        finally
        {
            stack.Dispose();
        }
    }

    private JsExpression ParseTemplateLiteral(JsToken token)
    {
        var templateExpr = ParseTemplateLiteralAsTemplate(token);
        if (templateExpr.Expressions.Count == 0)
            return At(new JsLiteralExpression(
                templateExpr.Quasis.Count == 0 ? string.Empty : templateExpr.Quasis[0] ?? string.Empty,
                GetTokenSourceText(token)), token.Position);

        return templateExpr;
    }

    private JsTemplateExpression ParseTemplateLiteralAsTemplate(JsToken token, bool allowInvalidEscapes = false)
    {
        var rawText = GetTokenSourceText(token);
        if (rawText.Length < 2 || rawText[0] != '`' || rawText[^1] != '`')
        {
            var literalText = GetStringLiteralText(token);
            return At(
                new JsTemplateExpression(new[] { literalText }, Array.Empty<JsExpression>(), new[] { literalText }),
                token.Position);
        }

        var content = rawText.Substring(1, rawText.Length - 2);
        var cookedQuasis = new List<string?>(2);
        var rawQuasis = new List<string>(2);
        var expressions = new List<JsExpression>(1);
        var cookedBuilder = new PooledCharBuilder(stackalloc char[64]);
        var rawBuilder = new PooledCharBuilder(stackalloc char[64]);
        try
        {
            var cookedIsUndefined = false;
            var i = 0;
            while (i < content.Length)
            {
                var c = content[i];
                if (c == '\\' && i + 1 < content.Length)
                {
                    var consumed = 0;
                    var normalizeRawLineContinuation = false;
                    if (TryDecodeTemplateEscape(content, i, out var cookedEscaped, out consumed,
                            out normalizeRawLineContinuation))
                    {
                        if (normalizeRawLineContinuation)
                            rawBuilder.Append("\\\n".AsSpan());
                        else
                            rawBuilder.Append(content.AsSpan(i, consumed));
                        if (!cookedIsUndefined)
                            cookedBuilder.Append(cookedEscaped.AsSpan());
                    }
                    else
                    {
                        rawBuilder.Append(content.AsSpan(i, consumed));
                        if (!allowInvalidEscapes)
                            throw Error("Invalid escape sequence in template literal", token.Position + i);
                        cookedIsUndefined = true;
                    }

                    i += consumed;
                    continue;
                }

                if (c == '\r')
                {
                    cookedBuilder.Append('\n');
                    rawBuilder.Append('\n');
                    if (i + 1 < content.Length && content[i + 1] == '\n')
                        i++;
                    i++;
                    continue;
                }

                if (c == '$' && i + 1 < content.Length && content[i + 1] == '{')
                {
                    cookedQuasis.Add(cookedIsUndefined ? null : cookedBuilder.ToString());
                    rawQuasis.Add(rawBuilder.ToString());
                    cookedBuilder.Clear();
                    rawBuilder.Clear();
                    cookedIsUndefined = false;

                    var exprStart = i + 2;
                    var exprEnd = FindTemplateExpressionEnd(content, exprStart);
                    if (exprEnd < 0) throw Error("Unterminated template expression", token.Position + i);

                    var exprText = content.Substring(exprStart, exprEnd - exprStart);
                    expressions.Add(ParseTemplateExpression(exprText, token.Position + exprStart));
                    i = exprEnd + 1;
                    continue;
                }

                cookedBuilder.Append(c);
                rawBuilder.Append(c);
                i++;
            }

            cookedQuasis.Add(cookedIsUndefined ? null : cookedBuilder.ToString());
            rawQuasis.Add(rawBuilder.ToString());
            return At(new JsTemplateExpression(cookedQuasis, expressions, rawQuasis), token.Position);
        }
        finally
        {
            cookedBuilder.Dispose();
            rawBuilder.Dispose();
        }
    }

    private static bool TryDecodeTemplateEscape(
        string text,
        int slashIndex,
        out string decoded,
        out int consumed,
        out bool normalizeRawLineContinuation)
    {
        decoded = string.Empty;
        consumed = 1;
        normalizeRawLineContinuation = false;
        if (slashIndex + 1 >= text.Length)
            return false;

        var esc = text[slashIndex + 1];
        switch (esc)
        {
            case '`':
                decoded = "`";
                consumed = 2;
                return true;
            case '\'':
                decoded = "'";
                consumed = 2;
                return true;
            case '"':
                decoded = "\"";
                consumed = 2;
                return true;
            case '\\':
                decoded = "\\";
                consumed = 2;
                return true;
            case 'n':
                decoded = "\n";
                consumed = 2;
                return true;
            case 'r':
                decoded = "\r";
                consumed = 2;
                return true;
            case 't':
                decoded = "\t";
                consumed = 2;
                return true;
            case 'b':
                decoded = "\b";
                consumed = 2;
                return true;
            case 'f':
                decoded = "\f";
                consumed = 2;
                return true;
            case 'v':
                decoded = "\v";
                consumed = 2;
                return true;
            case '\n':
                decoded = string.Empty;
                consumed = 2;
                return true;
            case '\u2028':
                decoded = string.Empty;
                consumed = 2;
                return true;
            case '\u2029':
                decoded = string.Empty;
                consumed = 2;
                return true;
            case '\r':
                decoded = string.Empty;
                normalizeRawLineContinuation = true;
                if (slashIndex + 2 < text.Length && text[slashIndex + 2] == '\n')
                    consumed = 3;
                else
                    consumed = 2;

                return true;
            case '0':
                consumed = 2;
                if (slashIndex + 2 < text.Length && char.IsDigit(text[slashIndex + 2]))
                {
                    while (slashIndex + consumed < text.Length &&
                           consumed < 4 &&
                           text[slashIndex + consumed] is >= '0' and <= '7')
                        consumed++;
                    return false;
                }

                decoded = "\0";
                return true;
            case >= '1' and <= '9':
                consumed = 2;
                while (slashIndex + consumed < text.Length &&
                       consumed < 4 &&
                       text[slashIndex + consumed] is >= '0' and <= '7')
                    consumed++;
                return false;
            case 'x':
                consumed = 2;
                if (slashIndex + 3 < text.Length &&
                    IsHexDigit(text[slashIndex + 2]) &&
                    IsHexDigit(text[slashIndex + 3]))
                {
                    var value = HexToInt(text[slashIndex + 2]) * 16 + HexToInt(text[slashIndex + 3]);
                    decoded = ((char)value).ToString();
                    consumed = 4;
                    return true;
                }

                if (slashIndex + 2 < text.Length && IsHexDigit(text[slashIndex + 2]))
                    consumed = 3;
                return false;
            case 'u':
                consumed = 2;
                if (slashIndex + 2 < text.Length && text[slashIndex + 2] == '{')
                {
                    var j = slashIndex + 3;
                    long scalar = 0;
                    var digits = 0;
                    while (j < text.Length && IsHexDigit(text[j]))
                    {
                        scalar = scalar * 16 + HexToInt(text[j]);
                        digits++;
                        j++;
                    }

                    if (j < text.Length && text[j] == '}')
                    {
                        consumed = j - slashIndex + 1;
                        if (digits > 0 && scalar <= 0x10FFFF)
                        {
                            decoded = char.ConvertFromUtf32((int)scalar);
                            return true;
                        }

                        return false;
                    }

                    consumed = j - slashIndex;
                    if (consumed < 2)
                        consumed = 2;
                    return false;
                }

                if (slashIndex + 5 < text.Length &&
                    IsHexDigit(text[slashIndex + 2]) &&
                    IsHexDigit(text[slashIndex + 3]) &&
                    IsHexDigit(text[slashIndex + 4]) &&
                    IsHexDigit(text[slashIndex + 5]))
                {
                    var value = (HexToInt(text[slashIndex + 2]) << 12) |
                                (HexToInt(text[slashIndex + 3]) << 8) |
                                (HexToInt(text[slashIndex + 4]) << 4) |
                                HexToInt(text[slashIndex + 5]);
                    decoded = ((char)value).ToString();
                    consumed = 6;
                    return true;
                }

                return false;
            default:
                decoded = esc.ToString();
                consumed = 2;
                return true;
        }
    }

    private JsExpression ParseTemplateExpression(string source, int basePosition)
    {
        var parser = CreateNestedExpressionParser(source, basePosition);
        var expr = parser.ParseAssignment(true);
        if (parser.current.Kind != JsTokenKind.Eof)
            throw Error("Invalid template expression", basePosition + parser.current.Position);

        return expr;
    }

    private JsParser CreateNestedExpressionParser(string nestedSource, int nestedBasePosition)
    {
        return new(nestedSource, allowSuperProperty, allowSuperCall, allowTopLevelAwait, isModule, sourcePath,
            nestedBasePosition)
        {
            strictMode = strictMode,
            generatorFunctionLevel = generatorFunctionLevel,
            asyncFunctionLevel = asyncFunctionLevel
        };
    }

    private static int FindTemplateExpressionEnd(string text, int start)
    {
        var depth = 1;
        var i = start;
        var lastSignificantChar = '\0';

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '\'')
            {
                i = ConsumeQuotedString(text, i, '\'');
                lastSignificantChar = '\'';
                continue;
            }

            if (c == '"')
            {
                i = ConsumeQuotedString(text, i, '"');
                lastSignificantChar = '"';
                continue;
            }

            if (c == '`')
            {
                i = ConsumeNestedTemplateLiteral(text, i);
                lastSignificantChar = '`';
                continue;
            }

            if (c == '/' && i + 1 < text.Length)
            {
                if (text[i + 1] == '/')
                {
                    i = ConsumeLineComment(text, i);
                    continue;
                }

                if (text[i + 1] == '*')
                {
                    i = ConsumeBlockComment(text, i);
                    continue;
                }

                if (CanStartRegexLiteral(lastSignificantChar))
                {
                    i = ConsumeRegexLiteral(text, i);
                    lastSignificantChar = '/';
                    continue;
                }
            }

            if (c == '{')
            {
                depth++;
                i++;
                lastSignificantChar = '{';
                continue;
            }

            if (c == '}')
            {
                depth--;
                if (depth == 0) return i;

                i++;
                lastSignificantChar = '}';
                continue;
            }

            if (!char.IsWhiteSpace(c)) lastSignificantChar = c;

            i++;
        }

        return -1;
    }

    private static int ConsumeQuotedString(string text, int start, char quote)
    {
        var i = start + 1;
        while (i < text.Length)
        {
            if (text[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (text[i] == quote) return i + 1;

            i++;
        }

        return i;
    }

    private static int ConsumeNestedTemplateLiteral(string text, int start)
    {
        var i = start + 1;
        while (i < text.Length)
        {
            if (text[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (text[i] == '`') return i + 1;

            i++;
        }

        return i;
    }

    private static int ConsumeLineComment(string text, int start)
    {
        var i = start + 2;
        while (i < text.Length && text[i] != '\n' && text[i] != '\r') i++;

        return i;
    }

    private static int ConsumeBlockComment(string text, int start)
    {
        var i = start + 2;
        while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;

        return i + 1 < text.Length ? i + 2 : text.Length;
    }

    private static int ConsumeRegexLiteral(string text, int start)
    {
        var i = start + 1;
        var inCharacterClass = false;

        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }

            if (c == '[')
            {
                inCharacterClass = true;
                i++;
                continue;
            }

            if (c == ']' && inCharacterClass)
            {
                inCharacterClass = false;
                i++;
                continue;
            }

            if (c == '/' && !inCharacterClass)
            {
                i++;
                while (i < text.Length && IsIdentifierPartForRegExpFlag(text[i])) i++;

                return i;
            }

            i++;
        }

        return i;
    }

    private static bool CanStartRegexLiteral(char lastSignificantChar)
    {
        return lastSignificantChar == '\0' ||
               lastSignificantChar is
                   '(' or '[' or '{' or ',' or ';' or ':' or '?' or
                   '!' or '~' or '=' or '+' or '-' or '*' or '%' or '^' or '&' or '|' or '<' or '>';
    }

    private static bool IsIdentifierNameToken(JsTokenKind kind)
    {
        return kind is
            JsTokenKind.Identifier or
            JsTokenKind.True or JsTokenKind.False or JsTokenKind.Null or JsTokenKind.Undefined or
            JsTokenKind.NaN or JsTokenKind.Infinity or
            JsTokenKind.Var or JsTokenKind.Let or JsTokenKind.Const or
            JsTokenKind.If or JsTokenKind.Else or
            JsTokenKind.Return or JsTokenKind.Function or
            JsTokenKind.For or JsTokenKind.While or JsTokenKind.Do or
            JsTokenKind.Break or JsTokenKind.Continue or JsTokenKind.Debugger or
            JsTokenKind.Typeof or JsTokenKind.Void or JsTokenKind.Delete or
            JsTokenKind.Switch or JsTokenKind.Case or JsTokenKind.Default or
            JsTokenKind.Throw or JsTokenKind.Try or JsTokenKind.Catch or JsTokenKind.Finally or
            JsTokenKind.With or JsTokenKind.In or JsTokenKind.Instanceof or JsTokenKind.Of or
            JsTokenKind.New or JsTokenKind.This or JsTokenKind.ReservedWord;
    }

    private static bool IsIdentifierPartForRegExpFlag(char c)
    {
        return c == '_' || c == '$' || char.IsLetterOrDigit(c);
    }

    private static bool IsHexDigit(char c)
    {
        return c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    private static int HexToInt(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => 10 + (c - 'a'),
            >= 'A' and <= 'F' => 10 + (c - 'A'),
            _ => -1
        };
    }

    // private void UpdateProgramDirectivePrologue(JsStatement statement)
    // {
    //     if (!_programDirectivePrologue)
    //     {
    //         return;
    //     }
    //
    //     if (statement is JsExpressionStatement { Expression: JsUseStrictLiteralExpression })
    //     {
    //         _strictMode = true;
    //
    //         return;
    //     }
    //
    //     _programDirectivePrologue = false;
    // }

    private void EnsureStringLiteralAllowedInCurrentMode(JsToken token)
    {
        if (!strictMode) return;

        if (!ContainsLegacyOctalEscape(GetTokenSourceText(token))) return;

        throw Error("Octal escape sequences are not allowed in strict mode", token.Position);
    }

    private void EnsureIdentifierAllowedInCurrentMode(string name, int nameId, int position,
        bool isParamDeclaration = false)
    {
        if (!strictMode) return;

        if (isParamDeclaration)
        {
            if (IsIdentifierText(name, nameId, "eval", ref evalIdentifierId))
                throw Error("Binding 'eval' in strict mode.", position);
            if (IsIdentifierText(name, nameId, "arguments", ref argumentsIdentifierId))
                throw Error("Binding 'arguments' in strict mode.", position);
        }

        if (StrictModeReservedWords.Contains(name))
        {
            if (IsEvalOrArguments(name, nameId)) throw Error("Unexpected eval or arguments in strict mode", position);

            throw Error($"Unexpected strict mode reserved word '{name}'", position);
        }
    }

    private static bool FunctionBodyHasUseStrictDirective(IReadOnlyList<JsStatement> statements)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not JsExpressionStatement { Expression: JsLiteralExpression literal }) return false;

            if (literal.Value is not string text) return false;

            if (string.Equals(text, "use strict", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private bool IsEvalOrArguments(string name, int nameId)
    {
        return IsIdentifierText(name, nameId, "eval", ref evalIdentifierId) ||
               IsIdentifierText(name, nameId, "arguments", ref argumentsIdentifierId);
    }

    private bool IsIdentifierText(string name, int nameId, string expected, ref int expectedIdCache)
    {
        if (nameId >= 0)
        {
            if (expectedIdCache < 0 && lexer.IdentifierTable.TryGetIdentifierId(expected, out var expectedId))
                expectedIdCache = expectedId;

            if (expectedIdCache >= 0)
                return nameId == expectedIdCache;
        }

        return string.Equals(name, expected, StringComparison.Ordinal);
    }

    private void ApplyObjectLiteralDuplicateChecks(
        ref ObjectPropertyDuplicateTracker propertyKinds,
        string key,
        JsObjectPropertyKind kind,
        int position,
        bool strictMode)
    {
        const byte dataMask = 1;
        const byte getMask = 2;
        const byte setMask = 4;

        var nextMask = kind switch
        {
            JsObjectPropertyKind.Data => dataMask,
            JsObjectPropertyKind.Getter => getMask,
            JsObjectPropertyKind.Setter => setMask,
            _ => dataMask
        };

        if (!propertyKinds.TryGetMask(key, out var previousMask))
        {
            propertyKinds.SetMask(key, nextMask);
            return;
        }

        var previousIsData = (previousMask & dataMask) != 0;
        var nextIsData = nextMask == dataMask;
        if (key == "__proto__" && previousIsData && nextIsData)
        {
        }

        propertyKinds.SetMask(key, (byte)(previousMask | nextMask));
    }

    private static bool ContainsLegacyOctalEscape(string rawTokenText)
    {
        for (var i = 1; i + 1 < rawTokenText.Length; i++)
        {
            if (rawTokenText[i] != '\\') continue;

            var slashCount = 1;
            for (var j = i - 1; j >= 0 && rawTokenText[j] == '\\'; j--) slashCount++;

            if ((slashCount & 1) == 0 || i + 1 >= rawTokenText.Length) continue;

            var next = rawTokenText[i + 1];
            if (next is >= '1' and <= '9') return true;

            if (next == '0' &&
                i + 2 < rawTokenText.Length &&
                rawTokenText[i + 2] is >= '0' and <= '9')
                return true;
        }

        return false;
    }

    private void ConsumeOptionalSemicolon()
    {
        if (Match(JsTokenKind.Semicolon)) return;

        if (current.Kind is JsTokenKind.Eof or JsTokenKind.RightBrace || current.HasLineTerminatorBefore) return;

        throw Error($"Expected Semicolon but found {current.Kind}", current.Position);
    }

    private bool Match(JsTokenKind kind)
    {
        if (current.Kind != kind) return false;

        Next();
        return true;
    }

    private JsToken Expect(JsTokenKind kind)
    {
        if (current.Kind != kind) throw Error($"Expected {kind} but found {current.Kind}", current.Position);

        var tok = current;
        Next();
        return tok;
    }

    private void Next()
    {
        if (hasPeek)
        {
            current = peek;
            hasPeek = false;
            peek = default;
            return;
        }

        current = lexer.NextToken();
    }

    private JsToken Peek()
    {
        if (hasPeek) return peek;

        peek = lexer.NextToken();
        hasPeek = true;
        return peek;
    }

    private T At<T>(T node, int position) where T : JsNode
    {
        node.Position = position + basePosition;
        return node;
    }

    private JsParseException Error(string message, int position)
    {
        return new(message, position + basePosition, locationSource);
    }

    private readonly struct ParserSnapshot(
        int lexerIndex,
        JsToken current,
        bool hasPeek,
        JsToken peek,
        int asyncFunctionLevel,
        int generatorFunctionLevel,
        bool strictMode,
        bool allowSuperProperty,
        bool allowSuperCall,
        int nestedFunctionTrackingDepth,
        ulong nestedFunctionTrackingMask)
    {
        public readonly int LexerIndex = lexerIndex;
        public readonly JsToken Current = current;
        public readonly bool HasPeek = hasPeek;
        public readonly JsToken Peek = peek;
        public readonly int AsyncFunctionLevel = asyncFunctionLevel;
        public readonly int GeneratorFunctionLevel = generatorFunctionLevel;
        public readonly bool StrictMode = strictMode;
        public readonly bool AllowSuperProperty = allowSuperProperty;
        public readonly bool AllowSuperCall = allowSuperCall;
        public readonly int NestedFunctionTrackingDepth = nestedFunctionTrackingDepth;
        public readonly ulong NestedFunctionTrackingMask = nestedFunctionTrackingMask;
    }

    private readonly record struct LogicalParseInfo(JsExpression Expression, bool HasLogicalAndOr);
}
