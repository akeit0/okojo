using System.Text;
using Okojo.Annotations;
using Okojo.Compiler;
using Okojo.DocGenerator.Annotations;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Reflection;
using Okojo.Runtime;
using Okojo.Runtime.Interop;

namespace Okojo.Tests;

public sealed class ClrNamespaceSample
{
    public static int StaticCount;
    public static string Label { get; set; } = "type";

    public static string DescribeStatic(int delta)
    {
        StaticCount += delta;
        return $"{Label}:{StaticCount}";
    }

    public string Ping()
    {
        return "pong";
    }

    public string Echo(int value)
    {
        return $"int:{value}";
    }

    public string Echo(string value)
    {
        return $"string:{value}";
    }

    public string Echo(object value)
    {
        return $"object:{value}";
    }

    public string JoinValues(int value)
    {
        return $"single:{value}";
    }

    public string JoinValues(params int[] values)
    {
        return $"many:{string.Join(",", values)}";
    }
}

public static class ClrStaticNamespaceSample
{
    public static int Count;
    public static string Name { get; set; } = "start";

    public static string AddAndDescribe(int delta)
    {
        Count += delta;
        return $"{Name}:{Count}";
    }

    public static void Reset()
    {
        Name = "start";
        Count = 0;
    }

    public static string Echo(int value)
    {
        return $"int:{value}";
    }

    public static string Echo(string value)
    {
        return $"string:{value}";
    }

    public static string Echo(object value)
    {
        return $"object:{value}";
    }

    public static string JoinValues(int value)
    {
        return $"single:{value}";
    }

    public static string JoinValues(params int[] values)
    {
        return $"many:{string.Join(",", values)}";
    }

    public static string Echo<T>(T value)
    {
        return $"generic:{typeof(T).Name}:{value}";
    }
}

public static class ClrRefOutSample
{
    public static void Increment(ref int value)
    {
        value++;
    }

    public static void AssignText(out string? value)
    {
        value = "done";
    }

    public static string Choose(string? value)
    {
        return value is null ? "string:null" : $"string:{value}";
    }

    public static string Choose(object? value)
    {
        return value is null ? "object:null" : $"object:{value}";
    }
}

public sealed class ClrOperatorSample(int value)
{
    public int Value { get; } = value;

    public static ClrOperatorSample operator +(ClrOperatorSample left, ClrOperatorSample right)
    {
        return new(left.Value + right.Value);
    }
}

public sealed class ManualHostBindingSample : IHostBindable
{
    private static readonly HostBinding HostBinding = CreateHostBinding();

    public float X { get; set; }

    HostBinding IHostBindable.GetHostBinding()
    {
        return HostBinding;
    }

    public static float Sin(float a)
    {
        return MathF.Sin(a);
    }

    public static ManualHostBindingSample Bounce(ManualHostBindingSample value)
    {
        return value;
    }

    public string EchoOptional(string value = "fallback")
    {
        return value;
    }

    public static JsHostObject ToJsObject(JsRealm realm, ManualHostBindingSample value)
    {
        return realm.WrapHostObject(value);
    }

    public static JsHostFunction ToJsType(JsRealm realm)
    {
        return realm.WrapHostType(typeof(ManualHostBindingSample), HostBinding);
    }

    private static HostBinding CreateHostBinding()
    {
        return new(typeof(ManualHostBindingSample),
            [
                new("X", HostMemberBindingKind.Property, false,
                    static (in info) => new(info.GetThis<ManualHostBindingSample>().X),
                    static (in info) =>
                    {
                        info.GetThis<ManualHostBindingSample>().X = info.GetArgumentSingle(0);
                        return JsValue.Undefined;
                    })
            ],
            [
                new("Sin", HostMemberBindingKind.Method, true,
                    methodBody: static (in info) => new(Sin(info.GetArgumentSingle(0))),
                    functionLength: 1),
                new("Bounce", HostMemberBindingKind.Method, true,
                    methodBody: static (in info) =>
                        HostValueConverter.ConvertToJsValue(info.Realm,
                            Bounce(info.GetArgument<ManualHostBindingSample>(0))),
                    functionLength: 1)
            ]);
    }
}

[GenerateJsObject]
[DocDeclaration("Foo\\Bar", "Docs.Shapes")]
public partial class GeneratedHostBindingSample
{
    public float X { get; set; }

    public static float Sin(float a)
    {
        return MathF.Sin(a);
    }

    [DocIgnore]
    public string Echo(string value)
    {
        return $"echo:{value}";
    }

    public static int SumNumbers(ReadOnlySpan<int> values)
    {
        var sum = 0;
        foreach (var value in values)
            sum += value;
        return sum;
    }

    public static string DescribeJsValues(ReadOnlySpan<JsValue> values)
    {
        if (values.Length == 0)
            return string.Empty;

        var parts = new string[values.Length];
        for (var i = 0; i < values.Length; i++)
            parts[i] = values[i].ToString() ?? string.Empty;
        return string.Join("|", parts);
    }

    public static string DescribeAny(ReadOnlySpan<object> values)
    {
        if (values.Length == 0)
            return string.Empty;

        var parts = new string[values.Length];
        for (var i = 0; i < values.Length; i++)
            parts[i] = values[i]?.ToString() ?? "null";
        return string.Join("|", parts);
    }

    public static string Pick(string value)
    {
        return $"string:{value}";
    }

    public static string Pick(int value)
    {
        return $"number:{value}";
    }

    public static string Pick(object value)
    {
        return $"object:{value}";
    }
}

public sealed class ClrAsyncNamespaceSample
{
    public async Task<string> EchoAsync(string value)
    {
        await Task.Yield();
        return $"echo:{value}";
    }

    public static async Task<string> AwaitEcho(Task<string> value)
    {
        return "await:" + await value;
    }
}

public sealed class ManualAsyncHostBindingSample : IHostBindable
{
    private static readonly HostBinding HostBinding = CreateHostBinding();

    HostBinding IHostBindable.GetHostBinding()
    {
        return HostBinding;
    }

    public static async Task<string> EchoAsync(string value)
    {
        await Task.Yield();
        return $"manual:{value}";
    }

    public static async Task<string> AwaitEcho(Task<string> value)
    {
        return "manual-await:" + await value;
    }

    public static JsHostFunction ToJsType(JsRealm realm)
    {
        return realm.WrapHostType(typeof(ManualAsyncHostBindingSample), HostBinding);
    }

    private static HostBinding CreateHostBinding()
    {
        return new(typeof(ManualAsyncHostBindingSample),
            [],
            [
                new("EchoAsync", HostMemberBindingKind.Method, true,
                    methodBody: static (in info) => info.Realm.WrapTask(EchoAsync(info.GetArgumentString(0))),
                    functionLength: 1),
                new("AwaitEcho", HostMemberBindingKind.Method, true,
                    methodBody: static (in info) =>
                        info.Realm.WrapTask(AwaitEcho(info.Realm.ToTask<string>(info.GetArgument(0)))),
                    functionLength: 1)
            ]);
    }
}

[GenerateJsObject]
[DocDeclaration("Foo\\Bar", "Docs.Shapes")]
public partial class GeneratedAsyncHostBindingSample
{
    public static async Task<string> EchoAsync(string value)
    {
        await Task.Yield();
        return $"generated:{value}";
    }

    public static async Task<string> AwaitEcho(Task<string> value)
    {
        return "generated-await:" + await value;
    }
}

public class HostInteropTests
{
    private static JsRealm CreateClrRealm()
    {
        return JsRuntime.Create(options => options.AllowClrAccess()).DefaultRealm;
    }

    [Test]
    public void HostObjectNamedMembersUseReflectionBackedSlots()
    {
        var realm = CreateClrRealm();
        var host = new HostCounter();
        realm.Global["host"] = realm.WrapHostValue(host);

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            host.Name = "sum";
            let first = host.AddAndDescribe(3);
            let keys = Object.keys(host).join(",");
            let hostName = host.Name;
            host.Name = "sum2";
            [first, hostName, host.Count, keys,host.Name].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("sum:3|sum|3|0,1,2|sum2"));
        Assert.That(host.Name, Is.EqualTo("sum2"));
        Assert.That(host.Count, Is.EqualTo(3));
    }

    [Test]
    public void HostObjectIndexerReadsWritesAndEnumeratesElements()
    {
        var realm = CreateClrRealm();
        var host = new HostCounter();
        realm.Global["host"] = JsValue.FromObject(realm.WrapHostObject(host));

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            host[1] = "bb";
            [host[0], host[1], Reflect.ownKeys(host).slice(0, 3).join(",")].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("a|bb|0,1,2"));
    }

    [Test]
    public void HostObjectUsesAccessorDescriptorsForNonMethods()
    {
        var realm = CreateClrRealm();
        realm.Global["host"] = realm.WrapHostValue(new HostCounter());

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const nameDesc = Object.getOwnPropertyDescriptor(host, "Name");
            const countDesc = Object.getOwnPropertyDescriptor(host, "Count");
            const methodDesc = Object.getOwnPropertyDescriptor(host, "AddAndDescribe");
            [
              typeof nameDesc.get === "function" && !("value" in nameDesc),
              typeof countDesc.get === "function" && typeof countDesc.set === "function" && !("value" in countDesc),
              typeof methodDesc.value === "function" && methodDesc.get === undefined && methodDesc.set === undefined
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true|true"));
    }

    [Test]
    public void HostObjectExposesClrTypeToStringTag()
    {
        var realm = CreateClrRealm();
        realm.Global["host"] = realm.WrapHostValue(new StringBuilder(100));

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            [
              Object.prototype.toString.call(host),
              host[Symbol.toStringTag]
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("[object System.Text.StringBuilder]|System.Text.StringBuilder"));
    }

    [Test]
    public void HostObjectReadingSetterOnlyPropertyReturnsUndefinedWithoutCallingSetter()
    {
        var realm = CreateClrRealm();
        var host = new HostAccessorEdges();
        realm.Global["host"] = realm.WrapHostValue(host);

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const value = host.SetterOnly;
            [value === undefined, host.SetterOnlyWriteCount, host.LastSetterOnlyValue].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|0|"));
        Assert.That(host.SetterOnlyWriteCount, Is.EqualTo(0));
        Assert.That(host.LastSetterOnlyValue, Is.EqualTo(string.Empty));
    }

    [Test]
    public void HostObjectWritingGetterOnlyPropertyDoesNotCallGetter()
    {
        var realm = CreateClrRealm();
        var host = new HostAccessorEdges();
        realm.Global["host"] = realm.WrapHostValue(host);

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            host.GetterOnly = 9;
            [host.GetterOnlyReadCount, host.GetterOnly].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("0|123"));
        Assert.That(host.GetterOnlyReadCount, Is.EqualTo(1));
    }

    [Test]
    public void HostWrappingRequiresClrAccessToBeEnabled()
    {
        var realm = JsRuntime.Create().DefaultRealm;

        var ex = Assert.Throws<InvalidOperationException>(() => realm.WrapHostValue(new HostCounter()));

        Assert.That(ex!.Message, Does.Contain("AllowClrAccess"));
    }

    [Test]
    public void JsRuntimeOptionsObjectCanEnableClrAccess()
    {
        var engine = JsRuntime.Create(new JsRuntimeOptions().AllowClrAccess());
        var value = engine.DefaultRealm.WrapHostValue(new HostCounter());

        Assert.That(value.IsObject, Is.True);
        Assert.That(value.AsObject(), Is.TypeOf<JsHostObject>());
        Assert.That(engine.IsClrAccessEnabled, Is.True);
    }

    [Test]
    public void ClrNamespaceObjectCanConstructAllowedAssemblyType()
    {
        ClrNamespaceSample.Label = "type";
        ClrNamespaceSample.StaticCount = 0;
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(ClrNamespaceSample).Assembly));
        var realm = engine.DefaultRealm;
        var sampleNamespace = realm.GetClrNamespace("Okojo.Tests");
        Assert.That(sampleNamespace.TryGetProperty("ClrNamespaceSample", out var sampleTypeValue), Is.True);
        Assert.That(sampleTypeValue.TryGetObject(out var sampleTypeObject), Is.True);
        Assert.That(sampleTypeObject, Is.TypeOf<JsHostFunction>());

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const value = new clr.Okojo.Tests.ClrNamespaceSample();
            value.Ping();
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("pong"));
    }

    [Test]
    public void ClrNamespaceAndTypeObjectsExposeToStringTag()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(ClrNamespaceSample).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            [
              Object.prototype.toString.call(clr.System),
              Object.prototype.toString.call(clr.Okojo.Tests.ClrNamespaceSample)
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("[object CLR Namespace System]|[object CLR Type Okojo.Tests.ClrNamespaceSample]"));
    }

    [Test]
    public void ClrTypeObjectExposesStaticMembersViaSlots()
    {
        ClrNamespaceSample.Label = "type";
        ClrNamespaceSample.StaticCount = 0;
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(ClrNamespaceSample).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const type = clr.Okojo.Tests.ClrNamespaceSample;
            const labelDesc = Object.getOwnPropertyDescriptor(type, "Label");
            const methodDesc = Object.getOwnPropertyDescriptor(type, "DescribeStatic");
            type.Label = "changed";
            [
              type.DescribeStatic(4),
              type.Label,
              type.StaticCount,
              typeof labelDesc.get === "function" && typeof labelDesc.set === "function" && !("value" in labelDesc),
              typeof methodDesc.value === "function" && methodDesc.get === undefined && methodDesc.set === undefined
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("changed:4|changed|4|true|true"));
    }

    [Test]
    public void ClrNamespaceObjectSupportsStaticClasses()
    {
        ClrStaticNamespaceSample.Reset();
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(ClrStaticNamespaceSample).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const type = clr.Okojo.Tests.ClrStaticNamespaceSample;
            let constructTypeError = false;
            try {
              new type();
            } catch (e) {
              constructTypeError = e && e.name === "TypeError";
            }
            type.Name = "static";
            [
              type.AddAndDescribe(2),
              type.Count,
              type.Name,
              constructTypeError
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("static:2|2|static|true"));
    }

    [Test]
    public void ClrTypeObjectSupportsStaticMethodOverloads()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(ClrStaticNamespaceSample).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const type = clr.Okojo.Tests.ClrStaticNamespaceSample;
            [type.Echo(7), type.Echo("hi"), type.Echo({ ok: 1 }).startsWith("object:"), typeof type.Echo].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("int:7|string:hi|true|function"));
    }

    [Test]
    public void ClrHostObjectSupportsInstanceMethodOverloads()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(ClrNamespaceSample).Assembly));
        var realm = engine.DefaultRealm;
        realm.Global["sample"] = realm.WrapHostValue(new ClrNamespaceSample());

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            [sample.Echo(3), sample.Echo("ok"), sample.Echo({ ok: 1 }).startsWith("object:"), typeof sample.Echo].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("int:3|string:ok|true|function"));
    }

    [Test]
    public void ClrReflectionOverloadResolutionPrefersFixedArityOverParamsArray()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(ClrStaticNamespaceSample).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const type = clr.Okojo.Tests.ClrStaticNamespaceSample;
            [type.JoinValues(), type.JoinValues(7), type.JoinValues(1, 2, 3)].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("many:|single:7|many:1,2,3"));
    }

    [Test]
    public void ClrInteropExposesSelectedSpecialNameMethods()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(ClrOperatorSample).Assembly)
            .AddClrAssembly(typeof(List<>).Assembly)
            .AddClrAssembly(typeof(string).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const ListOfString = clr.System.Collections.Generic.List$1(clr.System.String);
            const list = new ListOfString();
            list.Add("a");
            list.Add("b");
            list.set_Item(1, "z");

            const Sample = clr.Okojo.Tests.ClrOperatorSample;
            const sum = Sample.op_Addition(new Sample(3), new Sample(4));

            [
              typeof list.get_Item,
              list.get_Item(0),
              list.get_Item(1),
              sum.Value
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("function|a|z|7"));
    }

    [Test]
    public void ClrNamespaceObjectExposesConsoleWriteLineMethod()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(Console).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const type = clr.System.Console;
            const desc = Object.getOwnPropertyDescriptor(type, "WriteLine");
            [typeof type.WriteLine, typeof desc.value].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("function|function"));
    }

    [Test]
    public void ClrNamespaceObjectSupportsGenericTypeBinding()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(List<>).Assembly)
            .AddClrAssembly(typeof(string).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const ListOfString = clr.System.Collections.Generic.List(clr.System.String);
            const list = new ListOfString();
            list.Add("x");
            [
              Object.prototype.toString.call(ListOfString),
              Object.prototype.toString.call(list),
              list.Count,
              list[0]
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo(
                "[object CLR Type System.Collections.Generic.List<System.String>]|[object System.Collections.Generic.List<System.String>]|1|x"));
    }

    [Test]
    public void ClrNamespaceObjectSupportsGenericTypeAritySuffix()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(KeyValuePair<,>).Assembly)
            .AddClrAssembly(typeof(string).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const Pair = clr.System.Collections.Generic.KeyValuePair$2(
              clr.System.String,
              clr.System.String
            );
            const pair = new Pair("left", "right");
            [
              Object.prototype.toString.call(Pair),
              pair.Key,
              pair.Value
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo(
                "[object CLR Type System.Collections.Generic.KeyValuePair<System.String, System.String>]|left|right"));
    }

    [Test]
    public void ClrNamespaceObjectSupportsGenericMethodAritySuffix()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(Enumerable).Assembly)
            .AddClrAssembly(typeof(List<>).Assembly)
            .AddClrAssembly(typeof(string).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const ListOfString = clr.System.Collections.Generic.List$1(clr.System.String);
            const list = new ListOfString();
            list.Add("a");
            list.Add("b");
            const toArray = clr.System.Linq.Enumerable.ToArray$1(clr.System.String);
            const array = toArray(list);
            [
              array.Length,
              array[0],
              array[1]
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("2|a|b"));
    }

    [Test]
    public void ClrUsingResolverSupportsNamespaceAndTypeImports()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(Enumerable).Assembly)
            .AddClrAssembly(typeof(KeyValuePair<,>).Assembly)
            .AddClrAssembly(typeof(string).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const clrUsings = $using(clr.System, clr.System.Collections.Generic);
            clrUsings.Add(clr.System.Linq.Enumerable);

            const Pair = clrUsings.KeyValuePair$2(clrUsings.String, clrUsings.Int32);
            const pair = new Pair("count", 3);
            const ListOfString = clrUsings.List$1(clrUsings.String);
            const list = new ListOfString();
            list.Add("x");
            list.Add("y");
            const array = clrUsings.ToArray$1(clrUsings.String)(list);

            [
              pair.Key,
              pair.Value,
              array.Length,
              array[1]
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("count|3|2|y"));
    }

    [Test]
    public void ClrHostObjectsSupportSymbolIteratorViaGetEnumerator()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(Dictionary<,>).Assembly)
            .AddClrAssembly(typeof(string).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const $usings = $using(clr.System, clr.System.Collections.Generic);
            const Dict = $usings.Dictionary$2($usings.String, $usings.String);
            const dict = new Dict();
            dict.set_Item("one", "ichi");
            dict.set_Item("two", "ni");

            let parts = [];
            for (const p of dict) {
              parts.push(p.Key + ":" + p.Value);
            }

            parts.sort().join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("one:ichi|two:ni"));
    }

    [Test]
    public void ClrHelpersSupportRefOutPlaceholdersAndTypedNull()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(ClrRefOutSample).Assembly)
            .AddClrAssembly(typeof(int).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const sample = clr.Okojo.Tests.ClrRefOutSample;
            const n = $place(clr.System.Int32, 4);
            const s = $place(clr.System.String);
            sample.Increment(n);
            sample.AssignText(s);
            [
              n.value,
              s.value,
              sample.Choose($null(clr.System.String))
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("5|done|string:null"));
    }

    [Test]
    public void ClrCastHelperForcesClrConversion()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(ClrStaticNamespaceSample).Assembly)
            .AddClrAssembly(typeof(int).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const sample = clr.Okojo.Tests.ClrStaticNamespaceSample;
            [
              sample.Echo(7),
              sample.Echo($cast(clr.System.String, 7)),
              sample.Echo$1(clr.System.Int32)(9)
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("int:7|string:7|generic:Int32:9"));
    }

    [Test]
    public void ClrVoidMethodReturnsUndefined()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(List<>).Assembly)
            .AddClrAssembly(typeof(int).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const ListOfInt = clr.System.Collections.Generic.List(clr.System.Int32);
            const list = new ListOfInt();
            [
              list.Add(7) === undefined,
              list[0]
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|7"));
    }

    [Test]
    public void ClrNamespaceObjectRespectsAssemblyOptIn()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(StringBuilder).Assembly));
        var realm = engine.DefaultRealm;

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            let ok = false;
            try {
              new clr.Okojo.Tests.ClrNamespaceSample();
              ok = true;
            } catch (e) {
            }
            ok;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsFalse, Is.True);
    }

    [Test]
    public void ClrGlobalIsUndefinedWhenClrAccessDisabled()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("typeof clr;"));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("undefined"));
    }

    [Test]
    public void HostWrappingUsesStableWrapperIdentityPerRealm()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var sample = new ManualHostBindingSample();

        var first = realm.WrapHostObject(sample);
        var second = realm.WrapHostObject(sample);

        Assert.That(ReferenceEquals(first, second), Is.True);
    }

    [Test]
    public void HostWrappingUsesDistinctWrappersAcrossRealms()
    {
        var engine = JsRuntime.Create();
        var realmA = engine.DefaultRealm;
        var realmB = engine.CreateRealm();
        var sample = new ManualHostBindingSample();

        var first = realmA.WrapHostObject(sample);
        var second = realmB.WrapHostObject(sample);

        Assert.That(ReferenceEquals(first, second), Is.False);
    }

    [Test]
    public void HostWrappingDoesNotCacheValueTypeWrappers()
    {
        var realm = CreateClrRealm();

        var first = realm.WrapHostObject(7);
        var second = realm.WrapHostObject(7);

        Assert.That(ReferenceEquals(first, second), Is.False);
    }

    [Test]
    public void ManualHostBindingSupportsFastMembersAndReflectionFallback()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["ManualHostBindingSample"] = JsValue.FromObject(ManualHostBindingSample.ToJsType(realm));

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const type = ManualHostBindingSample;
            const sample = new type();
            sample.X = 1.5;
            [
              sample.X,
              type.Sin(0),
              sample.EchoOptional(),
              Object.getOwnPropertyDescriptor(sample, "X").get !== undefined
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("1.5|0|fallback|true"));
    }

    [Test]
    public void ManualHostBindingPreservesIdentityAcrossClrRoundTrips()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var sample = new ManualHostBindingSample();
        realm.Global["sample"] = JsValue.FromObject(ManualHostBindingSample.ToJsObject(realm, sample));
        realm.Global["ManualHostBindingSample"] = JsValue.FromObject(ManualHostBindingSample.ToJsType(realm));

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            sample === ManualHostBindingSample.Bounce(sample);
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ManualHostBindingHelpersUseSharedRealmWrappers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var sample = new ManualHostBindingSample();

        var wrapper = ManualHostBindingSample.ToJsObject(realm, sample);
        var wrappedAgain = realm.WrapHostObject(sample);
        var typeFunction = ManualHostBindingSample.ToJsType(realm);

        Assert.That(ReferenceEquals(wrapper, wrappedAgain), Is.True);
        Assert.That(
            ReferenceEquals(typeFunction,
                realm.WrapHostType(typeof(ManualHostBindingSample), ((IHostBindable)sample).GetHostBinding())),
            Is.True);
    }

    [Test]
    public void GeneratedHostBindingSupportsGeneratedMembers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["GeneratedHostBindingSample"] = JsValue.FromObject(GeneratedHostBindingSample.ToJsType(realm));

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const type = GeneratedHostBindingSample;
            const sample = new type();
            sample.X = 2.5;
            [
              sample.X,
              sample.Echo("ok"),
              type.Sin(0)
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("2.5|echo:ok|0"));
    }

    [Test]
    public void GeneratedHostBindingSupportsReadOnlySpanArguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["GeneratedHostBindingSample"] = JsValue.FromObject(GeneratedHostBindingSample.ToJsType(realm));

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            [
              GeneratedHostBindingSample.SumNumbers(1, 2, 3, 4),
              GeneratedHostBindingSample.DescribeJsValues(1, "x", true),
              GeneratedHostBindingSample.DescribeAny(1, "x", true),
              GeneratedHostBindingSample.Pick("x"),
              GeneratedHostBindingSample.Pick(7),
              GeneratedHostBindingSample.Pick(true)
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("10|1|x|true|1|x|True|string:x|number:7|object:True"));
    }

    [Test]
    public void GeneratedHostBindingHelpersReuseTheSameWrapper()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var sample = new GeneratedHostBindingSample();

        var wrapper = GeneratedHostBindingSample.ToJsObject(realm, sample);
        var wrappedAgain = realm.WrapHostObject(sample);
        var typeFunction = GeneratedHostBindingSample.ToJsType(realm);

        Assert.That(ReferenceEquals(wrapper, wrappedAgain), Is.True);
        Assert.That(
            ReferenceEquals(typeFunction,
                realm.WrapHostType(typeof(GeneratedHostBindingSample),
                    ((IHostBindable)sample).GetHostBinding())), Is.True);
    }

    [Test]
    public void ManualAndGeneratedBindingsWorkWithoutClrAccess()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["ManualHostBindingSample"] = JsValue.FromObject(ManualHostBindingSample.ToJsType(realm));
        realm.Global["GeneratedHostBindingSample"] = JsValue.FromObject(GeneratedHostBindingSample.ToJsType(realm));

        var script = new JsCompiler(realm).Compile(JavaScriptParser.ParseScript("""
            const a = new ManualHostBindingSample();
            const b = new GeneratedHostBindingSample();
            a.X = 3.5;
            b.X = 4.5;
            [
              a.X,
              ManualHostBindingSample.Sin(0),
              b.X,
              GeneratedHostBindingSample.Sin(0)
            ].join("|");
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("3.5|0|4.5|0"));
    }

    [Test]
    public async Task ClrHostInteropConvertsTaskAndPromise()
    {
        var engine = JsRuntime.Create(options => options
            .AllowClrAccess()
            .AddClrAssembly(typeof(ClrAsyncNamespaceSample).Assembly)
            .AddClrAssembly(typeof(string).Assembly));
        var realm = engine.DefaultRealm;
        realm.Global["sample"] = realm.WrapHostValue(new ClrAsyncNamespaceSample());

        var value = await realm.EvalAsync("""
                                          (async () => {
                                            return [
                                              await sample.EchoAsync("ok"),
                                              await clr.Okojo.Tests.ClrAsyncNamespaceSample.AwaitEcho(Promise.resolve("x"))
                                            ].join("|");
                                          })()
                                          """);

        Assert.That(value.IsString, Is.True);
        Assert.That(value.AsString(), Is.EqualTo("echo:ok|await:x"));
    }

    [Test]
    public async Task ManualHostBindingConvertsTaskAndPromiseWithoutClrAccess()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["ManualAsyncHostBindingSample"] = JsValue.FromObject(ManualAsyncHostBindingSample.ToJsType(realm));

        var value = await realm.EvalAsync("""
                                          (async () => {
                                            return [
                                              await ManualAsyncHostBindingSample.EchoAsync("ok"),
                                              await ManualAsyncHostBindingSample.AwaitEcho(Promise.resolve("x"))
                                            ].join("|");
                                          })()
                                          """);

        Assert.That(value.IsString, Is.True);
        Assert.That(value.AsString(), Is.EqualTo("manual:ok|manual-await:x"));
    }

    [Test]
    public async Task GeneratedHostBindingConvertsTaskAndPromiseWithoutClrAccess()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["GeneratedAsyncHostBindingSample"] =
            JsValue.FromObject(GeneratedAsyncHostBindingSample.ToJsType(realm));

        var value = await realm.EvalAsync("""
                                          (async () => {
                                            return [
                                              await GeneratedAsyncHostBindingSample.EchoAsync("ok"),
                                              await GeneratedAsyncHostBindingSample.AwaitEcho(Promise.resolve("x"))
                                            ].join("|");
                                          })()
                                          """);

        Assert.That(value.IsString, Is.True);
        Assert.That(value.AsString(), Is.EqualTo("generated:ok|generated-await:x"));
    }

    [Test]
    public async Task CallAsyncAwaitsReturnedPromise()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Global["GeneratedAsyncHostBindingSample"] =
            JsValue.FromObject(GeneratedAsyncHostBindingSample.ToJsType(realm));
        realm.Eval("""
                   async function runCallAsync() {
                     return await GeneratedAsyncHostBindingSample.EchoAsync("call");
                   }
                   """);

        var fn = realm.Global["runCallAsync"];
        var value = await realm.CallAsync(fn, JsValue.Undefined);

        Assert.That(value.IsString, Is.True);
        Assert.That(value.AsString(), Is.EqualTo("generated:call"));
    }

    [Test]
    public void HostValueConverter_Generic_Conversion_Uses_Direct_Typed_Paths()
    {
        using var runtime = JsRuntime.Create(options => options.AllowClrAccess());
        var realm = runtime.DefaultRealm;
        var host = new ManualHostBindingSample { X = 7 };
        var hostObject = realm.WrapHostObject(host);
        var hostValue = JsValue.FromObject(hostObject);

        Assert.Multiple(() =>
        {
            Assert.That(HostValueConverter.ConvertFromJsValue<int>(realm, JsValue.FromInt32(12)), Is.EqualTo(12));
            Assert.That(HostValueConverter.ConvertFromJsValue<double>(realm, new(12.5d)), Is.EqualTo(12.5d));
            Assert.That(HostValueConverter.ConvertFromJsValue<string>(realm, JsValue.FromInt32(12)), Is.EqualTo("12"));
            Assert.That(HostValueConverter.ConvertFromJsValue<string?>(realm, JsValue.Null), Is.Null);
            Assert.That(HostValueConverter.ConvertFromJsValue<int?>(realm, JsValue.Null), Is.Null);
            Assert.That(HostValueConverter.ConvertFromJsValue<object>(realm, JsValue.True), Is.EqualTo(true));
            Assert.That(HostValueConverter.ConvertFromJsValue<JsObject>(realm, hostValue), Is.SameAs(hostObject));
            Assert.That(HostValueConverter.ConvertFromJsValue<ManualHostBindingSample>(realm, hostValue),
                Is.SameAs(host));
        });
    }

    private sealed class HostCounter
    {
        private readonly string[] items = ["a", "b", "c"];
        public int Count;

        public string Name { get;set; } = "start";

        public int Length => items.Length;

        public string this[int index]
        {
            get => items[index];
            set => items[index] = value;
        }

        public string AddAndDescribe(int delta)
        {
            Count += delta;
            return $"{Name}:{Count}";
        }
    }

    private sealed class HostAccessorEdges
    {
        public int SetterOnlyWriteCount { get; private set; }

        public int GetterOnlyReadCount { get; private set; }

        public string LastSetterOnlyValue { get; private set; } = string.Empty;

        public string SetterOnly
        {
            set
            {
                SetterOnlyWriteCount++;
                LastSetterOnlyValue = value;
            }
        }

        public int GetterOnly
        {
            get
            {
                GetterOnlyReadCount++;
                return 123;
            }
        }
    }
}
