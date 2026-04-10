using System.Diagnostics;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Node;

internal sealed class NodePerformanceBuiltIn(NodeRuntime runtime)
{
    private const int PerformanceNowSlot = 0;
    private const int PerformanceTimeOriginSlot = 1;

    private const int ModulePerformanceSlot = 0;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly double timeOrigin = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private int atomNow = -1;
    private int atomPerformance = -1;
    private int atomTimeOrigin = -1;
    private JsPlainObject? moduleObject;
    private StaticNamedPropertyLayout? moduleShape;

    private JsPlainObject? performanceObject;
    private StaticNamedPropertyLayout? performanceShape;

    public JsPlainObject GetPerformanceObject()
    {
        if (performanceObject is not null)
            return performanceObject;

        var realm = runtime.MainRealm;
        var shape = performanceShape ??= CreatePerformanceShape(realm);
        var performance = new JsPlainObject(shape);
        performance.SetNamedSlotUnchecked(PerformanceNowSlot, JsValue.FromObject(CreateNowFunction(realm)));
        performance.SetNamedSlotUnchecked(PerformanceTimeOriginSlot, new(timeOrigin));
        performanceObject = performance;
        return performance;
    }

    public JsPlainObject GetModule()
    {
        if (moduleObject is not null)
            return moduleObject;

        var realm = runtime.MainRealm;
        var shape = moduleShape ??= CreateModuleShape(realm);
        var module = new JsPlainObject(shape);
        module.SetNamedSlotUnchecked(ModulePerformanceSlot, JsValue.FromObject(GetPerformanceObject()));
        moduleObject = module;
        return module;
    }

    private StaticNamedPropertyLayout CreatePerformanceShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomNow, JsShapePropertyFlags.Open, out var nowInfo);
        shape = shape.GetOrAddTransition(atomTimeOrigin, JsShapePropertyFlags.Open, out var timeOriginInfo);
        Debug.Assert(nowInfo.Slot == PerformanceNowSlot);
        Debug.Assert(timeOriginInfo.Slot == PerformanceTimeOriginSlot);
        return shape;
    }

    private StaticNamedPropertyLayout CreateModuleShape(JsRealm realm)
    {
        EnsureAtoms(realm);
        var shape = realm.EmptyShape.GetOrAddTransition(atomPerformance, JsShapePropertyFlags.Open,
            out var performanceInfo);
        Debug.Assert(performanceInfo.Slot == ModulePerformanceSlot);
        return shape;
    }

    private void EnsureAtoms(JsRealm realm)
    {
        atomNow = EnsureAtom(realm, atomNow, "now");
        atomTimeOrigin = EnsureAtom(realm, atomTimeOrigin, "timeOrigin");
        atomPerformance = EnsureAtom(realm, atomPerformance, "performance");
    }

    private static int EnsureAtom(JsRealm realm, int atom, string text)
    {
        return atom >= 0 ? atom : realm.Atoms.InternNoCheck(text);
    }

    private JsHostFunction CreateNowFunction(JsRealm realm)
    {
        return new(realm, "now", 0, (scoped in _) => { return new(stopwatch.Elapsed.TotalMilliseconds); }, false);
    }
}
