using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class OkojoDynamicPlainObjectTests
{
    [Test]
    public void JsonParse_Uses_DictionaryStart_PlainObject()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            JSON.parse('{"a":1,"b":2}');
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsObject, Is.True);
        Assert.That(realm.Accumulator.AsObject(), Is.TypeOf<JsPlainObject>());
        Assert.That(((JsPlainObject)realm.Accumulator.AsObject()).UsesDynamicNamedProperties, Is.True);
        var parsed = (JsPlainObject)realm.Accumulator.AsObject();
        Assert.That(parsed.Slots.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(parsed.TryGetOwnNamedSlotIndex(realm.Atoms.InternNoCheck("a"), out var slotA), Is.True);
        Assert.That(slotA, Is.EqualTo(0));
        Assert.That(parsed.TryGetOwnNamedSlotIndex(realm.Atoms.InternNoCheck("b"), out var slotB), Is.True);
        Assert.That(slotB, Is.EqualTo(1));
    }

    [Test]
    public void AppendOnly_PlainObject_Growth_Stays_On_FastShape_Path()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const obj = {};
            obj.a = 1;
            obj.b = 2;
            obj.c = 3;
            obj;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsObject, Is.True);
        Assert.That(realm.Accumulator.AsObject(), Is.TypeOf<JsPlainObject>());
        Assert.That(((JsPlainObject)realm.Accumulator.AsObject()).UsesDynamicNamedProperties, Is.False);
    }

    [Test]
    public void JsonParse_ObjectKeys_And_DescriptorSemantics_Remain_Ordinary()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const obj = JSON.parse('{"b":2,"a":1}');
            const keys = Object.keys(obj).join(',');
            const desc = Object.getOwnPropertyDescriptor(obj, "a");
            keys === "b,a" &&
            desc.value === 1 &&
            desc.writable === true &&
            desc.enumerable === true &&
            desc.configurable === true &&
            Object.prototype.propertyIsEnumerable.call(obj, "a") === true;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonParse_GetOwnPropertyNames_And_Entries_Preserve_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const obj = JSON.parse('{"2":"two","1":"one","b":2,"a":1}');
            const names = Object.getOwnPropertyNames(obj).join(',');
            const entries = Object.entries(obj);
            names === "1,2,b,a" &&
            entries.length === 4 &&
            entries[0][0] === "1" && entries[0][1] === "one" &&
            entries[1][0] === "2" && entries[1][1] === "two" &&
            entries[2][0] === "b" && entries[2][1] === 2 &&
            entries[3][0] === "a" && entries[3][1] === 1;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonParse_Mixed_Index_And_Named_Properties_Keep_Dynamic_Named_Slots_Dense()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            JSON.parse('{"2":"two","1":"one","b":2,"a":1}');
            """));

        realm.Execute(script);

        var parsed = (JsPlainObject)realm.Accumulator.AsObject();
        Assert.That(parsed.Slots.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(parsed.TryGetOwnNamedSlotIndex(realm.Atoms.InternNoCheck("b"), out var slotB), Is.True);
        Assert.That(slotB, Is.EqualTo(0));
        Assert.That(parsed.TryGetOwnNamedSlotIndex(realm.Atoms.InternNoCheck("a"), out var slotA), Is.True);
        Assert.That(slotA, Is.EqualTo(1));
    }

    [Test]
    public void JsonStringify_Uses_Ordinary_Named_Property_Enumeration_For_DictionaryStart_Object()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const obj = JSON.parse('{"2":"two","1":"one","b":2,"a":1}');
            JSON.stringify(obj) === '{"1":"one","2":"two","b":2,"a":1}';
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonStringify_Escapes_Strings_And_Property_Names()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const obj = { "a\"b": "line\n\t\\x" };
            JSON.stringify(obj) === '{"a\\"b":"line\\n\\t\\\\x"}';
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonParse_Delete_Then_Redefine_Preserves_Ordinary_Order()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const obj = JSON.parse('{"a":1,"b":2,"c":3}');
            delete obj.b;
            Object.defineProperty(obj, "b", { value: 9, writable: true, enumerable: true, configurable: true });
            Object.keys(obj).join(',') === "a,c,b" && obj.b === 9;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonParse_Duplicate_Named_Keys_Keep_First_Order_And_Last_Value()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const obj = JSON.parse('{"a":1,"b":2,"a":3}');
            Object.keys(obj).join(',') === "a,b" && obj.a === 3 && obj.b === 2;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void JsonParse_Dense_Arrays_Remain_Ordinary_And_Keep_Values()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const value = JSON.parse('{"items":[1,2,{"x":[3,4]}]}');
            Array.isArray(value.items) &&
            value.items.length === 3 &&
            value.items[0] === 1 &&
            value.items[1] === 2 &&
            Array.isArray(value.items[2].x) &&
            value.items[2].x.length === 2 &&
            value.items[2].x[0] === 3 &&
            value.items[2].x[1] === 4;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DictionaryStartPlainObject_Supports_Accessor_Definition()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const obj = JSON.parse('{"a":1}');
            let seen = 0;
            Object.defineProperty(obj, "x", {
              get: function () { return seen + 1; },
              set: function (v) { seen = v; },
              enumerable: true,
              configurable: true
            });
            obj.x = 4;
            const d = Object.getOwnPropertyDescriptor(obj, "x");
            obj.x === 5 &&
            typeof d.get === "function" &&
            typeof d.set === "function" &&
            Object.keys(obj).join(',') === "a,x";
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DictionaryStartPlainObject_Seal_And_Freeze_Remain_Ordinary()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const sealedObj = JSON.parse('{"a":1}');
            Object.seal(sealedObj);
            const sealedOk =
              Object.isSealed(sealedObj) === true &&
              delete sealedObj.a === false;

            const frozenObj = JSON.parse('{"a":1}');
            Object.freeze(frozenObj);
            frozenObj.a = 3;
            const d = Object.getOwnPropertyDescriptor(frozenObj, "a");
            sealedOk &&
            Object.isFrozen(frozenObj) === true &&
            frozenObj.a === 1 &&
            d.writable === false &&
            d.configurable === false;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DictionaryStartPlainObject_DefineProperties_Descriptors_Values_And_HasOwn_Work()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const obj = JSON.parse('{"b":2,"a":1}');
            Object.defineProperties(obj, {
              c: { value: 3, enumerable: true, writable: true, configurable: true },
              d: { get: function () { return this.a + this.b; }, enumerable: true, configurable: true }
            });
            const descriptors = Object.getOwnPropertyDescriptors(obj);
            const values = Object.values(obj);
            Object.hasOwn(obj, "c") === true &&
            Object.hasOwn(obj, "missing") === false &&
            descriptors.c.value === 3 &&
            descriptors.c.enumerable === true &&
            typeof descriptors.d.get === "function" &&
            values.length === 4 &&
            values[0] === 2 &&
            values[1] === 1 &&
            values[2] === 3 &&
            values[3] === 3;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void DictionaryStartPlainObject_SetExistingProperty_Writes_Without_StaticSlotInfo()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var obj = new JsPlainObject(realm, useDictionaryMode: true);
        var atom = realm.Atoms.InternNoCheck("a");

        obj.DefineDataPropertyAtom(realm, atom, JsValue.FromInt32(1), JsShapePropertyFlags.Open);

        Assert.That(obj.UsesDynamicNamedProperties, Is.True);
        Assert.That(obj.TrySetPropertyAtom(realm, atom, JsValue.FromInt32(7), out var slotInfo), Is.True);
        Assert.That(slotInfo.IsValid, Is.False);
        Assert.That(obj.TryGetPropertyAtom(realm, atom, out var value, out _), Is.True);
        Assert.That(value.IsInt32, Is.True);
        Assert.That(value.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void FastPlainObject_DeleteChurn_Promotes_To_DictionaryMode()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const obj = { a: 1, b: 2, c: 3 };
            delete obj.b;
            obj.b = 4;
            delete obj.c;
            obj.c = 5;
            obj;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsObject, Is.True);
        Assert.That(realm.Accumulator.AsObject(), Is.TypeOf<JsPlainObject>());
        Assert.That(((JsPlainObject)realm.Accumulator.AsObject()).UsesDynamicNamedProperties, Is.True);
    }

    [Test]
    public void FastPlainObject_RedefineChurn_Promotes_To_DictionaryMode_Without_Breaking_Order_Or_Descriptors()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const obj = { a: 1, b: 2 };
            Object.defineProperty(obj, "a", { get: function () { return 7; }, enumerable: true, configurable: true });
            Object.defineProperty(obj, "a", { value: 9, writable: true, enumerable: true, configurable: true });
            obj;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsObject, Is.True);
        Assert.That(realm.Accumulator.AsObject(), Is.TypeOf<JsPlainObject>());
        Assert.That(((JsPlainObject)realm.Accumulator.AsObject()).UsesDynamicNamedProperties, Is.True);
    }

    [Test]
    public void FunctionObject_DeleteChurn_Promotes_To_DictionaryMode()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function f() {}
            f.a = 1;
            f.b = 2;
            delete f.a;
            f.a = 3;
            delete f.b;
            f.b = 4;
            Object.keys(f).join(',') === "a,b";
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
        Assert.That(realm.Global["f"].TryGetObject(out var functionObject), Is.True);
        Assert.That(functionObject!.UsesDynamicNamedProperties, Is.True);
    }

    [Test]
    public void FunctionObject_RedefineChurn_Promotes_To_DictionaryMode_Without_Breaking_Order_Or_Descriptors()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            function f() {}
            f.a = 1;
            f.b = 2;
            Object.defineProperty(f, "a", { get: function () { return 7; }, enumerable: true, configurable: true });
            Object.defineProperty(f, "a", { value: 9, writable: true, enumerable: true, configurable: true });
            const keys = Object.keys(f).join(',');
            const desc = Object.getOwnPropertyDescriptor(f, "a");
            keys === "a,b" &&
            desc.value === 9 &&
            desc.writable === true &&
            desc.enumerable === true &&
            desc.configurable === true;
            """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
        Assert.That(realm.Global["f"].TryGetObject(out var functionObject), Is.True);
        Assert.That(functionObject!.UsesDynamicNamedProperties, Is.True);
    }
}
