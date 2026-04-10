using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class TryCatchTests
{
    [Test]
    public void CatchWithoutBinding_IsAccepted_AndExecutesBody()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   try {
                                                                       throw 7;
                                                                   } catch {
                                                                       x = 1;
                                                                   }
                                                                   x;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void CatchWithoutBinding_WithFinally_IsAccepted()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   try {
                                                                       throw 1;
                                                                   } catch {
                                                                       x = 2;
                                                                   } finally {
                                                                       x = x + 3;
                                                                   }
                                                                   x;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(5));
    }

    [Test]
    public void CatchObjectPattern_BindsRenamedProperty()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var ranCatch = false;
                                                                   try {
                                                                     throw { x: 23 };
                                                                   } catch ({ x: y }) {
                                                                     ranCatch = y === 23;
                                                                   }
                                                                   ranCatch;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void CatchArrayPattern_RestBinding_CopiesThrownArray()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var values = [1, 2, 3];
                                                                   var result = false;
                                                                   try {
                                                                     throw values;
                                                                   } catch ([...x]) {
                                                                     result = Array.isArray(x) && x.length === 3 && x[0] === 1 && x[1] === 2 && x[2] === 3 && x !== values;
                                                                   }
                                                                   result;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void CatchEmptyObjectPattern_RequiresObjectCoercible()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   try {
                                                                     throw null;
                                                                   } catch ({}) {
                                                                     0;
                                                                   }
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex, Is.Not.Null);
    }

    [Test]
    public void CatchArrayPattern_Elision_AdvancesIterator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var first = 0;
                                                                   var second = 0;
                                                                   function* g() {
                                                                     first += 1;
                                                                     yield;
                                                                     second += 1;
                                                                   };

                                                                   var ranCatch = false;
                                                                   try {
                                                                     throw g();
                                                                   } catch ([,]) {
                                                                     ranCatch = first === 1 && second === 0;
                                                                   }

                                                                   ranCatch;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void CatchArrayPattern_RestEmptyArray_ConsumesIterator()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var iterations = 0;
                                                                   var iter = function*() {
                                                                     iterations += 1;
                                                                   }();

                                                                   var ranCatch = false;
                                                                   try {
                                                                     throw iter;
                                                                   } catch ([...[]]) {
                                                                     ranCatch = iterations === 1;
                                                                   }

                                                                   ranCatch;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ThrowStatement_IsCaught_WithValue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   try {
                                                                       throw 7;
                                                                   } catch (e) {
                                                                       x = e;
                                                                   }
                                                                   x;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void TypeError_FromNotCallable_IsCaught()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   try {
                                                                       let a = 1;
                                                                       a();
                                                                   } catch (e) {
                                                                       x = 1;
                                                                   }
                                                                   x;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void Throw_InCalledFunction_IsCaught_ByCallerTry()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function f() { throw 9; }
                                                                   let x = 0;
                                                                   try {
                                                                       f();
                                                                   } catch (e) {
                                                                       x = e;
                                                                   }
                                                                   x;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(9));
    }

    [Test]
    public void TryFinally_RunsFinally_OnNormalPath()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   try {
                                                                       x = 1;
                                                                   } finally {
                                                                       x = 2;
                                                                   }
                                                                   x;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void TryFinally_Rethrows_AfterFinally()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   try {
                                                                       try {
                                                                           throw 1;
                                                                       } finally {
                                                                           x = 2;
                                                                       }
                                                                   } catch (e) {
                                                                       x = x + e;
                                                                   }
                                                                   x;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(3));
    }

    [Test]
    public void TryCatchFinally_RunsFinally_AfterCatch()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   try {
                                                                       throw 1;
                                                                   } catch (e) {
                                                                       x = e;
                                                                   } finally {
                                                                       x = x + 1;
                                                                   }
                                                                   x;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void CatchBinding_DoesNotLeak_ToOuterScope()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let e = 1;
                                                                   try {
                                                                       throw 9;
                                                                   } catch (e) {
                                                                       e = e + 1;
                                                                   }
                                                                   e;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void CatchBinding_IsNotVisible_InFinally()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 1;
                                                                   let y = 0;
                                                                   try {
                                                                       throw 2;
                                                                   } catch (x) {
                                                                       y = x;
                                                                   } finally {
                                                                       x = x + 1;
                                                                   }
                                                                   x * 10 + y;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(22));
    }

    [Test]
    public void CatchBinding_CanBeCaptured_ByNestedFunction()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let f = null;
                                                                   try {
                                                                       throw 7;
                                                                   } catch (e) {
                                                                       f = function () { return e; };
                                                                   }
                                                                   f();
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void TryFinally_ReturnInTry_RunsFinally_ThenReturns()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   function f() {
                                                                       try {
                                                                           return 3;
                                                                       } finally {
                                                                           x = 4;
                                                                       }
                                                                   }
                                                                   f();
                                                                   x * 10 + 3;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(43));
    }

    [Test]
    public void TryFinally_ReturnInFinally_OverridesTryReturn()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function f() {
                                                                       try {
                                                                           return 1;
                                                                       } finally {
                                                                           return 2;
                                                                       }
                                                                   }
                                                                   f();
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(2));
    }

    [Test]
    public void TryCatchFinally_RuntimeThrowInCatch_StillRunsFinally()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   try {
                                                                       try {
                                                                           throw 1;
                                                                       } catch (e) {
                                                                           let a = 1;
                                                                           a(); // runtime TypeError
                                                                       } finally {
                                                                           x = 9;
                                                                       }
                                                                   } catch (err) {
                                                                   }
                                                                   x;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(9));
    }

    [Test]
    public void NestedTryFinally_OuterFinallyRunsOnInnerReturn()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   function f() {
                                                                       try {
                                                                           try {
                                                                               return 1;
                                                                           } finally {
                                                                               x = x + 1;
                                                                           }
                                                                       } finally {
                                                                           x = x + 10;
                                                                       }
                                                                   }
                                                                   f();
                                                                   x;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(11));
    }

    [Test]
    public void BreakInsideTryFinally_RunsFinally()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   while (true) {
                                                                       try {
                                                                           break;
                                                                       } finally {
                                                                           x = x + 1;
                                                                       }
                                                                   }
                                                                   x;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void ContinueInsideTryFinally_RunsFinallyEachIteration()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 0;
                                                                   for (let i = 0; i < 4; i++) {
                                                                       try {
                                                                           continue;
                                                                       } finally {
                                                                           x = x + 1;
                                                                       }
                                                                   }
                                                                   x;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(4));
    }
}
