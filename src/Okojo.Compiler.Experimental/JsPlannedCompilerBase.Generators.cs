using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal abstract partial class JsPlannedCompilerBase
{
    protected void EmitGeneratorPrologue()
    {
        if (!isGenerator)
            return;
        generatorSwitchInstructionPc = builder.CodeLength;
        builder.Emit(JsOpCode.SwitchOnGeneratorState, 0xFF, 0, 0);
    }

    protected void PatchGeneratorSwitchTable()
    {
        if (!isGenerator)
            return;
        if (generatorResumeTargets.Count > byte.MaxValue)
            throw new NotSupportedException(
                "Generator switch table exceeds byte operand capacity."
            );
        var tableStart = builder.GeneratorSwitchTargetCount;
        if (tableStart > byte.MaxValue)
            throw new NotSupportedException(
                "Generator switch table offset exceeds byte operand capacity."
            );
        for (var i = 0; i < generatorResumeTargets.Count; i++)
            builder.AddGeneratorSwitchTarget(generatorResumeTargets[i]);
        builder.PatchByte(generatorSwitchInstructionPc + 2, (byte)tableStart);
        builder.PatchByte(generatorSwitchInstructionPc + 3, (byte)generatorResumeTargets.Count);
    }

    protected void EmitGeneratorPrestartSuspend()
    {
        builder.EmitLda(JsOpCode.LdaUndefined);
        EmitGeneratorSuspendResume(0xFD, guaranteedNextOnly: true);
    }

    private void EmitYieldExpression(FlatAst ast, in AstNode node)
    {
        if (!isGenerator)
            throw new InvalidOperationException("yield requires a generator function.");
        if (node.Arg0 >= 0)
            EmitExpression(ast, node.Arg0);
        else
            builder.EmitLda(JsOpCode.LdaUndefined);
        EmitGeneratorSuspendResume(0xFF, guaranteedNextOnly: false);
    }

    private void EmitGeneratorSuspendResume(byte generatorOperand, bool guaranteedNextOnly)
    {
        var registerCount = builder.RegisterCount;
        if (registerCount > byte.MaxValue)
            throw new NotSupportedException(
                "Generator live register range exceeds byte operand capacity."
            );
        if (nextGeneratorSuspendId >= byte.MaxValue)
            throw new NotSupportedException(
                "Generator suspend point count exceeds byte operand capacity."
            );

        var suspendId = nextGeneratorSuspendId++;
        builder.Emit(
            JsOpCode.SuspendGenerator,
            generatorOperand,
            0,
            (byte)registerCount,
            (byte)suspendId
        );
        while (generatorResumeTargets.Count <= suspendId)
            generatorResumeTargets.Add(-1);
        generatorResumeTargets[suspendId] = builder.CodeLength;
        builder.Emit(JsOpCode.ResumeGenerator, generatorOperand, 0, (byte)registerCount);

        if (generatorResumeValueRegister < 0)
            generatorResumeValueRegister = builder.AllocatePinnedRegister();
        EmitStar(generatorResumeValueRegister);
        if (guaranteedNextOnly)
        {
            builder.EmitCallRuntime((int)RuntimeId.GeneratorClearResumeState, 0, 0);
            EmitLdar(generatorResumeValueRegister);
            return;
        }

        if (generatorResumeModeRegister < 0)
            generatorResumeModeRegister = builder.AllocatePinnedRegister();
        builder.EmitCallRuntime((int)RuntimeId.GeneratorGetResumeMode, 0, 0);
        EmitStar(generatorResumeModeRegister);
        var next = builder.CreateLabel();
        var @return = builder.CreateLabel();
        var @throw = builder.CreateLabel();
        var done = builder.CreateLabel();
        builder.EmitLda(JsOpCode.LdaZero);
        EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, generatorResumeModeRegister);
        EmitJumpIfToBooleanTrue(next);
        EmitSmi(1);
        EmitRegisterWithSlotOp(JsOpCode.TestEqualStrict, generatorResumeModeRegister);
        EmitJumpIfToBooleanTrue(@return);
        EmitJump(@throw);

        builder.BindLabel(next);
        builder.EmitCallRuntime((int)RuntimeId.GeneratorClearResumeState, 0, 0);
        EmitLdar(generatorResumeValueRegister);
        EmitJump(done);

        builder.BindLabel(@return);
        builder.EmitCallRuntime((int)RuntimeId.GeneratorClearResumeState, 0, 0);
        EmitLdar(generatorResumeValueRegister);
        EmitAbruptCommand(AbruptCommand.Return);

        builder.BindLabel(@throw);
        builder.EmitCallRuntime((int)RuntimeId.GeneratorClearResumeState, 0, 0);
        EmitLdar(generatorResumeValueRegister);
        builder.Emit(JsOpCode.Throw);

        builder.BindLabel(done);
    }
}
