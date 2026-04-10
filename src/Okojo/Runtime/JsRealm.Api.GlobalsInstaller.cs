namespace Okojo.Runtime;

public sealed partial class JsRealm
{
    public void InstallGlobals(Action<JsGlobalInstaller> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new(this));
    }
}
