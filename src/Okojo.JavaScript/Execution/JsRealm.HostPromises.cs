namespace Okojo.JavaScript.Execution;

public sealed partial class JsRealm
{
    /// <summary>
    /// Creates an intrinsic pending Promise for a host API, independent of global Promise,
    /// Promise.prototype.then and species. The capability is an owner-sequence host handle;
    /// only its Promise value is exposed to script.
    /// </summary>
    public JsPromiseCapability CreatePromiseCapability() => new(this);
}
