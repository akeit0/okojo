using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;
using Okojo.JavaScript.Execution;

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

    [Test]
    public void TestMixedNumberArithmeticAfterInt32Overflow()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval(
            """
            function t() {
                let s = 2147483647;
                let i = 0;
                while (i < 3) {
                    s = s + i;
                    i = i + 1;
                }
                return s;
            }
            t();
            """
        );

        Assert.That(result.IsFloat64, Is.True);
        Assert.That(result.NumberValue, Is.EqualTo(2147483650d));
    }
}
