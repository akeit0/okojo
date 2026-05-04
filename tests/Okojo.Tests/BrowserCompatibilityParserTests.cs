using Okojo.Parsing;

namespace Okojo.Tests;

public sealed class BrowserCompatibilityParserTests
{
    [Test]
    public void ParseScript_Allows_Of_As_Function_Name()
    {
        Assert.That(() => JavaScriptParser.ParseScript("function of(a) { return a; }"), Throws.Nothing);
    }

    [Test]
    public void ParseScript_Allows_Mixed_Object_Binding_Declarators()
    {
        Assert.That(() => JavaScriptParser.ParseScript("""
            function f(d) {
              const k = d.width, p = d.height, {aa:n, V:m, D:l, hb:t} = d.value;
              return n + m + l + t + k + p;
            }
            """), Throws.Nothing);
    }
}
