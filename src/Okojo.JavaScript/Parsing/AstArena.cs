using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Okojo.JavaScript.Parsing;

/// <summary>Node kind tags for the flat AST.</summary>
public enum AstKind : byte
{
    // Statements
    EmptyStatement = 0,
    BlockStatement,
    ExpressionStatement,
    VariableDeclaration,
    VariableDeclarator,
    IfStatement,
    ReturnStatement,
    FunctionDeclaration,
    ClassDeclaration,
    ForStatement,
    ForInOfStatement,
    WhileStatement,
    DoWhileStatement,
    BreakStatement,
    ContinueStatement,
    LabeledStatement,
    ThrowStatement,
    TryStatement,
    SwitchStatement,
    DebuggerStatement,
    ImportDeclaration,
    ExportDeclaration,

    // Expressions
    Identifier,
    NumericLiteral,
    StringLiteral,
    RegExpLiteral,
    ThisExpression,
    SuperExpression,
    BinaryExpression,
    UnaryExpression,
    UpdateExpression,
    AssignmentExpression,
    CallExpression,
    MemberExpression,
    ConditionalExpression,
    SequenceExpression,
    FunctionExpression,
    ArrowFunctionExpression,
    ClassExpression,
    NewExpression,
    ArrayExpression,
    ObjectExpression,
    TemplateExpression,
    YieldExpression,
    AwaitExpression,
    SpreadElement,

    // Auxiliary
    SwitchCase,
    CatchClause,
    VariableDeclaratorPattern,
    ObjectProperty,
    ClassElement,
    ImportSpecifier,
    ExportSpecifier,
}

/// <summary>
///     Flat-array AST node. 16 bytes fixed size.
///     Children referenced by integer index into the owning arena's node
///     array (-1 = no child). Extra data (strings, lists) stored in side
///     tables addressed by <c>Extra</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct AstNode
{
    /// <summary>Node kind tag.</summary>
    public AstKind Kind;

    /// <summary>First child index, literal slot index, or flag bits.</summary>
    public int Arg0;

    /// <summary>Second child index or operand index.</summary>
    public int Arg1;

    /// <summary>Third child index or extra data offset.</summary>
    public int Arg2;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AstNode Create(AstKind kind, int arg0 = -1, int arg1 = -1, int arg2 = -1)
    {
        AstNode n;
        n.Kind = kind;
        n.Arg0 = arg0;
        n.Arg1 = arg1;
        n.Arg2 = arg2;
        return n;
    }
}

/// <summary>
///     Arena-owned flat AST. All nodes live in one contiguous array
///     pre-sized from the source length. O(1) node creation via bump
///     pointer; entire tree freed when the arena is discarded.
///
///     Side tables hold variable-length data that does not fit in the
///     fixed-size node struct: child lists for blocks/calls/arguments,
///     identifier name string indices, numeric/string literal values.
/// </summary>
public sealed class AstArena
{
    private AstNode[] _nodes;
    private int _count;

    // Side tables: children and strings stored in shared pools with
    // (offset, count) addressing from the node's Arg fields.
    private int[] _childPool;
    private int _childPoolCount;
    private string[] _stringPool;
    private int _stringPoolCount;
    private double[] _numberPool;
    private int _numberPoolCount;

    public AstArena(string source)
    {
        var estimated = Math.Max(64, source.Length / 2 + 16);
        _nodes = new AstNode[estimated];
        _childPool = new int[Math.Max(256, estimated * 2)];
        _stringPool = new string[Math.Max(64, estimated / 4)];
        _numberPool = new double[Math.Max(32, estimated / 8)];
    }

    public int Count => _count;

    public ref AstNode this[int index] => ref _nodes[index];

    public ReadOnlySpan<AstNode> Nodes => _nodes.AsSpan(0, _count);

    public ReadOnlySpan<int> ChildRange(int offset, int count)
        => _childPool.AsSpan(offset, count);

    public string GetString(int poolIndex) => _stringPool[poolIndex];

    public double GetNumber(int poolIndex) => _numberPool[poolIndex];

    /// <summary>O(1) node creation. Grows the array if needed.</summary>
    public int Add(AstKind kind, int arg0 = -1, int arg1 = -1, int arg2 = -1)
    {
        if ((uint)_count >= (uint)_nodes.Length)
            GrowNodes();

        var idx = _count++;
        _nodes[idx].Kind = kind;
        _nodes[idx].Arg0 = arg0;
        _nodes[idx].Arg1 = arg1;
        _nodes[idx].Arg2 = arg2;
        return idx;
    }

    /// <summary>Allocates a contiguous range in the child pool for a list of child node indices.</summary>
    public (int Offset, int Count) AddChildren(ReadOnlySpan<int> children)
    {
        EnsureChildPool(children.Length);
        var offset = _childPoolCount;
        children.CopyTo(_childPool.AsSpan(offset));
        _childPoolCount += children.Length;
        return (offset, children.Length);
    }

    public int AddString(string s)
    {
        EnsureStringPool();
        var idx = _stringPoolCount++;
        _stringPool[idx] = s;
        return idx;
    }

    public int AddNumber(double d)
    {
        EnsureNumberPool();
        var idx = _numberPoolCount++;
        _numberPool[idx] = d;
        return idx;
    }

    private void GrowNodes()
    {
        var newSize = _nodes.Length * 2;
        Array.Resize(ref _nodes, newSize);
    }

    private void EnsureChildPool(int additional)
    {
        if (_childPoolCount + additional <= _childPool.Length)
            return;
        var newSize = Math.Max(_childPool.Length * 2, _childPoolCount + additional);
        Array.Resize(ref _childPool, newSize);
    }

    private void EnsureStringPool()
    {
        if (_stringPoolCount >= _stringPool.Length)
            Array.Resize(ref _stringPool, _stringPool.Length * 2);
    }

    private void EnsureNumberPool()
    {
        if (_numberPoolCount >= _numberPool.Length)
            Array.Resize(ref _numberPool, _numberPool.Length * 2);
    }
}
