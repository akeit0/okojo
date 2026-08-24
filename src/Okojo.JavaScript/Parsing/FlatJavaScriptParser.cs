using System.Buffers;
using System.Runtime.InteropServices;

namespace Okojo.JavaScript.Parsing;

internal sealed class FlatJavaScriptParser
{
    private readonly FlatAst ast;
    private readonly JsLexer lexer;
    private readonly string source;
    private readonly bool isModule;
    private JsToken current;
    private int functionDepth;
    private int receiverFunctionDepth;
    private int generatorFunctionDepth;
    private int asyncFunctionDepth;
    private int loopDepth;
    private int switchDepth;
    private bool strictMode;
    private bool parsingAsyncParameters;
    private bool deferringAsyncParameterErrors;
    private bool allowSuperCall;
    private bool allowSuperProperty;
    private bool superPropertySeen;
    private bool forbidClassFieldArguments;
    private bool forbidReturnInClassStaticBlock;
    private PrivateNameScope? privateNameScope;
    private int deferredAsyncParameterErrorPosition = -1;
    private List<ActiveLabel>? activeLabels;
    private HashSet<string>? moduleImportBindings;
    private HashSet<string>? moduleExportNames;

    private FlatJavaScriptParser(string source, string? sourcePath, bool isModule = false)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        ast = new FlatAst(source, sourcePath);
        this.isModule = isModule;
        ast.IsModule = isModule;
        if (isModule)
        {
            strictMode = true;
            ast.StrictDeclared = true;
        }
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

    public static FlatAst ParseModule(string source, string? sourcePath = null)
    {
        var parser = new FlatJavaScriptParser(source, sourcePath, isModule: true);
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
                var statement = isModule ? ParseModuleItem() : ParseStatement();
                if (!isModule && functionDepth == 0 && IsUsingLikeDeclaration(statement))
                    throw Error(
                        "using declarations are not allowed at the top level of scripts",
                        Arena.GetPosition(statement)
                    );
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
            var program = Arena.Add(AstKind.Program, children.Offset, children.Count);
            if (isModule)
            {
                ValidateModuleBindings(program);
                ast.FinalizeModuleDescriptor();
            }
            return program;
        }
        finally
        {
            statements.Dispose();
        }
    }

    private void ValidateModuleBindings(int program)
    {
        var bindings = new Dictionary<string, (bool IsVar, int Position)>(StringComparer.Ordinal);
        var statements = Arena.ChildRange(Arena[program].Arg0, Arena[program].Arg1);
        for (var i = 0; i < statements.Length; i++)
            CollectModuleBindings(statements[i], isTopLevel: true, bindings);

        for (var i = 0; i < statements.Length; i++)
        {
            ref readonly var statement = ref Arena[statements[i]];
            if (statement.Kind != AstKind.ExportDeclaration)
                continue;
            var entries = ast.GetExportEntries(statement);
            for (var j = 0; j < entries.Length; j++)
            {
                ref readonly var entry = ref entries[j];
                if (entry.Kind != FlatExportKind.Local)
                    continue;
                var name = Arena.GetString(entry.LocalNameStringIndex);
                if (!bindings.ContainsKey(name))
                    throw Error($"Exported binding '{name}' is not declared", entry.Position);
            }
        }

        foreach (var binding in bindings)
            if (binding.Value.IsVar)
                (ast.ModuleVarBindings ??= new(StringComparer.Ordinal)).Add(binding.Key);
    }

    private void CollectModuleBindings(
        int statementIndex,
        bool isTopLevel,
        Dictionary<string, (bool IsVar, int Position)> bindings
    )
    {
        ref readonly var statement = ref Arena[statementIndex];
        switch (statement.Kind)
        {
            case AstKind.ImportDeclaration:
                var imports = ast.GetImportEntries(statement);
                for (var i = 0; i < imports.Length; i++)
                    DeclareModuleBinding(
                        Arena.GetString(imports[i].LocalNameStringIndex),
                        isVar: false,
                        imports[i].Position,
                        bindings
                    );
                return;
            case AstKind.ExportDeclaration:
                if (statement.Arg0 < 0)
                    return;
                var exports = ast.GetExportEntries(statement);
                if (exports.Length == 0)
                    return;
                if (exports[0].Kind == FlatExportKind.Local)
                    CollectModuleBindings(statement.Arg0, isTopLevel: true, bindings);
                else if (exports[0].Kind == FlatExportKind.DefaultDeclaration)
                {
                    var name = Arena.GetString(exports[0].LocalNameStringIndex);
                    if (name.Length != 0 && name[0] != '\0')
                        DeclareModuleBinding(name, isVar: false, exports[0].Position, bindings);
                }
                return;
            case AstKind.VariableDeclaration:
                var kind = (JsVariableDeclarationKind)statement.Arg2;
                if (!isTopLevel && kind != JsVariableDeclarationKind.Var)
                    return;
                var declarators = Arena.ChildRange(statement.Arg0, statement.Arg1);
                for (var i = 0; i < declarators.Length; i++)
                {
                    ref readonly var declarator = ref Arena[declarators[i]];
                    if (declarator.Kind == AstKind.VariableDeclarator)
                        DeclareModuleBinding(
                            Arena.GetString(declarator.Arg0),
                            kind == JsVariableDeclarationKind.Var,
                            Arena.GetPosition(declarators[i]),
                            bindings
                        );
                    else
                        CollectPatternModuleBindings(
                            declarator.Arg0,
                            kind == JsVariableDeclarationKind.Var,
                            bindings
                        );
                }
                return;
            case AstKind.FunctionDeclaration when isTopLevel:
                var function = ast.GetFunction(statement.Arg0);
                DeclareModuleBinding(
                    ast.GetString(function.NameStringIndex),
                    isVar: false,
                    function.Position,
                    bindings
                );
                return;
            case AstKind.ClassDeclaration when isTopLevel:
                var info = ast.GetClass(statement.Arg0);
                DeclareModuleBinding(
                    ast.GetString(info.NameStringIndex),
                    isVar: false,
                    info.Position,
                    bindings
                );
                return;
            case AstKind.BlockStatement:
                var blockStatements = Arena.ChildRange(statement.Arg0, statement.Arg1);
                for (var i = 0; i < blockStatements.Length; i++)
                    CollectModuleBindings(blockStatements[i], isTopLevel: false, bindings);
                return;
            case AstKind.IfStatement:
                CollectModuleBindings(statement.Arg1, isTopLevel: false, bindings);
                if (statement.Arg2 >= 0)
                    CollectModuleBindings(statement.Arg2, isTopLevel: false, bindings);
                return;
            case AstKind.WhileStatement:
                CollectModuleBindings(statement.Arg1, isTopLevel: false, bindings);
                return;
            case AstKind.DoWhileStatement:
                CollectModuleBindings(statement.Arg0, isTopLevel: false, bindings);
                return;
            case AstKind.ForStatement:
                var forParts = Arena.ChildRange(statement.Arg0, statement.Arg1);
                if (forParts[0] >= 0 && Arena[forParts[0]].Kind == AstKind.VariableDeclaration)
                    CollectModuleBindings(forParts[0], isTopLevel: false, bindings);
                CollectModuleBindings(forParts[3], isTopLevel: false, bindings);
                return;
            case AstKind.ForInOfStatement:
                var iterationParts = Arena.ChildRange(statement.Arg0, statement.Arg1);
                if (Arena[iterationParts[0]].Kind == AstKind.VariableDeclaration)
                    CollectModuleBindings(iterationParts[0], isTopLevel: false, bindings);
                CollectModuleBindings(iterationParts[2], isTopLevel: false, bindings);
                return;
            case AstKind.LabeledStatement:
                CollectModuleBindings(statement.Arg1, isTopLevel: false, bindings);
                return;
            case AstKind.TryStatement:
                CollectModuleBindings(statement.Arg0, isTopLevel: false, bindings);
                if (statement.Arg1 >= 0)
                    CollectModuleBindings(Arena[statement.Arg1].Arg1, isTopLevel: false, bindings);
                if (statement.Arg2 >= 0)
                    CollectModuleBindings(statement.Arg2, isTopLevel: false, bindings);
                return;
            case AstKind.SwitchStatement:
                var cases = Arena.ChildRange(statement.Arg1, statement.Arg2);
                for (var i = 0; i < cases.Length; i++)
                {
                    ref readonly var switchCase = ref Arena[cases[i]];
                    var caseStatements = Arena.ChildRange(switchCase.Arg1, switchCase.Arg2);
                    for (var j = 0; j < caseStatements.Length; j++)
                        CollectModuleBindings(caseStatements[j], isTopLevel: false, bindings);
                }
                return;
        }
    }

    private void CollectPatternModuleBindings(
        int pattern,
        bool isVar,
        Dictionary<string, (bool IsVar, int Position)> bindings
    )
    {
        ref readonly var node = ref Arena[pattern];
        switch (node.Kind)
        {
            case AstKind.Identifier:
                DeclareModuleBinding(
                    Arena.GetString(node.Arg0),
                    isVar,
                    Arena.GetPosition(pattern),
                    bindings
                );
                return;
            case AstKind.AssignmentExpression:
            case AstKind.SpreadElement:
                CollectPatternModuleBindings(node.Arg0, isVar, bindings);
                return;
            case AstKind.ArrayBindingPattern:
                var children = Arena.ChildRange(node.Arg0, node.Arg1);
                for (var i = 0; i < children.Length; i++)
                    if (children[i] >= 0)
                        CollectPatternModuleBindings(children[i], isVar, bindings);
                return;
            case AstKind.ObjectBindingPattern:
                var properties = ast.GetObjectProperties(node.Arg0, node.Arg1);
                for (var i = 0; i < properties.Length; i++)
                    CollectPatternModuleBindings(properties[i].ValueNode, isVar, bindings);
                return;
        }
    }

    private void DeclareModuleBinding(
        string name,
        bool isVar,
        int position,
        Dictionary<string, (bool IsVar, int Position)> bindings
    )
    {
        if (!bindings.TryGetValue(name, out var existing))
        {
            bindings.Add(name, (isVar, position));
            return;
        }
        if (isVar && existing.IsVar)
            return;
        throw Error($"Duplicate module binding '{name}'", position);
    }

    private int ParseModuleItem()
    {
        if (
            IsCurrentIdentifierName("import")
            && PeekToken().Kind is not (JsTokenKind.LeftParen or JsTokenKind.Dot)
        )
            return ParseImportDeclaration();
        if (IsCurrentIdentifierName("export"))
            return ParseExportDeclaration();
        return ParseStatement();
    }

    private int ParseExportDeclaration()
    {
        var position = current.Position;
        Next();
        if (Match(JsTokenKind.Star))
            return ParseExportStar(position);
        if (Match(JsTokenKind.LeftBrace))
            return ParseExportSpecifiers(position);
        if (Match(JsTokenKind.Default))
            return ParseExportDefault(position);

        int declaration;
        if (current.Kind is JsTokenKind.Var or JsTokenKind.Let or JsTokenKind.Const)
            declaration = ParseVariableDeclaration(consumeSemicolon: true);
        else if (current.Kind == JsTokenKind.Function)
            declaration = ParseFunctionDeclaration();
        else if (IsAsyncFunctionPrefix())
            declaration = ParseFunction(isDeclaration: true, isAsync: true);
        else if (IsCurrentIdentifierName("class"))
            declaration = ParseClass(isDeclaration: true);
        else
            throw Error("Unsupported export declaration", current.Position);

        var entries = new List<FlatExportEntry>();
        CollectDeclarationExportEntries(declaration, entries);
        return AddExportNode(position, declaration, CollectionsMarshal.AsSpan(entries));
    }

    private int ParseExportStar(int position)
    {
        var exportedName = -1;
        var kind = FlatExportKind.Star;
        if (IsCurrentIdentifierName("as"))
        {
            Next();
            var namePosition = current.Position;
            var name = ParseModuleExportName();
            DeclareModuleExportName(name, namePosition);
            exportedName = Arena.AddString(name);
            kind = FlatExportKind.Namespace;
        }
        ExpectContextualKeyword("from");
        var sourceToken = Expect(JsTokenKind.String);
        var requestIndex = AddModuleRequest(sourceToken, ParseImportAttributes());
        ConsumeSemicolon();
        Span<FlatExportEntry> entries = [new(requestIndex, -1, -1, exportedName, kind, position)];
        return AddExportNode(position, -1, entries);
    }

    private int ParseExportSpecifiers(int position)
    {
        var pending = new List<PendingExportSpecifier>(4);
        while (current.Kind != JsTokenKind.RightBrace)
        {
            var localPosition = current.Position;
            var localWasString = current.Kind == JsTokenKind.String;
            var localName = ParseModuleExportName();
            var exportedName = localName;
            if (IsCurrentIdentifierName("as"))
            {
                Next();
                exportedName = ParseModuleExportName();
            }
            pending.Add(new(localName, exportedName, localWasString, localPosition));
            if (!Match(JsTokenKind.Comma))
                break;
            if (current.Kind == JsTokenKind.RightBrace)
                break;
        }
        Expect(JsTokenKind.RightBrace);

        var requestIndex = -1;
        if (IsCurrentIdentifierName("from"))
        {
            Next();
            var sourceToken = Expect(JsTokenKind.String);
            requestIndex = AddModuleRequest(sourceToken, ParseImportAttributes());
        }
        ConsumeSemicolon();

        var entries = new FlatExportEntry[pending.Count];
        for (var i = 0; i < pending.Count; i++)
        {
            var specifier = pending[i];
            if (requestIndex < 0 && specifier.LocalWasString)
                throw Error("String export names require a source module", specifier.Position);
            DeclareModuleExportName(specifier.ExportedName, specifier.Position);
            entries[i] = new(
                requestIndex,
                requestIndex < 0 ? Arena.AddString(specifier.LocalName) : -1,
                requestIndex >= 0 ? Arena.AddString(specifier.LocalName) : -1,
                Arena.AddString(specifier.ExportedName),
                requestIndex < 0 ? FlatExportKind.Local : FlatExportKind.Indirect,
                specifier.Position
            );
        }
        return AddExportNode(position, -1, entries);
    }

    private int ParseExportDefault(int position)
    {
        int value;
        var kind = FlatExportKind.DefaultExpression;
        if (current.Kind == JsTokenKind.Function)
        {
            value = ParseFunctionExpression();
            kind = FlatExportKind.DefaultDeclaration;
        }
        else if (IsAsyncFunctionPrefix())
        {
            value = ParseFunctionExpression(isAsync: true);
            kind = FlatExportKind.DefaultDeclaration;
        }
        else if (IsCurrentIdentifierName("class"))
        {
            value = ParseClass(isDeclaration: false);
            kind = FlatExportKind.DefaultDeclaration;
        }
        else
        {
            value = ParseAssignment(allowIn: true);
            ConsumeSemicolon();
        }
        DeclareModuleExportName("default", position);
        var localName = GetDefaultExportLocalName(value);
        Span<FlatExportEntry> entries =
        [
            new(-1, Arena.AddString(localName), -1, Arena.AddString("default"), kind, position),
        ];
        return AddExportNode(position, value, entries);
    }

    private string GetDefaultExportLocalName(int value)
    {
        ref readonly var node = ref Arena[value];
        if (node.Kind is AstKind.FunctionExpression or AstKind.ArrowFunctionExpression)
        {
            var name = ast.GetString(ast.GetFunction(node.Arg0).NameStringIndex);
            return name.Length == 0 ? "\0default" : name;
        }
        if (node.Kind == AstKind.ClassExpression)
        {
            var name = ast.GetString(ast.GetClass(node.Arg0).NameStringIndex);
            return name.Length == 0 ? "\0default" : name;
        }
        return "\0default";
    }

    private void CollectDeclarationExportEntries(int declaration, List<FlatExportEntry> entries)
    {
        ref readonly var node = ref Arena[declaration];
        if (node.Kind == AstKind.VariableDeclaration)
        {
            var declarators = Arena.ChildRange(node.Arg0, node.Arg1);
            for (var i = 0; i < declarators.Length; i++)
            {
                ref readonly var declarator = ref Arena[declarators[i]];
                if (declarator.Kind == AstKind.VariableDeclarator)
                    AddLocalExportEntry(Arena.GetString(declarator.Arg0), declarators[i], entries);
                else
                    CollectPatternExportEntries(declarator.Arg0, entries);
            }
            return;
        }
        var name = node.Kind switch
        {
            AstKind.FunctionDeclaration => ast.GetString(
                ast.GetFunction(node.Arg0).NameStringIndex
            ),
            AstKind.ClassDeclaration => ast.GetString(ast.GetClass(node.Arg0).NameStringIndex),
            _ => throw new InvalidOperationException($"Invalid exported declaration {node.Kind}."),
        };
        AddLocalExportEntry(name, declaration, entries);
    }

    private void CollectPatternExportEntries(int pattern, List<FlatExportEntry> entries)
    {
        ref readonly var node = ref Arena[pattern];
        switch (node.Kind)
        {
            case AstKind.Identifier:
                AddLocalExportEntry(Arena.GetString(node.Arg0), pattern, entries);
                return;
            case AstKind.AssignmentExpression:
            case AstKind.SpreadElement:
                CollectPatternExportEntries(node.Arg0, entries);
                return;
            case AstKind.ArrayBindingPattern:
                var children = Arena.ChildRange(node.Arg0, node.Arg1);
                for (var i = 0; i < children.Length; i++)
                    if (children[i] >= 0)
                        CollectPatternExportEntries(children[i], entries);
                return;
            case AstKind.ObjectBindingPattern:
                var properties = ast.GetObjectProperties(node.Arg0, node.Arg1);
                for (var i = 0; i < properties.Length; i++)
                    CollectPatternExportEntries(properties[i].ValueNode, entries);
                return;
            default:
                throw new InvalidOperationException($"Invalid exported binding {node.Kind}.");
        }
    }

    private void AddLocalExportEntry(string name, int node, List<FlatExportEntry> entries)
    {
        var position = Arena.GetPosition(node);
        DeclareModuleExportName(name, position);
        var nameIndex = Arena.AddString(name);
        entries.Add(new(-1, nameIndex, -1, nameIndex, FlatExportKind.Local, position));
    }

    private int AddExportNode(int position, int value, ReadOnlySpan<FlatExportEntry> entries)
    {
        var range = ast.AddExportEntries(entries);
        return Arena.Add(AstKind.ExportDeclaration, value, range.Offset, range.Count, position);
    }

    private int ParseImportDeclaration()
    {
        var position = current.Position;
        Next();
        List<FlatImportEntry>? pending = null;

        if (current.Kind != JsTokenKind.String)
        {
            pending = new(4);
            if (current.Kind == JsTokenKind.Identifier)
            {
                pending.Add(ParseImportBinding("default", FlatImportKind.Default));
                if (Match(JsTokenKind.Comma))
                    ParseImportClauseTail(pending);
            }
            else
                ParseImportClauseTail(pending);

            ExpectContextualKeyword("from");
        }

        var sourceToken = Expect(JsTokenKind.String);
        var attributes = ParseImportAttributes();
        ConsumeSemicolon();
        var requestIndex = AddModuleRequest(sourceToken, attributes);
        var entries = pending is null
            ? Span<FlatImportEntry>.Empty
            : CollectionsMarshal.AsSpan(pending);
        for (var i = 0; i < entries.Length; i++)
            entries[i] = entries[i] with { ModuleRequestIndex = requestIndex };
        var range = ast.AddImportEntries(entries);
        return Arena.Add(
            AstKind.ImportDeclaration,
            requestIndex,
            range.Offset,
            range.Count,
            position
        );
    }

    private void ParseImportClauseTail(List<FlatImportEntry> entries)
    {
        if (Match(JsTokenKind.Star))
        {
            ExpectContextualKeyword("as");
            entries.Add(ParseImportBinding("*", FlatImportKind.Namespace));
            return;
        }
        Expect(JsTokenKind.LeftBrace);
        while (current.Kind != JsTokenKind.RightBrace)
        {
            var importedPosition = current.Position;
            var importedToken = current;
            var importedName = ParseModuleExportName();
            if (IsCurrentIdentifierName("as"))
            {
                Next();
                entries.Add(ParseImportBinding(importedName, FlatImportKind.Named));
            }
            else
            {
                if (importedToken.Kind != JsTokenKind.Identifier)
                    throw Error("Imported name requires a local alias", importedPosition);
                entries.Add(
                    new(
                        -1,
                        Arena.AddString(importedName),
                        Arena.AddString(importedName),
                        importedToken.IdentifierId,
                        FlatImportKind.Named,
                        importedPosition
                    )
                );
                DeclareModuleImportBinding(importedName, importedPosition);
            }
            if (!Match(JsTokenKind.Comma))
                break;
            if (current.Kind == JsTokenKind.RightBrace)
                break;
        }
        Expect(JsTokenKind.RightBrace);
    }

    private FlatImportEntry ParseImportBinding(string importedName, FlatImportKind kind)
    {
        var token = ExpectIdentifier();
        ValidateBindingIdentifier(token);
        var localName = GetIdentifierText(token);
        DeclareModuleImportBinding(localName, token.Position);
        return new(
            -1,
            Arena.AddString(importedName),
            Arena.AddString(localName),
            token.IdentifierId,
            kind,
            token.Position
        );
    }

    private string ParseModuleExportName()
    {
        var token = current;
        if (token.Kind == JsTokenKind.String)
        {
            Next();
            return lexer.GetStringLiteral(token);
        }
        if (!JsTokenFacts.IsIdentifierName(token.Kind))
            throw Error("Expected module export name", token.Position);
        Next();
        return GetIdentifierText(token);
    }

    private List<FlatImportAttribute>? ParseImportAttributes()
    {
        if (current.Kind != JsTokenKind.With && !IsCurrentIdentifierName("with"))
            return null;
        Next();
        Expect(JsTokenKind.LeftBrace);
        var attributes = new List<FlatImportAttribute>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (current.Kind != JsTokenKind.RightBrace)
        {
            var position = current.Position;
            var key =
                current.Kind == JsTokenKind.String
                    ? lexer.GetStringLiteral(current)
                    : GetObjectPropertyName(current);
            Next();
            Expect(JsTokenKind.Colon);
            var valueToken = Expect(JsTokenKind.String);
            if (!seen.Add(key))
                throw Error($"Duplicate import attribute key '{key}'", position);
            attributes.Add(
                new(
                    Arena.AddString(key),
                    Arena.AddString(lexer.GetStringLiteral(valueToken)),
                    position
                )
            );
            if (!Match(JsTokenKind.Comma))
                break;
            if (current.Kind == JsTokenKind.RightBrace)
                break;
        }
        Expect(JsTokenKind.RightBrace);
        return attributes;
    }

    private int AddModuleRequest(in JsToken sourceToken, List<FlatImportAttribute>? attributes) =>
        ast.AddModuleRequest(
            Arena.AddString(lexer.GetStringLiteral(sourceToken)),
            sourceToken.Position,
            attributes is null
                ? ReadOnlySpan<FlatImportAttribute>.Empty
                : CollectionsMarshal.AsSpan(attributes)
        );

    private void ExpectContextualKeyword(string value)
    {
        if (!IsCurrentIdentifierName(value))
            throw Error($"Expected '{value}'", current.Position);
        Next();
    }

    private void DeclareModuleImportBinding(string name, int position)
    {
        moduleImportBindings ??= new(StringComparer.Ordinal);
        if (!moduleImportBindings.Add(name))
            throw Error($"Duplicate module binding '{name}'", position);
    }

    private void DeclareModuleExportName(string name, int position)
    {
        moduleExportNames ??= new(StringComparer.Ordinal);
        if (!moduleExportNames.Add(name))
            throw Error($"Duplicate export name '{name}'", position);
    }

    private readonly record struct PendingExportSpecifier(
        string LocalName,
        string ExportedName,
        bool LocalWasString,
        int Position
    );

    private int ParseStatement()
    {
        var position = current.Position;
        if (IsCurrentIdentifierName("class"))
            return ParseClass(isDeclaration: true);
        if (IsAsyncFunctionPrefix())
            return ParseFunction(isDeclaration: true, isAsync: true);
        if (current.Kind == JsTokenKind.Identifier && PeekToken().Kind == JsTokenKind.Colon)
            return ParseLabeledStatement();
        if (
            current.Kind == JsTokenKind.Identifier
            && TryGetUsingDeclarationStatementKind(out var usingKind)
        )
            return ParseUsingDeclaration(usingKind);
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
            JsTokenKind.Debugger => ParseDebuggerStatement(),
            JsTokenKind.With => throw UnsupportedStatement(current.Kind),
            _ => ParseExpressionStatement(),
        };
    }

    private int ParseEmptyStatement(int position)
    {
        Next();
        return Arena.Add(AstKind.EmptyStatement, position: position);
    }

    private int ParseDebuggerStatement()
    {
        var position = Expect(JsTokenKind.Debugger).Position;
        ConsumeSemicolon();
        return Arena.Add(AstKind.DebuggerStatement, position: position);
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

    private bool IsUsingDeclarationStart()
    {
        if (current.Kind != JsTokenKind.Identifier || !IsCurrentIdentifierName("using"))
            return false;
        var next = PeekToken();
        return !next.HasLineTerminatorBefore
            && next.Kind is JsTokenKind.Identifier or JsTokenKind.Of;
    }

    private bool IsAwaitUsingPrefix()
    {
        if (!IsCurrentIdentifierName("await"))
            return false;
        var index = lexer.GetIndex();
        try
        {
            var second = lexer.NextToken();
            if (
                second.HasLineTerminatorBefore
                || second.Kind != JsTokenKind.Identifier
                || !source
                    .AsSpan(second.Position, second.SourceLength)
                    .SequenceEqual("using".AsSpan())
            )
                return false;
            var third = lexer.NextToken();
            return !third.HasLineTerminatorBefore
                && third.Kind is JsTokenKind.Identifier or JsTokenKind.Of;
        }
        finally
        {
            lexer.SetIndex(index);
        }
    }

    private bool TryGetUsingDeclarationStatementKind(out JsVariableDeclarationKind kind)
    {
        if (IsUsingDeclarationStart())
        {
            kind = JsVariableDeclarationKind.Using;
            return true;
        }

        if ((asyncFunctionDepth > 0 || (isModule && functionDepth == 0)) && IsAwaitUsingPrefix())
        {
            kind = JsVariableDeclarationKind.AwaitUsing;
            return true;
        }

        kind = default;
        return false;
    }

    private int ParseUsingDeclaration(
        JsVariableDeclarationKind kind,
        bool consumeSemicolon = true,
        bool allowMissingInitializer = false
    )
    {
        var position = current.Position;
        if (kind == JsVariableDeclarationKind.AwaitUsing)
        {
            if (asyncFunctionDepth == 0 && isModule && functionDepth == 0)
                ast.HasTopLevelAwait = true;
            Expect(current.Kind);
        }

        Expect(JsTokenKind.Identifier);

        Span<int> initial = stackalloc int[4];
        var declarators = new NodeList(initial);
        try
        {
            do
            {
                var identifier = ExpectIdentifier();
                ValidateBindingIdentifier(identifier);
                if (!Match(JsTokenKind.Assign))
                {
                    if (allowMissingInitializer)
                    {
                        declarators.Add(
                            Arena.Add(
                                AstKind.VariableDeclarator,
                                Arena.AddString(GetIdentifierText(identifier)),
                                identifier.IdentifierId,
                                -1,
                                identifier.Position
                            )
                        );
                        continue;
                    }
                    throw Error(
                        $"{(kind == JsVariableDeclarationKind.AwaitUsing ? "await using" : "using")} declaration requires an initializer",
                        identifier.Position
                    );
                }
                var initializer = ParseAssignment(allowIn: true);
                declarators.Add(
                    Arena.Add(
                        AstKind.VariableDeclarator,
                        Arena.AddString(GetIdentifierText(identifier)),
                        identifier.IdentifierId,
                        initializer,
                        identifier.Position
                    )
                );
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

    private bool IsUsingLikeDeclaration(int statementIndex)
    {
        ref readonly var node = ref Arena[statementIndex];
        if (node.Kind != AstKind.VariableDeclaration)
            return false;
        return ((JsVariableDeclarationKind)node.Arg2).IsUsingLike();
    }

    private int ParseVariableDeclaration(
        bool consumeSemicolon,
        bool allowMissingInitializer = false,
        bool allowInInitializer = true
    )
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
                    var hasInitializer = Match(JsTokenKind.Assign);
                    if (!hasInitializer && !allowMissingInitializer)
                        throw Error(
                            "Binding declaration requires initializer",
                            Arena.GetPosition(pattern)
                        );
                    declarators.Add(
                        Arena.Add(
                            AstKind.VariableDeclaratorPattern,
                            pattern,
                            hasInitializer ? ParseAssignment(allowInInitializer) : -1,
                            position: Arena.GetPosition(pattern)
                        )
                    );
                }
                else
                {
                    var identifier = ExpectIdentifier();
                    ValidateBindingIdentifier(identifier);
                    var initializer = -1;
                    if (Match(JsTokenKind.Assign))
                        initializer = ParseAssignment(allowInInitializer);
                    else if (kind == JsVariableDeclarationKind.Const && !allowMissingInitializer)
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
        ValidateBindingIdentifier(identifier);
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

    private int ParseFunctionExpression(bool isAsync = false) =>
        ParseFunction(isDeclaration: false, isAsync);

    private int ParseFunction(bool isDeclaration, bool isAsync = false)
    {
        var position = current.Position;
        if (isAsync)
            Next();
        Expect(JsTokenKind.Function);
        var isGenerator = Match(JsTokenKind.Star);
        var nameId = -1;
        string name;
        if (isDeclaration || current.Kind == JsTokenKind.Identifier)
        {
            var nameToken = ExpectIdentifier();
            ValidateBindingIdentifier(nameToken);
            name = GetIdentifierText(nameToken);
            if ((asyncFunctionDepth > 0 || (isAsync && !isDeclaration)) && name == "await")
                throw Error("Unexpected await binding", nameToken.Position);
            nameId = nameToken.IdentifierId;
        }
        else
            name = string.Empty;
        return ParseFunctionTail(
            isDeclaration,
            name,
            nameId,
            position,
            isMethod: false,
            isGenerator,
            isAsync
        );
    }

    private int ParseFunctionTail(
        bool isDeclaration,
        string name,
        int nameId,
        int position,
        bool isMethod,
        bool isGenerator = false,
        bool isAsync = false,
        bool isClassConstructor = false,
        bool isDerivedConstructor = false
    )
    {
        var generatorDepthBeforeFunction = generatorFunctionDepth;
        var asyncDepthBeforeFunction = asyncFunctionDepth;
        var parsingAsyncParametersBeforeFunction = parsingAsyncParameters;
        var allowSuperCallBeforeFunction = allowSuperCall;
        var allowSuperPropertyBeforeFunction = allowSuperProperty;
        var superPropertySeenBeforeFunction = superPropertySeen;
        var forbidClassFieldArgumentsBeforeFunction = forbidClassFieldArguments;
        var forbidReturnInClassStaticBlockBeforeFunction = forbidReturnInClassStaticBlock;
        receiverFunctionDepth++;
        try
        {
            generatorFunctionDepth = 0;
            asyncFunctionDepth = 0;
            parsingAsyncParameters = isAsync;
            allowSuperCall = isDerivedConstructor;
            allowSuperProperty = isMethod || isClassConstructor;
            superPropertySeen = false;
            forbidClassFieldArguments = false;
            forbidReturnInClassStaticBlock = false;
            return ParseFunctionTailCore(
                isDeclaration,
                name,
                nameId,
                position,
                isMethod,
                isGenerator,
                generatorDepthBeforeFunction,
                isAsync,
                asyncDepthBeforeFunction,
                isClassConstructor,
                isDerivedConstructor
            );
        }
        finally
        {
            generatorFunctionDepth = generatorDepthBeforeFunction;
            asyncFunctionDepth = asyncDepthBeforeFunction;
            parsingAsyncParameters = parsingAsyncParametersBeforeFunction;
            allowSuperCall = allowSuperCallBeforeFunction;
            allowSuperProperty = allowSuperPropertyBeforeFunction;
            superPropertySeen = superPropertySeenBeforeFunction;
            forbidClassFieldArguments = forbidClassFieldArgumentsBeforeFunction;
            forbidReturnInClassStaticBlock = forbidReturnInClassStaticBlockBeforeFunction;
            receiverFunctionDepth--;
        }
    }

    private int ParseFunctionTailCore(
        bool isDeclaration,
        string name,
        int nameId,
        int position,
        bool isMethod,
        bool isGenerator,
        int generatorDepthBeforeFunction,
        bool isAsync,
        int asyncDepthBeforeFunction,
        bool isClassConstructor,
        bool isDerivedConstructor
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
            generatorFunctionDepth = isGenerator ? generatorDepthBeforeFunction + 1 : 0;
            asyncFunctionDepth = isAsync ? asyncDepthBeforeFunction + 1 : 0;
            parsingAsyncParameters = false;
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
                    isMethod,
                    IsGenerator: isGenerator,
                    IsAsync: isAsync,
                    IsClassConstructor: isClassConstructor,
                    IsDerivedConstructor: isDerivedConstructor,
                    HasSuperPropertyReference: superPropertySeen
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

    private int ParseClass(bool isDeclaration)
    {
        var position = current.Position;
        if (!IsCurrentIdentifierName("class"))
            throw Error("Expected class", position);
        Next();

        var name = string.Empty;
        var nameId = -1;
        if (current.Kind == JsTokenKind.Identifier)
        {
            var nameToken = current;
            name = GetIdentifierText(nameToken);
            ValidateBindingIdentifier(nameToken);
            nameId = nameToken.IdentifierId;
            Next();
        }
        else if (isDeclaration)
            throw Error("Expected class name", current.Position);

        var extendsNode = -1;
        if (IsCurrentIdentifierName("extends"))
        {
            Next();
            extendsNode =
                current.Kind == JsTokenKind.New
                    ? ParseNewExpression()
                    : ParseMemberAndCallSuffix(ParsePrimary(), allowCalls: true);
        }

        Expect(JsTokenKind.LeftBrace);
        var strictBeforeClass = strictMode;
        var privateNameScopeBeforeClass = privateNameScope;
        var classPrivateNameScope = new PrivateNameScope(privateNameScopeBeforeClass);
        privateNameScope = classPrivateNameScope;
        strictMode = true;
        Span<FlatClassElement> initial = stackalloc FlatClassElement[8];
        var elements = new ClassElementList(initial);
        var constructorNode = -1;
        var nextInstanceFieldKeyIndex = 0;
        var instanceFieldsUseSuper = false;
        try
        {
            while (current.Kind != JsTokenKind.RightBrace)
            {
                if (current.Kind == JsTokenKind.Eof)
                    throw Error("Unterminated class", position);
                if (Match(JsTokenKind.Semicolon))
                    continue;

                var elementPosition = current.Position;
                var isStatic = false;
                if (IsCurrentIdentifierName("static"))
                {
                    var next = PeekToken();
                    if (
                        next.Kind
                        is not (
                            JsTokenKind.LeftParen
                            or JsTokenKind.Assign
                            or JsTokenKind.Semicolon
                            or JsTokenKind.RightBrace
                        )
                    )
                    {
                        isStatic = true;
                        Next();
                        if (current.Kind == JsTokenKind.LeftBrace)
                        {
                            elements.Add(
                                new FlatClassElement(
                                    Arena.AddString(string.Empty),
                                    ParseClassStaticBlock(elementPosition),
                                    elementPosition,
                                    JsClassElementKind.StaticBlock,
                                    FlatClassElementFlags.Static
                                )
                            );
                            _ = Match(JsTokenKind.Semicolon);
                            continue;
                        }
                    }
                }

                var isAsync = false;
                if (IsCurrentIdentifierName("async"))
                {
                    var next = PeekToken();
                    if (
                        !next.HasLineTerminatorBefore
                        && next.Kind
                            is not (
                                JsTokenKind.LeftParen
                                or JsTokenKind.Assign
                                or JsTokenKind.Semicolon
                                or JsTokenKind.RightBrace
                            )
                    )
                    {
                        isAsync = true;
                        Next();
                    }
                }

                var isGenerator = Match(JsTokenKind.Star);
                var computed = Match(JsTokenKind.LeftBracket);
                var isPrivate = false;
                int key;
                if (computed)
                {
                    key = ParseAssignment(allowIn: true);
                    Expect(JsTokenKind.RightBracket);
                }
                else
                {
                    if (current.Kind == JsTokenKind.PrivateIdentifier)
                    {
                        isPrivate = true;
                        key = Arena.AddString(GetPrivateIdentifierText(current));
                    }
                    else
                        key = Arena.AddString(GetObjectPropertyName(current));
                    Next();
                }

                var staticName = computed ? null : ast.GetString(key);
                if (
                    !isGenerator
                    && !isAsync
                    && !isPrivate
                    && staticName is "get" or "set"
                    && current.Kind != JsTokenKind.LeftParen
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
                        if (current.Kind == JsTokenKind.PrivateIdentifier)
                        {
                            isPrivate = true;
                            key = Arena.AddString(GetPrivateIdentifierText(current));
                        }
                        else
                            key = Arena.AddString(GetObjectPropertyName(current));
                        Next();
                    }

                    var accessor = ParseFunctionTail(
                        isDeclaration: false,
                        string.Empty,
                        -1,
                        elementPosition,
                        isMethod: true
                    );
                    var accessorFunction = ast.GetFunction(Arena[accessor].Arg0);
                    if (isGetter && accessorFunction.ParameterCount != 0)
                        throw Error("Getter must not have parameters", elementPosition);
                    if (
                        !isGetter
                        && (
                            accessorFunction.ParameterCount != 1
                            || accessorFunction.RestParameterIndex >= 0
                        )
                    )
                        throw Error("Expected setter parameter", elementPosition);
                    if (isPrivate)
                        DeclarePrivateElement(
                            classPrivateNameScope,
                            ast.GetString(key),
                            isGetter ? JsClassElementKind.Getter : JsClassElementKind.Setter,
                            isStatic,
                            elementPosition
                        );
                    elements.Add(
                        new FlatClassElement(
                            key,
                            accessor,
                            elementPosition,
                            isGetter ? JsClassElementKind.Getter : JsClassElementKind.Setter,
                            (isStatic ? FlatClassElementFlags.Static : 0)
                                | (computed ? FlatClassElementFlags.Computed : 0)
                                | (isPrivate ? FlatClassElementFlags.Private : 0)
                        )
                    );
                    continue;
                }

                if (current.Kind != JsTokenKind.LeftParen)
                {
                    if (isGenerator || isAsync)
                        throw Error("Invalid class field", elementPosition);
                    if (isStatic && !computed && string.Equals(staticName, "prototype"))
                        throw Error(
                            "Classes may not have a static field named 'prototype'",
                            elementPosition
                        );
                    var initializerUsesSuper = false;
                    var initializer = -1;
                    if (Match(JsTokenKind.Assign))
                    {
                        var expression = ParseClassFieldInitializer(
                            elementPosition,
                            out initializerUsesSuper
                        );
                        initializer = isStatic
                            ? AddStaticClassFieldInitializer(
                                expression,
                                elementPosition,
                                initializerUsesSuper,
                                computed ? -1 : key,
                                computed
                            )
                            : expression;
                    }
                    else if (isStatic)
                        initializer = AddStaticClassFieldInitializer(
                            -1,
                            elementPosition,
                            false,
                            -1,
                            computed
                        );
                    if (!isStatic)
                        instanceFieldsUseSuper |= initializerUsesSuper;
                    if (isPrivate)
                        DeclarePrivateElement(
                            classPrivateNameScope,
                            ast.GetString(key),
                            JsClassElementKind.Field,
                            isStatic,
                            elementPosition
                        );
                    elements.Add(
                        new FlatClassElement(
                            key,
                            initializer,
                            elementPosition,
                            JsClassElementKind.Field,
                            (isStatic ? FlatClassElementFlags.Static : 0)
                                | (computed ? FlatClassElementFlags.Computed : 0)
                                | (isPrivate ? FlatClassElementFlags.Private : 0),
                            !isStatic && computed ? nextInstanceFieldKeyIndex++ : -1
                        )
                    );
                    ConsumeSemicolon();
                    continue;
                }

                var isConstructor =
                    !isStatic
                    && !computed
                    && !isPrivate
                    && string.Equals(staticName, "constructor");
                if (isPrivate)
                    DeclarePrivateElement(
                        classPrivateNameScope,
                        ast.GetString(key),
                        JsClassElementKind.Method,
                        isStatic,
                        elementPosition
                    );
                if (isConstructor && (isGenerator || isAsync))
                    throw Error(
                        "Class constructor may not be async or a generator",
                        elementPosition
                    );
                if (isConstructor && constructorNode >= 0)
                    throw Error("Duplicate constructor in class", elementPosition);
                var method = ParseFunctionTail(
                    isDeclaration: false,
                    string.Empty,
                    -1,
                    elementPosition,
                    isMethod: !isConstructor,
                    isGenerator,
                    isAsync,
                    isClassConstructor: isConstructor,
                    isDerivedConstructor: isConstructor && extendsNode >= 0
                );
                if (isConstructor)
                    constructorNode = method;
                elements.Add(
                    new FlatClassElement(
                        key,
                        method,
                        elementPosition,
                        isConstructor ? JsClassElementKind.Constructor : JsClassElementKind.Method,
                        (isStatic ? FlatClassElementFlags.Static : 0)
                            | (computed ? FlatClassElementFlags.Computed : 0)
                            | (isPrivate ? FlatClassElementFlags.Private : 0)
                    )
                );
            }
            Expect(JsTokenKind.RightBrace);
            if (!classPrivateNameScope.Resolve(out var unresolvedPrivateName))
                throw Error(
                    $"Private name '{unresolvedPrivateName.Name}' is not declared in an enclosing class",
                    unresolvedPrivateName.Position
                );
            if (constructorNode < 0)
                constructorNode = AddImplicitClassConstructor(
                    position,
                    isDerived: extendsNode >= 0
                );
            if (instanceFieldsUseSuper)
            {
                var constructorFunctionIndex = Arena[constructorNode].Arg0;
                ast.SetFunction(
                    constructorFunctionIndex,
                    ast.GetFunction(constructorFunctionIndex) with
                    {
                        HasSuperPropertyReference = true,
                    }
                );
            }
            var elementRange = ast.AddClassElements(elements.AsSpan());
            var classIndex = ast.AddClass(
                new FlatClassInfo(
                    Arena.AddString(name),
                    nameId,
                    elementRange.Offset,
                    elementRange.Count,
                    constructorNode,
                    extendsNode,
                    position
                )
            );
            return Arena.Add(
                isDeclaration ? AstKind.ClassDeclaration : AstKind.ClassExpression,
                classIndex,
                position: position
            );
        }
        finally
        {
            elements.Dispose();
            strictMode = strictBeforeClass;
            privateNameScope = privateNameScopeBeforeClass;
        }
    }

    private int ParseClassStaticBlock(int position)
    {
        var allowSuperCallBeforeBlock = allowSuperCall;
        var allowSuperPropertyBeforeBlock = allowSuperProperty;
        var superPropertySeenBeforeBlock = superPropertySeen;
        var forbidClassFieldArgumentsBeforeBlock = forbidClassFieldArguments;
        var forbidReturnInClassStaticBlockBeforeBlock = forbidReturnInClassStaticBlock;
        var generatorDepthBeforeBlock = generatorFunctionDepth;
        var asyncDepthBeforeBlock = asyncFunctionDepth;
        var loopDepthBeforeBlock = loopDepth;
        var switchDepthBeforeBlock = switchDepth;
        allowSuperCall = false;
        allowSuperProperty = true;
        superPropertySeen = false;
        forbidClassFieldArguments = true;
        forbidReturnInClassStaticBlock = true;
        generatorFunctionDepth = 0;
        asyncFunctionDepth = 0;
        loopDepth = 0;
        switchDepth = 0;
        functionDepth++;
        receiverFunctionDepth++;
        try
        {
            var body = ParseBlock(out _, AstKind.Program);
            var parameters = ast.AddParameters(ReadOnlySpan<FlatParameter>.Empty);
            var functionIndex = ast.AddFunction(
                new FlatFunctionInfo(
                    Arena.AddString(string.Empty),
                    -1,
                    parameters.Offset,
                    parameters.Count,
                    0,
                    -1,
                    true,
                    true,
                    false,
                    position,
                    true,
                    HasSuperPropertyReference: superPropertySeen
                )
            );
            return Arena.Add(AstKind.FunctionExpression, functionIndex, body, position: position);
        }
        finally
        {
            receiverFunctionDepth--;
            functionDepth--;
            switchDepth = switchDepthBeforeBlock;
            loopDepth = loopDepthBeforeBlock;
            asyncFunctionDepth = asyncDepthBeforeBlock;
            generatorFunctionDepth = generatorDepthBeforeBlock;
            forbidReturnInClassStaticBlock = forbidReturnInClassStaticBlockBeforeBlock;
            forbidClassFieldArguments = forbidClassFieldArgumentsBeforeBlock;
            superPropertySeen = superPropertySeenBeforeBlock;
            allowSuperProperty = allowSuperPropertyBeforeBlock;
            allowSuperCall = allowSuperCallBeforeBlock;
        }
    }

    private int ParseClassFieldInitializer(int position, out bool hasSuperPropertyReference)
    {
        var allowSuperPropertyBeforeInitializer = allowSuperProperty;
        var superPropertySeenBeforeInitializer = superPropertySeen;
        var forbidClassFieldArgumentsBeforeInitializer = forbidClassFieldArguments;
        allowSuperProperty = true;
        superPropertySeen = false;
        forbidClassFieldArguments = true;
        receiverFunctionDepth++;
        try
        {
            var expression = ParseAssignment(allowIn: true);
            hasSuperPropertyReference = superPropertySeen;
            return expression;
        }
        finally
        {
            receiverFunctionDepth--;
            allowSuperProperty = allowSuperPropertyBeforeInitializer;
            superPropertySeen = superPropertySeenBeforeInitializer;
            forbidClassFieldArguments = forbidClassFieldArgumentsBeforeInitializer;
        }
    }

    private int AddStaticClassFieldInitializer(
        int expression,
        int position,
        bool hasSuperPropertyReference,
        int inferredNameStringIndex,
        bool inferNameFromFirstParameter
    )
    {
        var returnStatement = Arena.Add(AstKind.ReturnStatement, expression, position: position);
        Span<int> statements = [returnStatement];
        var bodyRange = Arena.AddChildren(statements);
        var body = Arena.Add(
            AstKind.Program,
            bodyRange.Offset,
            bodyRange.Count,
            position: position
        );
        var parameters = inferNameFromFirstParameter
            ? ast.AddParameters([
                new FlatParameter(
                    Arena.AddString("\0computed field key"),
                    -1,
                    -1,
                    -1,
                    position,
                    JsFormalParameterBindingKind.Plain
                ),
            ])
            : ast.AddParameters(ReadOnlySpan<FlatParameter>.Empty);
        var functionIndex = ast.AddFunction(
            new FlatFunctionInfo(
                Arena.AddString(string.Empty),
                -1,
                parameters.Offset,
                parameters.Count,
                0,
                -1,
                true,
                true,
                false,
                position,
                true,
                HasSuperPropertyReference: hasSuperPropertyReference,
                ReturnInferredNameStringIndex: inferredNameStringIndex,
                ReturnInferredNameFromFirstParameter: inferNameFromFirstParameter
            )
        );
        return Arena.Add(AstKind.FunctionExpression, functionIndex, body, position: position);
    }

    private int AddImplicitClassConstructor(int position, bool isDerived = false)
    {
        var empty = Arena.AddChildren(ReadOnlySpan<int>.Empty);
        var body = Arena.Add(AstKind.Program, empty.Offset, empty.Count, position: position);
        var parameters = ast.AddParameters(ReadOnlySpan<FlatParameter>.Empty);
        var functionIndex = ast.AddFunction(
            new FlatFunctionInfo(
                Arena.AddString(string.Empty),
                -1,
                parameters.Offset,
                parameters.Count,
                0,
                -1,
                true,
                true,
                false,
                position,
                false,
                IsClassConstructor: true,
                IsDerivedConstructor: isDerived,
                EmitImplicitSuperForwardAll: isDerived
            )
        );
        return Arena.Add(AstKind.FunctionExpression, functionIndex, body, position: position);
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
            case AstKind.AssignmentExpression
                when (JsAssignmentOperator)node.Arg2 == JsAssignmentOperator.Assign:
            case AstKind.SpreadElement:
                TrackParameterPatternNames(
                    node.Arg0,
                    ref names,
                    ref hasDuplicate,
                    ref hasRestrictedName
                );
                return;
            case AstKind.ArrayBindingPattern:
            case AstKind.ArrayExpression:
                var elements = Arena.ChildRange(node.Arg0, node.Arg1);
                for (var i = 0; i < elements.Length; i++)
                    if (elements[i] >= 0)
                    {
                        if (
                            Arena[elements[i]].Kind == AstKind.SpreadElement
                            && i != elements.Length - 1
                        )
                            throw Error(
                                "Rest binding must be the final element",
                                Arena.GetPosition(elements[i])
                            );
                        TrackParameterPatternNames(
                            elements[i],
                            ref names,
                            ref hasDuplicate,
                            ref hasRestrictedName
                        );
                    }
                return;
            case AstKind.ObjectBindingPattern:
            case AstKind.ObjectExpression:
                var properties = ast.GetObjectProperties(node.Arg0, node.Arg1);
                for (var i = 0; i < properties.Length; i++)
                {
                    if (properties[i].IsAccessor)
                        throw Error("Invalid method in binding pattern", properties[i].Position);
                    if (
                        properties[i].IsRest
                        && (
                            i != properties.Length - 1
                            || Arena[properties[i].ValueNode].Kind != AstKind.Identifier
                        )
                    )
                        throw Error("Invalid object rest binding", properties[i].Position);
                    TrackParameterPatternNames(
                        properties[i].ValueNode,
                        ref names,
                        ref hasDuplicate,
                        ref hasRestrictedName
                    );
                }
                return;
            default:
                throw Error("Invalid parameter binding pattern", Arena.GetPosition(nodeIndex));
        }
    }

    private void TrackParameterName(
        string name,
        ref ParameterNameTracker names,
        ref bool hasDuplicate,
        ref bool hasRestrictedName
    )
    {
        if (parsingAsyncParameters && name == "await")
            ReportAsyncParameterError(current.Position);
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
        var isAwait = false;
        if (
            (asyncFunctionDepth > 0 || (isModule && functionDepth == 0))
            && current.Kind is JsTokenKind.Identifier or JsTokenKind.ReservedWord
            && source.AsSpan(current.Position, current.SourceLength).SequenceEqual("await".AsSpan())
        )
        {
            isAwait = true;
            Next();
            if (asyncFunctionDepth == 0)
                ast.HasTopLevelAwait = true;
        }
        Expect(JsTokenKind.LeftParen);
        var init = -1;
        if (current.Kind != JsTokenKind.Semicolon)
        {
            if (current.Kind is JsTokenKind.Var or JsTokenKind.Let or JsTokenKind.Const)
            {
                init = ParseVariableDeclaration(
                    consumeSemicolon: false,
                    allowMissingInitializer: true,
                    allowInInitializer: false
                );
            }
            else if (
                current.Kind == JsTokenKind.Identifier
                && TryGetUsingDeclarationStatementKind(out var headUsingKind)
            )
            {
                init = ParseUsingDeclaration(
                    headUsingKind,
                    consumeSemicolon: false,
                    allowMissingInitializer: true
                );
            }
            else
            {
                init = ParseExpression(allowIn: false);
            }
        }
        if (current.Kind is JsTokenKind.In or JsTokenKind.Of)
        {
            var isOf = current.Kind == JsTokenKind.Of;
            if (isAwait && !isOf)
                throw Error("for await loops must use 'of'", current.Position);
            ValidateForInOfLeft(init, position, isOf);
            Next();
            var right = ParseExpression();
            Expect(JsTokenKind.RightParen);
            Span<int> iterationParts = [init, right, ParseLoopBody()];
            var iterationChildren = Arena.AddChildren(iterationParts);
            return Arena.Add(
                AstKind.ForInOfStatement,
                iterationChildren.Offset,
                iterationChildren.Count,
                isAwait ? 2
                    : isOf ? 1
                    : 0,
                position
            );
        }
        if (isAwait)
            throw Error("for await loops must use 'of'", current.Position);
        ValidateOrdinaryForInitializer(init);
        Expect(JsTokenKind.Semicolon);
        var test = current.Kind == JsTokenKind.Semicolon ? -1 : ParseExpression();
        Expect(JsTokenKind.Semicolon);
        var update = current.Kind == JsTokenKind.RightParen ? -1 : ParseExpression();
        Expect(JsTokenKind.RightParen);
        Span<int> parts = [init, test, update, ParseLoopBody()];
        var children = Arena.AddChildren(parts);
        return Arena.Add(AstKind.ForStatement, children.Offset, children.Count, position: position);
    }

    private void ValidateForInOfLeft(int left, int position, bool isOf)
    {
        if (left < 0)
            throw Error("Missing for-in/of binding", position);
        ref readonly var node = ref Arena[left];
        if (node.Kind == AstKind.VariableDeclaration)
        {
            var declarators = Arena.ChildRange(node.Arg0, node.Arg1);
            var declarationKind = (JsVariableDeclarationKind)node.Arg2;
            if (declarationKind.IsUsingLike())
            {
                if (!isOf)
                    throw Error("using declarations are not allowed in for-in", position);
                if (declarators.Length != 1)
                    throw Error("for-in/of requires one binding", position);
                return;
            }
            if (declarators.Length != 1)
                throw Error("for-in/of requires one binding", position);
            ref readonly var declarator = ref Arena[declarators[0]];
            var initializer =
                declarator.Kind == AstKind.VariableDeclaratorPattern
                    ? declarator.Arg1
                    : declarator.Arg2;
            if (initializer >= 0)
                throw Error("for-in/of binding cannot have an initializer", position);
            return;
        }

        switch (node.Kind)
        {
            case AstKind.Identifier:
            case AstKind.MemberExpression:
                return;
            case AstKind.ArrayExpression:
            case AstKind.ObjectExpression:
                if (isOf && IsDestructuringAssignmentTarget(left))
                    return;
                throw Error("Invalid for-in/of assignment target", position);
            default:
                throw Error("Invalid for-in/of assignment target", position);
        }
    }

    private void ValidateOrdinaryForInitializer(int init)
    {
        if (init < 0 || Arena[init].Kind != AstKind.VariableDeclaration)
            return;
        ref readonly var declaration = ref Arena[init];
        var kind = (JsVariableDeclarationKind)declaration.Arg2;
        var declarators = Arena.ChildRange(declaration.Arg0, declaration.Arg1);
        for (var i = 0; i < declarators.Length; i++)
        {
            ref readonly var declarator = ref Arena[declarators[i]];
            var initializer =
                declarator.Kind == AstKind.VariableDeclaratorPattern
                    ? declarator.Arg1
                    : declarator.Arg2;
            if (initializer < 0 && kind == JsVariableDeclarationKind.Const)
                throw Error("Const declaration requires initializer", Arena.GetPosition(init));
            if (initializer < 0 && declarator.Kind == AstKind.VariableDeclaratorPattern)
                throw Error("Binding declaration requires initializer", Arena.GetPosition(init));
        }
    }

    private int ParseLoopControl(AstKind kind)
    {
        var position = current.Position;
        Next();
        var labelStringIndex = -1;
        if (!current.HasLineTerminatorBefore && current.Kind == JsTokenKind.Identifier)
        {
            var label = GetIdentifierText(current);
            var target = FindActiveLabel(label);
            if (target is null)
                throw Error($"Unknown label '{label}'", current.Position);
            if (kind == AstKind.ContinueStatement && !target.Value.IsIteration)
                throw Error($"Continue target '{label}' is not an iteration statement", position);
            labelStringIndex = Arena.AddString(label);
            Next();
        }
        else if (
            kind == AstKind.ContinueStatement ? loopDepth == 0 : loopDepth == 0 && switchDepth == 0
        )
            throw Error($"Illegal {kind}", position);
        ConsumeSemicolon();
        return Arena.Add(kind, labelStringIndex, position: position);
    }

    private int ParseLabeledStatement()
    {
        var labelToken = current;
        var label = GetIdentifierText(labelToken);
        if (FindActiveLabel(label) is not null)
            throw Error($"Duplicate label '{label}'", labelToken.Position);
        Next();
        Expect(JsTokenKind.Colon);
        var active = new ActiveLabel(label, IsIterationLabelTarget(), functionDepth);
        (activeLabels ??= []).Add(active);
        try
        {
            return Arena.Add(
                AstKind.LabeledStatement,
                Arena.AddString(label),
                ParseStatement(),
                position: labelToken.Position
            );
        }
        finally
        {
            activeLabels.RemoveAt(activeLabels.Count - 1);
        }
    }

    private ActiveLabel? FindActiveLabel(string label)
    {
        if (activeLabels is null)
            return null;
        for (var i = activeLabels.Count - 1; i >= 0; i--)
            if (
                activeLabels[i].FunctionDepth == functionDepth
                && string.Equals(activeLabels[i].Name, label, StringComparison.Ordinal)
            )
                return activeLabels[i];
        return null;
    }

    private bool IsIterationLabelTarget()
    {
        if (current.Kind is JsTokenKind.For or JsTokenKind.While or JsTokenKind.Do)
            return true;
        if (current.Kind != JsTokenKind.Identifier)
            return false;

        var index = lexer.GetIndex();
        try
        {
            var token = current;
            while (token.Kind == JsTokenKind.Identifier)
            {
                if (lexer.NextToken().Kind != JsTokenKind.Colon)
                    return false;
                token = lexer.NextToken();
            }
            return token.Kind is JsTokenKind.For or JsTokenKind.While or JsTokenKind.Do;
        }
        finally
        {
            lexer.SetIndex(index);
        }
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
        if (functionDepth == 0 || forbidReturnInClassStaticBlock)
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
        int left;
        if (IsAsyncArrowPrefix())
        {
            left = ParseAsyncArrowFunction(position);
            if (Arena[left].Kind == AstKind.ArrowFunctionExpression)
                return left;
            left = ParsePostfixUpdateRemainder(left, position);
            left = ParseBinaryRemainder(left, allowIn, 1);
            left = ParseConditionalRemainder(left, allowIn, position);
        }
        else
        {
            if (
                generatorFunctionDepth > 0
                && current.Kind is JsTokenKind.Identifier or JsTokenKind.ReservedWord
                && source
                    .AsSpan(current.Position, current.SourceLength)
                    .SequenceEqual("yield".AsSpan())
            )
                return ParseYieldExpression(allowIn);
            left = ParseConditional(allowIn);
        }
        if (current.Kind == JsTokenKind.Arrow)
            return ParseArrowFunction(left, position);
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

    private int ParseYieldExpression(bool allowIn)
    {
        var position = current.Position;
        Next();
        var isDelegate = Match(JsTokenKind.Star);

        var argument =
            !isDelegate
            && (
                current.HasLineTerminatorBefore
                || current.Kind
                    is JsTokenKind.Semicolon
                        or JsTokenKind.Comma
                        or JsTokenKind.Colon
                        or JsTokenKind.RightParen
                        or JsTokenKind.RightBrace
                        or JsTokenKind.RightBracket
                        or JsTokenKind.Eof
                        or JsTokenKind.In
            )
                ? -1
                : ParseAssignment(allowIn);
        return Arena.Add(AstKind.YieldExpression, argument, isDelegate ? 1 : 0, position: position);
    }

    private int ParseConditional(bool allowIn)
    {
        var position = current.Position;
        return ParseConditionalRemainder(ParseBinary(allowIn, 1), allowIn, position);
    }

    private int ParseConditionalRemainder(int test, bool allowIn, int position)
    {
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
        return ParseBinaryRemainder(ParseUnary(), allowIn, minimumPrecedence);
    }

    private int ParseBinaryRemainder(int left, bool allowIn, int minimumPrecedence)
    {
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
            if (Arena[left].Kind == AstKind.PrivateIdentifier && op != JsBinaryOperator.In)
                throw Error("Private identifier must be the left side of 'in'", position);
            Next();
            var right = ParseBinary(allowIn, rightAssociative ? precedence : precedence + 1);
            left = Arena.Add(AstKind.BinaryExpression, left, right, (int)op, position);
        }
        if (Arena[left].Kind == AstKind.PrivateIdentifier)
            throw Error(
                "Private identifier must be the left side of 'in'",
                Arena.GetPosition(left)
            );
        return left;
    }

    private int ParseUnary()
    {
        var position = current.Position;
        if (
            current.Kind is JsTokenKind.Identifier or JsTokenKind.ReservedWord
            && source.AsSpan(current.Position, current.SourceLength).SequenceEqual("await".AsSpan())
        )
        {
            if (parsingAsyncParameters)
                ReportAsyncParameterError(position);
            if (asyncFunctionDepth > 0 || (isModule && functionDepth == 0))
            {
                if (asyncFunctionDepth == 0)
                    ast.HasTopLevelAwait = true;
                Next();
                return Arena.Add(AstKind.AwaitExpression, ParseUnary(), position: position);
            }
        }
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
            if (
                unary == JsUnaryOperator.Delete
                && Arena[argument].Kind == AstKind.MemberExpression
                && ((AstMemberFlags)Arena[argument].Arg2 & AstMemberFlags.Private) != 0
            )
                throw Error("Private fields cannot be deleted", position);
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

        return ParsePostfixUpdateRemainder(ParsePostfix(), position);
    }

    private int ParsePostfixUpdateRemainder(int expression, int position)
    {
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
        if (Match(JsTokenKind.Dot))
        {
            if (
                current.Kind != JsTokenKind.Identifier
                || !source
                    .AsSpan(current.Position, current.SourceLength)
                    .SequenceEqual("target".AsSpan())
            )
                throw Error($"Expected target but found {current.Kind}", current.Position);
            if (receiverFunctionDepth == 0)
                throw Error("new.target is only valid inside a function", position);
            Next();
            return ParseMemberAndCallSuffix(
                Arena.Add(AstKind.NewTargetExpression, position: position),
                allowCallSuffix
            );
        }

        var callee =
            current.Kind == JsTokenKind.New
                ? ParseNewExpression(allowCallSuffix: false)
                : ParseMemberAndCallSuffix(ParsePrimary(), allowCalls: false);
        if (
            Arena[callee].Kind == AstKind.OptionalChainExpression
            && ((AstOptionalChainFlags)Arena[callee].Arg1 & AstOptionalChainFlags.Parenthesized)
                == 0
        )
            throw Error("Optional chain cannot be used directly as a constructor", position);
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
        var optionalChain = false;
        while (true)
        {
            var position = Arena.GetPosition(expression);
            if (IsOptionalChainPunctuator())
            {
                if (Arena[expression].Kind == AstKind.SuperExpression)
                    throw Error("Optional chaining is not valid on super", current.Position);
                Next();
                Expect(JsTokenKind.Dot);
                optionalChain = true;
                if (Match(JsTokenKind.LeftBracket))
                {
                    var property = ParseExpression();
                    Expect(JsTokenKind.RightBracket);
                    expression = Arena.Add(
                        AstKind.MemberExpression,
                        expression,
                        property,
                        (int)(AstMemberFlags.Computed | AstMemberFlags.OptionalChainLink),
                        position
                    );
                    continue;
                }

                if (allowCalls && Match(JsTokenKind.LeftParen))
                {
                    expression = ParseCallArguments(expression, position, optional: true);
                    continue;
                }

                if (current.Kind == JsTokenKind.PrivateIdentifier)
                {
                    var privateName = GetPrivateIdentifierText(current);
                    ReferencePrivateName(privateName, current.Position);
                    Next();
                    expression = Arena.Add(
                        AstKind.MemberExpression,
                        expression,
                        Arena.AddString(privateName),
                        (int)(AstMemberFlags.Private | AstMemberFlags.OptionalChainLink),
                        position
                    );
                    continue;
                }

                if (!JsTokenFacts.IsIdentifierName(current.Kind))
                    throw Error($"Expected Identifier but found {current.Kind}", current.Position);
                var optionalProperty = current;
                Next();
                expression = Arena.Add(
                    AstKind.MemberExpression,
                    expression,
                    Arena.AddString(GetIdentifierText(optionalProperty)),
                    (int)AstMemberFlags.OptionalChainLink,
                    position
                );
                continue;
            }

            if (Match(JsTokenKind.Dot))
            {
                if (Arena[expression].Kind == AstKind.SuperExpression)
                {
                    if (!allowSuperProperty)
                        throw Error("super property is only valid in a method", position);
                    superPropertySeen = true;
                }
                if (current.Kind == JsTokenKind.PrivateIdentifier)
                {
                    if (Arena[expression].Kind == AstKind.SuperExpression)
                        throw Error("Private fields are not valid on super", current.Position);
                    var privateName = GetPrivateIdentifierText(current);
                    ReferencePrivateName(privateName, current.Position);
                    Next();
                    expression = Arena.Add(
                        AstKind.MemberExpression,
                        expression,
                        Arena.AddString(privateName),
                        (int)AstMemberFlags.Private,
                        position
                    );
                    continue;
                }
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
                if (Arena[expression].Kind == AstKind.SuperExpression)
                {
                    if (!allowSuperProperty)
                        throw Error("super property is only valid in a method", position);
                    superPropertySeen = true;
                }
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
                if (Arena[expression].Kind == AstKind.SuperExpression && !allowSuperCall)
                    throw Error("super() is only valid in a derived constructor", position);
                expression = ParseCallArguments(expression, position, optional: false);
                continue;
            }

            if (current.Kind == JsTokenKind.Template)
            {
                if (Arena[expression].Kind == AstKind.SuperExpression)
                    throw Error("Invalid use of super", position);
                if (optionalChain)
                    throw Error(
                        "Tagged template cannot follow an optional chain",
                        current.Position
                    );
                var template = current;
                expression = ParseTemplateLiteral(template, expression);
                continue;
            }

            if (Arena[expression].Kind == AstKind.SuperExpression)
                throw Error("Invalid use of super", position);
            return optionalChain
                ? Arena.Add(
                    AstKind.OptionalChainExpression,
                    expression,
                    (int)AstOptionalChainFlags.None,
                    position: Arena.GetPosition(expression)
                )
                : expression;
        }
    }

    private int ParseCallArguments(int callee, int position, bool optional)
    {
        var children = ParseArgumentListAfterOpenParen();
        return Arena.Add(
            optional ? AstKind.OptionalCallExpression : AstKind.CallExpression,
            callee,
            children.Offset,
            children.Count,
            position
        );
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
        if (IsCurrentIdentifierName("class"))
            return ParseClass(isDeclaration: false);
        if (IsCurrentIdentifierName("super"))
        {
            Next();
            return Arena.Add(AstKind.SuperExpression, position: token.Position);
        }
        if (IsCurrentIdentifierName("import"))
            return ParseImportExpression(token.Position);
        if (IsAsyncFunctionPrefix())
            return ParseFunctionExpression(isAsync: true);
        switch (token.Kind)
        {
            case JsTokenKind.Identifier:
                if (
                    forbidClassFieldArguments
                    && string.Equals(
                        GetIdentifierText(token),
                        "arguments",
                        StringComparison.Ordinal
                    )
                )
                    throw Error(
                        "'arguments' is not allowed in a class field initializer",
                        token.Position
                    );
                Next();
                return Arena.Add(
                    AstKind.Identifier,
                    Arena.AddString(GetIdentifierText(token)),
                    token.IdentifierId,
                    position: token.Position
                );
            case JsTokenKind.Let when !strictMode:
            case JsTokenKind.Of:
                Next();
                return Arena.Add(
                    AstKind.Identifier,
                    Arena.AddString(GetIdentifierText(token)),
                    token.IdentifierId,
                    position: token.Position
                );
            case JsTokenKind.PrivateIdentifier:
                var privateName = GetPrivateIdentifierText(token);
                ReferencePrivateName(privateName, token.Position);
                Next();
                return Arena.Add(
                    AstKind.PrivateIdentifier,
                    Arena.AddString(privateName),
                    position: token.Position
                );
            case JsTokenKind.Number:
                Next();
                return Arena.Add(
                    AstKind.NumericLiteral,
                    Arena.AddNumber(token.NumberLiteral),
                    position: token.Position
                );
            case JsTokenKind.BigInt:
                Next();
                return Arena.Add(
                    AstKind.BigIntLiteral,
                    Arena.AddString(lexer.GetBigIntLiteral(token).Value.ToString()),
                    position: token.Position
                );
            case JsTokenKind.String:
                Next();
                return Arena.Add(
                    AstKind.StringLiteral,
                    Arena.AddString(lexer.GetStringLiteral(token)),
                    position: token.Position
                );
            case JsTokenKind.Template:
                return ParseTemplateLiteral(token);
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
                return ParseParenthesizedExpressionOrArrow(token.Position);
            case JsTokenKind.LeftBracket:
                return ParseArrayLiteral();
            case JsTokenKind.LeftBrace:
                return ParseObjectLiteral();
            case JsTokenKind.Function:
                return ParseFunctionExpression();
            case JsTokenKind.Slash:
            case JsTokenKind.SlashAssign:
                return ParseRegExpLiteral();
            default:
                throw Error(
                    $"Expression token '{token.Kind}' is not supported by FlatJavaScriptParser",
                    token.Position
                );
        }
    }

    private int ParseImportExpression(int position)
    {
        Next();
        if (Match(JsTokenKind.Dot))
        {
            if (!IsCurrentIdentifierName("meta"))
                throw Error("Expected 'meta' after 'import.'", current.Position);
            Next();
            if (!isModule)
                throw Error("Cannot use import.meta outside a module", position);
            return Arena.Add(AstKind.ImportMetaExpression, position: position);
        }

        Expect(JsTokenKind.LeftParen);
        var specifier = ParseAssignment(allowIn: true);
        var options = -1;
        if (Match(JsTokenKind.Comma) && current.Kind != JsTokenKind.RightParen)
        {
            options = ParseAssignment(allowIn: true);
            _ = Match(JsTokenKind.Comma);
        }
        Expect(JsTokenKind.RightParen);
        return Arena.Add(AstKind.ImportCallExpression, specifier, options, position: position);
    }

    private int ParseParenthesizedExpressionOrArrow(
        int position,
        bool isAsync = false,
        int asyncCallee = -1
    )
    {
        Expect(JsTokenKind.LeftParen);
        Span<int> initial = stackalloc int[8];
        var expressions = new NodeList(initial);
        var hasSpread = false;
        var hasParenthesizedItem = false;
        var trailingComma = false;
        try
        {
            while (current.Kind != JsTokenKind.RightParen)
            {
                hasParenthesizedItem |= current.Kind == JsTokenKind.LeftParen;
                if (current.Kind == JsTokenKind.Ellipsis)
                {
                    var spreadPosition = current.Position;
                    Next();
                    expressions.Add(
                        Arena.Add(
                            AstKind.SpreadElement,
                            ParseAssignment(allowIn: true),
                            position: spreadPosition
                        )
                    );
                    hasSpread = true;
                }
                else
                    expressions.Add(ParseAssignment(allowIn: true));

                if (!Match(JsTokenKind.Comma))
                    break;
                trailingComma = current.Kind == JsTokenKind.RightParen;
                if (trailingComma)
                    break;
            }

            Expect(JsTokenKind.RightParen);
            if (current.Kind == JsTokenKind.Arrow)
            {
                if (hasParenthesizedItem)
                    throw Error("Invalid parenthesized arrow parameter", position);
                if (
                    trailingComma
                    && expressions.Count != 0
                    && Arena[expressions.AsSpan()[^1]].Kind == AstKind.SpreadElement
                )
                    throw Error("Rest parameter must be last", position);
                if (deferredAsyncParameterErrorPosition >= 0)
                    throw Error(
                        "Unexpected await in async function parameters",
                        deferredAsyncParameterErrorPosition
                    );
                deferringAsyncParameterErrors = false;
                return ParseArrowFunction(
                    CreateSequence(expressions.AsSpan(), position),
                    position,
                    isAsync
                );
            }

            if (isAsync)
            {
                parsingAsyncParameters = false;
                deferringAsyncParameterErrors = false;
                var arguments = Arena.AddChildren(expressions.AsSpan());
                var call = Arena.Add(
                    AstKind.CallExpression,
                    asyncCallee,
                    arguments.Offset,
                    arguments.Count,
                    position
                );
                return ParseMemberAndCallSuffix(call, allowCalls: true);
            }

            if (expressions.Count == 0)
                throw Error("Expected expression", position);
            if (hasSpread || trailingComma)
                throw Error("Invalid parenthesized expression", position);
            var expression = CreateSequence(expressions.AsSpan(), position);
            if (Arena[expression].Kind == AstKind.OptionalChainExpression)
                Arena[expression].Arg1 |= (int)AstOptionalChainFlags.Parenthesized;
            return expression;
        }
        finally
        {
            expressions.Dispose();
        }
    }

    private int CreateSequence(ReadOnlySpan<int> expressions, int position)
    {
        if (expressions.Length == 0)
            return -1;
        if (expressions.Length == 1)
            return expressions[0];
        var children = Arena.AddChildren(expressions);
        return Arena.Add(
            AstKind.SequenceExpression,
            children.Offset,
            children.Count,
            position: position
        );
    }

    private int ParseAsyncArrowFunction(int position)
    {
        var asyncToken = current;
        Next();
        var parsingAsyncParametersBeforeArrow = parsingAsyncParameters;
        var deferringAsyncParameterErrorsBeforeArrow = deferringAsyncParameterErrors;
        var deferredAsyncParameterErrorPositionBeforeArrow = deferredAsyncParameterErrorPosition;
        parsingAsyncParameters = true;
        deferringAsyncParameterErrors = current.Kind == JsTokenKind.LeftParen;
        deferredAsyncParameterErrorPosition = -1;
        try
        {
            if (current.Kind == JsTokenKind.LeftParen)
            {
                var callee = Arena.Add(
                    AstKind.Identifier,
                    Arena.AddString(GetIdentifierText(asyncToken)),
                    asyncToken.IdentifierId,
                    position: asyncToken.Position
                );
                return ParseParenthesizedExpressionOrArrow(
                    position,
                    isAsync: true,
                    asyncCallee: callee
                );
            }

            deferringAsyncParameterErrors = false;
            var parameter = ExpectIdentifier();
            var head = Arena.Add(
                AstKind.Identifier,
                Arena.AddString(GetIdentifierText(parameter)),
                parameter.IdentifierId,
                position: parameter.Position
            );
            return ParseArrowFunction(head, position, isAsync: true);
        }
        finally
        {
            parsingAsyncParameters = parsingAsyncParametersBeforeArrow;
            deferringAsyncParameterErrors = deferringAsyncParameterErrorsBeforeArrow;
            deferredAsyncParameterErrorPosition = deferredAsyncParameterErrorPositionBeforeArrow;
        }
    }

    private int ParseArrowFunction(int head, int position, bool isAsync = false)
    {
        if (current.HasLineTerminatorBefore)
            throw Error("Line terminator is not allowed before '=>'", current.Position);

        Span<int> initialNodes = stackalloc int[8];
        var nodes = new NodeList(initialNodes);
        Span<FlatParameter> initialParameters = stackalloc FlatParameter[8];
        var parameters = new ParameterList(initialParameters);
        ParameterNameTracker names = default;
        var hasDuplicate = false;
        var hasRestrictedName = false;
        var hasSimpleParameterList = true;
        var seenDefault = false;
        var functionLength = 0;
        var restParameterIndex = -1;
        var parsingAsyncParametersBeforeArrow = parsingAsyncParameters;
        try
        {
            if (head >= 0)
            {
                ref readonly var headNode = ref Arena[head];
                if (headNode.Kind == AstKind.SequenceExpression)
                {
                    var children = Arena.ChildRange(headNode.Arg0, headNode.Arg1);
                    for (var i = 0; i < children.Length; i++)
                        nodes.Add(children[i]);
                }
                else
                    nodes.Add(head);
            }

            var parameterNodes = nodes.AsSpan();
            for (var i = 0; i < parameterNodes.Length; i++)
            {
                ref readonly var parameterNode = ref Arena[parameterNodes[i]];
                var bindingNode = parameterNodes[i];
                var initializer = -1;
                var isRest = parameterNode.Kind == AstKind.SpreadElement;
                if (isRest)
                {
                    if (i != parameterNodes.Length - 1)
                        throw Error("Rest parameter must be last", Arena.GetPosition(bindingNode));
                    bindingNode = parameterNode.Arg0;
                    restParameterIndex = i;
                    hasSimpleParameterList = false;
                    seenDefault = true;
                }

                ref readonly var parameterValue = ref Arena[bindingNode];
                if (
                    parameterValue.Kind == AstKind.AssignmentExpression
                    && (JsAssignmentOperator)parameterValue.Arg2 == JsAssignmentOperator.Assign
                )
                {
                    if (isRest)
                        throw Error("Rest parameter cannot have an initializer", position);
                    bindingNode = parameterValue.Arg0;
                    initializer = parameterValue.Arg1;
                    hasSimpleParameterList = false;
                    seenDefault = true;
                }
                ref readonly var binding = ref Arena[bindingNode];
                var isPattern = binding.Kind is AstKind.ArrayExpression or AstKind.ObjectExpression;
                if (binding.Kind != AstKind.Identifier && !isPattern)
                    throw Error(
                        "Invalid arrow parameter binding",
                        Arena.GetPosition(parameterNodes[i])
                    );
                if (!seenDefault)
                    functionLength++;
                if (isPattern)
                {
                    hasSimpleParameterList = false;
                    TrackParameterPatternNames(
                        bindingNode,
                        ref names,
                        ref hasDuplicate,
                        ref hasRestrictedName
                    );
                    parameters.Add(
                        new FlatParameter(
                            Arena.AddString(
                                $"$arrow_pattern_{functionDepth}_{Arena.GetPosition(bindingNode)}"
                            ),
                            -1,
                            initializer,
                            bindingNode,
                            Arena.GetPosition(bindingNode),
                            isRest
                                ? JsFormalParameterBindingKind.RestPattern
                                : JsFormalParameterBindingKind.Pattern
                        )
                    );
                }
                else
                {
                    var name = Arena.GetString(binding.Arg0);
                    TrackParameterName(name, ref names, ref hasDuplicate, ref hasRestrictedName);
                    parameters.Add(
                        new FlatParameter(
                            binding.Arg0,
                            binding.Arg1,
                            initializer,
                            -1,
                            Arena.GetPosition(bindingNode),
                            isRest
                                ? JsFormalParameterBindingKind.Rest
                                : JsFormalParameterBindingKind.Plain
                        )
                    );
                }
            }

            if (hasDuplicate)
                throw Error("Duplicate parameter name", position);
            Expect(JsTokenKind.Arrow);
            parsingAsyncParameters = false;

            var parameterRange = ast.AddParameters(parameters.AsSpan());
            var strictBeforeFunction = strictMode;
            var loopDepthBeforeFunction = loopDepth;
            var switchDepthBeforeFunction = switchDepth;
            var forbidReturnInClassStaticBlockBeforeArrow = forbidReturnInClassStaticBlock;
            loopDepth = 0;
            switchDepth = 0;
            forbidReturnInClassStaticBlock = false;
            functionDepth++;
            var generatorDepthBeforeArrow = generatorFunctionDepth;
            var asyncDepthBeforeArrow = asyncFunctionDepth;
            generatorFunctionDepth = 0;
            asyncFunctionDepth = isAsync ? asyncDepthBeforeArrow + 1 : 0;
            int body;
            bool strictDeclared;
            try
            {
                if (current.Kind == JsTokenKind.LeftBrace)
                    body = ParseBlock(out strictDeclared, AstKind.Program, allowDirectives: true);
                else
                {
                    strictDeclared = false;
                    var expression = ParseAssignment(allowIn: true);
                    var returnStatement = Arena.Add(
                        AstKind.ReturnStatement,
                        expression,
                        position: Arena.GetPosition(expression)
                    );
                    Span<int> statements = [returnStatement];
                    var children = Arena.AddChildren(statements);
                    body = Arena.Add(
                        AstKind.Program,
                        children.Offset,
                        children.Count,
                        position: position
                    );
                }
            }
            finally
            {
                functionDepth--;
                generatorFunctionDepth = generatorDepthBeforeArrow;
                asyncFunctionDepth = asyncDepthBeforeArrow;
                loopDepth = loopDepthBeforeFunction;
                switchDepth = switchDepthBeforeFunction;
                forbidReturnInClassStaticBlock = forbidReturnInClassStaticBlockBeforeArrow;
            }

            var effectiveStrict = strictBeforeFunction || strictDeclared;
            strictMode = strictBeforeFunction;
            if (strictDeclared && !hasSimpleParameterList)
                throw Error(
                    "Illegal 'use strict' directive in function with non-simple parameters",
                    position
                );
            if (effectiveStrict && hasRestrictedName)
                throw Error("Unexpected eval or arguments in strict mode", position);
            var functionIndex = ast.AddFunction(
                new FlatFunctionInfo(
                    Arena.AddString(string.Empty),
                    -1,
                    parameterRange.Offset,
                    parameterRange.Count,
                    functionLength,
                    restParameterIndex,
                    effectiveStrict,
                    hasSimpleParameterList,
                    false,
                    position,
                    false,
                    true,
                    IsAsync: isAsync,
                    HasSuperPropertyReference: superPropertySeen
                )
            );
            return Arena.Add(
                AstKind.ArrowFunctionExpression,
                functionIndex,
                body,
                position: position
            );
        }
        finally
        {
            parsingAsyncParameters = parsingAsyncParametersBeforeArrow;
            parameters.Dispose();
            nodes.Dispose();
        }
    }

    private int ParseTemplateLiteral(in JsToken token, int tag = -1)
    {
        var contentStart = token.Position + 1;
        var contentEnd = token.Position + token.SourceLength - 1;
        Span<int> initial = stackalloc int[8];
        var parts = new NodeList(initial);
        var cooked = new PooledCharBuilder(stackalloc char[64]);
        var raw = new PooledCharBuilder(stackalloc char[64]);
        try
        {
            var cookedIsUndefined = false;
            var index = contentStart;
            while (index < contentEnd)
            {
                var value = source[index];
                if (value == '\\' && index + 1 < contentEnd)
                {
                    var valid = TemplateLiteralScanner.TryDecodeEscape(
                        source,
                        index,
                        out var decoded,
                        out var consumed,
                        out var normalizeRawLineContinuation
                    );
                    if (tag >= 0)
                    {
                        if (normalizeRawLineContinuation)
                            raw.Append("\\\n".AsSpan());
                        else
                            raw.Append(source.AsSpan(index, consumed));
                    }
                    if (!valid)
                    {
                        if (tag < 0)
                            throw Error("Invalid escape sequence in template literal", index);
                        cookedIsUndefined = true;
                    }
                    else if (!cookedIsUndefined)
                        cooked.Append(decoded.AsSpan());
                    index += consumed;
                    continue;
                }

                if (value == '\r')
                {
                    if (!cookedIsUndefined)
                        cooked.Append('\n');
                    if (tag >= 0)
                        raw.Append('\n');
                    if (index + 1 < contentEnd && source[index + 1] == '\n')
                        index++;
                    index++;
                    continue;
                }

                if (value == '$' && index + 1 < contentEnd && source[index + 1] == '{')
                {
                    AddTemplateQuasi(ref parts, cooked, raw, cookedIsUndefined, index, tag >= 0);
                    cooked.Clear();
                    if (tag >= 0)
                        raw.Clear();
                    cookedIsUndefined = false;
                    var expressionStart = index + 2;
                    var expressionEnd = TemplateLiteralScanner.FindExpressionEnd(
                        source,
                        expressionStart
                    );
                    if (expressionEnd < 0 || expressionEnd >= contentEnd)
                        throw Error("Unterminated template expression", index);

                    lexer.SetIndex(expressionStart);
                    current = lexer.NextToken();
                    parts.Add(ParseExpression());
                    if (current.Kind != JsTokenKind.RightBrace || current.Position != expressionEnd)
                        throw Error("Invalid template expression", current.Position);
                    index = expressionEnd + 1;
                    continue;
                }

                if (!cookedIsUndefined)
                    cooked.Append(value);
                if (tag >= 0)
                    raw.Append(value);
                index++;
            }

            AddTemplateQuasi(ref parts, cooked, raw, cookedIsUndefined, contentEnd, tag >= 0);
            lexer.SetIndex(token.Position + token.SourceLength);
            current = lexer.NextToken();
            if (tag >= 0)
            {
                var taggedChildren = Arena.AddChildren(parts.AsSpan());
                return Arena.Add(
                    AstKind.TaggedTemplateExpression,
                    tag,
                    taggedChildren.Offset,
                    taggedChildren.Count,
                    token.Position
                );
            }
            if (parts.Count == 1)
                return parts.AsSpan()[0];
            var children = Arena.AddChildren(parts.AsSpan());
            return Arena.Add(
                AstKind.TemplateExpression,
                children.Offset,
                children.Count,
                position: token.Position
            );
        }
        finally
        {
            raw.Dispose();
            cooked.Dispose();
            parts.Dispose();
        }
    }

    private void AddTemplateQuasi(
        ref NodeList parts,
        in PooledCharBuilder cooked,
        in PooledCharBuilder raw,
        bool cookedIsUndefined,
        int position,
        bool tagged
    )
    {
        if (tagged)
        {
            parts.Add(
                cookedIsUndefined
                    ? -1
                    : Arena.Add(
                        AstKind.StringLiteral,
                        Arena.AddString(cooked.ToString()),
                        position: position
                    )
            );
            parts.Add(
                Arena.Add(
                    AstKind.StringLiteral,
                    Arena.AddString(raw.ToString()),
                    position: position
                )
            );
            return;
        }

        parts.Add(
            Arena.Add(AstKind.StringLiteral, Arena.AddString(cooked.ToString()), position: position)
        );
    }

    private int ParseRegExpLiteral()
    {
        var start = current.Position;
        var literal = RegExpLiteralScanner.Scan(source, start);
        lexer.SetIndex(literal.End);
        current = lexer.NextToken();
        return Arena.Add(
            AstKind.RegExpLiteral,
            Arena.AddString(literal.Pattern),
            Arena.AddString(literal.Flags),
            position: start
        );
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

                var isAsyncMethod = false;
                if (
                    current.Kind == JsTokenKind.Identifier
                    && source
                        .AsSpan(current.Position, current.SourceLength)
                        .SequenceEqual("async".AsSpan())
                )
                {
                    var next = PeekToken();
                    if (
                        !next.HasLineTerminatorBefore
                        && next.Kind
                            is not (
                                JsTokenKind.LeftParen
                                or JsTokenKind.Colon
                                or JsTokenKind.Comma
                                or JsTokenKind.RightBrace
                                or JsTokenKind.Assign
                            )
                    )
                    {
                        isAsyncMethod = true;
                        Next();
                    }
                }

                var isGeneratorMethod = Match(JsTokenKind.Star);
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
                    if (isAsyncMethod)
                        throw Error("Async accessors are not valid", propertyPosition);
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
                        isMethod: true,
                        isGenerator: isGeneratorMethod,
                        isAsync: isAsyncMethod
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

                if (isGeneratorMethod)
                    throw Error("Expected '(' after generator method name", current.Position);
                if (isAsyncMethod)
                    throw Error("Expected '(' after async method name", current.Position);

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
                var restKind = Arena[node.Arg0].Kind;
                return restKind is AstKind.Identifier or AstKind.MemberExpression
                    || (
                        restKind is AstKind.ArrayExpression or AstKind.ObjectExpression
                        && IsDestructuringAssignmentTarget(node.Arg0)
                    );
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

    private string GetPrivateIdentifierText(in JsToken token) =>
        token.DataIndex >= 0
            ? "#" + lexer.GetIdentifierLiteral(token)
            : source.Substring(token.Position, token.SourceLength);

    private void ReferencePrivateName(string name, int position)
    {
        if (privateNameScope is null)
            throw Error($"Private name '{name}' is not declared in an enclosing class", position);
        privateNameScope.References.Add((name, position));
    }

    private void DeclarePrivateElement(
        PrivateNameScope scope,
        string name,
        JsClassElementKind kind,
        bool isStatic,
        int position
    )
    {
        if (name == "#constructor")
            throw Error("Class constructor may not be a private element", position);

        var accessorBit = kind switch
        {
            JsClassElementKind.Getter => (byte)1,
            JsClassElementKind.Setter => (byte)2,
            _ => (byte)0,
        };
        if (!scope.ElementDeclarations.TryGetValue(name, out var declaration))
        {
            scope.ElementDeclarations.Add(name, (isStatic, accessorBit));
            scope.Declarations.Add(name);
            return;
        }
        if (
            accessorBit != 0
            && declaration.AccessorMask != 0
            && declaration.IsStatic == isStatic
            && (declaration.AccessorMask & accessorBit) == 0
        )
        {
            scope.ElementDeclarations[name] = (
                isStatic,
                (byte)(declaration.AccessorMask | accessorBit)
            );
            return;
        }
        throw Error($"Duplicate private class member '{name}'", position);
    }

    private bool IsCurrentIdentifierName(string value) =>
        current.Kind is JsTokenKind.Identifier or JsTokenKind.ReservedWord
        && source.AsSpan(current.Position, current.SourceLength).SequenceEqual(value.AsSpan());

    private sealed class PrivateNameScope(PrivateNameScope? parent)
    {
        public HashSet<string> Declarations { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, (bool IsStatic, byte AccessorMask)> ElementDeclarations { get; } =
            new(StringComparer.Ordinal);
        public List<(string Name, int Position)> References { get; } = [];

        public bool Resolve(out (string Name, int Position) unresolved)
        {
            for (var i = 0; i < References.Count; i++)
            {
                var reference = References[i];
                if (Declarations.Contains(reference.Name))
                    continue;
                if (parent is not null)
                {
                    parent.References.Add(reference);
                    continue;
                }
                unresolved = reference;
                return false;
            }
            unresolved = default;
            return true;
        }
    }

    private void ValidateBindingIdentifier(in JsToken token)
    {
        var name = GetIdentifierText(token);
        if (
            strictMode
            && name
                is "eval"
                    or "arguments"
                    or "yield"
                    or "implements"
                    or "interface"
                    or "package"
                    or "private"
                    or "protected"
                    or "public"
                    or "static"
        )
            throw Error($"Invalid binding identifier '{name}' in strict mode", token.Position);
        if (isModule && name == "await")
            throw Error("Invalid binding identifier 'await' in module", token.Position);
        if ((asyncFunctionDepth > 0 || parsingAsyncParameters) && name == "await")
            ReportAsyncParameterError(token.Position);
    }

    private void ReportAsyncParameterError(int position)
    {
        if (deferringAsyncParameterErrors)
        {
            if (deferredAsyncParameterErrorPosition < 0)
                deferredAsyncParameterErrorPosition = position;
            return;
        }
        throw Error("Unexpected await in async function parameters", position);
    }

    private JsToken ExpectIdentifier()
    {
        if (
            current.Kind != JsTokenKind.Identifier
            && (current.Kind != JsTokenKind.Let || strictMode)
            && current.Kind != JsTokenKind.Of
        )
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

    private JsToken PeekToken()
    {
        var index = lexer.GetIndex();
        try
        {
            return lexer.NextToken();
        }
        finally
        {
            lexer.SetIndex(index);
        }
    }

    private bool IsAsyncFunctionPrefix()
    {
        if (
            current.Kind != JsTokenKind.Identifier
            || !source
                .AsSpan(current.Position, current.SourceLength)
                .SequenceEqual("async".AsSpan())
        )
            return false;
        var next = PeekToken();
        return !next.HasLineTerminatorBefore && next.Kind == JsTokenKind.Function;
    }

    private bool IsAsyncArrowPrefix()
    {
        if (
            current.Kind != JsTokenKind.Identifier
            || !source
                .AsSpan(current.Position, current.SourceLength)
                .SequenceEqual("async".AsSpan())
        )
            return false;

        var index = lexer.GetIndex();
        try
        {
            var token = lexer.NextToken();
            if (token.HasLineTerminatorBefore)
                return false;
            if (token.Kind == JsTokenKind.LeftParen)
                return true;
            if (token.Kind == JsTokenKind.Identifier)
            {
                var arrow = lexer.NextToken();
                return !arrow.HasLineTerminatorBefore && arrow.Kind == JsTokenKind.Arrow;
            }
            return false;
        }
        finally
        {
            lexer.SetIndex(index);
        }
    }

    private bool IsOptionalChainPunctuator()
    {
        if (current.Kind != JsTokenKind.Question)
            return false;
        var next = PeekToken();
        return !next.HasLineTerminatorBefore && next.Kind == JsTokenKind.Dot;
    }

    private readonly record struct ActiveLabel(string Name, bool IsIteration, int FunctionDepth);

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

    private ref struct ClassElementList
    {
        private Span<FlatClassElement> buffer;
        private FlatClassElement[]? rented;

        public ClassElementList(Span<FlatClassElement> initialBuffer)
        {
            buffer = initialBuffer;
        }

        public int Count { get; private set; }

        public void Add(FlatClassElement element)
        {
            if (Count == buffer.Length)
                Grow();
            buffer[Count++] = element;
        }

        public ReadOnlySpan<FlatClassElement> AsSpan() => buffer[..Count];

        public void Dispose()
        {
            if (rented is not null)
                ArrayPool<FlatClassElement>.Shared.Return(rented);
            rented = null;
            buffer = [];
            Count = 0;
        }

        private void Grow()
        {
            var next = ArrayPool<FlatClassElement>.Shared.Rent(Math.Max(8, buffer.Length * 2));
            buffer.CopyTo(next);
            if (rented is not null)
                ArrayPool<FlatClassElement>.Shared.Return(rented);
            rented = next;
            buffer = next;
        }
    }
}
