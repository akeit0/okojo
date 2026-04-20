using System.Text;
using Okojo.Bytecode;
using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class DeleteTests
{
    private static string GetRepoRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    [Test]
    public void Delete_ArrayIndex_CreatesHole_AndReturnsTrue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let a = [1, 2, 3];
                                                                   let r = delete a[1];
                                                                   if (!r) 0;
                                                                   else if (a[1] !== undefined) 0;
                                                                   else if (1 in a) 0;
                                                                   else 1;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void Delete_ArrayLength_ReturnsFalse_And_Preserves_Length()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var result = realm.Eval("""
                                var a = [1, 2, 3];
                                var deleted = delete a.length;
                                deleted === false && a.length === 3;
                                """);

        Assert.That(result.IsTrue, Is.True);
    }

    [Test]
    public void Delete_Computed_Member_On_Undefined_Throws_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var baseValue = undefined;
                                                                   delete baseValue[0];
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
    }

    [Test]
    public void Delete_Nested_Member_Reference_With_Undefined_Base_Throws_TypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   delete Object[0][0];
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
    }

    [Test]
    public void Delete_Compiler_Emits_DeleteKeyedProperty_RuntimeCall()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t(a, i) {
                                                                       return delete a[i];
                                                                   }
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var code = t.Script.Bytecode;

        var sawDeleteRuntime = false;
        for (var i = 0; i + 3 < code.Length; i++)
        {
            if ((JsOpCode)code[i] != JsOpCode.CallRuntime)
                continue;
            if ((RuntimeId)code[i + 1] == RuntimeId.DeleteKeyedProperty)
            {
                sawDeleteRuntime = true;
                break;
            }
        }

        Assert.That(sawDeleteRuntime, Is.True);
    }

    [Test]
    public void Delete_StrictMemberDelete_Compiler_Emits_Strict_RuntimeCall()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t(a, i) {
                                                                       "use strict";
                                                                       return delete a[i];
                                                                   }
                                                                   """));

        var t = script.ObjectConstants.OfType<JsBytecodeFunction>().Single(f => f.Name == "t");
        var code = t.Script.Bytecode;

        var sawDeleteRuntime = false;
        for (var i = 0; i + 3 < code.Length; i++)
        {
            if ((JsOpCode)code[i] != JsOpCode.CallRuntime)
                continue;
            if ((RuntimeId)code[i + 1] == RuntimeId.DeleteKeyedPropertyStrict)
            {
                sawDeleteRuntime = true;
                break;
            }
        }

        Assert.That(sawDeleteRuntime, Is.True);
    }

    [Test]
    public void Delete_StrictMemberDelete_Throws_When_RuntimeDeleteReturnsFalse()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function t() {
                                                                     "use strict";
                                                                     const target = new Proxy({}, {
                                                                       deleteProperty() { return false; }
                                                                     });
                                                                     delete target.x;
                                                                   }
                                                                   try {
                                                                     t();
                                                                     false;
                                                                   } catch (e) {
                                                                     e instanceof TypeError;
                                                                   }
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Delete_CatchBinding_ReturnsFalse_And_Binding_DoesNotEscapeCatch()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let deleteResult = true;
                                                                   let sawValue = false;
                                                                   let escaped = true;
                                                                   try {
                                                                     throw "catchme";
                                                                   } catch (e) {
                                                                     deleteResult = delete e;
                                                                     sawValue = e === "catchme";
                                                                   }
                                                                   try {
                                                                     e;
                                                                   } catch (err) {
                                                                     escaped = false;
                                                                   }
                                                                   deleteResult === false && sawValue && escaped === false;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Delete_StrictScript_NestedFunctionDeleteTypedArrayIndex_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   "use strict";
                                                                   function run() {
                                                                     return (function () {
                                                                       const sample = new Uint8Array(1);
                                                                       delete sample["0"];
                                                                     })();
                                                                   }
                                                                   try {
                                                                     run();
                                                                     false;
                                                                   } catch (e) {
                                                                     e instanceof TypeError;
                                                                   }
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Delete_StrictScript_ArrowDeleteTypedArrayIndex_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   "use strict";
                                                                   const sample = new Uint8Array(1);
                                                                   try {
                                                                     (() => { delete sample["0"]; })();
                                                                     false;
                                                                   } catch (e) {
                                                                     e instanceof TypeError;
                                                                   }
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Delete_StrictTypedArrayExactStyle_ThrowsForInBoundsAndReturnsTrueForOutOfBounds()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   "use strict";
                                                                   const TypedArray = Object.getPrototypeOf(Int8Array);
                                                                   let proto = TypedArray.prototype;
                                                                   let descriptorGetterThrows = {
                                                                     configurable: true,
                                                                     get() {
                                                                       throw new Error("OrdinaryGet was called!");
                                                                     }
                                                                   };
                                                                   Object.defineProperties(proto, {
                                                                     ["-1"]: descriptorGetterThrows,
                                                                     ["1"]: descriptorGetterThrows,
                                                                   });

                                                                   let sample = new Float64Array(1);
                                                                   let ok1 = delete sample["-1"] === true;
                                                                   let ok2 = delete sample[-1] === true;
                                                                   let threw1 = false;
                                                                   try { delete sample["0"]; } catch (e) { threw1 = e instanceof TypeError; }
                                                                   let threw2 = false;
                                                                   try { delete sample[0]; } catch (e) { threw2 = e instanceof TypeError; }
                                                                   let ok3 = delete sample["1"] === true;
                                                                   let ok4 = delete sample[1] === true;
                                                                   ok1 && ok2 && threw1 && threw2 && ok3 && ok4;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Delete_StrictScript_FunctionExpressionCallbackDeleteTypedArrayIndex_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   "use strict";
                                                                   function invoke(f) { return f(); }
                                                                   const sample = new Float64Array(1);
                                                                   try {
                                                                     invoke(function () { delete sample["0"]; });
                                                                     false;
                                                                   } catch (e) {
                                                                     e instanceof TypeError;
                                                                   }
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Delete_StrictScript_HigherOrderTypedArrayCallbackDelete_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   "use strict";
                                                                   function testWithTypedArrayConstructors(f) {
                                                                     function makePassthrough(TA, primitiveOrIterable) { return primitiveOrIterable; }
                                                                     f(Float64Array, makePassthrough.bind(undefined, Float64Array));
                                                                   }
                                                                   try {
                                                                     testWithTypedArrayConstructors(function(TA, makeCtorArg) {
                                                                       let sample = new TA(makeCtorArg(1));
                                                                       delete sample["0"];
                                                                     });
                                                                     false;
                                                                   } catch (e) {
                                                                     e instanceof TypeError;
                                                                   }
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Delete_StrictScript_AssertThrowsCallbackDeleteTypedArrayIndex_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   "use strict";
                                                                   var assert = {
                                                                     throws(expectedErrorConstructor, func) {
                                                                       try {
                                                                         func();
                                                                       } catch (thrown) {
                                                                         return thrown && thrown.constructor === expectedErrorConstructor;
                                                                       }
                                                                       return false;
                                                                     }
                                                                   };
                                                                   const sample = new Float64Array(1);
                                                                   assert.throws(TypeError, () => {
                                                                     delete sample["0"];
                                                                   });
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Delete_PrivateIn_ThrowsSyntaxError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var ex = Assert.Throws<JsParseException>(() =>
            JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                          class C {
                                                            m() { delete #x in this; }
                                                          }
                                                          """)));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("Unexpected identifier '#x'"));
    }

    [Test]
    public void Delete_SuperComputed_UninitializedThis_ThrowsBeforeKeyEvaluation()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   class Base {
                                                                     constructor() { throw new Error("base ctor should not run"); }
                                                                   }
                                                                   class Derived extends Base {
                                                                     constructor() {
                                                                       delete super[(super(), 0)];
                                                                     }
                                                                   }
                                                                   try {
                                                                     new Derived();
                                                                     0;
                                                                   } catch (e) {
                                                                     e instanceof ReferenceError;
                                                                   }
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Delete_SuperComputed_DoesNotToPropertyKey()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var obj = {
                                                                     m() {
                                                                       var key = { toString() { throw new Error("ToPropertyKey performed"); } };
                                                                       delete super[key];
                                                                     }
                                                                   };
                                                                   try {
                                                                     obj.m();
                                                                     0;
                                                                   } catch (e) {
                                                                     e instanceof ReferenceError;
                                                                   }
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
