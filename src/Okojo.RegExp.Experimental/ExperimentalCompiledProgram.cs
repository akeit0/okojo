namespace Okojo.RegExp.Experimental;

internal enum ExperimentalWholeInputSimpleAtomKind : byte
{
    Literal,
    Dot,
    CharacterSet,
    PropertyEscape
}

internal readonly record struct ExperimentalWholeInputSimpleAtomPlan(
    ExperimentalWholeInputSimpleAtomKind Kind,
    RegExpRuntimeFlags Flags,
    int LiteralCodePoint = 0,
    ExperimentalRegExpCharacterSet? CharacterSet = null,
    ExperimentalRegExpPropertyEscape PropertyEscape = default);

internal readonly record struct ExperimentalWholeInputSimpleRunPlan(
    ExperimentalWholeInputSimpleAtomPlan Atom,
    int MinCount,
    int MaxCount);

internal sealed class ExperimentalCompiledProgram
{
    public required ScratchRegExpProgram TreeProgram { get; init; }
    public ExperimentalRegExpBytecodeProgram? BytecodeProgram { get; init; }
    public ExperimentalWholeInputSimpleRunPlan? WholeInputSimpleRunPlan { get; init; }

    public static ExperimentalCompiledProgram Create(ScratchRegExpProgram treeProgram)
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

        var irProgram = ExperimentalRegExpIrGenerator.TryGenerate(treeProgram);
        var bytecodeProgram = ExperimentalRegExpCodeGenerator.TryGenerate(irProgram)
            ?? throw new InvalidOperationException("Experimental regexp compilation did not produce bytecode.");
        return new()
        {
            TreeProgram = treeProgram,
            BytecodeProgram = bytecodeProgram,
            WholeInputSimpleRunPlan = null
        };
    }

    private static ExperimentalWholeInputSimpleRunPlan? TryBuildWholeInputSimpleRunPlan(ScratchRegExpProgram treeProgram)
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
        out ExperimentalWholeInputSimpleAtomPlan atomPlan)
    {
        switch (node)
        {
            case ScratchRegExpProgram.ScopedModifiersNode scoped:
                return TryBuildWholeInputSimpleAtomPlan(scoped.Child, ApplyScopedModifiers(flags, scoped), out atomPlan);
            case ScratchRegExpProgram.LiteralNode literal:
                atomPlan = new(ExperimentalWholeInputSimpleAtomKind.Literal, flags, LiteralCodePoint: literal.CodePoint);
                return true;
            case ScratchRegExpProgram.DotNode:
                atomPlan = new(ExperimentalWholeInputSimpleAtomKind.Dot, flags);
                return true;
            case ScratchRegExpProgram.ClassNode cls:
                atomPlan = new(ExperimentalWholeInputSimpleAtomKind.CharacterSet, flags,
                    CharacterSet: CreateCharacterSet(cls));
                return true;
            case ScratchRegExpProgram.PropertyEscapeNode propertyEscape:
                atomPlan = new(ExperimentalWholeInputSimpleAtomKind.PropertyEscape, flags,
                    PropertyEscape: new(propertyEscape.Kind, propertyEscape.Negated, propertyEscape.Categories,
                        propertyEscape.PropertyValue));
                return true;
            default:
                atomPlan = default;
                return false;
        }
    }

    private static ExperimentalRegExpCharacterSet CreateCharacterSet(ScratchRegExpProgram.ClassNode cls)
    {
        var asciiBitmap = ScratchRegExpProgram.TryBuildAsciiClassBitmap(cls, out var lowMask, out var highMask)
            ? new ExperimentalRegExpAsciiBitmap(lowMask, highMask)
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
