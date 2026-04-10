using System.Diagnostics;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeUrlBuiltIn(NodeRuntime runtime)
{
    private const int ModuleFileUrlToPathSlot = 0;

    private int atomFileUrlToPath = -1;
    private JsPlainObject? moduleObject;
    private StaticNamedPropertyLayout? moduleShape;

    public JsPlainObject GetModule()
    {
        if (moduleObject is not null)
            return moduleObject;

        var realm = runtime.MainRealm;
        var shape = moduleShape ??= CreateModuleShape(realm);
        var module = new JsPlainObject(shape);
        module.SetNamedSlotUnchecked(ModuleFileUrlToPathSlot, JsValue.FromObject(CreateFileUrlToPathFunction(realm)));
        moduleObject = module;
        return module;
    }

    private StaticNamedPropertyLayout CreateModuleShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomFileUrlToPath, JsShapePropertyFlags.Open,
            out var fileUrlToPathInfo);
        Debug.Assert(fileUrlToPathInfo.Slot == ModuleFileUrlToPathSlot);
        return shape;
    }

    private void EnsureAtoms(JsRealm realm)
    {
        atomFileUrlToPath = EnsureAtom(realm, atomFileUrlToPath, "fileURLToPath");
    }

    private static int EnsureAtom(JsRealm realm, int atom, string text)
    {
        return atom >= 0 ? atom : realm.Atoms.InternNoCheck(text);
    }

    private static JsHostFunction CreateFileUrlToPathFunction(JsRealm realm)
    {
        return new(realm, "fileURLToPath", 1, static (in info) =>
        {
            var value = info.GetArgumentString(0);
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsFile)
                throw new JsRuntimeException(JsErrorKind.TypeError, "fileURLToPath requires a file:// URL");

            return JsValue.FromString(uri.LocalPath);
        }, false);
    }
}
