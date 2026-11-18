using Okojo.JavaScript;
using Okojo.JavaScript.Compiler;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;
using Okojo.JavaScript.Objects;
using Okojo.JavaScript.Parsing;

namespace Okojo.Tests;

public class ComparisonMethodsTests
{
    [Test]
    public void NullComparisonInCondition_DoesNotCoerceOrRepeatOperand()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            let reads = 0;
            const obj = { valueOf() { throw new Error("unexpected coercion"); } };
            function present(value) {
                if ((++reads, value) == null) return false;
                if (null != value) return true;
                throw new Error("inconsistent comparison");
            }
            !present(undefined) && !present(null) && present(0) && present(obj) && reads === 4;
            """
        );
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void StrictEquals_Primitives_Basic()
    {
        Assert.That(JsRealm.StrictEquals(JsValue.FromInt32(1), JsValue.FromInt32(1)), Is.True);
        Assert.That(JsRealm.StrictEquals(JsValue.FromInt32(1), JsValue.FromString("1")), Is.False);
        Assert.That(JsRealm.StrictEquals(JsValue.True, JsValue.False), Is.False);
        Assert.That(JsRealm.StrictEquals(JsValue.Null, JsValue.Null), Is.True);
    }

    [Test]
    public void AbstractEquals_BoolAndNumber_Coerces()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        Assert.That(JsRealm.AbstractEquals(realm, JsValue.True, JsValue.FromInt32(1)), Is.True);
        Assert.That(JsRealm.AbstractEquals(realm, JsValue.False, JsValue.FromInt32(0)), Is.True);
    }

    [Test]
    public void AbstractEquals_NumberAndString_Coerces()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        Assert.That(
            JsRealm.AbstractEquals(realm, JsValue.FromInt32(255), JsValue.FromString("0xff")),
            Is.True
        );
        Assert.That(
            JsRealm.AbstractEquals(realm, JsValue.FromInt32(1), JsValue.FromString("true")),
            Is.False
        );
    }

    [Test]
    public void AbstractEquals_ObjectAndPrimitive_ToPrimitiveBasic()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var obj = new JsPlainObject(realm);
        var valueOf = new JsHostFunction(
            realm,
            static (in info) =>
            {
                return JsValue.FromInt32(1);
            },
            "valueOf",
            0
        );
        obj.DefineDataPropertyAtom(
            realm,
            AtomTable.IdValueOf,
            JsValue.FromObject(valueOf),
            JsShapePropertyFlags.Open
        );

        Assert.That(
            JsRealm.AbstractEquals(realm, JsValue.FromObject(obj), JsValue.FromInt32(1)),
            Is.True
        );
    }

    [Test]
    public void AbstractEquals_ObjectToPrimitive_StringMismatch_Red()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var obj = new JsPlainObject(realm);
        var valueOf = new JsHostFunction(
            realm,
            (in info) =>
            {
                var r = info.Realm;
                return JsValue.FromObject(new JsPlainObject(r));
            },
            "valueOf",
            0
        );
        var toString = new JsHostFunction(
            realm,
            static (in info) =>
            {
                return JsValue.FromString("+1");
            },
            "toString",
            0
        );
        obj.DefineDataPropertyAtom(
            realm,
            AtomTable.IdValueOf,
            JsValue.FromObject(valueOf),
            JsShapePropertyFlags.Open
        );
        obj.DefineDataPropertyAtom(
            realm,
            AtomTable.IdToString,
            JsValue.FromObject(toString),
            JsShapePropertyFlags.Open
        );

        // Expected false: ToPrimitive(obj) => "+1", then "+1" == "1" is false.
        Assert.That(
            JsRealm.AbstractEquals(realm, JsValue.FromObject(obj), JsValue.FromString("1")),
            Is.False
        );
    }

    [Test]
    public void RelationalLessThan_ObjectValueOf_IsUsed()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(
            realm,
            JavaScriptParser.ParseScript(
                """
                ({ valueOf: function() { return 0; }, toString: function() { throw "bad"; } } < 1) === true;
                """
            )
        );
        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
