namespace Okojo.RegExp.Experimental;

internal sealed class ExperimentalCompiledProgram
{
    public required ScratchRegExpProgram TreeProgram { get; init; }
    public ExperimentalRegExpBytecodeProgram? BytecodeProgram { get; init; }

    public static ExperimentalCompiledProgram Create(ScratchRegExpProgram treeProgram)
    {
        return new()
        {
            TreeProgram = treeProgram,
            BytecodeProgram = ExperimentalRegExpCodeGenerator.TryGenerate(treeProgram)
        };
    }
}
