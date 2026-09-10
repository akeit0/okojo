using NUnit.Framework;
using Okojo.JavaScript.Bytecode;

namespace Okojo.Tests;

/// <summary>
///     R6 contract audit: pins the operand-length table in BytecodeInfo
///     against engine-audited values, and keeps the two unit families
///     (prefix-scalable operand counts vs fixed byte lengths) from drifting.
///
///     Expected values were audited against the hand-coded decode in
///     JsRealm.VmLoop handlers and arms (the ground truth), then verified by
///     round-tripping the benchmark corpus through OkojoBytecodeTool.
///
///     When adding or changing an opcode: update BytecodeInfo AND the
///     expected map here in the same change. A mismatch means either the
///     metadata table or the VM decoder drifted - both consumers of the
///     contract below will point at the offender.
/// </summary>
public class BytecodeOperandContractTests
{
    // Opcodes whose operands are uniform-width and therefore scale with
    // Wide/ExtraWide prefixes. Must match BytecodeInfo.SupportsOperandScalePrefix.
    private static readonly HashSet<JsOpCode> ScalableOps =
    [
        JsOpCode.CallAny,
        JsOpCode.CallUndefinedReceiver,
        JsOpCode.CallProperty,
        JsOpCode.CallRuntime,
        JsOpCode.TestEqualStrict,
        JsOpCode.LdaKeyedProperty,
        JsOpCode.StaKeyedProperty,
        JsOpCode.DefineOwnKeyedProperty,
        JsOpCode.DefineOwnKeyedPropertyNoName,
        JsOpCode.Construct,
        // Single register operand, scaled by Wide/ExtraWide in both the VM
        // loop and the static decoder (feedback operand removed).
        JsOpCode.Add,
        JsOpCode.Sub,
        JsOpCode.Mul,
        JsOpCode.Div,
        JsOpCode.Mod,
        JsOpCode.Exp,
        JsOpCode.BitwiseAnd,
        JsOpCode.BitwiseOr,
        JsOpCode.BitwiseXor,
        JsOpCode.ShiftLeft,
        JsOpCode.ShiftRight,
        JsOpCode.ShiftRightLogical,
        JsOpCode.TestEqual,
        JsOpCode.TestNotEqual,
        JsOpCode.TestLessThan,
        JsOpCode.TestGreaterThan,
        JsOpCode.TestLessThanOrEqual,
        JsOpCode.TestGreaterThanOrEqual,
        JsOpCode.TestInstanceOf,
        JsOpCode.TestIn,
    ];

    // Single-scale operand byte length per opcode (engine-audited).
    private static readonly Dictionary<JsOpCode, int> ExpectedByteLength = new()
    {
        [JsOpCode.LdaUndefined] = 0,
        [JsOpCode.LdaNull] = 0,
        [JsOpCode.LdaTheHole] = 0,
        [JsOpCode.LdaTrue] = 0,
        [JsOpCode.LdaFalse] = 0,
        [JsOpCode.LdaZero] = 0,
        [JsOpCode.PushContextAcc] = 0,
        [JsOpCode.PopContext] = 0,
        [JsOpCode.LogicalNot] = 0,
        [JsOpCode.TypeOf] = 0,
        [JsOpCode.ToName] = 0,
        [JsOpCode.ToNumber] = 0,
        [JsOpCode.ToNumeric] = 0,
        [JsOpCode.ToString] = 0,
        [JsOpCode.LdaCurrentFunction] = 0,
        [JsOpCode.LdaThis] = 0,
        [JsOpCode.LdaNewTarget] = 0,
        [JsOpCode.CreateEmptyObjectLiteral] = 0,
        [JsOpCode.CreateEmptyArrayLiteral] = 0,
        [JsOpCode.Inc] = 0,
        [JsOpCode.Dec] = 0,
        [JsOpCode.CreateMappedArguments] = 0,
        [JsOpCode.Return] = 0,
        [JsOpCode.Throw] = 0,
        [JsOpCode.Debugger] = 0,
        [JsOpCode.PopTry] = 0,
        [JsOpCode.Wide] = 0,
        [JsOpCode.ExtraWide] = 0,
        [JsOpCode.BitwiseNot] = 0,
        [JsOpCode.Negate] = 0,

        [JsOpCode.LdaSmi] = 1,
        [JsOpCode.LdaNumericConstant] = 1,
        [JsOpCode.LdaStringConstant] = 1,
        [JsOpCode.Ldar] = 1,
        [JsOpCode.LdaLexicalLocal] = 1,
        [JsOpCode.Star] = 1,
        [JsOpCode.StaLexicalLocal] = 1,
        [JsOpCode.PushContext] = 1,
        [JsOpCode.LdaCurrentContextSlot] = 1,
        [JsOpCode.StaCurrentContextSlot] = 1,
        [JsOpCode.LdaCurrentContextSlotNoTdz] = 1,
        [JsOpCode.CreateBlockContext] = 1,
        [JsOpCode.ForInEnumerate] = 1,
        [JsOpCode.ForInNext] = 1,
        [JsOpCode.ForInStep] = 1,
        [JsOpCode.CreateRestParameter] = 1,
        [JsOpCode.LdaKeyedProperty] = 1,
        [JsOpCode.CreateFunctionContext] = 1,
        [JsOpCode.CreateFunctionContextWithCells] = 1,

        [JsOpCode.Mov] = 2,
        [JsOpCode.LdaGlobal] = 2,
        [JsOpCode.StaGlobal] = 2,
        [JsOpCode.StaGlobalInit] = 2,
        [JsOpCode.StaGlobalFuncDecl] = 2,
        [JsOpCode.CreateClosure] = 2,
        [JsOpCode.TypeOfGlobal] = 2,
        [JsOpCode.GetNamedPropertyFromSuper] = 2,
        [JsOpCode.LdaTypedConst] = 2,
        [JsOpCode.LdaModuleVariable] = 2,
        [JsOpCode.StaModuleVariable] = 2,
        [JsOpCode.Jump] = 2,
        [JsOpCode.JumpIfTrue] = 2,
        [JsOpCode.JumpIfFalse] = 2,
        [JsOpCode.JumpIfToBooleanTrue] = 2,
        [JsOpCode.JumpIfToBooleanFalse] = 2,
        [JsOpCode.JumpIfNull] = 2,
        [JsOpCode.JumpIfUndefined] = 2,
        [JsOpCode.JumpIfNotUndefined] = 2,
        [JsOpCode.JumpIfJsReceiver] = 2,
        [JsOpCode.PushTry] = 2,
        [JsOpCode.SwitchOnSmi] = 2,
        [JsOpCode.LdaNumericConstantWide] = 2,
        [JsOpCode.LdaSmiWide] = 2,
        [JsOpCode.LdarWide] = 2,
        [JsOpCode.LdaLexicalLocalWide] = 2,
        [JsOpCode.StarWide] = 2,
        [JsOpCode.StaLexicalLocalWide] = 2,
        [JsOpCode.LdaCurrentContextSlotWide] = 2,
        [JsOpCode.StaCurrentContextSlotWide] = 2,
        [JsOpCode.LdaCurrentContextSlotNoTdzWide] = 2,
        [JsOpCode.CreateFunctionContextWithCellsWide] = 2,
        [JsOpCode.CreateObjectLiteral] = 2,
        [JsOpCode.LdaNamedProperty] = 3,
        [JsOpCode.StaNamedProperty] = 3,
        [JsOpCode.CallRuntime] = 3,
        [JsOpCode.InvokeIntrinsic] = 3,
        [JsOpCode.CallAny] = 3,
        [JsOpCode.CallUndefinedReceiver] = 3,
        [JsOpCode.Construct] = 3,
        [JsOpCode.CreateArrayLiteral] = 2,
        [JsOpCode.CreateArrayLiteralWithLength] = 2,
        [JsOpCode.CreateClosureWide] = 3,
        [JsOpCode.JumpLoop] = 3,

        [JsOpCode.LdaContextSlot] = 2,
        [JsOpCode.StaContextSlot] = 2,
        [JsOpCode.LdaContextSlotNoTdz] = 2,
        [JsOpCode.StaKeyedProperty] = 2,
        [JsOpCode.DefineOwnKeyedProperty] = 2,
        [JsOpCode.DefineOwnKeyedPropertyNoName] = 2,
        [JsOpCode.Add] = 1,
        [JsOpCode.Sub] = 1,
        [JsOpCode.Mul] = 1,
        [JsOpCode.Div] = 1,
        [JsOpCode.Mod] = 1,
        [JsOpCode.Exp] = 1,
        [JsOpCode.AddSmi] = 1,
        [JsOpCode.SubSmi] = 1,
        [JsOpCode.MulSmi] = 1,
        [JsOpCode.ModSmi] = 1,
        [JsOpCode.ExpSmi] = 1,
        [JsOpCode.TestLessThanSmi] = 1,
        [JsOpCode.TestGreaterThanSmi] = 1,
        [JsOpCode.TestLessThanOrEqualSmi] = 1,
        [JsOpCode.TestGreaterThanOrEqualSmi] = 1,
        [JsOpCode.BitwiseOr] = 1,
        [JsOpCode.BitwiseXor] = 1,
        [JsOpCode.BitwiseAnd] = 1,
        [JsOpCode.ShiftLeft] = 1,
        [JsOpCode.ShiftRight] = 1,
        [JsOpCode.ShiftRightLogical] = 1,
        [JsOpCode.TestEqual] = 1,
        [JsOpCode.TestNotEqual] = 1,
        [JsOpCode.TestEqualStrict] = 1,
        [JsOpCode.TestLessThan] = 1,
        [JsOpCode.TestGreaterThan] = 1,
        [JsOpCode.TestLessThanOrEqual] = 1,
        [JsOpCode.TestGreaterThanOrEqual] = 1,
        [JsOpCode.TestInstanceOf] = 1,
        [JsOpCode.TestIn] = 1,

        [JsOpCode.CreateObjectLiteralWide] = 3,
        [JsOpCode.ResumeGenerator] = 3,
        [JsOpCode.SwitchOnGeneratorState] = 3,
        [JsOpCode.LdaContextSlotWide] = 3,
        [JsOpCode.StaContextSlotWide] = 3,
        [JsOpCode.LdaContextSlotNoTdzWide] = 3,
        [JsOpCode.LdaTypedConstWide] = 3,
        [JsOpCode.InitializeNamedProperty] = 4,
        [JsOpCode.InitializeArrayElement] = 4,
        [JsOpCode.LdaGlobalWide] = 4,
        [JsOpCode.StaGlobalWide] = 4,
        [JsOpCode.StaGlobalInitWide] = 4,
        [JsOpCode.StaGlobalFuncDeclWide] = 4,
        [JsOpCode.TypeOfGlobalWide] = 4,
        [JsOpCode.GetNamedPropertyFromSuperWide] = 4,
        [JsOpCode.MovWide] = 4,
        [JsOpCode.LdaSmiExtraWide] = 4,
        [JsOpCode.SuspendGenerator] = 4,
        [JsOpCode.CallProperty] = 4,
        [JsOpCode.GetPrivateField] = 7,
        [JsOpCode.InitPrivateField] = 8,
        [JsOpCode.InitPrivateMethod] = 8,
        [JsOpCode.SetPrivateField] = 8,
        [JsOpCode.LdaNamedPropertyWide] = 6,
        [JsOpCode.StaNamedPropertyWide] = 6,
        [JsOpCode.InitPrivateAccessor] = 9,
    };

    [Test]
    public void EveryOpcodeIsCoveredByTheContract()
    {
        var defined = Enum.GetValues<JsOpCode>();
        var missing = defined
            .Where(op => !ExpectedByteLength.ContainsKey(op))
            .Select(op => op.ToString())
            .ToList();

        Assert.That(
            missing,
            Is.Empty,
            "Opcodes missing from the R6 contract table:\n" + string.Join(", ", missing)
        );
        Assert.That(
            ExpectedByteLength.Keys.Count,
            Is.EqualTo(defined.Length),
            "R6 contract table contains opcodes that no longer exist."
        );
    }

    [Test]
    public void SingleScaleByteLengthMatchesEngineAudit()
    {
        var mismatches = new List<string>();
        foreach (var (op, expected) in ExpectedByteLength)
        {
            var actual = BytecodeInfo.GetSingleScaleByteLength(op);
            if (actual != expected)
                mismatches.Add($"{op}: metadata={actual}, engine audit expects {expected}");
        }

        Assert.That(
            mismatches,
            Is.Empty,
            "Operand byte-length drift between BytecodeInfo metadata and the "
                + "engine-audited contract:\n"
                + string.Join("\n", mismatches)
        );
    }

    [Test]
    public void ScalablePrefixSetMatchesContract()
    {
        var actualScalable = Enum.GetValues<JsOpCode>()
            .Where(BytecodeInfo.SupportsOperandScalePrefix)
            .ToList();

        Assert.That(
            actualScalable,
            Is.EquivalentTo(ScalableOps),
            "SupportsOperandScalePrefix drifted from the contract's scalable set."
        );
    }
}
