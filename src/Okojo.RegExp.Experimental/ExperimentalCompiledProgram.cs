namespace Okojo.RegExp.Experimental;

internal sealed class ExperimentalCompiledProgram
{
    public required ScratchRegExpProgram TreeProgram { get; init; }
    public ExperimentalRegExpIrProgram? IrProgram { get; init; }
    public ExperimentalRegExpBytecodeProgram? BytecodeProgram { get; init; }

    public static ExperimentalCompiledProgram Create(ScratchRegExpProgram treeProgram)
    {
        var irProgram = ExperimentalRegExpIrGenerator.TryGenerate(treeProgram);
        return new()
        {
            TreeProgram = treeProgram,
            IrProgram = irProgram,
            BytecodeProgram = ExperimentalRegExpCodeGenerator.TryGenerate(irProgram)
        };
    }
}
