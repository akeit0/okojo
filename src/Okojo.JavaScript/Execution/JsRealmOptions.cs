namespace Okojo.JavaScript.Execution;

/// <summary>
///     Realm-level configuration.
///     Use this for per-realm host data and initialization.
/// </summary>
public sealed class JsRealmOptions
{
    /// <summary>Optional same-agent global-this object; binding storage remains realm-local.</summary>
    public JsObject? GlobalThisObject { get; set; }

    public object? HostDefined { get; set; }
    public Action<JsRealm>? Initialize { get; set; }

    /// <summary>Overrides module resolution/loading for this realm's independent module map.</summary>
    public IModuleSourceLoader? ModuleSourceLoader { get; set; }

    internal JsRealmOptions Clone()
    {
        return new()
        {
            GlobalThisObject = GlobalThisObject,
            HostDefined = HostDefined,
            Initialize = Initialize,
            ModuleSourceLoader = ModuleSourceLoader,
        };
    }
}
