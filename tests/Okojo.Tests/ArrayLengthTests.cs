using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ArrayLengthTests
{
    [Test]
    public void Array_Constructor_Allows_Uint32_Max_Length_But_Not_Above()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var okMax = new Array(4294967295).length === 4294967295;
            var threw = false;
            try {
              new Array(4294967296);
            } catch (e) {
              threw = e instanceof RangeError;
            }
            okMax && threw;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Array_Length_Assignment_And_DefineProperty_Invalid_Length_Throw_RangeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var assignThrew = false;
            try {
              [].length = 4294967296;
            } catch (e) {
              assignThrew = e instanceof RangeError;
            }

            var defineThrew = false;
            try {
              Object.defineProperty([], "length", { value: -1, configurable: true });
            } catch (e) {
              defineThrew = e instanceof RangeError;
            }

            assignThrew && defineThrew;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Array_Length_Shrink_Deletes_Sparse_High_Index_Without_Linear_Countdown()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
            var x = [0, 1, 2];
            x[4294967294] = 4294967294;
            x.length = 2;
            x[0] === 0 &&
            x[1] === 1 &&
            x[2] === undefined &&
            x[4294967294] === undefined;
            """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
