namespace Okojo.Runtime;

internal sealed class DelegateRealmApiModule(Action<JsRealm> setup) : IRealmApiModule
{
    private readonly Action<JsRealm> setup = setup ?? throw new ArgumentNullException(nameof(setup));

    public void Install(JsRealm realm)
    {
        setup(realm);
    }
}
