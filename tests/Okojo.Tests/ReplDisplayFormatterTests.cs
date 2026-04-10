using Okojo.Compiler;
using Okojo.Diagnostics;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ReplDisplayFormatterTests
{
    [Test]
    public void ReplFormatter_PrintsObjectArrayAndCircularLikeNode()
    {
        Assert.That(EvalDisplay("({x:3,y:2});"), Is.EqualTo("""
                                                            {
                                                                x: 3,
                                                                y: 2
                                                            }
                                                            """));
        Assert.That(EvalDisplay("['a',3,3];"), Is.EqualTo("""
                                                          [
                                                              'a',
                                                              3,
                                                              3
                                                          ]
                                                          """));
        Assert.That(EvalDisplay("var array=[2]; array.x=3; array;"), Is.EqualTo("""
            [
                2,
                x: 3
            ]
            """));
        Assert.That(EvalDisplay("o={x:3};"), Is.EqualTo("""
                                                        {
                                                            x: 3
                                                        }
                                                        """));
        Assert.That(EvalDisplay("o={x:3};o.o=o;o;"),
            Is.EqualTo("""
                       <ref *1> {
                           x: 3,
                           o: [Circular *1]
                       }
                       """));
        Assert.That(EvalDisplay("({x:3,y:2});", null), Is.EqualTo("{ x: 3, y: 2 }"));

        static string EvalDisplay(string source, int? indentSize = 4)
        {
            var realm = JsRuntime.Create().DefaultRealm;
            var compiler = new JsCompiler(realm);
            realm.Execute(compiler.Compile(JavaScriptParser.ParseScript(source)));
            return new ReplFormatter(realm, indentSize).Format(realm.Accumulator);
        }
    }
}
