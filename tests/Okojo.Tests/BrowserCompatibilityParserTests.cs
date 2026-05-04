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

    [Test]
    public void ParseScript_Allows_Object_Binding_Declarator_Before_Comma()
    {
        Assert.That(() => JavaScriptParser.ParseScript("""
            function Qo(a,b,c,d,e){
              var {J:f,I:g}=ek(247,()=>Oo(a,b,c,d,e)),h=g===!0,k=Yd(d.style.width),p=Yd(d.style.height);
              return h ? f : k + p;
            }
            """), Throws.Nothing);
    }
}
