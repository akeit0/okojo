namespace Okojo.Runtime;

/// <summary>
///     Realm-level configuration.
///     Use this for per-realm host data and initialization.
/// </summary>
public sealed class JsRealmOptions
{
    public object? HostDefined { get; set; }
    public Action<JsRealm>? Initialize { get; set; }

    internal JsRealmOptions Clone()
    {
        return new()
        {
            HostDefined = HostDefined,
            Initialize = Initialize
        };
    }
}
