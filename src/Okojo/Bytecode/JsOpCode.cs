namespace Okojo.Bytecode;

/// <summary>
///     Okojo Virtual Machine Opcodes.
///     Heavily inspired by V8 Ignition.
///     [acc] = Accumulator
///     [rX] = Register index (local/parameter)
///     [idx] = Constant pool index
///     [slot] = Feedback vector slot (for future IC support)
/// </summary>
public enum JsOpCode : byte
{
    // --- Loading / Moving ---
    LdaUndefined = 0x00,
    LdaNull,
    LdaTheHole, // For TDZ (Temporal Dead Zone)
    LdaTrue,
    LdaFalse,
    LdaZero,
    LdaSmi, // [imm8] -> acc
    LdaSmiWide, // [imm16_le] -> acc
    LdaSmiExtraWide, // [imm32_le] -> acc
    LdaNumericConstant, // [idx] numeric constant pool -> acc
    LdaNumericConstantWide, // [idx_lo] [idx_hi] numeric constant pool -> acc
    LdaStringConstant, // [idx] object constant pool string -> acc
    LdaTypedConst, // [tag] [idx] typed object constant pool -> acc
    LdaTypedConstWide, // [tag] [idx_lo] [idx_hi] typed object constant pool -> acc
    LdaThis, // current frame thisValue -> acc
    LdaNewTarget, // current frame new.target -> acc
    Ldar, // [reg] -> acc
    LdaLexicalLocal, // [reg] lexical local read with TDZ check
    Star, // acc -> [reg]
    StaLexicalLocal, // [reg] lexical local write with TDZ check
    LdaModuleVariable, // [cell_index_s8] [depth_u8] module binding load
    StaModuleVariable, // [cell_index_s8] [depth_u8] module binding store
    Mov, // [src_reg] -> [dst_reg]

    // --- Global Access ---
    LdaGlobal, // [name_idx] [slot]
    LdaGlobalWide, // [name_idx_lo] [name_idx_hi] [slot_lo] [slot_hi]
    StaGlobal, // [name_idx] [slot] strict path checks unresolvable + readonly
    StaGlobalWide, // [name_idx_lo] [name_idx_hi] [slot_lo] [slot_hi]
    StaGlobalInit, // [name_idx] [slot] declaration/initialization write
    StaGlobalInitWide, // [name_idx_lo] [name_idx_hi] [slot_lo] [slot_hi]
    StaGlobalFuncDecl, // [name_idx] [slot] global function declaration initialization
    StaGlobalFuncDeclWide, // [name_idx_lo] [name_idx_hi] [slot_lo] [slot_hi]
    TypeOfGlobal, // [name_idx] [slot] (no ReferenceError on missing global)
    TypeOfGlobalWide, // [name_idx_lo] [name_idx_hi] [slot_lo] [slot_hi]

    // --- Property Access ---
    LdaNamedProperty, // [obj_reg] [name_idx] [slot]
    LdaNamedPropertyWide, // [obj_reg] [name_idx_lo] [name_idx_hi] [slot_lo] [slot_hi]
    GetNamedPropertyFromSuper, // [name_idx] (receiver is current this)
    GetNamedPropertyFromSuperWide, // [name_idx_lo] [name_idx_hi]
    LdaKeyedProperty, // [obj_reg] (key in acc)
    StaNamedProperty, // [obj_reg] [name_idx] [slot] (value in acc)
    StaNamedPropertyWide, // [obj_reg] [name_idx_lo] [name_idx_hi] [slot_lo] [slot_hi]
    StaKeyedProperty, // [obj_reg] [key_reg] (value in acc)
    InitializeArrayElement, // [obj_reg_lo] [obj_reg_hi] [index_lo] [index_hi] (value in acc, fresh array literal init)
    DefineOwnKeyedProperty, // [obj_reg] [key_reg] (value in acc, own data define)
    InitializeNamedProperty, // [obj_reg_lo] [obj_reg_hi] [slot_lo] [slot_hi] (value in acc, literal init fast path)

    // --- Context / Scoping ---
    PushContext, // [reg]
    PushContextAcc, // acc must hold a JsContext
    PopContext,
    LdaContextSlot, // [slot_idx] [depth]
    LdaContextSlotWide, // [slot_idx_lo] [slot_idx_hi] [depth]
    StaContextSlot, // [slot_idx] [depth] (value in acc)
    StaContextSlotWide, // [slot_idx_lo] [slot_idx_hi] [depth] (value in acc)
    LdaCurrentContextSlot, // [slot_idx]
    LdaCurrentContextSlotWide, // [slot_idx_lo] [slot_idx_hi]
    LdaContextSlotNoTdz, // [slot_idx] [depth]
    LdaContextSlotNoTdzWide, // [slot_idx_lo] [slot_idx_hi] [depth]
    LdaCurrentContextSlotNoTdz, // [slot_idx]
    LdaCurrentContextSlotNoTdzWide, // [slot_idx_lo] [slot_idx_hi]
    StaCurrentContextSlot, // [slot_idx] (value in acc)
    StaCurrentContextSlotWide, // [slot_idx_lo] [slot_idx_hi] (value in acc)

    // --- Binary Operations (acc = acc op reg) ---
    Add, // [reg] [slot]
    Sub, // [reg] [slot]
    AddSmi, // [imm8] [slot] (lhs in acc)
    SubSmi, // [imm8] [slot] (lhs in acc)
    Inc, // acc = acc + 1
    Dec, // acc = acc - 1
    MulSmi, // [imm8] [slot] (lhs in acc)
    ModSmi, // [imm8] [slot] (lhs in acc)
    ExpSmi, // [imm8] [slot] (lhs in acc)
    Mul, // [reg] [slot]
    Div, // [reg] [slot]
    Mod, // [reg] [slot]
    Exp, // [reg] [slot]
    BitwiseOr, // [reg] [slot]
    BitwiseXor, // [reg] [slot]
    BitwiseAnd, // [reg] [slot]
    ShiftLeft, // [reg] [slot]
    ShiftRight, // [reg] [slot]
    ShiftRightLogical, // [reg] [slot]

    // --- Unary Operations ---
    BitwiseNot,
    LogicalNot,
    Negate,
    TypeOf,
    ToName,
    ToNumber,
    ToNumeric,
    ToString,

    // --- Comparisons (result in acc) ---
    TestEqual, // [reg] [slot]
    TestNotEqual, // [reg] [slot]
    TestEqualStrict, // [reg] [slot]
    TestLessThan, // [reg] [slot]
    TestGreaterThan, // [reg] [slot]
    TestLessThanSmi, // [imm8] [slot] (lhs in acc)
    TestGreaterThanSmi, // [imm8] [slot] (lhs in acc)
    TestLessThanOrEqualSmi, // [imm8] [slot] (lhs in acc)
    TestGreaterThanOrEqualSmi, // [imm8] [slot] (lhs in acc)
    TestLessThanOrEqual, // [reg] [slot]
    TestGreaterThanOrEqual, // [reg] [slot]
    TestInstanceOf, // [reg] [slot]
    TestIn, // [reg] [slot]

    // --- Control Flow ---
    Jump, // [offset16]
    JumpIfTrue, // [offset16]
    JumpIfFalse, // [offset16]
    JumpIfToBooleanTrue, // [offset16]
    JumpIfToBooleanFalse, // [offset16]
    JumpIfNull, // [offset16]
    JumpIfUndefined, // [offset16]
    JumpIfNotUndefined, // [offset16]
    JumpIfJsReceiver, // [offset16]
    SwitchOnSmi, // [table_start] [table_len] (index in acc)
    JumpLoop, // [offset16] [depth]
    PushTry, // [catch_offset16]
    PopTry,

    // --- Calls ---
    CallAny, // [func_reg] [arg_reg_start] [arg_count]
    CallProperty, // [func_reg] [obj_reg] [arg_reg_start] [arg_count]
    CallUndefinedReceiver, // [func_reg] [arg_reg_start] [arg_count]
    CallRuntime, // [runtime_id] [arg_reg_start] [arg_count]
    InvokeIntrinsic, // [intrinsic_id] [arg_reg_start] [arg_count]
    Construct, // [func_reg] [arg_reg_start] [arg_count]

    // --- Literals ---
    CreateArrayLiteral, // [idx_lo] [idx_hi]
    CreateObjectLiteral, // [idx] [flags]
    CreateObjectLiteralWide, // [idx_lo] [idx_hi] [flags]
    CreateEmptyArrayLiteral,
    CreateEmptyObjectLiteral,

    // --- Functions ---
    CreateClosure, // [idx] [flags]
    CreateClosureWide, // [idx_lo] [idx_hi] [flags]
    LdaCurrentFunction,
    CreateBlockContext, // [idx]
    CreateFunctionContext, // [idx]
    CreateFunctionContextWithCells, // [slot_count]
    CreateFunctionContextWithCellsWide, // [slot_count_lo] [slot_count_hi]
    CreateMappedArguments, // acc <- arguments object for current frame
    CreateRestParameter, // [start_index] acc <- array of arguments[start_index..]

    // --- Miscellaneous ---
    ForInEnumerate, // [obj_reg]
    ForInNext, // [enumerator_reg]
    ForInStep, // [enumerator_reg]
    SwitchOnGeneratorState, // [gen_reg] [table_start] [table_length]
    SuspendGenerator, // [gen_reg] [first_reg] [reg_count] [suspend_id]
    ResumeGenerator, // [gen_reg] [first_reg] [reg_count]
    InitPrivateField, // [obj_reg] [value_reg] [brand_id_lo] [brand_id_hi] [slot_index_lo] [slot_index_hi]
    InitPrivateAccessor, // [obj_reg] [getter_reg] [setter_reg] [brand_id_lo] [brand_id_hi] [slot_index_lo] [slot_index_hi]
    InitPrivateMethod, // [obj_reg] [method_reg] [brand_id_lo] [brand_id_hi] [slot_index_lo] [slot_index_hi]
    GetPrivateField, // [obj_reg] [brand_id_lo] [brand_id_hi] [slot_index_lo] [slot_index_hi]
    SetPrivateField, // [obj_reg] [value_reg] [brand_id_lo] [brand_id_hi] [slot_index_lo] [slot_index_hi]
    Return,
    Throw,
    Debugger,
    Wide, // Prefix: next opcode uses 16-bit operands where prefix-supported.
    ExtraWide, // Prefix: next opcode uses 32-bit operands where prefix-supported.
    LdarWide, // [reg_lo] [reg_hi] -> acc
    LdaLexicalLocalWide, // [reg_lo] [reg_hi] lexical local read with TDZ check
    StarWide, // acc -> [reg_lo] [reg_hi]
    StaLexicalLocalWide, // [reg_lo] [reg_hi] lexical local write with TDZ check
    MovWide // [src_reg_lo] [src_reg_hi] [dst_reg_lo] [dst_reg_hi]
}
