using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class SpreadCallFeatureTests
{
    [Test]
    public void Member_Call_With_Spread_Preserves_Receiver_And_Arguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const target = {
              base: 10,
              add(a, b, c) {
                return this.base + a + b + c;
              }
            };

            const values = [1, 2, 3];
            target.add(...values) === 16;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Call_With_Spread_On_ArrayLike_Helper_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const helper = (ta, ...rest) => {
              Array.prototype.copyWithin.call(ta, ...rest);
              return ta.join(",");
            };

            helper([0, 1, 2, 3], 0, 1, undefined) === "1,2,3,3";
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void New_With_Spread_Passes_Expanded_Arguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            class Box {
              constructor(a, b, c) {
                this.total = a + b + c;
              }
            }

            const values = [1, 2, 3];
            new Box(...values).total === 6;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Super_Call_With_Spread_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            class Base {
              constructor(a, b) {
                this.sum = a + b;
              }
            }

            class Derived extends Base {
              constructor(...args) {
                super(...args);
              }
            }

            new Derived(2, 5).sum === 7;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Nested_Subclass_Super_Call_With_Spread_Captures_Outer_Parameter()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            const helper = {
              run(construct, constructArgs) {
                class Derived extends construct {
                  constructor() {
                    super(...constructArgs);
                  }
                }

                return new Derived();
              }
            };

            class Base {
              constructor(...args) {
                this.args = args;
              }
            }

            const result = helper.run(Base, [1, 2, 3]);
            result.args.length === 3 &&
              result.args[0] === 1 &&
              result.args[1] === 2 &&
              result.args[2] === 3;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Spread_Call_Throws_For_NonIterable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            let threw = false;
            try {
              (function() {})(...123);
            } catch (e) {
              threw = e instanceof TypeError;
            }
            threw;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
