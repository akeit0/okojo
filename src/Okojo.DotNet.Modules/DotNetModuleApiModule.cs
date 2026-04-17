using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.DotNet.Modules;

internal sealed class DotNetModuleApiModule(DotNetModuleImportBridge bridge) : IRealmApiModule
{
    public void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);

        if (realm.Global.TryGetValue(DotNetModuleImportBridge.GlobalBridgeFunctionName, out _))
            return;

        realm.Global[DotNetModuleImportBridge.GlobalBridgeFunctionName] = JsValue.FromObject(
            new JsHostFunction(realm, DotNetModuleImportBridge.GlobalBridgeFunctionName, 1, static (in info) =>
            {
                var importBridge = (DotNetModuleImportBridge)((JsHostFunction)info.Function).UserData!;
                var resolvedId = info.GetArgumentOrDefault(0, JsValue.Undefined).AsString();
                return importBridge.Import(info.Realm, resolvedId);
            })
            {
                UserData = bridge
            });
    }
}
