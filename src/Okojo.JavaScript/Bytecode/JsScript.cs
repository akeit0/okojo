using System.Runtime.CompilerServices;
using Okojo.JavaScript.Parsing;

namespace Okojo.JavaScript.Bytecode;

/// <summary>
/// A realm-local executable instance. Code is shared; atoms, linked constants,
/// feedback, and the optional breakpoint execution view are never shared across realms.
/// </summary>
public sealed class JsScript
{
    private byte[] executionBytecode;
    private OkojoPrototypeNamedPropertyIcEntry[]? prototypeNamedPropertyIcEntries;
    private string? materializedFunctionSourceText;
    private readonly int[]? globalDeclarationAtoms;
    private bool executionValidated;

    internal JsScript(JsRealm realm, JsFunctionDescriptor function)
    {
        Realm = realm;
        Function = function;
        RegisterCount = function.Code.RegisterCount;
        executionBytecode = Code.BytecodeArray;
        globalDeclarationAtoms = Code.Declarations?.LinkAtoms(realm);
        AtomizedStringConstants = InternNames(realm, Code.NameArray);
        var descriptors = Code.ConstantDescriptors;
        ObjectConstants = descriptors.Length == 0 ? [] : new object[descriptors.Length];
        for (var i = 0; i < descriptors.Length; i++)
            ObjectConstants[i] = descriptors[i] switch
            {
                JsFunctionDescriptor child => realm.GetOrCreateFunctionInstance(child),
                JsObjectLiteralLayout layout => layout.Link(realm),
                JsTemplateSiteDescriptor site => new JsTemplateSite(realm, site),
                _ => descriptors[i],
            };
        NamedPropertyIcEntries =
            Code.NamedPropertySlotCount == 0
                ? null
                : new OkojoNamedPropertyIcEntry[Code.NamedPropertySlotCount];
        GlobalBindingIcEntries =
            Code.GlobalBindingSlotCount == 0
                ? null
                : new GlobalBindingIcEntry[Code.GlobalBindingSlotCount];
        TopLevelLexicalAtoms = Code.TopLevelLexicals is { } lexicals
            ? InternNames(realm, lexicals.Names)
            : null;
    }

    public JsRealm Realm { get; }
    public JsFunctionDescriptor Function { get; }
    public JsFunctionCode Code => Function.Code;
    public ReadOnlySpan<byte> Bytecode => Code.Bytecode;
    public int RegisterCount { get; }
    public bool StrictDeclared => Code.StrictDeclared;
    public SourceCode? SourceCode => Code.SourceCode;
    public string? SourceText => SourceCode?.Source;
    public string? SourcePath => SourceCode?.Path;
    public FunctionSourceTextSegment FunctionSourceText => Code.FunctionSourceText;
    public bool HasFunctionSourceText => !Code.FunctionSourceText.IsEmpty;

    public string? GetFunctionSourceTextString()
    {
        if (!HasFunctionSourceText)
            return null;
        return materializedFunctionSourceText ??= Code.FunctionSourceText.ToString();
    }

    /// <summary>Creates a fresh function identity without duplicating linked state.</summary>
    public JsBytecodeFunction CreateClosure() => new(this);

    internal byte[] BytecodeArray => Code.BytecodeArray;
    internal byte[] ExecutionBytecode => Volatile.Read(ref executionBytecode);
    internal ulong[] NumericConstants => Code.NumericConstantArray;
    internal object[] ObjectConstants { get; }
    internal int[] AtomizedStringConstants { get; }
    internal OkojoNamedPropertyIcEntry[]? NamedPropertyIcEntries { get; }
    internal GlobalBindingIcEntry[]? GlobalBindingIcEntries { get; }
    internal JsAgent Agent => Realm.Agent;
    internal int[]? GeneratorSwitchTargets => Code.GeneratorSwitchTargets;
    internal int[]? SwitchOnSmiTargets => Code.SwitchOnSmiTargets;
    internal int[]? TopLevelLexicalAtoms { get; }
    internal int[]? TopLevelLexicalSlots => Code.TopLevelLexicals?.Slots;
    internal bool[]? TopLevelLexicalConstFlags => Code.TopLevelLexicals?.ConstFlags;
    internal bool SuppressTopLevelLexicalRegistration => Code.SuppressTopLevelLexicalRegistration;
    internal string[]? DebugNames => Code.DebugInfo?.Names;
    internal int[]? CallSiteDebugPcs => Code.DebugInfo?.CallSitePcs;
    internal int[]? CallSiteDebugNameIndices => Code.DebugInfo?.CallSiteNameIndices;
    internal int[]? RuntimeCallDebugPcs => Code.DebugInfo?.RuntimeCallPcs;
    internal int[]? RuntimeCallDebugNameIndices => Code.DebugInfo?.RuntimeCallNameIndices;
    internal int[]? TdzReadDebugPcs => Code.DebugInfo?.TdzReadPcs;
    internal int[]? TdzReadDebugNameIndices => Code.DebugInfo?.TdzReadNameIndices;
    internal int[]? DebugPcOffsets => Code.DebugInfo?.PcOffsets;
    internal int[]? DebugSourceOffsets => Code.DebugInfo?.SourceOffsets;
    internal long[]? PrivateFieldDebugKeys => Code.DebugInfo?.PrivateFieldKeys;
    internal int[]? PrivateFieldDebugNameIndices => Code.DebugInfo?.PrivateFieldNameIndices;
    internal JsLocalDebugInfo[]? LocalDebugInfos => Code.DebugInfo?.Locals;

    internal OkojoPrototypeNamedPropertyIcEntry[]? PrototypeNamedPropertyIcEntries =>
        Volatile.Read(ref prototypeNamedPropertyIcEntries);

    internal OkojoPrototypeNamedPropertyIcEntry[] GetOrCreatePrototypeNamedPropertyIcEntries()
    {
        var entries = Volatile.Read(ref prototypeNamedPropertyIcEntries);
        if (entries is not null)
            return entries;
        var created = new OkojoPrototypeNamedPropertyIcEntry[Code.NamedPropertySlotCount];
        return Interlocked.CompareExchange(ref prototypeNamedPropertyIcEntries, created, null)
            ?? created;
    }

    internal byte[] GetOrCreateDebugBytecode()
    {
        var current = ExecutionBytecode;
        if (!ReferenceEquals(current, Code.BytecodeArray))
            return current;
        var copy = (byte[])current.Clone();
        var previous = Interlocked.CompareExchange(ref executionBytecode, copy, current);
        if (!ReferenceEquals(previous, current))
            return previous;
        // Rebase active VM cursors on their existing slow-check path, including
        // when the first breakpoint was installed inside a host callback.
        Agent.RequestExecutionCodeReload();
        return copy;
    }

    internal void ArmBreakpoints() => Agent.ArmBreakpoints(this);

    internal JsScript PrepareForExecution(JsRealm realm)
    {
        var instance = ReferenceEquals(Realm, realm) ? this : realm.LinkFunction(Function);
        instance.ValidateDeclarationsForExecution();
        instance.ArmBreakpoints();
        return instance;
    }

    internal void ValidateDeclarations() =>
        Code.Declarations?.Validate(Realm, globalDeclarationAtoms!);

    // The first execution validates because globals can change between link and
    // execution. Re-execution skips validation: the instance's own bindings from
    // its previous run must not read as conflicts (pre-split re-execution behavior).
    internal void ValidateDeclarationsForExecution()
    {
        if (executionValidated)
            return;
        ValidateDeclarations();
        executionValidated = true;
    }

    private static int[] InternNames(JsRealm realm, string[] names)
    {
        if (names.Length == 0)
            return [];
        var atoms = new int[names.Length];
        for (var i = 0; i < atoms.Length; i++)
            atoms[i] = realm.Atoms.InternNoCheck(names[i]);
        return atoms;
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

    /// <summary>
    ///     Gets the source-level callee expression associated with a call or construction
    ///     instruction. This diagnostic metadata is absent for compiler-generated calls.
    /// </summary>
    public bool TryGetCallSiteDebugNameAtPc(int opcodePc, out string name)
    {
        return JsScriptDebugInfo.TryGetDebugName(
            this,
            opcodePc,
            CallSiteDebugPcs,
            CallSiteDebugNameIndices,
            out name
        );
    }
}
