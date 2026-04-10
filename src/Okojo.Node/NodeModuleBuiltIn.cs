using System.Diagnostics;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeModuleBuiltIn(NodeRuntime runtime)
{
    private const int ModuleCreateRequireSlot = 0;

    private int atomCreateRequire = -1;
    private JsPlainObject? moduleObject;
    private StaticNamedPropertyLayout? moduleShape;

    public JsPlainObject GetModule()
    {
        if (moduleObject is not null)
            return moduleObject;

        var realm = runtime.MainRealm;
        var shape = moduleShape ??= CreateModuleShape(realm);
        var module = new JsPlainObject(shape);
        module.SetNamedSlotUnchecked(ModuleCreateRequireSlot, JsValue.FromObject(CreateCreateRequireFunction(realm)));
        moduleObject = module;
        return module;
    }

    private StaticNamedPropertyLayout CreateModuleShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomCreateRequire, JsShapePropertyFlags.Open,
            out var createRequireInfo);
        Debug.Assert(createRequireInfo.Slot == ModuleCreateRequireSlot);
        return shape;
    }

    private void EnsureAtoms(JsRealm realm)
    {
        atomCreateRequire = EnsureAtom(realm, atomCreateRequire, "createRequire");
    }

    private static int EnsureAtom(JsRealm realm, int atom, string text)
    {
        return atom >= 0 ? atom : realm.Atoms.InternNoCheck(text);
    }

    private JsHostFunction CreateCreateRequireFunction(JsRealm realm)
    {
        return new(realm, "createRequire", 1, (in info) =>
        {
            var referrer = info.GetArgumentString(0);
            if (referrer.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                referrer = new Uri(referrer).LocalPath;

            return JsValue.FromObject(CreateBoundRequireFunction(info.Realm, referrer));
        }, false);
    }

    private JsHostFunction CreateBoundRequireFunction(JsRealm realm, string referrer)
    {
        return new(realm, "require", 1, (in info) =>
        {
            var specifier = info.GetArgumentString(0);
            return runtime.Require(specifier, referrer);
        }, false);
    }
}
