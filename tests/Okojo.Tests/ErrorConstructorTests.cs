using Okojo.Compiler;
using Okojo.Parsing;
using Okojo.Runtime;

namespace Okojo.Tests;

public class ErrorConstructorTests
{
    [Test]
    public void ErrorConstructor_CreatesObjectWithNameAndMessage()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let e = Error("boom");
                                                                   e.name + ":" + e.message;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("Error:boom"));
    }

    [Test]
    public void NotCallable_ThrowsJsRuntimeExceptionTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let x = 1;
                                                                   x();
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.DetailCode, Is.EqualTo("NOT_CALLABLE"));
    }

    [Test]
    public void TypeError_CaughtObject_HasNormalizedNameAndMessage()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let out = "";
                                                                   try {
                                                                       let x = 1;
                                                                       x();
                                                                   } catch (e) {
                                                                       out = e.name + ":" + e.message + "|" + e.toString();
                                                                   }
                                                                   out;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("TypeError:Not a function|TypeError: Not a function"));
    }

    [Test]
    public void ReferenceError_CaughtObject_HasNormalizedNameAndMessage()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let out = "";
                                                                   try {
                                                                       noSuchGlobal;
                                                                   } catch (e) {
                                                                       out = e.name + ":" + e.message + "|" + e.toString();
                                                                   }
                                                                   out;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.AsString(),
            Is.EqualTo("ReferenceError:noSuchGlobal is not defined|ReferenceError: noSuchGlobal is not defined"));
    }

    [Test]
    public void ReferenceError_CaughtObject_HasReferenceErrorConstructor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let ok = false;
                                                                   try {
                                                                       noSuchGlobal;
                                                                   } catch (e) {
                                                                       ok = (e.constructor === ReferenceError);
                                                                   }
                                                                   ok;
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void CaughtTypeError_IsInstanceOfTypeError_AndError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let out = 0;
                                                                   try {
                                                                       let x = 1;
                                                                       x();
                                                                   } catch (e) {
                                                                       if (e instanceof TypeError) out = out + 1;
                                                                       if (e instanceof Error) out = out + 10;
                                                                   }
                                                                   out;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(11));
    }

    [Test]
    public void InstanceOf_WithNonCallableRhs_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let o = {};
                                                                   o instanceof 1;
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.DetailCode, Is.EqualTo("INSTANCEOF_RHS_NOT_CALLABLE"));
    }

    [Test]
    public void TypeErrorConstructor_CreatesTypeErrorPrototypeInstance()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   let e = TypeError("boom");
                                                                   if (e instanceof TypeError) {
                                                                       if (e instanceof Error) 1;
                                                                       else 0;
                                                                   } else 0;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.Int32Value, Is.EqualTo(1));
    }

    [Test]
    public void InstanceOf_UsesSymbolHasInstance_WhenPresent()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const C = function() {};
                                                                   Object.defineProperty(C, Symbol.hasInstance, {
                                                                     value: function (v) { return v === 42; }
                                                                   });
                                                                   42 instanceof C;
                                                                   """));

        realm.Execute(script);

        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void InstanceOf_SymbolHasInstanceNonCallable_ThrowsTypeError()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const C = function() {};
                                                                   Object.defineProperty(C, Symbol.hasInstance, { value: 1 });
                                                                   42 instanceof C;
                                                                   """));

        var ex = Assert.Throws<JsRuntimeException>(() => realm.Execute(script));
        Assert.That(ex!.Kind, Is.EqualTo(JsErrorKind.TypeError));
        Assert.That(ex.DetailCode, Is.EqualTo("INSTANCEOF_HASINSTANCE_NOT_CALLABLE"));
    }

    [Test]
    public void SyntaxErrorConstructor_IsInstalledAndConstructable()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const e = SyntaxError("bad");
                                                                   (typeof SyntaxError === "function") &&
                                                                   (e instanceof SyntaxError) &&
                                                                   (e instanceof Error) &&
                                                                   (e.name === "SyntaxError") &&
                                                                   (e.message === "bad");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void URIErrorConstructor_IsInstalled_WithExpectedPrototypeSurface()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const ctorDesc = Object.getOwnPropertyDescriptor(URIError, "prototype");
                                                                   const proto = URIError.prototype;
                                                                   const ctorProp = Object.getOwnPropertyDescriptor(proto, "constructor");
                                                                   const nameProp = Object.getOwnPropertyDescriptor(proto, "name");
                                                                   const messageProp = Object.getOwnPropertyDescriptor(proto, "message");
                                                                   [
                                                                     typeof URIError === "function",
                                                                     ctorDesc.writable === false,
                                                                     ctorDesc.enumerable === false,
                                                                     ctorDesc.configurable === false,
                                                                     proto.constructor === URIError,
                                                                     ctorProp.writable === true,
                                                                     ctorProp.enumerable === false,
                                                                     ctorProp.configurable === true,
                                                                     nameProp.value === "URIError",
                                                                     nameProp.writable === true,
                                                                     nameProp.enumerable === false,
                                                                     nameProp.configurable === true,
                                                                     messageProp.value === "",
                                                                     messageProp.writable === true,
                                                                     messageProp.enumerable === false,
                                                                     messageProp.configurable === true
                                                                   ].every(Boolean);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void NativeErrorInstances_InheritName_AndOnlyOwnMessageWhenProvided()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const empty = new TypeError();
                                                                   const withMessage = new TypeError("boom");
                                                                   [
                                                                     empty.name === "TypeError",
                                                                     Object.prototype.hasOwnProperty.call(empty, "name") === false,
                                                                     Object.prototype.hasOwnProperty.call(empty, "message") === false,
                                                                     withMessage.name === "TypeError",
                                                                     Object.prototype.hasOwnProperty.call(withMessage, "name") === false,
                                                                     Object.prototype.hasOwnProperty.call(withMessage, "message") === true,
                                                                     withMessage.message === "boom"
                                                                   ].every(Boolean);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void Error_Subclass_Construction_Uses_Subclass_Prototype()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   class ReturnCalledError extends Error {}
                                                                   const e = new ReturnCalledError("boom");
                                                                   [
                                                                     e instanceof ReturnCalledError,
                                                                     e instanceof Error,
                                                                     e.constructor === ReturnCalledError,
                                                                     Object.getPrototypeOf(e) === ReturnCalledError.prototype
                                                                   ].join("|");
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.AsString(), Is.EqualTo("true|true|true|true"));
    }

    [Test]
    public void ErrorPrototypeToString_HasNonEnumerableWritableConfigurableDescriptor()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const desc = Object.getOwnPropertyDescriptor(Error.prototype, "toString");
                                                                   [
                                                                     typeof Error.prototype.toString === "function",
                                                                     desc.writable === true,
                                                                     desc.enumerable === false,
                                                                     desc.configurable === true
                                                                   ].every(Boolean);
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void ErrorPrototypeToString_Throws_On_NonObject_Receivers()
    {
        var realm = JsRuntime.Create().DefaultRealm;
        var compiler = new JsCompiler(realm);
        var script = compiler.Compile(JavaScriptParser.ParseScript("""
                                                                   const values = [undefined, null, 1, true, "string", Symbol("x")];
                                                                   values.every((value) => {
                                                                     try {
                                                                       Error.prototype.toString.call(value);
                                                                       return false;
                                                                     } catch (e) {
                                                                       return e instanceof TypeError;
                                                                     }
                                                                   });
                                                                   """));

        realm.Execute(script);
        Assert.That(realm.Accumulator.IsTrue, Is.True);
    }

    [Test]
    public void UpdateExpression_OnLiteral_IsEarlyParseError()
    {
        var ex = Assert.Throws<JsParseException>(() =>
            JavaScriptParser.ParseScript("0++;"));
        Assert.That(ex!.Message, Does.Contain("Invalid left-hand side expression in update operation"));
    }
}
