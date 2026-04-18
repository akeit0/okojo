using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ArrowFunctionTests
{
    [Test]
    public void ArrowFunction_ExpressionBody_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let f = (x) => x + x;
                                                                   f(3);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(6));
    }

    [Test]
    public void ArrowFunction_LexicalThis_IsCapturedAtCreation()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function make() {
                                                                       let o = { x: 7 };
                                                                       o.m = function () {
                                                                           let f = () => this.x;
                                                                           return f();
                                                                       };
                                                                       return o.m();
                                                                   }
                                                                   make();
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(7));
    }

    [Test]
    public void ArrowFunction_LexicalThis_IsNotOverriddenByApply()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function F() {
                                                                     this.af = _ => this;
                                                                   }
                                                                   var usurper = {};
                                                                   var f = new F();
                                                                   f.af() === f &&
                                                                   f.af.apply(usurper) === f &&
                                                                   f.af.call(usurper) === f &&
                                                                   f.af.bind(usurper)() === f;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrowFunction_IsNotConstructor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   let f = () => 1;
                                                                   new f();
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Message, Does.Contain("constructor"));
    }

    [Test]
    public void ArrowFunction_LexicalNewTarget_IsCaptured()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var functionInvocationCount = 0;
                                                                   var newInvocationCount = 0;
                                                                   function F() {
                                                                     if ((_ => new.target)() !== undefined) {
                                                                       newInvocationCount++;
                                                                     }
                                                                     functionInvocationCount++;
                                                                   }
                                                                   F();
                                                                   new F();
                                                                   functionInvocationCount === 2 && newInvocationCount === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrowFunction_ReturnedClosure_Preserves_LexicalNewTarget()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   function F() {
                                                                     this.af = _ => {
                                                                       if (new.target) {
                                                                         return 1;
                                                                       }
                                                                       return 2;
                                                                     };
                                                                   }
                                                                   var f = new F();
                                                                   f.af() === 1;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrowFunction_LexicalSuperProperty_FromDerivedConstructor_Works()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var count = 0;
                                                                   class A {
                                                                     constructor() {
                                                                       count++;
                                                                     }
                                                                     increment() {
                                                                       count++;
                                                                     }
                                                                   }
                                                                   class B extends A {
                                                                     constructor() {
                                                                       super();
                                                                       (_ => super.increment())();
                                                                     }
                                                                   }
                                                                   new B();
                                                                   count === 2;
                                                                   """, true, false));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrowFunction_LexicalSuperCall_FromDerivedConstructor_ThrowsReferenceError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var count = 0;
                                                                   class A {
                                                                     constructor() {
                                                                       count++;
                                                                     }
                                                                   }
                                                                   class B extends A {
                                                                     constructor() {
                                                                       super();
                                                                       this.af = _ => super();
                                                                     }
                                                                   }
                                                                   var b = new B();
                                                                   var sawReferenceError = false;
                                                                   try {
                                                                     b.af();
                                                                   } catch (error) {
                                                                     sawReferenceError = error instanceof ReferenceError;
                                                                   }
                                                                   sawReferenceError && count === 2;
                                                                   """, true, true));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrowFunction_LexicalSuperCall_FromImmediatelyInvokedArrow_CallsOnce()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var script = JsCompiler.Compile(realm, JavaScriptParser.ParseScript("""
                                                                   var count = 0;

                                                                   class A {
                                                                     constructor() {
                                                                       count++;
                                                                     }
                                                                   }

                                                                   class B extends A {
                                                                     constructor() {
                                                                       (_ => super())();
                                                                     }
                                                                   }

                                                                   new B();
                                                                   count === 1;
                                                                   """, true, true));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
