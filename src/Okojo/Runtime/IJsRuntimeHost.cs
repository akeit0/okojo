using Okojo.Runtime.Interop;
using Okojo.SourceMaps;

namespace Okojo.Runtime;

/// <summary>
///     The host container surface the ECMAScript engine depends on. Implemented by the
///     embedding runtime container (<see cref="JsRuntime"/>) so the engine does not
///     reference the concrete container type.
/// </summary>
public interface IJsRuntimeHost
{
    JsRuntimeOptions Options { get; }
    TimeProvider TimeProvider { get; }
    IModuleSourceLoader ModuleSourceLoader { get; }
    IWorkerScriptSourceLoader WorkerScriptSourceLoader { get; }
    SourceMapRegistry? SourceMapRegistry { get; }
    bool IsClrAccessEnabled { get; }
    JsAgent CreateWorkerAgent(Action<JsAgentOptions>? configure = null);
}

/// <summary>
///     Engine-internal host seam exposing members that are internal to the runtime assembly.
///     Implemented by <see cref="JsRuntime"/>; referenced by the realm/agent when the engine
///     needs container services that are not part of the stable public surface.
/// </summary>
internal interface IJsRuntimeHostInternal : IJsRuntimeHost
{
    IClrAccessProvider? ClrAccessProvider { get; }
    string LoadWorkerScript(string path, string? referrer = null);
}
