using System.Diagnostics;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodeAssertBuiltIn(NodeRuntime runtime)
{
    private const int ModuleStrictEqualSlot = 0;
    private const int ModuleNotStrictEqualSlot = 1;
    private int atomNotStrictEqual = -1;

    private int atomStrictEqual = -1;
    private JsPlainObject? moduleObject;
    private StaticNamedPropertyLayout? moduleShape;

    public JsPlainObject GetModule()
    {
        if (moduleObject is not null)
            return moduleObject;

        var realm = runtime.MainRealm;
        var shape = moduleShape ??= CreateModuleShape(realm);
        var module = new JsPlainObject(shape);
        module.SetNamedSlotUnchecked(ModuleStrictEqualSlot, JsValue.FromObject(CreateStrictEqualFunction(realm)));
        module.SetNamedSlotUnchecked(ModuleNotStrictEqualSlot, JsValue.FromObject(CreateNotStrictEqualFunction(realm)));
        moduleObject = module;
        return module;
    }

    private StaticNamedPropertyLayout CreateModuleShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomStrictEqual, JsShapePropertyFlags.Open,
            out var strictEqualInfo);
        shape = shape.GetOrAddTransition(atomNotStrictEqual, JsShapePropertyFlags.Open, out var notStrictEqualInfo);
        Debug.Assert(strictEqualInfo.Slot == ModuleStrictEqualSlot);
        Debug.Assert(notStrictEqualInfo.Slot == ModuleNotStrictEqualSlot);
        return shape;
    }

    private void EnsureAtoms(JsRealm realm)
    {
        atomStrictEqual = EnsureAtom(realm, atomStrictEqual, "strictEqual");
        atomNotStrictEqual = EnsureAtom(realm, atomNotStrictEqual, "notStrictEqual");
    }

    private static int EnsureAtom(JsRealm realm, int atom, string text)
    {
        return atom >= 0 ? atom : realm.Atoms.InternNoCheck(text);
    }

    private static JsHostFunction CreateStrictEqualFunction(JsRealm realm)
    {
        return new(realm, "strictEqual", 2, static (in info) =>
        {
            var actual = info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined;
            var expected = info.Arguments.Length > 1 ? info.Arguments[1] : JsValue.Undefined;
            if (!SameValue(actual, expected))
                throw new JsRuntimeException(JsErrorKind.TypeError, GetFailureMessage(info, actual, expected, true));
            return JsValue.Undefined;
        }, false);
    }

    private static JsHostFunction CreateNotStrictEqualFunction(JsRealm realm)
    {
        return new(realm, "notStrictEqual", 2, static (in info) =>
        {
            var actual = info.Arguments.Length > 0 ? info.Arguments[0] : JsValue.Undefined;
            var expected = info.Arguments.Length > 1 ? info.Arguments[1] : JsValue.Undefined;
            if (SameValue(actual, expected))
                throw new JsRuntimeException(JsErrorKind.TypeError, GetFailureMessage(info, actual, expected, false));
            return JsValue.Undefined;
        }, false);
    }

    private static bool SameValue(in JsValue actual, in JsValue expected)
    {
        return JsValue.SameValue(actual, expected);
    }

    private static string GetFailureMessage(in CallInfo info, in JsValue actual, in JsValue expected, bool equal)
    {
        if (info.Arguments.Length > 2 && info.Arguments[2].IsString)
            return info.Arguments[2].AsString();

        var actualText = info.Realm.ToJsStringSlowPath(actual);
        var expectedText = info.Realm.ToJsStringSlowPath(expected);
        return equal
            ? $"Expected strict equality: actual={actualText}, expected={expectedText}"
            : $"Expected values to differ: actual={actualText}, expected={expectedText}";
    }
}
