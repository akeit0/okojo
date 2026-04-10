using Okojo.Runtime;

namespace Okojo.Tests;

public class ArithmeticTests
{
    [Test]
    public void TestSubSmi()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("1 / (-0 - 0)");
        Assert.That(result.FastFloat64Value, Is.EqualTo(double.NegativeInfinity));
    }
}
