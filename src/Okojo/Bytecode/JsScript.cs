using Okojo.Parsing;

namespace Okojo.Bytecode;

public sealed record JsScript
{
    internal JsScript(
        byte[] Bytecode,
        double[] NumericConstants,
        object[] ObjectConstants,
        int RegisterCount,
        int[] AtomizedStringConstants,
        bool StrictDeclared = false,
        string[]? DebugNames = null,
        int[]? RuntimeCallDebugPcs = null,
        int[]? RuntimeCallDebugNameIndices = null,
        int[]? TdzReadDebugPcs = null,
        int[]? TdzReadDebugNameIndices = null,
        OkojoNamedPropertyIcEntry[]? NamedPropertyIcEntries = null,
        GlobalBindingIcEntry[]? GlobalBindingIcEntries = null,
        int[]? DebugPcOffsets = null,
        int[]? DebugSourceOffsets = null,
        string? SourceText = null,
        string? SourcePath = null,
        FunctionSourceTextSegment FunctionSourceText = default,
        int[]? GeneratorSwitchTargets = null,
        int[]? SwitchOnSmiTargets = null,
        int[]? TopLevelLexicalAtoms = null,
        int[]? TopLevelLexicalSlots = null,
        bool[]? TopLevelLexicalConstFlags = null,
        long[]? PrivateFieldDebugKeys = null,
        int[]? PrivateFieldDebugNameIndices = null,
        JsLocalDebugInfo[]? LocalDebugInfos = null)
    {
        this.Bytecode = Bytecode;
        this.NumericConstants = NumericConstants;
        this.ObjectConstants = ObjectConstants;
        this.RegisterCount = RegisterCount;
        this.AtomizedStringConstants = AtomizedStringConstants;
        this.StrictDeclared = StrictDeclared;
        this.DebugNames = DebugNames;
        this.RuntimeCallDebugPcs = RuntimeCallDebugPcs;
        this.RuntimeCallDebugNameIndices = RuntimeCallDebugNameIndices;
        this.TdzReadDebugPcs = TdzReadDebugPcs;
        this.TdzReadDebugNameIndices = TdzReadDebugNameIndices;
        this.NamedPropertyIcEntries = NamedPropertyIcEntries;
        this.GlobalBindingIcEntries = GlobalBindingIcEntries;
        this.DebugPcOffsets = DebugPcOffsets;
        this.DebugSourceOffsets = DebugSourceOffsets;
        SourceCode = SourceText is null && SourcePath is null ? null : new SourceCode(SourceText, SourcePath);
        functionSourceText = FunctionSourceText;
        this.GeneratorSwitchTargets = GeneratorSwitchTargets;
        this.SwitchOnSmiTargets = SwitchOnSmiTargets;
        this.TopLevelLexicalAtoms = TopLevelLexicalAtoms;
        this.TopLevelLexicalSlots = TopLevelLexicalSlots;
        this.TopLevelLexicalConstFlags = TopLevelLexicalConstFlags;
        this.PrivateFieldDebugKeys = PrivateFieldDebugKeys;
        this.PrivateFieldDebugNameIndices = PrivateFieldDebugNameIndices;
        this.LocalDebugInfos = LocalDebugInfos;
    }

    public byte[] Bytecode { get; init; }
    public double[] NumericConstants { get; init; }
    public object[] ObjectConstants { get; init; }
    public int RegisterCount { get; init; }
    public int[] AtomizedStringConstants { get; init; }
    public bool StrictDeclared { get; init; }
    internal string[]? DebugNames { get; init; }
    internal int[]? RuntimeCallDebugPcs { get; init; }
    internal int[]? RuntimeCallDebugNameIndices { get; init; }
    internal int[]? TdzReadDebugPcs { get; init; }
    internal int[]? TdzReadDebugNameIndices { get; init; }
    internal OkojoNamedPropertyIcEntry[]? NamedPropertyIcEntries { get; init; }
    internal GlobalBindingIcEntry[]? GlobalBindingIcEntries { get; init; }
    public int[]? DebugPcOffsets { get; init; }
    public int[]? DebugSourceOffsets { get; init; }

    public SourceCode? SourceCode { get; init; }

    public string? SourceText
    {
        get => SourceCode?.Source;
        init => SourceCode = value is null && SourceCode?.Path is null ? null : new SourceCode(value, SourceCode?.Path);
    }

    public string? SourcePath
    {
        get => SourceCode?.Path;
        init => SourceCode = value is null && SourceCode?.Source is null
            ? null
            : new SourceCode(SourceCode?.Source, value);
    }

    private FunctionSourceTextSegment functionSourceText;

    public FunctionSourceTextSegment FunctionSourceText
    {
        get => functionSourceText;
        init => functionSourceText = value;
    }

    public bool HasFunctionSourceText => !functionSourceText.IsEmpty;

    public string? GetFunctionSourceTextString()
    {
        if (functionSourceText.IsEmpty)
            return null;

        return functionSourceText.ToString();
    }

    public int[]? GeneratorSwitchTargets { get; init; }
    public int[]? SwitchOnSmiTargets { get; init; }
    public int[]? TopLevelLexicalAtoms { get; init; }
    public int[]? TopLevelLexicalSlots { get; init; }
    public bool[]? TopLevelLexicalConstFlags { get; init; }
    public long[]? PrivateFieldDebugKeys { get; init; }
    public int[]? PrivateFieldDebugNameIndices { get; init; }
    public JsLocalDebugInfo[]? LocalDebugInfos { get; init; }

    internal JsAgent? Agent { get; set; }

    internal void BindAgent(JsAgent agent)
    {
        if (ReferenceEquals(Agent, agent))
            return;

        Agent = agent;
        agent.RegisterScript(this);
    }

    internal void ArmBreakpoints()
    {
        Agent?.ArmBreakpoints(this);
    }

    public bool TryGetSourceLocationAtPc(int opcodePc, out int line, out int column)
    {
        return JsScriptDebugInfo.TryGetSourceLocation(this, opcodePc, out line, out column);
    }

    internal bool TryGetExactSourceLocationAtPc(int opcodePc, out int line, out int column)
    {
        return JsScriptDebugInfo.TryGetExactSourceLocation(this, opcodePc, out line, out column);
    }

    public IReadOnlyList<JsLocalDebugInfo>? GetVisibleLocalDebugInfosAtPc(int opcodePc)
    {
        return JsScriptDebugInfo.GetVisibleLocalInfos(this, opcodePc);
    }
}
