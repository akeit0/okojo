namespace Okojo.Tests;

public class JsValueTests
{
    [Test]
    public void TestDouble()
    {
        var val = new JsValue(123.456);
        Assert.Multiple(() =>
        {
            Assert.That(val.IsNumber, Is.True, "Should be Number");
            Assert.That(val.IsFloat64, Is.True, "Should be Float64");
            Assert.That(val.IsInt32, Is.False, "Should not be Int32");
            Assert.That(val.Float64Value, Is.EqualTo(123.456));
            Assert.That(val.NumberValue, Is.EqualTo(123.456));
        });
    }

    [Test]
    public void TestInt32()
    {
        var val = JsValue.FromInt32(42);
        Assert.Multiple(() =>
        {
            Assert.That(val.Tag, Is.EqualTo(Tag.JsTagInt));
            Assert.That(val.IsNumber, Is.True, "Should be Number");
            Assert.That(val.IsInt32, Is.True, "Should be Int32");
            Assert.That(val.IsFloat64, Is.False, "Should not be Float64");
            Assert.That(val.Int32Value, Is.EqualTo(42));
            Assert.That(val.NumberValue, Is.EqualTo(42.0));
        });
    }

    [Test]
    public void TestConstants()
    {
        Assert.Multiple(() =>
        {
            Assert.That(JsValue.Undefined.IsUndefined, Is.True);
            Assert.That(JsValue.Undefined.IsNumber, Is.False);
            Assert.That(JsValue.True.IsTrue, Is.True);
            Assert.That(JsValue.False.IsFalse, Is.True);
            Assert.That(JsValue.True.IsBool, Is.True);
            Assert.That(JsValue.False.IsBool, Is.True);
            Assert.That(JsValue.Null.IsNull, Is.True);
            Assert.That(JsValue.Null.IsNumber, Is.False);
            Assert.That(JsValue.True.IsBool, Is.True);
            Assert.That(JsValue.True.IsNumber, Is.False);
            Assert.That(JsValue.True.U & 1, Is.EqualTo(1UL));
            Assert.That(JsValue.False.U & 1, Is.EqualTo(0UL));
            Assert.That(JsValue.NaN.IsFloat64, Is.True);
            Assert.That(JsValue.NaN.IsNaN, Is.True);
            Assert.That(JsValue.NaN.IsNumber, Is.True);
            Assert.That(JsValue.NaN.Float64Value == JsValue.NaN.Float64Value, Is.False);
        });
    }

    [Test]
    public void TestString()
    {
        var val = JsValue.FromString("Hello");
        Assert.Multiple(() =>
        {
            Assert.That(val.IsString, Is.True);
            Assert.That(val.IsNumber, Is.False);
            Assert.That(val.Obj, Is.EqualTo("Hello"));
        });
    }
}
