namespace Okojo.Parsing;

public sealed class JsParseException : Exception
{
    public JsParseException(string message, int position, string? source = null)
        : base(FormatMessage(message, position, source, out var line, out var column))
    {
        Position = position;
        Line = line;
        Column = column;
    }

    public int Position { get; }
    public int Line { get; }
    public int Column { get; }

    private static string FormatMessage(string message, int position, string? source, out int line, out int column)
    {
        if (string.IsNullOrEmpty(source))
        {
            line = 0;
            column = 0;
            return $"{message} at position {position}.";
        }

        (line, column) = SourceLocation.GetLineColumn(source, position);
        return $"{message} at line {line}, column {column} (position {position}).";
    }
}

public abstract class JsNode
{
    public int Position { get; set; }
}

public sealed class JsProgram(
    IReadOnlyList<JsStatement> statements,
    bool strictDeclared,
    IReadOnlyList<string>? topLevelLexicalNames = null,
    bool hasTopLevelAwait = false,
    string? sourceText = null,
    string? sourcePath = null,
    JsIdentifierTable? identifierTable = null) : JsNode
{
    public IReadOnlyList<JsStatement> Statements { get; } = statements;
    public bool StrictDeclared { get; } = strictDeclared;
    public IReadOnlyList<string> TopLevelLexicalNames { get; } = topLevelLexicalNames ?? Array.Empty<string>();
    public bool HasTopLevelAwait { get; } = hasTopLevelAwait;
    public string? SourceText { get; } = sourceText;
    public string? SourcePath { get; } = sourcePath;
    public JsIdentifierTable? IdentifierTable { get; } = identifierTable;
}

public abstract class JsStatement : JsNode
{
}

public sealed class JsEmptyStatement : JsStatement
{
}

public sealed class JsBlockStatement(
    IReadOnlyList<JsStatement> statements,
    bool strictDeclared,
    bool bodyMayCreateNestedFunction = false) : JsStatement
{
    public IReadOnlyList<JsStatement> Statements { get; } = statements;
    public bool StrictDeclared { get; } = strictDeclared;
    public bool BodyMayCreateNestedFunction { get; } = bodyMayCreateNestedFunction;
    public int EndPosition { get; set; }
}

public sealed class JsExpressionStatement(JsExpression expression) : JsStatement
{
    public JsExpression Expression { get; } = expression;
}

public sealed class JsVariableDeclarationStatement(
    JsVariableDeclarationKind kind,
    IReadOnlyList<JsVariableDeclarator> declarators,
    JsExpression? bindingPattern = null,
    JsExpression? bindingInitializer = null)
    : JsStatement
{
    public JsVariableDeclarationKind Kind { get; } = kind;
    public IReadOnlyList<JsVariableDeclarator> Declarators { get; } = declarators;
    public JsExpression? BindingPattern { get; } = bindingPattern;
    public JsExpression? BindingInitializer { get; } = bindingInitializer;
}

public sealed class JsEmptyObjectBindingDeclarationStatement(
    JsVariableDeclarationKind kind,
    JsExpression initializer)
    : JsStatement
{
    public JsVariableDeclarationKind Kind { get; } = kind;
    public JsExpression Initializer { get; } = initializer;
}

public enum JsVariableDeclarationKind
{
    Var,
    Let,
    Const
}

public sealed class JsVariableDeclarator(string name, JsExpression? initializer, int nameId = -1) : JsNode
{
    public string Name { get; } = name;
    public JsExpression? Initializer { get; } = initializer;
    public int NameId { get; } = nameId;
}

public sealed class JsIfStatement(JsExpression test, JsStatement consequent, JsStatement? alternate)
    : JsStatement
{
    public JsExpression Test { get; } = test;
    public JsStatement Consequent { get; } = consequent;
    public JsStatement? Alternate { get; } = alternate;
}

public sealed class JsReturnStatement(JsExpression? argument) : JsStatement
{
    public JsExpression? Argument { get; } = argument;
}

public sealed class JsFunctionDeclaration(
    string name,
    IReadOnlyList<string> parameters,
    JsBlockStatement body,
    bool isGenerator = false,
    bool isAsync = false,
    IReadOnlyList<JsExpression?>? parameterInitializers = null,
    IReadOnlyList<JsExpression?>? parameterPatterns = null,
    IReadOnlyList<int>? parameterPositions = null,
    IReadOnlyList<JsFormalParameterBindingKind>? parameterBindingKinds = null,
    int? functionLength = null,
    bool hasSimpleParameterList = true,
    bool hasDuplicateParameters = false,
    int restParameterIndex = -1,
    int nameId = -1,
    IReadOnlyList<int>? parameterIds = null)
    : JsStatement
{
    public string Name { get; } = name;
    public int NameId { get; } = nameId;
    public IReadOnlyList<string> Parameters { get; } = parameters;

    public IReadOnlyList<int> ParameterIds { get; } =
        parameterIds ?? JsFunctionExpression.CreateDefaultParameterIds(parameters.Count);

    public JsBlockStatement Body { get; } = body;
    public bool IsGenerator { get; } = isGenerator;
    public bool IsAsync { get; } = isAsync;

    public IReadOnlyList<JsExpression?> ParameterInitializers { get; } =
        parameterInitializers ?? JsFunctionExpression.CreateDefaultInitializers(parameters.Count);

    public IReadOnlyList<JsExpression?> ParameterPatterns { get; } =
        parameterPatterns ?? JsFunctionExpression.CreateDefaultInitializers(parameters.Count);

    public IReadOnlyList<int> ParameterPositions { get; } =
        parameterPositions ?? JsFunctionExpression.CreateDefaultParameterPositions(parameters.Count);

    public IReadOnlyList<JsFormalParameterBindingKind> ParameterBindingKinds { get; } =
        parameterBindingKinds ??
        JsFunctionExpression.CreateDefaultParameterBindingKinds(parameters.Count, restParameterIndex);

    public int FunctionLength { get; } = functionLength ?? parameters.Count;
    public bool HasSimpleParameterList { get; } = hasSimpleParameterList;
    public bool HasDuplicateParameters { get; } = hasDuplicateParameters;
    public int RestParameterIndex { get; } = restParameterIndex;
}

public sealed class JsClassDeclaration(string name, JsClassExpression classExpression, int nameId = -1) : JsStatement
{
    public string Name { get; } = name;
    public JsClassExpression ClassExpression { get; } = classExpression;
    public int NameId { get; } = nameId;
}

public sealed class JsImportDeclaration(
    string? defaultBinding,
    string? namespaceBinding,
    IReadOnlyList<JsImportSpecifier> namedBindings,
    string source,
    bool sideEffectOnly,
    IReadOnlyList<JsImportAttribute>? attributes = null) : JsStatement
{
    public string? DefaultBinding { get; } = defaultBinding;
    public string? NamespaceBinding { get; } = namespaceBinding;
    public IReadOnlyList<JsImportSpecifier> NamedBindings { get; } = namedBindings;
    public string Source { get; } = source;
    public bool SideEffectOnly { get; } = sideEffectOnly;
    public IReadOnlyList<JsImportAttribute> Attributes { get; } = attributes ?? Array.Empty<JsImportAttribute>();
}

public sealed class JsImportSpecifier(string importedName, string localName) : JsNode
{
    public string ImportedName { get; } = importedName;
    public string LocalName { get; } = localName;
}

public sealed class JsExportDeclarationStatement(JsStatement declaration) : JsStatement
{
    public JsStatement Declaration { get; } = declaration;
}

public sealed class JsExportDefaultDeclaration(JsExpression expression, bool isDeclaration = false) : JsStatement
{
    public JsExpression Expression { get; } = expression;
    public bool IsDeclaration { get; } = isDeclaration;
}

public sealed class JsExportNamedDeclaration(
    IReadOnlyList<JsExportSpecifier> specifiers,
    string? source = null,
    IReadOnlyList<JsImportAttribute>? attributes = null) : JsStatement
{
    public IReadOnlyList<JsExportSpecifier> Specifiers { get; } = specifiers;
    public string? Source { get; } = source;
    public IReadOnlyList<JsImportAttribute> Attributes { get; } = attributes ?? Array.Empty<JsImportAttribute>();
}

public sealed class JsExportSpecifier(string localName, string exportedName) : JsNode
{
    public string LocalName { get; } = localName;
    public string ExportedName { get; } = exportedName;
}

public sealed class JsExportAllDeclaration(
    string source,
    string? exportedName = null,
    IReadOnlyList<JsImportAttribute>? attributes = null) : JsStatement
{
    public string Source { get; } = source;
    public string? ExportedName { get; } = exportedName;
    public IReadOnlyList<JsImportAttribute> Attributes { get; } = attributes ?? Array.Empty<JsImportAttribute>();
}

public sealed class JsImportAttribute(string key, string value) : JsNode
{
    public string Key { get; } = key;
    public string Value { get; } = value;
}

public sealed class JsWhileStatement(JsExpression test, JsStatement body) : JsStatement
{
    public JsExpression Test { get; } = test;
    public JsStatement Body { get; } = body;
}

public sealed class JsDoWhileStatement(JsStatement body, JsExpression test) : JsStatement
{
    public JsStatement Body { get; } = body;
    public JsExpression Test { get; } = test;
}

public sealed class JsForStatement(
    JsNode? init,
    JsExpression? test,
    JsExpression? update,
    JsStatement body,
    bool bodyMayCreateNestedFunction = false)
    : JsStatement
{
    public JsNode? Init { get; } = init;
    public JsExpression? Test { get; } = test;
    public JsExpression? Update { get; } = update;
    public JsStatement Body { get; } = body;
    public bool BodyMayCreateNestedFunction { get; } = bodyMayCreateNestedFunction;
}

public sealed class JsForInOfStatement(
    JsNode left,
    JsExpression right,
    bool isOf,
    JsStatement body,
    bool isAwait = false,
    bool bodyMayCreateNestedFunction = false)
    : JsStatement
{
    public JsNode Left { get; } = left;
    public JsExpression Right { get; } = right;
    public bool IsOf { get; } = isOf;
    public JsStatement Body { get; } = body;
    public bool IsAwait { get; } = isAwait;
    public bool BodyMayCreateNestedFunction { get; } = bodyMayCreateNestedFunction;
}

public sealed class JsBreakStatement(string? label = null) : JsStatement
{
    public string? Label { get; } = label;
}

public sealed class JsContinueStatement(string? label = null) : JsStatement
{
    public string? Label { get; } = label;
}

public sealed class JsLabeledStatement(string label, JsStatement statement) : JsStatement
{
    public string Label { get; } = label;
    public JsStatement Statement { get; } = statement;
}

public sealed class JsThrowStatement(JsExpression argument) : JsStatement
{
    public JsExpression Argument { get; } = argument;
}

public sealed class JsDebuggerStatement : JsStatement
{
}

public sealed class JsSwitchStatement(JsExpression discriminant, IReadOnlyList<JsSwitchCase> cases)
    : JsStatement
{
    public JsExpression Discriminant { get; } = discriminant;
    public IReadOnlyList<JsSwitchCase> Cases { get; } = cases;
}

public sealed class JsSwitchCase(JsExpression? test, IReadOnlyList<JsStatement> consequent) : JsNode
{
    public JsExpression? Test { get; } = test;
    public IReadOnlyList<JsStatement> Consequent { get; } = consequent;
}

public sealed class JsTryStatement(JsBlockStatement block, JsCatchClause? handler, JsBlockStatement? finalizer)
    : JsStatement
{
    public JsBlockStatement Block { get; } = block;
    public JsCatchClause? Handler { get; } = handler;
    public JsBlockStatement? Finalizer { get; } = finalizer;
}

public sealed class JsWithStatement(JsExpression o, JsStatement body) : JsStatement
{
    public JsExpression Object { get; } = o;
    public JsStatement Body { get; } = body;
}

public sealed class JsCatchClause(
    string? paramName,
    JsBlockStatement body,
    JsExpression? bindingPattern = null,
    IReadOnlyList<JsVariableDeclarator>? declarators = null) : JsNode
{
    public string? ParamName { get; } = paramName;
    public JsBlockStatement Body { get; } = body;
    public JsExpression? BindingPattern { get; } = bindingPattern;

    public IReadOnlyList<JsVariableDeclarator> Declarators { get; } =
        declarators ?? Array.Empty<JsVariableDeclarator>();
}

public abstract class JsExpression : JsNode
{
}

public sealed class JsIdentifierExpression(string name, int nameId = -1) : JsExpression
{
    public string Name { get; } = name;
    public int NameId { get; } = nameId;
}

public sealed class JsThisExpression : JsExpression
{
}

public sealed class JsSuperExpression : JsExpression
{
}

public sealed class JsLiteralExpression(object? value, string text) : JsExpression
{
    public JsLiteralExpression(string text) : this(text, text)
    {
    }

    public object? Value { get; } = value;
    public string Text { get; } = text;
}

public sealed class JsRegExpLiteralExpression(string pattern, string flags) : JsExpression
{
    public string Pattern { get; } = pattern;
    public string Flags { get; } = flags;
}

public enum JsUnaryOperator
{
    Plus,
    Minus,
    LogicalNot,
    BitwiseNot,
    Typeof,
    Void,
    Delete
}

public sealed class JsUnaryExpression(JsUnaryOperator @operator, JsExpression argument) : JsExpression
{
    public JsUnaryOperator Operator { get; } = @operator;
    public JsExpression Argument { get; } = argument;
}

public enum JsBinaryOperator
{
    LogicalAnd,
    LogicalOr,
    NullishCoalescing,
    BitwiseOr,
    BitwiseXor,
    BitwiseAnd,
    Equal,
    NotEqual,
    StrictEqual,
    StrictNotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    In,
    Instanceof,
    ShiftLeft,
    ShiftRight,
    ShiftRightLogical,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Exponentiate
}

public sealed class JsBinaryExpression(JsBinaryOperator @operator, JsExpression left, JsExpression right) : JsExpression
{
    public JsBinaryOperator Operator { get; } = @operator;
    public JsExpression Left { get; } = left;
    public JsExpression Right { get; } = right;
}

public enum JsAssignmentOperator
{
    Assign,
    AddAssign,
    SubtractAssign,
    MultiplyAssign,
    ExponentiateAssign,
    DivideAssign,
    ModuloAssign,
    ShiftLeftAssign,
    ShiftRightAssign,
    ShiftRightLogicalAssign,
    BitwiseAndAssign,
    BitwiseOrAssign,
    BitwiseXorAssign,
    LogicalAndAssign,
    LogicalOrAssign,
    NullishCoalescingAssign
}

public sealed class JsAssignmentExpression(
    JsAssignmentOperator @operator,
    JsExpression left,
    JsExpression right,
    bool isParenthesizedLeftHandSide = false) : JsExpression
{
    public JsAssignmentOperator Operator { get; } = @operator;
    public JsExpression Left { get; } = left;
    public JsExpression Right { get; } = right;
    public bool IsParenthesizedLeftHandSide { get; } = isParenthesizedLeftHandSide;
}

public sealed class JsCallExpression(JsExpression callee, IReadOnlyList<JsExpression> arguments) : JsExpression
{
    public JsCallExpression(JsExpression callee, IReadOnlyList<JsExpression> arguments, bool isOptionalChainSegment)
        : this(callee, arguments)
    {
        IsOptionalChainSegment = isOptionalChainSegment;
    }

    public JsExpression Callee { get; } = callee;
    public IReadOnlyList<JsExpression> Arguments { get; } = arguments;
    public bool IsOptionalChainSegment { get; }
}

public sealed class JsSpreadExpression(JsExpression argument) : JsExpression
{
    public JsExpression Argument { get; } = argument;
}

public sealed class JsIntrinsicCallExpression(ushort intrinsicId, IReadOnlyList<JsExpression> arguments) : JsExpression
{
    public ushort IntrinsicId { get; } = intrinsicId;
    public IReadOnlyList<JsExpression> Arguments { get; } = arguments;
}

public sealed class JsMemberExpression(JsExpression o, JsExpression property, bool isComputed) : JsExpression
{
    public JsMemberExpression(JsExpression o, JsExpression property, bool isComputed, bool isPrivate) : this(o,
        property, isComputed)
    {
        IsPrivate = isPrivate;
    }

    public JsMemberExpression(JsExpression o, JsExpression property, bool isComputed, bool isPrivate,
        bool isOptionalChainSegment) : this(o, property, isComputed, isPrivate)
    {
        IsOptionalChainSegment = isOptionalChainSegment;
    }

    public JsExpression Object { get; } = o;
    public JsExpression Property { get; } = property;
    public bool IsComputed { get; } = isComputed;
    public bool IsPrivate { get; }
    public bool IsOptionalChainSegment { get; }
}

public sealed class JsPrivateIdentifierExpression(string name, int nameId = -1) : JsExpression
{
    public string Name { get; } = name;
    public int NameId { get; } = nameId;
}

public sealed class JsConditionalExpression(JsExpression test, JsExpression consequent, JsExpression alternate)
    : JsExpression
{
    public JsExpression Test { get; } = test;
    public JsExpression Consequent { get; } = consequent;
    public JsExpression Alternate { get; } = alternate;
}

public sealed class JsSequenceExpression(IReadOnlyList<JsExpression> expressions) : JsExpression
{
    public IReadOnlyList<JsExpression> Expressions { get; } = expressions;
}

public enum JsUpdateOperator
{
    Increment,
    Decrement
}

public sealed class JsUpdateExpression(JsUpdateOperator @operator, JsExpression argument, bool isPrefix) : JsExpression
{
    public JsUpdateOperator Operator { get; } = @operator;
    public JsExpression Argument { get; } = argument;
    public bool IsPrefix { get; } = isPrefix;
}

public sealed class JsNewExpression(JsExpression callee, IReadOnlyList<JsExpression> arguments) : JsExpression
{
    public JsExpression Callee { get; } = callee;
    public IReadOnlyList<JsExpression> Arguments { get; } = arguments;
}

public sealed class JsNewTargetExpression : JsExpression;

public sealed class JsImportMetaExpression : JsExpression;

public sealed class JsImportCallExpression(JsExpression argument, JsExpression? options = null) : JsExpression
{
    public JsExpression Argument { get; } = argument;
    public JsExpression? Options { get; } = options;
}

public sealed class JsFunctionExpression(
    string? name,
    IReadOnlyList<string> parameters,
    JsBlockStatement body,
    bool isGenerator = false,
    bool isAsync = false,
    bool isArrow = false,
    IReadOnlyList<JsExpression?>? parameterInitializers = null,
    IReadOnlyList<JsExpression?>? parameterPatterns = null,
    IReadOnlyList<int>? parameterPositions = null,
    IReadOnlyList<JsFormalParameterBindingKind>? parameterBindingKinds = null,
    int? functionLength = null,
    bool hasSimpleParameterList = true,
    bool hasSuperBindingHint = false,
    bool hasDuplicateParameters = false,
    int restParameterIndex = -1,
    int nameId = -1,
    IReadOnlyList<int>? parameterIds = null)
    : JsExpression
{
    public string? Name { get; } = name;
    public int NameId { get; } = nameId;
    public IReadOnlyList<string> Parameters { get; } = parameters;

    public IReadOnlyList<int> ParameterIds { get; } =
        parameterIds ?? CreateDefaultParameterIds(parameters.Count);

    public JsBlockStatement Body { get; } = body;
    public bool IsGenerator { get; } = isGenerator;
    public bool IsAsync { get; } = isAsync;
    public bool IsArrow { get; } = isArrow;

    public IReadOnlyList<JsExpression?> ParameterInitializers { get; } =
        parameterInitializers ?? CreateDefaultInitializers(parameters.Count);

    public IReadOnlyList<JsExpression?> ParameterPatterns { get; } =
        parameterPatterns ?? CreateDefaultInitializers(parameters.Count);

    public IReadOnlyList<int> ParameterPositions { get; } =
        parameterPositions ?? CreateDefaultParameterPositions(parameters.Count);

    public IReadOnlyList<JsFormalParameterBindingKind> ParameterBindingKinds { get; } =
        parameterBindingKinds ?? CreateDefaultParameterBindingKinds(parameters.Count, restParameterIndex);

    public int FunctionLength { get; } = functionLength ?? parameters.Count;
    public bool HasSimpleParameterList { get; } = hasSimpleParameterList;
    public bool HasSuperBindingHint { get; } = hasSuperBindingHint;
    public bool HasDuplicateParameters { get; } = hasDuplicateParameters;
    public int RestParameterIndex { get; } = restParameterIndex;

    internal static IReadOnlyList<JsExpression?> CreateDefaultInitializers(int count)
    {
        if (count <= 0) return Array.Empty<JsExpression?>();

        return new JsExpression?[count];
    }

    internal static IReadOnlyList<int> CreateDefaultParameterPositions(int count)
    {
        if (count <= 0) return Array.Empty<int>();

        var positions = new int[count];
        Array.Fill(positions, -1);
        return positions;
    }

    internal static IReadOnlyList<int> CreateDefaultParameterIds(int count)
    {
        if (count <= 0) return Array.Empty<int>();

        var ids = new int[count];
        Array.Fill(ids, -1);
        return ids;
    }

    internal static IReadOnlyList<JsFormalParameterBindingKind> CreateDefaultParameterBindingKinds(int count,
        int restParameterIndex = -1)
    {
        if (count <= 0) return Array.Empty<JsFormalParameterBindingKind>();

        var kinds = new JsFormalParameterBindingKind[count];
        Array.Fill(kinds, JsFormalParameterBindingKind.Plain);
        if ((uint)restParameterIndex < (uint)count) kinds[restParameterIndex] = JsFormalParameterBindingKind.Rest;

        return kinds;
    }
}

public sealed class JsYieldExpression(JsExpression? argument, bool isDelegate) : JsExpression
{
    public JsExpression? Argument { get; } = argument;
    public bool IsDelegate { get; } = isDelegate;
}

public sealed class JsAwaitExpression(JsExpression argument) : JsExpression
{
    public JsExpression Argument { get; } = argument;
}

public sealed class JsParameterInitializerExpression(JsExpression expression, int parameterIndex) : JsExpression
{
    public JsExpression Expression { get; } = expression;
    public int ParameterIndex { get; } = parameterIndex;
}

public sealed class JsClassExpression(
    string? name,
    IReadOnlyList<JsClassElement> elements,
    IReadOnlyList<JsExpression>? decorators = null,
    bool hasExtends = false,
    JsExpression? extendsExpression = null,
    int nameId = -1) : JsExpression
{
    public string? Name { get; } = name;
    public int NameId { get; } = nameId;
    public IReadOnlyList<JsClassElement> Elements { get; } = elements;
    public IReadOnlyList<JsExpression> Decorators { get; } = decorators ?? Array.Empty<JsExpression>();
    public bool HasExtends { get; } = hasExtends;
    public JsExpression? ExtendsExpression { get; } = extendsExpression;
    public int EndPosition { get; set; }
}

public sealed class JsClassElement(
    string? key,
    JsClassElementKind kind,
    JsFunctionExpression? value,
    bool isStatic = false,
    JsExpression? computedKey = null,
    JsExpression? fieldInitializer = null,
    JsBlockStatement? staticBlock = null,
    bool isPrivate = false) : JsNode
{
    public string? Key { get; } = key;
    public JsClassElementKind Kind { get; } = kind;
    public JsFunctionExpression? Value { get; } = value;
    public bool IsStatic { get; } = isStatic;
    public JsExpression? ComputedKey { get; } = computedKey;
    public bool IsComputedKey => ComputedKey is not null;
    public JsExpression? FieldInitializer { get; } = fieldInitializer;
    public JsBlockStatement? StaticBlock { get; } = staticBlock;
    public bool IsPrivate { get; } = isPrivate;
}

public enum JsClassElementKind
{
    Constructor,
    Method,
    Getter,
    Setter,
    Field,
    StaticBlock
}

public sealed class JsArrayExpression(IReadOnlyList<JsExpression?> elements) : JsExpression
{
    public IReadOnlyList<JsExpression?> Elements { get; } = elements;
}

public sealed class JsTemplateExpression(
    IReadOnlyList<string?> quasis,
    IReadOnlyList<JsExpression> expressions,
    IReadOnlyList<string>? rawQuasis = null) : JsExpression
{
    public IReadOnlyList<string?> Quasis { get; } = quasis;
    public IReadOnlyList<JsExpression> Expressions { get; } = expressions;
    public IReadOnlyList<string> RawQuasis { get; } = rawQuasis ?? ToRaw(quasis);

    private static IReadOnlyList<string> ToRaw(IReadOnlyList<string?> quasis)
    {
        var raw = new string[quasis.Count];
        for (var i = 0; i < quasis.Count; i++)
            raw[i] = quasis[i] ?? string.Empty;
        return raw;
    }
}

public sealed class JsTaggedTemplateExpression(
    JsExpression tag,
    JsTemplateExpression template) : JsExpression
{
    public JsExpression Tag { get; } = tag;
    public JsTemplateExpression Template { get; } = template;
}

public sealed class JsObjectExpression(IReadOnlyList<JsObjectProperty> properties) : JsExpression
{
    public IReadOnlyList<JsObjectProperty> Properties { get; } = properties;
}

public sealed class JsObjectProperty(
    string key,
    JsExpression value,
    JsObjectPropertyKind kind = JsObjectPropertyKind.Data,
    JsExpression? computedKey = null)
    : JsNode
{
    public string Key { get; } = key;
    public JsExpression Value { get; } = value;
    public JsObjectPropertyKind Kind { get; } = kind;
    public JsExpression? ComputedKey { get; } = computedKey;
    public bool IsComputed => ComputedKey is not null;
}

public enum JsObjectPropertyKind
{
    Data,
    Getter,
    Setter,
    Spread
}
