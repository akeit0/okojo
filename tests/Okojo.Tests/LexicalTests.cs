using Okojo.Runtime;

namespace Okojo.Tests;

public class LexicalTests
{
    [Test]
    public void Test1()
    {
        var source = """
                     function t() {
                         let x = 1;
                         let f = function f() {
                             return x;
                         }
                         x = 2;
                         return f;
                     }
                     t()();
                     """;
        var result = JsRuntime.Create().Eval(source);
        Assert.That(result.IsInt32, Is.True);
        Assert.That(result.Int32Value, Is.EqualTo(2));
    }
}
