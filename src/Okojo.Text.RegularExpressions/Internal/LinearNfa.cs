using System.Buffers;
using System.Runtime.CompilerServices;
using Okojo.Text.Unicode;

namespace Okojo.Text.RegularExpressions.Internal;

internal enum LinearOpCode : byte
{
    Character,
    CharacterClass,
    Any,
    Split,
    Jump,
    AssertStart,
    AssertEnd,
    WordBoundary,
    Match,
    Fail,
}

internal readonly struct LinearInstruction
{
    internal LinearInstruction(LinearOpCode op, int a = 0, int b = 0)
    {
        Op = op;
        A = a;
        B = b;
    }

    internal LinearOpCode Op { get; }
    internal int A { get; }
    internal int B { get; }

    internal LinearInstruction WithA(int value) => new(Op, value, B);

    internal LinearInstruction WithTargets(int a, int b) => new(Op, a, b);
}

internal sealed class LinearProgram
{
    internal required LinearInstruction[] Code { get; init; }
    internal required CharSet[] Classes { get; init; }
}

internal static class LinearNfaCompiler
{
    internal static LinearProgram? TryCompile(RegexNode root, RegExpOptions options)
    {
        if (!options.EnableLinearEngine || !root.IsLinearEligible)
            return null;
        var builder = new Builder(options);
        if (!builder.TryCompile(root))
            return null;
        builder.Emit(LinearOpCode.Match);
        return builder.Finish();
    }

    private sealed class Builder
    {
        private readonly RegExpOptions _options;
        private readonly List<LinearInstruction> _code = [];
        private readonly List<CharSet> _classes = [];
        private readonly Dictionary<CharSet, int> _classIndices = new(
            ReferenceEqualityComparer.Instance
        );
        private int _repeatExpansion;

        internal Builder(RegExpOptions options) => _options = options;

        internal int Emit(LinearOpCode op, int a = 0, int b = 0)
        {
            if (_code.Count >= _options.MaxProgramSize)
                return -1;
            int pc = _code.Count;
            _code.Add(new LinearInstruction(op, a, b));
            return pc;
        }

        internal bool TryCompile(RegexNode node)
        {
            switch (node.Kind)
            {
                case RegexNodeKind.Empty:
                    return true;
                case RegexNodeKind.Literal:
                    return Emit(LinearOpCode.Character, node.Value, (int)node.Options) >= 0;
                case RegexNodeKind.Dot:
                    return Emit(LinearOpCode.Any, 0, (int)node.Options) >= 0;
                case RegexNodeKind.CharacterClass:
                {
                    NodeOptions nodeOptions =
                        node.Options | (node.Negative ? NodeOptions.InvertClass : NodeOptions.None);
                    return Emit(LinearOpCode.CharacterClass, GetClass(node.Set!), (int)nodeOptions)
                        >= 0;
                }
                case RegexNodeKind.AnchorStart:
                    return Emit(LinearOpCode.AssertStart, 0, (int)node.Options) >= 0;
                case RegexNodeKind.AnchorEnd:
                    return Emit(LinearOpCode.AssertEnd, 0, (int)node.Options) >= 0;
                case RegexNodeKind.WordBoundary:
                    return Emit(LinearOpCode.WordBoundary, node.Negative ? 1 : 0, (int)node.Options)
                        >= 0;
                case RegexNodeKind.Capture:
                    return TryCompile(node.Children[0]);
                case RegexNodeKind.Sequence:
                    foreach (RegexNode child in node.Children)
                    {
                        if (!TryCompile(child))
                            return false;
                    }
                    return true;
                case RegexNodeKind.Alternation:
                    return TryCompileAlternation(node.Children);
                case RegexNodeKind.Quantifier:
                    return TryCompileQuantifier(node);
                default:
                    return false;
            }
        }

        private bool TryCompileAlternation(RegexNode[] alternatives)
        {
            if (alternatives.Length == 0)
                return true;
            if (alternatives.Length == 1)
                return TryCompile(alternatives[0]);
            var jumps = new List<int>(alternatives.Length - 1);
            for (int i = 0; i < alternatives.Length - 1; i++)
            {
                int split = Emit(LinearOpCode.Split);
                if (split < 0)
                    return false;
                int preferred = _code.Count;
                if (!TryCompile(alternatives[i]))
                    return false;
                int jump = Emit(LinearOpCode.Jump);
                if (jump < 0)
                    return false;
                jumps.Add(jump);
                int fallback = _code.Count;
                _code[split] = _code[split].WithTargets(preferred, fallback);
            }
            if (!TryCompile(alternatives[^1]))
                return false;
            int end = _code.Count;
            foreach (int jump in jumps)
                _code[jump] = _code[jump].WithA(end);
            return true;
        }

        private bool TryCompileQuantifier(RegexNode node)
        {
            int maximumExpansion = node.Maximum < 0 ? node.Minimum + 1 : node.Maximum;
            if (
                maximumExpansion < 0
                || maximumExpansion > _options.MaxLinearRepeatExpansion - _repeatExpansion
            )
            {
                return false;
            }
            _repeatExpansion += maximumExpansion;

            RegexNode child = node.Children[0];
            for (int i = 0; i < node.Minimum; i++)
            {
                if (!TryCompile(child))
                    return false;
            }

            if (node.Maximum == node.Minimum)
                return true;
            if (node.Maximum < 0)
            {
                int loop = _code.Count;
                int split = Emit(LinearOpCode.Split);
                if (split < 0)
                    return false;
                int body = _code.Count;
                if (!TryCompile(child))
                    return false;
                if (Emit(LinearOpCode.Jump, loop) < 0)
                    return false;
                int exit = _code.Count;
                _code[split] = node.Greedy
                    ? _code[split].WithTargets(body, exit)
                    : _code[split].WithTargets(exit, body);
                return true;
            }

            for (int i = node.Minimum; i < node.Maximum; i++)
            {
                int split = Emit(LinearOpCode.Split);
                if (split < 0)
                    return false;
                int body = _code.Count;
                if (!TryCompile(child))
                    return false;
                int exit = _code.Count;
                _code[split] = node.Greedy
                    ? _code[split].WithTargets(body, exit)
                    : _code[split].WithTargets(exit, body);
            }
            return true;
        }

        private int GetClass(CharSet set)
        {
            if (_classIndices.TryGetValue(set, out int index))
                return index;
            index = _classes.Count;
            _classes.Add(set);
            _classIndices.Add(set, index);
            return index;
        }

        internal LinearProgram Finish() =>
            new() { Code = _code.ToArray(), Classes = _classes.ToArray() };
    }
}

internal static class LinearNfaRunner
{
    [SkipLocalsInit]
    internal static bool IsMatch(
        LinearProgram program,
        ReadOnlySpan<char> input,
        int startIndex,
        bool sticky,
        bool unicode,
        RegExpOptions options,
        SearchPlan? plan
    )
    {
        int length = program.Code.Length;
        if (length == 0)
            return false;
        int workspaceLength = checked(length * 4);
        int[]? rented = null;
        Span<int> workspace =
            length <= 1024
                ? stackalloc int[workspaceLength]
                : (rented = ArrayPool<int>.Shared.Rent(workspaceLength)).AsSpan(0, workspaceLength);
        Span<int> current = workspace[..length];
        Span<int> next = workspace.Slice(length, length);
        Span<int> stack = workspace.Slice(length * 2, length);
        Span<int> seen = workspace.Slice(length * 3, length);
        seen.Clear();

        var budget = new ExecutionBudget(options);
        int generation = 1;
        int currentCount = 0;
        int position = startIndex;

        try
        {
            while (position <= input.Length)
            {
                if (
                    (!sticky || position == startIndex)
                    && CanStartHere(plan, input, position, unicode)
                    && AddClosure(
                        program,
                        input,
                        startPc: 0,
                        position,
                        current,
                        ref currentCount,
                        stack,
                        seen,
                        generation,
                        ref budget
                    )
                )
                {
                    return true;
                }

                if (position == input.Length)
                    return false;
                int codePoint = Utf16.ReadForward(input, position, unicode, out int width);
                int nextGeneration = NextGeneration(generation, seen);
                int nextCount = 0;

                for (int i = 0; i < currentCount; i++)
                {
                    budget.Step();
                    int pc = current[i];
                    LinearInstruction instruction = program.Code[pc];
                    if (
                        Matches(program, instruction, codePoint)
                        && AddClosure(
                            program,
                            input,
                            pc + 1,
                            position + width,
                            next,
                            ref nextCount,
                            stack,
                            seen,
                            nextGeneration,
                            ref budget
                        )
                    )
                    {
                        return true;
                    }
                }

                Span<int> temporary = current;
                current = next;
                next = temporary;
                currentCount = nextCount;
                generation = nextGeneration;
                position += width;
                if (sticky && currentCount == 0)
                    return false;
                if (currentCount == 0 && plan?.Prefix is { Length: > 0 } skipPrefix)
                {
                    // No active NFA states: the only way to match is to start at the
                    // next literal-prefix occurrence, so skip the dead region.
                    int offset = input[position..]
                        .IndexOf(skipPrefix.AsSpan(), StringComparison.Ordinal);
                    if (offset < 0)
                        return false;
                    position += offset;
                }
            }
            return false;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<int>.Shared.Return(rented, clearArray: false);
        }
    }

    private static bool CanStartHere(
        SearchPlan? plan,
        ReadOnlySpan<char> input,
        int position,
        bool unicode
    )
    {
        if (plan is null)
            return true;
        if (plan.Anchor == SearchAnchor.AbsoluteStart)
            return position == 0;
        if (plan.MinimumLength > 0 && position + plan.MinimumLength > input.Length)
            return false;
        if (plan.Anchor == SearchAnchor.LineStart)
        {
            return position == 0 || Utf16.IsLineTerminator(input[position - 1]);
        }
        if (plan.Prefix is { Length: > 0 } prefix)
        {
            if (position + prefix.Length > input.Length || input[position] != prefix[0])
                return false;
            return input.Slice(position, prefix.Length).SequenceEqual(prefix.AsSpan());
        }
        if (plan.LeadingSet is { } leading && position < input.Length)
        {
            return leading.Contains(Utf16.ReadForward(input, position, unicode, out _));
        }
        return true;
    }

    private static bool AddClosure(
        LinearProgram program,
        ReadOnlySpan<char> input,
        int startPc,
        int position,
        Span<int> list,
        ref int listCount,
        Span<int> stack,
        Span<int> seen,
        int generation,
        ref ExecutionBudget budget
    )
    {
        int stackCount = 0;
        Push(startPc, stack, ref stackCount, seen, generation, program.Code.Length);
        while (stackCount != 0)
        {
            int pc = stack[--stackCount];
            budget.Step();
            LinearInstruction instruction = program.Code[pc];
            switch (instruction.Op)
            {
                case LinearOpCode.Match:
                    return true;
                case LinearOpCode.Fail:
                    break;
                case LinearOpCode.Jump:
                    Push(
                        instruction.A,
                        stack,
                        ref stackCount,
                        seen,
                        generation,
                        program.Code.Length
                    );
                    break;
                case LinearOpCode.Split:
                    Push(
                        instruction.B,
                        stack,
                        ref stackCount,
                        seen,
                        generation,
                        program.Code.Length
                    );
                    Push(
                        instruction.A,
                        stack,
                        ref stackCount,
                        seen,
                        generation,
                        program.Code.Length
                    );
                    break;
                case LinearOpCode.AssertStart:
                {
                    NodeOptions nodeOptions = (NodeOptions)instruction.B;
                    if (
                        position == 0
                        || (nodeOptions & NodeOptions.Multiline) != 0
                            && position > 0
                            && Utf16.IsLineTerminator(input[position - 1])
                    )
                    {
                        Push(pc + 1, stack, ref stackCount, seen, generation, program.Code.Length);
                    }
                    break;
                }
                case LinearOpCode.AssertEnd:
                {
                    NodeOptions nodeOptions = (NodeOptions)instruction.B;
                    if (
                        position == input.Length
                        || (nodeOptions & NodeOptions.Multiline) != 0
                            && position < input.Length
                            && Utf16.IsLineTerminator(input[position])
                    )
                    {
                        Push(pc + 1, stack, ref stackCount, seen, generation, program.Code.Length);
                    }
                    break;
                }
                case LinearOpCode.WordBoundary:
                {
                    bool boundary = BacktrackingVm.IsWordBoundary(
                        input,
                        position,
                        (NodeOptions)instruction.B
                    );
                    if (boundary != (instruction.A != 0))
                        Push(pc + 1, stack, ref stackCount, seen, generation, program.Code.Length);
                    break;
                }
                default:
                    list[listCount++] = pc;
                    break;
            }
        }
        return false;
    }

    private static bool Matches(LinearProgram program, LinearInstruction instruction, int codePoint)
    {
        NodeOptions options = (NodeOptions)instruction.B;
        bool unicode = (options & NodeOptions.Unicode) != 0;
        return instruction.Op switch
        {
            LinearOpCode.Character => codePoint == instruction.A
                || (options & NodeOptions.IgnoreCase) != 0
                    && (
                        unicode
                            ? UnicodeCaseFolding.EqualsUnicode(codePoint, instruction.A)
                            : UnicodeCaseFolding.EqualsLegacy(codePoint, instruction.A)
                    ),
            LinearOpCode.CharacterClass => MatchClass(
                program.Classes[instruction.A],
                codePoint,
                options
            ),
            LinearOpCode.Any => (options & NodeOptions.DotAll) != 0
                || !Utf16.IsLineTerminator(codePoint),
            _ => false,
        };
    }

    private static bool MatchClass(CharSet set, int codePoint, NodeOptions options)
    {
        bool matches;
        if ((options & NodeOptions.IgnoreCase) == 0)
        {
            matches = set.Contains(codePoint);
        }
        else if ((options & NodeOptions.UnicodeSets) != 0)
        {
            matches = set.Contains(UnicodeCaseFolding.CanonicalizeUnicode(codePoint));
        }
        else
        {
            matches = set.ContainsCaseInsensitive(codePoint, (options & NodeOptions.Unicode) != 0);
        }
        return (options & NodeOptions.InvertClass) != 0 ? !matches : matches;
    }

    private static void Push(
        int pc,
        Span<int> stack,
        ref int count,
        Span<int> seen,
        int generation,
        int programLength
    )
    {
        if ((uint)pc >= (uint)programLength || seen[pc] == generation)
            return;
        seen[pc] = generation;
        stack[count++] = pc;
    }

    private static int NextGeneration(int generation, Span<int> seen)
    {
        if (generation == int.MaxValue)
        {
            seen.Clear();
            return 1;
        }
        return generation + 1;
    }
}
