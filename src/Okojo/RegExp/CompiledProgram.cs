namespace Okojo.RegExp;

internal enum WholeInputSimpleAtomKind : byte
{
    Literal,
    Dot,
    CharacterSet,
    PropertyEscape
}

internal readonly record struct WholeInputSimpleAtomPlan(
    WholeInputSimpleAtomKind Kind,
    RegExpRuntimeFlags Flags,
    int LiteralCodePoint = 0,
    RegExpCharacterSet? CharacterSet = null,
    RegExpPropertyEscape PropertyEscape = default);

internal readonly record struct WholeInputSimpleRunPlan(
    WholeInputSimpleAtomPlan Atom,
    int MinCount,
    int MaxCount);

internal readonly record struct LookaheadAssertionKey(
    ScratchRegExpProgram.Node Child,
    RegExpRuntimeFlags Flags);

internal sealed class CompiledProgram
{
    public required ScratchRegExpProgram TreeProgram { get; init; }
    public RegExpBytecodeProgram? BytecodeProgram { get; init; }
    public WholeInputSimpleRunPlan? WholeInputSimpleRunPlan { get; init; }
    public Dictionary<LookaheadAssertionKey, RegExpBytecodeProgram> LookaheadAssertionPrograms
    {
        get;
        init;
    } = [];

    public static CompiledProgram Create(ScratchRegExpProgram treeProgram)
    {
        var wholeInputSimpleRunPlan = TryBuildWholeInputSimpleRunPlan(treeProgram);
        if (wholeInputSimpleRunPlan is not null)
        {
            return new()
            {
                TreeProgram = treeProgram,
                WholeInputSimpleRunPlan = wholeInputSimpleRunPlan
            };
        }

        var irProgram = RegExpIrGenerator.TryGenerate(treeProgram);
        var bytecodeProgram = RegExpCodeGenerator.TryGenerate(irProgram)
            ?? throw new InvalidOperationException(" regexp compilation did not produce bytecode.");
        var lookaheadAssertionPrograms = BuildLookaheadAssertionPrograms(treeProgram);
        return new()
        {
            TreeProgram = treeProgram,
            BytecodeProgram = bytecodeProgram,
            WholeInputSimpleRunPlan = null,
            LookaheadAssertionPrograms = lookaheadAssertionPrograms
        };
    }

    internal bool TryGetLookaheadAssertionProgram(ScratchRegExpProgram.Node child, RegExpRuntimeFlags flags,
        out RegExpBytecodeProgram program)
    {
        return LookaheadAssertionPrograms.TryGetValue(new(child, flags), out program!);
    }

    private static Dictionary<LookaheadAssertionKey, RegExpBytecodeProgram>
        BuildLookaheadAssertionPrograms(ScratchRegExpProgram treeProgram)
    {
        var programs = new Dictionary<LookaheadAssertionKey, RegExpBytecodeProgram>();
        CollectLookaheadAssertionPrograms(treeProgram.Root, treeProgram.Flags, treeProgram.NodeCaptureIndices,
            treeProgram.NamedCaptureIndexes, programs);
        return programs;
    }

    private static void CollectLookaheadAssertionPrograms(ScratchRegExpProgram.Node node, RegExpRuntimeFlags flags,
        Dictionary<ScratchRegExpProgram.Node, int[]> nodeCaptureIndices, Dictionary<string, List<int>> namedCaptureIndexes,
        Dictionary<LookaheadAssertionKey, RegExpBytecodeProgram> programs)
    {
        switch (node)
        {
            case ScratchRegExpProgram.LookaheadNode lookahead:
                {
                    var key = new LookaheadAssertionKey(lookahead.Child, flags);
                    if (!programs.ContainsKey(key))
                    {
                        var irProgram = RegExpIrGenerator.TryGenerate(lookahead.Child, flags, nodeCaptureIndices,
                            namedCaptureIndexes);
                        var bytecodeProgram = RegExpCodeGenerator.TryGenerate(irProgram)
                            ?? throw new InvalidOperationException(" lookahead assertion compilation failed.");
                        programs.Add(key, bytecodeProgram);
                    }

                    CollectLookaheadAssertionPrograms(lookahead.Child, flags, nodeCaptureIndices, namedCaptureIndexes,
                        programs);
                    break;
                }
            case ScratchRegExpProgram.CaptureNode capture:
                CollectLookaheadAssertionPrograms(capture.Child, flags, nodeCaptureIndices, namedCaptureIndexes,
                    programs);
                break;
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                CollectLookaheadAssertionPrograms(scoped.Child, ApplyScopedModifiers(flags, scoped), nodeCaptureIndices,
                    namedCaptureIndexes, programs);
                break;
            case ScratchRegExpProgram.LookbehindNode lookbehind:
                CollectLookaheadAssertionPrograms(lookbehind.Child, flags, nodeCaptureIndices, namedCaptureIndexes,
                    programs);
                break;
            case ScratchRegExpProgram.QuantifierNode quantifier:
                CollectLookaheadAssertionPrograms(quantifier.Child, flags, nodeCaptureIndices, namedCaptureIndexes,
                    programs);
                break;
            case ScratchRegExpProgram.SequenceNode sequence:
                for (var i = 0; i < sequence.Terms.Length; i++)
                    CollectLookaheadAssertionPrograms(sequence.Terms[i], flags, nodeCaptureIndices, namedCaptureIndexes,
                        programs);
                break;
            case ScratchRegExpProgram.AlternationNode alternation:
                for (var i = 0; i < alternation.Alternatives.Length; i++)
                    CollectLookaheadAssertionPrograms(alternation.Alternatives[i], flags, nodeCaptureIndices,
                        namedCaptureIndexes, programs);
                break;
        }
    }

    private static WholeInputSimpleRunPlan? TryBuildWholeInputSimpleRunPlan(ScratchRegExpProgram treeProgram)
    {
        if (treeProgram.CaptureCount != 0 ||
            treeProgram.Flags.Multiline ||
            treeProgram.Root is not ScratchRegExpProgram.SequenceNode { Terms.Length: 3 } sequence ||
            sequence.Terms[0] is not ScratchRegExpProgram.AnchorNode { Start: true } ||
            sequence.Terms[2] is not ScratchRegExpProgram.AnchorNode { Start: false } ||
            sequence.Terms[1] is not ScratchRegExpProgram.QuantifierNode quantifier)
            return null;

        return TryBuildWholeInputSimpleAtomPlan(quantifier.Child, treeProgram.Flags, out var atomPlan)
            ? new(atomPlan, quantifier.Min, quantifier.Max)
            : null;
    }

    private static bool TryBuildWholeInputSimpleAtomPlan(ScratchRegExpProgram.Node node, RegExpRuntimeFlags flags,
        out WholeInputSimpleAtomPlan atomPlan)
    {
        switch (node)
        {
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                return TryBuildWholeInputSimpleAtomPlan(scoped.Child, ApplyScopedModifiers(flags, scoped), out atomPlan);
            case ScratchRegExpProgram.LiteralNode literal:
                atomPlan = new(WholeInputSimpleAtomKind.Literal, flags, LiteralCodePoint: literal.CodePoint);
                return true;
            case ScratchRegExpProgram.DotNode:
                atomPlan = new(WholeInputSimpleAtomKind.Dot, flags);
                return true;
            case ScratchRegExpProgram.ClassNode cls:
                atomPlan = new(WholeInputSimpleAtomKind.CharacterSet, flags,
                    CharacterSet: CreateCharacterSet(cls));
                return true;
            case ScratchRegExpProgram.PropertyEscapeNode propertyEscape:
                atomPlan = new(WholeInputSimpleAtomKind.PropertyEscape, flags,
                    PropertyEscape: new(propertyEscape.Kind, propertyEscape.Negated, propertyEscape.Categories,
                        propertyEscape.PropertyValue));
                return true;
            default:
                atomPlan = default;
                return false;
        }
    }

    private static RegExpCharacterSet CreateCharacterSet(ScratchRegExpProgram.ClassNode cls)
    {
        var asciiBitmap = ScratchRegExpProgram.TryBuildAsciiClassBitmap(cls, out var lowMask, out var highMask)
            ? new RegExpAsciiBitmap(lowMask, highMask)
            : default;
        var hasSimpleClass = ScratchRegExpProgram.TryCreateSimpleClass(cls, out var simpleClass);
        return new()
        {
            SimpleClass = hasSimpleClass ? simpleClass : null,
            ComplexClass = hasSimpleClass ? null : cls,
            AsciiBitmap = asciiBitmap
        };
    }

    private static RegExpRuntimeFlags ApplyScopedModifiers(RegExpRuntimeFlags flags,
        ScratchRegExpProgram.ScopedModifiersNode scoped)
    {
        return flags with
        {
            IgnoreCase = scoped.IgnoreCase ?? flags.IgnoreCase,
            Multiline = scoped.Multiline ?? flags.Multiline,
            DotAll = scoped.DotAll ?? flags.DotAll
        };
    }
}
