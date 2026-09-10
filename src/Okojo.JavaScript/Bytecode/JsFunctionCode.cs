using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Bytecode;

/// <summary>
/// Immutable, realm-independent bytecode. All arrays are transferred from the
/// emitter and are exposed publicly only as read-only spans. No JS objects,
/// atom handles, shapes, feedback, or debugger patches belong here.
/// </summary>
public sealed class JsFunctionCode
{
    internal JsFunctionCode(
        byte[] bytecode,
        ulong[] numericConstants,
        object[] constantDescriptors,
        int registerCount,
        string[] names,
        bool strictDeclared = false,
        int namedPropertySlotCount = 0,
        int globalBindingSlotCount = 0,
        JsFunctionDebugInfo? debugInfo = null,
        SourceCode? sourceCode = null,
        FunctionSourceTextSegment functionSourceText = default,
        int[]? generatorSwitchTargets = null,
        int[]? switchOnSmiTargets = null,
        JsGlobalDeclarationPlan? declarations = null,
        JsTopLevelLexicalPlan? topLevelLexicals = null,
        bool suppressTopLevelLexicalRegistration = false
    )
    {
        ArgumentNullException.ThrowIfNull(bytecode);
        ArgumentNullException.ThrowIfNull(numericConstants);
        ArgumentNullException.ThrowIfNull(constantDescriptors);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentOutOfRangeException.ThrowIfNegative(registerCount);
        ArgumentOutOfRangeException.ThrowIfNegative(namedPropertySlotCount);
        ArgumentOutOfRangeException.ThrowIfNegative(globalBindingSlotCount);

        // A closed portable constant vocabulary is essential: otherwise a host
        // can accidentally smuggle a realm through a nominally immutable unit.
        foreach (var value in constantDescriptors)
            if (
                value
                is not (
                    string
                    or JsBigInt
                    or int
                    or int[]
                    or JsFunctionDescriptor
                    or JsObjectLiteralLayout
                    or JsTemplateSiteDescriptor
                )
            )
                throw new ArgumentException(
                    $"Non-portable bytecode constant: {value?.GetType().FullName ?? "null"}.",
                    nameof(constantDescriptors)
                );

        BytecodeArray = bytecode;
        NumericConstantArray = numericConstants;
        ConstantDescriptors = constantDescriptors;
        RegisterCount = registerCount;
        NameArray = names;
        StrictDeclared = strictDeclared;
        NamedPropertySlotCount = namedPropertySlotCount;
        GlobalBindingSlotCount = globalBindingSlotCount;
        DebugInfo = debugInfo;
        SourceCode = sourceCode;
        FunctionSourceText = functionSourceText;
        GeneratorSwitchTargets = generatorSwitchTargets;
        SwitchOnSmiTargets = switchOnSmiTargets;
        Declarations = declarations;
        TopLevelLexicals = topLevelLexicals;
        SuppressTopLevelLexicalRegistration = suppressTopLevelLexicalRegistration;
    }

    public ReadOnlySpan<byte> Bytecode => BytecodeArray;
    public ReadOnlySpan<ulong> NumericConstants => NumericConstantArray;
    public ReadOnlySpan<string> SymbolicNames => NameArray;
    public int ConstantCount => ConstantDescriptors.Length;
    public int RegisterCount { get; }
    public bool StrictDeclared { get; }
    public int NamedPropertySlotCount { get; }
    public int GlobalBindingSlotCount { get; }
    public SourceCode? SourceCode { get; }
    public FunctionSourceTextSegment FunctionSourceText { get; }
    public bool HasDebugInfo => DebugInfo is not null;

    internal byte[] BytecodeArray { get; }
    internal ulong[] NumericConstantArray { get; }
    internal object[] ConstantDescriptors { get; }
    internal string[] NameArray { get; }
    internal JsFunctionDebugInfo? DebugInfo { get; }
    internal int[]? GeneratorSwitchTargets { get; }
    internal int[]? SwitchOnSmiTargets { get; }
    internal JsGlobalDeclarationPlan? Declarations { get; }
    internal JsTopLevelLexicalPlan? TopLevelLexicals { get; }
    internal bool SuppressTopLevelLexicalRegistration { get; }
}

/// <summary>Cold, optional diagnostic tables; source retention is independent.</summary>
internal sealed class JsFunctionDebugInfo
{
    internal string[]? Names { get; init; }
    internal int[]? CallSitePcs { get; init; }
    internal int[]? CallSiteNameIndices { get; init; }
    internal int[]? RuntimeCallPcs { get; init; }
    internal int[]? RuntimeCallNameIndices { get; init; }
    internal int[]? TdzReadPcs { get; init; }
    internal int[]? TdzReadNameIndices { get; init; }
    internal int[]? PcOffsets { get; init; }
    internal int[]? SourceOffsets { get; init; }
    internal long[]? PrivateFieldKeys { get; init; }
    internal int[]? PrivateFieldNameIndices { get; init; }
    internal JsLocalDebugInfo[]? Locals { get; init; }
}

internal sealed class JsTopLevelLexicalPlan(string[] names, int[] slots, bool[] constFlags)
{
    internal readonly string[] Names = names;
    internal readonly int[] Slots = slots;
    internal readonly bool[] ConstFlags = constFlags;
}
