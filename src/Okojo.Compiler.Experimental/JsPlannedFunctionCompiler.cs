using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;

namespace Okojo.JavaScript.Compiler.Experimental;

internal sealed partial class JsPlannedFunctionCompiler : JsPlannedCompilerBase
{
    private readonly IReadOnlyDictionary<string, CapturedBindingAccess> inheritedCaptures;
    private Dictionary<string, int>? parameterRegisterByName;
    private List<string?>? parameterNames;
    private int externalCaptureContextDepthOffset;
    private bool initializeParametersInPrologue;

    internal JsPlannedFunctionCompiler(
        JsRealm realm,
        IReadOnlyDictionary<string, CapturedBindingAccess>? inheritedCaptures = null,
        IReadOnlyDictionary<string, PlannedPrivateBinding>? privateBindings = null
    )
        : base(realm, privateBindings)
    {
        this.inheritedCaptures =
            inheritedCaptures
            ?? new Dictionary<string, CapturedBindingAccess>(StringComparer.Ordinal);
    }

    private void EnsureParameterMaps()
    {
        parameterRegisterByName ??= new(StringComparer.Ordinal);
        parameterNames ??= [];
    }

    protected override IEnumerable<KeyValuePair<string, CapturedBindingAccess>> ExternalCaptures =>
        inheritedCaptures;

    protected override int ExternalCaptureContextDepthOffset => externalCaptureContextDepthOffset;

    protected override bool TryResolveExternalBinding(
        string name,
        out CapturedBindingAccess access,
        out int contextDepth
    )
    {
        if (inheritedCaptures.TryGetValue(name, out access))
        {
            contextDepth = access.Depth + CurrentContextDepth + ExternalCaptureContextDepthOffset;
            return true;
        }

        contextDepth = 0;
        return false;
    }

    protected override void EmitRootContextBindings()
    {
        if (initializeParametersInPrologue)
            return;
        var rootScope = activeScopes.Peek();
        for (var i = 0; i < rootScope.Bindings.Count; i++)
        {
            var binding = rootScope.Bindings[i];
            if (binding.Planned.StorageKind != CompilerPlannedStorageKind.ContextSlot)
                continue;
            if (binding.Planned.Kind != CompilerCollectedBindingKind.Parameter)
                continue;
            if (
                parameterRegisterByName is null
                || !parameterRegisterByName.TryGetValue(
                    binding.Planned.Name,
                    out var parameterRegister
                )
            )
                continue;
            EmitLdar(parameterRegister);
            EmitStaCurrentContextSlot(binding.Planned.StorageIndex);
        }
    }

    private bool HasSyntheticArgumentsBinding()
    {
        var rootScope = activeScopes.Peek();
        for (var i = 0; i < rootScope.Bindings.Count; i++)
        {
            if (rootScope.Bindings[i].Planned.Kind != CompilerCollectedBindingKind.Arguments)
                continue;
            return true;
        }
        return false;
    }

    private void EmitArgumentsObjectCreation()
    {
        builder.EmitLda(JsOpCode.CreateMappedArguments);
    }

    private void EmitArgumentsBinding(int materializedRegister)
    {
        var rootScope = activeScopes.Peek();
        for (var i = 0; i < rootScope.Bindings.Count; i++)
        {
            var binding = rootScope.Bindings[i];
            if (binding.Planned.Kind != CompilerCollectedBindingKind.Arguments)
                continue;
            EmitLdar(materializedRegister);
            EmitStore(binding, isInitialization: true);
            return;
        }
    }
}
