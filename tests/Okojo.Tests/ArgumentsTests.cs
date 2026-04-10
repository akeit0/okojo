using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ArgumentsTests
{
    [Test]
    public void Arguments_IsMapped_ForNonStrictSimpleParameters()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function f(a) {
                                                                     arguments[0] = 2;
                                                                     var first = a === 2;
                                                                     a = 3;
                                                                     var second = arguments[0] === 3;
                                                                     return first && second && arguments.length === 1;
                                                                   }
                                                                   f(1);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_IsUnmapped_ForNonSimpleParameters()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function f(a = 10) {
                                                                     arguments[0] = 2;
                                                                     var first = a === 1;
                                                                     a = 3;
                                                                     var second = arguments[0] === 2;
                                                                     return first && second && arguments.length === 1;
                                                                   }
                                                                   f(1);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_AtTopLevel_ThrowsReferenceError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   try { arguments; false; }
                                                                   catch (e) { e instanceof ReferenceError; }
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_Object_Remains_Visible_In_Parameter_Initializer_When_Body_Shadows_Arguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var args;
                                                                   var f = function (x = args = arguments) {
                                                                     let arguments;
                                                                   };
                                                                   f();
                                                                   typeof args === 'object' && args.length === 0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_Object_Remains_Visible_In_Parameter_Initializer_When_Function_Body_Declares_Arguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var fnArgs;
                                                                   function f(x = fnArgs = arguments) {
                                                                     function arguments() {}
                                                                   }

                                                                   var genArgs;
                                                                   function* g(x = genArgs = arguments) {
                                                                     function arguments() {}
                                                                   }

                                                                   f();
                                                                   g();
                                                                   typeof fnArgs === 'object' && fnArgs.length === 0 &&
                                                                     typeof genArgs === 'object' && genArgs.length === 0;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_Remains_Available_In_Function_With_Destructuring_Declaration_Initializer()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function outer() {
                                                                     const wrapped = function () {
                                                                       const { value } = ({ value: arguments.length });
                                                                       return value;
                                                                     };
                                                                     return wrapped(1, 2, 3, 4);
                                                                   }

                                                                   outer() === 4;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_DeleteThenDefineProperties_RecreatesElementWithDescriptor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var arg;
                                                                   (function(a, b, c) { arg = arguments; }(0, 1, 2));
                                                                   delete arg[0];
                                                                   Object.defineProperties(arg, {
                                                                     "0": {
                                                                       value: 10,
                                                                       writable: true,
                                                                       enumerable: true,
                                                                       configurable: true
                                                                     }
                                                                   });
                                                                   var d = Object.getOwnPropertyDescriptor(arg, "0");
                                                                   d.value === 10 && d.writable === true && d.enumerable === true && d.configurable === true;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_DefineProperty_DataDescriptor_SyncsThenCanUnmap()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   (function(a, b, c) {
                                                                     Object.defineProperty(arguments, "0", {
                                                                       value: 20,
                                                                       writable: false,
                                                                       enumerable: false,
                                                                       configurable: false
                                                                     });
                                                                     var d = Object.getOwnPropertyDescriptor(arguments, "0");
                                                                     return "a=" + a + ",v=" + d.value + ",w=" + d.writable + ",e=" + d.enumerable + ",c=" + d.configurable;
                                                                   }(0,1,2));
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("a=20,v=20,w=false,e=false,c=false"));
    }

    [Test]
    public void Arguments_Length_Is_Ordinary_Writable_Own_Data_Property()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   (function() {
                                                                     arguments.length = "something different";
                                                                     var d = Object.getOwnPropertyDescriptor(arguments, "length");
                                                                     return arguments.length === "something different" &&
                                                                            d.value === "something different" &&
                                                                            d.writable === true &&
                                                                            d.enumerable === false &&
                                                                            d.configurable === true;
                                                                   }());
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_NonConfigurable_Mapped_Index_Stays_Synced_With_Parameter()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   (function(a) {
                                                                     Object.defineProperty(arguments, "0", { configurable: false });
                                                                     a = 2;
                                                                     var d = Object.getOwnPropertyDescriptor(arguments, "0");
                                                                     return arguments[0] === 2 &&
                                                                            d.value === 2 &&
                                                                            d.writable === true &&
                                                                            d.enumerable === true &&
                                                                            d.configurable === false;
                                                                   }(1));
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_Mapped_Index_Survives_Failed_Redefine_Both_Direct_And_Arrow_Callback_Paths()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var direct = (function(a) {
                                                                     Object.defineProperty(arguments, "0", { configurable: false });
                                                                     try { Object.defineProperty(arguments, "0", { configurable: true }); } catch (e) {}
                                                                     a = 2;
                                                                     return arguments[0] === 2;
                                                                   }(0));

                                                                   var viaArrow = (function(a) {
                                                                     function expectTypeError(fn) {
                                                                       try { fn(); }
                                                                       catch (e) { return e instanceof TypeError; }
                                                                       return false;
                                                                     }

                                                                     Object.defineProperty(arguments, "0", { configurable: false });
                                                                     var threw = expectTypeError(() => Object.defineProperty(arguments, "0", { configurable: true }));
                                                                     a = 2;
                                                                     return threw && arguments[0] === 2;
                                                                   }(0));

                                                                   direct + "," + viaArrow;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsString, Is.True);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true,true"));
    }

    [Test]
    public void Strict_And_Unmapped_Arguments_Callee_Use_Realm_ThrowTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function strictFn() {
                                                                     "use strict";
                                                                     return Object.getOwnPropertyDescriptor(arguments, "callee");
                                                                   }
                                                                   function nonSimple(a = 0) {
                                                                     return Object.getOwnPropertyDescriptor(arguments, "callee");
                                                                   }

                                                                   var strictDesc = strictFn();
                                                                   var nonSimpleDesc = nonSimple();
                                                                   var threw = false;
                                                                   try { strictDesc.get(); } catch (e) { threw = e instanceof TypeError; }

                                                                   strictDesc.get === strictDesc.set &&
                                                                   strictDesc.get === nonSimpleDesc.get &&
                                                                   strictDesc.configurable === false &&
                                                                   strictDesc.enumerable === false &&
                                                                   typeof strictDesc.get === "function" &&
                                                                   strictDesc.get.name === "" &&
                                                                   strictDesc.get.length === 0 &&
                                                                   Object.isFrozen(strictDesc.get) &&
                                                                   threw;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_DefineProperty_Failure_Does_Not_Corrupt_Mapped_Or_Accessor_Indices()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   (function(a) {
                                                                     Object.defineProperty(arguments, "0", { configurable: false });
                                                                     try {
                                                                       Object.defineProperty(arguments, "0", { configurable: true });
                                                                     } catch (e) {}

                                                                     a = 2;
                                                                     var firstOk = arguments[0] === 2;

                                                                     Object.defineProperty(arguments, "1", {
                                                                       get: () => 3,
                                                                       configurable: false
                                                                     });

                                                                     var threwValue = false;
                                                                     try {
                                                                       Object.defineProperty(arguments, "1", { value: "foo" });
                                                                     } catch (e) {
                                                                       threwValue = e instanceof TypeError;
                                                                     }

                                                                     var secondOk = arguments[1] === 3;

                                                                     var threwDelete = false;
                                                                     try {
                                                                       Function("arg", "\"use strict\"; return delete arg[1];")(arguments);
                                                                     } catch (e) {
                                                                       threwDelete = e instanceof TypeError;
                                                                     }

                                                                     return firstOk && threwValue && secondOk && threwDelete;
                                                                   }(0));
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_Remain_Mapped_When_Later_Strict_Arrow_Uses_Arguments()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   (function(a) {
                                                                     Object.defineProperty(arguments, "0", { configurable: false });
                                                                     try {
                                                                       Object.defineProperty(arguments, "0", { configurable: true });
                                                                     } catch (e) {
                                                                     }

                                                                     a = 2;

                                                                     var later = () => {
                                                                       "use strict";
                                                                       delete arguments[0];
                                                                     };

                                                                     return arguments[0] === 2;
                                                                   }(0));
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_ObjectFreeze_MakesIsFrozenTrue()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var argObj = (function() { return arguments; }(1,2,3));
                                                                   Object.freeze(argObj);
                                                                   Object.isFrozen(argObj);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_IsIterable_ForSpread()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   (function() {
                                                                     var arr = [...arguments];
                                                                     var desc = Object.getOwnPropertyDescriptor(arguments, Symbol.iterator);
                                                                     return arr.length === 3 &&
                                                                            arr[0] === 1 &&
                                                                            arr[1] === 2 &&
                                                                            arr[2] === 3 &&
                                                                            desc.enumerable === false &&
                                                                            desc.writable === true &&
                                                                            desc.configurable === true;
                                                                   }(1, 2, 3));
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_HasSymbolIterator_OwnProperty()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   (function() {
                                                                     var d = Object.getOwnPropertyDescriptor(arguments, Symbol.iterator);
                                                                     return typeof arguments[Symbol.iterator] === "function" &&
                                                                            d.value === Array.prototype.values &&
                                                                            d.enumerable === false &&
                                                                            d.writable === true &&
                                                                            d.configurable === true;
                                                                   }(1));
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Arguments_Spread_UsesCurrentFunctionBinding_InsideNestedNonArrowFunctions()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   (function() {
                                                                     return function() {
                                                                       var obj = {
                                                                         get then() {
                                                                           return function(resolve) {
                                                                             return resolve;
                                                                           };
                                                                         }
                                                                       };

                                                                       return [...arguments][0] === "ok" && typeof obj.then === "function";
                                                                     }("ok");
                                                                   }());
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void SloppyVarArguments_Initializer_OverridesArgumentsBinding()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const answer = "Answer to Life, the Universe, and Everything";
                                                                   function f() {
                                                                     var arguments = answer;
                                                                     return arguments;
                                                                   }
                                                                   f(42, 42, 42) === answer;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void SloppyVarArguments_BeforeAssignment_StillExposesArgumentsObject()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function f() {
                                                                     return typeof arguments;
                                                                     var arguments = 1;
                                                                   }
                                                                   f(42, 42, 42) === "object";
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
