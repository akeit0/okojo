using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoLinearLookupBenchmarks : OkojoNamedPropertyBenchmarkBase
{
    private NamedPropertyLayout layout = null!;
    private JsPlainObject @object = null!;
    private int targetAtom;

    [GlobalSetup]
    public void Setup()
    {
        InitializeRealm();
        BuildPropertyAtoms(8);
        targetAtom = PropertyAtoms[^1];
        layout = BuildStaticLayout(8);
        @object = BuildObject(8, false);
    }

    [Benchmark(Baseline = true)]
    public bool Layout_TryGetSlotInfo()
    {
        return layout.TryGetSlotInfo(targetAtom, out _);
    }

    [Benchmark]
    public bool Object_TryGetPropertyAtom()
    {
        return @object.TryGetPropertyAtom(Realm, targetAtom, out _, out _);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoMapLookupBenchmarks : OkojoNamedPropertyBenchmarkBase
{
    private NamedPropertyLayout layout = null!;
    private JsPlainObject @object = null!;
    private int targetAtom;

    [Params("Static", "Dynamic")] public string LayoutMode { get; set; } = "Static";

    [GlobalSetup]
    public void Setup()
    {
        InitializeRealm();
        BuildPropertyAtoms(32);
        targetAtom = PropertyAtoms[^1];
        layout = LayoutMode == "Dynamic"
            ? BuildDynamicLayout(32)
            : BuildStaticLayout(32);
        @object = BuildObject(32, LayoutMode == "Dynamic");
    }

    [Benchmark(Baseline = true)]
    public bool Layout_TryGetSlotInfo()
    {
        return layout.TryGetSlotInfo(targetAtom, out _);
    }

    [Benchmark]
    public bool Object_TryGetPropertyAtom()
    {
        return @object.TryGetPropertyAtom(Realm, targetAtom, out _, out _);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoObjectSetExistingLinearBenchmarks : OkojoNamedPropertyBenchmarkBase
{
    private JsPlainObject @object = null!;
    private JsValue setValue;
    private int targetAtom;

    [Params("Static", "Dynamic")] public string LayoutMode { get; set; } = "Static";

    [GlobalSetup]
    public void Setup()
    {
        InitializeRealm();
        BuildPropertyAtoms(8);
        targetAtom = PropertyAtoms[^1];
        setValue = JsValue.FromInt32(123);
        @object = BuildObject(8, LayoutMode == "Dynamic");
    }

    [Benchmark]
    public bool Object_TrySetExistingAtom()
    {
        return @object.TrySetPropertyAtom(Realm, targetAtom, setValue, out _);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoObjectSetExistingMapBenchmarks : OkojoNamedPropertyBenchmarkBase
{
    private JsPlainObject @object = null!;
    private JsValue setValue;
    private int targetAtom;

    [Params("Static", "Dynamic")] public string LayoutMode { get; set; } = "Static";

    [GlobalSetup]
    public void Setup()
    {
        InitializeRealm();
        BuildPropertyAtoms(32);
        targetAtom = PropertyAtoms[^1];
        setValue = JsValue.FromInt32(123);
        @object = BuildObject(32, LayoutMode == "Dynamic");
    }

    [Benchmark]
    public bool Object_TrySetExistingAtom()
    {
        return @object.TrySetPropertyAtom(Realm, targetAtom, setValue, out _);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoObjectCreateAndAppendLinearBenchmarks : OkojoNamedPropertyBenchmarkBase
{
    private int[] appendAtoms = null!;
    private int appendCounter;
    private JsValue setValue;

    [Params("Static", "Dynamic")] public string LayoutMode { get; set; } = "Static";

    [GlobalSetup]
    public void Setup()
    {
        InitializeRealm();
        BuildPropertyAtoms(8);
        setValue = JsValue.FromInt32(123);
        appendAtoms = BuildAppendAtoms(256);
        appendCounter = 0;
    }

    [Benchmark]
    public bool Object_CreateAndAppendPreinternedAtom()
    {
        var obj = BuildObject(8, LayoutMode == "Dynamic");
        var atom = appendAtoms[appendCounter++ & (appendAtoms.Length - 1)];
        return obj.TrySetPropertyAtom(Realm, atom, setValue, out _);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.Declared)]
public class OkojoMapAppendBaselineBenchmarks : OkojoNamedPropertyBenchmarkBase
{
    private int[] appendAtoms = null!;
    private int appendCounter;
    private int propertyCount;
    private JsValue setValue;

    [Params("NoResize", "ControlGrow", "EntryGrow")]
    public string GrowthCase { get; set; } = "NoResize";

    [GlobalSetup]
    public void Setup()
    {
        InitializeRealm();
        propertyCount = GrowthCase switch
        {
            "NoResize" => 20,
            "ControlGrow" => 24,
            "EntryGrow" => 32,
            _ => throw new InvalidOperationException($"Unknown GrowthCase: {GrowthCase}")
        };

        BuildPropertyAtoms(propertyCount);
        setValue = JsValue.FromInt32(123);
        appendAtoms = BuildAppendAtoms(256);
        appendCounter = 0;
    }

    [Benchmark(Baseline = true)]
    public Dictionary<int, PropertyDescriptor> Dictionary_CreateAndAppendOpenDataDescriptor()
    {
        var map = BuildPropertyDescriptorDictionary(propertyCount);
        var atom = appendAtoms[appendCounter++ & (appendAtoms.Length - 1)];
        map.Add(atom, PropertyDescriptor.OpenData(setValue));
        return map;
    }

    [Benchmark]
    public JsPlainObject DynamicObject_CreateAndAppendPreinternedAtom()
    {
        var obj = BuildObject(propertyCount, true);
        var atom = appendAtoms[appendCounter++ & (appendAtoms.Length - 1)];
        _ = obj.TrySetPropertyAtom(Realm, atom, setValue, out _);
        return obj;
    }
}

public abstract class OkojoNamedPropertyBenchmarkBase
{
    protected JsRealm Realm { get; private set; } = null!;
    protected int[] PropertyAtoms { get; private set; } = Array.Empty<int>();

    protected void InitializeRealm()
    {
        Realm = JsRuntime.CreateBuilder().Build().DefaultRealm;
    }

    protected void BuildPropertyAtoms(int propertyCount)
    {
        var atoms = new int[propertyCount];
        for (var i = 0; i < propertyCount; i++)
            atoms[i] = Realm.Atoms.InternNoCheck("p" + i);
        PropertyAtoms = atoms;
    }

    protected int[] BuildAppendAtoms(int count)
    {
        var atoms = new int[count];
        for (var i = 0; i < count; i++)
            atoms[i] = Realm.Atoms.InternNoCheck("append" + i);
        return atoms;
    }

    protected NamedPropertyLayout BuildStaticLayout(int propertyCount)
    {
        var layout = Realm.EmptyShape;
        for (var i = 0; i < propertyCount; i++)
            layout = layout.GetOrAddTransition(PropertyAtoms[i], out _);
        return layout;
    }

    protected NamedPropertyLayout BuildDynamicLayout(int propertyCount)
    {
        var layout = new DynamicNamedPropertyLayout(Realm);
        for (var i = 0; i < propertyCount; i++)
            layout.SetSlotInfo(PropertyAtoms[i], new(i, JsShapePropertyFlags.Open));
        return layout;
    }

    protected JsPlainObject BuildObject(int propertyCount, bool useDictionaryMode)
    {
        var obj = new JsPlainObject(Realm, useDictionaryMode: useDictionaryMode);
        for (var i = 0; i < propertyCount; i++)
            obj.DefineDataPropertyAtom(Realm, PropertyAtoms[i], JsValue.FromInt32(i), JsShapePropertyFlags.Open);
        return obj;
    }

    protected Dictionary<int, PropertyDescriptor> BuildPropertyDescriptorDictionary(int propertyCount)
    {
        var map = new Dictionary<int, PropertyDescriptor>();
        for (var i = 0; i < propertyCount; i++)
            map.Add(PropertyAtoms[i], PropertyDescriptor.OpenData(JsValue.FromInt32(i)));
        return map;
    }

    protected static string BuildJsonObject(int propertyCount)
    {
        if (propertyCount <= 0)
            return "{}";

        var parts = new string[propertyCount];
        for (var i = 0; i < propertyCount; i++)
            parts[i] = $"\"p{i}\":{i}";
        return "{" + string.Join(",", parts) + "}";
    }

    protected static string BuildJsonObjectWithDuplicateTail(int propertyCount)
    {
        if (propertyCount <= 1)
            return "{\"p0\":0,\"p0\":1}";

        var parts = new string[propertyCount + 1];
        for (var i = 0; i < propertyCount; i++)
            parts[i] = $"\"p{i}\":{i}";
        parts[^1] = $"\"p0\":{propertyCount}";
        return "{" + string.Join(",", parts) + "}";
    }

    protected static string BuildNestedJsonPayload(int width, int depth)
    {
        if (depth <= 0)
            return BuildJsonObject(width);

        var parts = new string[width];
        for (var i = 0; i < width; i++)
        {
            var child = depth == 1
                ? $"{{\"value\":{i},\"array\":[{i},{i + 1},{i + 2}]}}"
                : BuildNestedJsonPayload(Math.Max(2, width / 2), depth - 1);
            parts[i] = $"\"p{i}\":{child}";
        }

        return "{" + string.Join(",", parts) + "}";
    }
}
