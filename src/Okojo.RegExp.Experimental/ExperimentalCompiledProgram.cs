namespace Okojo.RegExp.Experimental;

internal readonly record struct ExperimentalWholeInputPropertyRunPlan(
    ExperimentalRegExpPropertyEscape PropertyEscape,
    int MinCount);

internal sealed class ExperimentalCompiledProgram
{
    public required ScratchRegExpProgram TreeProgram { get; init; }
    public ExperimentalRegExpIrProgram? IrProgram { get; init; }
    public ExperimentalRegExpBytecodeProgram? BytecodeProgram { get; init; }
    public ExperimentalWholeInputPropertyRunPlan? WholeInputPropertyRunPlan { get; init; }

    public static ExperimentalCompiledProgram Create(ScratchRegExpProgram treeProgram)
    {
        var irProgram = ExperimentalRegExpIrGenerator.TryGenerate(treeProgram);
        return new()
        {
            TreeProgram = treeProgram,
            IrProgram = irProgram,
            BytecodeProgram = ExperimentalRegExpCodeGenerator.TryGenerate(irProgram),
            WholeInputPropertyRunPlan = TryBuildWholeInputPropertyRunPlan(treeProgram)
        };
    }

    private static ExperimentalWholeInputPropertyRunPlan? TryBuildWholeInputPropertyRunPlan(ScratchRegExpProgram treeProgram)
    {
        if (treeProgram.CaptureCount != 0 ||
            treeProgram.Flags.Multiline ||
            treeProgram.Root is not ScratchRegExpProgram.SequenceNode { Terms.Length: 3 } sequence ||
            sequence.Terms[0] is not ScratchRegExpProgram.AnchorNode { Start: true } ||
            sequence.Terms[2] is not ScratchRegExpProgram.AnchorNode { Start: false } ||
            sequence.Terms[1] is not ScratchRegExpProgram.QuantifierNode
            {
                Max: int.MaxValue,
                Child: ScratchRegExpProgram.PropertyEscapeNode propertyEscape
            } quantifier)
            return null;

        return new(new(propertyEscape.Kind, propertyEscape.Negated, propertyEscape.Categories, propertyEscape.PropertyValue),
            quantifier.Min);
    }
}
