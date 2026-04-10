using Okojo.Objects;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ConversionMethodsTests
{
    [Test]
    public void EmptyStringJsValue_IsNotNumber()
    {
        var v = JsValue.FromString(string.Empty);
        Assert.That(v.IsString, Is.True);
        Assert.That(v.IsNumber, Is.False);
    }

    [Test]
    public void StringToNumberSlowPath_ParsesHex()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        Assert.That(realm.ToNumberSlowPath(JsValue.FromString("0xff")), Is.EqualTo(255d));
        Assert.That(realm.ToNumberSlowPath(JsValue.FromString("  0X10 ")), Is.EqualTo(16d));
    }

    [Test]
    public void ToNumberSlowPath_UsesStringConversion()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var value = JsValue.FromString("0xff");
        Assert.That(realm.ToNumberSlowPath(value), Is.EqualTo(255d));
    }

    [Test]
    public void ToPrimitiveSlowPath_PrefersValueOf_ForNumberHint()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var obj = new JsPlainObject(realm);

        var valueOf = new JsHostFunction(realm, static (in info) => { return JsValue.FromInt32(1); }, "valueOf", 0);
        var toString =
            new JsHostFunction(realm, static (in info) => { return JsValue.FromString("x"); }, "toString", 0);
        obj.DefineDataPropertyAtom(realm, AtomTable.IdValueOf, JsValue.FromObject(valueOf), JsShapePropertyFlags.Open);
        obj.DefineDataPropertyAtom(realm, AtomTable.IdToString, JsValue.FromObject(toString),
            JsShapePropertyFlags.Open);

        var primitive = realm.ToPrimitiveSlowPath(JsValue.FromObject(obj), false);
        Assert.That(primitive.IsInt32, Is.True);
        Assert.That(primitive.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void ToPrimitiveSlowPath_FallsBackToToString_WhenValueOfReturnsObject()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var obj = new JsPlainObject(realm);

        var valueOf = new JsHostFunction(realm, (in info) =>
        {
            var r = info.Realm;
            return JsValue.FromObject(new JsPlainObject(r));
        }, "valueOf", 0);
        var toString =
            new JsHostFunction(realm, static (in info) => { return JsValue.FromString("+1"); }, "toString", 0);
        obj.DefineDataPropertyAtom(realm, AtomTable.IdValueOf, JsValue.FromObject(valueOf), JsShapePropertyFlags.Open);
        obj.DefineDataPropertyAtom(realm, AtomTable.IdToString, JsValue.FromObject(toString),
            JsShapePropertyFlags.Open);

        var primitive = realm.ToPrimitiveSlowPath(JsValue.FromObject(obj), false);
        Assert.That(primitive.IsString, Is.True);
        Assert.That(primitive.AsString(), Is.EqualTo("+1"));
    }

    [Test]
    public void ToPrimitiveSlowPath_PropagatesThrowFromValueOf()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var obj = new JsPlainObject(realm);

        var valueOf = new JsHostFunction(realm,
            static (in info) => { throw new JsRuntimeException(JsErrorKind.TypeError, "valueOf boom"); }, "valueOf", 0);
        var toString = new JsHostFunction(realm, static (in info) => { return JsValue.FromString("ignored"); },
            "toString", 0);
        obj.DefineDataPropertyAtom(realm, AtomTable.IdValueOf, JsValue.FromObject(valueOf), JsShapePropertyFlags.Open);
        obj.DefineDataPropertyAtom(realm, AtomTable.IdToString, JsValue.FromObject(toString),
            JsShapePropertyFlags.Open);

        var ex = Assert.Throws<JsRuntimeException>(() =>
            realm.ToPrimitiveSlowPath(JsValue.FromObject(obj), false));
        Assert.That(ex!.Message, Is.EqualTo("valueOf boom"));
    }

    [Test]
    public void ObjectPlusEmptyString_UsesValueOfThenStringify()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        realm.Eval("""
                   var object = {valueOf: function() {return 1}, toString: function() {return 0}};
                   this.r = object + "";
                   this.t = typeof this.r + ":" + this.r;
                   """);
        Assert.That(realm.Global["t"].AsString(), Is.EqualTo("string:1"));
    }

    [Test]
    public void ObjectLiteralValueOfMethod_ReturnsDeclaredValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                ({valueOf: function() {return 1}, toString: function() {return 0}}).valueOf() === 1;
                                """);
        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void ToPrimitiveSlowPath_OnJsObjectLiteral_UsesValueOfFirst()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var objValue = realm.Eval("""
                                  ({valueOf: function() {return 1}, toString: function() {return 0}});
                                  """);
        Assert.That(objValue.TryGetObject(out _), Is.True);

        var prim = realm.ToPrimitiveSlowPath(objValue, false);
        Assert.That(prim.IsInt32, Is.True);
        Assert.That(prim.Int32Value, Is.EqualTo(1));
    }
}
