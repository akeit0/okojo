using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class FunctionOwnPropertiesTests
{
    [Test]
    public void Function_OwnProperties_LengthNamePrototype_AndPrototypeConstructor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function f(x){ return x; }
                                                                   const names = Object.getOwnPropertyNames(f);
                                                                   (names.length === 3) &&
                                                                   (names[0] === "length") &&
                                                                   (names[1] === "name") &&
                                                                   (names[2] === "prototype") &&
                                                                   (f.length === 1) &&
                                                                   (f.prototype.constructor === f);
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Function_OwnPropertyDescriptors_Have_Expected_Flags()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function sample(alpha, beta) {}
                                                                   var lengthDesc = Object.getOwnPropertyDescriptor(sample, "length");
                                                                   var nameDesc = Object.getOwnPropertyDescriptor(sample, "name");
                                                                   var prototypeDesc = Object.getOwnPropertyDescriptor(sample, "prototype");

                                                                   lengthDesc.value === 2 &&
                                                                   lengthDesc.writable === false &&
                                                                   lengthDesc.enumerable === false &&
                                                                   lengthDesc.configurable === true &&
                                                                   nameDesc.value === "sample" &&
                                                                   nameDesc.writable === false &&
                                                                   nameDesc.enumerable === false &&
                                                                   nameDesc.configurable === true &&
                                                                   prototypeDesc.value === sample.prototype &&
                                                                   prototypeDesc.value.constructor === sample &&
                                                                   prototypeDesc.writable === true &&
                                                                   prototypeDesc.enumerable === false &&
                                                                   prototypeDesc.configurable === false;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ArrowFunction_OwnProperties_HasNoPrototype()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var a = (x) => x;
                                                                   const names = Object.getOwnPropertyNames(a);
                                                                   (names.length === 2) &&
                                                                   (names[0] === "length") &&
                                                                   (names[1] === "name") &&
                                                                   (a.length === 1) &&
                                                                   (a.prototype === undefined);
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Function_Prototype_Can_Be_Replaced_Before_First_Read()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function F() {}
                                                                   var replacement = { marker: 1 };
                                                                   Object.defineProperty(F, "prototype", {
                                                                     value: replacement,
                                                                     writable: false,
                                                                     enumerable: false,
                                                                     configurable: false
                                                                   });

                                                                   var desc = Object.getOwnPropertyDescriptor(F, "prototype");
                                                                   desc.value === replacement &&
                                                                   desc.value.marker === 1 &&
                                                                   Object.prototype.hasOwnProperty.call(replacement, "constructor") === false &&
                                                                   desc.writable === false &&
                                                                   desc.enumerable === false &&
                                                                   desc.configurable === false;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Function_Length_UsesExpectedArgumentCount_WithRestAndDefaults()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   function af(...a) {}
                                                                   function bf(a, ...b) {}
                                                                   function cf(a, b = 1, c) {}
                                                                   (af.length === 0) && (bf.length === 1) && (cf.length === 1);
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BuiltIn_Function_Name_And_Length_Can_Be_Reconfigured_When_Installed_As_Real_Properties()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var nameDesc = Object.getOwnPropertyDescriptor(Proxy, "name");
                                                                   var lengthDesc = Object.getOwnPropertyDescriptor(Proxy, "length");
                                                                   var deletedName = delete Proxy.name;
                                                                   var deletedLength = delete Proxy.length;
                                                                   Object.defineProperty(Proxy, "name", { value: "Proxy2", configurable: true });
                                                                   Object.defineProperty(Proxy, "length", { value: 3, configurable: true });

                                                                   nameDesc.value === "Proxy" &&
                                                                   nameDesc.writable === false &&
                                                                   nameDesc.enumerable === false &&
                                                                   nameDesc.configurable === true &&
                                                                   lengthDesc.value === 2 &&
                                                                   lengthDesc.writable === false &&
                                                                   lengthDesc.enumerable === false &&
                                                                   lengthDesc.configurable === true &&
                                                                   deletedName === true &&
                                                                   deletedLength === true &&
                                                                   Proxy.name === "Proxy2" &&
                                                                   Proxy.length === 3;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void BuiltIn_Constructors_Have_NonWritable_Prototype_When_Initialized()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var dataViewDesc = Object.getOwnPropertyDescriptor(DataView, "prototype");
                                                                   var errorDesc = Object.getOwnPropertyDescriptor(Error, "prototype");
                                                                   var asyncFunction = async function foo() {}.constructor;
                                                                   var asyncDesc = Object.getOwnPropertyDescriptor(asyncFunction, "prototype");
                                                                   dataViewDesc.writable === false &&
                                                                   dataViewDesc.enumerable === false &&
                                                                   dataViewDesc.configurable === false &&
                                                                   errorDesc.writable === false &&
                                                                   errorDesc.enumerable === false &&
                                                                   errorDesc.configurable === false &&
                                                                   asyncDesc.writable === false &&
                                                                   asyncDesc.enumerable === false &&
                                                                   asyncDesc.configurable === false;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Class_Prototype_Constructor_Points_To_Class_For_Derived_Builtins()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class MyArray extends Uint8Array {}
                                                                   const sample = new MyArray(1);
                                                                   sample.constructor === MyArray &&
                                                                   MyArray.prototype.constructor === MyArray;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void FunctionPrototype_Restricted_Accessors_Share_Realm_ThrowTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   var throwTypeError = Object.getOwnPropertyDescriptor((function() {
                                                                     "use strict";
                                                                     return arguments;
                                                                   }()), "callee").get;
                                                                   var callerDesc = Object.getOwnPropertyDescriptor(Function.prototype, "caller");
                                                                   var argumentsDesc = Object.getOwnPropertyDescriptor(Function.prototype, "arguments");

                                                                   callerDesc.get === throwTypeError &&
                                                                   callerDesc.set === throwTypeError &&
                                                                   argumentsDesc.get === throwTypeError &&
                                                                   argumentsDesc.set === throwTypeError &&
                                                                   callerDesc.configurable === true &&
                                                                   callerDesc.enumerable === false &&
                                                                   argumentsDesc.configurable === true &&
                                                                   argumentsDesc.enumerable === false;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }
}
