using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Compiler.Experimental;

internal abstract partial class JsPlannedCompilerBase
{
    protected void EmitGeneratorPrologue()
    {
        if (!isGenerator && !isAsync)
            return;
        generatorSwitchInstructionPc = builder.CodeLength;
        builder.Emit(JsOpCode.SwitchOnGeneratorState, 0xFF, 0, 0);
    }

    protected void PatchGeneratorSwitchTable()
    {
        if (!isGenerator && !isAsync)
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
        if (node.Arg1 != 0)
        {
            EmitYieldDelegateExpression(ast, node.Arg0);
            return;
        }
        if (node.Arg0 >= 0)
            EmitExpression(ast, node.Arg0);
        else
            builder.EmitLda(JsOpCode.LdaUndefined);
        EmitGeneratorSuspendResume(0xFF, guaranteedNextOnly: false);
    }

    private void EmitAwaitExpression(FlatAst ast, in AstNode node)
    {
        if (!isAsync)
            throw new InvalidOperationException("await requires an async function.");
        EmitExpression(ast, node.Arg0);
        EmitAwaitSuspension();
    }

    private void EmitAwaitSuspension(bool returnAsNext = false)
    {
        EmitGeneratorSuspendResume(0xFE, guaranteedNextOnly: false, returnAsNext: returnAsNext);
    }

    private void EmitYieldDelegateExpression(FlatAst ast, int argument)
    {
        var marker = builder.GetTemporaryRegisterScopeMarker();
        try
        {
            EmitExpression(ast, argument);
            var iterableRegister = builder.AllocateTemporaryRegister();
            EmitStar(iterableRegister);
            var methodRegister = builder.AllocateTemporaryRegister();
            var iteratorRegister = builder.AllocateTemporaryRegister();
            if (isAsync)
                EmitCreateAsyncOrSyncIterator(iterableRegister, methodRegister, iteratorRegister);
            else
            {
                builder.EmitCallRuntime((int)RuntimeId.GetIteratorMethod, iterableRegister, 1);
                EmitStar(methodRegister);
                builder.EmitCallProperty(methodRegister, iterableRegister, 0, 0);
                EmitStar(iteratorRegister);
            }
            EmitYieldDelegateLoop(iteratorRegister);
        }
        finally
        {
            builder.ReleaseTemporaryRegistersToMarker(marker);
        }
    }

    private void EmitCreateAsyncOrSyncIterator(
        int iterableRegister,
        int methodRegister,
        int iteratorRegister
    )
    {
        var useSyncIterator = builder.CreateLabel();
        var ready = builder.CreateLabel();
        builder.EmitCallRuntime((int)RuntimeId.GetAsyncIteratorMethod, iterableRegister, 1);
        EmitStar(methodRegister);
        EmitLdar(methodRegister);
        EmitJumpIfNull(useSyncIterator);
        EmitJumpIfUndefined(useSyncIterator);
        builder.EmitCallProperty(methodRegister, iterableRegister, 0, 0);
        EmitStar(iteratorRegister);
        EmitJump(ready);

        builder.BindLabel(useSyncIterator);
        builder.EmitCallRuntime((int)RuntimeId.GetIteratorMethod, iterableRegister, 1);
        EmitStar(methodRegister);
        builder.EmitCallProperty(methodRegister, iterableRegister, 0, 0);
        EmitStar(iteratorRegister);
        builder.EmitCallRuntime(
            (int)RuntimeId.WrapSyncIteratorForAsyncDelegate,
            iteratorRegister,
            1
        );
        EmitStar(iteratorRegister);
        builder.BindLabel(ready);
    }

    private void EmitYieldDelegateLoop(int iteratorRegister)
    {
        if (iteratorRegister > byte.MaxValue)
            throw new NotSupportedException(
                "Generator delegate iterator register exceeds byte operand capacity."
            );
        builder.EmitLda(JsOpCode.LdaUndefined);
        var sentRegister = builder.AllocateTemporaryRegister();
        EmitStar(sentRegister);
        var nextFunctionRegister = builder.AllocateTemporaryRegister();
        var argumentRegister = builder.AllocateTemporaryRegister();
        var resultRegister = builder.AllocateTemporaryRegister();
        var nextName = builder.AddAtomizedStringConstant("next");
        var doneName = builder.AddAtomizedStringConstant("done");
        var valueName = builder.AddAtomizedStringConstant("value");
        builder.EmitLdaNamedProperty(iteratorRegister, nextName, builder.AllocateFeedbackSlot());
        EmitStar(nextFunctionRegister);

        var loop = builder.CreateLabel();
        var yield = builder.CreateLabel();
        var done = builder.CreateLabel();
        builder.BindLabel(loop);
        EmitLdar(sentRegister);
        EmitStar(argumentRegister);
        builder.EmitCallProperty(nextFunctionRegister, iteratorRegister, argumentRegister, 1);
        if (isAsync)
            EmitAwaitSuspension();
        EmitStar(resultRegister);
        var resultIsObject = builder.CreateLabel();
        EmitLdar(resultRegister);
        builder.EmitJump(JsOpCode.JumpIfJsReceiver, resultIsObject);
        builder.EmitCallRuntime((int)RuntimeId.ThrowIteratorResultNotObject, 0, 0);
        builder.BindLabel(resultIsObject);

        builder.EmitLdaNamedProperty(resultRegister, doneName, builder.AllocateFeedbackSlot());
        EmitJumpIfToBooleanFalse(yield);
        builder.EmitLdaNamedProperty(resultRegister, valueName, builder.AllocateFeedbackSlot());
        EmitJump(done);

        builder.BindLabel(yield);
        if (isAsync)
            builder.EmitLdaNamedProperty(resultRegister, valueName, builder.AllocateFeedbackSlot());
        else
            EmitLdar(resultRegister);
        EmitGeneratorSuspendResume(
            (byte)iteratorRegister,
            guaranteedNextOnly: false,
            inspectActiveDelegateOnNext: true,
            delegateCompletedAsNext: done
        );
        EmitStar(sentRegister);
        EmitJump(loop);

        builder.BindLabel(done);
    }

    private void EmitGeneratorSuspendResume(
        byte generatorOperand,
        bool guaranteedNextOnly,
        bool inspectActiveDelegateOnNext = false,
        BytecodeBuilder.Label delegateCompletedAsNext = default,
        bool returnAsNext = false
    )
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
        if (inspectActiveDelegateOnNext)
        {
            var delegateActive = builder.CreateLabel();
            builder.EmitCallRuntime((int)RuntimeId.GeneratorHasActiveDelegateIterator, 0, 0);
            builder.EmitJump(JsOpCode.JumpIfTrue, delegateActive);
            EmitLdar(generatorResumeValueRegister);
            EmitJump(delegateCompletedAsNext);
            builder.BindLabel(delegateActive);
        }
        EmitLdar(generatorResumeValueRegister);
        EmitJump(done);

        builder.BindLabel(@return);
        builder.EmitCallRuntime((int)RuntimeId.GeneratorClearResumeState, 0, 0);
        EmitLdar(generatorResumeValueRegister);
        if (returnAsNext)
            EmitJump(done);
        else
            EmitAbruptCommand(AbruptCommand.Return);

        builder.BindLabel(@throw);
        builder.EmitCallRuntime((int)RuntimeId.GeneratorClearResumeState, 0, 0);
        EmitLdar(generatorResumeValueRegister);
        builder.Emit(JsOpCode.Throw);

        builder.BindLabel(done);
    }
}
