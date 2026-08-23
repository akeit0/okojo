using Okojo.JavaScript.Bytecode;
using Okojo.JavaScript.Execution;

namespace Okojo.JavaScript.Compiler.Experimental;

internal sealed partial class JsPlannedFunctionCompiler : JsPlannedCompilerBase
{
    private readonly IReadOnlyDictionary<string, CapturedBindingAccess> inheritedCaptures;
    private readonly Dictionary<string, int> parameterRegisterByName;
    private readonly List<string?> parameterNames;
    private int externalCaptureContextDepthOffset;
    private bool initializeParametersInPrologue;

    public JsPlannedFunctionCompiler(
        JsRealm realm,
        IReadOnlyDictionary<string, CapturedBindingAccess>? inheritedCaptures = null
    )
        : base(realm)
    {
        this.inheritedCaptures =
            inheritedCaptures
            ?? new Dictionary<string, CapturedBindingAccess>(StringComparer.Ordinal);
        parameterRegisterByName = new(StringComparer.Ordinal);
        parameterNames = [];
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
                !parameterRegisterByName.TryGetValue(
                    binding.Planned.Name,
                    out var parameterRegister
                )
            )
                continue;
            EmitLdar(parameterRegister);
            EmitStaCurrentContextSlot(binding.Planned.StorageIndex);
        }
    }

    private void EmitArgumentsBinding()
    {
        var rootScope = activeScopes.Peek();
        for (var i = 0; i < rootScope.Bindings.Count; i++)
        {
            var binding = rootScope.Bindings[i];
            if (binding.Planned.Kind != CompilerCollectedBindingKind.Arguments)
                continue;
            builder.EmitLda(JsOpCode.CreateMappedArguments);
            EmitStore(binding, isInitialization: true);
            return;
        }
    }
}
